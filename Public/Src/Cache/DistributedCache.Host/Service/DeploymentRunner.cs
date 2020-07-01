// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Utilities.ParallelAlgorithms;

namespace BuildXL.Cache.Host.Service
{
    /// <summary>
    /// Deploys drops/files from a given deployment configuration to a CAS store and writes manifests describing contents
    /// so that subsequent process (i.e. Deployment Service) can read files and proffer deployments to clients.
    /// </summary>
    public class DeploymentRunner
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

        #endregion

        private OperationContext Context { get; }

        private IAbsFileSystem FileSystem { get; }

        private PinRequest PinRequest { get; set; }

        /// <summary>
        /// Content store used to store files in content addressable layout under deployment root
        /// </summary>
        private FileSystemContentStoreInternal Store { get; }

        private Tracer Tracer { get; } = new Tracer(nameof(DeploymentRunner));

        /// <summary>
        /// Describes the drops specified in deployment configuration
        /// </summary>
        private Dictionary<string, DropLayout> Drops { get; } = new Dictionary<string, DropLayout>();

        private HashSet<ContentHash> PinHashes { get; } = new HashSet<ContentHash>();

        private ActionQueue ActionQueue { get; }

        /// <summary>
        /// For testing purposes only. Used to intercept launch of drop.exe process and run custom logic in its place
        /// </summary>
        public Func<(string exePath, string args, string dropUrl, string targetDirectory, string relativeRoot), BoolResult> OverrideLaunchDropProcess { get; set; }

