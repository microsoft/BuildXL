// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.ExternalApi;
using BuildXL.Ipc.Interfaces;
using BuildXL.Storage;
using BuildXL.Storage.Fingerprints;
using BuildXL.Utilities.CLI;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Core.Tasks;
using BuildXL.Utilities.ParallelAlgorithms;
using Tool.ServicePipDaemon;
using static BuildXL.Utilities.Core.FormattableStringEx;

namespace Tool.BlobDaemon
{
    /// <summary>
    /// The daemon responsible for uploading content into azure storage.
    /// If possible, the daemon tries to minimize materialization by copying 
    /// files from the cache blob storage into the target blob storage.
    /// </summary>
    public sealed partial class BlobDaemon : ServicePipDaemon.ServicePipDaemon, IIpcOperationExecutor
    {
        /// <nodoc/>
        public const string BlobDaemonLogPrefix = "(BD) ";
        private const string LogFileName = "BlobDaemon";

#if NET9_0_OR_GREATER
        private readonly System.Threading.Lock s_lock = new();
#else
        private static readonly object s_lock = new();
#endif

        private readonly BlobDaemonConfig m_config;
        private readonly ActionQueue m_actionQueue;
        private readonly CounterCollection<BlobDaemonCounter> m_counters;
        private readonly ConcurrentDictionary<string, BlobContainerClient> m_containerClients = new();

        #region Options and commands

        internal static readonly List<Option> ConfigOptions = new List<Option>();

        private static T RegisterBlobConfigOption<T>(T option) where T : Option => RegisterOption(ConfigOptions, option);

        internal static readonly IntOption MaxDegreeOfParallelism = RegisterBlobConfigOption(new IntOption("maxDegreeOfParallelism")
        {
            ShortName = "mdp",
            HelpText = "Maximum number of files to upload concurrently",
            IsRequired = true,
            DefaultValue = BlobDaemonConfig.DefaultMaxDegreeOfParallelism,
        });

        internal static readonly StrOption DirectoryContentFilter = new StrOption("directoryFilter")
        {
            ShortName = "dcfilter",
            HelpText = "Directory content filter (only files that match the filter will be uploaded).",
            DefaultValue = null,
            IsRequired = false,
            IsMultiValue = true,
        };

        // fileTarget and directoryTarget are essentially the same, but we need different names, so we can differentiate them in the payload.
        internal static readonly StrOption FileUploadTarget = new StrOption("fileTarget")
        {
            ShortName = "fut",
            HelpText = "Serialized value for the upload location for files",
            DefaultValue = null,
            IsRequired = false,
            IsMultiValue = true,
        };

        internal static readonly StrOption DirectoryUploadTarget = new StrOption("directoryTarget")
        {
            ShortName = "dut",
            HelpText = "Serialized value for the upload location for directories",
            DefaultValue = null,
            IsRequired = false,
            IsMultiValue = true,
        };

        // fileAuthVar and directoryAuthVar are essentially the same, but we need different names, so we can differentiate them in the payload.
        internal static readonly StrOption FileAuthEnvVar = new StrOption("fileAuthVar")
        {
            ShortName = "favn",
            HelpText = "Env var name that contains an auth secret for files.",
            DefaultValue = null,
            IsRequired = false,
            IsMultiValue = true,
        };

        internal static readonly StrOption DirectoryAuthEnvVar = new StrOption("directoryAuthVar")
        {
            ShortName = "davn",
            HelpText = "Env var name that contains an auth secret for directories.",
            DefaultValue = null,
            IsRequired = false,
            IsMultiValue = true,
        };

        internal static BlobDaemonConfig CreateBlobDaemonConfig(ConfiguredCommand conf)
        {
            return new BlobDaemonConfig(
                maxDegreeOfParallelism: conf.Get(MaxDegreeOfParallelism),
                logDir: conf.Get(LogDir));
        }

        private static Client CreateClient(string serverMoniker, IClientConfig config)
        {
            return serverMoniker != null
                ? Client.Create(IpcProvider, serverMoniker, config)
                : null;
        }

