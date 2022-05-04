// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#if FEATURE_ANYBUILD_PROCESS_REMOTING

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AnyBuild;
using BuildXL.Native.IO;
using BuildXL.Pips.Operations;
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
        private readonly List<AbsolutePath> m_staticDirectories = new ();
        private readonly string m_remoteManagerDirectory;
        private readonly IRemoteFilePredictor m_filePredictor;

        /// <inheritdoc/>
        public bool IsInitialized { get; private set; }

        private bool m_initializationStarted = false;

        public AnyBuildRemoteProcessManager(
            LoggingContext loggingContext,
            PipExecutionContext executionContext,
            IConfiguration configuration,
            IRemoteFilePredictor filePredictor,
            CounterCollection<SandboxedProcessFactory.SandboxedProcessCounters> counters)
        {
            m_loggingContext = loggingContext;
            m_executionContext = executionContext;
            m_configuration = configuration;
            m_counters = counters;
            m_filePredictor = filePredictor;
            m_remoteManagerDirectory = Path.Combine(m_configuration.Layout.ExternalSandboxedProcessDirectory.ToString(m_executionContext.PathTable), nameof(AnyBuildRemoteProcessManager));
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

            Directory.CreateDirectory(m_remoteManagerDirectory);

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

            // Ensure local cache dir exists.
            if (!Directory.Exists(localCacheDir))
            {
                Directory.CreateDirectory(localCacheDir);
            }

            // Due to its asynchronous nature in shutting down AnyBuild daemon, the subst may have already gone when AnyBuild daemon is shutting down the CAS.
            // If we pass substed path as local cache dir, then the CAS may no longer find the cache dir anymore during shutdown.
            // To address this issue, we pass the fully-resolved local cache directory path to AnyBuild daemon.
            localCacheDir = FileUtilities.TryGetFinalPathNameByPath(localCacheDir, out string finalPath, out int _) ? finalPath : localCacheDir;

            var args = new List<string>()
            {
                $"--JsonConfigOverrides @\"{CreateJsonConfigOverrides()}\"",
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

        private string CreateJsonConfigOverrides()
        {
            string substSource = m_configuration.Logging.SubstSource.IsValid
                ? m_configuration.Logging.SubstSource.ToString(m_executionContext.PathTable)
                : string.Empty;
            string substTarget = m_configuration.Logging.SubstTarget.IsValid
                ? m_configuration.Logging.SubstTarget.ToString(m_executionContext.PathTable)
                : string.Empty;

            var jsonConfigOverridesObj = new
            {
                ProcessSubstitution = new
                {
                    MaxParallelLocalExecutionsFactor = 0
                },
                Run = new
                {
                    DisableDirectoryMetadataDedup = true,

                    // FUTURE OPTIONS: Only enabled when they are available in AnyBuild.

                    // Using PLACEHOLDER for pre-rendering because using COPY causes error like:
                    //   error CS1504: Source file 'D:\a\_work\5\s\Public\Src\FrontEnd\MsBuild.Serialization\GraphSerializationSettings.cs' could not be opened
                    //   -- The process cannot access the file because another process has locked a portion of the file.
                    PreRenderingMode = "Placeholder", // "COPY",
                    Substs = !string.IsNullOrEmpty(substSource) && !string.IsNullOrEmpty(substTarget) ? $"{substSource};{substTarget}" : string.Empty
                },
                Agents = new
                {
                    AgentSearchTimeoutSeconds = m_configuration.Schedule.RemoteAgentWaitTimeSec
                },
                StaticDirs = new
                {
                    DisablePostBuildAnalysis = true,
                    Windows = OperatingSystemHelper.IsWindowsOS 
                        ? m_staticDirectories.Select(p => p.ToString(m_executionContext.PathTable)).ToArray()
                        : Array.Empty<string>(),
                    Linux = OperatingSystemHelper.IsLinuxOS
                        ? m_staticDirectories.Select(p => p.ToString(m_executionContext.PathTable)).ToArray()
                        : Array.Empty<string>()
                },
                ActionCache = new
                {
                    // TODO: Need to evaluate if we really need this big.
                    MaxUploadSizeBytes = 1_500_000_000
                }
            };

            string jsonConfigOverrides = Newtonsoft.Json.JsonConvert.SerializeObject(jsonConfigOverridesObj, Newtonsoft.Json.Formatting.Indented);
            string jsonConfigOverridesFile = Path.Combine(m_remoteManagerDirectory, "AnyBuildRepoConfigOverrides.json");

            // TODO: Change to File.WriteAllTextAsync when moving completely from NET framework.
            File.WriteAllText(jsonConfigOverridesFile, jsonConfigOverrides);

            Tracing.Logger.Log.AnyBuildRepoConfigOverrides(m_loggingContext, Environment.NewLine + jsonConfigOverrides);

            return jsonConfigOverridesFile;
        }

        /// <inheritdoc/>
        public IRemoteProcessManagerInstaller? GetInstaller() => new AnyBuildInstaller(m_loggingContext);

        /// <inheritdoc/>
        public void RegisterStaticDirectories(IEnumerable<AbsolutePath> staticDirectories) => m_staticDirectories.AddRange(staticDirectories);

        /// <inheritdoc/>
        public Task<IEnumerable<AbsolutePath>> GetInputPredictionAsync(Process process) => m_filePredictor.GetInputPredictionAsync(process);

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
