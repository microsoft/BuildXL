// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Engine.Cache
{
    /// <summary>
    /// Files that are part of a cached graph.
    /// </summary>
    public enum GraphCacheFile
    {
        /// <summary>
        /// List of input files that generated the graph files (determines usability).
        /// </summary>
        PreviousInputs,

        /// <nodoc />
        PipTable,

        /// <nodoc />
        PathTable,

        /// <nodoc />
        StringTable,

        /// <nodoc />
        SymbolTable,

        /// <nodoc />
        QualifierTable,

        /// <nodoc />
        MountPathExpander,

        /// <nodoc />
        ConfigState,

        /// <nodoc />
        DirectedGraph,

        /// <nodoc />
        PipGraph,

        /// <nodoc />
        PipGraphId,

        /// <nodoc />
        HistoricTableSizes,
    }
}