        internal static readonly Command StartCmd = RegisterCommand(
            name: "start",
            description: "Starts the server process.",
            options: new Option[] { IpcServerMonikerOptional, MaxDegreeOfParallelism },
            needsIpcClient: false,
            clientAction: (conf, _) =>
            {
                var blobDaemonConfig = CreateBlobDaemonConfig(conf);
                var daemonConf = CreateDaemonConfig(conf);

                if (daemonConf.MaxConcurrentClients <= 1)
                {
                    conf.Logger.Error($"Must specify at least 2 '{nameof(DaemonConfig.MaxConcurrentClients)}' when running daemon to avoid deadlock when stopping this daemon from a different client");
                    return -1;
                }

                using (var bxlApiClient = CreateClient(conf.Get(IpcServerMonikerOptional), daemonConf))
                using (var daemon = new BlobDaemon(
                    parser: conf.Config.Parser,
                    daemonConfig: daemonConf,
                    blobDaemonConfig: blobDaemonConfig,
                    bxlClient: bxlApiClient))
                {
                    daemon.Start();
                    // We are blocking the thread here and waiting for the daemon to process all the requests.
                    // Once the daemon receives 'stop' command, GetResult will return, and we'll leave this method
                    // (i.e., ServicePip will finish).
                    daemon.Completion.GetAwaiter().GetResult();
                    return 0;
                }
            });

        internal static readonly Command FinalizeCmd = RegisterCommand(
            name: "finalize",
            description: "noop",
            clientAction: SyncRPCSend,
            serverAction: (conf, daemon) =>
            {
                // No-op. We have to implement 'finalize' cause engine will schedule a finalize pip.
                return Task.FromResult(IpcResult.Success());
            });

        internal static readonly Command UploadArtifactsCmd = RegisterCommand(
            name: "uploadArtifacts",
            description: "Uploads artifacts to the specified blob storage",
            options: new Option[] { IpcServerMonikerRequired, File, FileId, HashOptional, Directory, DirectoryId, DirectoryContentFilter, DirectoryFilterUseRelativePath, DirectoryRelativePathReplace, FileUploadTarget, DirectoryUploadTarget, FileAuthEnvVar, DirectoryAuthEnvVar },
            clientAction: SyncRPCSend,
            serverAction: async (conf, daemon) =>
            {
                var blobDaemon = daemon as BlobDaemon;
                var commandId = Guid.NewGuid();
                blobDaemon.Logger.Verbose($"[command:{commandId}] [UPLOADARTIFACTS] Started");
                var result = await blobDaemon.UploadArtifactsInternalAsync(conf, commandId);
                LogIpcResult(blobDaemon.Logger, LogLevel.Verbose, $"[command:{commandId}] [UPLOADARTIFACTS] ", result);
                // Trim the payload before sending the result.
                return SuccessOrFirstError(result);
            });

        #endregion

        static BlobDaemon()
        {
            // noop (used to force proper initialization of static fields)
        }

        /// <nodoc/>
        public BlobDaemon(
            IParser parser,
            DaemonConfig daemonConfig,
            BlobDaemonConfig blobDaemonConfig,
            IIpcProvider rpcProvider = null,
            Client bxlClient = null)
                : base(parser,
                      daemonConfig,
                      !string.IsNullOrWhiteSpace(blobDaemonConfig.LogDir) ? new FileLogger(blobDaemonConfig.LogDir, LogFileName, daemonConfig.Moniker, logVerbose: true, BlobDaemonLogPrefix) : daemonConfig.Logger,
                      rpcProvider,
                      bxlClient)
        {
            m_config = blobDaemonConfig;
            m_actionQueue = new ActionQueue(m_config.MaxDegreeOfParallelism);
            m_counters = new();

            var json = JsonSerializer.Serialize(m_config, new JsonSerializerOptions { WriteIndented = true });
            m_logger.Info($"BlobDaemon config: {JsonSerializer.Serialize(m_config, new JsonSerializerOptions { WriteIndented = true })}");
        }

