// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Selects the serialized and loaded directed graph implementation.
    /// </summary>
    public enum DirectedGraphMode
    {
        /// <summary>
        /// Uses the legacy managed deserialized graph.
        /// </summary>
        Legacy,

        /// <summary>
        /// Uses the split memory-mapped graph.
        /// </summary>
        MemoryMapped,
    }
}
