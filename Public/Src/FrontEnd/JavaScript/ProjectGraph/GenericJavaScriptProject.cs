// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities.Core;

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
            [AllowNull] IReadOnlyCollection<TDependency> dependencies,
            AbsolutePath tempFolder,
            bool cacheable,
            string[] tags,
            int timeoutInMilliseconds = 0,
            int warningTimeoutInMilliseconds = 0)
        {
            Contract.RequiresNotNullOrEmpty(name);
            Contract.Requires(projectFolder.IsValid);

            Name = name;
            ProjectFolder = projectFolder;
            Dependencies = dependencies;
            TempFolder = tempFolder;
            TimeoutInMilliseconds = timeoutInMilliseconds;
            WarningTimeoutInMilliseconds = warningTimeoutInMilliseconds;
            Cacheable = cacheable;
            Tags = tags;
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

        /// <nodoc/>
        public bool Cacheable { get; }

        /// <nodoc/>
        public string[] Tags { get; }

        /// <nodoc/>
        public int TimeoutInMilliseconds { get; internal set; }

        /// <nodoc/>
        public int WarningTimeoutInMilliseconds { get; internal set; }
    }
}
