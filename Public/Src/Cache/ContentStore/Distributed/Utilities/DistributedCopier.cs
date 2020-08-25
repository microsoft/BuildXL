// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
    public class DistributedCopier : IAbsolutePathRemoteFileCopier
    {
        /// <inheritdoc />
        public Task<FileExistenceResult> CheckFileExistsAsync(AbsolutePath path, TimeSpan timeout, CancellationToken cancellationToken)
        {
            var resultCode = FileUtilities.Exists(path.Path) ? FileExistenceResult.ResultCode.FileExists : FileExistenceResult.ResultCode.FileNotFound;

            return Task.FromResult(new FileExistenceResult(resultCode));
        }

        /// <inheritdoc />
        public async Task<CopyFileResult> CopyToAsync(OperationContext context, AbsolutePath sourcePath, Stream destinationStream, long expectedContentSize, CopyToOptions options)
        {
            // NOTE: Assumes source is local
            Contract.Assert(sourcePath.IsLocal);

            if (!FileUtilities.Exists(sourcePath.Path))
            {
                return new CopyFileResult(CopyResultCode.FileNotFoundError, $"Source file {sourcePath} doesn't exist.");
            }

            long startPosition = destinationStream.Position;

            using (Stream s = FileUtilities.CreateAsyncFileStream(sourcePath.Path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                return await s.CopyToAsync(destinationStream, 81920, context.Token).ContinueWith(_ => CopyFileResult.SuccessWithSize(destinationStream.Position - startPosition));
            }
        }
    }
}
