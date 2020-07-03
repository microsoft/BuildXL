// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.FrontEnd.Sdk.ProjectGraph;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Utilities.GenericProjectGraphResolver
{
    /// <summary>
    /// A constructor that can schedule a generic project by turning it into a pip
    /// </summary>
    /// <remarks>
    /// A project graph builder like <see cref="ProjectGraphToPipGraphConstructor{TProject}"/> can call <see cref="TrySchedulePipForProject(TProject, QualifierId)"/>
    /// in order to add a pip representing a project to a pip graph. 
    /// If the project rules itself out from being added to a pip graph by virtue of <see cref="IProjectWithDependencies{TProject}.CanBeScheduled"/>, then a project graph builder
    /// can notify that the given project won't be scheduled by calling <see cref="NotifyProjectNotScheduled(TProject)"/>.
    /// </remarks>
    public interface IProjectToPipConstructor<TProject> where TProject : IProjectWithDependencies<TProject>
    {
        /// <summary>
        /// Tries to schedule a project by turning it into a process pip and adding it to a pip graph.
        /// </summary>
        /// <returns> On success, the scheduled process pip.</returns>
        Possible<Pips.Operations.Process> TrySchedulePipForProject(TProject project, QualifierId qualifierId);

        /// <summary>
        /// When a project cannot be scheduled because <see cref="IProjectWithDependencies{TProject}.CanBeScheduled"/> returned false, the constructor is notified with this callback
        /// </summary>
        void NotifyProjectNotScheduled(TProject project);
    }
}
