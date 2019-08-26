// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Engine.Cache.Fingerprints.TwoPhase;
using BuildXL.Engine.Cache.Tracing;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tasks;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Engine.Cache.Fingerprints.SinglePhase
{
    /// <summary>
    /// Provides helper methods for storing temporal fingerprint cache entries. Temporal fingerprints have optimistic replace semantics
    /// whereby storing an entry with the same fingerprint twice is likely to replace
    /// </summary>
    public static class TemporalFingerprintExtensions
    {
        private static readonly ContentHash s_dummyPathSetHash = ContentHashingUtilities.ZeroHash;

        /// <summary>
        /// Attempts to get the cache entry which was last stored for the given fingerprint
        /// </summary>
        public static async Task<Possible<CacheEntry?>> TryGetLatestCacheEntryAsync(
            this ITwoPhaseFingerprintStore store,
            LoggingContext loggingContext,
            ContentFingerprint fingerprint,
            DateTime? time = null)
        {
            // This method starts at a leaf time node and traverses up until  a parent node
            // is found which has a corresponding cache entry. Then the children of that parent
            // node are iterated recursively (latest in time first) to find the node that closest in time
            // to the specified time parameter. This yields the cache entry added most recently.
            var node = new TimeTreeNode(time);
            while (node != null)
            {
                var getResult = await store.TryGetTemporalCacheEntryAsync(loggingContext, node, fingerprint);

                if (!getResult.Succeeded)
                {
                    return getResult;
                }
                else if (getResult.Result != null)
                {
                    return await TryGetLatestChildCacheEntryAsync(loggingContext, node, store, fingerprint, getResult);
                }

                node = node.Parent;
            }

            return (CacheEntry?)null;
        }

        /// <summary>
        /// Attempts to get the cache entry for the child which comes latest in time
        /// </summary>
        private static async Task<Possible<CacheEntry?>> TryGetLatestChildCacheEntryAsync(
            LoggingContext loggingContext,
            TimeTreeNode node,
            ITwoPhaseFingerprintStore store,
            ContentFingerprint fingerprint,
            Possible<CacheEntry?> fallbackResult)
        {
            var childNodes = node.Children;
            var children = childNodes.Select(childNode => store.TryGetTemporalCacheEntryAsync(loggingContext, childNode, fingerprint)).ToList();

            var childResults = await Task.WhenAll(children);

            // In reverse order (i.e. later in time), check children to see which
            // one has results
            for (int i = childResults.Length - 1; i >= 0; i--)
            {
                var childResult = childResults[i];
                var childNode = childNodes[i];
                if (!childResult.Succeeded)
                {
                    continue;
                }

                if (childResult.Result != null)
                {
                    return await TryGetLatestChildCacheEntryAsync(
                        loggingContext,
                        childNode,
                        store,
                        fingerprint,
                        childResult);
                }
            }

            return fallbackResult;
        }

        private static async Task<Possible<CacheEntry?, Failure>> TryGetTemporalCacheEntryAsync(
            this ITwoPhaseFingerprintStore store,
            LoggingContext loggingContext,
            TimeTreeNode node, 
            ContentFingerprint fingerprint)
        {
            // Unblock the caller
            await Task.Yield();

            var result = await store.TryGetCacheEntryAsync(
                                new WeakContentFingerprint(fingerprint.Hash),
                                s_dummyPathSetHash,
                                node.NodeFingerprint);

            if (result.Succeeded)
            {
                if (result.Result?.MetadataHash != null)
                {
                    Logger.Log.TemporalCacheEntryTrace(loggingContext, I($"Successfully retrieved temporal cache entry: Node='{node}', Fingerprint='{fingerprint}' MetadataHash='{result.Result?.MetadataHash}'"));
                }
                else {
                    Logger.Log.TemporalCacheEntryTrace(loggingContext, I($"Query for temporal cache entry returned no results: Node='{node}', Fingerprint='{fingerprint}'"));
                }
            }
            else
            {
                Logger.Log.TemporalCacheEntryTrace(loggingContext, I($"Failed retrieval of temporal cache entry: Node='{node}', Fingerprint='{fingerprint}' Failure:='{result.Failure.DescribeIncludingInnerFailures()}'"));
            }

            return result;
        }

        private static async Task<Possible<CacheEntryPublishResult, Failure>> TryPublishNodeTemporalCacheEntryAsync(
            this ITwoPhaseFingerprintStore store,
            LoggingContext loggingContext,
            TimeTreeNode node,
            ContentFingerprint fingerprint,
            CacheEntry entry)
        {
            // Unblock the caller
            await Task.Yield();

            var result = await store.TryPublishCacheEntryAsync(
                    new WeakContentFingerprint(fingerprint.Hash),
                    s_dummyPathSetHash,
                    node.NodeFingerprint,
                    entry);

            Logger.Log.TemporalCacheEntryTrace(loggingContext, I($"Publishing temporal cache entry: Node='{node}', Fingerprint='{fingerprint}' MetadataHash='{entry.MetadataHash}' Success='{result.Succeeded}'"));

            if (result.Succeeded)
            {
                Logger.Log.TemporalCacheEntryTrace(loggingContext, I($"Published temporal cache entry: Node='{node}', Fingerprint='{fingerprint}' Status='{result.Result.Status}'"));
            }
            else
            {
                Logger.Log.TemporalCacheEntryTrace(loggingContext, I($"Failed publish of temporal cache entry: Node='{node}', Fingerprint='{fingerprint}' Failure:='{result.Failure.DescribeIncludingInnerFailures()}'"));
            }

            return result;
        }

        /// <summary>
        /// Attempts to store the cache entry in the time node for the given time and
        /// all its parent nodes
        /// </summary>
        [SuppressMessage("AsyncUsage", "AsyncFixer02:awaitinsteadofwait")]
        public static async Task<Possible<Unit>> TryPublishTemporalCacheEntryAsync(
            this ITwoPhaseFingerprintStore store,
            LoggingContext loggingContext,
            ContentFingerprint fingerprint,
            CacheEntry entry,
            DateTime? time = null)
        {
            // Attempt to store the cache entry into all the time buckets
            var node = new TimeTreeNode(time);
            var tasks = new List<Task<Possible<CacheEntryPublishResult, Failure>>>();

            while (node != null)
            {
                tasks.Add(store.TryPublishNodeTemporalCacheEntryAsync(
                    loggingContext,
                    node,
                    fingerprint,
                    entry));

                node = node.Parent;
            }

            await Task.WhenAll(tasks);
            foreach (var task in tasks)
            {
                if (!task.Result.Succeeded)
                {
                    return task.Result.Failure;
                }
            }

            return Unit.Void;
        }
    }
}