        /// <nodoc/>
        public override void Dispose()
        {
            ReportStatisticsAsync().GetAwaiter().GetResult();
            base.Dispose();
        }

        internal static void EnsureCommandsInitialized()
        {
            Contract.Assert(Commands != null);

            // these operations are quite expensive, however, we expect to call this method only once during a build, so it should not cause any perf downgrade
            var numCommandsBase = countFields(typeof(ServicePipDaemon.ServicePipDaemon));
            var numCommandsBlobDaemon = countFields(typeof(BlobDaemon));

            Contract.Assert(Commands.Count == numCommandsBase + numCommandsBlobDaemon, $"The list of commands was not properly initialized (# of initialized commands = {Commands.Count}; # of ServicePipDaemon commands = {numCommandsBase}; # of BlobDaemon commands = {numCommandsBlobDaemon})");

            int countFields(Type type)
            {
                return type.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static).Where(f => f.FieldType == typeof(Command)).Count();
            }
        }

        private async Task<IIpcResult> UploadArtifactsInternalAsync(ConfiguredCommand conf, Guid commandId)
        {
            m_counters.IncrementCounter(BlobDaemonCounter.UploadArtifactsRequestCount);

            var files = File.GetValues(conf.Config).ToArray();
            var fileIds = FileId.GetValues(conf.Config).ToArray();
            var hashes = HashOptional.GetValues(conf.Config).ToArray();
            var fileUploadLocations = FileUploadTarget.GetValues(conf.Config).ToArray();
            var fileAuthEnvVars = FileAuthEnvVar.GetValues(conf.Config).ToArray();

            if (files.Length != fileIds.Length || files.Length != hashes.Length || files.Length != fileUploadLocations.Length || files.Length != fileAuthEnvVars.Length)
            {
                return new IpcResult(
                    IpcResultStatus.GenericError,
                    I($"File counts don't match: #files = {files.Length}, #fileIds = {fileIds.Length}, #hashes = {hashes.Length}, #uploadLocations = {fileUploadLocations.Length}, #authEnvVars = {fileAuthEnvVars.Length}"));
            }

            var directoryPaths = Directory.GetValues(conf.Config).ToArray();
            var directoryIds = DirectoryId.GetValues(conf.Config).ToArray();
            var directoryUploadLocations = DirectoryUploadTarget.GetValues(conf.Config).ToArray();
            var directoryFilters = DirectoryContentFilter.GetValues(conf.Config).ToArray();
            var directoryFilterUseRelativePath = DirectoryFilterUseRelativePath.GetValues(conf.Config).ToArray();
            var directoryRelativePathsReplaceSerialized = DirectoryRelativePathReplace.GetValues(conf.Config).ToArray();
            var directoryAuthEnvVars = DirectoryAuthEnvVar.GetValues(conf.Config).ToArray();

            if (directoryPaths.Length != directoryIds.Length
               || directoryPaths.Length != directoryUploadLocations.Length
               || directoryPaths.Length != directoryFilters.Length
               || directoryPaths.Length != directoryFilterUseRelativePath.Length
               || directoryPaths.Length != directoryRelativePathsReplaceSerialized.Length
               || directoryPaths.Length != directoryAuthEnvVars.Length)
            {
                return new IpcResult(
                    IpcResultStatus.GenericError,
                    I($"Directory counts don't match: #directories = {directoryPaths.Length}, #directoryIds = {directoryIds.Length}, #uploadLocations = {directoryUploadLocations.Length}, #directoryFilters = {directoryFilters.Length}, #directoryApplyFilterToRelativePath = {directoryFilterUseRelativePath.Length}, #directoryRelativePathReplace = {directoryRelativePathsReplaceSerialized.Length}, #authEnvVars = {directoryAuthEnvVars.Length}"));
            }

            FileContentInfo[] parsedContentInfos = new FileContentInfo[files.Length];

            using (var pooledSb = Pools.GetStringBuilder())
            {
                var sb = pooledSb.Instance;
                sb.AppendLine($"[command:{commandId}] Payload:");
                for (int i = 0; i < files.Length; i++)
                {
                    sb.AppendFormat("{0}|{1}|{2}|{3}{4}", files[i], fileIds[i], hashes[i], fileUploadLocations[i], Environment.NewLine);
                    parsedContentInfos[i] = FileContentInfo.Parse(hashes[i]);
                }

                for (int i = 0; i < directoryPaths.Length; i++)
                {
                    sb.AppendFormat("{0}|{1}|{2}|{3}|{4}|{5}{6}",
                        directoryPaths[i], directoryIds[i], directoryUploadLocations[i], directoryFilters[i], directoryFilterUseRelativePath[i], directoryRelativePathsReplaceSerialized[i], Environment.NewLine);
                }

                m_logger.Verbose(sb);
            }

            var possibleFileUploadLocations = ParseUploadLocations(fileUploadLocations);
            if (!possibleFileUploadLocations.Succeeded)
            {
                return new IpcResult(IpcResultStatus.InvalidInput, possibleFileUploadLocations.Failure.Describe());
            }

            var possibleDirectoryUploadLocations = ParseUploadLocations(directoryUploadLocations);
            if (!possibleDirectoryUploadLocations.Succeeded)
            {
                return new IpcResult(IpcResultStatus.InvalidInput, possibleDirectoryUploadLocations.Failure.Describe());
            }

            var possibleFilters = InitializeFilters(directoryFilters, RegexOptions.NonBacktracking);
            if (!possibleFilters.Succeeded)
            {
                // bad regex can only be caused by bad input
                return new IpcResult(IpcResultStatus.InvalidInput, possibleFilters.Failure.Describe());
            }

            var possibleRelativePathReplacementArguments = InitializeRelativePathReplacementArguments(directoryRelativePathsReplaceSerialized);
            if (!possibleRelativePathReplacementArguments.Succeeded)
            {
                return new IpcResult(IpcResultStatus.ExecutionError, possibleRelativePathReplacementArguments.Failure.Describe());
            }

            var filesKeyedByIsAbsent = Enumerable
                .Range(0, files.Length)
                .Select(i => new FileToUpload(
                    ApiClient,
                    FilePath: files[i],
                    FileId: fileIds[i],
                    FileContentInfo: parsedContentInfos[i],
                    UploadLocation: possibleFileUploadLocations.Result[i],
                    AuthVar: fileAuthEnvVars[i]))
                .ToLookup(f => WellKnownContentHashUtilities.IsAbsentFileHash(f.FileContentInfo.Hash));

            // If a user specified a particular file to be uploaded, this file must be uploaded.
            // The missing files cannot be uploaded, so we emit an error.
            if (filesKeyedByIsAbsent[true].Any())
            {
                string missingFiles = string.Join(Environment.NewLine, filesKeyedByIsAbsent[true].Select(f => $"{f.FilePath} ({f.FileArtifact} - {f.FileContentInfo})"));
                return new IpcResult(
                    IpcResultStatus.InvalidInput,
                    I($"Cannot upload the following files because they do not exist:{Environment.NewLine}{missingFiles}"));
            }

            var filesFromDirectories = await ProcessDirectoriesAsync(
                this,
                directoryPaths,
                directoryIds,
                possibleDirectoryUploadLocations.Result,
                possibleFilters.Result,
                directoryFilterUseRelativePath,
                possibleRelativePathReplacementArguments.Result,
                directoryAuthEnvVars,
                commandId,
                m_logger);

            if (!filesFromDirectories.Succeeded)
            {
                return new IpcResult(IpcResultStatus.ExecutionError, filesFromDirectories.Failure.Describe());
            }

            var groupedDirectoriesContent = filesFromDirectories.Result.ToLookup(f => WellKnownContentHashUtilities.IsAbsentFileHash(f.FileContentInfo.Hash));

            // we allow missing files inside of directories only if those files are output files (e.g., optional or temporary files) 
            if (groupedDirectoriesContent[true].Any(f => !f.FileArtifact.IsOutputFile))
            {
                return new IpcResult(
                    IpcResultStatus.InvalidInput,
                    I($"Uploading missing source file(s) is not supported:{Environment.NewLine}{string.Join(Environment.NewLine, groupedDirectoriesContent[true].Where(f => !f.FileArtifact.IsOutputFile))}"));
            }

            // return early if there is nothing to upload
            if (!filesKeyedByIsAbsent[false].Any() && !groupedDirectoriesContent[false].Any())
            {
                return new IpcResult(IpcResultStatus.Success, string.Empty);
            }

            var individualFilesToUpload = filesKeyedByIsAbsent[false].Concat(groupedDirectoriesContent[false]).ToList();

            try
            {
                await m_actionQueue.ForEachAsync(
                   individualFilesToUpload,
                   async (file, i) =>
                   {
                       await UploadFileAsync(file);
                   });
            }
            catch (Exception e)
            {
                return new IpcResult(IpcResultStatus.GenericError, e.ToStringDemystified());
            }

            return IpcResult.Success(
                $"Uploaded files ({individualFilesToUpload.Count}):{Environment.NewLine}{string.Join(Environment.NewLine, individualFilesToUpload.Select(f => f.FilePath))}");
        }

