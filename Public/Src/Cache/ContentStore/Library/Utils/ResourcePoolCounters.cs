// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Cache.ContentStore.Utils
{
    /// <summary>
    /// Performance counters available for <see cref="ResourcePool{TKey, TObject}"/>.
    /// </summary>
    public enum ResourcePoolCounters
    {
        /// <nodoc />
        CreatedResources,

        /// <nodoc />
        ResourceInitializationAttempts,

        /// <nodoc />
        ResourceInitializationSuccesses,

        /// <nodoc />
        ResourceInitializationFailures,

        /// <nodoc />
        ReleasedResources,

        /// <nodoc />
        ShutdownAttempts,

        /// <nodoc />
        ShutdownSuccesses,

        /// <nodoc />
        ShutdownFailures,

        /// <nodoc />
        ShutdownExceptions,

        /// <nodoc />
        GarbageCollectionAttempts,

        /// <nodoc />
        GarbageCollectionSuccesses,

        /// <nodoc />
        BackgroundGarbageCollectionFailures,
    }
}
