// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.FrontEnd.Sdk.ProjectGraph
{
    /// <summary>
    /// A project with direct dependencies.
    /// </summary>    
    /// <remarks>
    /// Projects implementing this interface can benefit from available generic toposorting facilities.
    /// </remarks>
    public interface IProjectWithDependencies<TProject> where TProject: IProjectWithDependencies<TProject>
    {
        /// <summary>
        /// The direct dependencies of the project
        /// </summary>
        IReadOnlyCollection<TProject> Dependencies { get; }

        /// <summary>
        /// Whether the project can be scheduled.
        /// </summary>
        /// <remarks>
        /// Examples of this is an MSBuild project whose targets to execute are empty. Or a Rush project whose build command is empty.
        /// </remarks>
        bool CanBeScheduled();
    }
}
