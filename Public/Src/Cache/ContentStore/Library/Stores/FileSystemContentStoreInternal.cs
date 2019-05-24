// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BuildXL.Cache.ContentStore.Exceptions;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Cache.ContentStore.Synchronization;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.UtilitiesCore.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;
using ContractUtilities = BuildXL.Cache.ContentStore.Utils.ContractUtilities;
using FileInfo = BuildXL.Cache.ContentStore.Interfaces.FileSystem.FileInfo;
using RelativePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.RelativePath;

namespace BuildXL.Cache.ContentStore.Stores
{
    /// <summary>
    ///     Callback to update last access time based on external access times.
    /// </summary>
    public delegate Task UpdateContentWithLastAccessTimeAsync(ContentHash contentHash, DateTime dateTime);

    /// <summary>
    ///    Checks if content is pinned locally and returns its content size.
    /// </summary>
    public delegate long PinnedSizeChecker(Context context, ContentHash contentHash);

    /// <summary>
    ///     Content addressable store where content is stored on disk
    /// </summary>
    /// <remarks>
    ///     CacheRoot               C:\blah\Cache
    ///     CacheShareRoot          C:\blah\Cache\Shared          \\machine\CAS
    ///     CacheContentRoot        C:\blah\Cache\Shared\SHA1
    ///     ContentHashRoot    C:\blah\Cache\Shared\SHA1\abc
    /// </remarks>
    public class FileSystemContentStoreInternal : StartupShutdownBase, IContentStoreInternal, IContentDirectoryHost
    {
        /// <nodoc />
        public const bool DefaultApplyDenyWriteAttributesOnContent = false;

        /// <summary>
        ///     Public name for monitoring use.
        /// </summary>
        public const string Component = "FileSystemContentStore";

        /// <summary>
        ///     Name of counter for current size.
        /// </summary>
        public const string CurrentByteCountName = Component + ".CurrentByteCount";

        /// <summary>
        ///     Name of counter for current number of blobs.
        /// </summary>
        public const string CurrentFileCountName = Component + ".CurrentFileCount";

        /// <summary>
        ///     A history of the schema versions with comments describing the changes
        /// </summary>
        protected enum VersionHistory
        {
            /// <summary>
            ///     Content in CAS was marked read-only
            /// </summary>
            Version0 = 0,

            /// <summary>
            ///     Content in C:\blah\Cache\SHA1
            /// </summary>
            Version1 = 1,

            /// <summary>
            ///     Content in C:\blah\Cache\Shared\SHA1
            /// </summary>
            Version2 = 2,

            /// <summary>
            ///     Content with attribute normalization and Deny WriteAttributes set
            /// </summary>
            CurrentVersion = 3
        }

        // TODO: Adjust defaults (bug 1365340)
        private const int ParallelPlaceFilesLimit = 8;

        /// <summary>
        ///     Format string for blob files
        /// </summary>
        private const string BlobNameExtension = "blob";

        private static readonly int BlobNameExtensionLength = BlobNameExtension.Length;

        /// <summary>
        ///     Directory to write temp files in.
        /// </summary>
        private const string TempFileSubdirectory = "temp";

        /// <summary>
        ///     Length of subdirectory names used for storing files. For example with length 3,
        ///     content with hash "abcdefg" will be stored in $root\abc\abcdefg.blob.
        /// </summary>
        private const int HashDirectoryNameLength = 3;

        /// <summary>
        ///     Name of the file in the cache directory that keeps track of the local CAS version.
        /// </summary>
        private const string VersionFileName = "LocalCAS.version";

        /// <nodoc />
        protected IAbsFileSystem FileSystem { get; }

        /// <nodoc />
        protected IClock Clock { get; }

        /// <nodoc />
        public AbsolutePath RootPath { get; }

        /// <inheritdoc />
        protected override Tracer Tracer => _tracer;

        /// <nodoc />
        public ContentStoreConfiguration Configuration { get; private set; }

        /// <summary>
        ///     Gets helper to read/write version numbers
        /// </summary>
        protected SerializedDataValue SerializedDataVersion { get; }

        private readonly ContentStoreInternalTracer _tracer = new ContentStoreInternalTracer();

        private readonly ConfigurationModel _configurationModel;

        private readonly AbsolutePath _contentRootDirectory;
        private readonly AbsolutePath _tempFolder;

        private readonly Dictionary<HashType, IContentHasher> _hashers;

        /// <summary>
        ///     LockSet used to ensure thread safety on write operations.
        /// </summary>
        private readonly LockSet<ContentHash> _lockSet = new LockSet<ContentHash>();

        private readonly NagleQueue<ContentHash> _nagleQueue;

        private bool _applyDenyWriteAttributesOnContent;

        private IContentChangeAnnouncer _announcer;

        /// <summary>
        ///     Path to the file in the cache directory that keeps track of the local CAS version.
        /// </summary>
        protected readonly AbsolutePath VersionFilePath;

        /// <summary>
        ///     List of cached files and their metadata.
        /// </summary>
        protected readonly IContentDirectory ContentDirectory;

        /// <nodoc />
        protected QuotaKeeper QuotaKeeper;

        /// <summary>
        ///     Tracker for the number of times each content has been pinned.
        /// </summary>
        protected readonly ConcurrentDictionary<ContentHash, Pin> PinMap = new ConcurrentDictionary<ContentHash, Pin>();

        /// <summary>
        ///     Tracker for the index of the most recent successful hardlinked replica for each hash.
        /// </summary>
        private readonly ConcurrentDictionary<ContentHash, int> _replicaCursors = new ConcurrentDictionary<ContentHash, int>();

        /// <summary>
        ///     Hook for performing some action immediately prior to evicting a file from the cache.
        /// </summary>
        /// <remarks>
        ///     For example, used in some tests to artificially slow the purger in order to simulate slower I/O for some scenarios.
        /// </remarks>
        private readonly Action _preEvictFileAction = null;

        /// <summary>
        ///     Configuration for Distributed Eviction.
        /// </summary>
        private readonly DistributedEvictionSettings _distributedEvictionSettings;

        /// <summary>
        /// Stream containing the empty file.
        /// </summary>
        private readonly Stream _emptyFileStream = new MemoryStream(CollectionUtilities.EmptyArray<byte>(), false);

        /// <summary>
        ///     Cumulative count of instances of the content directory being discovered as out of sync with the disk.
        /// </summary>
        private long _contentDirectoryMismatchCount;

        private BackgroundTaskTracker _taskTracker;

        private PinSizeHistory _pinSizeHistory;

        private int _pinContextCount;

        private long _maxPinSize;

        private readonly ContentStoreSettings _settings;

        private readonly FileSystemContentStoreInternalChecker _checker;

        /// <summary>
        ///     Initializes a new instance of the <see cref="FileSystemContentStoreInternal" /> class.
        /// </summary>
        public FileSystemContentStoreInternal(
            IAbsFileSystem fileSystem,
            IClock clock,
            AbsolutePath rootPath,
            ConfigurationModel configurationModel = null,
            NagleQueue<ContentHash> nagleQueue = null,
            DistributedEvictionSettings distributedEvictionSettings = null,
            ContentStoreSettings settings = null)
        {
            Contract.Requires(fileSystem != null);
            Contract.Requires(clock != null);

            _hashers = HashInfoLookup.CreateAll();
            int maxContentPathLengthRelativeToCacheRoot = GetMaxContentPathLengthRelativeToCacheRoot();

            RootPath = rootPath;
            if ((RootPath.Path.Length + 1 + maxContentPathLengthRelativeToCacheRoot) >= FileSystemConstants.MaxPath)
            {
                throw new CacheException("Root path does not provide enough room for cache paths to fit MAX_PATH");
            }

            _nagleQueue = nagleQueue;
            Clock = clock;
            FileSystem = fileSystem;
            _configurationModel = configurationModel ?? new ConfigurationModel();

            _contentRootDirectory = RootPath / Constants.SharedDirectoryName;
            _tempFolder = _contentRootDirectory / TempFileSubdirectory;

            VersionFilePath = RootPath / VersionFileName;

            SerializedDataVersion = new SerializedDataValue(FileSystem, VersionFilePath, (int)VersionHistory.CurrentVersion);

            ContentDirectory = new MemoryContentDirectory(FileSystem, RootPath, this);

            _pinContextCount = 0;
            _maxPinSize = -1;

            _distributedEvictionSettings = distributedEvictionSettings;
            _settings = settings ?? ContentStoreSettings.DefaultSettings;

            _checker = new FileSystemContentStoreInternalChecker(FileSystem, Clock, RootPath, _tracer, _settings, this);
        }

        private async Task PerformUpgradeToNextVersionAsync(Context context, VersionHistory currentVersion)
        {
            Contract.Requires(currentVersion < VersionHistory.CurrentVersion);

            foreach (var hashName in HashInfoLookup.All().Select(hashInfo => hashInfo.Name))
            {
                AbsolutePath v0ContentFolder = RootPath / hashName;
                Func<IEnumerable<AbsolutePath>> enumerateVersion0Blobs = () => FileSystem
                    .EnumerateFiles(v0ContentFolder, EnumerateOptions.Recurse)
                    .Where(fileInfo => fileInfo.FullPath.Path.EndsWith(BlobNameExtension, StringComparison.OrdinalIgnoreCase))
                    .Select(fileInfo => fileInfo.FullPath);

                switch (currentVersion)
                {
                    case VersionHistory.Version0:
                        Upgrade0To1(enumerateVersion0Blobs, v0ContentFolder);
                        break;
                    case VersionHistory.Version1:
                        Upgrade1To2(enumerateVersion0Blobs, v0ContentFolder);
                        break;
                    case VersionHistory.Version2:
                        await Upgrade2To3(context);
                        break;

                    default:
                        throw ContractUtilities.AssertFailure("Version migration code must be added.");
                }
            }

            SerializedDataVersion.WriteValueFile((int)currentVersion + 1);
            _tracer.Debug(context, $"version is now {(int)currentVersion + 1}");
        }

        private void Upgrade0To1(Func<IEnumerable<AbsolutePath>> enumerateVersion0Blobs, AbsolutePath v0ContentFolder)
        {
            if (FileSystem.DirectoryExists(v0ContentFolder))
            {
                Parallel.ForEach(
                    enumerateVersion0Blobs(),
                    path =>
                    {
                        FileAttributes attributes = FileSystem.GetFileAttributes(path);
                        if ((attributes & FileAttributes.ReadOnly) != 0)
                        {
                            FileSystem.SetFileAttributes(path, attributes & ~FileAttributes.ReadOnly);
                        }
                    });
            }
        }

        private void Upgrade1To2(Func<IEnumerable<AbsolutePath>> enumerateVersion0Blobs, AbsolutePath v0ContentFolder)
        {
            if (FileSystem.DirectoryExists(v0ContentFolder))
            {
                Parallel.ForEach(
                    enumerateVersion0Blobs(),
                    oldPath =>
                    {
                        if (TryGetHashFromPath(oldPath, out var hash))
                        {
                            AbsolutePath newPath = GetPrimaryPathFor(hash);
                            FileSystem.CreateDirectory(newPath.Parent);
                            FileSystem.MoveFile(oldPath, newPath, true);
                        }
                    });
                FileSystem.DeleteDirectory(v0ContentFolder, DeleteOptions.All);
            }
        }

        private Task Upgrade2To3(Context context)
        {
            var upgradeCacheBlobAction = new ActionBlock<FileInfo>(
                blobPath => ApplyPermissions(context, blobPath.FullPath, FileAccessMode.ReadOnly),
                new ExecutionDataflowBlockOptions {MaxDegreeOfParallelism = Environment.ProcessorCount});

            return upgradeCacheBlobAction.PostAllAndComplete(EnumerateBlobPathsFromDisk());
        }

        /// <summary>
        /// Checks that the content on disk is correct and every file in content directory matches it's hash.
        /// </summary>
        /// <returns></returns>
        public async Task<Result<SelfCheckResult>> SelfCheckContentDirectoryAsync(Context context, CancellationToken token)
        {
            using (var disposableContext = TrackShutdown(context, token))
            {
                return await _checker.SelfCheckContentDirectoryAsync(disposableContext.Context);
            }
        }

        /// <summary>
        /// Removes invalid content from cache.
        /// </summary>
        internal async Task RemoveInvalidContentAsync(OperationContext context, ContentHash contentHash)
        {
            // In order to remove the content we have to do the following things:
            // Remove file from disk
            // Update memory content directory
            // Update quota keeper
            // Notify distributed store that the content is gone from this machine
            // The first 3 things are happening in the first call.
            await EvictCoreAsync(
                context,
                new ContentHashWithLastAccessTimeAndReplicaCount(contentHash, Clock.UtcNow),
                force: true, // Need to evict an invalid content even if it is pinned.
                onlyUnlinked: false,
                size => { QuotaKeeper.OnContentEvicted(size); })
                .TraceIfFailure(context);

            if (_distributedEvictionSettings?.DistributedStore != null)
            {
                await _distributedEvictionSettings.DistributedStore.UnregisterAsync(context, new ContentHash[] {contentHash}, context.Token)
                    .TraceIfFailure(context);
            }
        }

        internal async Task<ContentHashWithSize?> TryHashFileAsync(Context context, AbsolutePath path, HashType hashType, Func<Stream, Stream> wrapStream = null)
        {
            // We only hash the file if a trusted hash is not supplied
            using (var stream = await FileSystem.OpenAsync(path, FileAccess.Read, FileMode.Open, FileShare.Read | FileShare.Delete))
            {
                if (stream == null)
                {
                    return null;
                }

                using (var wrappedStream = (wrapStream == null) ? stream : wrapStream(stream))
                {
                    // Hash the file in  place
                    return await HashContentAsync(context, wrappedStream, hashType, path);
                }
            }
        }

