// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Native.IO;
using BuildXL.Utilities.Tasks;

namespace BuildXL.Cache.ContentStore.Service
{
    /// <summary>
    /// Cross platform (file-based) management of interruptable services
    /// </summary>
    public class ServiceLifetimeManager
    {
        private static readonly Tracer Tracer = new Tracer(nameof(ServiceLifetimeManager));
        /// <summary>
        /// The directory in which signal files are stored
        /// </summary>
        private AbsolutePath SignalFileRoot { get; }

        /// <summary>
        /// The interval at which signal files are polled
        /// </summary>
        private TimeSpan PollingInterval { get; }

        private const string SignalFileRootVariableName = "ServiceLifetimeManager.SignalFileRoot";
        private const string ServiceIdVariableName = "ServiceLifetimeManager.ServiceId";
        private const string PollingIntervalVariableName = "ServiceLifetimeManager.PollingIntervalMs";

        /// <nodoc />
        public ServiceLifetimeManager(AbsolutePath root, TimeSpan pollingInterval)
        {
            Directory.CreateDirectory(root.Path);
            SignalFileRoot = root;
            PollingInterval = pollingInterval;
        }

        /// <summary>
        /// Runs an externally configured interruptable service whose service lifetime comes from environment variables set
        /// by parent process
        /// </summary>
        public static Task<T> RunDeployedInterruptableServiceAsync<T>(
            OperationContext context,
            Func<CancellationToken, Task<T>> runAsync,
            Func<string, string>? getEnvironmentVariable = null,
            bool requireServiceInterruptionEnabled = true)
        {
            getEnvironmentVariable ??= name => Environment.GetEnvironmentVariable(name)!;

            var signalFileRoot = getEnvironmentVariable(SignalFileRootVariableName);
            if (!requireServiceInterruptionEnabled && string.IsNullOrEmpty(signalFileRoot))
            {
                return runAsync(context.Token);
            }

            var lifetimeManager = new ServiceLifetimeManager(
                new AbsolutePath(signalFileRoot),
                TimeSpan.FromMilliseconds(double.Parse(getEnvironmentVariable(PollingIntervalVariableName))));

            var serviceId = getEnvironmentVariable(ServiceIdVariableName);
            return lifetimeManager.RunInterruptableServiceAsync(context, serviceId, runAsync);
        }

        /// <summary>
        /// Gets set of environment variables used to launch service in child process using <see cref="RunDeployedInterruptableServiceAsync{T}"/>
        /// </summary>
        public IDictionary<string, string> GetDeployedInterruptableServiceVariables(string serviceId)
        {
            return new Dictionary<string, string>()
            {
                { SignalFileRootVariableName, SignalFileRoot.Path },
                { PollingIntervalVariableName, PollingInterval.TotalMilliseconds.ToString() },
                { ServiceIdVariableName, serviceId },
            };
        }

        /// <summary>
        /// Get path of signal file indicating that the service is active
        /// </summary>
        private string ServiceActiveFile(string serviceId) => Path.Combine(SignalFileRoot.Path, $"{serviceId}.active");

        /// <summary>
        /// Get path of signal file indicating that the service should shutdown
        /// </summary>
        internal string ServiceShutdownFile(string serviceId) => Path.Combine(SignalFileRoot.Path, $"{serviceId}.shutdown");

        /// <summary>
        /// Get path of signal file indicating that the service should NOT startup
        /// </summary>
        private string PreventStartupFile(string serviceId) => Path.Combine(SignalFileRoot.Path, $"{serviceId}.preventstartup");

        /// <summary>
        /// Run a service which can be interrupted by another service (via <see cref="RunInterrupterServiceAsync{T}"/>)
        /// or shutdown (via <see cref="GracefulShutdownServiceAsync"/>).
        /// </summary>
        public async Task<T> RunInterruptableServiceAsync<T>(OperationContext context, string serviceId, Func<CancellationToken, Task<T>> runAsync)
        {
            await WaitForDeletionAsync(PreventStartupFile(serviceId), context.Token).RethrowIfFailure();
            return await RunServiceCoreAsync(context, serviceId, runAsync);
        }

