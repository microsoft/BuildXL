// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Logging;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Utilities.ParallelAlgorithms;
using CLAP;
using System.Diagnostics.ContractsLight;
using System.Text.RegularExpressions;

namespace BuildXL.Cache.MultiTool.App
{
    internal sealed partial class Program
    {
        private static Regex CheckpointNameRegex { get; } = new Regex(".*[.]checkpointInfo[.]txt");

        [Verb(Description = "Downloads a specific checkpoint from a given stamp into the local filesystem", IsDefault = true)]
        public static int DownloadCheckpoint(
            [Required] string outputPath,
            [Required] string storageConnectionString,
            [DefaultValue(null)] string? checkpointId,
            [DefaultValue("checkpoints")] string containerName,
            [DefaultValue(8)] int downloadParallelism,
            [DefaultValue(false)] bool overwrite
            )
        {
            using var consoleLog = new ConsoleLog();
            using var logger = new Logger(new[] { consoleLog });

            var loggingContext = new Context(logger);
            var context = new OperationContext(loggingContext);

            var fileSystem = new PassThroughFileSystem(logger);

            try
            {
                var outputDirectoryPath = new AbsolutePath(outputPath);
                fileSystem.CreateDirectory(outputDirectoryPath);

                WithCentralStorage(
                    context,
                    storageConnectionString,
                    containerName,
                    centralStorage => DownloadNewestMatchingCheckpointAsync(context, fileSystem, centralStorage, outputDirectoryPath, downloadParallelism, checkpointId, overwrite))
                    .Result
                    .ThrowIfFailure();
                return 0;
            }
            catch (Exception e)
            {
                Tracer.Error(context, $"Application closing due to exception: {e}");
                return -1;
            }
        }

        [Verb(Description = "Lists all currently accessible checkpoints from a stamp")]
        public static int ListCheckpoints(
            [Required] string storageConnectionString,
            [DefaultValue("checkpoints")] string containerName)
        {
            using var consoleLog = new ConsoleLog();
            using var logger = new Logger(new[] { consoleLog });

            var loggingContext = new Context(logger);
            var context = new OperationContext(loggingContext);

            var fileSystem = new PassThroughFileSystem(logger);

            try
            {
                WithCentralStorage(
                    context,
                    storageConnectionString,
                    containerName,
                    centralStorage => PrintCheckpointsAsync(context, centralStorage))
                    .Result
                    .ThrowIfFailure();
                return 0;
            }
            catch (Exception e)
            {
                Tracer.Error(context, $"Application closing due to exception: {e}");
                return -1;
            }
        }

        private static Task<BoolResult> PrintCheckpointsAsync(OperationContext context, BlobCentralStorage centralStorage)
        {
            return context.PerformOperationAsync(Tracer, async () =>
            {
                await foreach (var entry in centralStorage.ListBlobsWithNameMatchingAsync(context, CheckpointNameRegex))
                {
                    Tracer.Always(context, $"StorageId=[{entry.StorageId}] CreationTimeUtc=[{entry.CreationTime}] LastAccessTimeUtc=[{entry.LastAccessTime}]");
                }

                return BoolResult.Success;
            });
        }

        private static Task<T> WithCentralStorage<T>(OperationContext context, string storageConnectionString, string containerName, Func<BlobCentralStorage, Task<T>> action)
            where T : ResultBase
        {
            return context.PerformOperationAsync(Tracer, async () =>
            {
                IReadOnlyList<AzureBlobStorageCredentials> credentials = new[] {
                    new AzureBlobStorageCredentials(storageConnectionString)
                };

                var centralStorage = new BlobCentralStorage(new BlobCentralStoreConfiguration(credentials, containerName, checkpointsKey: "useless")
                {
                    EnableGarbageCollect = false,
                });

                await centralStorage.StartupAsync(context).ThrowIfFailureAsync();

                try
                {
                    return await action(centralStorage);
                }
                finally
                {
                    await centralStorage.StartupAsync(context).ThrowIfFailureAsync();
                }
            });
        }

