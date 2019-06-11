// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.Interfaces;
using BuildXL.Engine.Cache.Artifacts;
using BuildXL.Native.IO;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Engine.Cache.Plugin.CacheCore
{
    /// <summary>
    /// Implementation of <see cref="IArtifactContentCache" /> wrapping a Cache.Core <see cref="ICacheSession"/>.
    /// </summary>
    public sealed class CacheCoreArtifactContentCache : IArtifactContentCache
    {
        private readonly PossiblyOpenCacheSession m_cache;

        private readonly RootTranslator m_rootTranslator;

        private readonly bool m_replaceExistingFileOnMaterialization;

        /// <nodoc />
        public CacheCoreArtifactContentCache(
            ICacheSession cache,
            RootTranslator rootTranslator,
            bool replaceExistingFileOnMaterialization = false)
        {
            m_cache = new PossiblyOpenCacheSession(cache);
            m_rootTranslator = rootTranslator;
            m_replaceExistingFileOnMaterialization = replaceExistingFileOnMaterialization;
        }

        /// <inheritdoc />
        public async Task<Possible<ContentAvailabilityBatchResult, Failure>> TryLoadAvailableContentAsync(IReadOnlyList<ContentHash> hashes)
        {
            // TODO: These conversions are silly.
            CasHash[] casHashes = new CasHash[hashes.Count];
            for (int i = 0; i < casHashes.Length; i++)
            {
                casHashes[i] = new CasHash(new global::BuildXL.Cache.Interfaces.Hash(hashes[i]));
            }

            Possible<ICacheSession, Failure> maybeOpen = m_cache.Get(nameof(TryLoadAvailableContentAsync));
            if (!maybeOpen.Succeeded)
            {
                return maybeOpen.Failure;
            }

            Possible<string, Failure>[] multiMaybePinned = await maybeOpen.Result.PinToCasAsync(casHashes);
            Contract.Assume(multiMaybePinned != null);
            Contract.Assume(multiMaybePinned.Length == casHashes.Length);

            var results = new ContentAvailabilityResult[casHashes.Length];
            Failure aggregateFailure = null;
            bool allContentAvailable = true;

            for (int i = 0; i < casHashes.Length; i++)
            {
                Possible<string, Failure> maybePinned = multiMaybePinned[i];

                if (maybePinned.Succeeded)
                {
                    // TODO: This doesn't indicate what is local / how much was transfered. From the similar BuildCache adapter:
                    //  long transferred = successfulResult.TransferredBytes;
                    // TODO: This API will fail just because content isn't available; see below for that case.
                    results[i] = new ContentAvailabilityResult(hashes[i], isAvailable: true, bytesTransferred: 0, sourceCache: maybePinned.Result);
                    BuildXL.Storage.Tracing.Logger.Log.StorageCacheContentPinned(Events.StaticContext, casHashes[i].ToString(), maybePinned.Result);
                }
                else if (maybePinned.Failure is NoCasEntryFailure)
                {
                    // TODO: As above: this API will fail just because content isn't available, and that case isn't distinguishable (at least by looking at the interface alone).
                    //              That's not reasonable for implementations in general, such as BuildCache (or anything else with weak LRU; failures should not be 'normal'
                    //              By contract, IArtifactContentCache defines available vs. unavailable on the (successful) result object, as established here.
                    //              Note that we have idenitifed the content-miss case by reflecting; but that is quite fragile and requires some not-yet-prescribed co-operation from the implementations as to failure type.
                    allContentAvailable = false;
                    results[i] = new ContentAvailabilityResult(hashes[i], isAvailable: false, bytesTransferred: 0, sourceCache: "ContentMiss");
                }
                else
                {
                    if (aggregateFailure == null)
                    {
                        aggregateFailure = maybePinned.Failure.Annotate("Retrieval failed for one or more content blobs by hash");
                    }

                    // We return only an aggregate error from this function. To prevent erasing information, we log each error returned from
                    // the cache here (the caller may choose to also log the aggregate failure.
                    BuildXL.Storage.Tracing.Logger.Log.StorageCacheCopyLocalError(
                        Events.StaticContext,
                        hashes[i].ToString(),
                        maybePinned.Failure.DescribeIncludingInnerFailures());
                }
            }

            if (aggregateFailure == null)
            {
                return new ContentAvailabilityBatchResult(
                    ReadOnlyArray<ContentAvailabilityResult>.FromWithoutCopy(results),
                    allContentAvailable: allContentAvailable);
            }
            else
            {
                return aggregateFailure;
            }
        }

        private struct FileToDelete
        {
            public readonly string Path;

            private FileInfo m_fileInfoPriorDeletion;

            private DateTime m_deletionTime;

            public static readonly FileToDelete Invalid = new FileToDelete(null);

            private FileToDelete(string path)
            {
                Path = path;
                m_fileInfoPriorDeletion = default;
                m_deletionTime = default;
            }

            public static FileToDelete Create(string path)
            {
                Contract.Requires(!string.IsNullOrEmpty(path));
                return new FileToDelete(path);
            }

            public bool IsValid => !string.IsNullOrEmpty(Path);

            public Possible<Unit, Failure> TryDelete()
            {
                Contract.Requires(IsValid);

                var maybeExistence = FileUtilities.TryProbePathExistence(Path, followSymlink: false);
                PathExistence pathExistence = PathExistence.Nonexistent;

                if (maybeExistence.Succeeded)
                {
                    pathExistence = maybeExistence.Result;
                }
                else
                {
                    if (File.Exists(Path))
                    {
                        pathExistence = PathExistence.ExistsAsFile;
                    }
                    else if (Directory.Exists(Path))
                    {
                        pathExistence = PathExistence.ExistsAsDirectory;
                    }

                    BuildXL.Storage.Tracing.Logger.Log.FileMaterializationMismatchFileExistenceResult(
                        Events.StaticContext, 
                        Path, 
                        maybeExistence.Failure.Describe(), 
                        pathExistence.ToString());
                }

                if (pathExistence == PathExistence.ExistsAsFile)
                {
                    m_fileInfoPriorDeletion = new FileInfo(Path);
                }

                m_deletionTime = DateTime.UtcNow;

                switch (pathExistence)
                {
                    case PathExistence.ExistsAsFile:
                        {
                            var deleteFileResult = FileUtilities.TryDeleteFile(Path);

                            if (!deleteFileResult.Succeeded)
                            {
                                return deleteFileResult.Failure.Annotate(I($"Failed to delete file '{Path}'"));
                            }

                            break;
                        }
                    case PathExistence.ExistsAsDirectory:
                        {
                            FileUtilities.DeleteDirectoryContents(Path, deleteRootDirectory: true);
                            break;
                        }
                    default:
                        break;
                }

                return Unit.Void;
            }

            public async Task<string> GetDiagnosticsAsync()
            {
                Contract.Requires(IsValid);

                FileInfo existingFile = default;
                bool hasWritableACL = false;
                uint linkCount = 0;
                string existingHashStr = string.Empty;

                if (FileUtilities.FileExistsNoFollow(Path))
                {
                    ContentHash existingHash = await ContentHashingUtilities.HashFileAsync(Path);
                    existingHashStr = existingHash.ToString();
                    existingFile = new FileInfo(Path);
                    hasWritableACL = FileUtilities.HasWritableAccessControl(Path);
                    linkCount = FileUtilities.GetHardLinkCount(Path);
                }

                string fileToDeleteTimestamp = m_fileInfoPriorDeletion != null ? m_fileInfoPriorDeletion.CreationTime.ToString("MM/dd/yyyy hh:mm:ss.fff tt") : "Non-existent";
                string existingFileTimestamp = existingFile != null ? existingFile.CreationTime.ToString("MM/dd/yyyy hh:mm:ss.fff tt") : "Non-existent";
                string deletionTime = m_deletionTime.ToLocalTime().ToString("MM/dd/yyyy hh:mm:ss.fff tt");
                var info = new[]
                {
                    I($"File: {Path}"),
                    I($"Deletion attempt time: {deletionTime}"),
                    I($"Creation time of file to delete: {fileToDeleteTimestamp}"),
                    I($"Creation time of existing file: {existingFileTimestamp}"),
                    I($"Content hash of existing file: {existingHashStr}"),
                    I($"Has writable ACL: {hasWritableACL}"),
                    I($"Link count: {linkCount}")
                };

                return string.Join(" | ", info);
            }
        }

        private async Task<Possible<Unit, Failure>> TryMaterializeCoreAsync(
            FileRealizationMode fileRealizationModes,
            ExpandedAbsolutePath path,
            ContentHash contentHash)
        {
            FileToDelete fileToDelete = FileToDelete.Create(path.ExpandedPath);

            string pathForCache = GetExpandedPathForCache(path);
            FileToDelete fileForCacheToDelete = (string.IsNullOrEmpty(pathForCache)
                    || string.Equals(path.ExpandedPath, pathForCache, OperatingSystemHelper.IsUnixOS
                            ? StringComparison.Ordinal
                            : StringComparison.OrdinalIgnoreCase))
                        ? FileToDelete.Invalid
                        : FileToDelete.Create(pathForCache);

            if (!m_replaceExistingFileOnMaterialization)
            {
                // BuildXL controls the file deletion if the place file mode is FailIfExists.

                var mayBeDelete = fileToDelete.TryDelete();
                // The file materialization below can fail if fileToDelete and fileForCacheToDelete
                // point to different object files. One can think that fileForCacheToDelete should
                // be deleted as well by adding the following expression in the above statement:
                //
                //     .Then(r => fileForCacheToDelete.IsValid ? fileForCacheToDelete.TryDelete() : r);
                //
                // However, this deletion masks a possibly serious underlying issue because we expect
                // both fileToDelete and fileForCacheToDelete point to the same object file.

                if (!mayBeDelete.Succeeded)
                {
                    return mayBeDelete.Failure;
                }
            }

            Possible<ICacheSession, Failure> maybeOpen = m_cache.Get(nameof(TryMaterializeAsync));
            Possible<string, Failure> maybePlaced = await maybeOpen.ThenAsync(cache => cache.ProduceFileAsync(
                new CasHash(new global::BuildXL.Cache.Interfaces.Hash(contentHash)),
                pathForCache,
                GetFileStateForRealizationMode(fileRealizationModes)));

            if (!maybePlaced.Succeeded && maybePlaced.Failure.DescribeIncludingInnerFailures().Contains("File exists at destination"))
            {
                string diagnostic = await fileToDelete.GetDiagnosticsAsync();
                diagnostic = fileForCacheToDelete.IsValid
                    ? diagnostic + Environment.NewLine + (await fileForCacheToDelete.GetDiagnosticsAsync())
                    : diagnostic;

                return maybePlaced.Failure.Annotate(diagnostic);
            }

            return maybePlaced.Then(p => {
                // TODO:58494: Occasionally we see odd behavior where materialization seems to succeed, but the file is not present in the file system
                //             layer. This check tries to see if the file exits after successful materialization and fails if it doesn't.
                if (!FileUtilities.FileExistsNoFollow(pathForCache))
                {
                    return new Failure<string>(string.Format(
                                CultureInfo.InvariantCulture,
                                "The file '{0}' with content hash '{1}' was materialzed successfully, but can't be found on disk",
                                pathForCache,
                                contentHash));
                }

                return new Possible<Unit, Failure>(Unit.Void);
            });
        }

        /// <inheritdoc />
        public async Task<Possible<Unit, Failure>> TryMaterializeAsync(
            FileRealizationMode fileRealizationModes,
            ExpandedAbsolutePath path,
            ContentHash contentHash)
        {
            // TODO: The cache should be able to do this itself, preferably sharing the same code.
            //       When placing content, we may be replacing output that has been hardlinked elsewhere.
            //       Deleting links requires some care and magic, e.g. if a process has the file mapped.
            //       Correspondingly, IArtifactContentCache prescribes that materialization always produces a 'new' file.

            var placeResult = await Helpers.RetryOnFailureAsync(
                async lastAttempt =>
                {
                    return await TryMaterializeCoreAsync(fileRealizationModes, path, contentHash);
                });

            return placeResult;
        }

        /// <inheritdoc />
        public async Task<Possible<Unit, Failure>> TryStoreAsync(
            FileRealizationMode fileRealizationModes,
            ExpandedAbsolutePath path,
            ContentHash contentHash)
        {
            Possible<ContentHash, Failure> maybeStored = await TryStoreAsync(fileRealizationModes, path);

            return maybeStored.Then<Unit>(
                cacheReportedHash =>
                {
                    if (cacheReportedHash == contentHash)
                    {
                        return Unit.Void;
                    }
                    else
                    {
                        return new Failure<string>(
                            string.Format(
                                CultureInfo.InvariantCulture,
                                "Stored content had an unexpected hash. (expected: {0}; actual: {1})",
                                contentHash,
                                cacheReportedHash));
                    }
                });
        }

        /// <inheritdoc />
        public async Task<Possible<ContentHash, Failure>> TryStoreAsync(
            FileRealizationMode fileRealizationModes,
            ExpandedAbsolutePath path)
        {
            string expandedPath = GetExpandedPathForCache(path);

            Possible<ICacheSession, Failure> maybeOpen = m_cache.Get(nameof(TryStoreAsync));
            Possible<CasHash, Failure> maybeStored = await maybeOpen.ThenAsync(
                async cache =>
                {
                    var result = await Helpers.RetryOnFailureAsync(
                        async lastAttempt =>
                        {
                            return await cache.AddToCasAsync(expandedPath, GetFileStateForRealizationMode(fileRealizationModes));
                        });
                    return result;
                });

            return maybeStored.Then<ContentHash>(c => c.ToContentHash());
        }

        /// <inheritdoc />
        public async Task<Possible<Unit, Failure>> TryStoreAsync(
            Stream content,
            ContentHash contentHash)
        {
            Possible<ICacheSession, Failure> maybeOpen = m_cache.Get(nameof(TryStoreAsync));
            Possible<CasHash, Failure> maybeStored = await maybeOpen.ThenAsync(
                async cache =>
                {
                    var result = await Helpers.RetryOnFailureAsync(
                        async lastAttempt =>
                        {
                            return await cache.AddToCasAsync(content, new CasHash(contentHash));
                        });
                    return result;
                });

            return maybeStored.Then<Unit>(
                cacheReportedHash =>
                {
                    if (cacheReportedHash.ToContentHash() == contentHash)
                    {
                        return Unit.Void;
                    }
                    else
                    {
                        return new Failure<string>(
                            string.Format(
                                CultureInfo.InvariantCulture,
                                "Stored content had an unexpected hash. (expected: {0}; actual: {1})",
                                contentHash,
                                cacheReportedHash.BaseHash.RawHash));
                    }
                });
        }

        /// <summary>
        /// Gets the expanded path for the cache to use when putting/placing file. This allows
        /// the cache to pick a correct CAS root if CAS roots exist on various drives which
        /// would allow it to use a hardlink rather than copy if the appropriate CAS root is chosen.
        /// </summary>
        public string GetExpandedPathForCache(ExpandedAbsolutePath path)
        {
            if (m_rootTranslator == null)
            {
                return path.ExpandedPath;
            }
            else
            {
                var translatedPath = m_rootTranslator.Translate(path.ExpandedPath);
                if (translatedPath[0].ToUpperInvariantFast() == path.ExpandedPath[0].ToUpperInvariantFast() &&
                    translatedPath.Length > path.ExpandedPath.Length)
                {
                    // The path root did not change as a result of path translation and its longer
                    // so , just return the original path since the cache only cares about the root
                    // when deciding a particular CAS to hardlink from to avoid MAX_PATH issues
                    return path.ExpandedPath;
                }

                return translatedPath;
            }
        }

        /// <inheritdoc />
        public Task<Possible<Stream, Failure>> TryOpenContentStreamAsync(ContentHash contentHash)
        {
            return m_cache.Get(nameof(TryOpenContentStreamAsync))
                .ThenAsync(cache => cache.GetStreamAsync(new CasHash(new global::BuildXL.Cache.Interfaces.Hash(contentHash))));
        }

        private static FileState GetFileStateForRealizationMode(FileRealizationMode mode)
        {
            switch (mode)
            {
                case FileRealizationMode.Copy:
                    return FileState.Writeable;
                case FileRealizationMode.HardLink:
                    return FileState.ReadOnly;
                case FileRealizationMode.HardLinkOrCopy:
                    return FileState.ReadOnly;
                default:
                    throw Contract.AssertFailure("Unhandled FileRealizationMode");
            }
        }
    }
}
