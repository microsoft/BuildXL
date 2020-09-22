// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Tracing;

namespace BuildXL.Cache.ContentStore.Utils
{
    /// <summary>
    /// Performance counters available for <see cref="ResourcePoolV2{TKey, TObject}"/>.
    /// </summary>
    public enum ResourcePoolV2Counters
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
