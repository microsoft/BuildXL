// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Reflection;
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
        private readonly int _grpcPort;
        private readonly bool _useCompression;

        private readonly GrpcCopyClientCache _clientCache;

        private readonly bool _invalidateGrpcClientsOnCopyFailures;

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

        /// <nodoc />
        public GrpcFileCopier(Context context, int grpcPort, int maxGrpcClientCount, int maxGrpcClientAgeMinutes, bool useCompression = false, int? bufferSize = null, int? copyTimeoutInSeconds = null)
            : this(context, grpcPort, useCompression, new GrpcCopyClientCacheConfiguration()
            {
                MaxClientCount = maxGrpcClientCount,
                MaxClientAgeMinutes = maxGrpcClientAgeMinutes,
                BufferSize = bufferSize,
                CopyTimeoutSeconds = copyTimeoutInSeconds
            })
        {
        }

        /// <nodoc />
        public GrpcFileCopier(Context context, int grpcPort, bool useCompression = false, GrpcCopyClientCacheConfiguration grpcCopyClientCacheConfiguration = null, bool invalidateGrpcClientsOnCopyFailures = false)
        {
            _context = context;
            _grpcPort = grpcPort;
            _useCompression = useCompression;

            _invalidateGrpcClientsOnCopyFailures = invalidateGrpcClientsOnCopyFailures;
            _clientCache = new GrpcCopyClientCache(context, grpcCopyClientCacheConfiguration);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _clientCache.Dispose();
        }

        /// <inheritdoc />
        public async Task<FileExistenceResult> CheckFileExistsAsync(AbsolutePath path, TimeSpan timeout, CancellationToken cancellationToken)
        {
            // Extract host and contentHash from sourcePath
            (string host, ContentHash contentHash) = ExtractHostHashFromAbsolutePath(path);

            using var clientWrapper = await _clientCache.CreateAsync(host, _grpcPort, _useCompression);
            return await clientWrapper.Value.CheckFileExistsAsync(_context, contentHash);
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
            using var clientWrapper = await _clientCache.CreateAsync(host, _grpcPort, _useCompression);
            var result = await clientWrapper.Value.CopyToAsync(context, contentHash, destinationStream, options, context.Token);
            if (!result)
            {
                if (_invalidateGrpcClientsOnCopyFailures)
                {
                    clientWrapper.Invalidate();
                }
                else
                {
                    if ((result.Code == CopyResultCode.CopyBandwidthTimeoutError && options.TotalBytesCopied == 0) || result.Code == CopyResultCode.ConnectionTimeoutError)
                    {
                        clientWrapper.Invalidate();
                    }
                }
            }

            return result;
        }

        /// <inheritdoc />
        public async Task<PushFileResult> PushFileAsync(OperationContext context, ContentHash hash, Stream stream, MachineLocation targetMachine)
        {
            var targetPath = new AbsolutePath(targetMachine.Path);
            var targetMachineName = targetPath.IsLocal ? "localhost" : targetPath.GetSegments()[0];

            using var clientWrapper = await _clientCache.CreateAsync(targetMachineName, _grpcPort, _useCompression);
            return await clientWrapper.Value.PushFileAsync(context, hash, stream);
        }

        /// <inheritdoc />
        public async Task<BoolResult> RequestCopyFileAsync(OperationContext context, ContentHash hash, MachineLocation targetMachine)
        {
            var targetPath = new AbsolutePath(targetMachine.Path);
            var targetMachineName = targetPath.IsLocal ? "localhost" : targetPath.GetSegments()[0];

            using var clientWrapper = await _clientCache.CreateAsync(targetMachineName, _grpcPort, _useCompression);
            return await clientWrapper.Value.RequestCopyFileAsync(context, hash);
        }

        /// <inheritdoc />
        public async Task<DeleteResult> DeleteFileAsync(OperationContext context, ContentHash hash, MachineLocation targetMachine)
        {
            var targetPath = new AbsolutePath(targetMachine.Path);
            var targetMachineName = targetPath.IsLocal ? "localhost" : targetPath.GetSegments()[0];

            using (var client = new GrpcContentClient(
                new ServiceClientContentSessionTracer(nameof(ServiceClientContentSessionTracer)),
                new PassThroughFileSystem(),
                new ServiceClientRpcConfiguration(_grpcPort) { GrpcHost = targetMachineName },
                scenario: string.Empty))
            {
                return await client.DeleteContentAsync(context, hash, deleteLocalOnly: true);
            }
        }
    }
}
