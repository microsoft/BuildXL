// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using Azure.Storage.Blobs;
using Azure.Identity;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Utilities.ParallelAlgorithms;
using BuildXL.Utilities.Collections;
using Azure.Storage.Blobs.Specialized;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;

namespace BuildXL.Cache.Host.Service.Deployment
{
    public class StorageDeploymentIngester
    {
        #region Configuration

        /// <summary>
        /// The deployment root directory under which CAS and deployment manifests will be stored
        /// </summary>
        public AbsolutePath DeploymentRoot { get; }

        /// <summary>
        /// The path to the configuration file describing urls of deployed drops/files
        /// </summary>
        public AbsolutePath DeploymentConfigurationPath { get; }

        /// <summary>
        /// The path to the manifest file describing contents of deployed drops/files
        /// </summary>
        public AbsolutePath DeploymentManifestPath { get; }

        /// <summary>
        /// The path to the source root from while files should be pulled
        /// </summary>
        public AbsolutePath SourceRoot { get; }

        /// <summary>
        /// The path to drop.exe used to download drops
        /// </summary>
        public AbsolutePath DropExeFilePath { get; }

        /// <summary>
        /// The personal access token used to deploy drop files
        /// </summary>
        private string DropToken { get; }

        private IngesterConfiguration Configuration { get; }

        #endregion

        private OperationContext Context { get; }

        private IAbsFileSystem FileSystem { get; }

        private PinRequest PinRequest { get; set; }

        private IReadOnlyList<StorageAccountsByRegion> StorageAccounts { get; set; }

        private Tracer Tracer { get; } = new Tracer(nameof(DeploymentIngester));

        private ActionQueue ActionQueue { get; }

        private ConcurrentBigSet<(string Region, ContentHash Hash)> UploadedContent { get; } = new();

        private readonly JsonPreprocessor _preprocessor = new JsonPreprocessor(new ConstraintDefinition[0], new Dictionary<string, string>());

        /// <summary>
        /// For testing purposes only. Used to intercept launch of drop.exe process and run custom logic in its place
        /// </summary>
        public Func<(string exePath, string args, string dropUrl, string targetDirectory, string relativeRoot), BoolResult> OverrideLaunchDropProcess { get; set; }

        public Func<(string accountName, string containerName), Task<BlobContainerClient>> OverrideGetContainer { get; set; }

        /// <nodoc />
        public StorageDeploymentIngester(
            OperationContext context,
            IngesterConfiguration configuration,
            AbsolutePath sourceRoot,
            AbsolutePath deploymentRoot,
            AbsolutePath deploymentConfigurationPath,
            IAbsFileSystem fileSystem,
            AbsolutePath dropExeFilePath,
            string dropToken)
        {
            Context = context;
            SourceRoot = sourceRoot;
            Configuration = configuration;
            DeploymentConfigurationPath = deploymentConfigurationPath;
            DeploymentRoot = deploymentRoot;
            DeploymentManifestPath = DeploymentUtilities.GetDeploymentManifestPath(deploymentRoot);
            FileSystem = fileSystem;
            DropExeFilePath = dropExeFilePath;
            DropToken = dropToken;

            ActionQueue = new ActionQueue(Environment.ProcessorCount);
        }

        /// <summary>
        /// Describes a file in drops
        /// </summary>
        private class FileSpec
        {
            public RelativePath TargetDeploymentPath { get; set; }

            public AbsolutePath SourcePath { get; set; }

            public long Size { get; set; }

            public ContentHash Hash { get; set; }

            public ContentHash Md5ChecksumForBlob { get; set; }
        }

        /// <summary>
        /// Describes the files in a drop 
        /// </summary>
        private class DropLayout
        {
            public string Url { get; set; }
            public Uri ParsedUrl { get; set; }

            public List<FileSpec> Files { get; } = new List<FileSpec>();

            public override string ToString()
            {
                return $"Url={Url} FileCount={Files.Count}";
            }
        }

