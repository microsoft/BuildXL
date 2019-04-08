// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Engine.Cache.Fingerprints.TwoPhase
{
    /// <summary>
    /// Trivial two-phase fingerprint store implementation which is initially empty, and stores data only in memory.
    /// Since all content is stored in memory, this implementation is only suitable for testing.
    /// </summary>
    public sealed class InMemoryTwoPhaseFingerprintStore : ITwoPhaseFingerprintStore
    {
        private readonly ConcurrentBigMap<WeakContentFingerprint, Node> m_entries =
            new ConcurrentBigMap<WeakContentFingerprint, Node>();

        private readonly string m_cacheId;

        private sealed class Node
        {
            public readonly ContentHash PathSetHash;
            public readonly StrongContentFingerprint StrongFingerprint;
            public readonly CacheEntry CacheEntry;
            public Node Next;

            public Node(ContentHash pathSetHash, StrongContentFingerprint strongFingerprint, CacheEntry cacheEntry, Node next = null)
            {
                PathSetHash = pathSetHash;
                StrongFingerprint = strongFingerprint;
                CacheEntry = cacheEntry;
                Next = next;
            }
        }

        /// <nodoc />
        public InMemoryTwoPhaseFingerprintStore(string cacheId = nameof(InMemoryTwoPhaseFingerprintStore))
        {
            m_cacheId = cacheId;
        }

        /// <inheritdoc />
        public IEnumerable<Task<Possible<PublishedEntryRef, Failure>>> ListPublishedEntriesByWeakFingerprint(WeakContentFingerprint weak)
        {
            Node node;
            if (m_entries.TryGetValue(weak, out node))
            {
                while (node != null)
                {
                    yield return Task.FromResult(
                        new Possible<PublishedEntryRef, Failure>(
                            new PublishedEntryRef(
                                node.PathSetHash,
                                node.StrongFingerprint,
                                m_cacheId,
                                PublishedEntryRefLocality.Local)));
                    node = node.Next;
                }
            }
        }

        /// <inheritdoc />
        public Task<Possible<CacheEntry?, Failure>> TryGetCacheEntryAsync(WeakContentFingerprint weakFingerprint, ContentHash pathSetHash, StrongContentFingerprint strongFingerprint)
        {
            Node node;
            if (m_entries.TryGetValue(weakFingerprint, out node))
            {
                while (node != null)
                {
                    if (node.PathSetHash == pathSetHash && node.StrongFingerprint == strongFingerprint)
                    {
                        return Task.FromResult(new Possible<CacheEntry?, Failure>(node.CacheEntry));
                    }

                    node = node.Next;
                }
            }

            return Task.FromResult(new Possible<CacheEntry?, Failure>((CacheEntry?)null));
        }

        /// <inheritdoc />
        public Task<Possible<CacheEntryPublishResult, Failure>> TryPublishCacheEntryAsync(
            WeakContentFingerprint weakFingerprint,
            ContentHash pathSetHash,
            StrongContentFingerprint strongFingerprint,
            CacheEntry entry,
            CacheEntryPublishMode mode = CacheEntryPublishMode.CreateNew)
        {
            var newNode = new Node(pathSetHash, strongFingerprint, entry);
            var updatedNode = m_entries.AddOrUpdate(
                weakFingerprint,
                newNode,
                (key, node) => node,
                (key, node, existingNode) =>
                {
                    Node currentNode = existingNode;
                    Node priorNode = null;
                    while (currentNode != null)
                    {
                        if (node.PathSetHash == currentNode.PathSetHash && node.StrongFingerprint == currentNode.StrongFingerprint)
                        {
                            if (mode == CacheEntryPublishMode.CreateNewOrReplaceExisting)
                            {
                                if (priorNode != null)
                                {
                                    priorNode.Next = node;
                                }

                                node.Next = node;
                            }

                            return existingNode;
                        }

                        priorNode = currentNode;
                        currentNode = currentNode.Next;
                    }

                    node.Next = existingNode;
                    return node;
                }).Item.Value;

            if (mode == CacheEntryPublishMode.CreateNew)
            {
                if (updatedNode != newNode)
                {
                    return Task.FromResult(new Possible<CacheEntryPublishResult, Failure>(CacheEntryPublishResult.CreateConflictResult(updatedNode.CacheEntry)));
                }
            }

            return Task.FromResult(new Possible<CacheEntryPublishResult, Failure>(CacheEntryPublishResult.CreatePublishedResult()));
        }
    }
}