        private async Task UploadFileAsync(FileToUpload file)
        {
            var blobClient = GetBlobClient(file.UploadLocation, file.AuthVar);

            // If it's an output artifact, we first try to copy it from the cache storage.
            if (file.FileArtifact.IsOutputFile)
            {
                var possibleSourceUri = await ApiClient.GetContentLocationInBlobStorage(file.FileContentInfo.Hash);
                if (possibleSourceUri.Succeeded && possibleSourceUri.Result != null)
                {
                    try
                    {
                        var status = await blobClient.StartCopyFromUriAsync(possibleSourceUri.Result);
                        await status.WaitForCompletionAsync(CancellationToken.None);

                        // we did not get any exceptions - success!
                        m_counters.IncrementCounter(BlobDaemonCounter.ServerSideCopyCount);
                        return;
                    }
                    catch
#pragma warning disable ERP022 // Unobserved exception in a generic exception handler
                    {
                        // no-op: we failed the copy between accounts. Fall-back to uploading from local machine.
                    }
#pragma warning restore ERP022 // Unobserved exception in a generic exception handler
                }
            }

            // We either failed to copy the file from the cache storage, and it's not an output artifact (i.e., it won't be in the cache storage).
            // First we need to ensure that the file is on disk.
            var possibleMaterialization = await ApiClient.MaterializeFile(file.FileArtifact, file.FilePath);
            if (!possibleMaterialization.Succeeded)
            {
                throw new InvalidOperationException($"Failed to materialize file '{file.FilePath}' ({file.FileId}): {possibleMaterialization.Failure.Describe()}");
            }

            await blobClient.UploadAsync(file.FilePath, overwrite: true);
            m_counters.IncrementCounter(BlobDaemonCounter.LocalUploadCount);
        }

