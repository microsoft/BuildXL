// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Threading;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.Host.Configuration;

#nullable enable

namespace BuildXL.Cache.Host.Service
{
    /// <summary>
    /// Tracks CaSaaS offline time.
    /// </summary>
    public class ServiceOfflineDurationTracker : IDisposable
    {
        private readonly IClock _clock;
        private readonly IAbsFileSystem _fileSystem;
        private readonly int _logIntervalSeconds;
        private readonly AbsolutePath _logFilePath;
        private readonly Timer _timer;

        private bool _disposed;

        private const string FileName = "CaSaaSRunning.txt";

        private static Tracer Tracer { get; } = new Tracer(nameof(ServiceOfflineDurationTracker));

        private ServiceOfflineDurationTracker(
            OperationContext context,
            IClock clock,
            IAbsFileSystem fileSystem,
            int logIntervalSeconds,
            AbsolutePath logFilePath)
        {
            _clock = clock;
            _fileSystem = fileSystem;
            _logIntervalSeconds = logIntervalSeconds;
            _logFilePath = logFilePath;

            _timer = new Timer(
                callback: _ => { LogCurrentTimeStampToFile(context); },
                state: null,
                dueTime: TimeSpan.FromSeconds(_logIntervalSeconds),
                period: Timeout.InfiniteTimeSpan);
        }

        /// <nodoc />
        public static Result<ServiceOfflineDurationTracker> Create(
            OperationContext context,
            IClock clock,
            IAbsFileSystem fileSystem,
            DistributedCacheServiceConfiguration configuration)
        {
            var logIntervalSeconds = configuration.DistributedContentSettings.ServiceRunningLogInSeconds;

            var logFilePath = configuration.LocalCasSettings.GetCacheRootPathWithScenario(LocalCasServiceSettings.DefaultCacheName) / FileName;
            return Create(context, clock, fileSystem, logIntervalSeconds, logFilePath);
        }

        /// <nodoc />
        public static Result<ServiceOfflineDurationTracker> Create(
            OperationContext context,
            IClock clock,
            IAbsFileSystem fileSystem,
            int? logIntervalSeconds,
            AbsolutePath logFilePath)
        {
            return context.PerformOperation(Tracer,
                () =>
                {
                    if (logIntervalSeconds == null)
                    {
                        return new Result<ServiceOfflineDurationTracker>($"{nameof(ServiceOfflineDurationTracker)} is disabled");
                    }

                    var serviceTracker = new ServiceOfflineDurationTracker(context, clock, fileSystem, logIntervalSeconds.Value, logFilePath);

                    // We can't log current timestamp here, because it will mess up with the following GetOfflineDuration call.
                    // Instead, the caller of GetOfflineDuration may specify whether to update the file or not.
                    return new Result<ServiceOfflineDurationTracker>(serviceTracker);
                });
        }

        /// <summary>
        /// Gets the shutdown written to 'CaSaaSRunning.txt' file and logs the current time into the file if <paramref name="logTimeStampToFile"/> is true.
        /// </summary>
        /// <remarks>
        /// If <paramref name="logTimeStampToFile"/> is true, the next call to this method will return the datetime written by the previous invocation
        /// and not the time saved by the previous process.
        /// This is by design, because once the instance of this class is created and the timer instantiated in the constructor is called,
        /// the result of this call does no longer represent an actual offline time.
        ///
        /// We actual may reconsider the design of this class and use two files instead of one.
        /// </remarks>
        internal Result<DateTime> GetShutdownTime(OperationContext context, bool logTimeStampToFile)
        {
            const string ErrorPrefix = "Could not determine shutdown time";
            var result = context.PerformOperation(
                Tracer,
                () =>
                {
                    if (_fileSystem.FileExists(_logFilePath))
                    {
                        var lastTime = System.Text.Encoding.Default.GetString(_fileSystem.ReadAllBytes(_logFilePath));
                        return string.IsNullOrEmpty(lastTime)
                            ? new Result<DateTime>($"{ErrorPrefix}: CaSaaS running log file was empty")
                            : new Result<DateTime>(Convert.ToDateTime(lastTime));
                    }

                    return new Result<DateTime>($"{ErrorPrefix}: CaSaaS running log file was missing");
                },
                traceErrorsOnly: true);

            if (logTimeStampToFile)
            {
                LogCurrentTimeStampToFile(context);
            }

            return result;
        }

        internal void LogCurrentTimeStampToFile(OperationContext context)
        {
            LogTimeStampToFile(context, _clock.UtcNow.ToString());
        }

        private void LogTimeStampToFile(OperationContext context, string timeStampUtc)
        {
            context.PerformOperation(
                Tracer,
                traceErrorsOnly: true,
                operation: () =>
                {
                    _fileSystem.WriteAllText(_logFilePath, timeStampUtc);

                    ChangeTimer();

                    return BoolResult.Success;
                }).IgnoreFailure();
        }

        private void ChangeTimer()
        {
            if (!_disposed)
            {
                _timer.Change(TimeSpan.FromSeconds(_logIntervalSeconds), Timeout.InfiniteTimeSpan);
            }
        }

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
        }
    }
}
