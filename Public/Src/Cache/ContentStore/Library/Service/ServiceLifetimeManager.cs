// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
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
        /// <summary>
        /// The directory in which signal files are stored
        /// </summary>
        private AbsolutePath Root { get; }

        /// <summary>
        /// The interval at which signal files are polled
        /// </summary>
        private TimeSpan PollingInterval { get; }

        /// <nodoc />
        public ServiceLifetimeManager(AbsolutePath root, TimeSpan pollingInterval)
        {
            Root = root;
            PollingInterval = pollingInterval;
        }

        /// <summary>
        /// Get path of signal file indicating that the service is active
        /// </summary>
        private string ServiceActiveFile(string serviceId) => Path.Combine(Root.Path, $"{serviceId}.active");

        /// <summary>
        /// Get path of signal file indicating that the service should shutdown
        /// </summary>
        private string ServiceShutdownFile(string serviceId) => Path.Combine(Root.Path, $"{serviceId}.shutdown");

        /// <summary>
        /// Get path of signal file indicating that the service should NOT startup
        /// </summary>
        private string PreventStartupFile(string serviceId) => Path.Combine(Root.Path, $"{serviceId}.preventstartup");

        /// <summary>
        /// Run a service which can be interrupted by another service (via <see cref="RunInterrupterServiceAsync"/>)
        /// or shutdown (via <see cref="ShutdownServiceAsync"/>).
        /// </summary>
        public async Task<T> RunInterruptableServiceAsync<T>(OperationContext context, string serviceId, Func<CancellationToken, Task<T>> runAsync)
        {
            await WaitForDeletionAsync(context, PreventStartupFile(serviceId));
            return await RunServiceCoreAsync(context, serviceId, runAsync);
        }

        private async Task<T> RunServiceCoreAsync<T>(OperationContext context, string serviceId, Func<CancellationToken, Task<T>> runAsync)
        {
            using (Create(ServiceActiveFile(serviceId), FileShare.None))
            using (Create(ServiceShutdownFile(serviceId), FileShare.Delete))
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(context.Token))
            {
                WaitForDeletionAsync(context, ServiceShutdownFile(serviceId), () => cts.Cancel()).Forget();
                return await runAsync(cts.Token);
            }
        }

        /// <summary>
        /// Runs a service which can interrupt another service started with <see cref="RunInterruptableServiceAsync"/>
        /// </summary>
        public async Task<T> RunInterrupterServiceAsync<T>(OperationContext context, string serviceId, string serviceToInterruptId, Func<CancellationToken, Task<T>> runAsync)
        {
            // Create prevent startup for interrupted service so that it doesn't start until this service completes
            using (Create(PreventStartupFile(serviceToInterruptId), FileShare.None))
            {
                await ShutdownServiceAsync(context, serviceToInterruptId);

                return await RunServiceCoreAsync(context, serviceId, runAsync);
            }
        }

        /// <summary>
        /// Signals and waits for shutdown a service run under <see cref="RunInterruptableServiceAsync"/>
        /// </summary>
        public Task ShutdownServiceAsync(OperationContext context, string serviceId)
        {
            // Signal interrupted service to shutdown
            FileUtilities.DeleteFile(ServiceShutdownFile(serviceId));

            // Wait for interrupted service to shutdown
            return WaitForDeletionAsync(context, ServiceActiveFile(serviceId));
        }

        private FileStream Create(string path, FileShare share)
        {
            return new FileStream(path, FileMode.Create, FileAccess.Write, share, 1, FileOptions.DeleteOnClose);
        }

        private async Task WaitForDeletionAsync(OperationContext context, string path)
        {
            while (true)
            {
                context.Token.ThrowIfCancellationRequested();

                if (!File.Exists(path))
                {
                    return;
                }

                await Task.Delay(PollingInterval, context.Token);
            }
        }

        private async Task WaitForDeletionAsync(OperationContext context, string path, Action action)
        {
            await WaitForDeletionAsync(context, path);
            action();
        }
    }
}