        /// <nodoc />
        public DeploymentRunner(
            OperationContext context,
            AbsolutePath sourceRoot,
            AbsolutePath deploymentRoot,
            AbsolutePath deploymentConfigurationPath,
            IAbsFileSystem fileSystem,
            AbsolutePath dropExeFilePath,
            int retentionSizeGb,
            string dropToken)
        {
            Context = context;
            SourceRoot = sourceRoot;
            DeploymentConfigurationPath = deploymentConfigurationPath;
            DeploymentRoot = deploymentRoot;
            DeploymentManifestPath = DeploymentUtilities.GetDeploymentManifestPath(deploymentRoot);
            Store = new FileSystemContentStoreInternal(
                fileSystem,
                SystemClock.Instance,
                DeploymentUtilities.GetCasRootPath(deploymentRoot),
                new ConfigurationModel(new ContentStoreConfiguration(new MaxSizeQuota($"{retentionSizeGb}GB"))));
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
            public string Path { get; set; }

            public long Size { get; set; }

            public ContentHash Hash { get; set; }
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

                try
                {
                    await Store.StartupAsync(Context).ThrowIfFailure();

                    PinRequest = new PinRequest(Store.CreatePinContext());

                    // Read drop urls from deployment configuration
                    ReadAndDeployDeploymentConfiguration();

                    // Read deployment manifest to ascertain which drops have already been downloaded
                    // along with their contents
                    ReadDeploymentManifest();

                    // Download drops and store files into CAS
                    await DownloadAndStoreDropsAsync();

                    // Write the updated deployment describing files in drop
                    WriteDeploymentManifest();
                }
                finally
                {
                    await Store.ShutdownAsync(context).ThrowIfFailure();
                }

                return BoolResult.Success;
            });
        }

        /// <summary>
        /// Read drop urls from deployment configuration
        /// </summary>
        private void ReadAndDeployDeploymentConfiguration()
        {
            Context.PerformOperation(Tracer, () =>
            {
                string text = FileSystem.ReadAllText(DeploymentConfigurationPath);
                var document = JsonDocument.Parse(text);

                var urls = document.RootElement
                    .GetProperty(nameof(DeploymentConfiguration.Drops))
                    .EnumerateArray()
                    .SelectMany(e =>
                        e.EnumerateObject()
                         .Where(e => e.Name.StartsWith(nameof(DropDeploymentConfiguration.Url))))
                    .Select(e => e.Value.GetString());

                foreach (var url in urls)
                {
                    Drops[url] = ParseDropUrl(url);
                }

                // Deploy deployment configuration under deployment root so that deployment service can access it
                // NOTE: This is done as two step process to ensure file is replaced atomically.
                var targetDeployConfigPath = DeploymentUtilities.GetDeploymentConfiguationPath(DeploymentRoot);
                var targetDeployConfigTempPath = new AbsolutePath(targetDeployConfigPath.Path + ".tmp");
                FileSystem.CopyFile(DeploymentConfigurationPath, targetDeployConfigTempPath, replaceExisting: true);
                FileSystem.MoveFile(targetDeployConfigTempPath, targetDeployConfigPath, replaceExisting: true);

                return BoolResult.Success;
            },
           extraStartMessage: DeploymentConfigurationPath.ToString()).ThrowIfFailure();
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
        private void WriteDeploymentManifest()
        {
            Context.PerformOperation(Tracer, () =>
            {
                var deploymentManifest = new DeploymentManifest();
                foreach (var drop in Drops.Values)
                {
                    var layout = new DeploymentManifest.LayoutSpec();
                    foreach (var file in drop.Files)
                    {
                        layout[file.Path] = new DeploymentManifest.FileSpec()
                        {
                            Hash = file.Hash.Serialize(),
                            Size = file.Size
                        };
                    }

                    deploymentManifest.Drops[drop.Url] = layout;
                }

                var manifestText = JsonSerializer.Serialize(deploymentManifest, new JsonSerializerOptions()
                {
                    WriteIndented = true
                });

                // Write deployment manifest under deployment root for access by deployment service
                // NOTE: This is done as two step process to ensure file is replaced atomically.
                var tempDeploymentManifestPath = new AbsolutePath(DeploymentManifestPath.Path + ".tmp");
                FileSystem.WriteAllText(tempDeploymentManifestPath, manifestText);
                FileSystem.MoveFile(tempDeploymentManifestPath, DeploymentManifestPath, replaceExisting: true);
                return BoolResult.Success;
            },
            extraStartMessage: $"DropCount={Drops.Count}").ThrowIfFailure();
        }

        /// <summary>
        /// Read prior deployment manifest with description of drop contents
        /// </summary>
        private void ReadDeploymentManifest()
        {
            int foundDrops = 0;
            Context.PerformOperation(Tracer, () =>
            {
                if (!FileSystem.FileExists(DeploymentManifestPath))
                {
                    Tracer.OperationDebug(Context, $"No deployment manifest found at '{DeploymentManifestPath}'");
                    return BoolResult.Success;
                }

                var manifestText = FileSystem.ReadAllText(DeploymentManifestPath);
                var deploymentManifest = JsonSerializer.Deserialize<DeploymentManifest>(manifestText);

                foundDrops = deploymentManifest.Drops.Count;
                foreach (var dropEntry in deploymentManifest.Drops)
                {
                    if (Drops.TryGetValue(dropEntry.Key, out var layout))
                    {
                        foreach (var fileEntry in dropEntry.Value)
                        {
                            var fileSpec = fileEntry.Value;
                            layout.Files.Add(new FileSpec()
                            {
                                Path = fileEntry.Key,
                                Hash = new ContentHash(fileSpec.Hash),
                                Size = fileSpec.Size,
                            });
                        }

                        Tracer.OperationDebug(Context, $"Loaded drop '{dropEntry.Key}' with {dropEntry.Value.Count} files");
                    }
                    else
                    {
                        Tracer.OperationDebug(Context, $"Discarded drop '{dropEntry.Key}' with {dropEntry.Value.Count} files");
                    }
                }

                return BoolResult.Success;
            },
            messageFactory: r => $"ReadDropCount={foundDrops}").ThrowIfFailure();
        }

        /// <summary>
        /// Downloads and stores drops to CAS
        /// </summary>
        private Task DownloadAndStoreDropsAsync()
        {
            return Context.PerformOperationAsync(Tracer, async () =>
            {
                var hashes = Drops.Values.SelectMany(d => d.Files.Select(f => f.Hash)).ToList();

                var pinResults = await Store.PinAsync(Context, hashes, pinContext: PinRequest.PinContext, options: null);

                foreach (var pinResult in pinResults)
                {
                    if (pinResult.Item.Succeeded)
                    {
                        PinHashes.Add(hashes[pinResult.Index]);
                    }
                }

                foreach (var drop in Drops.Values)
                {
                    await DownloadAndStoreDropAsync(drop);
                }

                return BoolResult.Success;
            },
            extraStartMessage: $"DropCount={Drops.Count}").ThrowIfFailure();
        }

        /// <summary>
        /// Download and store a single drop to CAS
        /// </summary>
        private Task DownloadAndStoreDropAsync(DropLayout drop)
        {
            var context = Context.CreateNested(Tracer.Name);
            return context.PerformOperationAsync(Tracer, async () =>
            {
                // Can't skip local file drops since file system is mutable
                if (!drop.ParsedUrl.IsFile)
                {
                    if (drop.Files.Count != 0 && drop.Files.All(f => PinHashes.Contains(f.Hash)))
                    {
                        // If we loaded prior drop info and all drop contents are present in cache, just skip
                        return BoolResult.Success;
                    }
                }

                // Clear files since they will be repopulated below
                drop.Files.Clear();

                // Download and enumerate files associated with drop
                var files = DownloadDrop(context, drop);

                // Add file specs to drop
                foreach (var file in files)
                {
                    drop.Files.Add(new FileSpec()
                    {
                        Path = file.path.ToString()
                    });
                }

                // Stores files into CAS and populate file specs with hash and size info
                await ActionQueue.ForEachAsync(files, async (file, index) =>
                {
                    var result = await Store.PutFileAsync(context, file.fullPath, FileRealizationMode.Copy, HashType.MD5, PinRequest).ThrowIfFailure();

                    var spec = drop.Files[index];
                    spec.Hash = result.ContentHash;
                    spec.Size = result.ContentSize;

                    var targetPath = DeploymentRoot / DeploymentUtilities.GetContentRelativePath(result.ContentHash);

                    Contract.Check(FileSystem.FileExists(targetPath))?.Assert($"Could not find content for hash {result.ContentHash} at '{targetPath}'");
                });

                return BoolResult.Success;
            },
            extraStartMessage: drop.ToString(),
            extraEndMessage: r => drop.ToString()).ThrowIfFailure<BoolResult>();
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

                if (drop.ParsedUrl.IsFile)
                {
                    var path = SourceRoot / drop.ParsedUrl.LocalPath.TrimStart('\\');
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
                            var process = Process.Start(new ProcessStartInfo(DropExeFilePath.Path, args)
                            {
                                UseShellExecute = false
                            });

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
    }
}