        /// <summary>
        /// Run the deployment runner workflow to ingest drop files into deployment root
        /// </summary>
        public Task<BoolResult> RunAsync()
        {
            return Context.PerformOperationAsync(Tracer, async () =>
            {
                var context = Context.TracingContext;

                StorageAccounts = await ConstructStorageAccounts();

                // Read drop urls from deployment configuration
                IReadOnlyList<DropLayout> drops = GetDropsFromDeploymentConfiguration();

                DeploymentManifest previousDeploymentManifest = await ReadPreviousDeploymentManifestAsync();

                // Download drops and store files into CAS
                DeploymentManifest newManifest = await DownloadAndStoreDropsAsync(drops, previousDeploymentManifest);

                // Write the updated deployment describing files in drop
                await WriteDeploymentManifestsAsync(newManifest);

                return BoolResult.Success;
            });
        }

        private async Task<IReadOnlyList<StorageAccountsByRegion>> ConstructStorageAccounts()
        {
            async ValueTask<BlobContainerClient> getContainerClient(string accountName)
            {
                if (OverrideGetContainer != null)
                {
                    return await OverrideGetContainer((accountName, Configuration.ContentContainerName));
                }
                else
                {
                    var containerClient = new BlobContainerClient(new Uri($"https://{accountName}.blob.core.windows.net/{Configuration.ContentContainerName}"), new DefaultAzureCredential());
                    await containerClient.CreateIfNotExistsAsync();
                    return new BlobContainerClient(await GetUserDelegationContainerSasUri(containerClient), null);
                }
            }

            // Get a credential and create a service client object for the blob container.
            return await Configuration.StorageAccountsByRegion.ToAsyncEnumerable().SelectAwait(
                        async kv => new StorageAccountsByRegion(kv.Key,
                        await kv.Value.ToAsyncEnumerable().SelectAwait(async accountName => await getContainerClient(accountName)).ToArrayAsync())).ToListAsync();
        }

        private async static Task<Uri> GetUserDelegationContainerSasUri(BlobContainerClient blobContainerClient)
        {
            BlobServiceClient blobServiceClient = blobContainerClient.GetParentBlobServiceClient();

            // Get a user delegation key for the Blob service that's valid for seven days.
            // You can use the key to generate any number of shared access signatures 
            // over the lifetime of the key.
            Azure.Storage.Blobs.Models.UserDelegationKey userDelegationKey =
                await blobServiceClient.GetUserDelegationKeyAsync(DateTimeOffset.UtcNow,
                                                                  DateTimeOffset.UtcNow.AddDays(1));

            // Create a SAS token that's also valid for seven days.
            BlobSasBuilder sasBuilder = new BlobSasBuilder()
            {
                BlobContainerName = blobContainerClient.Name,
                Resource = "c",
                StartsOn = DateTimeOffset.UtcNow,
                ExpiresOn = DateTimeOffset.UtcNow.AddDays(7)
            };

            sasBuilder.SetPermissions(BlobAccountSasPermissions.All);

            // Add the SAS token to the container URI.
            BlobUriBuilder blobUriBuilder = new BlobUriBuilder(blobContainerClient.Uri)
            {
                // Specify the user delegation key.
                Sas = sasBuilder.ToSasQueryParameters(userDelegationKey,
                                                      blobServiceClient.AccountName)
            };

            Console.WriteLine("Container user delegation SAS URI: {0}", blobUriBuilder);
            Console.WriteLine();
            return blobUriBuilder.ToUri();
        }

        /// <summary>
        /// Read drop urls from deployment configuration
        /// </summary>
        private IReadOnlyList<DropLayout> GetDropsFromDeploymentConfiguration()
        {
            return Context.PerformOperation(Tracer, () =>
            {
                var drops = new List<DropLayout>();
                string text = FileSystem.ReadAllText(DeploymentConfigurationPath);
                var document = JsonDocument.Parse(text, DeploymentUtilities.ConfigurationDocumentOptions);

                var urls = document.RootElement
                    .EnumerateObject()
                    .Where(e => e.Name.StartsWith(nameof(DeploymentConfiguration.Drops)))
                    .SelectMany(d => d.Value
                        .EnumerateArray()
                        .SelectMany(e => GetUrlsFromDropElement(e)));

                foreach (var url in new[] { DeploymentUtilities.ConfigDropUri.ToString() }.Concat(urls))
                {
                    drops.Add(ParseDropUrl(url));
                }
                return Result.Success(drops);
            },
           extraStartMessage: DeploymentConfigurationPath.ToString()).ThrowIfFailure();
        }