        /// <summary>
        /// Unit testing hook.
        /// </summary>
        protected virtual void UpgradeLegacyVsoContentRenameFile(AbsolutePath sourcePath, AbsolutePath destinationPath)
        {
            FileSystem.MoveFile(sourcePath, destinationPath, replaceExisting: false);
        }

        /// <summary>
        /// Unit testing hook.
        /// </summary>
        protected virtual void UpgradeLegacyVsoContentRenameDirectory(AbsolutePath sourcePath, AbsolutePath destinationPath)
        {
            FileSystem.MoveDirectory(sourcePath, destinationPath);
        }

        /// <summary>
        /// Unit testing hook.
        /// </summary>
        protected virtual void UpgradeLegacyVsoContentDeleteDirectory(AbsolutePath directory)
        {
            FileSystem.DeleteDirectory(directory, DeleteOptions.All);
        }

        private async Task UpgradeLegacyVsoContentAsync(Context context)
        {
            var legacyVsoContentRootPath = _contentRootDirectory / ((int)HashType.DeprecatedVso0).ToString();

            if (FileSystem.DirectoryExists(legacyVsoContentRootPath))
            {
                var vsoContentRootPath = _contentRootDirectory / HashType.Vso0.ToString();

                try
                {
                    UpgradeLegacyVsoContentRenameDirectory(legacyVsoContentRootPath, vsoContentRootPath);
                }
                catch (Exception renameDirException)
                {
                    context.Warning(
                        $"Could not rename [{legacyVsoContentRootPath}] to [{vsoContentRootPath}]. Falling back to hard-linking individual files. {renameDirException}");

                    if (!FileSystem.DirectoryExists(vsoContentRootPath))
                    {
                        FileSystem.CreateDirectory(vsoContentRootPath);
                    }

                    foreach (FileInfo fileInCache in FileSystem.EnumerateFiles(legacyVsoContentRootPath, EnumerateOptions.Recurse))
                    {
                        AbsolutePath destinationPath = fileInCache.FullPath.SwapRoot(legacyVsoContentRootPath, vsoContentRootPath);
                        AbsolutePath destinationFolder = destinationPath.Parent;

                        if (!FileSystem.DirectoryExists(destinationFolder))
                        {
                            FileSystem.CreateDirectory(destinationFolder);
                        }
                        else if (FileSystem.FileExists(destinationPath))
                        {
                            continue;
                        }

                        try
                        {
                            UpgradeLegacyVsoContentRenameFile(fileInCache.FullPath, destinationPath);
                        }
                        catch (Exception fileMoveException)
                        {
                            context.Debug(
                                $"Could not rename [{fileInCache.FullPath}] to [{destinationPath}]. Falling back to hard-linking. {fileMoveException}");

                            CreateHardLinkResult result = FileSystem.CreateHardLink(fileInCache.FullPath, destinationPath, replaceExisting: false);
                            if (result != CreateHardLinkResult.Success)
                            {
                                throw new CacheException(
                                    $"Failed to create hard link from [{fileInCache.FullPath}] to [{destinationPath}]: {result}");
                            }
                        }
                    }

                    try
                    {
                        UpgradeLegacyVsoContentDeleteDirectory(legacyVsoContentRootPath);
                    }
                    catch (Exception deleteDirException)
                    {
                        context.Debug(
                            $"After moving or copying all content to [{vsoContentRootPath}], could not delete [{legacyVsoContentRootPath}]. Will try again next time. {deleteDirException}");
                    }
                }

                await MemoryContentDirectory.TransformFile(
                    context,
                    FileSystem,
                    RootPath,
                    pair =>
                    {
                        ContentHash contentHash = pair.Key;
                        ContentFileInfo fileInfo = pair.Value;

                        if (contentHash.HashType == HashType.DeprecatedVso0)
                        {
                            var hashBytes = contentHash.ToHashByteArray();
                            var newContentHash = new ContentHash(HashType.Vso0, hashBytes);
                            return new KeyValuePair<ContentHash, ContentFileInfo>(newContentHash, fileInfo);
                        }

                        return pair;
                    });
            }
        }

        /// <summary>
        ///     Migrates (or just deletes) the schema on disk
        /// </summary>
        private async Task UpgradeAsNecessaryAsync(Context context)
        {
            int currentVersionNumber;
            try
            {
                currentVersionNumber = ReadVersionNumber(context);
            }
            catch (Exception ex)
            {
                throw new CacheException(
                    ex,
                    "Failed to read the cache version file. Delete {0}, {1}, and {2} and try again. Exception message: {3}",
                    VersionFilePath,
                    _contentRootDirectory,
                    ContentDirectory.FilePath,
                    ex.ToString());
            }

            try
            {
                while (true)
                {
                    if (currentVersionNumber > (int)VersionHistory.CurrentVersion || currentVersionNumber < (int)VersionHistory.Version0)
                    {
                        throw new CacheException(
                            "CAS runtime version is {0}, but disk has version {1}. Delete {2} and {3} or use the latest version of the cache.",
                            (int)VersionHistory.CurrentVersion,
                            currentVersionNumber,
                            _contentRootDirectory,
                            ContentDirectory.FilePath);
                    }

                    var currentVersion = (VersionHistory)currentVersionNumber;

                    if (currentVersion == VersionHistory.CurrentVersion)
                    {
                        break;
                    }

                    await PerformUpgradeToNextVersionAsync(context, currentVersion);
                    Contract.Assert((currentVersionNumber + 1) == SerializedDataVersion.ReadValueFile());

                    currentVersionNumber = ReadVersionNumber(context);
                }

                // Upgrade any legacy VSO hashed content without a painful CAS upgrade.
                await UpgradeLegacyVsoContentAsync(context);

                Contract.Assert(SerializedDataVersion.ReadValueFile() == (int)VersionHistory.CurrentVersion);
            }
            catch (Exception ex)
            {
                throw new CacheException(
                    ex,
                    "Failed to upgrade local CAS. Delete {0} and {1} and try again. Exception message: {2}",
                    _contentRootDirectory,
                    ContentDirectory.FilePath,
                    ex.ToString());
            }
        }

        private int ReadVersionNumber(Context context)
        {
            int currentVersionNumber = SerializedDataVersion.ReadValueFile();
            _tracer.Debug(context, $"version is {currentVersionNumber}");
            return currentVersionNumber;
        }

        private void DeleteTempFolder()
        {
            if (FileSystem.DirectoryExists(_tempFolder))
            {
                FileSystem.DeleteDirectory(_tempFolder, DeleteOptions.All);
            }
        }

        /// <inheritdoc />
        public ContentDirectorySnapshot<ContentFileInfo> Reconstruct(Context context)
        {
            // NOTE: DO NOT call ContentDirectory from this method as this is called during the initialization of ContentDirectory and calls
            // into ContentDirectory would cause a deadlock.

            var stopwatch = Stopwatch.StartNew();
            long contentCount = 0;
            long contentSize = 0;

            try
            {
                var contentHashes = ReadSnapshotFromDisk(context);
                _tracer.Debug(context, $"Enumerated {contentHashes.Count} entries in {stopwatch.ElapsedMilliseconds}ms.");

                // We are using a list of classes instead of structs due to the maximum object size restriction
                // When the contents on disk grow large, a list of structs surpasses the limit and forces OOM
                var hashInfoPairs = new ContentDirectorySnapshot<ContentFileInfo>();
                foreach (var grouping in contentHashes.GroupByHash())
                {
                    var contentFileInfo = new ContentFileInfo(Clock, grouping.First().Payload.Length, grouping.Count());
                    contentCount++;
                    contentSize += contentFileInfo.TotalSize;

                    hashInfoPairs.Add(new PayloadFromDisk<ContentFileInfo>(grouping.Key, contentFileInfo));
                }

                return hashInfoPairs;
            }
            catch (Exception exception)
            {
                _tracer.ReconstructDirectoryException(context, exception);
                throw;
            }
            finally
            {
                stopwatch.Stop();
                _tracer.ReconstructDirectory(context, stopwatch.Elapsed, contentCount, contentSize);
            }
        }

        /// <inheritdoc />
        public IContentChangeAnnouncer Announcer
        {
            get { return _announcer; }

            set
            {
                Contract.Assert(_announcer == null);
                _announcer = value;
            }
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            var configFileExists = false;

            if (_configurationModel.Selection == ConfigurationSelection.RequireAndUseInProcessConfiguration)
            {
                if (_configurationModel.InProcessConfiguration == null)
                {
                    throw new CacheException("In-process configuration selected but it is null");
                }

                Configuration = _configurationModel.InProcessConfiguration;
            }
            else if (_configurationModel.Selection == ConfigurationSelection.UseFileAllowingInProcessFallback)
            {
                var readConfigResult = await FileSystem.ReadContentStoreConfigurationAsync(RootPath);

                if (readConfigResult.Succeeded)
                {
                    Configuration = readConfigResult.Data;
                    configFileExists = true;
                }
                else if (_configurationModel.InProcessConfiguration != null)
                {
                    Configuration = _configurationModel.InProcessConfiguration;
                }
                else
                {
                    throw new CacheException($"{nameof(ContentStoreConfiguration)} is missing");
                }
            }
            else
            {
                throw new CacheException($"Invalid {nameof(ConfigurationSelection)}={_configurationModel.Selection}");
            }

            if (!configFileExists && _configurationModel.MissingFileOption == MissingConfigurationFileOption.WriteOnlyIfNotExists)
            {
                await Configuration.Write(FileSystem, RootPath);
            }

            _tracer.Debug(context, $"{nameof(ContentStoreConfiguration)}: {Configuration}");

            _applyDenyWriteAttributesOnContent = Configuration.DenyWriteAttributesOnContent == DenyWriteAttributesOnContentSetting.Enable;

            await UpgradeAsNecessaryAsync(context);

            DeleteTempFolder();
            FileSystem.CreateDirectory(_tempFolder);

            await ContentDirectory.StartupAsync(context).ThrowIfFailure();

            var contentDirectoryCount = await ContentDirectory.GetCountAsync();
            if (contentDirectoryCount != 0 && !FileSystem.DirectoryExists(_contentRootDirectory))
            {
                return new BoolResult(
                    $"Content root directory {_contentRootDirectory} is missing despite CAS metadata indicating {contentDirectoryCount} files.");
            }

            var size = await ContentDirectory.GetSizeAsync();

            _pinSizeHistory =
                await
                    PinSizeHistory.LoadOrCreateNewAsync(
                        FileSystem,
                        Clock,
                        RootPath,
                        Configuration.HistoryBufferSize);

            _distributedEvictionSettings?.InitializeDistributedEviction(
                UpdateContentWithLastAccessTimeAsync,
                _tracer,
                GetPinnedSize,
                _nagleQueue);

            var quotaKeeperConfiguration = QuotaKeeperConfiguration.Create(
                Configuration,
                _distributedEvictionSettings,
                _settings,
                size);
            QuotaKeeper = QuotaKeeper.Create(
                FileSystem,
                _tracer,
                ShutdownStartedCancellationToken,
                this,
                quotaKeeperConfiguration);

            var result = await QuotaKeeper.StartupAsync(context);

            _taskTracker = new BackgroundTaskTracker(Component, new Context(context));

            _tracer.StartStats(context, size, contentDirectoryCount);

            if (_settings.StartSelfCheckInStartup)
            {
                // Starting the self check and ignore and trace the failure.
                // Self check procedure is a long running operation that can take longer then an average process lifetime.
                // So instead of relying on timers to recheck content directory, we rely on
                // periodic service restarts.
                SelfCheckContentDirectoryAsync(context, context.Token).FireAndForget(context);
            }

            return result;
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            var result = BoolResult.Success;

            var statsResult = await GetStatsAsync(context);

            if (QuotaKeeper != null)
            {
                _tracer.EndStats(context, QuotaKeeper.CurrentSize, await ContentDirectory.GetCountAsync());

                // NOTE: QuotaKeeper must be shut down before the content directory because it owns
                // background operations which may be calling EvictAsync or GetLruOrderedContentListAsync
                result &= await QuotaKeeper.ShutdownAsync(context);
            }

            if (_pinSizeHistory != null)
            {
                await _pinSizeHistory.SaveAsync(FileSystem);
            }

            if (_taskTracker != null)
            {
                await _taskTracker.Synchronize();
                await _taskTracker.ShutdownAsync(context);
            }

            if (ContentDirectory != null)
            {
                result &= await ContentDirectory.ShutdownAsync(context);
            }

            if (_contentDirectoryMismatchCount > 0)
            {
                _tracer.Warning(
                    context,
                    $"Corrected {_contentDirectoryMismatchCount} mismatches between cache blobs and content directory metadata.");
            }

            if (FileSystem.DirectoryExists(_tempFolder))
            {
                foreach (FileInfo fileInfo in FileSystem.EnumerateFiles(_tempFolder, EnumerateOptions.Recurse))
                {
                    try
                    {
                        ForceDeleteFile(fileInfo.FullPath);
                        _tracer.Warning(context, $"Temp file still existed at shutdown: {fileInfo.FullPath}");
                    }
                    catch (IOException ioException)
                    {
                        _tracer.Warning(context, $"Could not clean up temp file due to exception: {ioException}");
                    }
                }
            }

            if (statsResult)
            {
                statsResult.CounterSet.LogOrderedNameValuePairs(s => _tracer.Debug(context, s));
            }

            return result;
        }

