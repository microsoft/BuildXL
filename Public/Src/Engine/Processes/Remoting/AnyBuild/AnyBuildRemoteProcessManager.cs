// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#if FEATURE_ANYBUILD_PROCESS_REMOTING

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AnyBuild;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;
using static BuildXL.Utilities.Tracing.CounterCollection;

#nullable enable

namespace BuildXL.Processes.Remoting
{
    /// <summary>
    /// Remoting process manager that uses AnyBuild and AnyBuild.SDK for remoting process pip.
    /// </summary>
    internal class AnyBuildRemoteProcessManager : IRemoteProcessManager
    {
        private readonly IConfiguration m_configuration;
        private readonly PipExecutionContext m_executionContext;
        private readonly LoggingContext m_loggingContext;
        private readonly CounterCollection<SandboxedProcessFactory.SandboxedProcessCounters> m_counters;
        private readonly AsyncLazy<InitResult> m_initResultLazy;

        /// <inheritdoc/>
        public bool IsInitialized { get; private set; }

        private bool m_initializationStarted = false;

        public AnyBuildRemoteProcessManager(
            LoggingContext loggingContext,
            PipExecutionContext executionContext,
            IConfiguration configuration,
            CounterCollection<SandboxedProcessFactory.SandboxedProcessCounters> counters)
        {
            m_loggingContext = loggingContext;
            m_executionContext = executionContext;
            m_configuration = configuration;
            m_counters = counters;
            m_initResultLazy = new AsyncLazy<InitResult>(InitCoreAsync);
        }

