// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities.ParallelAlgorithms;
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
        private readonly CancellableOperationContext _shutdownBoundContext;
        private readonly OperationContext _nonCancellableContext;

        private readonly IRetryPolicy _fileWriteRetryPolicy;
        private readonly IRetryPolicy _blobUploadRetryPolicy;

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

        /// <summary>
        /// Can't trace the shutdowns because of the following: the shutdown complete message can't be delivered
        /// because all the traces goes through this instance and the shutdown itself cleans up the resources.
        /// </summary>
        public override bool TraceShutdown => false;

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
            _shutdownBoundContext = TrackShutdown(_context);
            _nonCancellableContext = new OperationContext(_context.TracingContext, token: default);

            _clock = clock;
            _fileSystem = fileSystem;
            _telemetryFieldsProvider = telemetryFieldsProvider;
            _container = container;
            _additionalBlobMetadata = additionalBlobMetadata;

            _writeQueue = NagleQueue<string>.CreateUnstarted(
                WriteLogBatchAsync,
                configuration.WriteMaxDegreeOfParallelism,
                configuration.WriteMaxInterval,
                configuration.WriteMaxBatchSize);

            _uploadQueue = NagleQueue<LogFile>.CreateUnstarted(
                UploadFileAsync,
                configuration.UploadMaxDegreeOfParallelism,
                configuration.UploadMaxInterval,
                batchSize: 1);

            _fileWriteRetryPolicy = _configuration.FileWriteRetryPolicy.AsRetryPolicy(shouldRetry: exception =>
            {
                if (exception is DirectoryNotFoundException)
                {
                    return true;
                }

                if (exception is IOException ioException
                    && ioException.Message.Contains("There is not enough space on disk"))
                {
                    return true;
                }

                return false;
            });

            _blobUploadRetryPolicy = _configuration.BlobUploadRetryPolicy.AsRetryPolicy(shouldRetry: exception =>
            {
                if (exception is StorageException storageException)
                {
                    if (storageException.RequestInformation.HttpStatusCode == 503)
                    {
                        // This happens when storage is overloaded.
                        return true;
                    }

                    if (storageException.RequestInformation.HttpStatusCode == 403
                        && storageException.Message.Contains("Make sure the value of Authorization header is formed correctly including the signature"))
                    {
                        // This happens when SAS tokens aren't refreshed in time. We will retry these operations under
                        // the assumption that CSS will recover and we'll obtain a token.
                        return true;
                    }
                }

                return false;
            });

            // TODO: this component doesn't have a quota, which could potentially be useful. If Azure Blob Storage
            // becomes unavailable for an extended period of time (or if we somehow fail to upload logs), we might
            // cause disk space issues.
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

            PrepareLoggingFolders(context);

            _writeQueue.Start();

            _uploadQueue.Start();

            _ = EnqueuePreExistingLogFilesForUploadAsync(context).FireAndForgetErrorsAsync(context);

            return BoolResult.Success;
        }

        private void PrepareLoggingFolders(OperationContext context)
        {
            // Any logs in the staging are basically lost: they were in memory only and being written down, and we
            // crashed or failed as we were writing them. We will delete them and ensure the folders we use to write
            // logs to exist.
            try
            {
                foreach (var file in _fileSystem.EnumerateFiles(_configuration.StagingFolderPath, EnumerateOptions.Recurse))
                {
                    DeleteFileIgnoringParallelismIssues(file.FullPath);
                }
            }
            catch (DirectoryNotFoundException)
            {
                // This happens on first boot. Ignore to avoid polluting telemetry.
            }
            catch (Exception exception)
            {
                // This was just an attempt to clean up useless logs, so we don't want to fail startup because of this.
                Tracer.Error(context, exception, $"Failed to delete pre-existing logs from staging folder `{_configuration.StagingFolderPath}`");
            }

            _fileSystem.CreateDirectory(_configuration.StagingFolderPath);

            _fileSystem.CreateDirectory(_configuration.UploadFolderPath);
        }

        private void DeleteFileIgnoringParallelismIssues(AbsolutePath path)
        {
            try
            {
                _fileSystem.DeleteFile(path);
            }
            catch (Exception exception)
#pragma warning disable ERP022 // Unobserved exception in a generic exception handler
            // This pragma is required because one of the branches swallows the exception on purpose
            {
                if ((exception is IOException ioException && ioException.Message.Contains("The process cannot access the file"))
                    || exception is FileNotFoundException)
                {
                    // This happens when multiple processes are running with the same log folder: a different
                    // instance uploads the log file on boot and then proceeds to delete it.
                    // In either way, we'll proceed to attempt to delete the file, which won't do anything.
                }
                else
                {
                    throw;
                }
            }
#pragma warning restore ERP022 // Unobserved exception in a generic exception handler
        }

        /// <summary>
        ///     Enqueues all pending files for upload.
        /// </summary>
        /// <remarks>
        ///     Should only be used when the class is not actively in use, so as to avoid causing double-upload
        ///     attempts. That issue is only possible if we have concurrent uploads turned on.
        /// </remarks>
        public Task<BoolResult> EnqueuePreExistingLogFilesForUploadAsync(OperationContext context)
        {
            return context.PerformOperationAsync(Tracer, async () =>
            {
                // Forcing this to be an async Task so we don't block startup
                await Task.Yield();

                try
                {
                    var pendingUpload = _fileSystem.EnumerateFiles(
                            _configuration.UploadFolderPath,
                            EnumerateOptions.Recurse)
                        .Select(fileInfo => new LogFile
                        {
                            Path = fileInfo.FullPath,
                        });

                    _uploadQueue.EnqueueAll(pendingUpload);
                }
                catch (DirectoryNotFoundException)
                {
                    // Ignore when the upload folder doesn't exist (happens on boot)
                }

                return BoolResult.Success;
            },
            traceErrorsOnly: true);
        }

        /// <nodoc />
        public Task<BoolResult> ShutdownAsync() => ShutdownAsync(_context);

        /// <inheritdoc />
        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            // This stops uploading more logs, wait for all in-memory logs to flush to disk, and then wait for ongoing
            // transfers to finish. Note that contrary to its name, the method is not actually asynchronous.
            if (!_configuration.DrainUploadsOnShutdown)
            {
                _uploadQueue.Suspend();
            }

            var result = BoolResult.Success;
            try
            {
                await _writeQueue.DisposeAsync();
            }
            catch (Exception exception)
            {
                result &= new BoolResult(exception, $"Failed to dispose `{nameof(_writeQueue)}`");
            }

            try
            {
                await _uploadQueue.DisposeAsync();
            }
            catch (Exception exception)
            {
                result &= new BoolResult(exception, $"Failed to dispose `{nameof(_uploadQueue)}`");
            }

            try
            {
                _shutdownBoundContext.Dispose();
            }
            catch (Exception exception)
            {
                result &= new BoolResult(exception, $"Failed to dispose `{nameof(_shutdownBoundContext)}`");
            }

            return result;
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

        private Task WriteLogBatchAsync(List<string> logs)
        {
            // We use a context that can't be cancelled for writing logs to files. The reason for this is we really
            // want all inflight logs to be written down and prepped for upload (otherwise they're lost!).
            return _nonCancellableContext.PerformOperationWithTimeoutAsync(Tracer, async (context) =>
                {
                    var blobName = GenerateBlobName();
                    var stagingLogFilePath = _configuration.StagingFolderPath / blobName;
                    var logFile = (await WriteLogsToFileWithRetryPolicyAsync(_context, stagingLogFilePath, logs).ThrowIfFailure()).Value;

                    var uploadLogFilePath = _configuration.UploadFolderPath / blobName;
                    _fileSystem.MoveFile(stagingLogFilePath, uploadLogFilePath, replaceExisting: true);
                    logFile.Path = uploadLogFilePath;
                    _uploadQueue.Enqueue(logFile);

                    return BoolResult.Success;
                },
                timeout: _configuration.FileWriteTimeout,
                pendingOperationTracingInterval: _configuration.FileWriteTracePeriod,
                traceErrorsOnly: true,
                counter: Counters[AzureBlobStorageLogCounters.WriteLogBatchCalls]);
        }

        private Task<Result<LogFile>> WriteLogsToFileWithRetryPolicyAsync(OperationContext context, AbsolutePath logFilePath, IReadOnlyList<string> logs)
        {
            return _fileWriteRetryPolicy.ExecuteAsync(async () =>
            {
                try
                {
                    // Its important to throw the original exception inside of the result here
                    return await WriteLogsToFileAsync(context, logFilePath, logs).RethrowIfFailure();
                }
                catch (DirectoryNotFoundException)
                {
                    _fileSystem.CreateDirectory(logFilePath.Parent!);
                    throw;
                }
            }, context.Token);
        }

        private Task<Result<LogFile>> WriteLogsToFileAsync(OperationContext context, AbsolutePath logFilePath, IReadOnlyList<string> logs)
        {
            return context.PerformOperationWithTimeoutAsync(Tracer, async (context) =>
                {
                    var logFile = await TryWriteLogFileAsync(context, logFilePath, logs);

                    Tracer.TrackMetric(context, $"LogLinesWritten", logFile.LogLinesWritten ?? 0);
                    Tracer.TrackMetric(context, $"CompressedBytesWritten", logFile.CompressedSizeBytes ?? 0);
                    Tracer.TrackMetric(context, $"UncompressedBytesWritten", logFile.UncompressedSizeBytes ?? 0);

                    return new Result<LogFile>(logFile);
                },
                timeout: _configuration.FileWriteAttemptTimeout,
                pendingOperationTracingInterval: _configuration.FileWriteAttemptTracePeriod,
                traceErrorsOnly: true,
                extraEndMessage: result =>
                {
                    if (result.Succeeded)
                    {
                        var value = result.Value;
                        return
                            $"LogFilePath=[{value.Path}] NumLogLines=[{logs.Count}] " +
                            $"CompressedSizeBytes=[{value.CompressedSizeBytes?.ToSizeExpression() ?? "Unknown"}] " +
                            $"UncompressedSizeBytes=[{value.UncompressedSizeBytes?.ToSizeExpression() ?? "Unknown"}]";
                    }
                    else
                    {
                        return $"LogFilePath=[{logFilePath}] NumLogLines=[{logs.Count}]";
                    }
                },
                counter: Counters[AzureBlobStorageLogCounters.WriteLogsToFileCalls]);
        }

        private async Task<LogFile> TryWriteLogFileAsync(OperationContext context, AbsolutePath logFilePath, IReadOnlyList<string> logs)
        {
            try
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
                    using (var gzipStream = new GZipStream(fileStream, CompressionLevel.Optimal, leaveOpen: true))
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

                return new LogFile()
                {
                    Path = logFilePath,
                    UncompressedSizeBytes = uncompressedSizeBytes,
                    CompressedSizeBytes = compressedSizeBytes,
                    // NumLines here isn't actually the number of lines, but the number of log infos.
                    // TODO: make this be the number of lines again
                    LogLinesWritten = logs.Count,
                };
            }
            catch (Exception exception)
            {
                Tracer.Error(context, exception, $"Failed to write logs to `{logFilePath}`. Deleting the temporary file.");
                DeleteFileIgnoringParallelismIssues(logFilePath);
                throw;
            }
        }

        [StackTraceHidden]
        private Task UploadFileAsync(List<LogFile> logFilePaths)
        {
            Contract.Requires(logFilePaths.Count == 1);

            // We use a context that's bound to the shutdown of this component. The reason for this is we don't want
            // to delay shutdown. Note that this does not loose logs, because logs will be in the upload folder, and
            // therefore uploaded by the next bootup.
            return UploadFileToBlobStorageAsync(_shutdownBoundContext, logFilePaths[0]);
        }

        private Task<BoolResult> UploadFileToBlobStorageAsync(OperationContext context, LogFile logFile)
        {
            return context.PerformOperationWithTimeoutAsync(Tracer, async (context) =>
                {
                    var succeeded = false;
                    var repeated = false;
                    var delete = true;
                    try
                    {
                        await UploadFileToBlobStorageWithRetryPolicyAsync(context, logFile);

                        succeeded = true;
                    }
                    catch (StorageException exception) when (
                        exception.RequestInformation.HttpStatusCode == (int)HttpStatusCode.PreconditionFailed
                        // Used only in the development storage case
                        || exception.RequestInformation.ErrorCode == "BlobAlreadyExists")
                    {
                        repeated = true;
                    }
                    catch (Exception exception)
#pragma warning disable ERP022 // Unobserved exception in a generic exception handler
                    // This pragma is required because one of the branches swallows the exception on purpose
                    {
                        if ((exception is IOException ioException && ioException.Message.Contains("The process cannot access the file"))
                            || exception is FileNotFoundException)
                        {
                            // This happens when multiple processes are running with the same log folder: a different
                            // instance uploads the log file on boot and then proceeds to delete it. We'll skip the
                            // deletion under the assumption that a different process will take care of it
                            delete = false;
                        }
                        else if (exception is UnauthorizedAccessException)
                        {
                            // We have observed some of these in production where we basically get access denied. In
                            // this case, we'll let future CASaaS bootups deal with this by pretending nothing happened
                            delete = false;
                            return new BoolResult(exception, "Failed to upload file due to access denial. Skipping file upload.");
                        }
                        else if (exception is OperationCanceledException || exception is TimeoutException)
                        {
                            // This happens when the upload is cancelled or times out. In this case, we'll leave the
                            // file alive to upload it in the future.
                            delete = false;
                        }
                        else
                        {
                            throw;
                        }
                    }
#pragma warning restore ERP022 // Unobserved exception in a generic exception handler
                    finally
                    {
                        if (delete)
                        {
                            // If this fails, it's not a big deal: a future process will try to upload the file on boot
                            // and fail because it will already exist in storage.
                            try
                            {
                                DeleteFileIgnoringParallelismIssues(logFile.Path);
                            }
                            catch (Exception exception)
                            {
                                Tracer.Error(context, exception, $"Failed to delete log file `{logFile.Path}` after upload");
                            }
                        }

                        if (succeeded && logFile.CompressedSizeBytes != null)
                        {
                            Tracer.TrackMetric(context, $"UploadedBytes", logFile.CompressedSizeBytes.Value);
                        }

                        if (repeated)
                        {
                            Tracer.Warning(context, $"Log file `{logFile.Path}` already exists in storage");
                            Tracer.TrackMetric(context, $"UploadAlreadyExists", 1);
                        }
                    }

                    return BoolResult.Success;
                },
                timeout: _configuration.BlobUploadTimeout,
                pendingOperationTracingInterval: _configuration.BlobUploadTracePeriod,
                traceErrorsOnly: true,
                extraEndMessage: _ => $"LogFilePath=[{logFile.Path}] UploadSizeBytes=[{logFile.CompressedSizeBytes?.ToSizeExpression() ?? "Unknown"}]",
                counter: Counters[AzureBlobStorageLogCounters.UploadToBlobStorageCalls]);
        }

        private Task UploadFileToBlobStorageWithRetryPolicyAsync(OperationContext context, LogFile logFile)
        {
            return _blobUploadRetryPolicy.ExecuteAsync(async () =>
            {
                // Its important to throw the original exception inside of the result here
                await AttemptUploadFileToBlobStorageAsync(context, logFile, logFile.Path).RethrowIfFailure();
            }, context.Token);
        }

        private Task<BoolResult> AttemptUploadFileToBlobStorageAsync(OperationContext context, LogFile logFile, AbsolutePath logFilePath)
        {
            return context.PerformOperationWithTimeoutAsync(Tracer, async (context) =>
            {
                var blob = _container.GetBlockBlobReference(logFilePath.FileName);

                if (_additionalBlobMetadata != null)
                {
                    foreach (KeyValuePair<string, string> pair in _additionalBlobMetadata)
                    {
                        blob.Metadata.Add(pair.Key, pair.Value);
                    }
                }

                if (logFile.UncompressedSizeBytes != null)
                {
                    blob.Metadata.Add("rawSizeBytes", logFile.UncompressedSizeBytes.ToString());
                }

                await blob.UploadFromFileAsync(
                    logFilePath.ToString(),
                    accessCondition: AccessCondition.GenerateIfNotExistsCondition(),
                    options: new BlobRequestOptions()
                    {
                        RetryPolicy = new Microsoft.WindowsAzure.Storage.RetryPolicies.ExponentialRetry(),
                    },
                    operationContext: null,
                    cancellationToken: context.Token);

                return BoolResult.Success;
            },
            timeout: _configuration.BlobUploadAttemptTimeout,
            pendingOperationTracingInterval: _configuration.BlobUploadAttemptTracePeriod,
            traceErrorsOnly: true);
        }

        private string GenerateBlobName()
        {
            var stamp = _telemetryFieldsProvider.Stamp ?? "Stamp";
            var machine = _telemetryFieldsProvider.MachineName ?? Environment.MachineName;
            var timestamp = _clock.UtcNow.ToReadableString();
            var guid = Guid.NewGuid();

            // Two important things here:
            //  1. The file extension needs to match the data format. If it doesn't, things will likely fail in Kusto
            //     See: https://docs.microsoft.com/en-us/azure/data-explorer/ingestion-supported-formats
            //  2. The naming convention here is meant to help use Kusto External Tables for ingestion-less queries
            //     See: https://docs.microsoft.com/en-us/azure/data-explorer/kusto/query/schema-entities/externaltables
            return $"{timestamp}-{stamp}-{machine}-{guid}.csv.gz";
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

            /// <nodoc />
            public long? LogLinesWritten { get; set; }
        };
    }
}
