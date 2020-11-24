// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Sdk.ProjectGraph;
using BuildXL.Pips.Builders;

namespace BuildXL.FrontEnd.Utilities.GenericProjectGraphResolver
{
    /// <summary>
    /// Represents the result of creating a process pip out of a project"/>
    /// </summary>
    public readonly struct ProjectCreationResult<TProject> where TProject : IProjectWithDependencies<TProject>
    {
        /// <nodoc/>
        public ProjectCreationResult(TProject project, Pips.Operations.Process process, ProcessOutputs outputs)
        {
            Contract.RequiresNotNull(process);
            Contract.RequiresNotNull(outputs);

            Project = project;
            Process = process;
            Outputs = outputs;
        }

        /// <nodoc/>
        public TProject Project { get; }

        /// <nodoc/>
        public ProcessOutputs Outputs { get; }

        /// <nodoc/>
        public Pips.Operations.Process Process { get; }
    }
}
