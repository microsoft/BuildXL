// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using BuildXL.Utilities;

namespace BuildXL.Pips
{
    /// <summary>
    /// Statistics about graph reloading (for the purpose of later patching it).
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815:ShouldOverrideEquals", Justification = "Never used in comparisons")]
    public struct GraphPatchingStatistics
    {
        /// <summary>
        /// Elapsed time in milliseconds it took to reload the graph.
        /// </summary>
        public int ElapsedMilliseconds { get; set; }

        /// <summary>
        /// Number of reloaded pips.
        /// </summary>
        public int NumPipsReloaded { get; set; }

        /// <summary>
        /// Number of pips that were automatically added by the underlying PipGraph.Builder
        /// as a consequence of reloading pips (e.g., HashSourceFile pips).
        /// </summary>
        public int NumPipsAutomaticallyAdded { get; set; }

        /// <summary>
        /// Number of pips that are not reloadable (e.g., meta pips).
        /// </summary>
        public int NumPipsNotReloadable { get; set; }

        /// <summary>
        /// Number of skipped (not reloaded) pips.
        /// </summary>
        public int NumPipsSkipped { get; set; }

        /// <summary>
        /// Affected specs that were specified when partially reloading graph (i.e., from which pips were not reloaded).
        /// </summary>
        public IEnumerable<AbsolutePath> AffectedSpecs { get; set; }
    }
}
