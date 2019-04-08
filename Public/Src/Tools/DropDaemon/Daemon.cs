// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Ipc;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.ExternalApi;
using BuildXL.Ipc.Interfaces;
using BuildXL.Tracing;
using BuildXL.Tracing.CloudBuild;
using BuildXL.Utilities;
using BuildXL.Utilities.CLI;
using BuildXL.Utilities.Tracing;
using JetBrains.Annotations;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.Drop.WebApi;
using Newtonsoft.Json;
using static Tool.DropDaemon.Statics;

namespace Tool.DropDaemon
{
    /// <summary>
    ///     Responsible for accepting and handling TCP/IP connections from clients.
    /// </summary>
    public sealed class Daemon : IDisposable, IIpcOperationExecutor
    {
        /// <summary>Prefix for the error message of the exception that gets thrown when a symlink is attempted to be added to drop.</summary>
        internal const string SymlinkAddErrorMessagePrefix = "SymLinks may not be added to drop: ";

        private const string LogFileName = "DropDaemon";

        /// <nodoc/>
        public const string DropDLogPrefix = "(DropD) ";

        /// <summary>Daemon configuration.</summary>
        public DaemonConfig Config { get; }

        /// <summary>Drop configuration.</summary>
        public DropConfig DropConfig { get; }

        /// <summary>Task to wait on for the completion result.</summary>
        public Task Completion => m_server.Completion;

        /// <summary>Name of the drop this daemon is constructing.</summary>
        public string DropName => DropConfig.Name;

        /// <summary>Client for talking to BuildXL.</summary>
        [CanBeNull]
        public Client ApiClient { get; }

        private readonly Task<IDropClient> m_dropClientTask;
        private readonly ICloudBuildLogger m_etwLogger;
        private readonly IServer m_server;
        private readonly IParser m_parser;

        private readonly ILogger m_logger;

        /// <nodoc />
        public ILogger Logger => m_logger;

        /// <nodoc />
        public Daemon(IParser parser, DaemonConfig daemonConfig, DropConfig dropConfig, Task<IDropClient> dropClientTask, IIpcProvider rpcProvider = null, Client client = null)
        {
            Contract.Requires(daemonConfig != null);
            Contract.Requires(dropConfig != null);

            Config = daemonConfig;
            DropConfig = dropConfig;
            m_parser = parser;
            ApiClient = client;
            m_logger = !string.IsNullOrWhiteSpace(dropConfig.LogDir) ? new FileLogger(dropConfig.LogDir, LogFileName, Config.Moniker, dropConfig.Verbose, DropDLogPrefix) : Config.Logger;
            m_logger.Info("Using DropDaemon config: " + JsonConvert.SerializeObject(Config));

            rpcProvider = rpcProvider ?? IpcFactory.GetProvider();
            m_server = rpcProvider.GetServer(Config.Moniker, Config);

            m_etwLogger = new BuildXLBasedCloudBuildLogger(Config.Logger, Config.EnableCloudBuildIntegration);
            m_dropClientTask = dropClientTask ?? Task.Run(() => (IDropClient)new VsoClient(m_logger, dropConfig));
        }

        /// <summary>
        ///     Starts to listen for client connections.  As soon as a connection is received,
        ///     it is placed in an action block from which it is picked up and handled asynchronously
        ///     (in the <see cref="ParseAndExecuteCommand"/> method).
        /// </summary>
        public void Start()
        {
            m_server.Start(this);
        }

        /// <summary>
        ///     Requests shut down, causing this daemon to immediatelly stop listening for TCP/IP
        ///     connections. Any pending requests, however, will be processed to completion.
        /// </summary>
        public void RequestStop()
        {
            m_server.RequestStop();
        }

        /// <summary>
        ///     Calls <see cref="RequestStop"/> then waits for <see cref="Completion"/>.
        /// </summary>
        public Task RequestStopAndWaitForCompletionAsync()
        {
            RequestStop();
            return Completion;
        }

