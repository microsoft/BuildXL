// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using ContentStoreTest.Test;

namespace ContentStoreTest.Distributed.ContentLocation
{
    public class TestFileCopier : IFileCopier<AbsolutePath>, IFileExistenceChecker<AbsolutePath>, ICopyRequester
    {
        public ConcurrentDictionary<AbsolutePath, AbsolutePath> FilesCopied { get; } = new ConcurrentDictionary<AbsolutePath, AbsolutePath>();

        public ConcurrentDictionary<AbsolutePath, bool> FilesToCorrupt { get; } = new ConcurrentDictionary<AbsolutePath, bool>();

        public ConcurrentDictionary<AbsolutePath, ConcurrentQueue<FileExistenceResult.ResultCode>> FileExistenceByReturnCode { get; } = new ConcurrentDictionary<AbsolutePath, ConcurrentQueue<FileExistenceResult.ResultCode>>();

        public ConcurrentDictionary<AbsolutePath, ConcurrentQueue<TimeSpan>> FileExistenceTimespans { get; } = new ConcurrentDictionary<AbsolutePath, ConcurrentQueue<TimeSpan>>();

        public Dictionary<MachineLocation, ICopyRequestHandler> CopyHandlersByLocation { get; } = new Dictionary<MachineLocation, ICopyRequestHandler>();

        public int FilesCopyAttemptCount => FilesCopied.Count;

        public async Task<CopyFileResult> CopyFileAsync(AbsolutePath path, AbsolutePath destinationPath, long contentSize, bool overwrite, CancellationToken cancellationToken)
        {
            FilesCopied.AddOrUpdate(destinationPath, p => path, (dest, prevPath) => overwrite ? path : prevPath);

            if (!File.Exists(path.Path))
            {
                return new CopyFileResult(CopyFileResult.ResultCode.SourcePathError, $"Source file {path} doesn't exist.");
            }

            if (File.Exists(destinationPath.Path))
            {
                if (!overwrite)
                {
                    return new CopyFileResult(
                        CopyFileResult.ResultCode.DestinationPathError,
                        $"Destination file {destinationPath} exists but overwrite not specified.");
                }
            }

            if (FilesToCorrupt.ContainsKey(path))
            {
                TestGlobal.Logger.Debug($"Corrupting file {path}");
#pragma warning disable AsyncFixer02 // WriteAllBytesAsync should be used instead of File.WriteAllBytes
                await Task.Run(
                    () => File.WriteAllBytes(destinationPath.Path, ThreadSafeRandom.GetBytes(150)));
#pragma warning restore AsyncFixer02 // WriteAllBytesAsync should be used instead of File.WriteAllBytes
            }
            else
            {
                await Task.Run(() => File.Copy(path.Path, destinationPath.Path), cancellationToken);
            }

            return CopyFileResult.SuccessWithSize(new System.IO.FileInfo(destinationPath.Path).Length);
        }

        public async Task<CopyFileResult> CopyToAsync(AbsolutePath sourcePath, Stream destinationStream, long expectedContentSize, CancellationToken cancellationToken)
        {
            long startPosition = destinationStream.Position;

            FilesCopied.AddOrUpdate(sourcePath, p => sourcePath, (dest, prevPath) => prevPath);

            if (!File.Exists(sourcePath.Path))
            {
                return new CopyFileResult(CopyFileResult.ResultCode.SourcePathError, $"Source file {sourcePath} doesn't exist.");
            }

            Stream s;
            if (FilesToCorrupt.ContainsKey(sourcePath))
            {
                TestGlobal.Logger.Debug($"Corrupting file {sourcePath}");
                s = new MemoryStream(ThreadSafeRandom.GetBytes((int)expectedContentSize));
            }
            else
            {
                s = File.OpenRead(sourcePath.Path);
            }

            return await s.CopyToAsync(destinationStream).ContinueWith((_) => CopyFileResult.SuccessWithSize(destinationStream.Position - startPosition));
        }

        public Task<FileExistenceResult> CheckFileExistsAsync(AbsolutePath path, TimeSpan timeout, CancellationToken cancellationToken)
        {
            FileExistenceTimespans.AddOrUpdate(
                path,
                _ =>
                {
                    var queue = new ConcurrentQueue<TimeSpan>();
                    queue.Enqueue(timeout);
                    return queue;
                },
                (_, queue) =>
                {
                    queue.Enqueue(timeout);
                    return queue;
                });

            if (FileExistenceByReturnCode.TryGetValue(path, out var resultQueue) && resultQueue.TryDequeue(out var result))
            {
                return Task.FromResult(new FileExistenceResult(result));
            }

            if (File.Exists(path.Path))
            {
                return Task.FromResult(new FileExistenceResult(FileExistenceResult.ResultCode.FileExists));
            }

            return Task.FromResult(new FileExistenceResult(FileExistenceResult.ResultCode.Error));
        }

        public void SetNextFileExistenceResult(AbsolutePath path, FileExistenceResult.ResultCode result)
        {
            FileExistenceByReturnCode[path] = new ConcurrentQueue<FileExistenceResult.ResultCode>(new[] { result });
        }

        public int GetExistenceCheckCount(AbsolutePath path)
        {
            if (FileExistenceTimespans.TryGetValue(path, out var existenceCheckTimespans))
            {
                return existenceCheckTimespans.Count;
            }

            return 0;
        }

        public Task<BoolResult> RequestCopyFileAsync(Context context, ContentHash hash, MachineLocation targetMachine)
        {
            return CopyHandlersByLocation[targetMachine].HandleCopyFileRequestAsync(context, hash);
        }
    }
}
