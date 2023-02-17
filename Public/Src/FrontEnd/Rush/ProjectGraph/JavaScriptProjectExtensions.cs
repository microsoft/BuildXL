// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.FrontEnd.JavaScript.ProjectGraph;
using BuildXL.Utilities.Core;

namespace BuildXL.FrontEnd.Rush.ProjectGraph
{
    /// <nodoc/>
    public static class JavaScriptProjectExtensions
    {
        /// <summary>
        /// The location of the shrinkwrap-deps.json file for the given project
        /// </summary>
        /// <remarks>
        /// This is only applicable to a Rush-based build
        /// </remarks>
        public static AbsolutePath ShrinkwrapDepsFile(this JavaScriptProject project, PathTable pathTable) => project.TempFolder.Combine(pathTable, "shrinkwrap-deps.json");
    }
}