        private BlobClient GetBlobClient(UploadLocation uploadLocation, string accessTokenVar)
        {
            if (uploadLocation.LocationKind == UploadLocationKind.UriBased)
            {
                // If it's URI based, just use the URI. We cannot reliably determine the account and container from the URI.
                BlobClient blobClient = new BlobClient(new Uri(uploadLocation.Uri), new StaticTokenCredential(Environment.GetEnvironmentVariable(accessTokenVar)));
                return blobClient;
            }
            else if (uploadLocation.LocationKind == UploadLocationKind.ContainerBased)
            {
                var key = $"{uploadLocation.Account}/{uploadLocation.Container}";
                if (m_containerClients.TryGetValue(key, out var containerClient))
                {
                    // If we already have a client for this account/container, use it.
                    return containerClient.GetBlobClient(uploadLocation.RelativePath);
                }

                lock (s_lock)
                {
                    if (m_containerClients.TryGetValue(key, out var innerContainerClient))
                    {
                        // If we already have a client for this account/container, use it.
                        return innerContainerClient.GetBlobClient(uploadLocation.RelativePath);
                    }

                    var credential = new StaticTokenCredential(Environment.GetEnvironmentVariable(accessTokenVar));
                    var serviceClient = new BlobServiceClient(new Uri(uploadLocation.Account), credential);
                    innerContainerClient = serviceClient.GetBlobContainerClient(uploadLocation.Container);
                    m_containerClients.TryAdd(key, innerContainerClient);

                    return innerContainerClient.GetBlobClient(uploadLocation.RelativePath);
                }
            }

            // should never reach here
            return null;
        }

