// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Tracing;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using OperationContext = BuildXL.Cache.ContentStore.Tracing.Internal.OperationContext;

#nullable enable

namespace BuildXL.Cache.Logging
{
    /// <summary>
    ///     Extends NLog to write logs into Azure Blob Storage
    /// </summary>
    /// <remarks>
    ///     This is intended for Kusto ingestion via Azure Event Grid. When the log file gets uploaded into Azure
    ///     Storage, an Event Grid notification is sent over to Kusto, making it available for ingestion. Kusto will
    ///     then download the file and ingest it into the cluster.
    ///
    ///     The working assumption here is that the log lines are formatted as expected by Kusto. This is purely a
    ///     configuration issue which can't be enforced from the code here, so we assume that to be correct.
    /// </remarks>
    public sealed class AzureBlobStorageLog : StartupShutdownSlimBase, IKustoLog
    {
        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(AzureBlobStorageLog));

        private readonly AzureBlobStorageLogConfiguration _configuration;

        private readonly OperationContext _context;

        private readonly IClock _clock;

        private readonly IAbsFileSystem _fileSystem;

        private readonly ITelemetryFieldsProvider _telemetryFieldsProvider;

        private readonly CloudBlobContainer _container;

        private readonly IReadOnlyDictionary<string, string>? _additionalBlobMetadata;

        /// <summary>
        /// Logs are pushed into this queue, and batched together for writing them into files on disk.
        /// </summary>
        private readonly NagleQueue<string> _writeQueue;

        /// <summary>
        /// This queue has files that are ready to be uploaded. This is done in order to perform controlled uploading:
        ///  - Ensures that no more than X transfers are concurrently running
        ///  - Decouples log writing to disk from log uploads, since the latter process is likely to be significantly
        ///    slower than the former.
        /// </summary>
        private readonly NagleQueue<LogFile> _uploadQueue;

        /// <nodoc />
        public CounterCollection<AzureBlobStorageLogCounters> Counters { get; } = new CounterCollection<AzureBlobStorageLogCounters>();

        /// <summary>
        /// Allows external users to write to the stream when the output file has just been open
        /// </summary>
        public Func<StreamWriter, Task>? OnFileOpen { get; set; } = null;

        /// <summary>
        /// Allows external users to write to the stream when the output file is about to close
        /// </summary>
        public Func<StreamWriter, Task>? OnFileClose { get; set; } = null;

        /// <nodoc />
        public AzureBlobStorageLog(
            AzureBlobStorageLogConfiguration configuration,
            OperationContext context,
            IClock clock,
            IAbsFileSystem fileSystem,
            ITelemetryFieldsProvider telemetryFieldsProvider,
            AzureBlobStorageCredentials credentials,
            IReadOnlyDictionary<string, string>? additionalBlobMetadata)
            : this(configuration, context, clock, fileSystem, telemetryFieldsProvider,
                credentials.CreateCloudBlobClient().GetContainerReference(configuration.ContainerName),
                additionalBlobMetadata)
        {
        }

        /// <nodoc />
        public AzureBlobStorageLog(
            AzureBlobStorageLogConfiguration configuration,
            OperationContext context,
            IClock clock,
            IAbsFileSystem fileSystem,
            ITelemetryFieldsProvider telemetryFieldsProvider,
            CloudBlobContainer container,
            IReadOnlyDictionary<string, string>? additionalBlobMetadata)
        {
            _configuration = configuration;

            _context = context;
            _clock = clock;
            _fileSystem = fileSystem;
            _telemetryFieldsProvider = telemetryFieldsProvider;
            _container = container;
            _additionalBlobMetadata = additionalBlobMetadata;

            _writeQueue = NagleQueue<string>.CreateUnstarted(
                configuration.WriteMaxDegreeOfParallelism,
                configuration.WriteMaxInterval,
                configuration.WriteMaxBatchSize);

            _uploadQueue = NagleQueue<LogFile>.CreateUnstarted(
                configuration.UploadMaxDegreeOfParallelism,
                configuration.UploadMaxInterval,
                1);

            // TODO: this component doesn't have a quota, which could potentially be useful. If Azure Blob Storage
            // becomes unavailable for an extended period of time, we might cause disk space issues.
        }

        /// <nodoc />
        public Task<BoolResult> StartupAsync() => StartupAsync(_context);

        /// <inheritdoc />
        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            await _container.CreateIfNotExistsAsync(
                accessType: BlobContainerPublicAccessType.Off,
                options: null,
                operationContext: null,
                cancellationToken: context.Token);

            // Any logs in the staging are basically lost: they were in memory only, and we crashed or failed as we
            // were writing them. We just recreate the directory.
            try
            {
                _fileSystem.DeleteDirectory(_configuration.StagingFolderPath, DeleteOptions.Recurse);
            }
            catch (DirectoryNotFoundException)
            {

            }

            _fileSystem.CreateDirectory(_configuration.StagingFolderPath);

            _fileSystem.CreateDirectory(_configuration.UploadFolderPath);

            _writeQueue.Start(WriteBatchAsync);

            _uploadQueue.Start(UploadBatchAsync);

