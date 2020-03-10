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
    /// Once CaSaaS is started up, we determine the previous CaSaaS offline time and log it.
    /// </summary>
    public class DistributedCacheServiceRunningTracker : IDisposable
    {
        private readonly IClock _clock;
        private readonly IAbsFileSystem _fileSystem;
        private readonly int _logIntervalSeconds;
        private readonly AbsolutePath _logFilePath;
        private readonly Timer _timer;

        private bool _disposed;

        private const string FileName = "CaSaaSRunning.txt";

        private static Tracer Tracer { get; } = new Tracer(nameof(DistributedCacheServiceRunningTracker));

        public DistributedCacheServiceRunningTracker(
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
                callback: _ => { PeriodicLogToFile(context, clock.UtcNow.ToString()); },
                state: null,
                dueTime: Timeout.InfiniteTimeSpan,
                period: Timeout.InfiniteTimeSpan);
        }

        public static Result<DistributedCacheServiceRunningTracker> Create(
            OperationContext context,
            IClock clock,
            IAbsFileSystem fileSystem,
            DistributedCacheServiceConfiguration configuration,
            TimeSpan startUpTime)
        {
            return context.PerformOperation(Tracer,
                () =>
                {
                    var logIntervalSeconds = configuration.DistributedContentSettings.ServiceRunningLogInSeconds;
                    if (logIntervalSeconds == null)
                    {
                        context.TraceInfo(ServiceStartedTrace(startUpTime));
                        return new Result<DistributedCacheServiceRunningTracker>($"{nameof(DistributedCacheServiceRunningTracker)} is disabled");
                    }

                    var logFilePath = configuration.LocalCasSettings.GetCacheRootPathWithScenario(LocalCasServiceSettings.DefaultCacheName) / FileName;

                    var serviceTracker = new DistributedCacheServiceRunningTracker(context, clock, fileSystem, logIntervalSeconds.Value, logFilePath);

                    serviceTracker.Start(context, startUpTime).ThrowIfFailure();

                    return new Result<DistributedCacheServiceRunningTracker>(serviceTracker);
                });
        }

        public string GetStatus()
        {
            if (_fileSystem.FileExists(_logFilePath))
            {
                var lastTime = System.Text.Encoding.Default.GetString(_fileSystem.ReadAllBytes(_logFilePath));
                return string.IsNullOrEmpty(lastTime)
                    ? "CaSaaS running log file was empty, could not determine offline time"
                    : $"offlineTime: {_clock.UtcNow - Convert.ToDateTime(lastTime)}";
            }
            else
            {
                return "creating CaSaaS running log file, could not determine offline time";
            }
        }

        public void PeriodicLogToFile(OperationContext context, string timeStampUtc)
        {
            var result = context.PerformOperation(
                Tracer,
                traceErrorsOnly: true,
                operation: () =>
                {
                    _fileSystem.WriteAllText(_logFilePath, timeStampUtc);

                    if (!_disposed)
                    {
                        _timer.Change(TimeSpan.FromSeconds(Convert.ToDouble(_logIntervalSeconds)), Timeout.InfiniteTimeSpan);
                    }

                    return BoolResult.Success;
                });

            // note: very improbable race condition if timer decides to dispose because of failure to log, and CaSaaS shuts down at the same time
            // Both calling Dispose() at the same time, will result in both seeing the timer not being disposed before, but unlikely.
            if (!result)
            {
                Dispose();
            }
        }

        public BoolResult Start(OperationContext context, TimeSpan startUpTime)
        {
            return context.PerformOperation(
                Tracer,
                traceErrorsOnly: true,
                operation: () =>
                {
                    context.TraceInfo($"{ServiceStartedTrace(startUpTime)}, {GetStatus()}");

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

        private static string ServiceStartedTrace(TimeSpan startupTime)
        {
            return $"CaSaaS started, startup duration took: {startupTime.ToString()}";
        }
    }
}
