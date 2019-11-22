// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Native.IO;
using BuildXL.Native.IO.Windows;
using BuildXL.Storage;
using BuildXL.Storage.ChangeTracking;
using BuildXL.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;
using Microsoft.Win32.SafeHandles;
using static BuildXL.Cache.ContentStore.Interfaces.FileSystem.VfsUtilities;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Engine.Cache.Artifacts
{
    /// <summary>
    /// A <see cref="LocalDiskContentStore"/> encapsulates content-based interactions with local filesystems, in terms of paths,
    /// and tracking changes to local filesystems over time.
    /// - Discovering content at a path (hashing; e.g. source files)
    /// - Placing content at a path (by hash) from a cache (e.g. cached tool outputs)
    /// - Storing content at a path to a cache for use later (e.g. just-generated tool outputs)
    ///
    /// Placing and storing content is also exposed by content caches (<see cref="BuildXL.Engine.Cache.Artifacts.IArtifactContentCache"/>). However, a content cache
    /// by itself provides only unconditional operations (i.e., 'definitely place content'). A local-disk content store instead
    /// statefully tracks content placed prior, so redundant work can be skipped.
    ///
    /// In terms of collected filesystem state, a local-disk content store aggregates a <see cref="FileContentTable"/> for remembering
    /// hashes (avoiding redundant placements, etc.) and a <see cref="FileChangeTracker"/> for detecting changes to placed / stored / discovered files.
    /// Unless otherwise specified, a caller can assume that an operation updates both structures - a typical caller should be able to load these structures,
    /// create a store instance around them, and later save them back, without updating them directly.
    ///
    /// Note that files are tracked with <see cref="TrackingUpdateMode.Supersede"/>, so operating on any particular path multiple times between scans is unsafe
    /// (for pip execution, this is safe since each file has some final producer pip, which runs once).
    /// </summary>
    /// <remarks>
    /// TODO: Anti-dependencies: This thing should learn how to probe for files gracefully (instead of caller probing),
    ///         and add appropriate tracking entries for the anti-dependency (e.g. TryDiscover on an absent source file should be self sufficient).
    /// TODO: Zig-zag: We should maybe only track changes to files which are *final*.
    ///     We need to revisit the management of artifact states in zig-zag, so that we distinguish non-final placement (maybe
    ///     we place the same file multiple times during top-down materialization) from final placement.
    /// </remarks>
    [SuppressMessage("Microsoft.Design", "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable")]
    public sealed class LocalDiskContentStore : ILocalDiskFileSystemView
    {
        private readonly PathTable m_pathTable;
        private readonly FileContentTable m_fileContentTable;
        private readonly DirectoryTranslator m_pathToNormalizedPathTranslator;
        private readonly DirectoryTranslator m_normalizedPathToRealPathTranslator;
        private readonly FileChangeTrackingSelector m_fileChangeTrackerSelector;
        private readonly SemaphoreSlim m_hashingSemaphore = new SemaphoreSlim(EngineEnvironmentSettings.HashingConcurrency);
        private readonly LoggingContext m_loggingContext;
        private readonly string m_vfsCasRoot;

        /// <summary>
        /// Creates a store which tracks files and content with the provided <paramref name="fileContentTable"/> and <paramref name="fileChangeTracker"/>.
        /// The change tracker is allowed to fail each tracking operation, so long as it can record that tracking is incomplete (<see cref="FileChangeTracker.CreateDisabledTracker"/> is sufficient).
        /// The caller retains ownership of both structures and is expected to save and reload them later as needed.
        /// </summary>
        public LocalDiskContentStore(
            LoggingContext loggingContext,
            PathTable pathTable,
            FileContentTable fileContentTable,
            IFileChangeTrackingSubscriptionSource fileChangeTracker,
            DirectoryTranslator directoryTranslator = null,
            FileChangeTrackingSelector changeTrackingFilter = null,
            AbsolutePath vfsCasRoot = default)
        {
            Contract.Requires(loggingContext != null);
            Contract.Requires(pathTable != null);
            Contract.Requires(fileContentTable != null);
            Contract.Requires(fileChangeTracker != null);

            m_loggingContext = loggingContext;
            m_pathTable = pathTable;
            m_fileContentTable = fileContentTable;
            m_pathToNormalizedPathTranslator = directoryTranslator;
            m_normalizedPathToRealPathTranslator = directoryTranslator?.GetReverseTranslator();
            m_fileChangeTrackerSelector = changeTrackingFilter ?? FileChangeTrackingSelector.CreateAllowAllFilter(pathTable, fileChangeTracker);
            m_vfsCasRoot = vfsCasRoot.IsValid ? vfsCasRoot.ToString(pathTable) : null;
            if (m_vfsCasRoot != null && m_normalizedPathToRealPathTranslator != null)
            {
                // Resolve vfs cas root to actual path
                m_vfsCasRoot = m_normalizedPathToRealPathTranslator.Translate(m_vfsCasRoot);
            }
        }

        /// <nodoc />
        private CounterCollection<LocalDiskContentStoreCounter> Counters { get; } = new CounterCollection<LocalDiskContentStoreCounter>();

        /// <summary>
        /// Attempts to place content at the specified path. The content should have previously been stored or loaded into <paramref name="cache"/>.
        /// If not, this operation may fail. This operation may also fail due to I/O-related issues, such as lack of permissions to the destination.
        /// Note that the containing directory for <paramref name="path"/> is assumed to be created already.
        /// The materialized file is tracked for changes and added to the file content table.
        /// </summary>
        public async Task<Possible<ContentMaterializationResult, Failure>> TryMaterializeAsync(
            IArtifactContentCache cache,
            FileRealizationMode fileRealizationModes,
            AbsolutePath path,
            ContentHash contentHash,
            PathAtom fileName = default,
            AbsolutePath symlinkTarget = default,
            ReparsePointInfo? reparsePointInfo = null,
            bool trackPath = true,
            bool recordPathInFileContentTable = true)
        {
            using (Counters.StartStopwatch(LocalDiskContentStoreCounter.TryMaterializeTime))
            {
                ExpandedAbsolutePath expandedPath = Expand(path);
                bool virtualize = !(symlinkTarget.IsValid || reparsePointInfo?.IsActionableReparsePoint == true) && IsVirtualizedPath(expandedPath.ExpandedPath);

                // Note we have to establish existence or TryGetKnownContentHashAsync would throw.
                if (!virtualize && FileUtilities.FileExistsNoFollow(expandedPath.ExpandedPath))
                {
                    var openFlags = FileFlagsAndAttributes.FileFlagOverlapped | FileFlagsAndAttributes.FileFlagOpenReparsePoint;

                    // TODO:58494: We shouldn't need to open a handle here and then drop it immediately. The cache needs to know about the file content table
                    // and elide its own copies when appropriate.
                    SafeFileHandle handle;
                    OpenFileResult openResult = FileUtilities.TryCreateOrOpenFile(
                        expandedPath.ExpandedPath,
                        FileDesiredAccess.Synchronize,
                        FileShare.Read | FileShare.Delete,
                        FileMode.Open,
                        openFlags,
                        out handle);

                    if (openResult.Succeeded)
                    {
                        using (handle)
                        {
                            Contract.Assert(handle != null && !handle.IsInvalid);

                            VersionedFileIdentityAndContentInfo? maybeExistingDestinationIdentityAndHash;
                            try
                            {
                                maybeExistingDestinationIdentityAndHash =
                                    m_fileContentTable.TryGetKnownContentHash(expandedPath.ExpandedPath, handle);
                            }
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
                            catch
                            {
                                // TryGetKnownContentHashAsync may only throw NativeWin32Exception, but we simply catch Exception
                                // because we don't care about any particular exception.
                                maybeExistingDestinationIdentityAndHash = null;
                            }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler

                            if (maybeExistingDestinationIdentityAndHash.HasValue)
                            {
                                VersionedFileIdentityAndContentInfo existingDestinationIdentityAndHash = maybeExistingDestinationIdentityAndHash.Value;

                                if (existingDestinationIdentityAndHash.FileContentInfo.Hash == contentHash)
                                {
                                    Possible<TrackedFileContentInfo> maybeTrackedContentInfoForExistingFile = trackPath
                                        ? TrackChangesToFile(
                                            handle,
                                            expandedPath,
                                            existingDestinationIdentityAndHash,
                                            reparsePointInfo: reparsePointInfo ?? (symlinkTarget.IsValid
                                                ? ReparsePointInfo.Create(ReparsePointType.SymLink, symlinkTarget.ToString(m_pathTable))
                                                : (ReparsePointInfo?)null))
                                        : TrackedFileContentInfo.CreateUntracked(existingDestinationIdentityAndHash.FileContentInfo);

                                    // If couldn't get file name or the file (failure in tracking) name does not match, we need to materialize from the cache
                                    if (maybeTrackedContentInfoForExistingFile.Succeeded &&
                                        (!fileName.IsValid || maybeTrackedContentInfoForExistingFile.Result.FileName == fileName))
                                    {
                                        return new ContentMaterializationResult(
                                            ContentMaterializationOrigin.UpToDate,
                                            maybeTrackedContentInfoForExistingFile.Result);
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        Tracing.Logger.Log.FailedOpenHandleToGetKnownHashDuringMaterialization(
                            m_loggingContext,
                            expandedPath.ExpandedPath,
                            openResult.CreateFailureForError().DescribeIncludingInnerFailures());
                    }
                }

                if (fileName.IsValid)
                {
                    expandedPath = expandedPath.WithFileName(m_pathTable, fileName);
                }

                Possible<Unit, Failure> possibleMaterialization;
                if (!symlinkTarget.IsValid && (reparsePointInfo == null || !reparsePointInfo.Value.IsActionableReparsePoint))
                {
                    possibleMaterialization = await cache.TryMaterializeAsync(
                        fileRealizationModes,
                        expandedPath,
                        contentHash);

                    if (!possibleMaterialization.Succeeded)
                    {
                        return possibleMaterialization.Failure.Annotate("Try materialize file from cache failed");
                    }
                    else if (virtualize)
                    {
                        return possibleMaterialization.Then(p =>
                            new ContentMaterializationResult(
                                ContentMaterializationOrigin.DeployedFromCache,
                                TrackedFileContentInfo.CreateUntrackedWithUnknownLength(contentHash, PathExistence.ExistsAsFile)));
                    }
                }
                else
                {
                    possibleMaterialization = symlinkTarget.IsValid
                        ? CreateSymlinkIfNotExistsOrTargetMismatch(expandedPath.Path, symlinkTarget)
                        : CreateSymlinkIfNotExistsOrTargetMismatch(expandedPath.Path, reparsePointInfo.Value.GetReparsePointTarget());
                }

                Possible<TrackedFileContentInfo, Failure> possibleTrackedFile = await possibleMaterialization
                    .ThenAsync(p => TryOpenAndTrackPathAsync(
                        expandedPath,
                        contentHash,
                        fileName,
                        trackPath: trackPath,
                        recordPathInFileContentTable: recordPathInFileContentTable));

                return possibleTrackedFile.Then(
                    trackedFileContentInfo => new ContentMaterializationResult(ContentMaterializationOrigin.DeployedFromCache, trackedFileContentInfo));
            }
        }

        private string ExpandFileName(ref PathAtom fileName) => fileName.IsValid ? fileName.ToString(m_pathTable.StringTable) : default;

        /// <summary>
        /// Creates a symlink
        /// </summary>
        private Possible<Unit> CreateSymlinkIfNotExistsOrTargetMismatch(AbsolutePath symlink, AbsolutePath symlinkTarget)
        {
            var source = Expand(symlink);
            var target = Expand(symlinkTarget);
            bool created;

            var maybeSymbolicLink = FileUtilities.TryCreateSymlinkIfNotExistsOrTargetsDoNotMatch(source.ExpandedPath, target.ExpandedPath, true, out created);
            if (!maybeSymbolicLink.Succeeded)
            {
                return new Failure<string>($"Failed to create symlink from '{source}' to '{target}'", maybeSymbolicLink.Failure);
            }

            return Unit.Void;
        }

        /// <summary>
        /// Creates a symlink. SymlinkTarget can be any non-empty string.
        /// </summary>
        private Possible<Unit> CreateSymlinkIfNotExistsOrTargetMismatch(AbsolutePath symlink, string symlinkTarget)
        {
            Contract.Requires(!string.IsNullOrEmpty(symlinkTarget));
            var source = Expand(symlink);
            bool created;

            var maybeSymbolicLink = FileUtilities.TryCreateSymlinkIfNotExistsOrTargetsDoNotMatch(source.ExpandedPath, symlinkTarget, true, out created);
            if (!maybeSymbolicLink.Succeeded)
            {
                return new Failure<string>($"Failed to create symlink from '{source}' to '{symlinkTarget}'", maybeSymbolicLink.Failure);
            }

            return Unit.Void;
        }

        /// <summary>
        /// Ensures that the file name casing matches for the destination if file name is provided.
        /// </summary>
        private async Task<Possible<Unit, Failure>> EnsureFileNameCasingMatchAsync(ExpandedAbsolutePath expandedPath, PathAtom fileNameAtom)
        {
            var pathNameAtom = expandedPath.Path.GetName(m_pathTable);

            if (!fileNameAtom.IsValid || pathNameAtom == fileNameAtom)
            {
                return Unit.Void;
            }

            Contract.Assert(pathNameAtom.CaseInsensitiveEquals(m_pathTable.StringTable, fileNameAtom), "File name should only differ by casing");

            try
            {
                var destination = expandedPath.ExpandedPath;
                if (!FileUtilities.FileExistsNoFollow(destination))
                {
                    // File does not exist, no need to update
                    return Unit.Void;
                }

                var requiredFileName = fileNameAtom.ToString(m_pathTable.StringTable);
                var possibleActualFileName = FileUtilities.GetFileName(destination);

                if (!possibleActualFileName.Succeeded || possibleActualFileName.Result == requiredFileName)
                {
                    // File name already matches or could not be determined just return
                    return Unit.Void;
                }

                // Replace the file name with the file name with correct casing
                var updatedDestination = destination.Substring(0, destination.Length - requiredFileName.Length) + requiredFileName;

                // Move file to temporary location
                await FileUtilities.MoveFileAsync(destination, updatedDestination, replaceExisting: false);

                return Unit.Void;
            }
            catch (BuildXLException ex)
            {
                return new RecoverableExceptionFailure(ex);
            }
        }

        /// <summary>
        /// Attempts to place content at the specified path. The content should have previously been stored or loaded into <paramref name="cache"/>.
        /// If not, this operation may fail. This operation may also fail due to I/O-related issues, such as lack of permissions to the destination.
        ///
        /// This operation differs from normal materialization:
        /// - The target path is unconditionally replaced with a new, writable copy.
        /// - The file content table / change tracker are not updated to know about the new, writable copy.
        ///
        /// This is for the special purpose of materializing files that will be re-written in place:
        /// - There's no need to track the placed file at all; if all goes well, the re-written version will later be discovered or stored.
        /// - Shortcuts taken if the content is up to date are not valid. The only way we can know that the target path is writable and with link-count 1
        ///   (no extra hardlinks) is by creating a new copy that nobody has seen yet.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic", Justification = "Symmetry with other materialization methods.")]
        public Task<Possible<Unit, Failure>> TryMaterializeTransientWritableCopyAsync(
            IArtifactContentCache cache,
            AbsolutePath path,
            ContentHash contentHash) => cache.TryMaterializeAsync(FileRealizationMode.Copy, Expand(path), contentHash);

        /// <summary>
        /// Tries to enumerate a directory and track membership.
        /// </summary>
        public Possible<PathExistence> TryEnumerateDirectoryAndTrackMembership(
            AbsolutePath path,
            Action<string, FileAttributes> handleEntry)
        {
            return m_fileChangeTrackerSelector
                .GetTracker(path)
                .TryEnumerateDirectoryAndTrackMembership(path.ToString(m_pathTable), handleEntry)
                .Then(enumerationResult => enumerationResult.Existence);
        }

        /// <summary>
        /// Discovers the content of the file at the given path without storing it to a content cache.
        /// </summary>
        /// <param name="expandedPath">The path to the file.</param>
        /// <param name="forUsnQuery">When true, we are only using the handle for USN query purposes.</param>
        /// <param name="createHandleWithSequentialScan">When true access to file is intended to be sequential from beginning to end, which the system can use as a hint to optimize file caching.</param>
        private Possible<SafeFileHandle, Failure> TryCreateHandleToFile(
            string expandedPath,
            bool forUsnQuery = false,
            bool createHandleWithSequentialScan = false)
        {
            FileFlagsAndAttributes openFlags = FileFlagsAndAttributes.None
                // Open for asynchronous I/O.
                | FileFlagsAndAttributes.FileFlagOverlapped
                // Open with reparse point flag even if file may not be a reparse point.
                // For normal files, this flag is ignored.
                | FileFlagsAndAttributes.FileFlagOpenReparsePoint
                // Path can be directory symlink, and thus need FileFlagBackupSemantics otherwise access denied.
                | FileFlagsAndAttributes.FileFlagBackupSemantics;

            if (createHandleWithSequentialScan)
            {
                openFlags = openFlags | FileFlagsAndAttributes.FileFlagSequentialScan;
            }

            FileDesiredAccess desiredAccess = FileDesiredAccess.GenericRead;

            if (forUsnQuery)
            {
                // FileReadAttributes is sufficient to query the USN information and if we open the file with
                // read permissions then it can cause anti virus software to scan the file causing additional
                // overhead to this call and to the filesystem performance in general.
                desiredAccess = FileDesiredAccess.FileReadAttributes;
            }

            SafeFileHandle handle = null;
            using (Counters.StartStopwatch(LocalDiskContentStoreCounter.TryDiscoverTime_OpenProbeHandle))
            {
                OpenFileResult openResult = default;
                Helpers.RetryOnFailure(
                    lastAttempt =>
                    {
                        openResult = FileUtilities.TryCreateOrOpenFile(
                            expandedPath,
                            desiredAccess,
                            FileShare.Read | FileShare.Delete,
                            FileMode.Open,
                            openFlags,
                            out handle);

                        if (openResult.Status == OpenFileStatus.Timeout ||
                            openResult.Status == OpenFileStatus.SharingViolation)
                        {
                            Tracing.Logger.Log.TimeoutOpeningFileForHashing(m_loggingContext, expandedPath);
                            return false; // returning "not done", i.e., do retry
                        }
                        else
                        {
                            return true; // returning "done", i.e., don't retry
                        }
                    });

                if (!openResult.Succeeded)
                {
                    return openResult.CreateFailureForError().Annotate("Failed to open a file for reading in order to hash its contents");
                }
            }

            return handle;
        }

        /// <summary>
        /// Discovers the content of the file at the given path without storing it to a content cache.
        /// </summary>
        /// <param name="fileArtifact">The file to hash.</param>
        /// <param name="path">Optional: The expanded path of the fileArtifact. Passing this in is a performance optimization if the path was
        /// already expanded by the caller. This must match the fileArtifact.</param>
        /// <param name="ignoreKnownContentHash">If true, ignore recorded known content hash in <see cref="FileContentTable"/>.</param>
        /// <param name="createHandleWithSequentialScan">When true access to file is intended to be sequential from beginning to end, which the system can use as a hint to optimize file caching.</param>
        /// <remarks>
        /// The content hash of the file is recorded in <see cref="FileContentTable"/>.
        ///
        /// When <paramref name="ignoreKnownContentHash"/> is true, the recorded content hash stored in <see cref="FileContentTable"/>, if any,
        /// is not used. This essentially forces re-computation of content hash and makes <see cref="FileContentTable"/> record the recomputed
        /// content hash. <see cref="FileContentTable"/> internally has a map from <see cref="FileIdAndVolumeId"/>s to tuples (USN, content hash)
        /// of USN and cached content hash. To avoid opening handles on retrieving cached content hashes, <see cref="FileContentTable"/>, if enabled,
        /// also maintains a map form paths to <see cref="FileIdAndVolumeId"/>s. That map is populated when a content hash (and USN) is recorded, and
        /// is only invalidated during journal scan. (Yes, the map is only enabled if journal scanning is enabled.)
        ///
        /// Unfortunately, in the case of preserved output (where this method is called to compute content hash because the output is not stored to the cache),
        /// the map from paths to file id may lead to stale content hashes. Suppose that a pip P has a declared input i and a preserved output o. Do a clean build on P.
        /// Then, modify i. Run another build. The journal scanning will detect changes on i, but none on o. Thus, the only invalidated mapping is the mapping from i
        /// to its file id. Now, this method is called again, but it will get the known content hash through the mapping from paths to <see cref="FileIdAndVolumeId"/>,
        /// which by-passing the USN check. Thus, the obtained known content hash is the old one.
        ///
        /// When <paramref name="createHandleWithSequentialScan"/> is true, then a handle for <paramref name="fileArtifact"/> is opened with <see cref="FileFlagsAndAttributes.FileFlagSequentialScan"/>.
        /// According to MSDN (https://msdn.microsoft.com/en-us/library/windows/desktop/aa363858(v=vs.85).aspx#caching_behavior), specifying the <see cref="FileFlagsAndAttributes.FileFlagSequentialScan"/>
        /// flag can increase performance for applications that read large files using sequential access, which sounds perfect for hashing the file.
        /// Performance gains can be even more noticeable for applications that read large files mostly sequentially, but occasionally skip forward over small ranges of bytes.
        /// However, the OS uses <see cref="FileFlagsAndAttributes.FileFlagSequentialScan"/> as a hint for dropping other files from the standby (filesystem cache).
        /// Thus, a workable strategy would be, when BuildXL hashes files, it will set the flag when no other pips consume the output file.
        /// </remarks>
        public async Task<Possible<ContentDiscoveryResult, Failure>> TryDiscoverAsync(
            FileArtifact fileArtifact,
            ExpandedAbsolutePath path = default,
            bool ignoreKnownContentHash = false,
            bool createHandleWithSequentialScan = false)
        {
            Contract.Requires(fileArtifact.Path.IsValid);

            TimeSpan? hashingDuration = null;
            using (Counters.StartStopwatch(LocalDiskContentStoreCounter.TryDiscoverTime))
            {
                bool isTracked = m_fileChangeTrackerSelector.ShouldTrack(fileArtifact.Path);
                path = path.Path.IsValid ? path : Expand(fileArtifact.Path);
                string expandedPath = path.ExpandedPath;

                var maybeHandle = TryCreateHandleToFile(
                    expandedPath,
                    forUsnQuery: true,
                    createHandleWithSequentialScan: createHandleWithSequentialScan);

                if (!maybeHandle.Succeeded)
                {
                    return maybeHandle.Failure;
                }

                using (SafeFileHandle handle = maybeHandle.Result)
                {
                    DiscoveredContentHashOrigin origin = DiscoveredContentHashOrigin.Cached;
                    VersionedFileIdentityAndContentInfo info = default;
                    ReparsePointType reparsePointType = ReparsePointType.None;
                    string finalLocation = null;

                    Contract.Assert(handle != null && !handle.IsInvalid);

                    // We have seen few cases where closing of the stream throws an ERROR_INCORRECT_FUNCTION native error.
                    // This could be a result of opening a non-file stream while coding a BuildXL Spec build file.
                    //
                    // Either way, catch the exception and make it an recoverable one.
                    try
                    {
                        VersionedFileIdentityAndContentInfo? maybeKnownHash = default;

                        if (!ignoreKnownContentHash)
                        {
                            using (Counters.StartStopwatch(LocalDiskContentStoreCounter.TryDiscoverTime_TryGetKnownContentHash))
                            {
                                maybeKnownHash = m_fileContentTable.TryGetKnownContentHash(expandedPath, handle);
                            }
                        }

                        if (maybeKnownHash.HasValue)
                        {
                            origin = DiscoveredContentHashOrigin.Cached;
                            info = maybeKnownHash.Value;
                        }
                        else
                        {
                            var maybeReadHandle = TryCreateHandleToFile(
                                expandedPath,
                                forUsnQuery: false,
                                createHandleWithSequentialScan: createHandleWithSequentialScan);

                            if (!maybeReadHandle.Succeeded)
                            {
                                return maybeReadHandle.Failure;
                            }

                            // System.IO.FileStream has perf problems (such as creating lots of leaked, throwaway event handles) with async reads.
                            // We switched to AsyncFile here instead due to a measured decrease in handle creations.
                            using (SafeFileHandle readHandle = maybeReadHandle.Result)
                            {
                                origin = DiscoveredContentHashOrigin.NewlyHashed;

                                using (Counters.StartStopwatch(LocalDiskContentStoreCounter.TryDiscoverTime_GetReparsePointType))
                                {
                                    var possibleReparsePointType = FileUtilities.TryGetReparsePointType(expandedPath);
                                    if (!possibleReparsePointType.Succeeded)
                                    {
                                        return possibleReparsePointType.Failure;
                                    }

                                    reparsePointType = possibleReparsePointType.Result;
                                }

                                string contentPath;
                                ContentHash hash;
                                long contentLength;
                                bool shouldHashByTargetPath = FileUtilities.IsReparsePointActionable(reparsePointType);
                                if (shouldHashByTargetPath)
                                {
                                    using (Counters.StartStopwatch(LocalDiskContentStoreCounter.TryDiscoverTime_HashByTargetPath))
                                    {
                                        // This is an actionable reparse point. We simply use the hash of the reparse point target as
                                        // the content hash of this file. This is acceptable for determining whether the file has changes.
                                        // It is not correct to store this file into CAS (storing this file in the cache would
                                        // result in a hash that doesn't match the content). It might be saved into the cache metadata though.

                                        var possibleFinalLocation = FileUtilities.TryGetReparsePointTarget(readHandle, expandedPath);

                                        if (!possibleFinalLocation.Succeeded)
                                        {
                                            throw possibleFinalLocation.Failure.Throw();
                                        }

                                        finalLocation = possibleFinalLocation.Result;

                                        hash = ComputePathHash(finalLocation);
                                        contentPath = expandedPath;
                                        contentLength = finalLocation.Length;
                                        Tracing.Logger.Log.HashedSymlinkAsTargetPath(m_loggingContext, expandedPath, finalLocation);
                                    }
                                }
                                else
                                {
                                    using (Counters.StartStopwatch(LocalDiskContentStoreCounter.TryDiscoverTime_HashFileContentWithSemaphore))
                                    using (await m_hashingSemaphore.AcquireAsync())
                                    using (var file = AsyncFileFactory.CreateAsyncFile(readHandle, FileDesiredAccess.GenericRead, ownsHandle: false, path: expandedPath))
                                    using (var stream = file.CreateReadableStream())
                                    using (var innerCounter = Counters.StartStopwatch(LocalDiskContentStoreCounter.TryDiscoverTime_HashFileContent))
                                    {
                                        hash = await ContentHashingUtilities.HashContentStreamAsync((Stream)stream);
                                        contentPath = stream.File.Path ?? "<unknown-path>";
                                        contentLength = stream.Length;
                                        hashingDuration = innerCounter.Elapsed;
                                    }
                                }

                                var identity = m_fileContentTable.RecordContentHash(
                                    contentPath,
                                    readHandle,
                                    hash,
                                    contentLength);
                                info = new VersionedFileIdentityAndContentInfo(identity, new FileContentInfo(hash, contentLength));
                                Counters.AddToCounter(LocalDiskContentStoreCounter.HashFileContentSizeBytes, info.FileContentInfo.Length);
                            }
                        }
                    }
                    catch (BuildXLException ex)
                    {
                        return new RecoverableExceptionFailure(ex).Annotate("Reading and hashing an open file failed");
                    }
                    catch (AggregateException ex)
                    {
                        // In Office there were cases while they are creating their specs, where they'd open a stream to a
                        // device that closing failes with ERROR_INCORRECT_FUNCTION native error. If this happens, log a warning
                        // and continue since we have succeeded reading and hashing the content.

                        // Make sure the inner exception is IOException. That way we don't "eat-up" the wrong exception.
                        IOException ioEx = ex.InnerException as IOException;
                        if (ioEx != null)
                        {
                            // Make sure the inner.inner exception is NativeWin32Exception. That way we don't "eat-up" the wrong exception.
                            NativeWin32Exception natEx = ioEx.InnerException as NativeWin32Exception;
                            if (natEx != null)
                            {
                                Tracing.Logger.Log.ClosingFileStreamAfterHashingFailed(
                                    Events.StaticContext,
                                    expandedPath,
                                    ioEx.Message + "|" + natEx.Message,
                                    natEx.ToStringDemystified());
                            }
                            else
                            {
                                return new RecoverableExceptionFailure(new BuildXLException("Failed to hash content stream. See inner exception.", ex));
                            }
                        }

                        throw;
                    }
                    catch (IOException ex)
                    {
                        return new RecoverableExceptionFailure(new BuildXLException("Failed to hash content stream. See inner exception.", ex));
                    }

                    using (Counters.StartStopwatch(LocalDiskContentStoreCounter.TryDiscoverTime_TrackChangesToFile))
                    {
                        Possible<TrackedFileContentInfo> maybeTracked = TrackChangesToFile(
                            handle,
                            path,
                            info,
                            // Only output files require file name to be retrieved in order to maintain casing when caching
                            requiresFileName: fileArtifact.IsOutputFile,
                            reparsePointInfo: ReparsePointInfo.Create(reparsePointType, finalLocation));
                        return maybeTracked.Then(tracked => new ContentDiscoveryResult(origin, tracked, hashingDuration));
                    }
                }
            }
        }

        /// <summary>
        /// Computes hash of a given file path.
        /// </summary>
        public ContentHash ComputePathHash(AbsolutePath path)
        {
            return ComputePathHash(Expand(path).ExpandedPath);
        }

        /// <summary>
        /// Gets whether the given path is virtualized (i.e. under the vfs root)
        /// </summary>
        private bool IsVirtualizedPath(string filePath)
        {
            if (m_vfsCasRoot == null)
            {
                // Virtualization not enabled. Path is not virtualized
                return false;
            }

            if (m_normalizedPathToRealPathTranslator != null)
            {
                filePath = m_normalizedPathToRealPathTranslator.Translate(filePath);
            }

            return filePath.IsPathWithin(m_vfsCasRoot);
        }

        /// <summary>
        /// Computes hash of a given file path.
        /// </summary>
        public ContentHash ComputePathHash(string filePath)
        {
            Contract.Requires(filePath != null);

            if (m_pathToNormalizedPathTranslator != null)
            {
                filePath = m_pathToNormalizedPathTranslator.Translate(filePath);
            }

            return ContentHashingUtilities.HashString(filePath.ToUpperInvariant());
        }

        /// <summary>
        /// Tracks a non-existent relative path chain from a tracked parent root.
        /// If trackedParentPath = 'C:\foo' and relativeAbsentPath = 'a\b\c'
        /// Then the following paths are tracked as absent: 'C:\foo\a', 'C:\foo\a\b', 'C:\foo\a\b\c'.
        /// ADVANCED. Use with care.
        /// See <see cref="IFileChangeTrackingSubscriptionSource.TrackAbsentRelativePath"/>
        /// </summary>
        public bool TrackAbsentPath(AbsolutePath trackedParentPath, AbsolutePath absentChildPath)
        {
            using (Counters.StartStopwatch(LocalDiskContentStoreCounter.TrackAbsentPathTime))
            {
                string expandedTrackedParentPath = trackedParentPath.ToString(m_pathTable);
                string relativePath = trackedParentPath.ExpandRelative(m_pathTable, absentChildPath);
                return m_fileChangeTrackerSelector.GetTracker(absentChildPath).TrackAbsentRelativePath(expandedTrackedParentPath, relativePath);
            }
        }

        /// <summary>
        /// Probes for the existence of a file or directory at the given path.
        /// Changes to existence (e.g. creation of a file where one was previously not present) are tracked.
        /// </summary>
        /// <remarks>
        /// TODO: Other methods like TryDiscoverAsync should be able to handle absence-tracking, and understand the special absent-file hash
        ///       For now we just have this as a drop-in for where File.Exists and other methods were being used by callers before hashing / storing / materializing
        /// </remarks>
        public Possible<PathExistence, Failure> TryProbeAndTrackPathForExistence(AbsolutePath path)
        {
            Contract.Requires(path.IsValid);

            return TryProbeAndTrackPathForExistence(path.Expand(m_pathTable));
        }

        /// <summary>
        /// <see cref="TryProbeAndTrackPathForExistence(AbsolutePath)"/>
        /// </summary>
        public Possible<PathExistence, Failure> TryProbeAndTrackPathForExistence(ExpandedAbsolutePath path, bool? isReadOnly = default)
        {
            Contract.Requires(path.IsValid);

            using (Counters.StartStopwatch(LocalDiskContentStoreCounter.TryProbeAndTrackPathForExistenceTime))
            {
                var tracker = m_fileChangeTrackerSelector.GetTracker(path.Path);
                return tracker.TryProbeAndTrackPath(path.ExpandedPath, isReadOnly);
            }
        }

        /// <summary>
        /// Attempts to store content as found at the specified path to the given <paramref name="cache"/>.
        /// This operation may fail due to I/O-related issues, such as lack of permissions to the destination.
        /// The stored file is tracked for changes and added to the file content table.
        ///
        /// Caller can also specify whether the file to store is a symlink using <paramref name="isSymlink"/>.
        /// If <paramref name="isSymlink"/> is left unspecified, then this method will try to check if the file is a symlink.
        /// If the file is a symlink, then this method will log a warning because storing symlink to cache makes builds behave unexpectedly,
        /// e.g., cache replays symlinks as concrete files, pip may not rebuild if symlink target is modified, pip may fail if symlink target
        /// is nonexistent, etc.
        /// </summary>
        public async Task<Possible<TrackedFileContentInfo>> TryStoreAsync(
            IArtifactContentCache cache,
            FileRealizationMode fileRealizationModes,
            AbsolutePath path,
            bool tryFlushPageCacheToFileSystem,
            ContentHash? knownContentHash = null,
            bool trackPath = true,
            bool? isSymlink = null)
        {
            Contract.Requires(cache != null);
            Contract.Requires(path.IsValid);

            var expandedPath = Expand(path);
            var possiblyPreparedFile = TryPrepareFileToTrackOrStore(expandedPath, tryFlushPageCacheToFileSystem);

            if (!possiblyPreparedFile.Succeeded)
            {
                return possiblyPreparedFile.Failure;
            }

            PathAtom fileName = possiblyPreparedFile.Result.FileName;
            expandedPath = expandedPath.WithFileName(m_pathTable, fileName);

            if (!isSymlink.HasValue)
            {
                var possibleReparsePoint = FileUtilities.TryGetReparsePointType(expandedPath.ExpandedPath);
                isSymlink = possibleReparsePoint.Succeeded && possibleReparsePoint.Result == ReparsePointType.SymLink;
            }

            if (isSymlink == true)
            {
                Tracing.Logger.Log.StoreSymlinkWarning(m_loggingContext, expandedPath.ExpandedPath);
            }

            // If path is a symlink, and the file realization mode is hard-link, then the cache will create
            // a hardlink to the final target of the symlink. Thus, symlink production works under the assumption
            // that the symlink is not a dangling symlink.
            if (knownContentHash.HasValue)
            {
                // Applies only to CopyFile & rewritten files where we have already hashed the inputs to see if we should rerun the pip,
                // We may revisit this code if we ever want to completely get rid of all double hashing by having the cache not hash on ingress.
                Possible<Unit, Failure> possiblyStored = await cache.TryStoreAsync(
                    fileRealizationModes,
                    expandedPath,
                    knownContentHash.Value);

                // TryStoreAsync possibly replaced the file (such as hardlinking out of the cache, if we already had identical content).
                // So, we only track the file after TryStoreAsync is done (not earlier when we hashed it).
                return await possiblyStored.ThenAsync(
                    p => TryOpenAndTrackPathAsync(expandedPath, knownContentHash.Value, fileName, trackPath: trackPath));
            }
            else
            {
                Possible<ContentHash, Failure> possiblyStored = await cache.TryStoreAsync(
                    fileRealizationModes,
                    expandedPath);

                return await possiblyStored.ThenAsync(
                    contentHash => TryOpenAndTrackPathAsync(expandedPath, contentHash, fileName, trackPath: trackPath));
            }
        }

        /// <summary>
        /// Attempts to track file without storing into the cache.
        /// </summary>
        public async Task<Possible<TrackedFileContentInfo>> TryTrackAsync(
            FileArtifact file,
            bool tryFlushPageCacheToFileSystem,
            ContentHash? knownContentHash = null,
            bool ignoreKnownContentHashOnDiscoveringContent = false,
            bool createHandleWithSequentialScan = false)
        {
            Contract.Requires(file.IsValid);

            var expandedPath = Expand(file.Path);
            var possiblyPreparedFile = TryPrepareFileToTrackOrStore(expandedPath, tryFlushPageCacheToFileSystem);

            if (!possiblyPreparedFile.Succeeded)
            {
                return possiblyPreparedFile.Failure;
            }

            PathAtom fileName = possiblyPreparedFile.Result.FileName;
            expandedPath = expandedPath.WithFileName(m_pathTable, fileName);

            if (knownContentHash.HasValue)
            {
                return await TryOpenAndTrackPathAsync(expandedPath, knownContentHash.Value, fileName);
            }

            var possibleDiscover = await TryDiscoverAsync(
                file,
                path: expandedPath,
                ignoreKnownContentHash: ignoreKnownContentHashOnDiscoveringContent,
                createHandleWithSequentialScan: createHandleWithSequentialScan);

            if (!possibleDiscover.Succeeded)
            {
                return possibleDiscover.Failure;
            }

            return possibleDiscover.Result.TrackedFileContentInfo;
        }

        /// <summary>
        /// Prepares file for tracking or storing to cache purpose.
        /// </summary>
        public Possible<PrepareFileToTrackOrStoreResult> TryPrepareFileToTrackOrStore(ExpandedAbsolutePath path, bool tryFlushPageCacheToFileSystem)
        {
            Contract.Requires(path.IsValid);

            // Note that this may flush the file from cache back to the filesystem.
            // This must occur before cache-ingress due to ingress possibly adding a deny-write ACL.
            // Flush only if non-Unix OS because the flush method is not implemented for Unix OS.
            if (tryFlushPageCacheToFileSystem && !OperatingSystemHelper.IsUnixOS)
            {
                TryFlushPageCacheToFileSystem(path.ExpandedPath);
            }

            // Get file name prior to storing to the cache because cache may overwrite file with
            // improper casing (i.e. casing matching the path in the path table not what's on disk)
            var possibleFileName = TryGetFileName(path);
            if (!possibleFileName.Succeeded)
            {
                return possibleFileName.Failure;
            }

            return new PrepareFileToTrackOrStoreResult(possibleFileName.Result);
        }

        /// <summary>
        /// Flushes changes to the file to the filesystem. Note that this is intended as a preamble to tracking a file; caller is responsible for putting the file into the <see cref="FileChangeTracker"/> and <see cref="FileContentTable"/>
        /// </summary>
        /// <remarks>
        /// If a flush is requested, calls <see cref="FileUtilities.FlushPageCacheToFilesystem"/> for the given path. This is an alternative to <see cref="FileContentTable.RecordContentHash(FileStream, ContentHash, bool?)"/>
        /// with <c>strict: true</c> and for the same reason (avoid spurious USN changes due to file mappings); <c>RecordContentHashAsync</c> must occur after cache ingress but at that
        /// point we may have already established a deny-write ACL (happens with hardlinks enabled; that prevents opening for write in order to flush).
        ///
        /// Note that if a flush is requested, the file must be opened for write access. This means that concurrent attempts to hash the file via this same means
        /// will fail (since the concurrent hashers will not specify <c>FILE_SHARE_WRITE</c>). This is acceptable for hashing just-produced outputs and ingressing to
        /// cache, since we already reserve the right to replace the file altogether (hardlinking back out of the cache for example).
        ///
        /// If flushing is requested and fails (such as due to ACLs or sharing), this operation logs a warning but may still succeed overall (flushing is best effort; not needed for correctness).
        /// </remarks>
        private void TryFlushPageCacheToFileSystem(string path)
        {
            SafeFileHandle handle = null;

            try
            {
                OpenFileResult openResultForWriting = FileUtilities.TryCreateOrOpenFile(
                    path,
                    FileDesiredAccess.GenericWrite | FileDesiredAccess.GenericRead,
                    FileShare.Read | FileShare.Delete,
                    FileMode.Open,
                    FileFlagsAndAttributes.FileFlagOverlapped | FileFlagsAndAttributes.FileFlagOpenReparsePoint,
                    out handle);

                if (openResultForWriting.Succeeded)
                {
                    Contract.Assert(handle != null && !handle.IsInvalid);

                    using (handle)
                    {
                        using (Counters.StartStopwatch(LocalDiskContentStoreCounter.FlushFileToFilesystemTime))
                        {
                            // TODO: With UseHardLinks, a cache put may involve creating a hardlink *from* the cache if we already have the hash and then this is wasteful. In general this flush could be better positioned w.r.t. cache interaction
                            //       Flush after hashing (maybe more performant)? Avoid flush if hardlinked out of the cache? Find a way to get the same USN stability without an expensive flush-to-disk?
                            NtStatus flushStatus = FileUtilities.FlushPageCacheToFilesystem(handle);

                            if (!flushStatus.IsSuccessful)
                            {
                                Tracing.Logger.Log.StorageFailureToFlushFileOnDisk(
                                    m_loggingContext,
                                    path,
                                    flushStatus.StatusCode?.ToString() ?? string.Empty);
                            }
                        }
                    }
                }
                else
                {
                    Tracing.Logger.Log.StorageFailureToOpenFileForFlushOnIngress(
                        m_loggingContext,
                        path,
                        openResultForWriting.CreateExceptionForError().Message);
                }
            }
            finally
            {
                handle?.Dispose();
            }
        }

        private async Task<Possible<TrackedFileContentInfo, Failure>> TryOpenAndTrackPathAsync(
            ExpandedAbsolutePath path,
            ContentHash hash,
            PathAtom fileName,
            bool trackPath = true,
            // For legacy reason, recordPathInFileContentTable is default to false because if trackPath is false,
            // then recordPathInFileContentTable used to automatically be false.
            bool recordPathInFileContentTable = false)
        {
            Contract.Requires(path != null);

            // If path needs to be tracked, then path needs to be recorded in the file content table.
            recordPathInFileContentTable = trackPath ? true : recordPathInFileContentTable;

            var possibleEnsureFileName = await EnsureFileNameCasingMatchAsync(path, fileName);
            if (!possibleEnsureFileName.Succeeded)
            {
                return possibleEnsureFileName.Failure;
            }

            // TODO:58494: We shouldn't need to open a new handle here. In fact, that is a race, and why there isn't a RecordContentHashAsync taking a path.
            // Somebody could change the file since we finished copying from the cache. The cache should instead be enlightened to use the file content table.
            return Helpers.RetryOnFailure(
                    lastAttempt =>
                    {
                        var attempt = FileUtilities.UsingFileHandleAndFileLength(
                            path.ExpandedPath,
                            FileDesiredAccess.GenericRead,
                            FileShare.Read | FileShare.Delete,
                            FileMode.Open,
                            FileFlagsAndAttributes.FileFlagOverlapped | FileFlagsAndAttributes.FileFlagOpenReparsePoint,
                            (handle, length) =>
                            {
                                VersionedFileIdentityAndContentInfo? identityAndContentInfo = default;

                                if (recordPathInFileContentTable)
                                {
                                    // strict: is disabled since we instead flush in TryFlushAndHashFile
                                    var identity = m_fileContentTable.RecordContentHash(
                                            path.ExpandedPath,
                                            handle,
                                            hash,
                                            length,
                                            strict: false);
                                    identityAndContentInfo = new VersionedFileIdentityAndContentInfo(identity, new FileContentInfo(hash, length));
                                }

                                if (!trackPath)
                                {
                                    // We are not interested in tracking the file (perhaps because it has been tracked, see our handling of copy-file pip),
                                    // but we need the file length, and potentially need to ensure that the file name casing match.
                                    return new TrackedFileContentInfo(new FileContentInfo(hash, length), FileChangeTrackingSubscription.Invalid, fileName);
                                }

                                Contract.Assert(recordPathInFileContentTable && identityAndContentInfo.HasValue);

                                // Note that the identity kind may be Anonymous if we couldn't establish an identity for the target file;
                                // in that case we still need to call TrackChangesToFile to ensure the tracker latches to disabled.
                                return TrackChangesToFile(handle, path, identityAndContentInfo.Value, knownFileName: fileName);
                            });

                        if (attempt.Succeeded)
                        {
                            return new Possible<TrackedFileContentInfo, Failure>(attempt.Result);
                        }

                        return new Failure<string>($"{nameof(TrackChangesToFile)} failed to establish identity and track file: {path.ExpandedPath}");
                    });
        }

        private Possible<TrackedFileContentInfo> TrackChangesToFile(
            SafeFileHandle handle,
            ExpandedAbsolutePath path,
            in VersionedFileIdentityAndContentInfo identityAndContentInfo,
            PathAtom knownFileName = default,
            bool requiresFileName = true,
            ReparsePointInfo? reparsePointInfo = null)
        {
            FileChangeTrackingSubscription subscription = TrackChangesToFile(handle, path, identityAndContentInfo.Identity);

            if (requiresFileName && !knownFileName.IsValid)
            {
                var possibleFileName = TryGetFileName(path);

                if (!possibleFileName.Succeeded)
                {
                    return possibleFileName.Failure;
                }

                knownFileName = possibleFileName.Result;
            }

            return new TrackedFileContentInfo(identityAndContentInfo.FileContentInfo, subscription, knownFileName, reparsePointInfo);
        }

        private FileChangeTrackingSubscription TrackChangesToFile(SafeFileHandle handle, ExpandedAbsolutePath path, in VersionedFileIdentity identity)
        {
            using (Counters.StartStopwatch(LocalDiskContentStoreCounter.TryTrackChangesToFileTime))
            {
                // Note that the tracker will latch to Disabled on the first failure (invalid subscription returned).
                // TODO: We use the 'Supersede' update mode here - this is appropriate for callers that have one defined owner of each path (such as pip execution),
                //      rather than multiple independent observers (in which case we want to capture invalidations for *any* observer, rather than tracking only the most recent).
                //      For pip execution, supersede mode reaches a fixpoint faster - production of files by a re-run pip supersedes the old tracked versions rather than causing
                //      causing invalidations on the next scan.
                return m_fileChangeTrackerSelector.GetTracker(path.Path).TryTrackChangesToFile(
                    handle,
                    path.ExpandedPath,
                    identity,
                    TrackingUpdateMode.Supersede);
            }
        }

        private Possible<PathAtom> TryGetFileName(ExpandedAbsolutePath path)
        {
            using (Counters.StartStopwatch(LocalDiskContentStoreCounter.TryGetFileNameTime))
            {
                var possibleFileName = FileUtilities.GetFileName(path.ExpandedPath);
                return possibleFileName.Then(fileName => PathAtom.Create(m_pathTable.StringTable, fileName));
            }
        }

        private ExpandedAbsolutePath Expand(AbsolutePath path)
        {
            return path.Expand(m_pathTable);
        }

        /// <summary>
        /// Log statistics for local disk content store
        /// </summary>
        public void LogStats()
        {
            Counters.AddToCounter(LocalDiskContentStoreCounter.UntrackedPathCalls, m_fileChangeTrackerSelector.UntrackedPathRequestCount);
            Counters.AddToCounter(LocalDiskContentStoreCounter.TrackedPathCalls, m_fileChangeTrackerSelector.TrackedPathRequestCount);
            Counters.LogAsStatistics("LocalDiskContentStore", m_loggingContext);
        }

        /// <summary>
        /// Result of preparing file to track or to store.
        /// </summary>
        public readonly struct PrepareFileToTrackOrStoreResult
        {
            /// <summary>
            /// File name.
            /// </summary>
            public readonly PathAtom FileName;

            /// <summary>
            /// Creates an instance of <see cref="PrepareFileToTrackOrStoreResult"/>.
            /// </summary>
            /// <param name="fileName"></param>
            public PrepareFileToTrackOrStoreResult(PathAtom fileName)
            {
                FileName = fileName;
            }
        }
    }

    /// <summary>
    /// Origin of materialized content. The target path may have already had the desired content,
    /// or the content may have been deployed out of cache.
    /// </summary>
    public enum ContentMaterializationOrigin
    {
        /// <summary>
        /// The destination path was populated from cache.
        /// </summary>
        DeployedFromCache,

        /// <summary>
        /// The destination path already had the desired content.
        /// </summary>
        UpToDate,
    }

    /// <summary>
    /// Result of materializing content to some path.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    public readonly struct ContentMaterializationResult
    {
        /// <summary>
        /// Content info that was placed, and a tracking subscription (if not <see cref="FileChangeTrackingSubscription.Invalid"/>, then
        /// changes to the materialized file can later be detected).
        /// </summary>
        public readonly TrackedFileContentInfo TrackedFileContentInfo;

        /// <summary>
        /// Indicates up-to-date-ness of the file at the time of materialization (was a file with the correct content already present?)
        /// </summary>
        public readonly ContentMaterializationOrigin Origin;

        /// <nodoc />
        public ContentMaterializationResult(ContentMaterializationOrigin origin, TrackedFileContentInfo trackedFileContentInfo)
        {
            TrackedFileContentInfo = trackedFileContentInfo;
            Origin = origin;
        }
    }

    /// <summary>
    /// Origin of a hash discovered via <see cref="LocalDiskContentStore.TryDiscoverAsync"/>.
    /// </summary>
    public enum DiscoveredContentHashOrigin
    {
        /// <summary>
        /// The content hash was already known (cached in a <see cref="FileContentTable"/>).
        /// </summary>
        Cached,

        /// <summary>
        /// The content hash was computed just now.
        /// </summary>
        NewlyHashed,
    }

    /// <summary>
    /// Result of discovering existing content to some path.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    public readonly struct ContentDiscoveryResult
    {
        /// <summary>
        /// Content info that was discovered, and a tracking subscription (if not <see cref="FileChangeTrackingSubscription.Invalid"/>, then
        /// changes to the discovered file can later be detected).
        /// </summary>
        public readonly TrackedFileContentInfo TrackedFileContentInfo;

        /// <summary>
        /// Indicates if the content hash of the discovered content was already known, or if the file had to instead be hashed.
        /// </summary>
        public readonly DiscoveredContentHashOrigin Origin;

        /// <summary>
        /// Content hashing duration
        /// </summary>
        public readonly TimeSpan? HashingDuration;

        /// <nodoc />
        public ContentDiscoveryResult(DiscoveredContentHashOrigin origin, TrackedFileContentInfo trackedFileContentInfo, TimeSpan? hashingDuration)
        {
            TrackedFileContentInfo = trackedFileContentInfo;
            Origin = origin;
            HashingDuration = hashingDuration;
        }
    }
}
