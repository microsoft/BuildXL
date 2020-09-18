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
using BuildXL.Cache.ContentStore.Tracing;
using System.Threading;
using System.Text;

namespace BuildXL.Launcher.Server
{
    /// <summary>
    /// Service used ensure deployments are uploaded to target storage accounts and provide manifest for with download urls and tools to launch
    /// </summary>
    public class DeploymentService
    {
        private Tracer Tracer { get; } = new Tracer(nameof(DeploymentService));

        /// <summary>
        /// The root of the mounted deployment folder created by the <see cref="DeploymentIngester"/>
        /// </summary>
        private AbsolutePath DeploymentRoot { get; }

        /// <summary>
        /// Cached expirable value for read deployment info
        /// </summary>
        private VolatileMap<UnitValue, (DeploymentManifest manifest, string configJson)> CachedDeploymentInfo { get; }

        /// <summary>
        /// Cache of secrets for authorization
        /// </summary>
        private VolatileMap<string, string> AuthorizationSecretCache { get; }

        /// <summary>
        /// Map for getting expirable sas urls by storage account and hash 
        /// </summary>
        private VolatileMap<(string storageName, string hash), AsyncLazy<string>> SasUrls { get; }

        /// <summary>
        /// Map for getting expirable secrets by name, kind, and time to live
        /// </summary>
        private VolatileMap<(string secretName, SecretKind kind), AsyncLazy<string>> CachedSecrets { get; }

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
            CachedSecrets = new VolatileMap<(string secretName, SecretKind kind), AsyncLazy<string>>(clock);

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
                    var deployConfig = ReadDeploymentConfiguration(parameters, out var deploymentManifest, out var contentId);
                    if (!deployConfig.AuthorizationSecretNames.Contains(parameters.AuthorizationSecretName))
                    {
                        throw new UnauthorizedAccessException($"Secret names do not match: Expected='{string.Join(", ", deployConfig.AuthorizationSecretNames)}' Actual='{parameters.AuthorizationSecretName}'");
                    }

                    var secret = await GetSecretAsync(context, new SecretConfiguration()
                    {
                        Name = parameters.AuthorizationSecretName,
                        TimeToLiveMinutes = deployConfig.AuthorizationSecretTimeToLiveMinutes
                    });

                    if (secret != parameters.AuthorizationSecret)
                    {
                        throw new UnauthorizedAccessException($"Secret values do not match for secret name: '{parameters.AuthorizationSecretName}'");
                    }

