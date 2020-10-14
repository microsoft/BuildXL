// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.Host.Service;
using CLAP;
using Microsoft.WindowsAzure.Storage;
using Newtonsoft.Json;
using static BuildXL.Utilities.ConfigurationHelper;

// ReSharper disable once UnusedMember.Global
namespace BuildXL.Cache.ContentStore.App
{
    internal sealed partial class Application
    {
        /// <summary>
        /// Run the distributed service verb.
        /// </summary>
        [SuppressMessage("Microsoft.Maintainability", "CA1506:AvoidExcessiveClassCoupling")]
        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        [Verb(Description = "Run distributed CAS service")]
        internal void DistributedService
            (
            [Description("Path to DistributedContentSettings file")] string settingsPath,
            [Description("Cache root path")] string cachePath,
            [DefaultValue((int)ServiceConfiguration.GrpcDisabledPort), Description(GrpcPortDescription)] int grpcPort,
            [Description("Name of the memory mapped file used to share GRPC port. 'CASaaS GRPC port' if not specified.")] string grpcPortFileName,
            [DefaultValue(null), Description("Writable directory for service operations (use CWD if null)")] string dataRootPath,
            [DefaultValue(null), Description("Identifier for the stamp this service will run as")] string stampId,
            [DefaultValue(null), Description("Identifier for the ring this service will run as")] string ringId,
            [DefaultValue(Constants.OneMB), Description("Max size quota in MB")] int maxSizeQuotaMB,
            [DefaultValue(false)] bool debug,
            [DefaultValue(false), Description("Whether or not GRPC is used for file copies")] bool useDistributedGrpc,
            [DefaultValue(null), Description("Buffer size for streaming GRPC copies")] int? bufferSizeForGrpcCopies,
            [DefaultValue(null), Description("Files greater than this size are compressed if compression is used")] int? gzipBarrierSizeForGrpcCopies,
            [DefaultValue(null), Description("nLog configuration path. If empty, it is disabled")] string nLogConfigurationPath,
            [DefaultValue(null), Description("Whether to use Azure Blob logging or not")] string nLogToBlobStorageSecretName,
            [DefaultValue(null), Description("If using Azure Blob logging, where to temporarily store logs")] string nLogToBlobStorageWorkspacePath
            )
        {
            // We don't actually support the cache name being anything different than this, so there is no point in
            // allowing it.
            var cacheName = "Default";
            Initialize();

            if (debug)
            {
                System.Diagnostics.Debugger.Launch();
            }

            try
            {
                Validate();

                var dcs = JsonConvert.DeserializeObject<DistributedContentSettings>(File.ReadAllText(settingsPath));

                var host = new HostInfo(stampId, ringId, new List<string>());

                if (grpcPort == 0)
                {
                    grpcPort = Helpers.GetGrpcPortFromFile(_logger, grpcPortFileName);
                }

                var grpcCopyClientConfiguration = new GrpcCopyClientConfiguration();
                ApplyIfNotNull(dcs.GrpcCopyClientBufferSizeBytes, v => grpcCopyClientConfiguration.ClientBufferSizeBytes = v);
                ApplyIfNotNull(dcs.GrpcCopyClientUseGzipCompression, v => grpcCopyClientConfiguration.UseGzipCompression = v);
                ApplyIfNotNull(dcs.GrpcCopyClientConnectOnStartup, v => grpcCopyClientConfiguration.ConnectOnStartup = v);
                ApplyIfNotNull(dcs.TimeToFirstByteTimeoutInSeconds, v => grpcCopyClientConfiguration.TimeToFirstByteTimeout = TimeSpan.FromSeconds(v));
                ApplyIfNotNull(dcs.GrpcCopyClientDisconnectionTimeoutSeconds, v => grpcCopyClientConfiguration.DisconnectionTimeout = TimeSpan.FromSeconds(v));
                ApplyIfNotNull(dcs.GrpcCopyClientConnectionTimeoutSeconds, v => grpcCopyClientConfiguration.ConnectionTimeout = TimeSpan.FromSeconds(v));
                ApplyIfNotNull(dcs.GrpcCopyClientOperationDeadlineSeconds, v => grpcCopyClientConfiguration.OperationDeadline = TimeSpan.FromSeconds(v));
                ApplyIfNotNull(dcs.GrpcCopyClientGrpcCoreClientOptions, v => grpcCopyClientConfiguration.GrpcCoreClientOptions = v);
                grpcCopyClientConfiguration.BandwidthCheckerConfiguration = BandwidthChecker.Configuration.FromDistributedContentSettings(dcs);

                var resourcePoolConfiguration = new ResourcePoolConfiguration();
                ApplyIfNotNull(dcs.MaxGrpcClientCount, v => resourcePoolConfiguration.MaximumResourceCount = v);
                ApplyIfNotNull(dcs.MaxGrpcClientAgeMinutes, v => resourcePoolConfiguration.MaximumAge = TimeSpan.FromMinutes(v));
                ApplyIfNotNull(dcs.GrpcCopyClientCacheGarbageCollectionPeriodMinutes, v => resourcePoolConfiguration.GarbageCollectionPeriod = TimeSpan.FromMinutes(v));

                // We don't have to dispose the copier here. RunAsync will take care of that.
                var grpcCopyClientCacheConfiguration = new GrpcCopyClientCacheConfiguration()
                {
                    ResourcePoolConfiguration = resourcePoolConfiguration,
                    GrpcCopyClientConfiguration = grpcCopyClientConfiguration,
                };
                ApplyIfNotNull(dcs.GrpcCopyClientCacheResourcePoolVersion, v => grpcCopyClientCacheConfiguration.ResourcePoolVersion = (GrpcCopyClientCacheConfiguration.PoolVersion)v);

                var grpcFileCopierConfiguration = new GrpcFileCopierConfiguration()
                {
                    GrpcPort = grpcPort,
                    GrpcCopyClientCacheConfiguration = grpcCopyClientCacheConfiguration,
                };
                ApplyIfNotNull(dcs.GrpcFileCopierGrpcCopyClientInvalidationPolicy, v => {
                    if (!Enum.TryParse<GrpcFileCopierConfiguration.ClientInvalidationPolicy>(v, out var parsed))
                    {
                        throw new ArgumentException($"Failed to parse `{nameof(dcs.GrpcFileCopierGrpcCopyClientInvalidationPolicy)}` setting with value `{dcs.GrpcFileCopierGrpcCopyClientInvalidationPolicy}` into type `{nameof(GrpcFileCopierConfiguration.ClientInvalidationPolicy)}`");
                    }

                    grpcFileCopierConfiguration.GrpcCopyClientInvalidationPolicy = parsed;
                });

                var grpcCopier = new GrpcFileCopier(context: new Interfaces.Tracing.Context(_logger), grpcFileCopierConfiguration);

                var copier = useDistributedGrpc
                        ? grpcCopier
                        : (IRemoteFileCopier)new DistributedCopier();

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

                var arguments = CreateDistributedCacheServiceArguments(
                    copier: copier,
                    copyRequester: grpcCopier,
                    dcs: dcs,
                    host: host,
                    cacheName: cacheName,
                    cacheRootPath: cachePath,
                    grpcPort: (uint)grpcPort,
                    maxSizeQuotaMB: maxSizeQuotaMB,
                    dataRootPath: dataRootPath,
                    ct: _cancellationToken,
                    bufferSizeForGrpcCopies: bufferSizeForGrpcCopies,
                    gzipBarrierSizeForGrpcCopies: gzipBarrierSizeForGrpcCopies,
                    loggingSettings: loggingSettings,
                    telemetryFieldsProvider: new TelemetryFieldsProvider(ringId, stampId, serviceName: "DistributedService"));

                DistributedCacheServiceFacade.RunAsync(arguments).GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        private class TelemetryFieldsProvider : ITelemetryFieldsProvider
        {
            public string BuildId => "Unknown";

            public string ServiceName { get; }

            public string APEnvironment => "None";

            public string APCluster => "None";

            public string APMachineFunction => "None";

            public string MachineName => Environment.MachineName;

            public string ServiceVersion => "None";

            public string Stamp { get; }

            public string Ring { get; }

            public string ConfigurationId => "None";

            public TelemetryFieldsProvider(string ring, string stamp, string serviceName)
            {
                Ring = ring;
                Stamp = stamp;
                ServiceName = serviceName;
            }
        }
    }
}
