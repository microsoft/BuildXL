// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Tracing.Internal;

namespace BuildXL.Cache.ContentStore.Distributed.Utilities
{
    /// <summary>
    /// File copier which operates over Grpc. <seealso cref="GrpcCopyClient"/>
    /// </summary>
    public class GrpcFileCopier : IAbsolutePathRemoteFileCopier, IContentCommunicationManager, IDisposable
    {
        private readonly Context _context;
        private readonly GrpcFileCopierConfiguration _configuration;

        private readonly GrpcCopyClientCache _clientCache;

        /// <summary>
        /// Extract the host name from an AbsolutePath's segments.
        /// </summary>
        public static string GetHostName(bool isLocal, IReadOnlyList<string> segments)
        {
            if (OperatingSystemHelper.IsWindowsOS)
            {
                return isLocal ? "localhost" : segments.First();
            }
            else
            {
                // Linux always uses the first segment as the host name.
                return segments.First();
            }
        }

        /// <summary>
        /// Constructor for <see cref="GrpcFileCopier"/>.
        /// </summary>
        public GrpcFileCopier(Context context, GrpcFileCopierConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
            _clientCache = new GrpcCopyClientCache(context, _configuration.GrpcCopyClientCacheConfiguration);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _clientCache.Dispose();
        }

        /// <inheritdoc />
        public Task<FileExistenceResult> CheckFileExistsAsync(AbsolutePath path, TimeSpan timeout, CancellationToken cancellationToken)
        {
            // Extract host and contentHash from sourcePath
            (string host, ContentHash contentHash) = ExtractHostHashFromAbsolutePath(path);

            var context = new OperationContext(_context, cancellationToken);
            return _clientCache.UseAsync(context, host, _configuration.GrpcPort, (nestedContext, client) => client.CheckFileExistsAsync(nestedContext, contentHash));
        }

        private (string host, ContentHash contentHash) ExtractHostHashFromAbsolutePath(AbsolutePath sourcePath)
        {
            // TODO: Keep the segments in the AbsolutePath object?
            // TODO: Indexable structure?
            var segments = sourcePath.GetSegments();
            Contract.Assert(segments.Count >= 4);

            string host = GetHostName(sourcePath.IsLocal, segments);

            var hashLiteral = segments.Last();
            if (hashLiteral.EndsWith(GrpcDistributedPathTransformer.BlobFileExtension, StringComparison.OrdinalIgnoreCase))
            {
                hashLiteral = hashLiteral.Substring(0, hashLiteral.Length - GrpcDistributedPathTransformer.BlobFileExtension.Length);
            }
            var hashTypeLiteral = segments.ElementAt(segments.Count - 1 - 2);

            if (!Enum.TryParse(hashTypeLiteral, ignoreCase: true, out HashType hashType))
            {
                throw new InvalidOperationException($"{hashTypeLiteral} is not a valid member of {nameof(HashType)}");
            }

            var contentHash = new ContentHash(hashType, HexUtilities.HexToBytes(hashLiteral));

            return (host, contentHash);
        }

        /// <inheritdoc />
        public async Task<CopyFileResult> CopyToAsync(OperationContext context, AbsolutePath sourcePath, Stream destinationStream, long expectedContentSize,
            CopyToOptions options)
        {
            // Extract host and contentHash from sourcePath
            (string host, ContentHash contentHash) = ExtractHostHashFromAbsolutePath(sourcePath);

            // Contact hard-coded port on source
            try
            {
                // ResourcePoolV2 may throw TimeoutException if the connection fails.
                // Wrapping this error and converting it to an "error code".

                return await _clientCache.UseWithInvalidationAsync(context, host, _configuration.GrpcPort, async (nestedContext, clientWrapper) =>
                {
                    var result = await clientWrapper.Value.CopyToAsync(nestedContext, contentHash, destinationStream, options);
                    InvalidateResourceIfNeeded(nestedContext, options, result, clientWrapper);
                    return result;
                });
            }
            catch (ResultPropagationException e)
            {
                if (e.Result.Exception != null)
                {
                    return GrpcCopyClient.CreateResultFromException(e.Result.Exception);
                }

                return new CopyFileResult(CopyResultCode.Unknown, e.Result);
            }
            catch (Exception e)
            {
                return new CopyFileResult(CopyResultCode.Unknown, e);
            }
        }

        private void InvalidateResourceIfNeeded(Context context, CopyToOptions options, CopyFileResult result, IResourceWrapperAdapter<GrpcCopyClient> clientWrapper)
        {
            if (!result)
            {
                switch (_configuration.GrpcCopyClientInvalidationPolicy)
                {
                    case GrpcFileCopierConfiguration.ClientInvalidationPolicy.Disabled:
                        break;
                    case GrpcFileCopierConfiguration.ClientInvalidationPolicy.OnEveryError:
                        clientWrapper.Invalidate(context);
                        break;
                    case GrpcFileCopierConfiguration.ClientInvalidationPolicy.OnConnectivityErrors:
                        if ((result.Code == CopyResultCode.CopyBandwidthTimeoutError && options.TotalBytesCopied == 0) ||
                            result.Code == CopyResultCode.ConnectionTimeoutError)
                        {
                            if (options?.BandwidthConfiguration?.InvalidateOnTimeoutError ?? true)
                            {
                                clientWrapper.Invalidate(context);
                            }
                        }

                        break;
                }
            }
        }

        /// <inheritdoc />
        public Task<PushFileResult> PushFileAsync(OperationContext context, ContentHash hash, Stream stream, MachineLocation targetMachine)
        {
            var targetPath = new AbsolutePath(targetMachine.Path);
            var targetMachineName = targetPath.IsLocal ? "localhost" : targetPath.GetSegments()[0];

            return _clientCache.UseAsync(context, targetMachineName, _configuration.GrpcPort, (nestedContext, client) => client.PushFileAsync(nestedContext, hash, stream));
        }

        /// <inheritdoc />
        public Task<BoolResult> RequestCopyFileAsync(OperationContext context, ContentHash hash, MachineLocation targetMachine)
        {
            var targetPath = new AbsolutePath(targetMachine.Path);
            var targetMachineName = targetPath.IsLocal ? "localhost" : targetPath.GetSegments()[0];

            return _clientCache.UseAsync(context, targetMachineName, _configuration.GrpcPort, (nestedContext, client) => client.RequestCopyFileAsync(nestedContext, hash));
        }

        /// <inheritdoc />
        public async Task<DeleteResult> DeleteFileAsync(OperationContext context, ContentHash hash, MachineLocation targetMachine)
        {
            var targetPath = new AbsolutePath(targetMachine.Path);
            var targetMachineName = targetPath.IsLocal ? "localhost" : targetPath.GetSegments()[0];

            using (var client = new GrpcContentClient(
                new ServiceClientContentSessionTracer(nameof(ServiceClientContentSessionTracer)),
                new PassThroughFileSystem(),
                new ServiceClientRpcConfiguration(_configuration.GrpcPort) { GrpcHost = targetMachineName },
                scenario: string.Empty))
            {
                return await client.DeleteContentAsync(context, hash, deleteLocalOnly: true);
            }
        }
    }
}