            return RecoverFromCrash(context);
        }

        /// <summary>
        ///     Enqueues all pending files for upload.
        /// </summary>
        /// <remarks>
        ///     Should only be used when the class is not actively in use, so as to avoid causing double-upload
        ///     attempts. That issue is only possible if we have concurrent uploads turned on.
        /// </remarks>
        public BoolResult RecoverFromCrash(OperationContext context)
        {
            return context.PerformOperation(Tracer, () =>
            {
                var pendingUpload = _fileSystem.EnumerateFiles(
                        _configuration.UploadFolderPath,
                        EnumerateOptions.Recurse)
                    .Select(fileInfo => new LogFile
                    {
                        Path = fileInfo.FullPath,
                    })
                    .ToList();
                _uploadQueue.EnqueueAll(pendingUpload);
                return BoolResult.Success;
            },
            traceErrorsOnly: true,
            counter: Counters[AzureBlobStorageLogCounters.RecoverFromCrashCalls]);
        }

        /// <nodoc />
        public Task<BoolResult> ShutdownAsync() => ShutdownAsync(_context);

        /// <inheritdoc />
        protected override Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            // This stops uploading more logs, wait for all in-memory logs to flush to disk, and then wait for ongoing
            // transfers to finish. Note that contrary to its name, the method is not actually asynchronous.
            if (!_configuration.DrainUploadsOnShutdown)
            {
                _uploadQueue.Suspend();
            }

            _writeQueue.Dispose();
            _uploadQueue.Dispose();
            return BoolResult.SuccessTask;
        }

        /// <inheritdoc />
        public void Write(string log)
        {
            _writeQueue.Enqueue(log);
        }

        /// <inheritdoc />
        public void WriteBatch(IEnumerable<string> logs)
        {
            _writeQueue.EnqueueAll(logs);
        }

        private Task WriteBatchAsync(string[] logEventInfos)
        {
            return _context.PerformOperationAsync(Tracer, async () =>
                {
                    // TODO: retry policy for writing to file?
                    var blobName = GenerateBlobName();
                    var stagingLogFilePath = _configuration.StagingFolderPath / blobName;
                    var logFile = (await WriteLogsToFileAsync(_context, stagingLogFilePath, logEventInfos).ThrowIfFailure()).Value;

                    var uploadLogFilePath = _configuration.UploadFolderPath / blobName;
                    _fileSystem.MoveFile(stagingLogFilePath, uploadLogFilePath, replaceExisting: true);
                    logFile.Path = uploadLogFilePath;
                    _uploadQueue.Enqueue(logFile);

                    return BoolResult.Success;
                },
                counter: Counters[AzureBlobStorageLogCounters.ProcessBatchCalls],
                traceErrorsOnly: true,
                silentOperationDurationThreshold: TimeSpan.MaxValue,
                extraEndMessage: _ => $"NumLines=[{logEventInfos.Length}]");
        }

        private Task UploadBatchAsync(LogFile[] logFilePaths)
        {
            Contract.Requires(logFilePaths.Length == 1);

            return _context.PerformOperationAsync(Tracer, () => UploadToBlobStorageAsync(_context, logFilePaths[0]),
                counter: Counters[AzureBlobStorageLogCounters.ProcessBatchCalls],
                traceErrorsOnly: true,
                // This isn't traced because we always have a single element in the batch, which gets traced inside
                // the individual upload.
                silentOperationDurationThreshold: TimeSpan.MaxValue,
                extraEndMessage: _ => $"LogFile=[{logFilePaths[0].Path}]");
        }

        private Task<Result<LogFile>> WriteLogsToFileAsync(OperationContext context, AbsolutePath logFilePath, string[] logs)
        {
            return context.PerformOperationAsync(Tracer, async () =>
                {
                    long compressedSizeBytes = 0;
                    long uncompressedSizeBytes = 0;

                    using (Stream fileStream = _fileSystem.Open(
                        logFilePath,
                        FileAccess.Write,
                        FileMode.CreateNew,
                        FileShare.None,
                        FileOptions.SequentialScan | FileOptions.Asynchronous))
                    {
                        // We need to make sure we close the compression stream before we take the fileStream's
                        // position, because the compression stream won't write everything until it's been closed,
                        // which leads to bad recorded values in compressedSizeBytes.
                        using (var gzipStream = new GZipStream(fileStream, CompressionLevel.Fastest, leaveOpen: true))
                        {
                            using var recordingStream = new CountingStream(gzipStream);
                            using var streamWriter = new StreamWriter(recordingStream, Encoding.UTF8, bufferSize: 32 * 1024, leaveOpen: true);

                            if (OnFileOpen != null)
                            {
                                await OnFileOpen(streamWriter);
                            }

                            foreach (var log in logs)
                            {
                                await streamWriter.WriteLineAsync(log);
                            }

                            if (OnFileClose != null)
                            {
                                await OnFileClose(streamWriter);
                            }

                            // Needed to ensure the recording stream receives everything it needs to receive
                            await streamWriter.FlushAsync();
                            uncompressedSizeBytes = recordingStream.BytesWritten;
                        }

                        compressedSizeBytes = fileStream.Position;
                    }

                    Tracer.TrackMetric(context, $"LogLinesWritten", logs.Length);
                    Tracer.TrackMetric(context, $"CompressedBytesWritten", compressedSizeBytes);
                    Tracer.TrackMetric(context, $"UncompressedBytesWritten", uncompressedSizeBytes);

                    return new Result<LogFile>(new LogFile()
                    {
                        Path = logFilePath,
                        UncompressedSizeBytes = uncompressedSizeBytes,
                        CompressedSizeBytes = compressedSizeBytes,
                    });
                },
                traceErrorsOnly: true,
                silentOperationDurationThreshold: TimeSpan.MaxValue,
                extraEndMessage: result =>
                {
                    if (result.Succeeded)
                    {
                        var value = result.Value;
                        return
                            $"LogFilePath=[{value.Path}] NumLogLines=[{logs.Length}] " +
                            $"CompressedSizeBytes=[{value.CompressedSizeBytes?.ToSizeExpression() ?? "Unknown"}] " +
                            $"UncompressedSizeBytes=[{value.UncompressedSizeBytes?.ToSizeExpression() ?? "Unknown"}]";
                    }
                    else
                    {
                        return $"LogFilePath=[{logFilePath}] NumLogLines=[{logs.Length}]";
                    }
                },
                counter: Counters[AzureBlobStorageLogCounters.WriteLogsToFileCalls]);
        }

        private Task<BoolResult> UploadToBlobStorageAsync(OperationContext context, LogFile uploadTask)
        {
            var logFilePath = uploadTask.Path;
            Contract.Requires(_fileSystem.FileExists(logFilePath));

            return context.PerformOperationAsync(Tracer, async () =>
                {
                    var blob = _container.GetBlockBlobReference(logFilePath.FileName);

                    if (await blob.ExistsAsync())
                    {
                        Tracer.Debug(context, $"Log file `{logFilePath}` already exists");
                        _fileSystem.DeleteFile(logFilePath);

                        Tracer.TrackMetric(context, $"UploadAlreadyExists", 1);
                        return BoolResult.Success;
                    }

                    var uploadSucceeded = true;
                    try
                    {
                        if (_additionalBlobMetadata != null)
                        {
                            foreach (KeyValuePair<string, string> pair in _additionalBlobMetadata)
                            {
                                blob.Metadata.Add(pair.Key, pair.Value);
                            }
                        }

                        if (uploadTask.UncompressedSizeBytes != null)
                        {
                            blob.Metadata.Add("rawSizeBytes", uploadTask.UncompressedSizeBytes.ToString());
                        }

                        await blob.UploadFromFileAsync(
                            logFilePath.ToString(),
                            accessCondition: AccessCondition.GenerateEmptyCondition(),
                            options: new BlobRequestOptions()
                            {
                                RetryPolicy = new Microsoft.WindowsAzure.Storage.RetryPolicies.ExponentialRetry(),
                            },
                            operationContext: null);
                    }
                    catch (Exception)
                    {
                        uploadSucceeded = false;
                        throw;
                    }
                    finally
                    {
                        if (uploadSucceeded)
                        {
                            _fileSystem.DeleteFile(logFilePath);

                            if (uploadTask.CompressedSizeBytes != null)
                            {
                                Tracer.TrackMetric(context, $"UploadedBytes", uploadTask.CompressedSizeBytes.Value);
                            }
                        }
                    }

                    return BoolResult.Success;
                },
                traceErrorsOnly: true,
                silentOperationDurationThreshold: TimeSpan.FromMinutes(1),
                extraEndMessage: _ => $"LogFilePath=[{logFilePath}] UploadSizeBytes=[{uploadTask.CompressedSizeBytes?.ToSizeExpression() ?? "Unknown"}]",
                counter: Counters[AzureBlobStorageLogCounters.UploadToBlobStorageCalls]);
        }

        private string GenerateBlobName()
        {
            // NOTE(jubayard): the file extension needs to match the data format. If it doesn't, things will likely
            // fail on the Kusto side.
            // See: https://kusto.azurewebsites.net/docs/management/data-ingestion/index.html#supported-data-formats
            var stamp = _telemetryFieldsProvider.Stamp ?? "Stamp";
            var machine = _telemetryFieldsProvider.MachineName ?? "Machine";
            var timestamp = _clock.UtcNow.ToReadableString();
            var guid = Guid.NewGuid();
            return $"{stamp}_{machine}_{timestamp}_{guid}.csv.gz";
        }

        private struct LogFile
        {
            public AbsolutePath Path { get; set; }

            /// <summary>
            ///     Kusto can optimize ingestion if it knows the size of the uncompressed file in bytes. We therefore
            ///     add it as metadata into the blobs.
            ///     See: https://kusto.azurewebsites.net/docs/management/data-ingestion/eventgrid.html#data-format
            /// </summary>
            public long? UncompressedSizeBytes { get; set; }

            /// <nodoc />
            public long? CompressedSizeBytes { get; set; }
        };
    }
}