                    return BoolResult.Success;
                });

            return result.Succeeded;
        }

        /// <summary>
        /// Uploads the deployment files to the target storage account and returns the launcher manifest for the given deployment parameters
        /// </summary>
        public Task<LauncherManifest> UploadFilesAndGetManifestAsync(OperationContext context, DeploymentParameters parameters, bool waitForCompletion)
        {
            int pendingFiles = 0;
            int totalFiles = 0;
            int completedFiles = 0;
            int pendingSecrets = 0;
            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    var resultManifest = new LauncherManifest();
                    var deployConfig = ReadDeploymentConfiguration(parameters, out var deploymentManifest, out var contentId);

                    resultManifest.ContentId = contentId;

                    var uploadTasks = new List<Task<(string targetPath, FileSpec spec)>>();

                    resultManifest.Tool = deployConfig.Tool;
                    resultManifest.Drops = deployConfig.Drops;

                    var storage = await LoadStorageAsync(context, deployConfig.AzureStorageSecretInfo);

                    foreach (var drop in deployConfig.Drops)
                    {
                        if (drop.Url == null)
                        {
                            continue;
                        }

                        var dropLayout = deploymentManifest.Drops[drop.Url];
                        foreach (var fileEntry in dropLayout)
                        {
                            // Queue file for deployment
                            uploadTasks.Add(ensureUploadedAndGetEntry());

                            async Task<(string targetPath, FileSpec entry)> ensureUploadedAndGetEntry()
                            {
                                var downloadUrl = parameters.GetContentInfoOnly
                                    ? null
                                    : await EnsureUploadedAndGetDownloadUrlAsync(context, fileEntry.Value, deployConfig, storage);

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

                    if (deployConfig.Tool?.SecretEnvironmentVariables != null)
                    {
                        // Populate environment variables from secrets.
                        foreach (var secretEnvironmentVariable in deployConfig.Tool.SecretEnvironmentVariables)
                        {
                            var secretTask = GetSecretAsync(context, secretEnvironmentVariable.Value);
                            if (secretTask.IsCompleted || waitForCompletion)
                            {
                                var secretValue = await secretTask;
                                resultManifest.Tool.EnvironmentVariables[secretEnvironmentVariable.Key] = secretValue;
                            }
                            else
                            {
                                pendingSecrets++;
                            }
                        }
                    }

                    resultManifest.IsComplete = pendingFiles == 0 && pendingSecrets == 0;

                    return Result.Success(resultManifest);
                },
                extraStartMessage: $"Machine={parameters.Machine} Stamp={parameters.Stamp} Wait={waitForCompletion}",
                extraEndMessage: r => $"Machine={parameters.Machine} Stamp={parameters.Stamp} Id={r.GetValueOrDefault()?.ContentId} Drops={r.GetValueOrDefault()?.Drops.Count ?? 0} Files[Total={totalFiles}, Pending={pendingFiles}, Completed={completedFiles}] PendingSecrets={pendingSecrets} Wait={waitForCompletion}"
                ).ThrowIfFailureAsync();
        }

        private Task<string> GetSecretAsync(OperationContext context, SecretConfiguration secretInfo)
        {
            AsyncLazy<string> lazySecret = GetOrAddExpirableAsyncLazy<(string, SecretKind), string>(
                CachedSecrets,
                (secretInfo.Name, SecretKind.PlainText),
                TimeSpan.FromMinutes(secretInfo.TimeToLiveMinutes),
                () =>
                {
                    return context.PerformOperationAsync(
                        Tracer,
                        async () =>
                        {
                            return Result.Success(await SecretsProvider.GetPlainSecretAsync(secretInfo.Name, context.Token));
                        },
                        extraEndMessage: r => $"Name={secretInfo.Name} TimeToLiveMinutes={secretInfo.TimeToLiveMinutes}").ThrowIfFailureAsync();
                });

            return lazySecret.GetValueAsync();
        }

        private Task<CentralStorage> LoadStorageAsync(OperationContext context, SecretConfiguration storageSecretInfo)
        {
            AsyncLazy<CentralStorage> lazyCentralStorage = GetOrAddExpirableAsyncLazy<string, CentralStorage>(
                StorageAccountsBySecretName,
                storageSecretInfo.Name,
                TimeSpan.FromMinutes(storageSecretInfo.TimeToLiveMinutes),
                async () =>
                {
                    var credentials = await SecretsProvider.GetBlobCredentialsAsync(
                        storageSecretInfo.Name,
                        useSasTokens: false,
                        context.Token);

                    CentralStorage centralStorage = OverrideCreateCentralStorage?.Invoke((storageSecretInfo.Name, credentials))
                    ?? new BlobCentralStorage(new BlobCentralStoreConfiguration(credentials,
                        containerName: "deploymentfiles",
                        checkpointsKey: "N/A"));

                    await centralStorage.StartupAsync(context).ThrowIfFailure();

                    return centralStorage;
                });

            return lazyCentralStorage.GetValueAsync();
        }

        /// <summary>
        /// Ensures the given file under the deployment root is uploaded to the specified storage account and returns the download url
        /// </summary>
        private Task<string> EnsureUploadedAndGetDownloadUrlAsync(OperationContext context, FileSpec value, DeploymentConfiguration configuration, CentralStorage storage)
        {
            var sasUrlTimeToLive = TimeSpan.FromMinutes(configuration.SasUrlTimeToLiveMinutes);
            var key = (configuration.AzureStorageSecretInfo.Name, value.Hash);
            AsyncLazy<string> lazySasUrl = GetOrAddExpirableAsyncLazy(
                SasUrls,
                key,
                sasUrlTimeToLive,
                async () =>
                {
                    try
                    {
                        await Task.Yield();

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
        private DeploymentConfiguration ReadDeploymentConfiguration(DeploymentParameters parameters, out DeploymentManifest manifest, out string contentId)
        {
            if (!CachedDeploymentInfo.TryGetValue(UnitValue.Unit, out var cachedValue))
            {
                var manifestText = File.ReadAllText(DeploymentUtilities.GetDeploymentManifestPath(DeploymentRoot).Path);

                manifest = JsonSerializer.Deserialize<DeploymentManifest>(manifestText);

                var configurationPath = DeploymentUtilities.GetDeploymentConfigurationPath(DeploymentRoot, manifest);

                var configJson = File.ReadAllText(configurationPath.Path);

                cachedValue = (manifest, configJson);
            }

            var preprocessor = DeploymentUtilities.GetHostJsonPreprocessor(parameters);

            var preprocessedConfigJson = preprocessor.Preprocess(cachedValue.configJson);
            contentId = ContentHashers.Get(HashType.Murmur).GetContentHash(Encoding.UTF8.GetBytes(preprocessedConfigJson)).ToHex().Substring(0, 16);

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
