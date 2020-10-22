// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Launcher.Server;
using BuildXL.Processes;
using BuildXL.Utilities.ParallelAlgorithms;
using BuildXL.Utilities.Tasks;
using Microsoft.Practices.TransientFaultHandling;
using Microsoft.Win32.SafeHandles;
using static BuildXL.Cache.Host.Configuration.DeploymentManifest;

namespace BuildXL.Cache.Host.Service
{
    /// <summary>
    /// Deploys drops/files from a given deployment configuration to a CAS store and writes manifests describing contents
    /// so that subsequent process (i.e. Deployment Service) can read files and proffer deployments to clients.
    /// </summary>
    public class DeploymentLauncher : StartupShutdownBase
    {
        /// <summary>
        /// For testing purposes only.
        /// </summary>
        public static IDeploymentLauncherHost OverrideHost { get; set; }

        #region Configuration

        /// <summary>
        /// The deployment root directory under which CAS and deployments will be stored
        /// </summary>
        public DisposableDirectory DeploymentDirectory { get; }

        private LauncherSettings Settings { get; }

        #endregion

        private ActionQueue DownloadQueue { get; }

        private IAbsFileSystem FileSystem { get; }

        /// <summary>
        /// Content store used to store files in content addressable layout under deployment root
        /// </summary>
        private FileSystemContentStoreInternal Store { get; }

        protected override Tracer Tracer { get; } = new Tracer(nameof(DeploymentLauncher));

        private DeployedTool _currentRun;

        private readonly SemaphoreSlim _mutex = TaskUtilities.CreateMutex();

        public IDeployedTool CurrentRun => _currentRun;

        private RetryPolicy RetryPolicy { get; }

        /// <summary>
        /// Lifetime manager used to signal shutdown of launched services
        /// </summary>
        public ServiceLifetimeManager LifetimeManager { get; }

        /// <summary>
        /// For testing purposes only. Used to intercept launch of drop.exe process and run custom logic in its place
        /// </summary>
        public Func<(string exePath, string args, string dropUrl, string targetDirectory, string relativeRoot), BoolResult> OverrideLaunchDropProcess { get; set; }

        private readonly IDeploymentLauncherHost _host;

        /// <nodoc />
        public DeploymentLauncher(
            LauncherSettings settings,
            IAbsFileSystem fileSystem,
            IDeploymentLauncherHost host = null)
        {
            Settings = settings;
            var targetDirectory = new AbsolutePath(settings.TargetDirectory);
            DeploymentDirectory = new DisposableDirectory(fileSystem, targetDirectory / "bin");
            _host = host ?? OverrideHost ?? DeploymentLauncherHost.Instance;

            LifetimeManager = new ServiceLifetimeManager(DeploymentDirectory.Path / "lifetime", TimeSpan.FromSeconds(Settings.ServiceLifetimePollingIntervalSeconds));

            Store = new FileSystemContentStoreInternal(
                fileSystem,
                SystemClock.Instance,
                DeploymentUtilities.GetCasRootPath(targetDirectory),
                new ConfigurationModel(new ContentStoreConfiguration(new MaxSizeQuota($"{settings.RetentionSizeGb}GB"))),
                settings: new ContentStoreSettings()
                {
                    TraceFileSystemContentStoreDiagnosticMessages = true,

                    // Disable empty file shortcuts to ensure all content is always placed on disk
                    UseEmptyContentShortcut = false
                });

            FileSystem = fileSystem;

            DownloadQueue = new ActionQueue(settings.DownloadConcurrency);
        }

        /// <summary>
        /// Uploads the deployment files to the target storage account and returns the launcher manifest for the given deployment parameters
        /// </summary>
        public Task<BoolResult> RunAsync(OperationContext context)
        {
            return WithOperationContext(context, context.Token, ctx => ctx.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    while (!ctx.Token.IsCancellationRequested)
                    {
                        using var timeoutSource = new CancellationTokenSource();
                        timeoutSource.CancelAfter(Settings.DeployTimeout);

                        using var timeoutContext = ctx.WithCancellationToken(timeoutSource.Token);

                        await GetDownloadAndRunDeployment(timeoutContext).IgnoreFailure();

                        // Wait before querying for deployment updates again
                        await Task.Delay(TimeSpan.FromSeconds(Settings.QueryIntervalSeconds));
                    }

                    return BoolResult.Success;
                }));
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            var result = await Store.StartupAsync(context);

            if (Settings.CreateJobObject && JobObject.OSSupportsNestedJobs)
            {
                JobObject.SetTerminateOnCloseOnCurrentProcessJob();
            }

