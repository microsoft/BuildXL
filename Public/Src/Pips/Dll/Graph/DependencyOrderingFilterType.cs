// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Pips.Graph
{
    /// <summary>
    /// Relative dependency ordering required for a <see cref="DependencyOrderingFilter" />.
    /// </summary>
    public enum DependencyOrderingFilterType
    {
        /// <summary>
        /// The found pip must possibly precede the reference pip in wall-clock time.
        /// This excludes only the case that the found pip is ordered after the reference pip.
        /// </summary>
        PossiblyPrecedingInWallTime,

        /// <summary>
        /// The found pip must be concurrent with the reference pip.
        /// (it may not be ordered after or ordered before the reference pip).
        /// </summary>
        Concurrent,

        /// <summary>
        /// The found pip must be ordered befor the reference pip.
        /// </summary>
        OrderedBefore,
    }
}