        private static bool ShouldAttemptHardLink(AbsolutePath contentPath, FileAccessMode accessMode, FileRealizationMode realizationMode)
        {
            return contentPath.IsLocal && accessMode == FileAccessMode.ReadOnly &&
                   (realizationMode == FileRealizationMode.Any ||
                    realizationMode == FileRealizationMode.HardLink);
        }

        private bool TryCreateHardlink(
            Context context,
            AbsolutePath source,
            AbsolutePath destination,
            FileRealizationMode realizationMode,
            bool replaceExisting,
            out CreateHardLinkResult hardLinkResult)
        {
            var result = CreateHardLinkResult.Unknown;
            var stopwatch = Stopwatch.StartNew();

            try
            {
                result = FileSystem.CreateHardLink(source, destination, replaceExisting);
                Contract.Assert((result == CreateHardLinkResult.FailedDestinationExists).Implies(!replaceExisting));

                var resultAcceptable = false;
                switch (result)
                {
                    case CreateHardLinkResult.Success:
                    case CreateHardLinkResult.FailedDestinationExists:
                    case CreateHardLinkResult.FailedMaxHardLinkLimitReached:
                    case CreateHardLinkResult.FailedSourceDoesNotExist:
                    case CreateHardLinkResult.FailedAccessDenied:
                    case CreateHardLinkResult.FailedSourceHandleInvalid:
                        resultAcceptable = true;
                        break;

                    case CreateHardLinkResult.FailedNotSupported:
                    case CreateHardLinkResult.FailedSourceAndDestinationOnDifferentVolumes:
                        resultAcceptable = realizationMode != FileRealizationMode.HardLink;
                        break;
                }

                if (!resultAcceptable)
                {
                    throw new CacheException("Failed to create hard link from [{0}] to [{1}]: {2}", source, destination, result);
                }

                hardLinkResult = result;
                return hardLinkResult == CreateHardLinkResult.Success;
            }
            finally
            {
                stopwatch.Stop();
                _tracer.CreateHardLink(context, result, source, destination, realizationMode, replaceExisting, stopwatch.Elapsed);
            }
        }

        /// <inheritdoc />
        public Task<PutResult> PutFileAsync(
            Context context, AbsolutePath path, FileRealizationMode realizationMode, ContentHash contentHash, PinRequest? pinRequest)
        {
            return PutFileImplAsync(context, path, realizationMode, contentHash, pinRequest);
        }

        private Task<PutResult> PutFileImplAsync(
            Context context, AbsolutePath path, FileRealizationMode realizationMode, ContentHash contentHash, PinRequest? pinRequest, Func<Stream, Stream> wrapStream = null)
        {
            return PutFileCall<ContentStoreInternalTracer>.RunAsync(
                _tracer, OperationContext(context), path, realizationMode, contentHash, trustedHash: false, async () =>
            {
                PinContext pinContext = pinRequest?.PinContext;
                bool shouldAttemptHardLink = ShouldAttemptHardLink(path, FileAccessMode.ReadOnly, realizationMode);

                using (LockSet<ContentHash>.LockHandle contentHashHandle = await _lockSet.AcquireAsync(contentHash))
                {
                    CheckPinned(contentHash, pinRequest);
                    long contentSize = await GetContentSizeInternalAsync(context, contentHash, pinContext);
                    if (contentSize >= 0)
                    {
                        // The user provided a hash for content that we already have. Try to satisfy the request without hashing the given file.
                        bool putInternalSucceeded;
                        if (shouldAttemptHardLink)
                        {
                            putInternalSucceeded = await PutContentInternalAsync(
                                context,
                                contentHash,
                                contentSize,
                                pinContext,
                                onContentAlreadyInCache: async (hashHandle, primaryPath, info) =>
                                {
                                    var r = await PlaceLinkFromCacheAsync(
                                        context,
                                        path,
                                        FileReplacementMode.ReplaceExisting,
                                        realizationMode,
                                        contentHash,
                                        info);
                                    return r == CreateHardLinkResult.Success;
                                },
                                onContentNotInCache: primaryPath => Task.FromResult(false),
                                announceAddOnSuccess: false);
                        }
                        else
                        {
                            putInternalSucceeded = await PutContentInternalAsync(
                                context,
                                contentHash,
                                contentSize,
                                pinContext,
                                onContentAlreadyInCache: (hashHandle, primaryPath, info) => Task.FromResult(true),
                                onContentNotInCache: primaryPath => Task.FromResult(false));
                        }

                        if (putInternalSucceeded)
                        {
                            return new PutResult(contentHash, contentSize)
                                .WithLockAcquisitionDuration(contentHashHandle);
                        }
                    }
                }

                var result = await PutFileAsync(
                    context, path, contentHash.HashType, realizationMode, wrapStream, pinRequest);

                if (realizationMode != FileRealizationMode.CopyNoVerify && result.ContentHash != contentHash && result.Succeeded)
                {
                    return new PutResult(result.ContentHash, $"Content at {path} had actual content hash {result.ContentHash} and did not match expected value of {contentHash}");
                }

                return result;
            });
        }

        /// <inheritdoc />
        public Task<PutResult> PutFileAsync(
            Context context, AbsolutePath path, FileRealizationMode realizationMode, HashType hashType, PinRequest? pinRequest)
        {
            return PutFileImplAsync(context, path, realizationMode, hashType, pinRequest, trustedHashWithSize: null);
        }

        private Task<PutResult> PutFileImplAsync(
            Context context, AbsolutePath path, FileRealizationMode realizationMode, HashType hashType, PinRequest? pinRequest, ContentHashWithSize? trustedHashWithSize, Func<Stream, Stream> wrapStream = null)
        {
            Contract.Requires(trustedHashWithSize == null || trustedHashWithSize.Value.Size >= 0);

            return PutFileCall<ContentStoreInternalTracer>.RunAsync(_tracer, OperationContext(context), path, realizationMode, hashType, trustedHash: trustedHashWithSize != null, async () =>
            {
                ContentHashWithSize content = trustedHashWithSize ?? default;
                if (trustedHashWithSize == null)
                {
                    // We only hash the file if a trusted hash is not supplied
                    var possibleContent = await TryHashFileAsync(context, path, hashType, wrapStream);
                    if (possibleContent == null)
                    {
                        return new PutResult(default(ContentHash), $"Source file not found at '{path}'.");
                    }

                    content = possibleContent.Value;
                }

                // If we are given the empty file, the put is a no-op.
                // We have dedicated logic for pinning and returning without having
                // the empty file in the cache directory.
                if (_settings.UseEmptyFileHashShortcut && content.Hash.IsEmptyHash())
                {
                    return new PutResult(content.Hash, 0L);
                }

                using (LockSet<ContentHash>.LockHandle contentHashHandle = await _lockSet.AcquireAsync(content.Hash))
                {
                    CheckPinned(content.Hash, pinRequest);
                    PinContext pinContext = pinRequest?.PinContext;
                    var stopwatch = new Stopwatch();

                    if (ShouldAttemptHardLink(path, FileAccessMode.ReadOnly, realizationMode))
                    {
                        bool putInternalSucceeded = await PutContentInternalAsync(
                            context,
                            content.Hash,
                            content.Size,
                            pinContext,
                            onContentAlreadyInCache: async (hashHandle, primaryPath, info) =>
                            {
                                // The content exists in the cache. Try to replace the file that is being put in
                                // with a link to the file that is already in the cache. Release the handle to
                                // allow for the hardlink to succeed.
                                try
                                {
                                    _tracer.PutFileExistingHardLinkStart();
                                    stopwatch.Start();

                                    // ReSharper disable once AccessToDisposedClosure
                                    var result = await PlaceLinkFromCacheAsync(
                                        context,
                                        path,
                                        FileReplacementMode.ReplaceExisting,
                                        realizationMode,
                                        content.Hash,
                                        info);
                                    return result == CreateHardLinkResult.Success;
                                }
                                finally
                                {
                                    stopwatch.Stop();
                                    _tracer.PutFileExistingHardLinkStop(stopwatch.Elapsed);
                                }
                            },
                            onContentNotInCache: primaryPath =>
                            {
                                try
                                {
                                    _tracer.PutFileNewHardLinkStart();
                                    stopwatch.Start();

                                    ApplyPermissions(context, path, FileAccessMode.ReadOnly);

                                    var hardLinkResult = CreateHardLinkResult.Unknown;
                                    Func<bool> tryCreateHardlinkFunc = () => TryCreateHardlink(
                                        context, path, primaryPath, realizationMode, false, out hardLinkResult);

                                    bool result = tryCreateHardlinkFunc();
                                    if (hardLinkResult == CreateHardLinkResult.FailedDestinationExists)
                                    {
                                        // Extraneous blobs on disk. Delete them and retry.
                                        RemoveAllReplicasFromDiskFor(context, content.Hash);
                                        result = tryCreateHardlinkFunc();
                                    }

                                    return Task.FromResult(result);
                                }
                                finally
                                {
                                    stopwatch.Stop();
                                    _tracer.PutFileNewHardLinkStop(stopwatch.Elapsed);
                                }
                            },
                            announceAddOnSuccess: false);

                        if (putInternalSucceeded)
                        {
                            return new PutResult(content.Hash, content.Size)
                                .WithLockAcquisitionDuration(contentHashHandle);
                        }
                    }

                    // If hard linking failed or wasn't attempted, fall back to copy.
                    stopwatch = new Stopwatch();
                    await PutContentInternalAsync(
                        context,
                        content.Hash,
                        content.Size,
                        pinContext,
                        onContentAlreadyInCache: (hashHandle, primaryPath, info) => Task.FromResult(true),
                        onContentNotInCache: async primaryPath =>
                        {
                            try
                            {
                                _tracer.PutFileNewCopyStart();
                                stopwatch.Start();

                                await RetryOnUnexpectedReplicaAsync(
                                    context,
                                    () =>
                                    {
                                        if (realizationMode == FileRealizationMode.Move)
                                        {
                                            return Task.Run(() => FileSystem.MoveFile(path, primaryPath, replaceExisting: false));
                                        }
                                        else
                                        {
                                            return SafeCopyFileAsync(context, content.Hash, path, primaryPath, FileReplacementMode.FailIfExists);
                                        }
                                    },
                                    content.Hash,
                                    expectedReplicaCount: 0);
                                return true;
                            }
                            finally
                            {
                                stopwatch.Stop();
                                _tracer.PutFileNewCopyStop(stopwatch.Elapsed);
                            }
                        });

                    return new PutResult(content.Hash, content.Size)
                        .WithLockAcquisitionDuration(contentHashHandle);
                }
            });
        }

        /// <inheritdoc />
        public Task<PutResult> PutTrustedFileAsync(Context context, AbsolutePath path, FileRealizationMode realizationMode, ContentHashWithSize contentHashWithSize, PinRequest? pinContext = null)
        {
            return PutFileImplAsync(context, path, realizationMode, contentHashWithSize.Hash.HashType, pinContext, trustedHashWithSize: contentHashWithSize);
        }

        /// <inheritdoc />
        public Task<GetStatsResult> GetStatsAsync(Context context)
        {
            return GetStatsCall<ContentStoreInternalTracer>.RunAsync(_tracer, OperationContext(context), async () =>
            {
                var counters = new CounterSet();
                counters.Merge(_tracer.GetCounters(), $"{Component}.");

                if (StartupCompleted)
                {
                    counters.Add($"{CurrentByteCountName}", QuotaKeeper.CurrentSize);
                    counters.Add($"{CurrentFileCountName}", await ContentDirectory.GetCountAsync());
                    counters.Merge(ContentDirectory.GetCounters(), "ContentDirectory.");

                    var quotaKeeperCounter = QuotaKeeper.Counters;
                    if (quotaKeeperCounter != null)
                    {
                        counters.Merge(quotaKeeperCounter.ToCounterSet());
                    }
                }

                foreach (var kvp in _hashers)
                {
                    counters.Merge(kvp.Value.GetCounters(), $"{Component}.{kvp.Key}");
                }

                return new GetStatsResult(counters);
            });
        }

