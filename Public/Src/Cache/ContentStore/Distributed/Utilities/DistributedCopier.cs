// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Native.IO;

namespace BuildXL.Cache.ContentStore.Distributed.Utilities
{
    /// <summary>
    /// File copier to handle copying files between two distributed instances
    /// </summary>
    public class DistributedCopier : IRemoteFileCopier
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

            return new MachineLocation(cacheRoot.Path.ToUpperInvariant());
        }

        /// <inheritdoc />
        public virtual async Task<CopyFileResult> CopyToAsync(OperationContext context, ContentLocation sourceLocation, Stream destinationStream, CopyOptions options)
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