        private IEnumerable<string> GetUrlsFromDropElement(JsonElement e)
        {
            IEnumerable<string> getValuesWithName(string name)
            {
                return e.EnumerateObject()
                .Where(p => _preprocessor.ParseNameWithoutConstraints(p).Equals(name))
                .Select(p => p.Value.GetString());
            }

            var baseUrls = getValuesWithName(nameof(DropDeploymentConfiguration.BaseUrl));
            var relativeRoots = getValuesWithName(nameof(DropDeploymentConfiguration.RelativeRoot));
            var fullUrls = getValuesWithName(nameof(DropDeploymentConfiguration.Url));

            var dropConfiguration = new DropDeploymentConfiguration();
            foreach (var baseUrl in baseUrls)
            {
                dropConfiguration.BaseUrl = baseUrl;
                foreach (var relativeRoot in relativeRoots)
                {
                    dropConfiguration.RelativeRoot = relativeRoot;
                    yield return dropConfiguration.EffectiveUrl;
                }
            }

            foreach (var fullUrl in fullUrls)
            {
                yield return fullUrl;
            }
        }

        private DropLayout ParseDropUrl(string url)
        {
            return new DropLayout()
            {
                Url = url,
                ParsedUrl = new Uri(url)
            };
        }

        /// <summary>
        /// Write out updated deployment manifest
        /// </summary>
        private Task WriteDeploymentManifestsAsync(DeploymentManifest newManifest)
        {
            return Context.PerformOperationAsync(Tracer, async () =>
            {
                var manifestText = JsonSerializer.Serialize(newManifest, new JsonSerializerOptions()
                {
                    WriteIndented = true
                });

                FileSystem.CreateDirectory(DeploymentManifestPath.Parent);
                FileSystem.WriteAllText(DeploymentManifestPath, manifestText);

                await UploadFilesAsync(Context, new DeploymentManifest.LayoutSpec(),
                    new FileSpec[]
                    {
                        new()
                        {
                            SourcePath = DeploymentManifestPath,
                            TargetDeploymentPath = DeploymentUtilities.DeploymentManifestRelativePath
                        }
                    });

                return BoolResult.Success;
            },
            extraStartMessage: $"DropCount={newManifest.Drops.Count}").ThrowIfFailureAsync();
        }

        /// <summary>
        /// Read prior deployment manifest with description of drop contents
        /// </summary>
        private Task<DeploymentManifest> ReadPreviousDeploymentManifestAsync()
        {
            return Context.PerformOperationAsync(Tracer, async () =>
            {
                // all storage accounts should have the manifest, just pick one
                var container = StorageAccounts[0].Accounts[0];
                var blob = container.GetBlockBlobClient(DeploymentUtilities.DeploymentManifestRelativePath.ToString());
                if (!await blob.ExistsAsync())
                {
                    return new DeploymentManifest();
                }

                var previousManifestContent = await blob.DownloadContentAsync();
                var previousManifest = JsonSerializer.Deserialize<DeploymentManifest>(previousManifestContent.Value.Content.ToString());

                return Result.Success(previousManifest);
            },
            extraEndMessage: r => $"ReadDropCount={r.GetValueOrDefault()?.Drops.Count ?? -1}").ThrowIfFailureAsync();
        }

        /// <summary>
        /// Downloads and stores drops to CAS
        /// </summary>
        private Task<DeploymentManifest> DownloadAndStoreDropsAsync(IReadOnlyList<DropLayout> drops, DeploymentManifest previousManifest)
        {
            return Context.PerformOperationAsync(Tracer, async () =>
            {
                var deploymentManifest = new DeploymentManifest();
                foreach (var drop in drops.Where(d => !previousManifest.Drops.ContainsKey(d.Url)))
                {
                    if (previousManifest.Drops.TryGetValue(drop.Url, out var dropLayout))
                    {
                        deploymentManifest.Drops[drop.Url] = dropLayout;
                    }
                    else
                    {
                        DeploymentManifest.LayoutSpec layout = await DownloadAndStoreDropAsync(drop);
                        deploymentManifest.Drops[drop.Url] = layout;
                    }

                }

                return Result.Success(deploymentManifest);
            },
            extraStartMessage: $"DropCount={drops.Count}").ThrowIfFailureAsync();
        }