        /// <inheritdoc />
        public async Task<bool> Validate(Context context)
        {
            bool foundIssue = false;

            foundIssue |= !await ValidateNameHashesMatchContentHashesAsync(context);
            foundIssue |= !ValidateAcls(context);
            foundIssue |= !await ValidateContentDirectoryAsync(context);

            return !foundIssue;
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<ContentInfo>> EnumerateContentInfoAsync()
        {
            return ContentDirectory.EnumerateContentInfoAsync();
        }

        /// <inheritdoc />
        public Task<IEnumerable<ContentHash>> EnumerateContentHashesAsync()
        {
            return ContentDirectory.EnumerateContentHashesAsync();
        }

        /// <summary>
        ///     Complete all pending/background operations.
        /// </summary>
        public async Task SyncAsync(Context context, bool purge = true)
        {
            await QuotaKeeper.SyncAsync(context, purge);

            // Ensure there are no pending LRU updates.
            await ContentDirectory.SyncAsync();
        }

        /// <inheritdoc />
        public PinSizeHistory.ReadHistoryResult ReadPinSizeHistory(int windowSize)
        {
            return _pinSizeHistory.ReadHistory(windowSize);
        }

        private async Task<bool> ValidateNameHashesMatchContentHashesAsync(Context context)
        {
            int mismatchedParentDirectoryCount = 0;
            int mismatchedContentHashCount = 0;
            _tracer.Always(context, "Validating local CAS content hashes...");
            await TaskSafetyHelpers.WhenAll(EnumerateBlobPathsFromDisk().Select(
                async blobPath =>
                {
                    var contentFile = blobPath.FullPath;
                    if (!contentFile.FileName.StartsWith(contentFile.Parent.FileName, StringComparison.OrdinalIgnoreCase))
                    {
                        mismatchedParentDirectoryCount++;

                        _tracer.Debug(
                            context,
                            $"The first {HashDirectoryNameLength} characters of the name of content file at {contentFile}" +
                            $" do not match the name of its parent directory {contentFile.Parent.FileName}.");
                    }

                    if (!TryGetHashFromPath(contentFile, out var hashFromPath))
                    {
                        _tracer.Debug(
                            context,
                            $"The path '{contentFile}' does not contain a well-known hash name.");
                        return;
                    }

                    var hasher = _hashers[hashFromPath.HashType];
                    ContentHash hashFromContents;
                    using (Stream contentStream = await FileSystem.OpenSafeAsync(
                        contentFile, FileAccess.Read, FileMode.Open, FileShare.Read | FileShare.Delete, FileOptions.SequentialScan, HashingExtensions.HashStreamBufferSize))
                    {
                        hashFromContents = await hasher.GetContentHashAsync(contentStream);
                    }

                    if (hashFromContents != hashFromPath)
                    {
                        mismatchedContentHashCount++;

                        _tracer.Debug(
                            context,
                            $"Content at {contentFile} content hash {hashFromContents} did not match expected value of {hashFromPath}.");
                    }
                }));

            _tracer.Always(context, $"{mismatchedParentDirectoryCount} mismatches between content file name and parent directory.");
            _tracer.Always(context, $"{mismatchedContentHashCount} mismatches between content file name and file contents.");

            return mismatchedContentHashCount == 0 && mismatchedParentDirectoryCount == 0;
        }

        private bool ValidateAcls(Context context)
        {
            // Getting ACLs currently requires using File.GetAccessControl.  We should extend IAbsFileSystem to enable this query.
            if (!(FileSystem is PassThroughFileSystem))
            {
                _tracer.Always(context, "Skipping validation of ACLs because the CAS is not using a PassThroughFileSystem.");
                return true;
            }

            _tracer.Always(context, "Validating local CAS content file ACLs...");
         
            int missingDenyAclCount = 0;

            foreach (var blobPath in EnumerateBlobPathsFromDisk())
            {
                var contentFile = blobPath.FullPath;

                // FileSystem has no GetAccessControl API, so we must bypass it here.  We can relax the restriction to PassThroughFileSystem once we implement GetAccessControl in IAbsFileSystem.
                bool denyAclExists = true;
#if !FEATURE_CORECLR
                const string worldSidValue = "Everyone";
                var security = File.GetAccessControl(contentFile.Path);
                var fileSystemAccessRules =
                    security.GetAccessRules(true, false, typeof(NTAccount)).Cast<FileSystemAccessRule>();
                denyAclExists = fileSystemAccessRules.Any(rule =>
                                    rule.IdentityReference.Value.Equals(worldSidValue, StringComparison.OrdinalIgnoreCase) &&
                                    rule.AccessControlType == AccessControlType.Deny &&
                                    rule.FileSystemRights == (_applyDenyWriteAttributesOnContent
                                        ? (FileSystemRights.WriteData | FileSystemRights.AppendData)
                                        : FileSystemRights.Write) && // Should this be exact (as it is now), or at least, deny ACLs?
                                    rule.InheritanceFlags == InheritanceFlags.None &&
                                    rule.IsInherited == false &&
                                    rule.PropagationFlags == PropagationFlags.None
                                    );               
#endif

                if (!denyAclExists)
                {
                    missingDenyAclCount++;
                    _tracer.Always(context, $"Content at {contentFile} is missing proper deny ACLs.");
                }
            }

            _tracer.Always(context, $"{missingDenyAclCount} projects are missing proper deny ACLs.");

            return missingDenyAclCount == 0;
        }

        private async Task<bool> ValidateContentDirectoryAsync(Context context)
        {
            _tracer.Always(context, "Validating local CAS content directory");
            int contentDirectoryMismatchCount = 0;

            var fileSystemContentDirectory = EnumerateBlobPathsFromDisk()
                .Select(blobPath => TryGetHashFromPath(blobPath.FullPath, out var hash) ? (ContentHash?)hash : null)
                .Where(hash => hash != null)
                .GroupBy(hash => hash.Value)
                .ToDictionary(replicaGroup => replicaGroup.Key, replicaGroup => replicaGroup.Count());

            foreach (var x in fileSystemContentDirectory.Keys)
            {
                var fileSystemHash = x;
                int fileSystemHashReplicaCount = fileSystemContentDirectory[fileSystemHash];

                await ContentDirectory.UpdateAsync(fileSystemHash, false, Clock, fileInfo =>
                {
                    if (fileInfo == null)
                    {
                        contentDirectoryMismatchCount++;
                        _tracer.Always(context, $"Cache content directory for hash {fileSystemHash} from disk does not exist.");
                    }
                    else if (fileInfo.ReplicaCount != fileSystemHashReplicaCount)
                    {
                        contentDirectoryMismatchCount++;
                        _tracer.Always(
                            context,
                            $"Directory for hash {fileSystemHash} describes {fileInfo.ReplicaCount} replicas, but {fileSystemHashReplicaCount} replicas exist on disk.");
                    }

                    return null;
                });
            }

            foreach (var x in (await ContentDirectory.EnumerateContentHashesAsync())
                .Where(hash => !fileSystemContentDirectory.ContainsKey(hash)))
            {
                var missingHash = x;
                contentDirectoryMismatchCount++;
                await ContentDirectory.UpdateAsync(missingHash, false, Clock, fileInfo =>
                {
                    if (fileInfo != null)
                    {
                        _tracer.Always(
                            context,
                            $"Directory for hash {missingHash} describes {fileInfo.ReplicaCount} replicas, but no replicas exist on disk.");
                    }

                    return null;
                });
            }

            _tracer.Always(
                context, $"{contentDirectoryMismatchCount} mismatches between cache content directory and content files on disk.");

            return contentDirectoryMismatchCount == 0;
        }

        /// <summary>
        ///     Protected implementation of Dispose pattern.
        /// </summary>
        protected override void DisposeCore()
        {
            base.DisposeCore();

            foreach (IContentHasher hasher in _hashers.Values)
            {
                hasher.Dispose();
            }

            QuotaKeeper?.Dispose();
            _taskTracker?.Dispose();
            ContentDirectory.Dispose();
        }

        /// <summary>
        ///     Called by PutContentInternalAsync when the content already exists in the cache.
        /// </summary>
        /// <returns>True if the callback is successful.</returns>
        private delegate Task<bool> OnContentAlreadyExistsInCacheAsync(
            ContentHash contentHash, AbsolutePath primaryPath, ContentFileInfo info);

        /// <summary>
        ///     Called by PutContentInternalAsync when the content already exists in the cache.
        /// </summary>
        /// <returns>True if the callback is successful.</returns>
        private delegate Task<bool> OnContentNotInCacheAsync(AbsolutePath primaryPath);

        private async Task<bool> PutContentInternalAsync(
            Context context,
            ContentHash contentHash,
            long contentSize,
            PinContext pinContext,
            OnContentAlreadyExistsInCacheAsync onContentAlreadyInCache,
            OnContentNotInCacheAsync onContentNotInCache,
            bool announceAddOnSuccess = true)
        {
            AbsolutePath primaryPath = GetPrimaryPathFor(contentHash);
            bool failed = false;
            long addedContentSize = 0;

            _tracer.PutContentInternalStart();
            var stopwatch = Stopwatch.StartNew();

            await ContentDirectory.UpdateAsync(contentHash, touch: true, Clock, async fileInfo =>
            {
                if (fileInfo == null || await RemoveEntryIfNotOnDiskAsync(context, contentHash))
                {
                    using (var txn = await QuotaKeeper.ReserveAsync(contentSize))
                    {
                        FileSystem.CreateDirectory(primaryPath.Parent);

                        if (!await onContentNotInCache(primaryPath))
                        {
                            failed = true;
                            return null;
                        }

                        txn.Commit();
                        PinContentIfContext(contentHash, pinContext);
                        addedContentSize = contentSize;
                        return new ContentFileInfo(Clock, contentSize);
                    }
                }

                if (!await onContentAlreadyInCache(contentHash, primaryPath, fileInfo))
                {
                    failed = true;
                    return null;
                }

                PinContentIfContext(contentHash, pinContext);

                addedContentSize = fileInfo.FileSize;
                return fileInfo;
            });

            _tracer.PutContentInternalStop(stopwatch.Elapsed);

            if (failed)
            {
                return false;
            }

            if (addedContentSize > 0)
            {
                _tracer.AddPutBytes(addedContentSize);
            }

            if (_announcer != null && addedContentSize > 0 && announceAddOnSuccess)
            {
                await _announcer.ContentAdded(new ContentHashWithSize(contentHash, addedContentSize));
            }

            return true;
        }

        /// <inheritdoc />
        public Task<PutResult> PutStreamAsync(Context context, Stream stream, ContentHash contentHash, PinRequest? pinRequest)
        {
            return PutStreamCall<ContentStoreInternalTracer>.RunAsync(_tracer, OperationContext(context), contentHash, async () =>
            {
                PinContext pinContext = pinRequest?.PinContext;

                using (LockSet<ContentHash>.LockHandle contentHashHandle = await _lockSet.AcquireAsync(contentHash))
                {
                    CheckPinned(contentHash, pinRequest);
                    long contentSize = await GetContentSizeInternalAsync(context, contentHash, pinContext);
                    if (contentSize >= 0)
                    {
                        // The user provided a hash for content that we already have. Try to satisfy the request without hashing the given stream.
                        bool putInternalSucceeded = await PutContentInternalAsync(
                            context,
                            contentHash,
                            contentSize,
                            pinContext,
                            onContentAlreadyInCache: (hashHandle, primaryPath, info) => Task.FromResult(true),
                            onContentNotInCache: primaryPath => Task.FromResult(false));

                        if (putInternalSucceeded)
                        {
                            return new PutResult(contentHash, contentSize)
                                .WithLockAcquisitionDuration(contentHashHandle);
                        }
                    }
                }

                var r = await PutStreamImplAsync(context, stream, contentHash.HashType, pinRequest);

                return r.ContentHash != contentHash && r.Succeeded
                    ? new PutResult(r, contentHash, $"Calculated hash={r.ContentHash} does not match caller's hash={contentHash}")
                    : r;
            });
        }

        /// <inheritdoc />
        public Task<PutResult> PutStreamAsync(Context context, Stream stream, HashType hashType, PinRequest? pinRequest)
        {
            return PutStreamCall<ContentStoreInternalTracer>.RunAsync(
                _tracer, OperationContext(context), hashType, () => PutStreamImplAsync(context, stream, hashType, pinRequest));
        }

        private async Task<PutResult> PutStreamImplAsync(Context context, Stream stream, HashType hashType, PinRequest? pinRequest)
        {
            PinContext pinContext = pinRequest?.PinContext;
            ContentHash contentHash = new ContentHash(hashType);
            AbsolutePath pathToTempContent = null;

            bool shouldDelete = false;
            try
            {
                long contentSize;

                var hasher = _hashers[hashType];
                using (var hashingStream = hasher.CreateReadHashingStream(stream))
                {
                    pathToTempContent = await WriteToTemporaryFileAsync(context, hashingStream);
                    contentSize = FileSystem.GetFileSize(pathToTempContent);
                    contentHash = hashingStream.GetContentHash();

                    // This our temp file and it is responsibility of this method to delete it.
                    shouldDelete = true;
                }

                using (LockSet<ContentHash>.LockHandle contentHashHandle = await _lockSet.AcquireAsync(contentHash))
                {
                    CheckPinned(contentHash, pinRequest);

                    if (!await PutContentInternalAsync(
                        context,
                        contentHash,
                        contentSize,
                        pinContext,
                        onContentAlreadyInCache: (hashHandle, primaryPath, info) => Task.FromResult(true),
                        onContentNotInCache: async primaryPath =>
                        {
                            // ReSharper disable once AccessToModifiedClosure
                            await RetryOnUnexpectedReplicaAsync(
                                context,
                                () =>
                                {
                                    FileSystem.MoveFile(pathToTempContent, primaryPath, replaceExisting: false);
                                    return Task.FromResult(true);
                                },
                                contentHash,
                                expectedReplicaCount: 0);

                            pathToTempContent = null;
                            return true;
                        }))
                    {
                        return new PutResult(contentHash, $"{nameof(PutStreamAsync)} failed to put {pathToTempContent} with hash {contentHash} with an unknown error");
                    }

                    return new PutResult(contentHash, contentSize)
                        .WithLockAcquisitionDuration(contentHashHandle);
                }
            }
            finally
            {
                if (shouldDelete)
                {
                    DeleteTempFile(context, contentHash, pathToTempContent);
                }
            }
        }

        /// <summary>
        ///     Deletes a file that is marked read-only
        /// </summary>
        /// <param name="path">Path to the file</param>
        protected virtual void DeleteReadOnlyFile(AbsolutePath path)
        {
            Contract.Requires(path != null);

            FileSystem.DeleteFile(path);
        }

        private void ForceDeleteFile(AbsolutePath path)
        {
            if (path == null)
            {
                return;
            }

            DeleteReadOnlyFile(path);
        }

        private void TryForceDeleteFile(Context context, AbsolutePath path)
        {
            try
            {
                ForceDeleteFile(path);
            }
            catch (Exception exception) when (exception is IOException || exception is BuildXLException || exception is UnauthorizedAccessException)
            {
                _tracer.Debug(context, $"Unable to force delete {path.Path} exception=[{exception}]");
            }
        }

        private void DeleteTempFile(Context context, ContentHash contentHash, AbsolutePath path)
        {
            if (path == null)
            {
                return;
            }

            if (!path.Parent.Equals(_tempFolder))
            {
                _tracer.Error(context, $"Will not delete temp file in unexpected location, path=[{path}]");
                return;
            }

            try
            {
                ForceDeleteFile(path);
                _tracer.Debug(context, $"Deleted temp content at '{path.Path}' for {contentHash}");
            }
            catch (Exception exception) when (exception is IOException || exception is BuildXLException || exception is UnauthorizedAccessException)
            {
                _tracer.Warning(
                    context,
                    $"Unable to delete temp content at '{path.Path}' for {contentHash} due to exception: {exception}");
            }
        }

        private AbsolutePath GetTemporaryFileName()
        {
            return _tempFolder / GetRandomFileName();
        }

        private AbsolutePath GetTemporaryFileName(ContentHash contentHash)
        {
            return _tempFolder / (GetRandomFileName() + contentHash.ToHex());
        }

        private static string GetRandomFileName()
        {
            // Don't use Path.GetRandomFileName(), it's not random enough when running multi-threaded.
            return Guid.NewGuid().ToString("N").Substring(0, 12);
        }

        /// <summary>
        ///     Writes the content stream to local disk in a temp directory under the store's root.
        ///     Marks the file as Read Only and sets ACL to deny file writes.
        /// </summary>
        /// <param name="context">Tracing context.</param>
        /// <param name="inputStream">Content stream to write</param>
        /// <returns>Absolute path that points to the file</returns>
        private async Task<AbsolutePath> WriteToTemporaryFileAsync(Context context, Stream inputStream)
        {
            AbsolutePath pathToTempContent = GetTemporaryFileName();
            AbsolutePath pathToTempContentDirectory = pathToTempContent.Parent;
            FileSystem.CreateDirectory(pathToTempContentDirectory);

            // We want to set an ACL which denies writes before closing the destination stream. This way, there
            // are no instants in which we have neither an exclusive lock on writing the file nor a protective
            // ACL. Note that we can still be fooled in the event of external tampering via renames/move, but this
            // approach makes it very unlikely that our own code would ever write to or truncate the file before we move it.

            using (Stream tempFileStream = await FileSystem.OpenSafeAsync(pathToTempContent, FileAccess.Write, FileMode.CreateNew, FileShare.Delete))
            {
                await inputStream.CopyToWithFullBufferAsync(tempFileStream, FileSystemConstants.FileIOBufferSize);
                ApplyPermissions(context, pathToTempContent, FileAccessMode.ReadOnly);
            }

            return pathToTempContent;
        }

        private async Task<ContentHashWithSize> HashContentAsync(Context context, Stream stream, HashType hashType, AbsolutePath path)
        {
            Contract.Requires(stream != null);

            var stopwatch = new Stopwatch();

            try
            {
                _tracer.HashContentFileStart(context, path);
                stopwatch.Start();

                ContentHash contentHash = await _hashers[hashType].GetContentHashAsync(stream);
                return new ContentHashWithSize(contentHash, stream.Length);
            }
            catch (Exception e)
            {
                _tracer.Error(context, e, "Error while hashing content.");
                throw;
            }
            finally
            {
                stopwatch.Stop();
                _tracer.HashContentFileStop(context, path, stopwatch.Elapsed);
            }
        }

        private void ApplyPermissions(Context context, AbsolutePath path, FileAccessMode accessMode)
        {
            var stopwatch = new Stopwatch();

            try
            {
                _tracer.ApplyPermsStart();
                stopwatch.Start();

                if (accessMode == FileAccessMode.ReadOnly)
                {
                    FileSystem.DenyFileWrites(path);

                    if (_applyDenyWriteAttributesOnContent)
                    {
                        if (_applyDenyWriteAttributesOnContent && !IsNormalEnough(path))
                        {
                            // Only normalize attributes if DenyWriteAttributesOnContent is set
                            Normalize(path);
                        }

                        FileSystem.DenyAttributeWrites(path);

                        if (!IsNormalEnough(path))
                        {
                            throw new CacheException("The attributes of file {0} were modified during ingress. Found flags: {1}", path, File.GetAttributes(path.Path).ToString());
                        }
                    }
                }
                else if (_applyDenyWriteAttributesOnContent && !IsNormalEnough(path))
                {
                    // Only normalize attributes if DenyWriteAttributesOnContent is set
                    Normalize(path);
                }

                // When DenyWriteAttributesOnContent is set to false, we shouldn't give an error
                // even if clearing potential Deny-WriteAttributes fails.  This is especially true
                // because in most cases where we're unable to clear those ACLs, we were probably
                // unable to set them in the first place.
                if (!_applyDenyWriteAttributesOnContent)
                {
                    try
                    {
                        FileSystem.AllowAttributeWrites(path);
                    }
                    catch (IOException ex)
                    {
                        context.Warning(ex.ToString());
                    }
                }
            }
            finally
            {
                stopwatch.Stop();
                _tracer.ApplyPermsStop(stopwatch.Elapsed);
            }
        }

        private void Normalize(AbsolutePath path)
        {
            try
            {
                FileSystem.SetFileAttributes(path, FileAttributes.Normal);
            }
            catch (IOException)
            {
                FileSystem.AllowAttributeWrites(path);
                FileSystem.SetFileAttributes(path, FileAttributes.Normal);
            }
            catch (UnauthorizedAccessException)
            {
                FileSystem.AllowAttributeWrites(path);
                FileSystem.SetFileAttributes(path, FileAttributes.Normal);
            }
        }

        // Since setting ACLs seems to flip on the Archive bit,
        // we have to be content with allowing the archive bit to be set for cache blobs
        // We're whitelisting even more here because other values (that we don't care about)
        // sometimes survive being set to "Normal," and we don't want to throw in those cases.
        private bool IsNormalEnough(AbsolutePath path)
        {
            const FileAttributes ignoredFileAttributes =
                FileAttributes.Normal | FileAttributes.Archive | FileAttributes.Compressed |
                FileAttributes.SparseFile | FileAttributes.Encrypted | FileAttributes.Offline |
                FileAttributes.IntegrityStream | FileAttributes.NoScrubData | FileAttributes.System |
                FileAttributes.Temporary | FileAttributes.Device | FileAttributes.Directory |
                FileAttributes.NotContentIndexed | FileAttributes.ReparsePoint | FileAttributes.Hidden;
            return FileSystem.FileAttributesAreSubset(path, ignoredFileAttributes);
        }

        private enum ForEachReplicaCallbackResult
        {
            StopIterating,
            TryNextReplicaIfExists,
            TryNextReplica
        }

        private enum ReplicaExistence
        {
            Exists,
            DoesNotExist
        }

        private delegate Task<ForEachReplicaCallbackResult> ForEachReplicaCallback(
            AbsolutePath primaryPath, int replicaIndex, AbsolutePath replicaPath, bool replicaExists);

        // Perform the callback for each replica, starting from the primary replica (index 0)
        private async Task ForEachReplicaAsync(
            LockSet<ContentHash>.LockHandle contentHashHandle, ContentFileInfo info, ForEachReplicaCallback pathCallback)
        {
            AbsolutePath primaryPath = GetPrimaryPathFor(contentHashHandle.Key);
            ForEachReplicaCallbackResult result = await pathCallback(primaryPath, 0, primaryPath, true);
            if (result == ForEachReplicaCallbackResult.StopIterating)
            {
                return;
            }

            for (int replicaIndex = 1;
                replicaIndex < info.ReplicaCount || result == ForEachReplicaCallbackResult.TryNextReplica;
                replicaIndex++)
            {
                var replicaPath = GetReplicaPathFor(contentHashHandle.Key, replicaIndex);

                result = await pathCallback(primaryPath, replicaIndex, replicaPath, replicaIndex < info.ReplicaCount);
                if (result == ForEachReplicaCallbackResult.StopIterating)
                {
                    return;
                }
            }
        }

        /// <summary>
        ///     Gets the path that points to the location of a particular replica of this content hash.
        /// </summary>
        /// <param name="contentHash">Content hash to get path for</param>
        /// <param name="replicaIndex">The index of the replica. 0 is the primary.</param>
        /// <returns>Path for the hash</returns>
        /// <remarks>Does not guarantee anything is at the returned path</remarks>
        protected AbsolutePath GetReplicaPathFor(ContentHash contentHash, int replicaIndex)
        {
            Contract.Requires(replicaIndex >= 0);

            // MOve hashtype into inner call
            return _contentRootDirectory / contentHash.HashType.Serialize() / GetRelativePathFor(contentHash, replicaIndex);
        }

        /// <summary>
        ///     Gets the path that points to the location of this content hash.
        /// </summary>
        /// <param name="contentHash">Content hash to get path for</param>
        /// <returns>Path for the hash</returns>
        /// <remarks>Does not guarantee anything is at the returned path</remarks>
        protected internal AbsolutePath GetPrimaryPathFor(ContentHash contentHash)
        {
            return GetReplicaPathFor(contentHash, 0);
        }

        private static RelativePath GetRelativePathFor(ContentHash contentHash, int replicaIndex)
        {
            string hash = contentHash.ToHex();

            // Create a subdirectory to not stress directory-wide locks used by the file system
            var hashSubDirectory = GetHashSubDirectory(contentHash);

            if (replicaIndex == 0)
            {
                return hashSubDirectory / string.Format(CultureInfo.InvariantCulture, "{0}.{1}", hash, BlobNameExtension);
            }

            return hashSubDirectory /
                   string.Format(CultureInfo.InvariantCulture, "{0}.{1}.{2}", hash, replicaIndex, BlobNameExtension);
        }

        private static RelativePath GetHashSubDirectory(ContentHash contentHash)
        {
            return new RelativePath(contentHash.ToHex().Substring(0, HashDirectoryNameLength));
        }

        internal bool TryGetFileInfo(ContentHash contentHash, out ContentFileInfo fileInfo) => ContentDirectory.TryGetFileInfo(contentHash, out fileInfo);

        internal ContentDirectorySnapshot<FileInfo> ReadSnapshotFromDisk(Context context)
        {
            // We are using a list of classes instead of structs due to the maximum object size restriction
            // When the contents on disk grow large, a list of structs surpasses the limit and forces OOM
            var contentHashes = new ContentDirectorySnapshot<FileInfo>();
            if (_settings.UseNativeBlobEnumeration)
            {
                EnumerateBlobPathsFromDisk(context, fileInfo => parseAndAccumulateContentHashes(fileInfo));
            }
            else
            {
                foreach (var fileInfo in EnumerateBlobPathsFromDisk())
                {
                    parseAndAccumulateContentHashes(fileInfo);
                }
            }

            return contentHashes;

            void parseAndAccumulateContentHashes(FileInfo fileInfo)
            {
                // A directory could have an old hash in its name or may be renamed by the user.
                // This is not an error condition if we can't get the hash out of it.
                if (TryGetHashFromPath(fileInfo.FullPath, out var contentHash))
                {
                    contentHashes.Add(new PayloadFromDisk<FileInfo>(contentHash, fileInfo));
                }
                else
                {
                    _tracer.Debug(context, $"Can't process directory '{fileInfo.FullPath}' because the path does not contain a well-known hash name.");
                }
            }
        }

        private IEnumerable<FileInfo> EnumerateBlobPathsFromDisk()
        {
            if (!FileSystem.DirectoryExists(_contentRootDirectory))
            {
                return new FileInfo[] {};
            }

            return FileSystem
                .EnumerateFiles(_contentRootDirectory, EnumerateOptions.Recurse)
                .Where(
                    fileInfo => fileInfo.FullPath.Path.EndsWith(BlobNameExtension, StringComparison.OrdinalIgnoreCase));
        }

        private void EnumerateBlobPathsFromDisk(Context context, Action<FileInfo> fileHandler)
        {
            try
            {
                FileSystem.EnumerateFiles(_contentRootDirectory, $"*.{BlobNameExtension}", recursive: true, fileHandler);
            }
            catch (IOException e)
            {
                _tracer.Info(context, $"Error enumerating blobs: {e}");
            }
        }

        private IEnumerable<FileInfo> EnumerateBlobPathsFromDiskFor(ContentHash contentHash)
        {
            var hashSubPath = _contentRootDirectory / contentHash.HashType.ToString() / GetHashSubDirectory(contentHash);
            if (!FileSystem.DirectoryExists(hashSubPath))
            {
                return new FileInfo[] {};
            }

            return FileSystem
                .EnumerateFiles(hashSubPath, EnumerateOptions.None)
                .Where(fileInfo =>
                {
                    var filePath = fileInfo.FullPath;
                    return TryGetHashFromPath(filePath, out var hash) &&
                           hash.Equals(contentHash) &&
                           filePath.FileName.EndsWith(BlobNameExtension, StringComparison.OrdinalIgnoreCase);
                });
        }

        internal static bool TryGetHashFromPath(AbsolutePath path, out ContentHash contentHash)
        {
            var hashName = path.Parent.Parent.FileName;
            if (Enum.TryParse<HashType>(hashName, ignoreCase: true, out var hashType))
            {
                string hashHexString = GetFileNameWithoutExtension(path);
                try
                {
                    contentHash = new ContentHash(hashType, HexUtilities.HexToBytes(hashHexString));
                }
                catch (ArgumentException)
                {
                    // If the file name format is malformed, throw an exception with more actionable error message.
                    throw new CacheException($"Failed to obtain the hash from file name '{path}'. File name should be in hexadecimal form.");
                }

                return true;
            }

            contentHash = default;
            return false;
        }

        /// <nodoc />
        public static string GetFileNameWithoutExtension(AbsolutePath path)
        {
            // Unlike <see cref = "Path.GetFileNameWithoutExtension" /> this method returns the name before the first '.', not the name until the last '.'.
            // I.e. for a file name <code>"foo.bar.baz"</code> this method returns "foo", but <see cref="Path.GetFileNameWithoutExtension"/> returns "foo.bar".

            Contract.Requires(path != null);
            string fileName = path.GetFileName();
            if (fileName.IndexOf('.') is var i && i == -1)
            {
                // No path extension found.
                return fileName;
            }
            return fileName.Substring(0, i);
        }

        private int GetReplicaIndexFromPath(AbsolutePath path)
        {
            if (TryGetHashFromPath(path, out var contentHash))
            {
                string fileName = path.GetFileName();

                // ReSharper disable once PossibleNullReferenceException
                if (fileName.StartsWith(contentHash.ToHex(), StringComparison.OrdinalIgnoreCase) &&
                    fileName.EndsWith(BlobNameExtension, StringComparison.OrdinalIgnoreCase))
                {
                    var fileNameParts = fileName.Split('.');
                    if (fileNameParts.Length == 2)
                    {
                        return 0;
                    }

                    if (fileNameParts.Length == 3)
                    {
                        if (int.TryParse(fileNameParts[1], out var index))
                        {
                            return index;
                        }
                    }
                }
            }

            return -1;
        }

        /// <summary>
        ///     Snapshots the cached content in LRU order (i.e. the order, according to last-access time, in which they should be
        ///     purged to make space).
        /// </summary>
        /// <returns>LRU-ordered hashes.</returns>
        public virtual Task<IReadOnlyList<ContentHash>> GetLruOrderedContentListAsync()
        {
            return ContentDirectory.GetLruOrderedCacheContentAsync();
        }

        /// <summary>
        ///     Snapshots the cached content in LRU order (i.e. the order, according to last-access time, in which they should be
        ///     purged to make space). Coupled with its last-access time.
        /// </summary>
        public virtual Task<IReadOnlyList<ContentHashWithLastAccessTimeAndReplicaCount>> GetLruOrderedContentListWithTimeAsync()
        {
            return ContentDirectory.GetLruOrderedCacheContentWithTimeAsync();
        }

        /// <summary>
        ///       Update content with provided last access time.
        /// </summary>
        public async Task UpdateContentWithLastAccessTimeAsync(ContentHash contentHash, DateTime lru)
        {
            using (await _lockSet.AcquireAsync(contentHash))
            {
                ContentDirectory.UpdateContentWithLastAccessTime(contentHash, lru);
            }
        }

        private bool TryGetContentTotalSize(ContentHash contentHash, out long size)
        {
            if (ContentDirectory.TryGetFileInfo(contentHash, out var fileInfo))
            {
                size = fileInfo.TotalSize;
                return true;
            }

            size = 0;
            return false;
        }

        /// <summary>
        ///     Remove specified content.
        /// </summary>
        public Task<EvictResult> EvictAsync(Context context, ContentHashWithLastAccessTimeAndReplicaCount contentHashInfo, bool onlyUnlinked, Action<long> evicted)
        {
            // This operation respects pinned content and won't evict it if it's pinned.
            return EvictCoreAsync(context, contentHashInfo, force: false, onlyUnlinked, evicted);
        }

        private async Task<EvictResult> EvictCoreAsync(Context context, ContentHashWithLastAccessTimeAndReplicaCount contentHashInfo, bool force, bool onlyUnlinked, Action<long> evicted)
        {
            ContentHash contentHash = contentHashInfo.ContentHash;

            long pinnedSize = 0;
            using (LockSet<ContentHash>.LockHandle? contentHashHandle = _lockSet.TryAcquire(contentHash))
            {
                if (contentHashHandle == null)
                {
                    _tracer.Debug(context, $"Skipping check of pinned size for {contentHash} because another thread has a lock on it.");
                    return new EvictResult(evictedSize: 0, evictedFiles: 0, pinnedSize: 0, contentHashInfo.LastAccessTime, successfullyEvictedHash: false, contentHashInfo.ReplicaCount);
                }

                // Only checked PinMap if force is false, otherwise even pinned content should be evicted.
                if (!force && PinMap.TryGetValue(contentHash, out var pin) && pin.Count > 0)
                {
                    // The content is pinned. Eviction is not possible in this case.
                    if (TryGetContentTotalSize(contentHash, out var size))
                    {
                        pinnedSize = size;
                    }

                    return new EvictResult(evictedSize: 0, evictedFiles: 0, pinnedSize: pinnedSize, contentHashInfo.LastAccessTime, successfullyEvictedHash: false, contentHashInfo.ReplicaCount);
                }

                // Intentionally tracking only (potentially) successful eviction.
                return await EvictCall.RunAsync(
                    _tracer,
                    OperationContext(context),
                    contentHash,
                    async () =>
                    {
                        long evictedSize = 0;
                        long evictedFiles = 0;
                        bool successfullyEvictedHash = false;

                        await ContentDirectory.UpdateAsync(
                            contentHash,
                            touch: false,
                            Clock,
                            async fileInfo =>
                            {
                                if (fileInfo == null)
                                {
                                    // The content is not found in content directory.
                                    return null;
                                }

                                if (!force && PinMap.TryGetValue(contentHash, out pin) && pin.Count > 0)
                                {
                                    pinnedSize = fileInfo.TotalSize;

                                    // Nothing was modified, so no need to save anything.
                                    return null;
                                }

                                // Used by tests to inject an arbitrary delay
                                _preEvictFileAction?.Invoke();

                                await ContentDirectory.RemoveAsync(contentHash);

                                var remainingReplicas = new List<AbsolutePath>(0);
                                var evictions = new List<ContentHashWithSize>();

                                // ReSharper disable once AccessToDisposedClosure
                                await ForEachReplicaAsync(
                                    contentHashHandle.Value,
                                    fileInfo,
                                    (primaryPath, replicaIndex, replicaPath, replicaExists) =>
                                    {
                                        bool exists = FileSystem.FileExists(replicaPath);
                                        bool evict = !exists || !onlyUnlinked || FileSystem.GetHardLinkCount(replicaPath) <= 1;
                                        if (evict)
                                        {
                                            try
                                            {
                                                if (exists)
                                                {
                                                    SafeForceDeleteFile(context, replicaPath);
                                                }

                                                evicted?.Invoke(fileInfo.FileSize);
                                                _tracer.Diagnostic(
                                                    context,
                                                    $"Evicted content hash=[{contentHash}] replica=[{replicaIndex}] size=[{fileInfo.FileSize}]");
                                                evictedFiles++;
                                                evictedSize += fileInfo.FileSize;
                                                evictions.Add(new ContentHashWithSize(contentHash, fileInfo.FileSize));

                                                _tracer.TrackMetric(context, "ContentHashEvictedBytes", fileInfo.FileSize);
                                            }
                                            catch (Exception exception)
                                            {
                                                _tracer.Warning(
                                                    context,
                                                    $"Unable to purge {replicaPath.Path} because of exception: {exception}");
                                                remainingReplicas.Add(replicaPath);
                                            }
                                        }
                                        else
                                        {
                                            remainingReplicas.Add(replicaPath);
                                        }

                                        return Task.FromResult(ForEachReplicaCallbackResult.TryNextReplicaIfExists);
                                    });

                                if (_announcer != null)
                                {
                                    foreach (var e in evictions)
                                    {
                                        await _announcer.ContentEvicted(e);
                                    }
                                }

                                if (remainingReplicas.Count > 0)
                                {
                                    for (int i = 0; i < remainingReplicas.Count; i++)
                                    {
                                        AbsolutePath destinationPath = GetReplicaPathFor(contentHash, i);
                                        if (remainingReplicas[i] != destinationPath)
                                        {
                                            _tracer.Debug(
                                                context,
                                                $"Renaming [{remainingReplicas[i]}] to [{destinationPath}] as part of cleanup.");
                                            FileSystem.MoveFile(remainingReplicas[i], destinationPath, false);
                                        }
                                    }

                                    fileInfo.ReplicaCount = remainingReplicas.Count;
                                    return fileInfo;
                                }
                                else
                                {
                                    // Notify Redis of eviction when Distributed Eviction is turned off
                                    if (_distributedEvictionSettings == null)
                                    {
                                        _nagleQueue?.Enqueue(contentHash);
                                    }

                                    successfullyEvictedHash = true;
                                }

                                PinMap.TryRemove(contentHash, out pin);

                                return null;
                            });

                    return new EvictResult(evictedSize, evictedFiles, pinnedSize, contentHashInfo.LastAccessTime, successfullyEvictedHash, contentHashInfo.ReplicaCount);
                });
            }
        }

        /// <inheritdoc />
        public Task<PlaceFileResult> PlaceFileAsync(
            Context context,
            ContentHash contentHash,
            AbsolutePath destinationPath,
            FileAccessMode accessMode,
            FileReplacementMode replacementMode,
            FileRealizationMode realizationMode,
            PinRequest? pinRequest)
        {
            return PlaceFileCall<ContentStoreInternalTracer>.RunAsync(
                _tracer,
                OperationContext(context),
                contentHash,
                destinationPath,
                accessMode,
                replacementMode,
                realizationMode,
                async () => await PlaceFileInternalAsync(
                    context,
                    new ContentHashWithPath(contentHash, destinationPath),
                    accessMode,
                    replacementMode,
                    realizationMode,
                    pinRequest));
        }

        /// <inheritdoc />
        public async Task<IEnumerable<Task<Indexed<PlaceFileResult>>>> PlaceFileAsync(
            Context context,
            IReadOnlyList<ContentHashWithPath> placeFileArgs,
            FileAccessMode accessMode,
            FileReplacementMode replacementMode,
            FileRealizationMode realizationMode,
            PinRequest? pinRequest = null)
        {
            var placeFileInternalBlock = new TransformBlock<Indexed<ContentHashWithPath>, Indexed<PlaceFileResult>>(
                async p =>
                    (await PlaceFileAsync(context, p.Item.Hash, p.Item.Path, accessMode, replacementMode, realizationMode, pinRequest)).WithIndex(p.Index),
                new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = ParallelPlaceFilesLimit, });

            // TODO: Better way ? (bug 1365340)
            placeFileInternalBlock.PostAll(placeFileArgs.AsIndexed());
            var results = await Task.WhenAll(Enumerable.Range(0, placeFileArgs.Count).Select(i => placeFileInternalBlock.ReceiveAsync()));
            placeFileInternalBlock.Complete();

            return results.AsTasks();
        }

