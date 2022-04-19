// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.Host.Service.Internal;
using BuildXL.Utilities.ConfigurationHelpers;

#nullable enable

namespace BuildXL.Cache.Host.Service.OutOfProc
{
    /// <summary>
    /// A delegate used to request the teardown of the current process.
    /// </summary>
    public delegate void RequestTeardownDelegate(OperationContext context, string reason);

    /// <summary>
    /// A helper class that "wraps" an out-of-proc cache service.
    /// </summary>
    public class CacheServiceWrapper : StartupShutdownBase
    {
        private readonly CacheServiceWrapperConfiguration _configuration;
        private readonly ServiceLifetimeManager _serviceLifetimeManager;
        private readonly RetrievedSecrets _secrets;
        private readonly IDeploymentLauncherHost _host;
        private readonly RequestTeardownDelegate _requestTeardownDelegate;
        private IDisposable? _interProcessSecretsCommunicator;

        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(CacheServiceWrapper));

        private LauncherManagedProcess? _runningProcess;

        public CacheServiceWrapper(
            CacheServiceWrapperConfiguration configuration,
            ServiceLifetimeManager serviceLifetimeManager,
            RetrievedSecrets secrets,
            IDeploymentLauncherHost host,
            RequestTeardownDelegate requestTeardownDelegate)
        {
            _configuration = configuration;
            _serviceLifetimeManager = serviceLifetimeManager;
            _secrets = secrets;
            _host = host;
            _requestTeardownDelegate = requestTeardownDelegate;
        }

        /// <summary>
        /// True when the instance is ready for use.
        /// </summary>
        [MemberNotNullWhen(true, nameof(LaunchedProcess))]
        public bool Ready => StartupCompleted && !ShutdownStarted;

        /// <summary>
        /// Returns a process that was launched by this instance.
        /// Not null when startup completed.
        /// </summary>
        public ILauncherProcess? LaunchedProcess => _runningProcess?.Process;

        /// <summary>
        /// Creates <see cref="CacheServiceWrapper"/> from <paramref name="configuration"/>.
        /// </summary>
        public static async Task<Result<CacheServiceWrapper>> CreateAsync(DistributedCacheServiceArguments configuration, IDeploymentLauncherHost launcher, RequestTeardownDelegate requestTeardownDelegate)
        {
            // Validating the cache configuration
            
            var wrapperConfiguration = tryCreateConfiguration(configuration);
            if (!wrapperConfiguration.Succeeded)
            {
                const string BaseError = "Can't start cache service as a separate process because";
                return Result.FromErrorMessage<CacheServiceWrapper>($"{BaseError} {wrapperConfiguration.ErrorMessage}");
            }

            // Obtaining the secrets and creating a wrapper.
            var serviceLifetimeManager = new ServiceLifetimeManager(wrapperConfiguration.Value.WorkingDirectory, wrapperConfiguration.Value.ServiceLifetimePollingInterval);
            var secretsRetriever = new DistributedCacheSecretRetriever(configuration);

            var secrets = await secretsRetriever.TryRetrieveSecretsAsync();
            if (!secrets.Succeeded)
            {
                return new Result<CacheServiceWrapper>(secrets);
            }

            return Result.Success(new CacheServiceWrapper(wrapperConfiguration.Value, serviceLifetimeManager, secrets.Value, launcher, requestTeardownDelegate));

            // Creating final configuration based on provided settings and by using reasonable defaults.
            static Result<CacheServiceWrapperConfiguration> tryCreateConfiguration(DistributedCacheServiceArguments configuration)
            {
                var outOfProcSettings = configuration.Configuration.DistributedContentSettings.OutOfProcCacheSettings;

                if (outOfProcSettings is null)
                {
                    return Result.FromErrorMessage<CacheServiceWrapperConfiguration>($"{nameof(configuration.Configuration.DistributedContentSettings.OutOfProcCacheSettings)} should not be null.");
                }

                if (outOfProcSettings.Executable is null)
                {
                    return Result.FromErrorMessage<CacheServiceWrapperConfiguration>($"{nameof(outOfProcSettings.Executable)} is null.");
                }

                if (!File.Exists(outOfProcSettings.Executable))
                {
                    // This is not a bullet proof check, but if the executable is not found we should not even trying to create an out of proc cache service.
                    return Result.FromErrorMessage<CacheServiceWrapperConfiguration>($"the executable is not found at '{outOfProcSettings.Executable}'.");
                }

                if (outOfProcSettings.CacheConfigPath is null)
                {
                    return Result.FromErrorMessage<CacheServiceWrapperConfiguration>($"{nameof(outOfProcSettings.CacheConfigPath)} is null.");
                }

                if (!File.Exists(outOfProcSettings.CacheConfigPath))
                {
                    // This is not a bullet proof check, but if the executable is not found we should not even trying to create an out of proc cache service.
                    return Result.FromErrorMessage<CacheServiceWrapperConfiguration>($"the cache configuration is not found at '{outOfProcSettings.CacheConfigPath}'.");
                }

                // The next layout should be in sync with CloudBuild.
                AbsolutePath executable = getExecutingPath() / outOfProcSettings.Executable;
                var workingDirectory = getRootPath(configuration.Configuration);

                var hostParameters = HostParameters.FromTelemetryProvider(configuration.TelemetryFieldsProvider);

                var resultingConfiguration = new CacheServiceWrapperConfiguration(
                    serviceId: CacheServiceWrapperConfiguration.CacheServiceId,
                    executable: executable,
                    workingDirectory: workingDirectory,
                    hostParameters: hostParameters,
                    cacheConfigPath: new AbsolutePath(outOfProcSettings.CacheConfigPath),
                    // DataRootPath is set in CloudBuild and we need to propagate this configuration to the launched process.
                    dataRootPath: new AbsolutePath(configuration.Configuration.DataRootPath),
                    useInterProcSecretsCommunication: outOfProcSettings.UseInterProcSecretsCommunication,
                    outOfProcSettings.EnvironmentVariables);

                outOfProcSettings.ServiceLifetimePollingInterval.ApplyIfNotNull(v => resultingConfiguration.ServiceLifetimePollingInterval = v.Value);
                outOfProcSettings.GracefulShutdownTimeout.ApplyIfNotNull(v => resultingConfiguration.ShutdownTimeout = v.Value);
                outOfProcSettings.KillTimeout.ApplyIfNotNull(v => resultingConfiguration.ProcessTerminationTimeout = v.Value);

                return resultingConfiguration;
            }

            static AbsolutePath getRootPath(DistributedCacheServiceConfiguration configuration) => configuration.LocalCasSettings.GetCacheRootPathWithScenario(LocalCasServiceSettings.DefaultCacheName);

            static AbsolutePath getExecutingPath() => new AbsolutePath(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);
        }

