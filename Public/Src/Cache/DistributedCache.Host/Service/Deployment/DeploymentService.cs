// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.ParallelAlgorithms;
using JetBrains.Annotations;
using static BuildXL.Cache.Host.Configuration.DeploymentManifest;
using static BuildXL.Cache.Host.Service.DeploymentUtilities;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;

namespace BuildXL.Cache.Host.Service
{
    /// <summary>
    /// Service used ensure deployments are uploaded to target storage accounts and provide manifest for with download urls and tools to launch
    /// </summary>
    public class DeploymentService
    {
        private Tracer Tracer { get; } = new Tracer(nameof(DeploymentService));

        private DeploymentServiceConfiguration Configuration { get; }

        /// <summary>
        /// The root of the mounted deployment folder created by the <see cref="DeploymentIngester"/>
        /// </summary>
        private AbsolutePath DeploymentRoot { get; }

        /// <summary>
        /// Cached expirable value for read deployment info
        /// </summary>
        private VolatileMap<string, Lazy<object>> CachedDeploymentInfo { get; }

        /// <summary>
        /// Map for getting expirable sas urls by storage account and hash 
        /// </summary>
        private VolatileMap<(string storageName, string hash), AsyncLazy<DownloadInfo>> SasUrls { get; }

        /// <summary>
        /// Map for getting expirable sas urls by a secret generated token used for retrieving the sas url
        /// </summary>
        private VolatileMap<string, string> SasUrlsByToken { get; }

        /// <summary>
        /// Map for getting expirable secrets by name, kind, and time to live
        /// </summary>
        private VolatileMap<(ISecretsProvider secretsProvider, string secretName, SecretKind kind), AsyncLazy<string>> CachedSecrets { get; }

        /// <summary>
        /// Map from storage account secret name to target storage account
        /// </summary>
        private VolatileMap<string, AsyncLazy<CentralStorage>> StorageAccountsBySecret { get; }

        private VolatileMap<string, Lazy<ProxyManager>> ProxyManagers { get; }

        private IClock Clock { get; }

        private ActionQueue UploadQueue { get; }

        /// <summary>
        /// The secrets provider used to get connection string secrets for storage accounts
        /// </summary>
        private Func<string, ISecretsProvider> SecretsProviderFactory { get; }

        private VolatileMap<string, AsyncLazy<ISecretsProvider>> SecretsProvidersByUri { get; }

        /// <summary>
        /// For testing purposes only. Used to intercept call to create blob central storage
        /// </summary>
        public Func<(string storageSecretName, AzureBlobStorageCredentials credentials), CentralStorage> OverrideCreateCentralStorage { get; set; }

        /// <nodoc />
        public DeploymentService(DeploymentServiceConfiguration configuration, AbsolutePath deploymentRoot, Func<string, ISecretsProvider> secretsProviderFactory, IClock clock, int uploadConcurrency = 1)
        {
            Configuration = configuration;
            DeploymentRoot = deploymentRoot;
            Clock = clock;
            SecretsProviderFactory = secretsProviderFactory;
            StorageAccountsBySecret = new VolatileMap<string, AsyncLazy<CentralStorage>>(clock);
            SecretsProvidersByUri = new VolatileMap<string, AsyncLazy<ISecretsProvider>>(clock);
            SasUrls = new VolatileMap<(string storageName, string hash), AsyncLazy<DownloadInfo>>(clock);
            SasUrlsByToken = new VolatileMap<string, string>(clock);
            CachedDeploymentInfo = new VolatileMap<string, Lazy<object>>(clock);
            CachedSecrets = new VolatileMap<(ISecretsProvider, string, SecretKind), AsyncLazy<string>>(clock);
            ProxyManagers = new VolatileMap<string, Lazy<ProxyManager>>(clock);

            UploadQueue = new ActionQueue(uploadConcurrency);
        }

        // TODO [LANCEC]: Consider returning prior deployment until all files are uploaded.