        private async Task<PlaceFileResult> PlaceFileInternalAsync(
            Context context,
            ContentHashWithPath contentHashWithPath,
            FileAccessMode accessMode,
            FileReplacementMode replacementMode,
            FileRealizationMode realizationMode,
            PinRequest? pinRequest)
        {
            try
            {
                var contentHash = contentHashWithPath.Hash;
                var destinationPath = contentHashWithPath.Path;

                // Check for file existing in the non-racing SkipIfExists case.
                if ((replacementMode == FileReplacementMode.SkipIfExists || replacementMode == FileReplacementMode.FailIfExists) && FileSystem.FileExists(destinationPath))
                {
                    return new PlaceFileResult(PlaceFileResult.ResultCode.NotPlacedAlreadyExists);
                }

                // If this is the empty hash, then directly create an empty file.
                // This avoids hash-level lock, all I/O in the cache directory, and even
                // operations in the in-memory representation of the cache.
                if (_settings.UseEmptyFileHashShortcut && contentHashWithPath.Hash.IsEmptyHash())
                {
                    await FileSystem.CreateEmptyFileAsync(contentHashWithPath.Path);
                    return new PlaceFileResult(PlaceFileResult.ResultCode.PlacedWithCopy);
                }

                // Lookup hash in content directory
                using (LockSet<ContentHash>.LockHandle contentHashHandle = await _lockSet.AcquireAsync(contentHash))
                {
                    if (pinRequest.HasValue)
                    {
                        PinContentIfContext(contentHash, pinRequest.Value.PinContext);
                    }

                    var code = PlaceFileResult.ResultCode.Unknown;
                    long contentSize = 0;
                    DateTime lastAccessTime = DateTime.MinValue;

                    await ContentDirectory.UpdateAsync(contentHash, true, Clock, async fileInfo =>
                    {
                        if (fileInfo == null)
                        {
                            code = PlaceFileResult.ResultCode.NotPlacedContentNotFound;
                            return null;
                        }

                        contentSize = fileInfo.FileSize;
                        lastAccessTime = DateTime.FromFileTimeUtc(fileInfo.LastAccessedFileTimeUtc);

                        if (ShouldAttemptHardLink(destinationPath, accessMode, realizationMode))
                        {
                            // ReSharper disable once AccessToDisposedClosure
                            CreateHardLinkResult hardLinkResult = await PlaceLinkFromCacheAsync(
                                    context,
                                    destinationPath,
                                    replacementMode,
                                    realizationMode,
                                    contentHash,
                                    fileInfo);
                            if (hardLinkResult == CreateHardLinkResult.Success)
                            {
                                code = PlaceFileResult.ResultCode.PlacedWithHardLink;
                            }
                            else if (hardLinkResult == CreateHardLinkResult.FailedDestinationExists)
                            {
                                code = PlaceFileResult.ResultCode.NotPlacedAlreadyExists;
                            }
                            else if (hardLinkResult == CreateHardLinkResult.FailedSourceDoesNotExist)
                            {
                                await RemoveEntryIfNotOnDiskAsync(context, contentHash);
                                code = PlaceFileResult.ResultCode.NotPlacedContentNotFound;
                                return null;
                            }
                        }

                        return fileInfo;
                    });

                    if (code != PlaceFileResult.ResultCode.Unknown)
                    {
                        return new PlaceFileResult(code, contentSize)
                            .WithLockAcquisitionDuration(contentHashHandle);
                    }

                    // If hard linking failed or wasn't attempted, fall back to copy.
                    var stopwatch = Stopwatch.StartNew();
                    try
                    {
                        PlaceFileResult result;
                        if (realizationMode == FileRealizationMode.CopyNoVerify)
                        {
                            result = await CopyFileWithNoValidationAsync(
                                context, contentHash, destinationPath, accessMode, replacementMode);
                        }
                        else
                        {
                            result = await CopyFileAndValidateStreamAsync(
                                context, contentHash, destinationPath, accessMode, replacementMode);
                        }

                        result.FileSize = contentSize;
                        result.LastAccessTime = lastAccessTime;
                        return result
                            .WithLockAcquisitionDuration(contentHashHandle);
                    }
                    finally
                    {
                        stopwatch.Stop();
                        _tracer.PlaceFileCopy(context, destinationPath, contentHash, stopwatch.Elapsed);
                    }
                }
            }
            catch (Exception e)
            {
                return new PlaceFileResult(e);
            }
        }

