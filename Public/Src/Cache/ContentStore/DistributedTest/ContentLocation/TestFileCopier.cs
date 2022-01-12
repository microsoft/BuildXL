// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Utilities.Tasks;
using ContentStoreTest.Test;

namespace ContentStoreTest.Distributed.ContentLocation
{
    using ContentLocation = BuildXL.Cache.ContentStore.Distributed.ContentLocation;

    public class TestFileCopier : IRemoteFileCopier, IContentCommunicationManager
    {
        public AbsolutePath WorkingDirectory { get; set; }

        public ConcurrentDictionary<AbsolutePath, AbsolutePath> FilesCopied { get; } = new ConcurrentDictionary<AbsolutePath, AbsolutePath>();

        public ConcurrentDictionary<AbsolutePath, bool> FilesToCorrupt { get; } = new ConcurrentDictionary<AbsolutePath, bool>();

        public Dictionary<MachineLocation, ICopyRequestHandler> CopyHandlersByLocation { get; } = new();

        public Dictionary<MachineLocation, IPushFileHandler> PushHandlersByLocation { get; } = new();

        public Dictionary<MachineLocation, IDeleteFileHandler> DeleteHandlersByLocation { get; } = new();

        public int FilesCopyAttemptCount => FilesCopied.Count;

        public TimeSpan? CopyDelay;
        public Task<CopyFileResult> CopyToAsyncTask;

        private readonly IAbsFileSystem _fileSystem;

        public TestFileCopier(IAbsFileSystem fileSystem = null)
        {
            _fileSystem = fileSystem ?? new PassThroughFileSystem();
        }

        public MachineLocation GetLocalMachineLocation(AbsolutePath cacheRoot)
        {
            return new MachineLocation(cacheRoot.Path);
        }

        public Task<CopyFileResult> CopyToAsync(OperationContext context, ContentLocation sourceLocation, Stream destinationStream, CopyOptions options)
        {
            var sourcePath = PathUtilities.GetContentPath(sourceLocation.Machine.Path, sourceLocation.Hash);

            var result = CopyToAsyncCore(context, sourcePath, destinationStream, options);
            CopyToAsyncTask = result;
            return result;
        }

        private async Task<CopyFileResult> CopyToAsyncCore(OperationContext context, AbsolutePath sourcePath, Stream destinationStream, CopyOptions options)
        {
            try
            {
                if (CopyDelay != null)
                {
                    await Task.Delay(CopyDelay.Value);
                }

                long startPosition = destinationStream.Position;

                FilesCopied.AddOrUpdate(sourcePath, p => sourcePath, (dest, prevPath) => prevPath);

                if (!_fileSystem.FileExists(sourcePath))
                {
                    return new CopyFileResult(CopyResultCode.FileNotFoundError, $"Source file {sourcePath} doesn't exist.");
                }

                using Stream s = GetStream(sourcePath);

                await s.CopyToAsync(destinationStream);

                return CopyFileResult.SuccessWithSize(destinationStream.Position - startPosition);
            }
            catch (Exception e)
            {
                return new CopyFileResult(CopyResultCode.DestinationPathError, e);
            }
        }

        private Stream GetStream(AbsolutePath sourcePath)
        {
            Stream s;
            if (FilesToCorrupt.ContainsKey(sourcePath))
            {
                TestGlobal.Logger.Debug($"Corrupting file {sourcePath}");
                s = new MemoryStream(ThreadSafeRandom.GetBytes(100));
            }
            else
            {
                s =  _fileSystem.OpenReadOnly(sourcePath, FileShare.Read);
            }

            return s;
        }
        
        public Task<BoolResult> RequestCopyFileAsync(OperationContext context, ContentHash hash, MachineLocation targetMachine)
        {
            return UseAsync(CopyHandlersByLocation, targetMachine, h => h.HandleCopyFileRequestAsync(context, hash, CancellationToken.None));
        }

        public async Task<DeleteResult> DeleteFileAsync(OperationContext context, ContentHash hash, MachineLocation targetMachine)
        {
            var result = await UseAsync(DeleteHandlersByLocation, targetMachine,
                h => h.HandleDeleteAsync(context, hash, new DeleteContentOptions() {DeleteLocalOnly = true}));
            return result;
        }

        public virtual async Task<PushFileResult> PushFileAsync(OperationContext context, ContentHash hash, Stream stream, MachineLocation targetMachine, CopyOptions options)
        {
            var result = await UseAsync(PushHandlersByLocation, targetMachine, h => h.HandlePushFileAsync(context, hash, new FileSource(stream), CancellationToken.None));

            return result ? PushFileResult.PushSucceeded(result.ContentSize) : new PushFileResult(result);
        }

        private Task<TResult> UseAsync<T, TResult>(Dictionary<MachineLocation, T> map, MachineLocation location, Func<T, Task<TResult>> action)
        {
            var instance = map[location];
            return action(instance);
        }
    }
}
