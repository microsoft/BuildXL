// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Exceptions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.Host.Service;
using CLAP;

// ReSharper disable once UnusedMember.Global
namespace BuildXL.Cache.ContentStore.App
{
    internal sealed partial class Application
    {
        private const uint DefaultGracefulShutdownSeconds = ServiceConfiguration.DefaultGracefulShutdownSeconds;
        private const string GracefulShutdownSecondsDescription =
            "Number of seconds to give clients to disconnect before connections are closed hard";

        private const string GrpcPortDescription = "The port number for spinning up a service with GRPC";
        private const string RemoteGrpcPortDescription = "The port number for contacting a backing cache service";

        /// <summary>
        ///     Run the service verb.
        /// </summary>
        [SuppressMessage("Microsoft.Maintainability", "CA1506:AvoidExcessiveClassCoupling")]
        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        [Verb(Description = "Run CAS service", IsDefault = true)]
        internal void Service
            (
            [Description("Cache names")] string[] names,
            [Description("Cache root paths")] string[] paths,
            [DefaultValue(DefaultGracefulShutdownSeconds), Description(GracefulShutdownSecondsDescription)] uint gracefulShutdownSeconds,
            [DefaultValue(ServiceConfiguration.GrpcDisabledPort), Description(GrpcPortDescription)] uint grpcPort,
            [Description("Name of the memory mapped file used to share GRPC port. 'CASaaS GRPC port' if not specified.")] string grpcPortFileName,
            [DefaultValue(null), Description("Writable directory for service operations (use CWD if null)")] string dataRootPath,
            [DefaultValue(null), Description("Duration of inactivity after which a session will be timed out.")] double? unusedSessionTimeoutSeconds,
            [DefaultValue(null), Description("Duration of inactivity after which a session with a heartbeat will be timed out.")] double? unusedSessionHeartbeatTimeoutSeconds,
            [DefaultValue(false), Description("Stop running service")] bool stop,
            [DefaultValue(Constants.OneGBInMB), Description("Max size quota in MB")] int maxSizeQuotaMB,
            [DefaultValue(ServiceConfiguration.GrpcDisabledPort), Description(RemoteGrpcPortDescription)] uint backingGrpcPort,
            [DefaultValue(null), Description("Name of scenario for backing CAS service")] string backingScenario,
            [DefaultValue("None"), Description("Ring Id. Used only for telemetry.")] string ringId,
            [DefaultValue("None"), Description("Stamp Id. Used only for telemetry.")] string stampId,
            [DefaultValue(null), Description("nLog configuration path. If empty, it is disabled")] string nLogConfigurationPath,
            [DefaultValue(null), Description("Whether to use Azure Blob logging or not")] string nLogToBlobStorageSecretName,
            [DefaultValue(null), Description("If using Azure Blob logging, where to temporarily store logs")] string nLogToBlobStorageWorkspacePath,
            [DefaultValue(false), Description("Enable metadata")] bool enableMetadata
            )
        {
            Initialize();

            if (stop)
            {
                IpcUtilities.SetShutdown(_scenario);
                return;
            }

            if (names == null || paths == null)
            {
                throw new CacheException("At least one cache name/path is required.");
            }

            if (names.Length != paths.Length)
            {
                throw new CacheException("Mismatching lengths of names/paths arguments.");
            }

            var serverDataRootPath = !string.IsNullOrWhiteSpace(dataRootPath)
                ? new AbsolutePath(dataRootPath)
                : new AbsolutePath(Environment.CurrentDirectory);

            var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(_cancellationToken);

            if (_scenario != null)
            {
                _logger.Debug($"scenario=[{_scenario}]");
            }

            using var exitEvent = GetExitEvent();

            var localCasSettings = LocalCasSettings.Default(maxSizeQuotaMB, paths[0], names[0], grpcPort, grpcPortFileName);
            for (int i = 1; i < names.Length; i++)
            {
                localCasSettings.AddNamedCache(names[i], paths[i]);
            }

            localCasSettings.ServiceSettings.ScenarioName = _scenario;

            if (unusedSessionTimeoutSeconds != null)
            {
                localCasSettings.ServiceSettings.UnusedSessionTimeoutMinutes = TimeSpan.FromSeconds(unusedSessionTimeoutSeconds.Value).TotalMinutes;
            }

            if (unusedSessionHeartbeatTimeoutSeconds != null)
            {
                localCasSettings.ServiceSettings.UnusedSessionHeartbeatTimeoutMinutes = TimeSpan.FromSeconds(unusedSessionHeartbeatTimeoutSeconds.Value).TotalMinutes;
            }

            var distributedContentSettings = DistributedContentSettings.CreateDisabled();
            if (backingGrpcPort != ServiceConfiguration.GrpcDisabledPort)
            {
                distributedContentSettings.BackingGrpcPort = (int)backingGrpcPort;
                distributedContentSettings.BackingScenario = backingScenario;
            }

            if (enableMetadata)
            {
                distributedContentSettings.EnableMetadataStore = true;
            }

            LoggingSettings loggingSettings = null;
            if (!string.IsNullOrEmpty(nLogConfigurationPath))
            {
                loggingSettings = new LoggingSettings()
                {
                    NLogConfigurationPath = nLogConfigurationPath,
                    Configuration = new AzureBlobStorageLogPublicConfiguration()
                    {
                        SecretName = nLogToBlobStorageSecretName,
                        WorkspaceFolderPath = nLogToBlobStorageWorkspacePath,
                    }
                };
            }

            var distributedCacheServiceConfiguration = new DistributedCacheServiceConfiguration(localCasSettings, distributedContentSettings, loggingSettings);

            // Ensure the computed keyspace is computed based on the hostInfo's StampId
            distributedCacheServiceConfiguration.UseStampBasedIsolation = false;

            var distributedCacheServiceArguments = new DistributedCacheServiceArguments(
                tracingContext: new Context(_logger),
                telemetryFieldsProvider: new TelemetryFieldsProvider(ringId, stampId, serviceName: "Service"),
                copier: new DistributedCopier(),
                copyRequester: null,
                host: new EnvironmentVariableHost(new Context(_logger)),
                hostInfo: new HostInfo(null, null, new List<string>()),
                cancellation: cancellationTokenSource.Token,
                dataRootPath: serverDataRootPath.Path,
                configuration: distributedCacheServiceConfiguration,
                keyspace: null);

            var runTask = Task.Run(() => DistributedCacheServiceFacade.RunAsync(distributedCacheServiceArguments));

            // Because the facade completes immediately and named wait handles don't exist in CORECLR,
            // completion here is gated on Control+C. In the future, this can be redone with another option,
            // such as a MemoryMappedFile or GRPC heartbeat. This is just intended to be functional.
            int completedIndex = WaitHandle.WaitAny(new WaitHandle[] { cancellationTokenSource.Token.WaitHandle, exitEvent });

            var source = completedIndex == 0 ? "control-C" : "exit event";
            _logger.Always($"Shutdown by {source}.");

            if (completedIndex == 1)
            {
                cancellationTokenSource.Cancel();
            }

            runTask.GetAwaiter().GetResult();
        }

        private WaitHandle GetExitEvent()
        {
            if (OperatingSystemHelper.IsWindowsOS)
            {
                return IpcUtilities.GetShutdownWaitHandle(_scenario);
            }
            else
            {
                // Not supported on non-windows OS. Return no-op wait handle.
                return CancellationToken.None.WaitHandle;
            }
        }
    }
}