        private async Task<T> RunServiceCoreAsync<T>(OperationContext context, string serviceId, Func<CancellationToken, Task<T>> runAsync)
        {
            using (Create(ServiceActiveFile(serviceId), FileShare.None))
            using (Create(ServiceShutdownFile(serviceId), FileShare.Delete))
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(context.Token))
            {
                var token = cts.Token;
                WaitForDeletionAsync(ServiceShutdownFile(serviceId), () => cts.Cancel(), token).Forget();
                return await runAsync(token);
            }
        }

        /// <summary>
        /// Runs a service which can interrupt another service started with <see cref="RunInterruptableServiceAsync{T}"/>
        /// </summary>
        public async Task<T> RunInterrupterServiceAsync<T>(OperationContext context, string serviceId, string serviceToInterruptId, Func<CancellationToken, Task<T>> runAsync)
        {
            // Create prevent startup for interrupted service so that it doesn't start until this service completes
            using (Create(PreventStartupFile(serviceToInterruptId), FileShare.None))
            {
                await GracefulShutdownServiceAsync(context, serviceToInterruptId).RethrowIfFailure();

                return await RunServiceCoreAsync(context, serviceId, runAsync);
            }
        }

        /// <summary>
        /// Signals and waits for shutdown a service run under <see cref="RunInterruptableServiceAsync{T}"/>
        /// </summary>
        public Task<BoolResult> GracefulShutdownServiceAsync(OperationContext context, string serviceId)
        {
            return context.PerformOperationAsync(
                Tracer,
                () =>
                {
                    // Signal interrupted service to shutdown
                    FileUtilities.DeleteFile(ServiceShutdownFile(serviceId));

                    // Wait for interrupted service to shutdown
                    return WaitForShutdownAsync(context, serviceId);
                },
                extraEndMessage: r => serviceId,
                extraStartMessage: serviceId
            );
        }

        /// <summary>
        /// Waits for shutdown of service
        /// </summary>
        public Task<BoolResult> WaitForShutdownAsync(OperationContext context, string serviceId)
        {
            // Wait for interrupted service to shutdown
            return WaitForDeletionAsync(ServiceActiveFile(serviceId), context.Token);
        }

        /// <summary>
        /// Waits for a shutdown signal.
        /// </summary>
        public Task<BoolResult> WaitForShutdownRequestAsync(OperationContext context, string serviceId)
        {
            return WaitForDeletionAsync(ServiceShutdownFile(serviceId), context.Token);
        }

        /// <summary>
        /// Create a file that indicates that the service started.
        /// </summary>
        public BoolResult ServiceStarted(OperationContext context, string serviceId)
        {
            return context.PerformOperation(
                Tracer,
                () =>
                {
                    File.WriteAllText(ServiceShutdownFile(serviceId), string.Empty);
                    File.WriteAllText(ServiceActiveFile(serviceId), string.Empty);
                    return BoolResult.Success;
                },
                extraStartMessage: serviceId);
        }

        /// <summary>
        /// Delete a file that indicates that the service is active.
        /// </summary>
        public BoolResult ServiceStopped(OperationContext context, string serviceId)
        {
            return context.PerformOperation(
                Tracer,
                () =>
                {
                    FileUtilities.DeleteFile(ServiceActiveFile(serviceId));
                    return BoolResult.Success;
                },
                extraStartMessage: serviceId);
        }

        private FileStream Create(string path, FileShare share)
        {
            return new FileStream(path, FileMode.Create, FileAccess.Write, share, 1, FileOptions.DeleteOnClose);
        }

        private async Task<BoolResult> WaitForDeletionAsync(string path, CancellationToken token)
        {
            try
            {
                while (true)
                {
                    token.ThrowIfCancellationRequested();

                    if (!File.Exists(path))
                    {
                        return BoolResult.Success;
                    }

                    await Task.Delay(PollingInterval, token);
                }
            }
            catch (OperationCanceledException e)
            {
                return new BoolResult(e) { IsCancelled = true };
            }
        }

        private async Task WaitForDeletionAsync(string path, Action action, CancellationToken token)
        {
            await WaitForDeletionAsync(path, token).RethrowIfFailure();
            action();
        }
    }
}