        private async Task<PlaceFileResult> CopyFileWithNoValidationAsync(
            Context context,
            ContentHash contentHash,
            AbsolutePath destinationPath,
            FileAccessMode accessMode,
            FileReplacementMode replacementMode)
        {
            var code = PlaceFileResult.ResultCode.PlacedWithCopy;
            AbsolutePath contentPath = await PinContentAndGetFullPathAsync(contentHash, null);
            try
            {
                if (contentPath == null)
                {
                    code = PlaceFileResult.ResultCode.NotPlacedContentNotFound;
                }
                else
                {
                    try
                    {
                        var replaceExisting = replacementMode == FileReplacementMode.ReplaceExisting;
                        await FileSystem.CopyFileAsync(contentPath, destinationPath, replaceExisting);
                    }
                    catch (IOException e)
                    {
                        if ((uint)e.HResult == Hresult.FileExists || (e.InnerException != null &&
                                                                      (uint)e.InnerException.HResult == Hresult.FileExists))
                        {
                            // File existing in the racing SkipIfExists case.
                            code = PlaceFileResult.ResultCode.NotPlacedAlreadyExists;
                        }
                        else
                        {
                            return new PlaceFileResult(e, $"Failed to place hash=[{contentHash}] to path=[{destinationPath}]");
                        }
                    }

                    ApplyPermissions(context, destinationPath, accessMode);
                }
            }
            finally
            {
                if (PinMap.TryGetValue(contentHash, out var pin))
                {
                    pin.Decrement();
                }
            }

            return new PlaceFileResult(code);
        }

