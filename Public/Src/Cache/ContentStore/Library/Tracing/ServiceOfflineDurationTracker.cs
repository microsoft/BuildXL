// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Threading;
using BuildXL.Utilities.ParallelAlgorithms;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;

#nullable enable

namespace BuildXL.Cache.ContentStore.Tracing
{
    /// <summary>
    /// Tracks CaSaaS offline time.
    /// </summary>
    /// <remarks>
    /// The instance of this class deals with two file names, one is used to write the current time periodically, and the second one is used to
    /// determine whether the service abnormally terminated or not.
    /// </remarks>
    public class ServiceOfflineDurationTracker : IDisposable
    {
        private const string ErrorPrefix = "Could not determine shutdown time";

        /// <nodoc />
        public const string PreviousFileName = "CaSaaSPrevious.txt";

        /// <nodoc />
        public const string CurrentFileName = "CaSaaSRunning.txt";

        private static Tracer Tracer { get; } = new Tracer(nameof(ServiceOfflineDurationTracker));

        private readonly IClock _clock;
        private readonly IAbsFileSystem _fileSystem;
        private readonly TimeSpan _logInterval;
        private readonly AbsolutePath _currentLogFilePath;
        private readonly AbsolutePath _previousLogFilePath;
        private readonly Timer _timer;

        private readonly Context _originalTracingContext;
        private int _logTimeStampToFileSync = 0;
        private bool _disposed;

        private object _lazyLastWriteTimeLock = new object();
        private Result<(DateTime lastWriteTime, bool shutdownCorrectly)>? _lazyLastWriteTimeWithShutdown;
        private bool _lazyLastWriteTimeWithShutdownInitialized;

        private ServiceOfflineDurationTracker(
            OperationContext context,
            IClock clock,
            IAbsFileSystem fileSystem,
            TimeSpan logInterval,
            AbsolutePath logFolder)
        {
            _clock = clock;
            _fileSystem = fileSystem;
            _logInterval = logInterval;
            _currentLogFilePath = logFolder / CurrentFileName;
            _previousLogFilePath = logFolder / PreviousFileName;
            _originalTracingContext = context;

            _timer = new Timer(
                callback: _ => { LogCurrentTimeStampToFile(context); },
                state: null,
                dueTime: _logInterval,
                period: Timeout.InfiniteTimeSpan);
        }

        /// <nodoc />
        public static Result<ServiceOfflineDurationTracker> Create(
            OperationContext context,
            IClock clock,
            IAbsFileSystem fileSystem,
            TimeSpan? logInterval,
            AbsolutePath logFilePath)
        {
            return context.PerformOperation(Tracer,
                () =>
                {
                    if (logInterval == null)
                    {
                        return new Result<ServiceOfflineDurationTracker>($"{nameof(ServiceOfflineDurationTracker)} is disabled");
                    }

                    var serviceTracker = new ServiceOfflineDurationTracker(context, clock, fileSystem, logInterval.Value, logFilePath);

                    // We can't log current timestamp here, because it will mess up with the following GetOfflineDuration call.
                    // Instead, the caller of GetOfflineDuration may specify whether to update the file or not.
                    return new Result<ServiceOfflineDurationTracker>(serviceTracker);
                });
        }

