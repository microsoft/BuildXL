using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.Host.Service;
using BuildXL.Utilities;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using static BuildXL.Cache.Host.Configuration.DeploymentManifest;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;
using BuildXL.Utilities.ParallelAlgorithms;
using BuildXL.Cache.ContentStore.Utils;

namespace BuildXL.Launcher.Server
{
    /// <summary>
    /// Service used ensure deployments are uploaded to target storage accounts and provide manifest for with download urls and tools to launch
    /// </summary>
    public class DeploymentService
    {
        /// <summary>
        /// The root of the mounted deployment folder created by the <see cref="DeploymentIngester"/>
        /// </summary>
        private AbsolutePath DeploymentRoot { get; }

        /// <summary>
        /// Cached expirable value for read deployment info
        /// </summary>
        private VolatileMap<UnitValue, (DeploymentManifest manifest, string configJson)> CachedDeploymentInfo { get; }

        /// <summary>
        /// Map for getting expirable sas urls by storage account and hash 
        /// </summary>
        private VolatileMap<(string storageName, string hash), AsyncLazy<string>> SasUrls { get; }

        /// <summary>
        /// Map from storage account secret name to target storage account
        /// </summary>
        private VolatileMap<string, AsyncLazy<CentralStorage>> StorageAccountsBySecretName { get; }

        private IClock Clock { get; }

        private ActionQueue UploadQueue { get; }

        /// <summary>
        /// The secrets provider used to get connection string secrets for storage accounts
        /// </summary>
        private ISecretsProvider SecretsProvider { get; }

        /// <summary>
        /// For testing purposes only. Used to intercept call to create blob central storage
        /// </summary>
        public Func<(string storageSecretName, AzureBlobStorageCredentials credentials), CentralStorage> OverrideCreateCentralStorage { get; set; }

        /// <nodoc />
        public DeploymentService(AbsolutePath deploymentRoot, ISecretsProvider secretsProvider, IClock clock, int uploadConcurrency = 1)
        {
            DeploymentRoot = deploymentRoot;
            Clock = clock;
            SecretsProvider = secretsProvider;
            StorageAccountsBySecretName = new VolatileMap<string, AsyncLazy<CentralStorage>>(clock);
            SasUrls = new VolatileMap<(string storageName, string hash), AsyncLazy<string>>(clock);
            CachedDeploymentInfo = new VolatileMap<UnitValue, (DeploymentManifest manifest, string configJson)>(clock);

            UploadQueue = new ActionQueue(uploadConcurrency);
        }

        // TODO [LANCEC]: Consider returning prior deployment until all files are uploaded.

        /// <summary>
        /// Uploads the deployment files to the target storage account and returns the launcher manifest for the given deployment parameters
        /// </summary>
        public async Task<LauncherManifest> UploadFilesAndGetManifestAsync(OperationContext context, DeploymentParameters parameters, bool waitForCompletion)
        {
            var resultManifest = new LauncherManifest();
            var deployConfig = ReadDeploymentConfiguration(parameters, out var manifest);

            var uploadTasks = new List<Task<(string targetPath, FileSpec spec)>>();

            resultManifest.Tool = deployConfig.Tool;
            resultManifest.Drops = deployConfig.Drops;

            var storage = await LoadStorage(context, deployConfig.AzureStorageSecretName);

            foreach (var drop in deployConfig.Drops)
            {
                if (drop.Url == null)
                {
                    continue;
                }

                var dropLayout = manifest.Drops[drop.Url];
                foreach (var fileEntry in dropLayout)
                {
                    // Queue file for deployment
                    uploadTasks.Add(ensureUploadedAndGetEntry());

                    async Task<(string targetPath, FileSpec entry)> ensureUploadedAndGetEntry()
                    {
                        var downloadUrl = await EnsureUploadedAndGetDownloadUrlAsync(context, fileEntry.Value, deployConfig, storage);

                        // Compute and record path in final layout
                        var targetPath = Path.Combine(drop.TargetRelativePath ?? string.Empty, fileEntry.Key);
                        return (targetPath, new FileSpec()
                        {
                            Hash = fileEntry.Value.Hash,
                            Size = fileEntry.Value.Size,
                            DownloadUrl = downloadUrl
                        });
                    }
                }
            }

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
                if (uploadTask.Status == TaskStatus.RanToCompletion)
                {
                    var entry = await uploadTask;
                    resultManifest.Deployment[entry.targetPath] = entry.spec;
                }
            }