        /// <summary>
        /// Download and store a single drop to CAS
        /// </summary>
        private Task<DeploymentManifest.LayoutSpec> DownloadAndStoreDropAsync(DropLayout drop)
        {
            var context = Context.CreateNested(Tracer.Name);
            return context.PerformOperationAsync(Tracer, async () =>
            {
                var layoutSpec = new DeploymentManifest.LayoutSpec();
                // Download and enumerate files associated with drop
                var files = DownloadDrop(context, drop).Select(paths => new FileSpec() { SourcePath = paths.fullPath, TargetDeploymentPath = paths.path }).ToList();

                await UploadFilesAsync(context, layoutSpec, files);

                return Result.Success(layoutSpec);
            },
            extraStartMessage: drop.ToString(),
            extraEndMessage: r => drop.ToString()).ThrowIfFailureAsync();
        }

        private async Task UploadFilesAsync(OperationContext context, DeploymentManifest.LayoutSpec layoutSpec, IReadOnlyList<FileSpec> files)
        {
            await ActionQueue.ForEachAsync(files, (file, index) =>
            {
                file.Hash = ContentHashingHelper.HashFile(file.SourcePath.ToString(), HashType.SHA256);
                file.Md5ChecksumForBlob = ContentHashingHelper.HashFile(file.SourcePath.ToString(), HashType.MD5);
                lock (layoutSpec)
                {
                    layoutSpec[file.TargetDeploymentPath.ToString()] = new DeploymentManifest.FileSpec { Hash = file.Hash.ToString(), Size = file.Size };
                }

                return Task.CompletedTask;
            });

            var filesxRegions = StorageAccounts.SelectMany(regionalAccounts => files.Select(fileSpec => (regionalAccounts, fileSpec)));
            await ActionQueue.ForEachAsync(filesxRegions, async (accountsAndFile, index) =>
            {
                if (UploadedContent.Add((accountsAndFile.regionalAccounts.Region, accountsAndFile.fileSpec.Hash)))
                {
                    string blobName = DeploymentUtilities.GetContentRelativePath(accountsAndFile.fileSpec.Hash).ToString();
                    var sasUrlToFile = await UploadFileToFirstStorageAccountAsync(context, accountsAndFile.fileSpec, blobName, accountsAndFile.regionalAccounts);
                    await ReplicateFileToOtherStorageAccountsAsync(context, blobName, sasUrlToFile, accountsAndFile.regionalAccounts);
                }
            });
        }

        private Task ReplicateFileToOtherStorageAccountsAsync(OperationContext context, string blobName, Uri sasUrlToFile, StorageAccountsByRegion regionalAccounts)
        {
            var otherAccounts = regionalAccounts.Accounts.Skip(1);
            return context.PerformOperationAsync(Tracer, async () =>
            {
                foreach (BlobContainerClient container in otherAccounts)
                {
                    await container.GetBlockBlobClient(blobName).SyncCopyFromUriAsync(sasUrlToFile);
                }
                return BoolResult.Success;
            },
            extraEndMessage: r => $"Blob={blobName} Region={regionalAccounts.Region} Accounts={otherAccounts.Count()}").ThrowIfFailureAsync();
        }

        private Task<Uri> UploadFileToFirstStorageAccountAsync(OperationContext context, FileSpec file, string blobName, StorageAccountsByRegion regionalAccounts)
        {
            var container = regionalAccounts.Accounts[0];
            return context.PerformOperationAsync(Tracer, async () =>
            {
                var uploadOptions = new BlobUploadOptions
                {
                    // Verify content on upload
                    HttpHeaders = new BlobHttpHeaders { ContentHash = file.Md5ChecksumForBlob.ToHashByteArray() }
                };

                using var fileStream = FileSystem.OpenReadOnly(file.SourcePath, FileShare.Read | FileShare.Delete);
                var blobClient = container.GetBlobClient(blobName);
                Azure.Response<BlobContentInfo> result = await blobClient.UploadAsync(fileStream, uploadOptions);

                return Result.Success(blobClient.Uri);
            },
            extraEndMessage: r => $"Blob={blobName} File={file.TargetDeploymentPath} Region={regionalAccounts.Region} Account={container.AccountName}").ThrowIfFailureAsync();
        }