        private static Possible<UploadLocation[]> ParseUploadLocations(string[] uploadLocations)
        {
            var result = new UploadLocation[uploadLocations.Length];
            for (int i = 0; i < uploadLocations.Length; i++)
            {
                var possibleLocation = UploadLocation.TryParse(uploadLocations[i]);
                if (!possibleLocation.Succeeded)
                {
                    return new Failure<string>($"Invalid upload location '{uploadLocations[i]}'");
                }

                result[i] = possibleLocation.Result;
            }

            return result;
        }

        private static async Task<Possible<IEnumerable<FileToUpload>>> ProcessDirectoriesAsync(
            BlobDaemon daemon,
            string[] directoryPaths,
            string[] directoryIds,
            UploadLocation[] uploadLocations,
            Regex[] contentFilters,
            bool[] applyFilterToRelativePath,
            RelativePathReplacementArguments[] relativePathsReplacementArgs,
            string[] authVars,
            Guid commandId,
            IIpcLogger logger)
        {
            Contract.Requires(directoryPaths != null);
            Contract.Requires(directoryIds != null);
            Contract.Requires(uploadLocations != null);
            Contract.Requires(contentFilters != null);
            Contract.Requires(directoryPaths.Length == directoryIds.Length);
            Contract.Requires(directoryPaths.Length == uploadLocations.Length);
            Contract.Requires(directoryPaths.Length == contentFilters.Length);
            Contract.Requires(directoryPaths.Length == applyFilterToRelativePath.Length);
            Contract.Requires(directoryPaths.Length == relativePathsReplacementArgs.Length);

            var processDirectoryTasks = Enumerable
                .Range(0, directoryPaths.Length)
                .Select(i => ProcessDirectoryAsync(
                    daemon, directoryPaths[i], directoryIds[i], uploadLocations[i], contentFilters[i], applyFilterToRelativePath[i], relativePathsReplacementArgs[i], authVars[i], logger, commandId))
                .ToArray();

            var results = await TaskUtilities.SafeWhenAll(processDirectoryTasks);

            if (results.Any(r => !r.Succeeded))
            {
                return new Failure<string>(
                    string.Join("; ", results.Where(r => !r.Succeeded).Select(r => r.Failure.Describe())));
            }

            return new Possible<IEnumerable<FileToUpload>>(results.SelectMany(r => r.Result));
        }

