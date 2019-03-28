// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Native.IO;

namespace BuildXL.Cache.ContentStore.Distributed.Utilities
{
    /// <summary>
    /// File copier to handle copying files between two distributed instances
    /// </summary>
    public class DistributedCopier : IAbsolutePathFileCopier
    {
        /// <inheritdoc />
        public Task<FileExistenceResult> CheckFileExistsAsync(AbsolutePath path, TimeSpan timeout, CancellationToken cancellationToken)
        {
            var resultCode = FileUtilities.Exists(path.Path) ? FileExistenceResult.ResultCode.FileExists : FileExistenceResult.ResultCode.FileNotFound;

            return Task.FromResult(new FileExistenceResult(resultCode));
        }

        /// <inheritdoc />
        public async Task<CopyFileResult> CopyFileAsync(AbsolutePath path, AbsolutePath destinationPath, long contentSize, bool overwrite, CancellationToken cancellationToken)
        {
            // NOTE: Assumes both source and destination are local
            Contract.Assert(path.IsLocal);
            Contract.Assert(destinationPath.IsLocal);

            if (!FileUtilities.Exists(path.Path))
            {
                return new CopyFileResult(CopyFileResult.ResultCode.FileNotFoundError, $"Source file {path} doesn't exist.");
            }

            if (FileUtilities.Exists(destinationPath.Path))
            {
                if (!overwrite)
                {
                    return new CopyFileResult(
                        CopyFileResult.ResultCode.DestinationPathError,
                        $"Destination file {destinationPath} exists but overwrite not specified.");
                }
            }

            if (!await FileUtilities.CopyFileAsync(path.Path, destinationPath.Path))
            {
                return new CopyFileResult(CopyFileResult.ResultCode.SourcePathError, $"Failed to copy {destinationPath} from {path}");
            }

            return CopyFileResult.SuccessWithSize(new System.IO.FileInfo(destinationPath.Path).Length);

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
                return await s.CopyToAsync(destinationStream, 81920, cancellationToken).ContinueWith((_) => CopyFileResult.SuccessWithSize(destinationStream.Position - startPosition));
            }
        }
    }
}
