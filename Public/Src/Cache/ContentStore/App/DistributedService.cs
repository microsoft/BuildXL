// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.Host.Service;
using CLAP;
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

                var grpcFileCopierConfiguration = GrpcFileCopierConfiguration.FromDistributedContentSettings(dcs, grpcPort);

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