        private async Task<PlaceFileResult> CopyFileAndValidateStreamAsync(
            Context context,
            ContentHash contentHash,
            AbsolutePath destinationPath,
            FileAccessMode accessMode,
            FileReplacementMode replacementMode)
        {
            var code = PlaceFileResult.ResultCode.Unknown;
            var hasher = _hashers[contentHash.HashType];
            ContentHash computedHash = new ContentHash(contentHash.HashType);

            using (Stream contentStream =
                await OpenStreamInternalWithLockAsync(context, contentHash, null, FileShare.Read | FileShare.Delete))
            {
                if (contentStream == null)
                {
                    code = PlaceFileResult.ResultCode.NotPlacedContentNotFound;
                }
                else
                {
                    using (var hashingStream = hasher.CreateReadHashingStream(contentStream))
                    {
                        try
                        {
                            FileSystem.CreateDirectory(destinationPath.Parent);
                            var fileMode = replacementMode == FileReplacementMode.ReplaceExisting ? FileMode.Create : FileMode.CreateNew;

                            using (Stream targetFileStream = await FileSystem.OpenSafeAsync(destinationPath, FileAccess.Write, fileMode, FileShare.Delete))
                            {
                                await hashingStream.CopyToWithFullBufferAsync(
                                    targetFileStream, FileSystemConstants.FileIOBufferSize);

                                ApplyPermissions(context, destinationPath, accessMode);
                            }

                            computedHash = hashingStream.GetContentHash();
                        }
                        catch (IOException e)
                        {
                            if (e.InnerException != null && (long)e.InnerException.HResult == Hresult.FileExists)
                            {
                                // File existing in the racing SkipIfExists case.
                                code = PlaceFileResult.ResultCode.NotPlacedAlreadyExists;
                            }
                            else
                            {
                                return new PlaceFileResult(e, $"Failed to place hash=[{contentHash}] to path=[{destinationPath}]");
                            }
                        }
                    }
                }
            }

            if (code == PlaceFileResult.ResultCode.Unknown)
            {
                if (computedHash != contentHash)
                {
                    await RemoveCorruptedThenThrowAsync(context, contentHash, computedHash, destinationPath);
                }

                code = PlaceFileResult.ResultCode.PlacedWithCopy;
            }

            return new PlaceFileResult(code);
        }

        private async Task<CreateHardLinkResult> PlaceLinkFromCacheAsync(
            Context context,
            AbsolutePath destinationPath,
            FileReplacementMode replacementMode,
            FileRealizationMode realizationMode,
            ContentHash contentHash,
            ContentFileInfo info)
        {
            FileSystem.CreateDirectory(destinationPath.Parent);

            int defaultStartIndex = info.ReplicaCount - 1;

            // If a cursor has been saved for this hash, try that one first
            if (_replicaCursors.TryGetValue(contentHash, out var startIndex))
            {
                if (startIndex >= info.ReplicaCount || startIndex < 0)
                {
                    // Remove an out-of-range cursor
                    _replicaCursors.TryRemove(contentHash, out startIndex);
                    startIndex = defaultStartIndex;
                }
            }
            else
            {
                // If not, try the most recently created replica first
                startIndex = defaultStartIndex;
            }

            CreateHardLinkResult result = await PlaceLinkFromReplicaAsync(
                context,
                destinationPath,
                replacementMode,
                realizationMode,
                contentHash,
                info,
                startIndex,
                ReplicaExistence.Exists);

            if (result != CreateHardLinkResult.FailedMaxHardLinkLimitReached)
            {
                return result;
            }

            // This replica is full
            _replicaCursors.TryRemove(contentHash, out _);

            if (info.ReplicaCount > 1)
            {
                // Try a random existing replica before making a new one.
                var randomIndex = ThreadSafeRandom.Generator.Next(info.ReplicaCount - 1);
                result = await PlaceLinkFromReplicaAsync(
                    context,
                    destinationPath,
                    replacementMode,
                    realizationMode,
                    contentHash,
                    info,
                    randomIndex,
                    ReplicaExistence.Exists);
                if (result != CreateHardLinkResult.FailedMaxHardLinkLimitReached)
                {
                    // Save the cursor here as the most recent replica tried. No contention on the value due to the lock.
                    _replicaCursors.AddOrUpdate(contentHash, randomIndex, (hash, i) => randomIndex);
                    _tracer.Debug(
                        context,
                        $"Moving replica cursor to index {randomIndex} because callback stopped on replica {GetReplicaPathFor(contentHash, randomIndex).Path}.");
                    return result;
                }
            }

            var newReplicaIndex = info.ReplicaCount;
            return await PlaceLinkFromReplicaAsync(
                context,
                destinationPath,
                replacementMode,
                realizationMode,
                contentHash,
                info,
                newReplicaIndex,
                ReplicaExistence.DoesNotExist);
        }

        private async Task<CreateHardLinkResult> PlaceLinkFromReplicaAsync(
            Context context,
            AbsolutePath destinationPath,
            FileReplacementMode replacementMode,
            FileRealizationMode realizationMode,
            ContentHash contentHash,
            ContentFileInfo info,
            int replicaIndex,
            ReplicaExistence replicaExistence)
        {
            var primaryPath = GetPrimaryPathFor(contentHash);
            var replicaPath = GetReplicaPathFor(contentHash, replicaIndex);
            if (replicaExistence == ReplicaExistence.DoesNotExist)
            {
                // Create a new replica
                using (var txn = await QuotaKeeper.ReserveAsync(info.FileSize))
                {
                    await RetryOnUnexpectedReplicaAsync(
                        context,
                        () => SafeCopyFileAsync(
                            context,
                            contentHash,
                            primaryPath,
                            replicaPath,
                            FileReplacementMode.FailIfExists),
                        contentHash,
                        info.ReplicaCount);
                    txn.Commit();
                }

                if (_announcer != null)
                {
                    await _announcer.ContentAdded(new ContentHashWithSize(contentHash, info.FileSize));
                }

                info.ReplicaCount++;
            }

            if (!TryCreateHardlink(
                context,
                replicaPath,
                destinationPath,
                realizationMode,
                replacementMode == FileReplacementMode.ReplaceExisting,
                out CreateHardLinkResult hardLinkResult))
            {
                if (hardLinkResult == CreateHardLinkResult.FailedSourceDoesNotExist &&
                    primaryPath != replicaPath &&
                    FileSystem.FileExists(primaryPath))
                {
                    _tracer.Warning(
                        context,
                        $"Missing replica for hash=[{contentHash}]. Recreating replica=[{replicaPath}] from primary replica.");
                    Interlocked.Increment(ref _contentDirectoryMismatchCount);
                    await SafeCopyFileAsync(
                            context,
                            contentHash,
                            primaryPath,
                            replicaPath,
                            FileReplacementMode.FailIfExists);
                    TryCreateHardlink(
                        context,
                        replicaPath,
                        destinationPath,
                        realizationMode,
                        replacementMode == FileReplacementMode.ReplaceExisting,
                        out hardLinkResult);
                }
            }

            return hardLinkResult;
        }

        private async Task SafeCopyFileAsync(
            Context context,
            ContentHash contentHash,
            AbsolutePath sourcePath,
            AbsolutePath destinationPath,
            FileReplacementMode replacementMode)
        {
            AbsolutePath tempPath = GetTemporaryFileName(contentHash);
            await FileSystem.CopyFileAsync(sourcePath, tempPath, false);
            ApplyPermissions(context, tempPath, FileAccessMode.ReadOnly);
            FileSystem.MoveFile(tempPath, destinationPath, replacementMode == FileReplacementMode.ReplaceExisting);
        }

        private async Task RetryOnUnexpectedReplicaAsync(
            Context context, Func<Task> tryFunc, ContentHash contentHash, int expectedReplicaCount)
        {
            try
            {
                await tryFunc();
            }
            catch (IOException)
            {
                RemoveExtraReplicasFromDiskFor(context, contentHash, expectedReplicaCount);
                await tryFunc();
            }
        }

        /// <summary>
        ///     Removes the corresponding entry from the content directory if the content for a hash doesn't exist on disk.
        /// </summary>
        /// <param name="context">Tracing context</param>
        /// <param name="contentHash">The hash whose content and content directory entry are in question.</param>
        /// <returns>Whether a bad entry was removed.</returns>
        private async Task<bool> RemoveEntryIfNotOnDiskAsync(Context context, ContentHash contentHash)
        {
            var primaryPath = GetPrimaryPathFor(contentHash);
            if (!FileSystem.FileExists(primaryPath) && (await ContentDirectory.RemoveAsync(contentHash) != null))
            {
                _tracer.Warning(
                    context,
                    $"Removed content directory entry for hash {contentHash} because the cache does not have content at {primaryPath}.");
                Interlocked.Increment(ref _contentDirectoryMismatchCount);
                return true;
            }

            return false;
        }

        private void RemoveAllReplicasFromDiskFor(Context context, ContentHash contentHash)
        {
            RemoveExtraReplicasFromDiskFor(context, contentHash, 0);
        }

        /// <summary>
        ///     Removes all replicas for the given hash beyond the expected number from disk.
        /// </summary>
        /// <param name="context">Tracing context</param>
        /// <param name="contentHash">The hash whose replicas are to be limited.</param>
        /// <param name="expectedReplicaCount">The number of replicas to which the hash's replicas are to be limited.</param>
        private void RemoveExtraReplicasFromDiskFor(Context context, ContentHash contentHash, int expectedReplicaCount)
        {
            AbsolutePath[] extraReplicaPaths =
                EnumerateBlobPathsFromDiskFor(contentHash)
                    .Select(blobPath => blobPath.FullPath)
                    .Where(replicaPath => GetReplicaIndexFromPath(replicaPath) >= expectedReplicaCount).ToArray();

            if (extraReplicaPaths.Any())
            {
                _tracer.Warning(context, $"Unexpected cache blob for hash=[{contentHash}]. Removing extra blob(s).");
                Interlocked.Increment(ref _contentDirectoryMismatchCount);
            }

            foreach (AbsolutePath extraReplicaPath in extraReplicaPaths)
            {
                _tracer.Debug(context, $"Deleting extra blob {extraReplicaPath.Path}.");
                SafeForceDeleteFile(context, extraReplicaPath);
            }
        }

        private async Task RemoveCorruptedThenThrowAsync(Context context, ContentHash contentHash, ContentHash computedHash, AbsolutePath destinationPath)
        {
            AbsolutePath[] replicaPaths = EnumerateBlobPathsFromDiskFor(contentHash).Select(blobPath => blobPath.FullPath).ToArray();

            foreach (AbsolutePath replicaPath in replicaPaths)
            {
                _tracer.Debug(context, $"Deleting corrupted blob {replicaPath.Path}.");
                SafeForceDeleteFile(context, replicaPath);
            }

            _tracer.Debug(context, $"Removing content directory entry for corrupted hash {contentHash}.");
            await ContentDirectory.RemoveAsync(contentHash);

            throw new ContentHashMismatchException(destinationPath, computedHash, contentHash);
        }