        /// <inheritdoc />
        protected override Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            var executablePath = _configuration.Executable.Path;
            if (!File.Exists(executablePath))
            {
                return Task.FromResult(new BoolResult($"Executable '{executablePath}' does not exist."));
            }

            // Need to specify through the arguments what type of secrets provider to use.
            // Currently we serialize all the secrets as a single string.
            var secretsProviderKind = _configuration.UseInterProcSecretsCommunication
                ? CrossProcessSecretsCommunicationKind.MemoryMappedFile
                : CrossProcessSecretsCommunicationKind.EnvironmentSingleEntry;

            var argumentsList = new []
                                {
                                    "CacheService",
                                    // If cacheConfigPath is null the validation will fail and the process won't be started.
                                    "--cacheConfigurationPath", _configuration.CacheConfigPath?.Path ?? string.Empty,
                                    // This is not a standalone cache service, it is controlled by ServiceLifetimeManager.
                                    "--standalone", "false",
                                    "--secretsProviderKind", secretsProviderKind.ToString(),
                                    "--dataRootPath", _configuration.DataRootPath.ToString(),
                                };

            var environment = new Dictionary<string, string>
                              {
                                  // Out-of-proc cache should propagate the ConfigurationId property to the child process.
                                  _configuration.HostParameters.ToEnvironment(saveConfigurationId: true),
                                  _serviceLifetimeManager.GetDeployedInterruptableServiceVariables(_configuration.ServiceId),
                                  _configuration.EnvironmentVariables,
                                  CacheServiceWrapperConfiguration.DefaultDotNetEnvironmentVariables
                              };

            if (_configuration.UseInterProcSecretsCommunication)
            {
                _interProcessSecretsCommunicator = InterProcessSecretsCommunicator.Expose(context, _secrets, _configuration.InterProcessSecretsFileName);
            }
            else
            {
                // Passing the secrets via environment variable in a single value.
                // This may be problematic if the serialized size will exceed some size (like 32K), but
                // it should not be the case for now.
                environment.Add(RetrievedSecretsSerializer.SerializedSecretsKeyName, RetrievedSecretsSerializer.Serialize(_secrets));
            }

            var process = _host.CreateProcess(
                new ProcessStartInfo()
                {
                    UseShellExecute = false,
                    FileName = executablePath,
                    Arguments = string.Join(" ", argumentsList),
                    // A strange cast to a nullable dictionary is needed to avoid warnings from the C# compiler.
                    Environment = { (IDictionary<string, string?>)environment },
                });

            _runningProcess = new LauncherManagedProcess(process, _configuration.ServiceId, _serviceLifetimeManager);

            // Handling the process exit event in case of a crash or an unbormal exit.
            process.Exited += () =>
                              {
                                  // Calling the host about the process exit only if we haven't started the shutdown procedure ourselves.
                                  if (!ShutdownStarted)
                                  {
                                      _requestTeardownDelegate(context, processDescription());
                                  }

                                  string processDescription()
                                  {
                                      try
                                      {
                                          // We should be able to get the Exit code and process id here.
                                          return $"Child process exited. Id={process.Id}, ExitCode={process.ExitCode}";
                                      }
                                      catch (Exception e)
                                      {
                                          return $"Failed getting process description on exit: {e}";
                                      }
                                  }
                              };

            Tracer.Info(context, "Starting out-of-proc cache process.");
            var result = _runningProcess.Start(context);

            Tracer.Info(context, $"Started out-of-proc cache process (Id={process.Id}). Result: {result}.");
            return Task.FromResult(result);
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            if (_runningProcess != null)
            {
                _interProcessSecretsCommunicator?.Dispose();
                return await _runningProcess.StopAsync(context, _configuration.ShutdownTimeout, _configuration.ProcessTerminationTimeout);
            }

            return BoolResult.Success;
        }
    }
}
