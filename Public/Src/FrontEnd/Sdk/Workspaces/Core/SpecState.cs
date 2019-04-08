// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.FrontEnd.Workspaces.Core
{
    /// <summary>
    /// Spec file state used by incremental graph construction.
    /// </summary>
    /// <remarks>
    /// Different spec states help to construct the smallest cone that needs to be evaluated for constructing partial graph or for incremental graph construction.
    /// </remarks>
    public enum SpecState
    {
        /// <summary>
        /// The spec was changed since last run or it is a non-incremental mode.
        /// </summary>
        Changed,

        /// <summary>
        /// The spec depends on a changed spec.
        /// </summary>
        Downstream,

        /// <summary>
        /// The spec is used by a changed spec.
        /// </summary>
        Upstream,

        /// <summary>
        /// The spec is used by a spec that depends on a changed spec.
        /// </summary>
        IndirectUpstream,

        /// <summary>
        /// The spec is not affected by the current set of changes.
        /// </summary>
        Unaffected,

        /// <summary>
        /// The spec is filtered out after applying the filter.
        /// </summary>
        FilteredOut,
    }
}
