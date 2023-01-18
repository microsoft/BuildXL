// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Cache.ContentStore.Interfaces.Tracing
{
    /// <summary>
    /// Defines the source of a file or metadata amongst the various cache layers.
    /// </summary>
    public enum ArtifactSource
    {
        /// <summary>
        /// Default zero value.
        /// </summary>
        Unknown,

        /// <summary>
        /// The file was present in the local cache ("L1").
        /// </summary>
        LocalCache,

        /// <summary>
        /// The file comes from the datacenter peer-to-peer cache ("L2").
        /// </summary>
        DatacenterCache,

        /// <summary>
        /// The file comes from backing store, i.e. Artifact Services or "L3".
        /// </summary>
        BackingStore,
    }
}
