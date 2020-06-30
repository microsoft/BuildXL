// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.FrontEnd.JavaScript.ProjectGraph
{
    /// <summary>
    /// A JavaScript graph is just a collection of JavaScript projects and some graph-level configuration
    /// </summary>
    public class JavaScriptGraph<TGraphConfiguration> : GenericJavaScriptGraph<JavaScriptProject, TGraphConfiguration> where TGraphConfiguration: class
    {
        /// <nodoc/>
        public JavaScriptGraph(IReadOnlyCollection<JavaScriptProject> projects, TGraphConfiguration configuration) : base(projects, configuration)
        { }
    }
}
