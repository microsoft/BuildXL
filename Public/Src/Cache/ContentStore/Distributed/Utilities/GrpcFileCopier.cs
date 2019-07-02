// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Cache.ContentStore.Service.Grpc;

namespace BuildXL.Cache.ContentStore.Distributed.Utilities
{
    /// <summary>
    /// File copier which operates over Grpc. <seealso cref="GrpcCopyClient"/>, <seealso cref="GrpcServerFactory"/>
    /// </summary>
    public class GrpcFileCopier : IAbsolutePathFileCopier
    {
        private const int DefaultGrpcPort = 7089;
        private readonly Context _context;
        private int _grpcPort;
        private bool _useCompression;

        private GrpcCopyClientCache _clientCache;

        /// <summary>
        /// Constructor for <see cref="GrpcFileCopier"/>.
        /// </summary>
        public GrpcFileCopier(Context context, int grpcPort, int maxGrpcClientCount, int maxGrpcClientAgeMinutes, int grpcClientCleanupDelayMinutes, bool useCompression = false, int? bufferSize = null)
        {
            _context = context;
            _grpcPort = grpcPort;
            _useCompression = useCompression;

            _clientCache = new GrpcCopyClientCache(context, maxGrpcClientCount, maxGrpcClientAgeMinutes, grpcClientCleanupDelayMinutes, bufferSize: bufferSize);
        }

        /// <inheritdoc />
        public async Task<FileExistenceResult> CheckFileExistsAsync(AbsolutePath path, TimeSpan timeout, CancellationToken cancellationToken)
        {
            // Extract host and contentHash from sourcePath
            (string host, ContentHash contentHash) = ExtractHostHashFromAbsolutePath(path);

            FileExistenceResult fileExistenceResult = null;
            using (var clientWrapper = await _clientCache.CreateAsync(host, _grpcPort, _useCompression))
            {
                fileExistenceResult = await clientWrapper.Value.CheckFileExistsAsync(_context, contentHash);
            }

            return fileExistenceResult;
        }

        /// <inheritdoc />
        public async Task<CopyFileResult> CopyFileAsync(AbsolutePath sourcePath, AbsolutePath destinationPath, long contentSize, bool overwrite, CancellationToken cancellationToken)
        {
            // Extract host and contentHash from sourcePath
            (string host, ContentHash contentHash) = ExtractHostHashFromAbsolutePath(sourcePath);

            CopyFileResult copyFileResult = null;
            // Contact hard-coded port on source
            using (var clientWrapper = await _clientCache.CreateAsync(host, _grpcPort, _useCompression))
            {
                copyFileResult = await clientWrapper.Value.CopyFileAsync(_context, contentHash, destinationPath, cancellationToken);
            }

            return copyFileResult;
        }

        private (string host, ContentHash contentHash) ExtractHostHashFromAbsolutePath(AbsolutePath sourcePath)
        {
            // TODO: Keep the segments in the AbsolutePath object?
            // TODO: Indexable structure?
            var segments = sourcePath.GetSegments();
            Contract.Assert(segments.Count >= 4);

            var host = segments.First();
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
        public async Task<CopyFileResult> CopyToAsync(AbsolutePath sourcePath, Stream destinationStream, long expectedContentSize, CancellationToken cancellationToken)
        {
            // Extract host and contentHash from sourcePath
            (string host, ContentHash contentHash) = ExtractHostHashFromAbsolutePath(sourcePath);

            CopyFileResult copyFileResult = null;
            // Contact hard-coded port on source
            using (var clientWrapper = await _clientCache.CreateAsync(host, _grpcPort, _useCompression))
            {
                copyFileResult = await clientWrapper.Value.CopyToAsync(_context, contentHash, destinationStream, cancellationToken);
            }

            return copyFileResult;
        }
    }
}
