// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.FrontEnd.JavaScript.ProjectGraph
{
    /// <summary>
    /// Generic version of a JavaScript graph where the project is parametric
    /// </summary>
    /// <remarks>
    /// Useful for deserialization, when the project is a flattened version of what the final project will become
    /// Allows to introduce a generic TGraphConfiguration on construction, intenteded for graph-level configuration
    /// information different JavaScript coordinators may have
    /// </remarks>
    public class GenericJavaScriptGraph<TProject, TGraphConfiguration> where TGraphConfiguration : class
    {
        /// <nodoc/>
        public GenericJavaScriptGraph(IReadOnlyCollection<TProject> projects, TGraphConfiguration configuration = default)
        {
            Projects = projects;
            Configuration = configuration;
        }

        /// <nodoc/>
        public TGraphConfiguration Configuration { get; }

        /// <nodoc/>
        public IReadOnlyCollection<TProject> Projects { get; }
    }
}