            return resultManifest;
        }

        private Task<CentralStorage> LoadStorage(OperationContext context, string storageSecretName)
        {
            AsyncLazy<CentralStorage> lazyCentralStorage = GetOrAddExpirableAsyncLazy<string, CentralStorage>(
                StorageAccountsBySecretName,
                storageSecretName,
                TimeSpan.FromMinutes(30),
                () =>
                {
                    return UploadQueue.RunAsync(async () =>
                    {
                        var credentials = await SecretsProvider.GetBlobCredentialsAsync(
                            storageSecretName,
                            useSasTokens: true,
                            context.Token);

                        CentralStorage centralStorage = OverrideCreateCentralStorage?.Invoke((storageSecretName, credentials))
                        ?? new BlobCentralStorage(new BlobCentralStoreConfiguration(credentials,
                            containerName: "deploymentfiles",
                            checkpointsKey: "N/A"));

                        await centralStorage.StartupAsync(context).ThrowIfFailure();

                        return centralStorage;
                    });
                });

            return lazyCentralStorage.GetValueAsync();
        }

        /// <summary>
        /// Ensures the given file under the deployment root is uploaded to the specified storage account and returns the download url
        /// </summary>
        private Task<string> EnsureUploadedAndGetDownloadUrlAsync(OperationContext context, FileSpec value, DeploymentConfiguration configuration, CentralStorage storage)
        {
            var sasUrlTimeToLive = TimeSpan.FromMinutes(configuration.SasUrlTimeToLiveMinutes);
            var key = (configuration.AzureStorageSecretName, value.Hash);
            AsyncLazy<string> lazySasUrl = GetOrAddExpirableAsyncLazy(
                SasUrls,
                key,
                sasUrlTimeToLive,
                async () =>
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

            return lazySasUrl.GetValueAsync();
        }

        /// <summary>
        /// Gets the deployment configuration based on the manifest, preprocesses it, and returns the deserialized value
        /// </summary>
        private DeploymentConfiguration ReadDeploymentConfiguration(DeploymentParameters parameters, out DeploymentManifest manifest)
        {
            if (!CachedDeploymentInfo.TryGetValue(UnitValue.Unit, out var cachedValue))
            {
                var manifestText = File.ReadAllText(DeploymentUtilities.GetDeploymentManifestPath(DeploymentRoot).Path);

                manifest = JsonSerializer.Deserialize<DeploymentManifest>(manifestText);

                var configurationPath = DeploymentUtilities.GetDeploymentConfigurationPath(DeploymentRoot, manifest);

                var configJson = File.ReadAllText(configurationPath.Path);

                cachedValue = (manifest, configJson);
            }

            var preprocessor = new JsonPreprocessor(
                new Dictionary<string, string>()
                    {
                        { "Stamp", parameters.Stamp },
                        { "MachineFunction", parameters.MachineFunction },
                        { "Region", parameters.Region },
                        { "Ring", parameters.Ring },
                        { "Environment", parameters.Environment },
                    }
                    .Where(e => !string.IsNullOrEmpty(e.Value))
                    .Select(e => new ConstraintDefinition(e.Key, new[] { e.Value })),
                new Dictionary<string, string>()
                    {
                        { "Stamp", parameters.Stamp },
                        { "Region", parameters.Region },
                        { "Ring", parameters.Ring },
                    });

            var preprocessedConfigJson = preprocessor.Preprocess(cachedValue.configJson);

            var config = JsonSerializer.Deserialize<DeploymentConfiguration>(preprocessedConfigJson, DeploymentUtilities.ConfigurationSerializationOptions);

            CachedDeploymentInfo.TryAdd(UnitValue.Unit, cachedValue, TimeSpan.FromMinutes(5));

            manifest = cachedValue.manifest;

            return config;
        }

        private AsyncLazy<TValue> GetOrAddExpirableAsyncLazy<TKey, TValue>(
            VolatileMap<TKey, AsyncLazy<TValue>> map,
            TKey key,
            TimeSpan timeToLive,
            Func<Task<TValue>> func)
        {
            AsyncLazy<TValue> asyncLazyValue;
            while (!map.TryGetValue(key, out asyncLazyValue))
            {
                asyncLazyValue = new AsyncLazy<TValue>(func);
                map.TryAdd(key, asyncLazyValue, timeToLive);
            }

            return asyncLazyValue;
        }
    }
}