        /// <summary>
        /// Synchronous version of <see cref="CreateAsync"/>
        /// </summary>
        public IIpcResult Create()
        {
            return CreateAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        ///     Creates the drop.  Handles drop-related exceptions by omitting their stack traces.
        ///     In all cases emits an appropriate <see cref="DropCreationEvent"/> indicating the
        ///     result of this operation.
        /// </summary>
        public async Task<IIpcResult> CreateAsync()
        {
            DropCreationEvent dropCreationEvent =
                await SendDropEtwEvent(
                    WrapDropErrorsIntoDropEtwEvent(InternalCreateAsync));

            return dropCreationEvent.Succeeded
                ? IpcResult.Success(Inv("Drop {0} created.", DropName))
                : new IpcResult(IpcResultStatus.ExecutionError, dropCreationEvent.ErrorMessage);
        }

        /// <summary>
        ///     Invokes the 'drop addfile' operation by delegating to <see cref="IDropClient.AddFileAsync"/>.
        ///     Handles drop-related exceptions by omitting their stack traces.
        /// </summary>
        public Task<IIpcResult> AddFileAsync(IDropItem dropItem)
        {
            return AddFileAsync(dropItem, IsSymLinkOrMountPoint);
        }

        internal async Task<IIpcResult> AddFileAsync(IDropItem dropItem, Func<string, bool> symlinkTester)
        {
            Contract.Requires(dropItem != null);

            // Check if the file is a symlink, only if the file exists on disk at this point; if it is a symlink, reject it outright.
            if (File.Exists(dropItem.FullFilePath) && symlinkTester(dropItem.FullFilePath))
            {
                return new IpcResult(IpcResultStatus.ExecutionError, SymlinkAddErrorMessagePrefix + dropItem.FullFilePath);
            }

            return await WrapDropErrorsIntoIpcResult(async () =>
            {
                IDropClient dropClient = await m_dropClientTask;
                AddFileResult result = await dropClient.AddFileAsync(dropItem);
                return IpcResult.Success(Inv(
                    "File '{0}' {1} under '{2}' in drop '{3}'.",
                    dropItem.FullFilePath,
                    result,
                    dropItem.RelativeDropPath,
                    DropName));
            });
        }

        /// <summary>
        /// Gets file's path relative to a given root.
        /// The method assumes that file is under the root; however it does not enforce this assumption.
        /// </summary>
        private string GetRelativePath(string root, string file)
        {
            var rootEndsWithSlash =
                root[root.Length - 1] == Path.DirectorySeparatorChar
                || root[root.Length - 1] == Path.AltDirectorySeparatorChar;
            return file.Substring(root.Length + (rootEndsWithSlash ? 0 : 1));
        }

        /// <summary>
        /// Synchronous version of <see cref="FinalizeAsync"/>
        /// </summary>
        public IIpcResult Finalize()
        {
            return FinalizeAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        ///     Finalizes the drop.  Handles drop-related exceptions by omitting their stack traces.
        ///     In all cases emits an appropriate <see cref="DropFinalizationEvent"/> indicating the
        ///     result of this operation.
        /// </summary>
        public async Task<IIpcResult> FinalizeAsync()
        {
            var dropFinalizationEvent =
                await SendDropEtwEvent(
                    WrapDropErrorsIntoDropEtwEvent(InternalFinalizeAsync));

            return dropFinalizationEvent.Succeeded
                ? IpcResult.Success(Inv("Drop {0} finalized", DropName))
                : new IpcResult(IpcResultStatus.ExecutionError, dropFinalizationEvent.ErrorMessage);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (m_dropClientTask.IsCompleted && !m_dropClientTask.IsFaulted)
            {
                ReportStatisticsAsync().GetAwaiter().GetResult();

                m_dropClientTask.Result.Dispose();
            }

            m_server.Dispose();
            ApiClient?.Dispose();
            m_logger.Dispose();
        }

        /// <summary>
        ///     Invokes the 'drop create' operation by delegating to <see cref="IDropClient.CreateAsync"/>.
        ///
        ///     If successful, returns <see cref="DropCreationEvent"/> with <see cref="DropOperationBaseEvent.Succeeded"/>
        ///     set to true, <see cref="DropCreationEvent.DropExpirationInDays"/> set to drop expiration in days,
        ///     and <see cref="DropOperationBaseEvent.AdditionalInformation"/> set to the textual representation
        ///     of the returned <see cref="DropItem"/> object.
        ///
        ///     Doesn't handle any exceptions.
        /// </summary>
        private async Task<DropCreationEvent> InternalCreateAsync()
        {
            IDropClient dropClient = await m_dropClientTask;
            DropItem dropItem = await dropClient.CreateAsync();
            return new DropCreationEvent()
            {
                Succeeded = true,
                AdditionalInformation = DropItemToString(dropItem),
                DropExpirationInDays = ComputeDropItemExpiration(dropItem),
            };
        }

        /// <summary>
        ///     Invokes the 'drop finalize' operation by delegating to <see cref="IDropClient.FinalizeAsync"/>.
        ///
        ///     If successful, returns <see cref="DropFinalizationEvent"/> with <see cref="DropOperationBaseEvent.Succeeded"/>
        ///     set to true.
        ///
        ///     Doesn't handle any exceptions.
        /// </summary>
        private async Task<DropFinalizationEvent> InternalFinalizeAsync()
        {
            IDropClient dropClient = await m_dropClientTask;
            await dropClient.FinalizeAsync();
            return new DropFinalizationEvent()
            {
                Succeeded = true,
            };
        }

        private async Task ReportStatisticsAsync()
        {
            IDropClient dropClient = await m_dropClientTask;
            var stats = dropClient.GetStats();
            if (stats != null && stats.Any())
            {
                // log stats
                m_logger.Info("Statistics: ");
                m_logger.Info(string.Join(Environment.NewLine, stats.Select(s => s.Key + " = " + s.Value)));

                stats.Add(nameof(DropConfig) + nameof(DropConfig.MaxParallelUploads), DropConfig.MaxParallelUploads);
                stats.Add(nameof(DropConfig) + nameof(DropConfig.NagleTime), (long)DropConfig.NagleTime.TotalMilliseconds);
                stats.Add(nameof(DropConfig) + nameof(DropConfig.BatchSize), DropConfig.BatchSize);
                stats.Add(nameof(DropConfig) + nameof(DropConfig.EnableChunkDedup), DropConfig.EnableChunkDedup ? 1 : 0);
                stats.Add("DaemonConfig" + nameof(Config.MaxConcurrentClients), Config.MaxConcurrentClients);
                stats.Add("DaemonConfig" + nameof(Config.ConnectRetryDelay), (long)Config.ConnectRetryDelay.TotalMilliseconds);
                stats.Add("DaemonConfig" + nameof(Config.MaxConnectRetries), Config.MaxConnectRetries);

                stats.AddRange(m_counters.AsStatistics());

                // report stats to BuildXL (if m_client is specified)
                if (ApiClient != null)
                {
                    var possiblyReported = await ApiClient.ReportStatistics(stats);
                    if (possiblyReported.Succeeded && possiblyReported.Result)
                    {
                        m_logger.Info("Statistics successfully reported to BuildXL.");
                    }
                    else
                    {
                        var errorDescription = possiblyReported.Succeeded ? string.Empty : possiblyReported.Failure.Describe();
                        m_logger.Warning("Reporting stats to BuildXL failed. " + errorDescription);
                    }
                }
            }
            else
            {
                m_logger.Info("No stats recorded by drop client of type " + dropClient.GetType().Name);
            }
        }

        private static Task<IIpcResult> WrapDropErrorsIntoIpcResult(Func<Task<IIpcResult>> factory)
        {
            return HandleKnownErrors(
                factory,
                (errorMessage) => new IpcResult(IpcResultStatus.ExecutionError, errorMessage));
        }

        private static Task<TDropEvent> WrapDropErrorsIntoDropEtwEvent<TDropEvent>(Func<Task<TDropEvent>> factory) where TDropEvent : DropOperationBaseEvent
        {
            return HandleKnownErrors(
                factory,
                (errorMessage) =>
                {
                    var dropEvent = Activator.CreateInstance<TDropEvent>();
                    dropEvent.Succeeded = false;
                    dropEvent.ErrorMessage = errorMessage;
                    return dropEvent;
                });
        }

        private static async Task<TResult> HandleKnownErrors<TResult>(Func<Task<TResult>> factory, Func<string, TResult> errorValueFactory)
        {
            try
            {
                return await factory();
            }
            catch (VssUnauthorizedException e)
            {
                return errorValueFactory("[DROP AUTH ERROR] " + e.Message);
            }
            catch (DropServiceException e)
            {
                return errorValueFactory("[DROP SERVICE ERROR] " + e.Message);
            }
            catch (DropDaemonException e)
            {
                return errorValueFactory("[DROP DAEMON ERROR] " + e.Message);
            }
        }

        private static string DropItemToString(DropItem dropItem)
        {
            try
            {
                return dropItem?.ToJson().ToString();
            }
#pragma warning disable ERP022 // TODO: This should really handle specific errors
            catch
            {
                return null;
            }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler
        }

        private static int ComputeDropItemExpiration(DropItem dropItem)
        {
            DateTime? expirationDate;
            return dropItem.TryGetExpirationTime(out expirationDate) || expirationDate.HasValue
                ? (int)expirationDate.Value.Subtract(DateTime.UtcNow).TotalDays
                : -1;
        }

        private async Task<T> SendDropEtwEvent<T>(Task<T> task) where T : DropOperationBaseEvent
        {
            long startTime = DateTime.UtcNow.Ticks;
            T dropEvent = null;
            try
            {
                dropEvent = await task;
                return dropEvent;
            }
            finally
            {
                // if 'task' failed, create an event indicating an error
                if (dropEvent == null)
                {
                    dropEvent = Activator.CreateInstance<T>();
                    dropEvent.Succeeded = false;
                    dropEvent.ErrorMessage = "internal error";
                }

                // common properties: execution time, drop type, drop url
                dropEvent.ElapsedTimeTicks = DateTime.UtcNow.Ticks - startTime;
                dropEvent.DropType = "VsoDrop";
                if (m_dropClientTask.IsCompleted && !m_dropClientTask.IsFaulted)
                {
                    dropEvent.DropUrl = (await m_dropClientTask).DropUrl;
                }

                // send event
                m_etwLogger.Log(dropEvent);
            }
        }

        private CounterCollection<DaemonCounter> m_counters = new CounterCollection<DaemonCounter>();

        private enum DaemonCounter
        {
            /// <nodoc/>
            [CounterType(CounterType.Stopwatch)]
            ParseArgsDuration,

            /// <nodoc/>
            [CounterType(CounterType.Stopwatch)]
            ServerActionDuration,

            /// <nodoc/>
            QueueDurationMs,
        }

        private async Task<IIpcResult> ParseAndExecuteCommand(IIpcOperation operation)
        {
            string cmdLine = operation.Payload;
            m_logger.Verbose("Command received: {0}", cmdLine);
            ConfiguredCommand conf;
            using (m_counters.StartStopwatch(DaemonCounter.ParseArgsDuration))
            {
                conf = Program.ParseArgs(cmdLine, m_parser);
            }

            IIpcResult result;
            using (m_counters.StartStopwatch(DaemonCounter.ServerActionDuration))
            {
                 result = await conf.Command.ServerAction(conf, this);
            }

            TimeSpan queueDuration = operation.Timestamp.Daemon_BeforeExecuteTime - operation.Timestamp.Daemon_AfterReceivedTime;
            m_counters.AddToCounter(DaemonCounter.QueueDurationMs, (long)queueDuration.TotalMilliseconds);

            return result;
        }

        Task<IIpcResult> IIpcOperationExecutor.ExecuteAsync(IIpcOperation operation)
        {
            Contract.Requires(operation != null);

            return ParseAndExecuteCommand(operation);
        }
    }
}