        /// <summary>
        /// Checks whether the current deployment parameters represent an authorized query 
        /// </summary>
        public async Task<bool> IsAuthorizedAsync(OperationContext context, DeploymentParameters parameters)
        {
            var result = await context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    Tracer.Debug(context, "IsAuthorizedAsync: Reading deployment configuration");
                    var deployConfig = ReadDeploymentConfiguration(parameters, out _, out var contentId);
                    if (!deployConfig.AuthorizationSecretNames.Contains(parameters.AuthorizationSecretName))
                    {
                        throw new UnauthorizedAccessException($"Secret names do not match: Expected='{string.Join(", ", deployConfig.AuthorizationSecretNames)}' Actual='{parameters.AuthorizationSecretName}'");
                    }

                    Tracer.Debug(context, "IsAuthorizedAsync: Loading secrets provider");
                    var secretsProvider = await GetSecretsProviderAsync(context, deployConfig.KeyVaultUri);

                    Tracer.Debug(context, "IsAuthorizedAsync: Getting secret");
                    var secret = await GetSecretAsync(context, secretsProvider, new SecretConfiguration()
                    {
                        Name = parameters.AuthorizationSecretName,
                        TimeToLive = deployConfig.AuthorizationSecretTimeToLive,
                        Kind = SecretKind.PlainText
                    });

                    Tracer.Debug(context, "IsAuthorizedAsync: Comparing secret");
                    if (secret != parameters.AuthorizationSecret)
                    {
                        throw new UnauthorizedAccessException($"Secret values do not match for secret name: '{parameters.AuthorizationSecretName}'");
                    }

                    return BoolResult.Success;
                },
                extraStartMessage: $"{parameters} SecretName={parameters.AuthorizationSecretName}",
                extraEndMessage: r => $"{parameters} SecretName={parameters.AuthorizationSecretName}");