            if (Settings.RunInBackgroundOnStartup)
            {
                RunAsync(context).IgnoreTaskResult();
            }

            return result;
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            using var releaser = await _mutex.AcquireAsync(context.Token);

            var success = BoolResult.Success;
            if (_currentRun != null)
            {
                success &= await _currentRun.ShutdownAsync(context);
            }

            DeploymentDirectory.Dispose();

            return success & await Store.ShutdownAsync(context);
        }

        public Task<BoolResult> GetDownloadAndRunDeployment(OperationContext context)
        {
            context = context.CreateNested(Tracer.Name);

            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    context.Token.ThrowIfCancellationRequested();

                    using var releaser = await _mutex.AcquireAsync(context.Token);

                    using var client = _host.CreateServiceClient();

                    // Get the launch manifest with only content id populated
                    var manifest = await GetLaunchManifestAsync(context, client, getContentIdOnly: true).ThrowIfFailureAsync();
                    if (manifest.ContentId == _currentRun?.Manifest.ContentId && _currentRun.IsActive)
                    {
                        return BoolResult.WithSuccessMessage($"Skipped because retrieved content id match matches active run. Id={manifest.ContentId}");
                    }

                    // The content has changed from the active run. Get full manifest.
                    manifest = await GetLaunchManifestAsync(context, client, getContentIdOnly: false).ThrowIfFailureAsync();
                    if (!manifest.IsComplete)
                    {
                        return BoolResult.WithSuccessMessage($"Skipped because manifest ingestion is not complete. Id={manifest.ContentId}");
                    }

                    var hashes = manifest.Deployment.Select(f => new ContentHash(f.Value.Hash)).Distinct().ToList();

                    var deploymentTargetDirectoryPath =
                        Settings.OverrideServiceDeploymentLocation ??
                        (DeploymentDirectory.Path / manifest.Tool.ServiceId / $"{DateTime.Now.ToReadableString()}_{manifest.ContentId}").Path;

                    if (_currentRun != null && Settings.OverrideServiceDeploymentLocation != null)
                    {
                        // Stop the currently active run
                        await _currentRun.ShutdownAsync(context).IgnoreFailure();
                        _currentRun = null;
                    }

                    var directory = new DisposableDirectory(FileSystem, new AbsolutePath(deploymentTargetDirectoryPath));
                    var deployedTool = new DeployedTool(this, manifest, directory, Store.CreatePinContext());

                    var pinResults = await Store.PinAsync(context, hashes, pinContext: deployedTool.PinRequest.PinContext, options: null);

                    var result = await DownloadAndDeployAsync(context, client, deployedTool);
                    if (!result)
                    {
                        directory.Dispose();
                        return result;
                    }

                    if (_currentRun != null)
                    {
                        // Stop the currently active run
                        await _currentRun.ShutdownAsync(context).IgnoreFailure();
                        _currentRun = null;
                    }

                    // Start up the tool
                    var startResult = await deployedTool.StartupAsync(context);
                    if (startResult)
                    {
                        _currentRun = deployedTool;
                    }
                    else
                    {
                        await deployedTool.ShutdownAsync(context).IgnoreFailure();
                    }

                    return startResult;
                });
        }

        private Task<Result<LauncherManifest>> GetLaunchManifestAsync(OperationContext context, IDeploymentServiceClient client, bool getContentIdOnly)
        {
            return context.PerformOperationAsync(Tracer, async () =>
            {
                // Set the trace id
                Settings.DeploymentParameters.ContextId = context.TracingContext.Id;
                Settings.DeploymentParameters.GetContentIdOnly = getContentIdOnly;

                // Query for launcher manifest from remote service
                var manifest = await client.GetLaunchManifestAsync(context, Settings);
                return Result.Success(manifest);
            });
        }

        /// <summary>
        /// Download and store a single drop to CAS
        /// </summary>
        private Task<BoolResult> DownloadAndDeployAsync(OperationContext context, IDeploymentServiceClient client, DeployedTool deploymentInfo)
        {
            var manifest = deploymentInfo.Manifest;

            return context.PerformOperationWithTimeoutAsync<BoolResult>(Tracer, async context =>
            {
                // Stores files into CAS and populate file specs with hash and size info
                var results = await DownloadQueue.SelectAsync(manifest.Deployment.GroupBy(kvp => kvp.Value.Hash), (filesByHash, index) =>
                {
                    var fileInfo = filesByHash.First().Value;
                    var file = filesByHash.First().Key;
                    var count = filesByHash.Count();

                    context.Token.ThrowIfCancellationRequested();

                    return context.PerformOperationAsync(
                        Tracer,
                        async () =>
                        {
                            var hash = new ContentHash(filesByHash.Key);

                            // Hash is not pinned. Need to download into cache
                            if (!deploymentInfo.PinRequest.PinContext.Contains(hash))
                            {
                                // Download the file matching the hash
                                await DownloadFileAsync(context, client, fileInfo, deploymentInfo, file);
                            }

                            // Copy the file to additional deployment locations
                            foreach (var additionalFile in filesByHash.Select(kvp => deploymentInfo.Directory.Path / kvp.Key))
                            {
                                await Store.PlaceFileAsync(
                                    context,
                                    hash,
                                    additionalFile,
                                    FileAccessMode.ReadOnly,
                                    FileReplacementMode.ReplaceExisting,
                                    FileRealizationMode.Any,
                                    deploymentInfo.PinRequest).ThrowIfFailureAsync();

                            }

                            return BoolResult.Success;
                        },
                        extraEndMessage: r => $"Hash={filesByHash.Key}, FirstFile={file}");
                });

                return results.FirstOrDefault(r => !r.Succeeded) ?? BoolResult.Success;
            },
            extraStartMessage: $"Id={manifest.ContentId}, Files={manifest.Deployment.Count}",
            extraEndMessage: r => $"Id={manifest.ContentId}, Files={manifest.Deployment.Count}");
        }

        private Task DownloadFileAsync(OperationContext context, IDeploymentServiceClient client, FileSpec fileInfo, DeployedTool deploymentInfo, string firstFile)
        {
            return context.PerformOperationAsync(Tracer, async () =>
            {
                var hash = new ContentHash(fileInfo.Hash);

                using (var downloadStream = await client.GetStreamAsync(context, fileInfo.DownloadUrl))
                {
                    await Store.PutStreamAsync(
                        context,
                        downloadStream,
                        hash,
                        deploymentInfo.PinRequest).ThrowIfFailureAsync();
                }

                return BoolResult.Success;
            },
            extraStartMessage: $"Hash={fileInfo.Hash}, Size={fileInfo.Size}, FirstTarget={firstFile}, Folder={deploymentInfo.Directory.Path}",
            extraEndMessage: r => $"Hash={fileInfo.Hash}, Size={fileInfo.Size}, FirstTarget={firstFile}, Folder={deploymentInfo.Directory.Path}"
            ).ThrowIfFailureAsync();
        }

        /// <summary>
        /// Describes location of a tool deployment with ability to run the tool
        /// </summary>
        private class DeployedTool : StartupShutdownSlimBase, IDeployedTool
        {
            private readonly SemaphoreSlim _mutex = TaskUtilities.CreateMutex();

            /// <summary>
            /// The active process for the tool
            /// </summary>
            public ILauncherProcess RunningProcess { get; private set; }

            private TaskSourceSlim<bool> ProcessExitSource { get; set; }

            /// <summary>
            /// Gets whether the tool process is running
            /// </summary>
            public bool IsActive => !RunningProcess?.HasExited ?? false;

            /// <summary>
            /// The launcher manifest used to create tool deployment
            /// </summary>
            public LauncherManifest Manifest { get; }

            /// <summary>
            /// The directory containing the tool deployment
            /// </summary>
            public DisposableDirectory Directory { get; }

            private DeploymentLauncher Launcher { get; }
            public PinRequest PinRequest { get; set; }

            public AbsolutePath DirectoryPath => Directory.Path;

            protected override Tracer Tracer { get; } = new Tracer(nameof(DeployedTool));

            public DeployedTool(DeploymentLauncher launcher, LauncherManifest manifest, DisposableDirectory directory, PinContext pinContext)
            {
                Launcher = launcher;
                Manifest = manifest;
                Directory = directory;
                PinRequest = new PinRequest(pinContext);
            }

            /// <summary>
            /// Starts the tool. Assumes tool has already been deployed.
            /// </summary>
            protected override Task<BoolResult> StartupCoreAsync(OperationContext context)
            {
                return context.PerformOperationAsync(
                    Tracer,
                    async () =>
                    {
                        // TODO: Wait for health signal?
                        //       Or maybe process should terminate itself if its not healthy?
                        using (await _mutex.AcquireAsync(context.Token))
                        {
                            if (RunningProcess == null)
                            {
                                RunningProcess = Launcher._host.CreateProcess(new ProcessStartInfo()
                                {
                                    UseShellExecute = false,

                                    FileName = (Directory.Path / Manifest.Tool.Executable).Path,
                                    Arguments = string.Join(" ", Manifest.Tool.Arguments.Select(arg => QuoteArgumentIfNecessary(ExpandTokens(arg)))),
                                    Environment =
                                        {
                                            Launcher.Settings.DeploymentParameters.ToEnvironment(),
                                            Manifest.Tool.EnvironmentVariables.ToDictionary(kvp => kvp.Key, kvp => ExpandTokens(kvp.Value)),
                                            Launcher.LifetimeManager.GetDeployedInterruptableServiceVariables(Manifest.Tool.ServiceId)
                                        }
                                }); ;

                                ProcessExitSource = TaskSourceSlim.Create<bool>();
                                RunningProcess.Exited += () =>
                                {
                                    context.PerformOperation(
                                        Launcher.Tracer,
                                        () =>
                                        {
                                            return Result.Success(ProcessExitSource.TrySetResult(true));
                                        },
                                        caller: "ServiceExited",
                                        messageFactory: r => $"ProcessId={RunningProcess.Id}, ServiceId={Manifest.Tool.ServiceId}, ExitCode={RunningProcess.ExitCode}").IgnoreFailure();
                                };

                                RunningProcess.Start(context);
                            }

                            return BoolResult.Success;
                        }
                    },
                    traceOperationStarted: true,
                    extraStartMessage: $"ServiceId={Manifest.Tool.ServiceId}",
                    extraEndMessage: r => $"ProcessId={RunningProcess?.Id ?? -1}, ServiceId={Manifest.Tool.ServiceId}");
            }

            private static string QuoteArgumentIfNecessary(string arg)
            {
                return arg.Contains(" ") ? $"\"{arg}\"" : arg;
            }

            private string ExpandTokens(string value)
            {
                value = ExpandToken(value, "ServiceDir", DirectoryPath.Path);
                return value;
            }

            private static string ExpandToken(string value, string tokenName, string tokenValue)
            {
                if (!string.IsNullOrEmpty(tokenValue))
                {
                    return Regex.Replace(value, Regex.Escape($"%{tokenName}%"), tokenValue, RegexOptions.IgnoreCase);
                }

                return value;
            }

            /// <summary>
            /// Terminates the tool process
            /// </summary>
            protected override Task<BoolResult> ShutdownCoreAsync(OperationContext context)
            {
                var tool = Manifest.Tool;

                return context.PerformOperationWithTimeoutAsync<BoolResult>(
                    Tracer,
                    async nestedContext =>
                    {
                        using (await _mutex.AcquireAsync(context.Token))
                        {
                            try
                            {
                                if (RunningProcess != null && !RunningProcess.HasExited)
                                {
                                    using var registration = nestedContext.Token.Register(() =>
                                    {
                                        TerminateService(context);
                                        ProcessExitSource.TrySetCanceled();
                                    });

                                    await context.PerformOperationAsync(
                                        Tracer,
                                        async () =>
                                        {
                                            await Launcher.LifetimeManager.ShutdownServiceAsync(nestedContext, tool.ServiceId);
                                            return BoolResult.Success;
                                        },
                                        caller: "GracefulShutdownService").IgnoreFailure();

                                    await ProcessExitSource.Task;
                                }
                            }
                            finally
                            {
                                await PinRequest.PinContext.DisposeAsync();

                                Directory.Dispose();
                            }

                            return BoolResult.Success;
                        }
                    },
                    timeout: TimeSpan.FromSeconds(tool.ShutdownTimeoutSeconds),
                    extraStartMessage: $"ProcessId={RunningProcess?.Id}, ServiceId={Manifest.Tool.ServiceId}",
                    extraEndMessage: r => $"ProcessId={RunningProcess?.Id}, ServiceId={Manifest.Tool.ServiceId}");
            }

            /// <summary>
            /// Terminates the service by killing the process
            /// </summary>
            private void TerminateService(OperationContext context)
            {
                context.PerformOperation(
                    Tracer,
                    () =>
                    {
                        RunningProcess.Kill(context);
                        return BoolResult.Success;
                    },
                    extraStartMessage: $"ProcessId={RunningProcess?.Id}, ServiceId={Manifest.Tool.ServiceId}",
                    messageFactory: r => $"ProcessId={RunningProcess?.Id}, ServiceId={Manifest.Tool.ServiceId}").IgnoreFailure();
            }
        }
    }
}
