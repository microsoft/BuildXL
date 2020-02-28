// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Sdk.ProjectGraph;

namespace BuildXL.FrontEnd.Utilities.GenericProjectGraphResolver
{
    /// <summary>
    /// Represents the result of scheduling a project graph when using a <see cref="ProjectGraphToPipGraphConstructor{TProject}"/>
    /// </summary>
    public sealed class ProjectGraphSchedulingResult<TProject> where TProject : IProjectWithDependencies<TProject>
    {
        /// <nodoc/>
        public ProjectGraphSchedulingResult(IReadOnlyDictionary<TProject, Pips.Operations.Process> scheduledProcesses)
        {
            Contract.RequiresNotNull(scheduledProcesses);
            ScheduledProcesses = scheduledProcesses;
        }

        /// <summary>
        /// Each scheduled process pip, with its corresponding original project
        /// </summary>
        public IReadOnlyDictionary<TProject, Pips.Operations.Process> ScheduledProcesses { get; }
    }
}
