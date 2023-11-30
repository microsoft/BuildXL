// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Native.IO;

namespace BuildXL.Cache.ContentStore.Distributed.Utilities
{
    /// <summary>
    /// File copier to handle copying files between two distributed instances. This is only used in tests. Be aware 
    /// that we have tests that run an app, so this class is also used in our main executables.
    /// </summary>
    public sealed class MockRemoteFileCopier : IRemoteFileCopier
    {
        /// <inheritdoc />
        public MachineLocation GetLocalMachineLocation(AbsolutePath cacheRoot)
        {
            if (!cacheRoot.IsLocal)
            {
                throw new ArgumentException($"Local cache root must be a local path. Found {cacheRoot}.");
            }

            if (!cacheRoot.GetFileName().Equals(Constants.SharedDirectoryName))
            {
                cacheRoot = cacheRoot / Constants.SharedDirectoryName;
            }

            return MachineLocation.Parse(cacheRoot.Path.ToUpperInvariant());
        }

        /// <inheritdoc />
        public async Task<CopyFileResult> CopyToAsync(OperationContext context, ContentLocation sourceLocation, Stream destinationStream, CopyOptions options)
        {
            var sourcePath = new AbsolutePath(sourceLocation.Machine.Path) / FileSystemContentStoreInternal.GetPrimaryRelativePath(sourceLocation.Hash, includeSharedFolder: false);

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
