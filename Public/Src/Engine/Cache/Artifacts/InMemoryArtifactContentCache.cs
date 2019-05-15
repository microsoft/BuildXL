// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Native.IO;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Tasks;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Engine.Cache.Artifacts
{
    /// <summary>
    /// Trivial artifact content cache implementation which is initially empty, and stores content only in memory.
    /// Since all content is stored in memory, this implementation is only suitable for testing.
    /// </summary>
    /// <remarks>
    /// This implementation, despite having a simple map of hash -> bytes in memory, models a distinct 'local' vs. 'remote'
    /// cache. This is used for some testing scenarios aroung e.g. zig-zag, in which the indication of which content was brought local
    /// (from somewhere remote) is significant.
    /// - After a store operation, a hash is then available locally and remotely (regardless of where it was before).
    /// - After a 'load-available' operation, a hash is marked as local if it was only remote.
    /// - Materialize operations require content to be local (note that this is stricter than the BuildCache behavior, but the strictness is very nice for testing).
    /// </remarks>
    public sealed class InMemoryArtifactContentCache : IArtifactContentCacheForTest
    {
        private readonly object m_lock;

        private readonly Dictionary<ContentHash, CacheEntry> m_content;
        private ConcurrentDictionary<string, FileRealizationMode> m_pathRealizationModes;

        private sealed class CacheEntry
        {
            public readonly byte[] Content;

            /// <remarks>
            /// We only need to model local vs. remote storage for testing scenarios in which 'bytes transferred' / 'files transferred' metrics
            /// need to be authentic.
            /// </remarks>
            public CacheSites Sites;

            public CacheEntry(byte[] content, CacheSites sites)
            {
                Content = content;
                Sites = sites;
            }
        }

        /// <nodoc />
        public InMemoryArtifactContentCache()
            : this(new object(), new Dictionary<ContentHash, CacheEntry>())
        {
        }

        /// <inheritdoc />
        public FileRealizationMode GetRealizationMode(string path)
        {
            return m_pathRealizationModes[path];
        }

        /// <inheritdoc />
        public void ReinitializeRealizationModeTracking()
        {
            m_pathRealizationModes = new ConcurrentDictionary<string, FileRealizationMode>(StringComparer.OrdinalIgnoreCase);
        }

        /// <nodoc />
        private InMemoryArtifactContentCache(
            object syncLock,
            Dictionary<ContentHash, CacheEntry> content)
        {
            Contract.Requires(syncLock != null);
            Contract.Requires(content != null);

            m_lock = syncLock;
            m_content = content;
        }

        /// <summary>
        /// Wraps the content cache's content for use with another execution context
        /// </summary>
        public InMemoryArtifactContentCache Wrap()
        {
            return new InMemoryArtifactContentCache(m_lock, m_content);
        }

        /// <inheritdoc />
        public void Clear()
        {
            m_content.Clear();
            m_pathRealizationModes?.Clear();
        }

        /// <inheritdoc />
        public void DiscardContentIfPresent(ContentHash content, CacheSites sitesToDiscardFrom)
        {
            lock (m_lock)
            {
                CacheEntry entry;
                if (m_content.TryGetValue(content, out entry))
                {
                    CacheSites newSites = entry.Sites & ~sitesToDiscardFrom;
                    if (newSites == CacheSites.None)
                    {
                        m_content.Remove(content);
                    }
                    else
                    {
                        entry.Sites = newSites;
                    }
                }
            }
        }

        /// <inheritdoc />
        public CacheSites FindContainingSites(ContentHash hash)
        {
            lock (m_lock)
            {
                CacheEntry entry;
                bool available = m_content.TryGetValue(hash, out entry);

                if (available)
                {
                    return entry.Sites;
                }
                else
                {
                    return CacheSites.None;
                }
            }
        }

        /// <inheritdoc />
        public Task<Possible<ContentAvailabilityBatchResult, Failure>> TryLoadAvailableContentAsync(IReadOnlyList<ContentHash> hashes)
        {
            return Task.Run<Possible<ContentAvailabilityBatchResult, Failure>>(
                () =>
                {
                    lock (m_lock)
                    {
                        bool allAvailable = true;
                        var results = new ContentAvailabilityResult[hashes.Count];
                        for (int i = 0; i < hashes.Count; i++)
                        {
                            CacheEntry entry;
                            bool available = m_content.TryGetValue(hashes[i], out entry);
                            long bytesTransferredRemotely;

                            // The content is now in the local site, if it wasn't already.
                            if (available)
                            {
                                bytesTransferredRemotely = (entry.Sites & CacheSites.Local) == 0
                                    ? entry.Content.Length
                                    : 0;

                                entry.Sites |= CacheSites.Local;
                            }
                            else
                            {
                                bytesTransferredRemotely = 0;
                            }

                            results[i] = new ContentAvailabilityResult(hashes[i], isAvailable: available, bytesTransferred: bytesTransferredRemotely, sourceCache: "InMemoryCache");
                            allAvailable &= available;
                        }

                        return new ContentAvailabilityBatchResult(
                            ReadOnlyArray<ContentAvailabilityResult>.FromWithoutCopy(results),
                            allContentAvailable: allAvailable);
                    }
                });
        }

        /// <inheritdoc />
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:DisposeObjectsBeforeLosingScope")]
        public Task<Possible<Stream, Failure>> TryOpenContentStreamAsync(ContentHash contentHash)
        {
            return Task.Run<Possible<Stream, Failure>>(
                () =>
                {
                    lock (m_lock)
                    {
                        CacheEntry entry;
                        if (m_content.TryGetValue(contentHash, out entry))
                        {
                            if ((entry.Sites & CacheSites.Local) == 0)
                            {
                                return new Failure<string>("Content is available in 'remote' cache but is not local. Load it locally first with TryLoadAvailableContentAsync.");
                            }

                            return new MemoryStream(entry.Content, writable: false);
                        }
                        else
                        {
                            return new Failure<string>("Content not found (locally or remotely). Store it first with TryStoreAsync.");
                        }
                    }
                });
        }

        /// <inheritdoc />
        public Task<Possible<Unit, Failure>> TryMaterializeAsync(
            FileRealizationMode fileRealizationModes,
            ExpandedAbsolutePath path,
            ContentHash contentHash)
        {
            return Task.Run<Possible<Unit, Failure>>(
                () =>
                {
                    lock (m_lock)
                    {
                        CacheEntry entry;
                        if (m_content.TryGetValue(contentHash, out entry))
                        {
                            if ((entry.Sites & CacheSites.Local) == 0)
                            {
                                return new Failure<string>("Content is available in 'remote' cache but is not local. Load it locally first with TryLoadAvailableContentAsync.");
                            }

                            string expandedPath = path.ExpandedPath;

                            // IArtifactContentCache prescribes that materialization always produces a 'new' file.
                            var mayBeDelete = FileUtilities.TryDeletePathIfExists(expandedPath);

                            if (!mayBeDelete.Succeeded)
                            {
                                return mayBeDelete.Failure;
                            }

                            try
                            {
                                if (m_pathRealizationModes != null)
                                {
                                    m_pathRealizationModes[expandedPath] = fileRealizationModes;
                                }

                                ExceptionUtilities.HandleRecoverableIOException(
                                    () =>
                                    {
                                        Directory.CreateDirectory(Path.GetDirectoryName(expandedPath));
                                        File.WriteAllBytes(expandedPath, entry.Content);
                                    },
                                    ex => { throw new BuildXLException("Failed to materialize content (content found, but couldn't write it)", ex); });

                                return Unit.Void;
                            }
                            catch (BuildXLException ex)
                            {
                                return new RecoverableExceptionFailure(ex);
                            }
                        }
                        else
                        {
                            return new Failure<string>("Content not found (locally or remotely). Store it first with TryStoreAsync.");
                        }
                    }
                });
        }

        /// <inheritdoc />
        public async Task<Possible<Unit, Failure>> TryStoreAsync(
            FileRealizationMode fileRealizationModes,
            ExpandedAbsolutePath path,
            ContentHash contentHash)
        {
            Possible<ContentHash, Failure> maybeStored = await TryStoreInternalAsync(
                path,
                fileRealizationModes,
                knownContentHash: contentHash);
            return maybeStored.Then(hash => Unit.Void);
        }

        /// <inheritdoc />
        public Task<Possible<ContentHash, Failure>> TryStoreAsync(
            FileRealizationMode fileRealizationModes,
            ExpandedAbsolutePath path)
        {
            return TryStoreInternalAsync(
                path,
                fileRealizationModes,
                knownContentHash: null);
        }

        private Task<Possible<ContentHash, Failure>> TryStoreInternalAsync(
            ExpandedAbsolutePath path,
            FileRealizationMode fileRealizationModes,
            ContentHash? knownContentHash)
        {
            return Task.Run<Possible<ContentHash, Failure>>(
                () =>
                {
                    lock (m_lock)
                    {
                        byte[] contentBytes = ExceptionUtilities.HandleRecoverableIOException(
                            () => { return File.ReadAllBytes(path.ExpandedPath); },
                            ex => { throw new BuildXLException("Failed to store content (couldn't read new content from disk)", ex); });

                        ContentHash contentHash = ContentHashingUtilities.HashBytes(contentBytes);

                        if (knownContentHash.HasValue && contentHash != knownContentHash.Value)
                        {
                            return new Failure<string>(I($"Stored content had an unexpected hash. (expected: {knownContentHash.Value}; actual: {contentHash})"));
                        }

                        CacheEntry entry;
                        if (m_content.TryGetValue(contentHash, out entry))
                        {
                            // We assume that stores of content already present somewhere still cause replication
                            // to both the local and remote sites. See class remarks.
                            entry.Sites |= CacheSites.LocalAndRemote;
                            return contentHash;
                        }
                        else
                        {
                            try
                            {
                                if (m_pathRealizationModes != null)
                                {
                                    m_pathRealizationModes[path.ExpandedPath] = fileRealizationModes;
                                }

                                // We assume that stored content is instantly and magically replicated to some remote place.
                                // See class remarks.
                                m_content[contentHash] = new CacheEntry(contentBytes, CacheSites.LocalAndRemote);

                                return contentHash;
                            }
                            catch (BuildXLException ex)
                            {
                                return new RecoverableExceptionFailure(ex);
                            }
                        }
                    }
                });
        }

        /// <inheritdoc />
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:DisposeObjectsBeforeLosingScope")]
        public async Task<Possible<Unit, Failure>> TryStoreAsync(Stream content, ContentHash contentHash)
        {
            Possible<ContentHash, Failure> maybeStored = await TryStoreInternalAsync(content, knownContentHash: null);
            return maybeStored.Then(hash => Unit.Void);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:DisposeObjectsBeforeLosingScope")]
        private Task<Possible<ContentHash, Failure>> TryStoreInternalAsync(Stream content, ContentHash? knownContentHash)
        {
            return Task.Run<Possible<ContentHash, Failure>>(
                () =>
                {
                    lock (m_lock)
                    {
                        CacheEntry entry;
                        if (knownContentHash.HasValue && m_content.TryGetValue(knownContentHash.Value, out entry))
                        {
                            // We assume that stores of content already present somewhere still cause replication
                            // to both the local and remote sites. See class remarks.
                            entry.Sites |= CacheSites.LocalAndRemote;
                            return knownContentHash.Value;
                        }
                        else
                        {
                            try
                            {
                                byte[] contentBytes = ExceptionUtilities.HandleRecoverableIOException(
                                    () =>
                                    {
                                        MemoryStream memoryStream;
                                        Stream streamToRead;
                                        if (!content.CanSeek)
                                        {
                                            memoryStream = new MemoryStream();
                                            streamToRead = memoryStream;
                                        }
                                        else
                                        {
                                            memoryStream = null;
                                            streamToRead = content;
                                        }

                                        using (memoryStream)
                                        {
                                            if (memoryStream != null)
                                            {
                                                content.CopyTo(memoryStream);
                                                memoryStream.Position = 0;
                                            }

                                            Contract.Assert(streamToRead.CanSeek);
                                            Contract.Assume(streamToRead.Length <= int.MaxValue);
                                            var length = (int)streamToRead.Length;
                                            var contentBytesLocal = new byte[length];
                                            int read = 0;
                                            while (read < length)
                                            {
                                                int readThisIteration = streamToRead.Read(contentBytesLocal, read, length - read);
                                                if (readThisIteration == 0)
                                                {
                                                    throw new BuildXLException("Unexpected end of stream");
                                                }

                                                read += readThisIteration;
                                            }

                                            return contentBytesLocal;
                                        }
                                    },
                                    ex => { throw new BuildXLException("Failed to read content from the provided stream in order to store it", ex); });

                                ContentHash contentHash = knownContentHash ?? ContentHashingUtilities.HashBytes(contentBytes);

                                // We assume that stored content is instantly and magically replicated to some remote place.
                                // See class remarks.
                                m_content[contentHash] = new CacheEntry(contentBytes, CacheSites.LocalAndRemote);

                                return contentHash;
                            }
                            catch (BuildXLException ex)
                            {
                                return new RecoverableExceptionFailure(ex);
                            }
                        }
                    }
                });
        }
    }
}
