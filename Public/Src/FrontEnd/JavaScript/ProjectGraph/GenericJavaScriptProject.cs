// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using JetBrains.Annotations;

namespace BuildXL.FrontEnd.JavaScript.ProjectGraph
{
    /// <summary>
    /// A version of a JavaScript project where the collection of dependencies is generic.
    /// </summary>
    [DebuggerDisplay("{Name}")]
    public class GenericJavaScriptProject<TDependency>
    {
        /// <nodoc/>
        public GenericJavaScriptProject(
            string name,
            AbsolutePath projectFolder,
            [CanBeNull] IReadOnlyCollection<TDependency> dependencies,
            AbsolutePath tempFolder)
        {   
            Contract.RequiresNotNullOrEmpty(name);
            Contract.Requires(projectFolder.IsValid);

            Name = name;
            ProjectFolder = projectFolder;
            Dependencies = dependencies;
            TempFolder = tempFolder;
        }

        /// <nodoc/>
        public string Name { get; }

        /// <nodoc/>
        public AbsolutePath ProjectFolder { get; }

        /// <nodoc/>
        public AbsolutePath PackageJsonFile(PathTable pathTable) => ProjectFolder.Combine(pathTable, "package.json");

        /// <nodoc/>
        public AbsolutePath NodeModulesFolder(PathTable pathTable) => ProjectFolder.Combine(pathTable, "node_modules");

        /// <nodoc/>
        public IReadOnlyCollection<TDependency> Dependencies { get; internal set; }

        /// <nodoc/>
        /// <remarks>Can be invalid</remarks>
        public AbsolutePath TempFolder { get; }
    }
}
