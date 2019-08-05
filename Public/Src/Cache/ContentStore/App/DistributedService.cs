// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Interfaces.Distributed;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.Host.Service;
using CLAP;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;
using Newtonsoft.Json;

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
            [Description("Cache name")] string cacheName,
            [Description("Cache root path")] string cachePath,
            [DefaultValue((int)ServiceConfiguration.GrpcDisabledPort), Description(GrpcPortDescription)] int grpcPort,
            [Description("Name of the memory mapped file used to share GRPC port. 'CASaaS GRPC port' if not specified.")] string grpcPortFileName,
            [DefaultValue(null), Description("Writable directory for service operations (use CWD if null)")] string dataRootPath,
            [DefaultValue(null), Description("Identifier for the stamp this service will run as")] string stampId,
            [DefaultValue(null), Description("Identifier for the ring this service will run as")] string ringId,
            [DefaultValue(Constants.OneMB), Description("Max size quota in MB")] int maxSizeQuotaMB,
            [DefaultValue(false)] bool debug,
            [DefaultValue(false), Description("Whether or not GRPC is used for file copies")] bool useDistributedGrpc,
            [DefaultValue(false), Description("Whether or not GZip is used for GRPC file copies")] bool useCompressionForCopies,
            [DefaultValue(null), Description("Buffer size for streaming GRPC copies")] int? bufferSizeForGrpcCopies,
            [DefaultValue(null), Description("Files greater than this size are compressed if compression is used")] int? gzipBarrierSizeForGrpcCopies
            )
        {
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

                var grpcCopier = new GrpcFileCopier(
                            context: new Interfaces.Tracing.Context(_logger),
                            grpcPort: grpcPort,
                            maxGrpcClientCount: dcs.MaxGrpcClientCount,
                            maxGrpcClientAgeMinutes: dcs.MaxGrpcClientAgeMinutes,
                            grpcClientCleanupDelayMinutes: dcs.GrpcClientCleanupDelayMinutes,
                            useCompression: useCompressionForCopies,
                            bufferSize: bufferSizeForGrpcCopies);

                var copier = useDistributedGrpc
                        ? grpcCopier
                        : (IAbsolutePathFileCopier)new DistributedCopier();

                var arguments = CreateDistributedCacheServiceArguments(
                    copier: copier,
                    pathTransformer: useDistributedGrpc ? new GrpcDistributedPathTransformer(_logger) : (IAbsolutePathTransformer)new DistributedPathTransformer(),
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
                    gzipBarrierSizeForGrpcCopies: gzipBarrierSizeForGrpcCopies);

                DistributedCacheServiceFacade.RunAsync(arguments).GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        private class EnvironmentVariableHost : IDistributedCacheServiceHost
        {
            public string GetSecretStoreValue(string key)
            {
                return Environment.GetEnvironmentVariable(key);
            }

            public void OnStartedService()
            {
            }

            public Task OnStartingServiceAsync()
            {
                return Task.CompletedTask;
            }

            public void OnTeardownCompleted()
            {
            }

            public Task<Dictionary<string, Secret>> RetrieveSecretsAsync(List<RetrieveSecretsRequest> requests, CancellationToken token)
            {
                var secrets = new Dictionary<string, Secret>();

                foreach (var request in requests)
                {
                    Secret secret = null;

                    var secretValue = GetSecretStoreValue(request.Name);
                    if (string.IsNullOrEmpty(secretValue))
                    {
                        // Environment variables are null by default. Skip if that's the case
                        continue;
                    }

                    switch (request.Kind)
                    {
                        case SecretKind.PlainText:
                            // In this case, the value is expected to be an entire connection string
                            secret = new PlainTextSecret(secretValue);
                            break;
                        case SecretKind.SasToken:
                            secret = CreateSasTokenSecret(request, secretValue);
                            break;
                        default:
                            throw new NotSupportedException($"It is expected that all supported credential kinds be handled when creating a DistributedService. {request.Kind} is unhandled.");
                    }

                    Contract.Requires(secret != null);
                    secrets[request.Name] = secret;
                }

                return Task.FromResult(secrets);
            }

            private Secret CreateSasTokenSecret(RetrieveSecretsRequest request, string secretValue)
            {
                var resourceTypeVariableName = $"{request.Name}_ResourceType";
                var resourceType = GetSecretStoreValue(resourceTypeVariableName);
                if (string.IsNullOrEmpty(resourceType))
                {
                    throw new ArgumentNullException($"Missing environment variable {resourceTypeVariableName} that stores the resource type for secret {request.Name}");
                }

                switch (resourceType.ToLowerInvariant())
                {
                    case "storagekey":
                        return CreateAzureStorageSasTokenSecret(request, secretValue);
                    default:
                        throw new NotSupportedException($"Unknown resource type {resourceType} for secret named {request.Name}. Check environment variable {resourceTypeVariableName} has a valid value.");
                }
            }

            private Secret CreateAzureStorageSasTokenSecret(RetrieveSecretsRequest request, string secretValue)
            {
                // In this case, the environment variable is expected to hold an Azure Storage connection string
                var cloudStorageAccount = CloudStorageAccount.Parse(secretValue);

                // Create a godlike SAS token for the account, so that we don't need to reimplement the Central Secrets Service.
                var sasToken = cloudStorageAccount.GetSharedAccessSignature(new SharedAccessAccountPolicy
                {
                    SharedAccessExpiryTime = null,
                    Permissions = SharedAccessAccountPermissions.Add | SharedAccessAccountPermissions.Create | SharedAccessAccountPermissions.Delete | SharedAccessAccountPermissions.List | SharedAccessAccountPermissions.ProcessMessages | SharedAccessAccountPermissions.Read | SharedAccessAccountPermissions.Update | SharedAccessAccountPermissions.Write,
                    Services = SharedAccessAccountServices.Blob,
                    ResourceTypes = SharedAccessAccountResourceTypes.Object | SharedAccessAccountResourceTypes.Container | SharedAccessAccountResourceTypes.Service,
                    Protocols = SharedAccessProtocol.HttpsOnly,
                    IPAddressOrRange = null,
                });

                var internalSasToken = new SasToken() {
                    Token = sasToken,
                    StorageAccount = cloudStorageAccount.Credentials.AccountName,
                };
                return new UpdatingSasToken(internalSasToken);
            }
        }
    }
}