        private static async Task<Possible<FileToUpload[]>> ProcessDirectoryAsync(
            BlobDaemon daemon,
            string directoryPath,
            string directoryId,
            UploadLocation uploadLocation,
            Regex contentFilter,
            bool applyFilterToRelativePath,
            RelativePathReplacementArguments relativePathReplacementArguments,
            string authVar,
            IIpcLogger logger,
            Guid commandId)
        {
            Contract.Requires(!string.IsNullOrEmpty(directoryPath));
            Contract.Requires(!string.IsNullOrEmpty(directoryId));

            if (daemon.ApiClient == null)
            {
                return new Failure<string>("ApiClient is not initialized");
            }

            DirectoryArtifact directoryArtifact = BuildXL.Ipc.ExternalApi.DirectoryId.Parse(directoryId);

            var maybeResult = await daemon.ApiClient.GetSealedDirectoryContent(directoryArtifact, directoryPath);
            if (!maybeResult.Succeeded)
            {
                return new Failure<string>("Could not get the directory content from BuildXL server: " + maybeResult.Failure.Describe());
            }

            var directoryContent = maybeResult.Result;
            logger.Log(
                LogLevel.Verbose,
                $"[command:{commandId}] (dirPath'{directoryPath}', dirId='{directoryId}') contains '{directoryContent.Count}' files:",
                directoryContent.SelectMany(f => f.RenderContent().Append(Environment.NewLine)),
                placeItemsOnSeparateLines: false);

            if (contentFilter != null)
            {
                var filteredContent = FilterDirectoryContent(directoryPath, directoryContent, contentFilter, applyFilterToRelativePath);
                logger.Verbose("{5} [dirId='{0}'] Filter '{1}' (applied to relative paths: '{4}') excluded {2} file(s) out of {3}", directoryId, contentFilter, directoryContent.Count - filteredContent.Count, directoryContent.Count, applyFilterToRelativePath, commandId);
                directoryContent = filteredContent;
            }

            var files = directoryContent
                // SharedOpaque directories might contain 'absent' output files. These are not real files, so we are excluding them.
                .Where(file => !WellKnownContentHashUtilities.IsAbsentFileHash(file.ContentInfo.Hash) || file.Artifact.IsSourceFile);
            var result = new List<FileToUpload>();
            foreach (SealedDirectoryFile file in files)
            {
                // We append file's relative path to the upload location - either to URI or to the relative path.
                var uploadPath = uploadLocation.LocationKind == UploadLocationKind.UriBased
                    ? uploadLocation.Uri
                    : uploadLocation.RelativePath;
                // The uploadPath can be an empty relative path (i.e. '.') which means we are uploading to the root of the container,
                // so we need to resolve it to an empty string.
                var resolvedUploadPath = uploadPath == "." ? string.Empty : I($"{uploadPath}/");
                var remoteFileName = I($"{resolvedUploadPath}{GetRelativePath(directoryPath, file.FileName, relativePathReplacementArguments)}");
                var fileId = BuildXL.Ipc.ExternalApi.FileId.ToString(file.Artifact);

                var updatedUploadLocation = uploadLocation.LocationKind == UploadLocationKind.UriBased
                    ? UploadLocation.CreateUriBased(remoteFileName)
                    : UploadLocation.CreateContainerBased(uploadLocation.Account, uploadLocation.Container, remoteFileName);

                result.Add(new FileToUpload(
                    daemon.ApiClient,
                    FilePath: file.FileName,
                    FileId: fileId,
                    FileContentInfo: file.ContentInfo,
                    UploadLocation: updatedUploadLocation,
                    AuthVar: authVar));
            }

            return result.ToArray();
        }

        private async Task ReportStatisticsAsync()
        {
            var stats = m_counters.AsStatistics("BlobDaemon");
            stats.AddRange(GetDaemonStats("BlobDaemon"));
            m_logger.Info($"Statistics:{string.Join(string.Empty, stats.Select(s => $"{Environment.NewLine}{s.Key}={s.Value}"))}");

            // report stats to BuildXL
            if (ApiClient != null)
            {
                var possiblyReported = await ApiClient.ReportStatistics(stats);
                if (possiblyReported.Succeeded && possiblyReported.Result)
                {
                    m_logger.Info("Statistics successfully reported to BuildXL.");
                }
                else
                {
                    var errorDescription = possiblyReported.Succeeded ? string.Empty : possiblyReported.Failure.Describe();
                    m_logger.Warning("Reporting stats to BuildXL failed. " + errorDescription);
                }
            }
        }

        private enum BlobDaemonCounter
        {
            [CounterType(CounterType.Stopwatch)]
            GetSealedDirectoryContentDuration,

            UploadArtifactsRequestCount,

            ServerSideCopyCount,

            LocalUploadCount,
        }
    }
}