        /// <summary>
        /// Gets the time when the service wrote a heartbeat message to disk and a flag whether the service was shut down correctly or not.
        /// </summary>
        /// <remarks>
        /// The method can be called multiple times and will provide the same (memoized) results.
        /// </remarks>
        public Result<(DateTime lastServiceHeartbeatTime, bool shutdownCorrectly)> GetLastServiceHeartbeatTime(OperationContext context)
        {
            return LazyInitializer.EnsureInitialized(ref _lazyLastWriteTimeWithShutdown, ref _lazyLastWriteTimeWithShutdownInitialized, ref _lazyLastWriteTimeLock, () => getLastShutdownTimeCore())!;
            
            Result<(DateTime lastWriteTime, bool shutdownCorrectly)> getLastShutdownTimeCore()
            {
                return context.PerformOperation(
                    Tracer,
                    () =>
                    {
                        try
                        {
                            // First, trying to read from the previous file that should exist if the service stopped normally.
                            bool shutdownCorrectly = true;

                            // Re-throwing all the errors because we don't expect them here!
                            Result<bool> readResult = TryReadHeartBeatTimeFromFile(_previousLogFilePath, out var result).ThrowIfFailure();

                            // If the file was missing, then trying to read from the current file but this means that we haven't shut down correctly.
                            if (!readResult.Value)
                            {
                                shutdownCorrectly = false;
                                readResult = TryReadHeartBeatTimeFromFile(_currentLogFilePath, out result).ThrowIfFailure();

                                if (!readResult.Value)
                                {
                                    // The main file is missing as well!
                                    return new Result<(DateTime lastWriteTime, bool shutdownCorrectly)>($"{ErrorPrefix}: CaSaaS running log file was missing");
                                }
                            }

                            return Result.Success((result, shutdownCorrectly));
                        }
                        finally
                        {
                            // Nuking the old file, because this method should be run only once!
                            // If the file is missing the call will do no-op.
                            _fileSystem.DeleteFile(_previousLogFilePath);
                        }
                    });
            }
        }

        private Result<bool> TryReadHeartBeatTimeFromFile(AbsolutePath fileName, out DateTime result)
        {
            result = default;
            try
            {
                var lastTime = System.Text.Encoding.Default.GetString(_fileSystem.ReadAllBytes(fileName));

                if (string.IsNullOrEmpty(lastTime))
                {
                    return new Result<bool>($"{ErrorPrefix}: CaSaaS running log file '{fileName}' was empty");
                }

                result = parseDateTime(lastTime);
                return true;
            }
            catch (DirectoryNotFoundException)
            {
                // DirectoryNotFoundException is thrown if the part of the path is not available.
                // This is possible when 'd:\dbs\Cache\ContentAddressableStore folder is not yet created.
                return false;
            }
            catch (FileNotFoundException)
            {
                return false;
            }

            static DateTime parseDateTime(string dateTimeAsString)
            {
                try
                {
                    return DateTime.Parse(dateTimeAsString);
                }
                catch (FormatException e)
                {
                    throw new FormatException($"String '{dateTimeAsString}' was not recognized as a valid DateTime.", e);
                }
            }
        }

        /// <nodoc />
        public void LogCurrentTimeStampToFile(OperationContext context)
        {
            LogTimeStampToFile(context, _clock.UtcNow.ToString("G"));
        }

        private void LogTimeStampToFile(OperationContext context, string timeStampUtc)
        {
            context.PerformOperation(
                Tracer,
                traceOperationStarted: false,
                operation: () =>
                {
                    return ConcurrencyHelper.RunOnceIfNeeded(
                        ref _logTimeStampToFileSync,
                        () =>
                        {
                            _fileSystem.WriteAllText(_currentLogFilePath, timeStampUtc);
                            return BoolResult.Success;
                        },
                        funcIsRunningResultProvider: () => BoolResult.Success);
                }).IgnoreFailure();

            // Changing the timer regardless if write to the file succeed or failed.    
            ChangeTimer();
        }

        private void ChangeTimer()
        {
            if (!_disposed)
            {
                try
                {
                    _timer.Change(_logInterval, Timeout.InfiniteTimeSpan);
                }
                catch(ObjectDisposedException) { }
            }
        }

        /// <nodoc />
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

#pragma warning disable AsyncFixer02
            _timer.Dispose();
#pragma warning restore AsyncFixer02

            // If the timer's callback is still running, this operation will be skipped due to re-entrancy check inside of it.
            LogCurrentTimeStampToFile(new OperationContext(_originalTracingContext));

            // Renaming the current file that indicates that the shutdown was successful.
            RenameCurrentToPreviousLogFile();
        }

        private void RenameCurrentToPreviousLogFile()
        {
            try
            {
                _fileSystem.MoveFile(_currentLogFilePath, _previousLogFilePath, replaceExisting: true);
            }
            catch (Exception e)
            {
                Tracer.Warning(_originalTracingContext, $"Fail to swap '{_currentLogFilePath}' with '{_previousLogFilePath}': {e}");
            }
        }
    }
}
