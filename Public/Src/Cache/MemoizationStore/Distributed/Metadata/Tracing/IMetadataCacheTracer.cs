// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;

namespace BuildXL.Cache.MemoizationStore.Distributed.Metadata.Tracing
{
    /// <summary>
    /// Provides a tracer interface for metadata cache calls.
    /// </summary>
    public interface IMetadataCacheTracer
    {
        /// <summary>
        /// Records the start of a GetSelectors call.
        /// </summary>
        void GetDistributedSelectorsStart(Context context);

        /// <summary>
        /// Records the end of a GetSelectors call.
        /// </summary>
        void GetDistributedSelectorsStop(Context context, TimeSpan elapsed);

        /// <summary>
        /// Records the start of an AddSelectors call.
        /// </summary>
        void AddSelectorsStart(Context context);

        /// <summary>
        /// Records the end of an AddSelectors call.
        /// </summary>
        void AddSelectorsStop(Context context, TimeSpan elapsed);

        /// <summary>
        /// Records the start of an invalidation call in redis.
        /// </summary>
        void InvalidateCacheEntryStart(Context context, StrongFingerprint strongFingerprint);

        /// <summary>
        /// Records the end of an invalidation call in redis.
        /// </summary>
        void InvalidateCacheEntryStop(Context context, TimeSpan elapsed);

        /// <summary>
        /// Records the start of a get call for ContentHashLists in redis.
        /// </summary>
        void GetContentHashListStart(Context context);

        /// <summary>
        /// Records the end of a get call for ContentHashLists in redis.
        /// </summary>
        void GetContentHashListStop(Context context, TimeSpan elapsed);

        /// <summary>
        /// Records the start of an add call for ContentHashLists in redis.
        /// </summary>
        void AddContentHashListStart(Context context);

        /// <summary>
        /// Records the end of an add call for ContentHashLists in redis.
        /// </summary>
        void AddContentHashListStop(Context context, TimeSpan elapsed);

        /// <summary>
        /// Records the fact that a content hash list was satisfied in redis.
        /// </summary>
        void RecordGetContentHashListFetchedDistributed(Context context, StrongFingerprint strongFingerprint);

        /// <summary>
        /// Records that a contenthashlist call faulted to the backing store.
        /// </summary>
        void RecordContentHashListFetchedFromBackingStore(Context context, StrongFingerprint strongFingerprint);

        /// <summary>
        /// Records that a getselectors call faulted to the backing store.
        /// </summary>
        void RecordSelectorsFetchedFromBackingStore(Context context, Fingerprint weakFingerprint);

        /// <summary>
        /// Records the fact that a selectors call was satisfied in redis.
        /// </summary>
        void RecordSelectorsFetchedDistributed(Context context, Fingerprint weakFingerprint, int selectorsCount);
    }
}