        private RelativePath GetRelativePath(AbsolutePath path, AbsolutePath parent)
        {
            if (path.Path.TryGetRelativePath(parent.Path, out var relativePath))
            {
                return new RelativePath(relativePath);
            }
            else
            {
                throw Contract.AssertFailure($"'{path}' not under expected parent path '{parent}'");
            }
        }

        private IReadOnlyList<(RelativePath path, AbsolutePath fullPath)> DownloadDrop(OperationContext context, DropLayout drop)
        {
            return context.PerformOperation(Tracer, () =>
            {
                var files = new List<(RelativePath path, AbsolutePath fullPath)>();

                if (drop.ParsedUrl == DeploymentUtilities.ConfigDropUri)
                {
                    files.Add((new RelativePath(DeploymentUtilities.DeploymentConfigurationFileName), DeploymentConfigurationPath));
                }
                else if (drop.ParsedUrl.IsFile)
                {
                    var path = SourceRoot / drop.ParsedUrl.LocalPath.TrimStart('\\', '/');
                    if (FileSystem.DirectoryExists(path))
                    {
                        foreach (var file in FileSystem.EnumerateFiles(path, EnumerateOptions.Recurse))
                        {
                            files.Add((GetRelativePath(file.FullPath, parent: path), file.FullPath));
                        }
                    }
                    else
                    {
                        Contract.Assert(FileSystem.FileExists(path));
                        files.Add((new RelativePath(path.FileName), path));
                    }
                }
                else
                {
                    string relativeRoot = "";
                    if (!string.IsNullOrEmpty(drop.ParsedUrl.Query))
                    {
                        var query = HttpUtility.ParseQueryString(drop.ParsedUrl.Query);
                        relativeRoot = query.Get("root") ?? "";
                    }

                    var tempDirectory = FileSystem.GetTempPath() / Path.GetRandomFileName();
                    var args = $@"get -u {drop.Url} -d ""{tempDirectory}"" --patAuth {DropToken}";

                    context.PerformOperation(Tracer, () =>
                    {
                        FileSystem.CreateDirectory(tempDirectory);

                        if (OverrideLaunchDropProcess != null)
                        {
                            OverrideLaunchDropProcess((
                                exePath: DropExeFilePath.Path,
                                args: args,
                                dropUrl: drop.Url,
                                targetDirectory: tempDirectory.Path,
                                relativeRoot: relativeRoot)).ThrowIfFailure();
                        }
                        else
                        {
                            var process = new Process()
                            {
                                StartInfo = new ProcessStartInfo(DropExeFilePath.Path, args)
                                {
                                    UseShellExecute = false,
                                    RedirectStandardOutput = true,
                                    RedirectStandardError = true,
                                },
                            };

                            process.OutputDataReceived += (s, e) =>
                            {
                                Tracer.Debug(context, "Drop Output: " + e.Data);
                            };

                            process.ErrorDataReceived += (s, e) =>
                            {
                                Tracer.Error(context, "Drop Error: " + e.Data);
                            };

                            process.Start();
                            process.BeginOutputReadLine();
                            process.BeginErrorReadLine();

                            process.WaitForExit();

                            if (process.ExitCode != 0)
                            {
                                return new BoolResult($"Process exited with code: {process.ExitCode}");
                            }
                        }

                        var filesRoot = tempDirectory / relativeRoot;

                        foreach (var file in FileSystem.EnumerateFiles(filesRoot, EnumerateOptions.Recurse))
                        {
                            files.Add((GetRelativePath(file.FullPath, parent: filesRoot), file.FullPath));
                        }

                        return BoolResult.Success;
                    },
                    extraStartMessage: $"Url='{drop.Url}' Exe='{DropExeFilePath}' Args='{args.Replace(DropToken, "***")}' Root='{relativeRoot}'"
                    ).ThrowIfFailure();
                }

                return Result.Success<IReadOnlyList<(RelativePath path, AbsolutePath fullPath)>>(files);
            },
            extraStartMessage: drop.Url,
            messageFactory: r => r.Succeeded ? $"{drop.Url} FileCount={r.Value.Count}" : drop.Url).ThrowIfFailure();
        }

        private record StorageAccountsByRegion(string Region, BlobContainerClient[] Accounts);
        //private record ContainerClientAndDelegationKey(BlobContainerClient ContainerClient, UserDelegationKey DelegationKey);
    }
}
