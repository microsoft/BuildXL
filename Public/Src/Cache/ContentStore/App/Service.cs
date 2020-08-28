// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using BuildXL.Cache.ContentStore.Exceptions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Utils;
using CLAP;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
#if !NET_FRAMEWORK
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.Host.Service;
#endif

// ReSharper disable once UnusedMember.Global
namespace BuildXL.Cache.ContentStore.App
{
    internal sealed partial class Application
    {
        private const uint DefaultMaxConnections = ServiceConfiguration.DefaultMaxConnections;
        private const uint DefaultGracefulShutdownSeconds = ServiceConfiguration.DefaultGracefulShutdownSeconds;
        private const string MaxConnectionsDescription = "Maximum number of concurrent clients";
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
            [DefaultValue(DefaultMaxConnections), Description(MaxConnectionsDescription)] uint maxConnections,
            [DefaultValue(DefaultGracefulShutdownSeconds), Description(GracefulShutdownSecondsDescription)] uint gracefulShutdownSeconds,
            [DefaultValue(ServiceConfiguration.GrpcDisabledPort), Description(GrpcPortDescription)] uint grpcPort,
            [Description("Name of the memory mapped file used to share GRPC port. 'CASaaS GRPC port' if not specified.")] string grpcPortFileName,
            [DefaultValue(null), Description("Writable directory for service operations (use CWD if null)")] string dataRootPath,
            [DefaultValue(null), Description("Duration of inactivity after which a session will be timed out.")] double? unusedSessionTimeoutSeconds,
            [DefaultValue(null), Description("Duration of inactivity after which a session with a heartbeat will be timed out.")] double? unusedSessionHeartbeatTimeoutSeconds,
            [DefaultValue(false), Description("Stop running service")] bool stop,
            [DefaultValue(Constants.OneMB), Description("Max size quota in MB")] int maxSizeQuotaMB,
            [DefaultValue(ServiceConfiguration.GrpcDisabledPort), Description(RemoteGrpcPortDescription)] int backingGrpcPort,
            [DefaultValue(null), Description("Name of scenario for backing CAS service")] string backingScenario,
            [DefaultValue("None"), Description("Ring Id. Used only for telemetry.")] string ringId,
            [DefaultValue("None"), Description("Stamp Id. Used only for telemetry.")] string stampId,
            [DefaultValue(null), Description("nLog configuration path. If empty, it is disabled")] string nLogConfigurationPath,
            [DefaultValue(null), Description("Whether to use Azure Blob logging or not")] string nLogToBlobStorageSecretName,
            [DefaultValue(null), Description("If using Azure Blob logging, where to temporarily store logs")] string nLogToBlobStorageWorkspacePath
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

            var caches = new Dictionary<string, string>();
            for (var i = 0; i < names.Length; i++)
            {
                caches.Add(names[i], paths[i]);
            }

            var serverDataRootPath = !string.IsNullOrWhiteSpace(dataRootPath)
                ? new AbsolutePath(dataRootPath)
                : new AbsolutePath(Environment.CurrentDirectory);

            var cancellationTokenSource = new CancellationTokenSource();

#if NET_FRAMEWORK
            var configuration = new ServiceConfiguration(caches, serverDataRootPath, maxConnections, gracefulShutdownSeconds, (int)grpcPort, grpcPortFileName);
            if (!configuration.IsValid)
            {
                throw new CacheException($"Invalid service configuration, error=[{configuration.Error}]");
            }

            var localContentServerConfiguration = new LocalServerConfiguration(configuration);
            if (unusedSessionTimeoutSeconds != null)
            {
                localContentServerConfiguration.UnusedSessionTimeout = TimeSpan.FromSeconds(unusedSessionTimeoutSeconds.Value);
            }

            if (unusedSessionHeartbeatTimeoutSeconds != null)
            {
                localContentServerConfiguration.UnusedSessionHeartbeatTimeout = TimeSpan.FromSeconds(unusedSessionHeartbeatTimeoutSeconds.Value);
            }

            if (_scenario != null)
            {
                _logger.Debug($"scenario=[{_scenario}]");
            }

            var exitSignal = new ManualResetEvent(false);
            Console.CancelKeyPress += (sender, args) =>
            {
                exitSignal.Set();
                args.Cancel = true;
            };

            using (var exitEvent = IpcUtilities.GetShutdownWaitHandle(_scenario))
            {
                var server = new LocalContentServer(
                    _fileSystem,
                    _logger,
                    _scenario,
                    path =>
                        new FileSystemContentStore(
                            _fileSystem,
                            SystemClock.Instance,
                            path,
                            new ConfigurationModel(inProcessConfiguration: ContentStoreConfiguration.CreateWithMaxSizeQuotaMB((uint)maxSizeQuotaMB))),
                    localContentServerConfiguration);

                using (server)
                {
                    var context = new Context(_logger);
                    try
                    {
                        var result = server.StartupAsync(context).Result;
                        if (!result.Succeeded)
                        {
                            throw new CacheException(result.ErrorMessage);
                        }

                        int completedIndex = WaitHandle.WaitAny(new WaitHandle[] { exitSignal, exitEvent });
                        var source = completedIndex == 0 ? "control-C" : "exit event";
                        _tracer.Always(context, $"Shutdown by {source}.");
                    }
                    finally
                    {
                        var result = server.ShutdownAsync(context).Result;
                        if (!result.Succeeded)
                        {
                            _tracer.Warning(context, $"Failed to shutdown store: {result.ErrorMessage}");
                        }
                    }
                }
            }
#else
            Console.CancelKeyPress += (sender, args) =>
            {
                cancellationTokenSource.Cancel();
                args.Cancel = true;
            };

            var localCasSettings = LocalCasSettings.Default(maxSizeQuotaMB, serverDataRootPath.Path, names[0], (uint)grpcPort);
            localCasSettings.ServiceSettings.ScenarioName = _scenario;

            var distributedContentSettings = DistributedContentSettings.CreateDisabled();
            if (backingGrpcPort != ServiceConfiguration.GrpcDisabledPort)
            {
                distributedContentSettings.BackingGrpcPort = backingGrpcPort;
                distributedContentSettings.BackingScenario = backingScenario;
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
                logger: _logger,
                copier: null,
                pathTransformer: new GrpcDistributedPathTransformer(_logger),
                copyRequester: null,
                host: new EnvironmentVariableHost(),
                hostInfo: new HostInfo(null, null, new List<string>()),
                cancellation: cancellationTokenSource.Token,
                dataRootPath: serverDataRootPath.Path,
                configuration: distributedCacheServiceConfiguration,
                keyspace: null)
            {
                TelemetryFieldsProvider = new TelemetryFieldsProvider(ringId, stampId, serviceName: "Service"),
            };

            DistributedCacheServiceFacade.RunAsync(distributedCacheServiceArguments).GetAwaiter().GetResult();

            // Because the facade completes immediately and named wait handles don't exist in CORECLR,
            // completion here is gated on Control+C. In the future, this can be redone with another option,
            // such as a MemoryMappedFile or GRPC heartbeat. This is just intended to be functional.
            cancellationTokenSource.Token.WaitHandle.WaitOne();
#endif
        }
    }
}
