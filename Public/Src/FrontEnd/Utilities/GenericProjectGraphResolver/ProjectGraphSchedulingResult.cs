// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Sdk.ProjectGraph;
using BuildXL.Pips.Builders;

namespace BuildXL.FrontEnd.Utilities.GenericProjectGraphResolver
{
    /// <summary>
    /// Represents the result of scheduling a project graph when using a <see cref="ProjectGraphToPipGraphConstructor{TProject}"/>
    /// </summary>
    public sealed class ProjectGraphSchedulingResult<TProject> where TProject : IProjectWithDependencies<TProject>
    {
        /// <nodoc/>
        public ProjectGraphSchedulingResult(IReadOnlyDictionary<TProject, ProcessOutputs> scheduledProcessOutputs)
        {
            Contract.RequiresNotNull(scheduledProcessOutputs);
            ScheduledProcessOutputs = scheduledProcessOutputs;
        }

        /// <summary>
        /// The outputs of each scheduled process pip, with its corresponding original project
        /// </summary>
        public IReadOnlyDictionary<TProject, ProcessOutputs> ScheduledProcessOutputs { get; }
    }
}
