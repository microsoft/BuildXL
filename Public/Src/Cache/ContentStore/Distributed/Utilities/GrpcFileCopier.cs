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
        private Context _context;
        private int _grpcPort;

        /// <summary>
        /// Constructor for <see cref="GrpcFileCopier"/>.
        /// </summary>
        public GrpcFileCopier(Context context, int grpcPort)
        {
            _context = context;
            _grpcPort = grpcPort;
        }

        /// <inheritdoc />
        public Task<FileExistenceResult> CheckFileExistsAsync(AbsolutePath path, TimeSpan timeout, CancellationToken cancellationToken)
        {
            // TODO: Implement!
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public async Task<CopyFileResult> CopyFileAsync(AbsolutePath sourcePath, AbsolutePath destinationPath, long contentSize, bool overwrite, CancellationToken cancellationToken)
        {
            // Extract host and contentHash from sourcePath
            string host = ExtractHostFromAbsolutePath(sourcePath);

            CopyFileResult copyFileResult = null;
            // Contact hard-coded port on source
            using (var client = GrpcCopyClient.Create(host, DefaultGrpcPort))
            {
                copyFileResult = await client.CopyFileAsync(_context, sourcePath, destinationPath, cancellationToken);
            }

            return copyFileResult;
        }

        private string ExtractHostFromAbsolutePath(AbsolutePath sourcePath)
        {
            Contract.Assert(sourcePath.IsUnc);
            var segments = sourcePath.GetSegments();
            return segments.First();
        }

        /// <inheritdoc />
        public async Task<CopyFileResult> CopyToAsync(AbsolutePath sourcePath, Stream destinationStream, long expectedContentSize, CancellationToken cancellationToken)
        {
            // Extract host and contentHash from sourcePath
            string host = ExtractHostFromAbsolutePath(sourcePath);

            CopyFileResult copyFileResult = null;
            // Contact hard-coded port on source
            using (var client = GrpcCopyClient.Create(host, DefaultGrpcPort))
            {
                copyFileResult = await client.CopyToAsync(_context, sourcePath, destinationStream, cancellationToken);
            }

            return copyFileResult;
        }
    }
}
