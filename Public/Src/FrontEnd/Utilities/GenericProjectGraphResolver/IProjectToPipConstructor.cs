// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.FrontEnd.Sdk.ProjectGraph;
using BuildXL.Pips.Builders;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Utilities.GenericProjectGraphResolver
{
    /// <summary>
    /// A constructor that can schedule a generic project by turning it into a pip
    /// </summary>
    /// <remarks>
    /// A project graph builder like <see cref="ProjectGraphToPipGraphConstructor{TProject}"/> can call <see cref="TryCreatePipForProject(TProject, QualifierId)"/>
    /// in order to create a pip representing a project and subsequently call <see cref="TrySchedulePipForProject(ProjectCreationResult{TProject}, QualifierId)"/> to add it to the pip graph. 
    /// If the project rules itself out from being added to a pip graph by virtue of <see cref="IProjectWithDependencies{TProject}.CanBeScheduled"/>, then a project graph builder
    /// can notify that the given project won't be scheduled by calling <see cref="NotifyProjectNotScheduled(TProject)"/>.
    /// If the scheduling of a given project is customized and the pip constructor is not reponsible for adding it to the pip graph, <see cref="NotifyCustomProjectScheduled(TProject, ProcessOutputs, QualifierId)"/> is called
    /// to keep the pip constructor aware of its existence.
    /// </remarks>
    public interface IProjectToPipConstructor<TProject> where TProject : IProjectWithDependencies<TProject>
    {
        /// <summary>
        /// Tries to turn a project into a process pip.
        /// </summary>
        /// <remarks>
        /// It does not add the resulting pip to the graph.
        /// </remarks>
        Possible<ProjectCreationResult<TProject>> TryCreatePipForProject(TProject project, QualifierId qualifierId);

        /// <summary>
        /// Tries to add a process to the pip graph created by <see cref="TryCreatePipForProject(TProject, QualifierId)"/>
        /// </summary>
        /// <returns> On success, the scheduled process pip.</returns>
        public Possible<ProcessOutputs> TrySchedulePipForProject(ProjectCreationResult<TProject> project, QualifierId qualifierId);

        /// <summary>
        /// Notify the constructor that a pip was scheduled by an external entity, so the pip constructor can account for it in case
        /// other pips depend on it
        /// </summary>
        public void NotifyCustomProjectScheduled(TProject project, ProcessOutputs outputs, QualifierId qualifierId);

        /// <summary>
        /// When a project cannot be scheduled because <see cref="IProjectWithDependencies{TProject}.CanBeScheduled"/> returned false, the constructor is notified with this callback
        /// </summary>
        void NotifyProjectNotScheduled(TProject project);
    }

    /// <nodoc/>
    public static class ProjectToPipConstructorExtensions
    {
        /// <summary>
        /// Tries to turn a project into a pip and add it to the pip graph
        /// </summary>
        public static Possible<ProcessOutputs> TrySchedulePipForProject<TProject>(this IProjectToPipConstructor<TProject> constructor, TProject project, QualifierId qualifierId) where TProject : IProjectWithDependencies<TProject>
        {
            return constructor.TryCreatePipForProject(project, qualifierId).Then(creationResult => constructor.TrySchedulePipForProject(creationResult, qualifierId));
        }
    }
}