            return result.Succeeded;
        }

        private Task<ISecretsProvider> GetSecretsProviderAsync(OperationContext context, string keyVaultUri)
        {
            return GetOrAddExpirableAsync<string, ISecretsProvider>(
                 SecretsProvidersByUri,
                 keyVaultUri,
                 TimeSpan.FromHours(2),
                 () => context.PerformOperationAsync(
                     Tracer,
                     () => Task.FromResult(Result.Success(SecretsProviderFactory.Invoke(keyVaultUri))),
                     extraStartMessage: keyVaultUri,
                     extraEndMessage: r => keyVaultUri).ThrowIfFailureAsync());
        }

        /// <summary>
        /// Uploads the deployment files to the target storage account and returns the launcher manifest for the given deployment parameters
        /// </summary>
        public Task<LauncherManifest> UploadFilesAndGetManifestAsync(OperationContext context, DeploymentParameters parameters, bool waitForCompletion)
        {
            int pendingFiles = 0;
            int totalFiles = 0;
            int completedFiles = 0;
            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    var deployConfig = ReadDeploymentConfiguration(parameters, out var deploymentManifest, out var contentId);
                    var resultManifest = new LauncherManifest()
                    {
                        ContentId = contentId,
                        DeploymentManifestChangeId = deploymentManifest.ChangeId
                    };

                    var uploadTasks = new List<Task<(string targetPath, FileSpec spec)>>();

                    resultManifest.Tool = deployConfig.Tool;
                    resultManifest.Drops = deployConfig.Drops;

                    var secretsProvider = await GetSecretsProviderAsync(context, deployConfig.KeyVaultUri);

                    if (deployConfig.Tool?.SecretEnvironmentVariables != null)
                    {
                        // Populate environment variables from secrets.
                        foreach (var secretEnvironmentVariable in deployConfig.Tool.SecretEnvironmentVariables)
                        {
                            // Default to using environment variable name as the secret name
                            secretEnvironmentVariable.Value.Name ??= secretEnvironmentVariable.Key;

                            var secretValue = await GetSecretAsync(context, secretsProvider, secretEnvironmentVariable.Value);
                            resultManifest.Tool.EnvironmentVariables[secretEnvironmentVariable.Key] = secretValue;

                            if (secretEnvironmentVariable.Value.Kind == SecretKind.SasToken)
                            {
                                // Currently, all sas tokens are assumed to be storage secrets
                                // This is passed so that EnvironmentVariableHost can interpret the secret
                                // as a connection string and create a sas token on demand
                                resultManifest.Tool.EnvironmentVariables[$"{secretEnvironmentVariable.Key}_ResourceType"] = "storagekey";
                            }
                        }

                        var secretsContentId = ComputeShortContentId(JsonSerialize(resultManifest.Tool.EnvironmentVariables));

                        // NOTE: We append the content id so that we can distinguish purely secrets changes in logging
                        resultManifest.ContentId += $"_{secretsContentId}";
                    }

                    var storage = await LoadStorageAsync(context, secretsProvider, deployConfig.AzureStorageSecretInfo, deployConfig.AzureFileShareName);

                    var proxyBaseAddress = GetProxyBaseAddress(context, () => (deployConfig, deploymentManifest), parameters);

                    var filesAndTargetPaths = deployConfig.Drops
                        .Where(drop => drop.Url != null)
                        .SelectMany(drop => deploymentManifest.Drops[drop.Url]
                            .Select(fileEntry => (fileSpec: fileEntry.Value, targetPath: Path.Combine(drop.TargetRelativePath ?? string.Empty, fileEntry.Key))))
                        .ToList();

                    if (deployConfig.Proxy != null)
                    {
                        // If proxy is enabled, add deployment configuration file to deployment so it can be read by
                        // deployment proxy service
                        filesAndTargetPaths.Add((
                            deploymentManifest.GetDeploymentConfigurationSpec(),
                            deployConfig.Proxy.TargetRelativePath));
                    }

                    foreach ((var fileSpec, var targetPath) in filesAndTargetPaths)
                    {
                        resultManifest.Deployment[targetPath] = fileSpec;

                        // Queue file for deployment
                        uploadTasks.Add(ensureUploadedAndGetEntry());

                        async Task<(string targetPath, FileSpec entry)> ensureUploadedAndGetEntry()
                        {
                            var downloadInfo = parameters.GetContentInfoOnly
                                ? null
                                : await EnsureUploadedAndGetDownloadUrlAsync(context, fileSpec, deployConfig, storage);

                            var downloadUrl = downloadInfo?.GetUrl(context, hash: fileSpec.Hash, proxyBaseAddress: proxyBaseAddress);

                            // Compute and record path in final layout
                            return (targetPath, new FileSpec()
                            {
                                Hash = fileSpec.Hash,
                                Size = fileSpec.Size,
                                DownloadUrl = downloadUrl
                            });
                        }
                    }

                    var deploymentContentId = ComputeShortContentId(JsonSerialize(resultManifest.Deployment));

                    // NOTE: We append the content id so that we can distinguish purely secrets changes in logging
                    resultManifest.ContentId += $"_{deploymentContentId}";

                    var uploadCompletion = Task.WhenAll(uploadTasks);
                    if (waitForCompletion)
                    {
                        await uploadCompletion;
                    }
                    else
                    {
                        uploadCompletion.FireAndForget(context);
                    }

                    foreach (var uploadTask in uploadTasks)
                    {
                        totalFiles++;
                        if (uploadTask.IsCompleted)
                        {
                            completedFiles++;
                            var entry = await uploadTask;
                            resultManifest.Deployment[entry.targetPath] = entry.spec;
                        }
                        else
                        {
                            pendingFiles++;
                        }
                    }

                    resultManifest.IsComplete = pendingFiles == 0;

                    return Result.Success(resultManifest);
                },
                extraStartMessage: $"Machine={parameters.Machine} Stamp={parameters.Stamp} Wait={waitForCompletion}",
                extraEndMessage: r => $"Machine={parameters.Machine} Stamp={parameters.Stamp} Id=[{r.GetValueOrDefault()?.ContentId}] Drops={r.GetValueOrDefault()?.Drops.Count ?? 0} Files[Total={totalFiles}, Pending={pendingFiles}, Completed={completedFiles}] Wait={waitForCompletion}"
                ).ThrowIfFailureAsync();
        }

        public string GetProxyBaseAddress(OperationContext context, HostParameters parameters)
        {
            return GetProxyBaseAddress(
                context,
                () => (ReadDeploymentConfiguration(parameters, out var manifest, out _), manifest),
                parameters,
                // Return service url to route content requests to this service if this is a seed machine
                getDefaultBaseAddress: config => config.Proxy.ServiceConfiguration.DeploymentServiceUrl);
        }

        private string GetProxyBaseAddress(OperationContext context, Func<(DeploymentConfiguration configuration, DeploymentManifest manifest)> getConfiguration, HostParameters parameters, Func<DeploymentConfiguration, string> getDefaultBaseAddress = null)
        {
            return context.PerformOperation(
                Tracer,
                () =>
                {
                    var (configuration, manifest) = getConfiguration();

                    // Invalidate proxy on any changes to deployment configuration
                    var contentId = manifest.GetDeploymentConfigurationSpec().Hash;
                    if (configuration.Proxy == null)
                    {
                        return new Result<string>(null, isNullAllowed: true);
                    }

                    var proxyManager = GetOrAddExpirableLazy(
                        ProxyManagers,
                        parameters.Stamp + configuration.Proxy.Domain + contentId,
                        configuration.Proxy.ServiceConfiguration.ProxyAddressTimeToLive,
                        () => new ProxyManager(configuration));

                    return new Result<string>(proxyManager.GetBaseAddress(parameters, configuration) ?? getDefaultBaseAddress?.Invoke(configuration), isNullAllowed: true);
                },
                messageFactory: r => $"{parameters} BaseAddress={r.GetValueOrDefault()}").Value;
        }

        private async Task<string> GetSecretAsync(OperationContext context, ISecretsProvider secretsProvider, SecretConfiguration secretInfo)
        {
            if (secretInfo.OverrideKeyVaultUri != null)
            {
                secretsProvider = await GetSecretsProviderAsync(context, secretInfo.OverrideKeyVaultUri);
            }

            return await GetOrAddExpirableAsync(
                CachedSecrets,
                (secretsProvider, secretInfo.Name, secretInfo.Kind),
                secretInfo.TimeToLive,
                () =>
                {
                    return context.PerformOperationAsync<Result<string>>(
                        Tracer,
                        async () =>
                        {
                            var secretValue = await secretsProvider.GetPlainSecretAsync(secretInfo.Name, context.Token);

                            if (secretInfo.Kind == SecretKind.SasToken)
                            {
                                // The logic below relies on conventions used for sas token secrets:
                                // 1. Secret name is same as account + "-sas"
                                // 2. Secret value is access key (NOT full connection string)
                                Contract.Assert(secretInfo.Name.EndsWith("-sas", StringComparison.OrdinalIgnoreCase), "Convention requires that secret name is account name suffixed with '-sas'.");

                                if (!secretValue.StartsWith("DefaultEndpointProtocol="))
                                {
                                    var accountName = secretInfo.Name.Substring(0, secretInfo.Name.Length - 4 /* Subtract length of '-sas' suffix */);
                                    secretValue = $"DefaultEndpointsProtocol=https;AccountName={accountName};AccountKey={secretValue};EndpointSuffix=core.windows.net";
                                }
                            }

                            return Result.Success<string>(secretValue);
                        },
                        extraEndMessage: r => $"Name={secretInfo.Name} TimeToLiveMinutes={secretInfo.TimeToLive}").ThrowIfFailureAsync();
                });
        }

        private async Task<CentralStorage> LoadStorageAsync(OperationContext context, ISecretsProvider secretsProvider, SecretConfiguration storageSecretInfo, string fileShare)
        {
            var secretValue = await GetSecretAsync(context, secretsProvider, storageSecretInfo);

            return await GetOrAddExpirableAsync<string, CentralStorage>(
                StorageAccountsBySecret,
                secretValue,
                storageSecretInfo.TimeToLive,
                async () =>
                {
                    var credentials = new AzureBlobStorageCredentials(new PlainTextSecret(secretValue));

                    CentralStorage centralStorage;

                    if (OverrideCreateCentralStorage != null)
                    {
                        centralStorage = OverrideCreateCentralStorage.Invoke((storageSecretInfo.Name, credentials));
                    }
                    else if (!string.IsNullOrEmpty(fileShare))
                    {
                        centralStorage = new AzureFilesCentralStorage(new BlobCentralStoreConfiguration(credentials,
                                containerName: fileShare,
                                checkpointsKey: "N/A"));
                    }
                    else
                    {
                        centralStorage = new BlobCentralStorage(new BlobCentralStoreConfiguration(credentials,
                                 containerName: "deploymentfiles",
                                 checkpointsKey: "N/A"));
                    }

                    await centralStorage.StartupAsync(context).ThrowIfFailure();

                    return centralStorage;
                });
        }

        /// <summary>
        /// Ensures the given file under the deployment root is uploaded to the specified storage account and returns the download url
        /// </summary>
        private Task<DownloadInfo> EnsureUploadedAndGetDownloadUrlAsync(OperationContext context, FileSpec value, DeploymentConfiguration configuration, CentralStorage storage)
        {
            var sasUrlTimeToLive = configuration.SasUrlTimeToLive;
            var key = (configuration.AzureStorageSecretInfo.Name, value.Hash);
            return GetOrAddExpirableAsync(
                SasUrls,
                key,
                sasUrlTimeToLive,
                async () =>
                {
                    var downloadUrl = await UploadQueue.RunAsync(async () =>
                    {
                        try
                        {
                            var relativePath = DeploymentUtilities.GetContentRelativePath(new ContentHash(value.Hash)).ToString();

                            var now = Clock.UtcNow;
                            var expiry = now + sasUrlTimeToLive.Multiply(2);
                            var result = await storage.TryGetSasUrlAsync(context, relativePath, expiry: expiry);
                            if (result.Succeeded)
                            {
                                return result.Value;
                            }

                            await storage.UploadFileAsync(context, DeploymentRoot / relativePath, relativePath).ThrowIfFailure();

                            // NOTE: We compute the expiry to be 2x the desired expiry such that if returned from cache
                            // the URL will definitely live for at least SasUrlTimeToLive
                            expiry = now + sasUrlTimeToLive.Multiply(2);
                            return await storage.TryGetSasUrlAsync(context, relativePath, expiry: expiry).ThrowIfFailureAsync();
                        }
                        catch
                        {
                            SasUrls.Invalidate(key);
                            throw;
                        }
                    });

                    var downloadInfo = new DownloadInfo(downloadUrl);
                    SasUrlsByToken.TryAdd(
                        downloadInfo.AccessToken,
                        downloadInfo.DownloadUrl,
                        // Ensure token outlives sas url
                        sasUrlTimeToLive.Multiply(1.5));
                    return downloadInfo;
                });
        }

        /// <summary>
        /// Attempts to get the storage sas url given the token
        /// </summary>
        public Result<string> TryGetDownloadUrl(OperationContext context, string accessToken, string traceInfo)
        {
            return context.PerformOperation(
                Tracer,
                () =>
                {
                    if (SasUrlsByToken.TryGetValue(accessToken, out var sasUrl))
                    {
                        return Result.Success(sasUrl);
                    }

                    throw new UnauthorizedAccessException("Unable to find url for token");
                },
                extraStartMessage: traceInfo,
                messageFactory: r => traceInfo);
        }

        /// <summary>
        /// Reads the current manifest id for high-level change detection for deployment configuration
        /// </summary>
        public string ReadManifestChangeId()
        {
            var manifestId = GetOrAddExpirableValue(CachedDeploymentInfo, "MANIFEST_ID", TimeSpan.FromMinutes(1), () =>
            {
                return File.ReadAllText(DeploymentUtilities.GetDeploymentManifestIdPath(DeploymentRoot).Path);
            });

            return manifestId;
        }

        /// <summary>
        /// Gets the deployment configuration based on the manifest, preprocesses it, and returns the deserialized value
        /// </summary>
        private DeploymentConfiguration ReadDeploymentConfiguration(HostParameters parameters, out DeploymentManifest manifest, out string contentId)
        {
            var manifestId = ReadManifestChangeId();

            var cachedValue = GetOrAddExpirableValue(CachedDeploymentInfo, manifestId, TimeSpan.FromMinutes(10), () =>
            {
                var manifestText = File.ReadAllText(DeploymentUtilities.GetDeploymentManifestPath(DeploymentRoot).Path);

                var manifest = JsonSerializer.Deserialize<DeploymentManifest>(manifestText);
                manifest.ChangeId = manifestId;

                var configurationPath = DeploymentUtilities.GetDeploymentConfigurationPath(DeploymentRoot, manifest);

                var configJson = File.ReadAllText(configurationPath.Path);

                return (manifest, configJson);
            },
            // Force update since for the same manifest id, this will always return the same value
            // We only utilize this helper to ensure calls are deduplicated
            update: true);

            var preprocessor = DeploymentUtilities.GetHostJsonPreprocessor(parameters);

            var preprocessedConfigJson = preprocessor.Preprocess(cachedValue.configJson);
            contentId = ComputeShortContentId(preprocessedConfigJson);

            var config = JsonSerializer.Deserialize<DeploymentConfiguration>(preprocessedConfigJson, DeploymentUtilities.ConfigurationSerializationOptions);

            manifest = cachedValue.manifest;

            return config;
        }

        private static string ComputeShortContentId(string value)
        {
            return DeploymentUtilities.ComputeContentId(value).Substring(0, 8);
        }

        private async Task<TValue> GetOrAddExpirableAsync<TKey, TValue>(
            VolatileMap<TKey, AsyncLazy<TValue>> map,
            TKey key,
            TimeSpan timeToLive,
            Func<Task<TValue>> func)
        {
            AsyncLazy<TValue> asyncLazyValue;
            while (!map.TryGetValue(key, out asyncLazyValue))
            {
                asyncLazyValue = new AsyncLazy<TValue>(async () =>
                {
                    // Ensure no synchronous portion to execution of async delegate
                    await Task.Yield();
                    return await func();
                });
                map.TryAdd(key, asyncLazyValue, timeToLive);
            }

            bool isCompleted = asyncLazyValue.IsCompleted;
            var result = await asyncLazyValue.GetValueAsync();
            if (!isCompleted)
            {
                // Ensure continuations run asynchronously
                await Task.Yield();
            }

            return result;
        }

        private TValue GetOrAddExpirableValue<TKey, TValue>(
            VolatileMap<TKey, Lazy<object>> map,
            TKey key,
            TimeSpan timeToLive,
            Func<TValue> func,
            bool update = false)
        {
            var untypedValue = GetOrAddExpirableLazy<TKey, object>(
                map,
                key,
                timeToLive,
                () => func(),
                update);

            return (TValue)untypedValue;
        }

        private TValue GetOrAddExpirableLazy<TKey, TValue>(
            VolatileMap<TKey, Lazy<TValue>> map,
            TKey key,
            TimeSpan timeToLive,
            Func<TValue> func,
            bool update = false)
        {
            Lazy<TValue> lazyValue;
            while (!map.TryGetValue(key, out lazyValue))
            {
                lazyValue = new Lazy<TValue>(func);
                map.TryAdd(key, lazyValue, timeToLive);
            }

            if (update)
            {
                map.TryAdd(key, lazyValue, timeToLive, replaceIfExists: true);
            }

            return lazyValue.Value;
        }

        private class ProxyManager
        {
            private readonly ConcurrentBigSet<string> _machines = new ConcurrentBigSet<string>();
            private readonly DeploymentConfiguration _configuration;

            public ProxyManager(DeploymentConfiguration configuration)
            {
                _configuration = configuration;
            }

            public string GetBaseAddress(HostParameters parameters, DeploymentConfiguration machineSpecificConfiguration)
            {
                var host = machineSpecificConfiguration.Proxy.OverrideProxyHost;
                if (host == null)
                {
                    int minProxyMachineIndexInclusive = 0;
                    int maxProxyMachineIndexExclusive = _machines.Count;

                    if (!machineSpecificConfiguration.Proxy.ConsumerOnly)
                    {
                        var result = _machines.GetOrAdd(parameters.Machine);
                        var machineIndex = result.Index;
                        if (machineIndex < _configuration.Proxy.Seeds)
                        {
                            // Seed machines do not use proxy. Instead they use the real storage SAS url
                            return null;
                        }

                        minProxyMachineIndexInclusive = machineIndex / _configuration.Proxy.FanOutFactor;
                        maxProxyMachineIndexExclusive = Math.Min(machineIndex, minProxyMachineIndexInclusive + _configuration.Proxy.FanOutFactor);
                    }

                    int proxyMachineIndex = ThreadSafeRandom.Generator.Next(minProxyMachineIndexInclusive, maxProxyMachineIndexExclusive);
                    if (_machines.Count >= proxyMachineIndex)
                    {
                        // No proxy machines available
                        return null;
                    }

                    host = _machines[proxyMachineIndex];
                }

                return new UriBuilder()
                {
                    Host = host,
                    Port = _configuration.Proxy.ServiceConfiguration.Port
                }.Uri.ToString();
            }
        }

        private class DownloadInfo
        {
            public string DownloadUrl { get; }
            public string AccessToken { get; }

            public DownloadInfo(string downloadUrl)
            {
                DownloadUrl = downloadUrl;
                AccessToken = ContentHash.Random().ToHex();
            }

            internal string GetUrl(Context context, string hash, [CanBeNull] string proxyBaseAddress)
            {
                if (proxyBaseAddress == null)
                {
                    return DownloadUrl;
                }

                return DeploymentProxyService.GetContentUrl(context, baseAddress: proxyBaseAddress, hash: hash, accessToken: AccessToken);
            }
        }
    }
}