        /// <inheritdoc/>
        public async Task<IRemoteProcessPip> CreateAndStartAsync(RemoteProcessInfo processInfo, CancellationToken cancellationToken)
        {
            Contract.Requires(IsInitialized);

            InitResult initResult = await m_initResultLazy.GetValueAsync();

            if (initResult.RemoteProcessFactory == null)
            {
                return new ErrorRemoteProcessPip(initResult.Exception!.ToString());
            }

            IRemoteProcessFactory factory = initResult.RemoteProcessFactory;
            var commandInfo = new RemoteCommandExecutionInfo(
                processInfo.Executable,
                processInfo.Args,
                processInfo.WorkingDirectory,
                useLocalEnvironment: false,
                processInfo.Environments.ToList());

            try
            {
                IRemoteProcess remoteCommand = await factory.CreateAndStartAsync(commandInfo, cancellationToken);
                return new AnyBuildRemoteProcess(remoteCommand);
            }
            catch (Exception e)
            {
                return new ErrorRemoteProcessPip(e.ToString());
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (m_initializationStarted)
            {
                InitResult initResult = m_initResultLazy.GetValueAsync().GetAwaiter().GetResult();
                if (initResult.DaemonManager != null)
                {
                    // Daemon manager was started during initialization, so it must be disposed.
                    initResult.DaemonManager.Dispose();
                }
            }
        }

        /// <inheritdoc/>
        public async Task InitAsync()
        {
            InitResult result = await m_initResultLazy.GetValueAsync();
            if (result.Exception != null)
            {
                throw result.Exception;
            }
        }

        private async Task<InitResult> InitCoreAsync()
        {
            m_initializationStarted = true;

            using Stopwatch _ = m_counters.StartStopwatch(SandboxedProcessFactory.SandboxedProcessCounters.SandboxedPipExecutorInitializingRemoteProcessManager);

            AnyBuildClient abClient;

            using (m_counters.StartStopwatch(SandboxedProcessFactory.SandboxedProcessCounters.SandboxedPipExecutorRemoteProcessManagerFindAnyBuild))
            {
                try
                {
                    Tracing.Logger.Log.FindAnyBuildClient(m_loggingContext, EngineEnvironmentSettings.AnyBuildInstallDir ?? "default");
                    abClient = AnyBuildClient.Find(EngineEnvironmentSettings.AnyBuildInstallDir);
                }
                catch (AnyBuildNotInstalledException e)
                {
                    Tracing.Logger.Log.ExceptionOnFindingAnyBuildClient(m_loggingContext, e.ToString());
                    return new InitResult(
                        null,
                        null,
                        null,
                        new BuildXLException("Failed to remote process because AnyBuild client cannot be found", e));
                }
            }

            Contract.Assert(abClient != null);

            AnyBuildDaemonManager daemonManager;

            using (m_counters.StartStopwatch(SandboxedProcessFactory.SandboxedProcessCounters.SandboxedPipExecutorRemoteProcessManagerStartAnyBuildDaemon))
            {
                try
                {
                    string logDir = m_configuration.Logging.LogsDirectory.ToString(m_executionContext.PathTable);
                    string extraParams = CreateAnyBuildParams();

                    Tracing.Logger.Log.FindOrStartAnyBuildDaemon(m_loggingContext, extraParams, logDir);

                    daemonManager = await abClient.FindOrStartAnyBuildDaemonAsync(
                        closeDaemonOnDispose: true,
                        m_executionContext.CancellationToken,
                        logDirectory: logDir,
                        additionalAnyBuildParameters: extraParams,
                        // TODO: Use available ports instead of the defaults. It may address the issue with /server-.
                        // daemonPort: GetUnusedPort(),
                        // shimPort: GetUnusedPort(),
                        inheritHandlesOnProcessCreation: false);
                }
                catch (Exception e)
                {
                    Tracing.Logger.Log.ExceptionOnFindOrStartAnyBuildDaemon(m_loggingContext, e.ToString());
                    return new InitResult(
                        abClient,
                        null,
                        null,
                        new BuildXLException("Failed to remote process because AnyBuild daemon cannot be found or started", e));
                }
            }

            IRemoteProcessFactory remoteProcessFactory;

            using (m_counters.StartStopwatch(SandboxedProcessFactory.SandboxedProcessCounters.SandboxedPipExecutorRemoteProcessManagerGetAnyBuildRemoteFactory))
            {
                try
                {
                    remoteProcessFactory = abClient.GetRemoteProcessFactory();
                }
                catch (Exception e)
                {
                    Tracing.Logger.Log.ExceptionOnGetAnyBuildRemoteProcessFactory(m_loggingContext, e.ToString());
                    return new InitResult(
                        abClient,
                        daemonManager,
                        null,
                        new BuildXLException("Failed to remote process because AnyBuild remote process factory cannot be obtained", e));
                }
            }

            IsInitialized = true;

            return new InitResult(abClient, daemonManager, remoteProcessFactory, null);
        }

        private string CreateAnyBuildParams()
        {
            string localCacheDir = m_configuration.Layout.CacheDirectory.Combine(m_executionContext.PathTable, "AnyBuildLocalCache").ToString(m_executionContext.PathTable);
            var jsonConfig = new List<string>()
            {
                "ProcessSubstitution.MaxParallelLocalExecutionsFactor=0",
                "Run.DisableDirectoryMetadataDedup=true",
                $"Agents.AgentSearchTimeoutSeconds={m_configuration.Schedule.RemoteAgentWaitTimeSec}"
            };

            string jsonConfigOverrides = string.Join(" ", jsonConfig);

            var args = new List<string>()
            {
                $"--JsonConfigOverrides {jsonConfigOverrides}",
                "--DisableActionCache",
                "--RemoteAll",
                "--DoNotUseMachineUtilizationForScheduling",
                "--NoSandboxingBuildEngine",
                $"--CacheDir {localCacheDir}",
            };

            string extraArgs = EngineEnvironmentSettings.AnyBuildExtraArgs;
            if (!string.IsNullOrEmpty(extraArgs))
            {
                extraArgs = extraArgs.Replace("~~", " ").Replace("!!", "\"");
                args.Add(extraArgs);
            }

            return string.Join(" ", args);
        }

        /// <inheritdoc/>
        public IRemoteProcessManagerInstaller? GetInstaller() => new AnyBuildInstaller(m_loggingContext);

        // private static int GetUnusedPort()
        // {
        //     var listener = new TcpListener(IPAddress.Loopback, 0);
        //     listener.Start();
        //     int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        //     listener.Stop();
        //     return port;
        // }

        private record InitResult(AnyBuildClient? AbClient, AnyBuildDaemonManager? DaemonManager, IRemoteProcessFactory? RemoteProcessFactory, BuildXLException? Exception);
    }
}

#endif
