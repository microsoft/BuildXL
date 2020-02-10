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
using BuildXL.Cache.ContentStore.Distributed.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Utils;
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
    public sealed class AzureBlobStorageLog : StartupShutdownSlimBase
    {
        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(AzureBlobStorageLog));

        private readonly AzureBlobStorageLogConfiguration _configuration;

        private readonly OperationContext _context;

        private readonly IClock _clock;

        private readonly IAbsFileSystem _fileSystem;

        private readonly ITelemetryFieldsProvider _telemetryFieldsProvider;

        private readonly CloudBlobContainer _container;

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
        public AzureBlobStorageLog(AzureBlobStorageLogConfiguration configuration, OperationContext context, IClock clock, IAbsFileSystem fileSystem, ITelemetryFieldsProvider telemetryFieldsProvider, AzureBlobStorageCredentials credentials)
        {
            _configuration = configuration;

            _context = context;
            _clock = clock;
            _fileSystem = fileSystem;
            _telemetryFieldsProvider = telemetryFieldsProvider;

            var cloudBlobClient = credentials.CreateCloudBlobClient();
            _container = cloudBlobClient.GetContainerReference(configuration.ContainerName);

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
                counter: Counters[AzureBlobStorageLogCounters.RecoverFromCrashCalls],
                traceErrorsOnly: true);
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

        /// <summary>
        ///     Write a single log line
        /// </summary>
        public void Write(string log)
        {
            _writeQueue.Enqueue(log);
        }

        /// <summary>
        ///     Write multiple log lines
        /// </summary>
        public void Write(IEnumerable<string> logs)
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
                counter: Counters[AzureBlobStorageLogCounters.ProcessBatchCalls]);
        }

        private Task UploadBatchAsync(LogFile[] logFilePaths)
        {
            Contract.Requires(logFilePaths.Length == 1);

            return _context.PerformOperationAsync(Tracer, () =>
                {
                    return UploadToBlobStorageAsync(_context, logFilePaths[0]);
                },
                counter: Counters[AzureBlobStorageLogCounters.ProcessBatchCalls],
                traceErrorsOnly: true);
        }

        private Task<Result<LogFile>> WriteLogsToFileAsync(OperationContext context, AbsolutePath logFilePath, string[] logs)
        {
            return context.PerformOperationAsync(Tracer, async () =>
                {
                    using var fileStream = await _fileSystem.OpenSafeAsync(
                        logFilePath,
                        FileAccess.Write,
                        FileMode.CreateNew,
                        FileShare.None,
                        FileOptions.SequentialScan | FileOptions.Asynchronous);
                    using var gzipStream = new GZipStream(fileStream, CompressionLevel.Fastest, leaveOpen: true);
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

                    await streamWriter.FlushAsync();

                    Tracer.TrackMetric(context, $"LogLinesWritten", logs.Length);

                    var compressedSizeBytes = fileStream.Position;
                    Tracer.TrackMetric(context, $"CompressedBytesWritten", compressedSizeBytes);

                    var uncompressedSizeBytes = recordingStream.BytesWritten;
                    Tracer.TrackMetric(context, $"UncompressedBytesWritten", uncompressedSizeBytes);

                    return new Result<LogFile>(new LogFile()
                    {
                        Path = logFilePath,
                        UncompressedSizeBytes = uncompressedSizeBytes,
                        CompressedSizeBytes = compressedSizeBytes,
                    });
                },
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
                counter: Counters[AzureBlobStorageLogCounters.WriteLogsToFileCalls],
                traceErrorsOnly: true);
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
                        context.TraceDebug($"Log file `{logFilePath}` already exists");
                        _fileSystem.DeleteFile(logFilePath);

                        Tracer.TrackMetric(context, $"UploadAlreadyExists", 1);
                        return BoolResult.Success;
                    }

                    var uploadSucceeded = true;
                    try
                    {
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
                extraEndMessage: _ => $"LogFilePath=[{logFilePath}] UploadSizeBytes=[{uploadTask.CompressedSizeBytes?.ToSizeExpression() ?? "Unknown"}]",
                counter: Counters[AzureBlobStorageLogCounters.UploadToBlobStorageCalls],
                traceErrorsOnly: true);
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
