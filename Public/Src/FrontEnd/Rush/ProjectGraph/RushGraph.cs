// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.FrontEnd.Rush.ProjectGraph
{
    /// <summary>
    /// A Rush graph is just a collection of Rush projects and some graph-level configuration
    /// </summary>
    public sealed class RushGraph : GenericRushGraph<RushProject>
    {
        /// <nodoc/>
        public RushGraph(IReadOnlyCollection<RushProject> projects, RushConfiguration configuration) : base(projects, configuration)
        { }
    }
}