        /// <summary>
        ///     Delete the file or, if unable, move it to a temp location and force delete it.
        /// </summary>
        /// <remarks>
        ///     Use this to safely delete blobs from their canonical locations without leaving unprotected files around.
        /// </remarks>
        private void SafeForceDeleteFile(Context context, AbsolutePath path)
        {
            try
            {
                DeleteReadOnlyFile(path);
            }
            catch (Exception exception) when (exception is IOException || exception is BuildXLException || exception is UnauthorizedAccessException)
            {
                AbsolutePath tempPath = GetTemporaryFileName();
                _tracer.Debug(
                    context,
                    $"Unable to delete {path.Path} exception=[{exception}]. Moving to temp path=[{tempPath}] instead and attempting to delete more thoroughly.");
                FileSystem.MoveFile(path, tempPath, replaceExisting: false);
                TryForceDeleteFile(context, tempPath);
            }
        }

        private async Task PinContextDisposeAsync(IEnumerable<KeyValuePair<ContentHash, int>> pinCounts)
        {
            long pinnedSize = 0;

            foreach (var pinCount in pinCounts)
            {
                using (await _lockSet.AcquireAsync(pinCount.Key))
                {
                    if (PinMap.TryGetValue(pinCount.Key, out Pin pin))
                    {
                        pin.Add(-1 * pinCount.Value);
                        if (pin.Count == 0)
                        {
                            PinMap.TryRemoveSpecific(pinCount.Key, pin);
                        }
                    }

                    if (TryGetContentTotalSize(pinCount.Key, out var size))
                    {
                        pinnedSize += size;
                    }
                }
            }

            QuotaKeeper.Calibrate();

            lock (_pinSizeHistory)
            {
                _maxPinSize = Math.Max(pinnedSize, _maxPinSize);

                if (Interlocked.Decrement(ref _pinContextCount) == 0)
                {
                    _pinSizeHistory.Add(_maxPinSize);
                    _maxPinSize = -1;
                }
            }
        }

        /// <summary>
        ///     Increment the info's pin count and add the hash to the given context.
        /// </summary>
        /// <param name="hash">Hash to pin.</param>
        /// <param name="pinContext">Context to pin the hash to.</param>
        private void PinContentIfContext(ContentHash hash, PinContext pinContext)
        {
            if (pinContext != null)
            {
                IncrementPin(hash);

                pinContext.AddPin(hash);
            }
        }

        private void IncrementPin(ContentHash hash)
        {
            PinMap.GetOrAdd(hash, new Pin()).Increment();
        }

        /// <summary>
        ///     Provides a PinContext for this cache which can be used in conjunction with other APIs to pin relevant content in
        ///     the cache.
        ///     The content may be unpinned by disposing of the PinContext.
        /// </summary>
        /// <returns>The created PinContext.</returns>
        public PinContext CreatePinContext()
        {
            Interlocked.Increment(ref _pinContextCount);

            // ReSharper disable once RedundantArgumentName
            return new PinContext(_taskTracker, unpinAsync: pairs => PinContextDisposeAsync(pairs));
        }

        /// <summary>
        ///     Pin existing content.
        /// </summary>
        public Task<PinResult> PinAsync(Context context, ContentHash contentHash, PinContext pinContext)
        {
            return PinCall<ContentStoreInternalTracer>.RunAsync(_tracer, OperationContext(context), contentHash, async () =>
            {
                var bulkResults = await PinAsync(context, new[] { contentHash }, pinContext);
                return bulkResults.Single().Item;
            });
        }

        /// <inheritdoc />
        public async Task<IEnumerable<Indexed<PinResult>>> PinAsync(Context context, IReadOnlyList<ContentHash> contentHashes, PinContext pinContext)
        {
            var stopwatch = Stopwatch.StartNew();

            var (results, error) = await PinCoreAsync(context, contentHashes, pinContext);
            _tracer.PinBulkStop(context, stopwatch.Elapsed, contentHashes: contentHashes, results: results, error);

            return results;
        }

        private async Task<(IEnumerable<Indexed<PinResult>> results, Exception error)> PinCoreAsync(Context context, IReadOnlyList<ContentHash> contentHashes, PinContext pinContext)
        {
            var results = new List<PinResult>(contentHashes.Count);
            try
            {
                _tracer.PinBulkStart(context, contentHashes);
                
                var pinRequest = new PinRequest(pinContext);

                // TODO: This is still relatively inefficient. We're taking a lock per hash and pinning each individually. (bug 1365340)
                // The batching needs to go further down.
                foreach (var contentHash in contentHashes)
                {
                    // Pinning the empty file always succeeds; no I/O or other operations required,
                    // because we have dedicated logic to place it when required.
                    if (_settings.UseEmptyFileHashShortcut && contentHash.IsEmptyHash())
                    {
                        results.Add(new PinResult(contentSize: 0, lastAccessTime: Clock.UtcNow, code: PinResult.ResultCode.Success));
                    }
                    else
                    {
                        ContentFileInfo contentInfo = await GetContentSizeAndLastAccessTimeAsync(context, contentHash, pinRequest);
                        results.Add(contentInfo != null ? new PinResult(contentInfo.FileSize, DateTime.FromFileTimeUtc(contentInfo.LastAccessedFileTimeUtc)) : PinResult.ContentNotFound);
                    }
                }

                return (results: results.AsIndexed(), error: null);
            }
            catch (Exception exception)
            {
                return (results: contentHashes.Select(x => PinResult.ContentNotFound).AsIndexed(), error: exception);
            }
        }

        /// <inheritdoc />
        public async Task<bool> ContainsAsync(Context context, ContentHash contentHash, PinRequest? pinRequest)
        {
            PinContext pinContext = pinRequest?.PinContext;

            using (await _lockSet.AcquireAsync(contentHash))
            {
                bool found = false;
                await ContentDirectory.UpdateAsync(contentHash, touch: true, clock: Clock, updateFileInfo: async fileInfo =>
                {
                    // If _checkFiles is true, make an additional check whether the file is actually on the disk.
                    // Otherwise, we will just trust our in-memory record.
                    if (fileInfo != null && !(_settings.CheckFiles && await RemoveEntryIfNotOnDiskAsync(context, contentHash)))
                    {
                        found = true;
                        PinContentIfContext(contentHash, pinContext);
                    }

                    return null;
                });

                return found;
            }
        }

        private void CheckPinned(ContentHash contentHash, PinRequest? pinRequest)
        {
            if (pinRequest?.VerifyAlreadyPinned == true)
            {
                IsPinned(contentHash, pinRequest);
            }
        }

        /// <summary>
        /// Checks if whether content is locally pinned.
        /// </summary>
        public bool IsPinned(ContentHash contentHash, PinRequest? pinRequest = null)
        {
            var verifyAlreadyPinned = false;
            PinContext verifyPinContext = null;

            if (pinRequest.HasValue)
            {
                verifyAlreadyPinned = pinRequest.Value.VerifyAlreadyPinned;
                verifyPinContext = pinRequest.Value.VerifyPinContext;
            }

            bool pinned = PinMap.TryGetValue(contentHash, out var pin) && pin.Count > 0;
            if (verifyAlreadyPinned)
            {
                if (!pinned)
                {
                    throw new CacheException("Expected content with hash {0} to be pinned, but it was not.", contentHash);
                }

                if (verifyPinContext != null && !verifyPinContext.Contains(contentHash))
                {
                    throw new CacheException(
                        "Expected content with hash {0} was pinned, but not to the expected pin context.", contentHash);
                }
            }

            return pinned;
        }

        /// <inheritdoc />
        public async Task<GetContentSizeResult> GetContentSizeAndCheckPinnedAsync(Context context, ContentHash contentHash, PinRequest? pinRequest)
        {
            using (await _lockSet.AcquireAsync(contentHash))
            {
                var contentWasPinned = IsPinned(contentHash, pinRequest);
                PinContext pinContext = pinRequest?.PinContext;
                long contentSize = await GetContentSizeInternalAsync(context, contentHash, pinContext);
                return new GetContentSizeResult(contentSize, contentWasPinned);
            }
        }

        private async Task<ContentFileInfo> GetContentSizeAndLastAccessTimeAsync(Context context, ContentHash contentHash, PinRequest? pinRequest)
        {
            using (await _lockSet.AcquireAsync(contentHash))
            {
                PinContext pinContext = pinRequest?.PinContext;
                return await GetContentSizeAndLastAccessTimeInternalAsync(context, contentHash, pinContext);
            }
        }

        /// <summary>
        /// Gets total pinned size. Returns -1 if unpinned.
        /// </summary>
        private long GetPinnedSize(Context context, ContentHash contentHash)
        {
            long pinnedSize = -1;
            if (IsPinned(contentHash))
            {
                TryGetContentTotalSize(contentHash, out pinnedSize);
            }

            return pinnedSize;
        }

        private async Task<long> GetContentSizeInternalAsync(Context context, ContentHash contentHash, PinContext pinContext = null)
        {
            var info = await GetContentSizeAndLastAccessTimeInternalAsync(context, contentHash, pinContext);
            return info?.FileSize ?? -1;
        }

        private async Task<ContentFileInfo> GetContentSizeAndLastAccessTimeInternalAsync(Context context, ContentHash contentHash, PinContext pinContext = null)
        {
            ContentFileInfo info = null;

            await ContentDirectory.UpdateAsync(contentHash, touch: true, clock: Clock, updateFileInfo: async contentFileInfo =>
            {
                if (contentFileInfo != null && !await RemoveEntryIfNotOnDiskAsync(context, contentHash))
                {
                    info = contentFileInfo;
                    PinContentIfContext(contentHash, pinContext);
                }

                return null;
            });

            return info;
        }

        /// <inheritdoc />
        public Task<OpenStreamResult> OpenStreamAsync(Context context, ContentHash contentHash, PinRequest? pinRequest)
        {
            return OpenStreamCall<ContentStoreInternalTracer>.RunAsync(_tracer, OperationContext(context), contentHash, async () =>
            {

                // Short-circut requests for the empty stream
                // No lock is required since no file is involved.
                if (_settings.UseEmptyFileHashShortcut && contentHash.IsEmptyHash())
                {
                    return new OpenStreamResult(_emptyFileStream);
                }

                using (var lockHandle = await _lockSet.AcquireAsync(contentHash))
                {
                    var stream = await OpenStreamInternalWithLockAsync(context, contentHash, pinRequest, FileShare.Read | FileShare.Delete);
                    return new OpenStreamResult(stream)
                        .WithLockAcquisitionDuration(lockHandle);
                }
            });
        }

        private async Task<Stream> OpenStreamInternalWithLockAsync(Context context, ContentHash contentHash, PinRequest? pinRequest, FileShare share)
        {
            AbsolutePath contentPath = await PinContentAndGetFullPathAsync(contentHash, pinRequest);

            if (contentPath == null)
            {
                return null;
            }

            var contentStream = await FileSystem.OpenAsync(contentPath, FileAccess.Read, FileMode.Open, share);

            if (contentStream == null)
            {
                await RemoveEntryIfNotOnDiskAsync(context, contentHash);
                return null;
            }

            return contentStream;
        }

        /// <summary>
        ///     OpenStream helper method.
        /// </summary>
        private async Task<AbsolutePath> PinContentAndGetFullPathAsync(ContentHash contentHash, PinRequest? pinRequest)
        {
            CheckPinned(contentHash, pinRequest);

            var found = false;
            await ContentDirectory.UpdateAsync(contentHash, true, Clock, fileInfo =>
            {
                if (fileInfo != null)
                {
                    found = true;
                    PinContentIfContext(contentHash, pinRequest?.PinContext);
                }

                return null;
            });

            if (!found)
            {
                return null;
            }

            return GetPrimaryPathFor(contentHash);
        }

        /// <summary>
        /// Gets whether the store contains the given content
        /// </summary>
        public bool Contains(ContentHash hash)
        {
            return ContentDirectory.TryGetFileInfo(hash, out _);
        }

        /// <summary>
        ///     Gives the maximum path to files stored under the cache root.
        /// </summary>
        /// <returns>Max length</returns>
        public static int GetMaxContentPathLengthRelativeToCacheRoot()
        {
            var maxHashNameLength = HashInfoLookup.All().Max(v => v.Name.Length);
            var maxHashStringLength = HashInfoLookup.All().Max(v => v.StringLength);

            int maxContentPathLengthRelativeToCacheRoot =
                Constants.SharedDirectoryName.Length +
                1 + // path separator
                maxHashNameLength +
                1 + // path separator
                HashDirectoryNameLength + // hash directory
                1 + // path separator
                maxHashStringLength + // filename base, 2 characters per byte in hex string
                1 + // dot preceding filename extension
                BlobNameExtensionLength; // filename extension

            return maxContentPathLengthRelativeToCacheRoot;
        }

        /// <inheritdoc />
        public Task<PutResult> PutFileAsync(Context context, AbsolutePath path, HashType hashType, FileRealizationMode realizationMode, Func<Stream, Stream> wrapStream, PinRequest? pinRequest)
        {
            return PutFileImplAsync(context, path, realizationMode, hashType, pinRequest, trustedHashWithSize: null, wrapStream);
        }

        /// <inheritdoc />
        public Task<PutResult> PutFileAsync(Context context, AbsolutePath path, ContentHash contentHash, FileRealizationMode realizationMode, Func<Stream, Stream> wrapStream, PinRequest? pinRequest)
        {
            return PutFileImplAsync(context, path, realizationMode, contentHash, pinRequest, wrapStream);
        }
    }
}
