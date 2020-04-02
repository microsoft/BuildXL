// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using JetBrains.Annotations;

namespace BuildXL.FrontEnd.Rush.ProjectGraph
{
    /// <summary>
    /// A version of a rush project where the collection of dependencies is generic.
    /// </summary>
    [DebuggerDisplay("{Name}")]
    public class GenericRushProject<TDepedency>
    {
        /// <nodoc/>
        public GenericRushProject(
            string name,
            AbsolutePath projectFolder,
            [CanBeNull] IReadOnlyCollection<TDepedency> dependencies,
            AbsolutePath tempFolder,
            [CanBeNull] IReadOnlyCollection<AbsolutePath> additionalOutputDirectories)
        {   
            Contract.RequiresNotNullOrEmpty(name);
            Contract.Requires(projectFolder.IsValid);
            Contract.Requires(tempFolder.IsValid);

            Name = name;
            ProjectFolder = projectFolder;
            Dependencies = dependencies;
            TempFolder = tempFolder;
            AdditionalOutputDirectories = additionalOutputDirectories ?? CollectionUtilities.EmptyArray<AbsolutePath>();
        }

        /// <nodoc/>
        public string Name { get; }

        /// <nodoc/>
        public AbsolutePath ProjectFolder { get; }

        /// <nodoc/>
        public AbsolutePath ProjectPath(PathTable pathTable) => ProjectFolder.Combine(pathTable, "package.json");

        /// <nodoc/>
        public IReadOnlyCollection<TDepedency> Dependencies { get; internal set; }

        /// <nodoc/>
        public AbsolutePath TempFolder { get; }

        /// <nodoc/>
        public IReadOnlyCollection<AbsolutePath> AdditionalOutputDirectories { get; }
    }
}
