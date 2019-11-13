// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Native.IO;

namespace BuildXL.Cache.ContentStore.Distributed.Utilities
{
    /// <summary>
    /// File copier to handle copying files between two distributed instances
    /// </summary>
    public class DistributedCopier : ITraceableAbsolutePathFileCopier
    {
        /// <inheritdoc />
        public Task<FileExistenceResult> CheckFileExistsAsync(AbsolutePath path, TimeSpan timeout, CancellationToken cancellationToken)
        {
            var resultCode = FileUtilities.Exists(path.Path) ? FileExistenceResult.ResultCode.FileExists : FileExistenceResult.ResultCode.FileNotFound;

            return Task.FromResult(new FileExistenceResult(resultCode));
        }

        /// <inheritdoc />
        public Task<CopyFileResult> CopyToAsync(OperationContext context, AbsolutePath sourcePath, Stream destinationStream, long expectedContentSize)
        {
            return CopyToAsync(sourcePath, destinationStream, expectedContentSize, context.Token);
        }

        /// <inheritdoc />
        public async Task<CopyFileResult> CopyToAsync(AbsolutePath sourcePath, Stream destinationStream, long expectedContentSize, CancellationToken cancellationToken)
        {
            // NOTE: Assumes source is local
            Contract.Assert(sourcePath.IsLocal);

            if (!FileUtilities.Exists(sourcePath.Path))
            {
                return new CopyFileResult(CopyFileResult.ResultCode.SourcePathError, $"Source file {sourcePath} doesn't exist.");
            }

            long startPosition = destinationStream.Position;

            using (Stream s = FileUtilities.CreateAsyncFileStream(sourcePath.Path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                return await s.CopyToAsync(destinationStream, 81920, cancellationToken).ContinueWith(_ => CopyFileResult.SuccessWithSize(destinationStream.Position - startPosition));
            }
        }
    }
}
