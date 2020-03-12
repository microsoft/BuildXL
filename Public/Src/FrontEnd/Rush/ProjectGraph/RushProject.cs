// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics;
using BuildXL.FrontEnd.Sdk.ProjectGraph;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Rush.ProjectGraph
{
    /// <summary>
    /// A Rush project is a projection of a RushConfigurationProject (defined in rush-core-lib) with enough information to schedule a pip from it
    /// </summary>
    [DebuggerDisplay("{Name}")]
    public sealed class RushProject : GenericRushProject<RushProject>, IProjectWithDependencies<RushProject>
    {
        /// <nodoc/>
        public RushProject(
            string name,
            AbsolutePath projectFolder,
            string buildCommand,
            AbsolutePath tempFolder,
            IReadOnlyCollection<AbsolutePath> additionalOutputDirectories) : base(name, projectFolder, null, buildCommand, tempFolder, additionalOutputDirectories)
        {
        }

        /// <nodoc/>
        public static RushProject FromGenericRushProject<T>(GenericRushProject<T> genericRushProject)
        {
            return new RushProject(
                genericRushProject.Name,
                genericRushProject.ProjectFolder,
                genericRushProject.BuildCommand,
                genericRushProject.TempFolder,
                genericRushProject.AdditionalOutputDirectories);
        }

        /// <nodoc/>
        public void SetDependencies(IReadOnlyCollection<RushProject> dependencies)
        {
            Dependencies = dependencies;
        }

        /// <inheritdoc/>
        /// TODO: filter projects with empty build command here
        public bool CanBeScheduled() => true;
    }
}
