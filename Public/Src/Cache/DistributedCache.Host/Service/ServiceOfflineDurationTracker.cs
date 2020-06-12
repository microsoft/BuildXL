// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
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

        public ServiceOfflineDurationTracker(
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

            // Timer does not start until we call Start(), because in testing we do not want to start this timer, we use a simulated test clock instead.
            _timer = new Timer(
                callback: _ => { LogTimeStampToFile(context, clock.UtcNow.ToString()); },
                state: null,
                dueTime: Timeout.InfiniteTimeSpan,
                period: Timeout.InfiniteTimeSpan);
        }

        public static Result<ServiceOfflineDurationTracker> Create(
            OperationContext context,
            IClock clock,
            IAbsFileSystem fileSystem,
            DistributedCacheServiceConfiguration configuration)
        {
            return context.PerformOperation(Tracer,
                () =>
                {
                    var logIntervalSeconds = configuration.DistributedContentSettings.ServiceRunningLogInSeconds;
                    if (logIntervalSeconds == null)
                    {
                        return new Result<ServiceOfflineDurationTracker>($"{nameof(ServiceOfflineDurationTracker)} is disabled");
                    }

                    var logFilePath = configuration.LocalCasSettings.GetCacheRootPathWithScenario(LocalCasServiceSettings.DefaultCacheName) / FileName;

                    var serviceTracker = new ServiceOfflineDurationTracker(context, clock, fileSystem, logIntervalSeconds.Value, logFilePath);

                    serviceTracker.Start(context).ThrowIfFailure();

                    return new Result<ServiceOfflineDurationTracker>(serviceTracker);
                });
        }

        internal Result<TimeSpan> GetOfflineDuration()
        {
            const string ErrorPrefix = "Could not determine offline time";
            if (_fileSystem.FileExists(_logFilePath))
            {
                var lastTime = System.Text.Encoding.Default.GetString(_fileSystem.ReadAllBytes(_logFilePath));
                return string.IsNullOrEmpty(lastTime)
                    ? new Result<TimeSpan>($"{ErrorPrefix}: CaSaaS running log file was empty")
                    : new Result<TimeSpan>(_clock.UtcNow - Convert.ToDateTime(lastTime));
            }

            return new Result<TimeSpan>($"{ErrorPrefix}: CaSaaS running log file was missing");
        }

        internal void LogTimeStampToFile(OperationContext context, string timeStampUtc)
        {
            context.PerformOperation(
                Tracer,
                traceErrorsOnly: true,
                operation: () =>
                {
                    _fileSystem.WriteAllText(_logFilePath, timeStampUtc);

                    if (!_disposed)
                    {
                        _timer.Change(TimeSpan.FromSeconds(_logIntervalSeconds), Timeout.InfiniteTimeSpan);
                    }

                    return BoolResult.Success;
                }).IgnoreFailure();
        }

        private BoolResult Start(OperationContext context)
        {
            return context.PerformOperation(
                Tracer,
                traceErrorsOnly: true,
                operation: () =>
                {
                    _timer.Change(TimeSpan.Zero, Timeout.InfiniteTimeSpan);

                    return BoolResult.Success;
                });
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

#pragma warning disable AsyncFixer02
            _timer.Dispose();
#pragma warning restore AsyncFixer02

            _disposed = true;
        }
    }
}