        private static Task<BoolResult> DownloadNewestMatchingCheckpointAsync(OperationContext context, PassThroughFileSystem fileSystem, BlobCentralStorage centralStorage, AbsolutePath outputDirectoryPath, int downloadParallelism, string? checkpointId = null, bool overwrite = false)
        {
            Contract.Requires(checkpointId == null || !string.IsNullOrEmpty(checkpointId));
            Contract.Requires(downloadParallelism > 0);

            return context.PerformOperationAsync(Tracer, async () =>
            {
                var checkpointInfoFilePath = outputDirectoryPath / "checkpointInfo.txt";

                if (overwrite || !fileSystem.FileExists(checkpointInfoFilePath))
                {
                    var regex = string.IsNullOrEmpty(checkpointId) ? CheckpointNameRegex : new Regex($".*?{checkpointId}.*[.]checkpointInfo[.]txt");
                    var latestCheckpoint = await centralStorage
                        .ListBlobsWithNameMatchingAsync(context, regex)
                        .OrderBy(entry => entry.CreationTime)
                        .LastAsync(context.Token);

                    await centralStorage.TryGetFileAsync(context, latestCheckpoint.StorageId, checkpointInfoFilePath).ThrowIfFailureAsync();
                }

                return await DownloadCheckpointFromFileAsync(context, fileSystem, outputDirectoryPath, checkpointInfoFilePath, centralStorage, downloadParallelism);
            });
        }

        private static Task<BoolResult> DownloadCheckpointFromFileAsync(OperationContext context, PassThroughFileSystem fileSystem, AbsolutePath outputDirectoryPath, AbsolutePath checkpointInfoFilePath, BlobCentralStorage centralStorage, int downloadParallelism)
        {
            Contract.Requires(downloadParallelism > 0);

            return context.PerformOperationAsync(Tracer, async () =>
            {
                if (!fileSystem.FileExists(checkpointInfoFilePath))
                {
                    return new BoolResult($"Checkpoint info file at `{checkpointInfoFilePath}` does not exist");
                }
                var checkpointInfo = CheckpointManager.ParseCheckpointInfo(checkpointInfoFilePath);
                await DownloadCheckpointAsync(context, fileSystem, centralStorage, checkpointInfo, outputDirectoryPath, downloadParallelism);

                return BoolResult.Success;
            });
        }

        private static async Task DownloadCheckpointAsync(OperationContext context, PassThroughFileSystem fileSystem, BlobCentralStorage centralStorage, Dictionary<string, string> checkpointInfo, AbsolutePath outputDirectoryPath, int downloadParallelism)
        {
            fileSystem.CreateDirectory(outputDirectoryPath);

            await ParallelAlgorithms.WhenDoneAsync(
                downloadParallelism,
                context.Token,
                action: async (addItem, kvp) =>
                {
                    var fileName = kvp.Key;
                    var storageId = kvp.Value;
                    var outputFilePath = outputDirectoryPath / fileName;

                    await FetchFileAsync(context, fileSystem, centralStorage, storageId, outputFilePath).ThrowIfFailureAsync();
                },
                items: checkpointInfo.ToArray());
            context.Token.ThrowIfCancellationRequested();
        }

        private static Task<BoolResult> FetchFileAsync(OperationContext context, IAbsFileSystem fileSystem, BlobCentralStorage centralStorage, string storageId, AbsolutePath outputFilePath)
        {
            return context.PerformOperationAsync(Tracer, async () =>
            {
                var expectedContentHash = new ContentHash(storageId.Split("||DCS||")[0]);

                var performDownload = true;
                if (fileSystem.FileExists(outputFilePath))
                {
                    performDownload = !(await HashMatchesAsync(fileSystem, expectedContentHash, outputFilePath));
                    if (performDownload)
                    {
                        fileSystem.DeleteFile(outputFilePath);
                    }
                }

                if (performDownload)
                {
                    await centralStorage.TryGetFileAsync(context, storageId, outputFilePath).ThrowIfFailureAsync();
                }

                return BoolResult.Success;
            },
            extraStartMessage: $"StorageId=[{storageId}] FilePath=[{outputFilePath}]",
            extraEndMessage: r => $"StorageId=[{storageId}] FilePath=[{outputFilePath}]",
            traceErrorsOnly: true);
        }

        private static async Task<bool> HashMatchesAsync(IAbsFileSystem fileSystem, ContentHash expectedContentHash, AbsolutePath outputFilePath)
        {
            var hashInfo = HashInfoLookup.Find(expectedContentHash.HashType);
            using var hasher = hashInfo.CreateContentHasher();
            using var stream = await fileSystem.OpenReadOnlySafeAsync(outputFilePath, FileShare.Read);

            var contentHash = await hasher.GetContentHashAsync(stream);
            return contentHash.Equals(expectedContentHash);
        }
    }
}
