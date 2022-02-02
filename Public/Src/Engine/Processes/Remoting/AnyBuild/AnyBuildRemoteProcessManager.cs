// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// #define FEATURE_ANYBUILD_PROCESS_REMOTING
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
        private bool m_isDaemonStarted;

        /// <inheritdoc/>
        public bool IsInitialized { get; private set; }

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

            IRemoteProcessFactory factory = (await m_initResultLazy.GetValueAsync()).RemoteProcessFactory;
            var commandInfo = new RemoteCommandExecutionInfo(
                processInfo.Executable,
                processInfo.Args,
                processInfo.WorkingDirectory,
                useLocalEnvironment: false,
                processInfo.Environments.ToList());

            IRemoteProcess remoteCommand = await factory.CreateAndStartAsync(commandInfo, cancellationToken);

            return new AnyBuildRemoteProcess(remoteCommand);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (m_isDaemonStarted)
            {
                m_initResultLazy.GetValueAsync().GetAwaiter().GetResult().DaemonManager.Dispose();
            }
        }

        /// <inheritdoc/>
        public Task InitAsync()
        {
            return m_initResultLazy.GetValueAsync();
        }

        private async Task<InitResult> InitCoreAsync()
        {
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
                    throw new BuildXLException("Failed to remote process because AnyBuild client cannot be found", e);
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
                        inheritHandlesOnProcessCreation: false);
                }
                catch (Exception e)
                {
                    Tracing.Logger.Log.ExceptionOnFindOrStartAnyBuildDaemon(m_loggingContext, e.ToString());

                    throw new BuildXLException("Failed to remote process because AnyBuild daemon cannot be found or started");
                }
            }

            m_isDaemonStarted = true;

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

                    throw new BuildXLException("Failed to remote process because AnyBuild remote process factory cannot be obtained");
                }
            }

            IsInitialized = true;

            return new InitResult(abClient, daemonManager, remoteProcessFactory);
        }

        private string CreateAnyBuildParams()
        {
            string localCacheDir = m_configuration.Layout.CacheDirectory.Combine(m_executionContext.PathTable, "AnyBuildLocalCache").ToString(m_executionContext.PathTable);
            var args = new List<string>()
            {
                "--JsonConfigOverrides ProcessSubstitution.MaxParallelLocalExecutionsFactor=0 Run.DisableDirectoryMetadataDedup=true",
                "--DisableActionCache",
                "--RemoteAll",
                "--Verbose",
                "--DoNotUseMachineUtilizationForScheduling",
                "--NoSandboxingBuildEngine",
                $"--CacheDir {localCacheDir}",
            };

            if (!string.IsNullOrEmpty(EngineEnvironmentSettings.AnyBuildServicePrincipalAppId))
            {
                args.Add("--ClientApplicationId");
                args.Add(EngineEnvironmentSettings.AnyBuildServicePrincipalAppId);

                if (string.IsNullOrEmpty(EngineEnvironmentSettings.AnyBuildServicePrincipalPwdEnv))
                {
                    throw new BuildXLException($"Service principal password is required for starting AnyBuild daemon");
                }

                args.Add("--ClientSecretEnvironmentVariable");
                args.Add(EngineEnvironmentSettings.AnyBuildServicePrincipalPwdEnv);
            }

            if (!string.IsNullOrEmpty(m_configuration.Schedule.RemoteExecutionServiceUri))
            {
                try
                {
                    var uri = new Uri(m_configuration.Schedule.RemoteExecutionServiceUri);
                    if (uri.IsLoopback)
                    {
                        args.Add("--Loopback");
                        args.Add("--WaitForAgentForever");
                    }
                    else
                    {
                        args.Add($"--RemoteExecServiceUri {uri}");
                    }
                }
                catch (UriFormatException e)
                {
                    throw new BuildXLException(
                        $"Invalid {nameof(m_configuration.Schedule.RemoteExecutionServiceUri)}: {m_configuration.Schedule.RemoteExecutionServiceUri}",
                        e);
                }
            }

            args.Add(EngineEnvironmentSettings.AnyBuildExtraArgs ?? string.Empty);

            return string.Join(" ", args);
        }

        private record InitResult(AnyBuildClient AbClient, AnyBuildDaemonManager DaemonManager, IRemoteProcessFactory RemoteProcessFactory);
    }
}

#endif
