// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.MsBuild.Serialization;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Configuration;
using static BuildXL.Utilities.Core.FormattableStringEx;

namespace BuildXL.FrontEnd.MsBuild
{
    /// <summary>
    /// Failure when scheduling a given MSBuild project
    /// </summary>
    public class MsBuildProjectSchedulingFailure : Failure
    {
        private readonly ProjectWithPredictions<AbsolutePath> m_project;
        private readonly string m_failure;
        private readonly PathTable m_pathTable;

        /// <nodoc/>
        public MsBuildProjectSchedulingFailure(ProjectWithPredictions<AbsolutePath> project, string failure, PathTable pathTable)
        {
            Contract.RequiresNotNull(project);
            Contract.RequiresNotNull(pathTable);
            Contract.RequiresNotNullOrEmpty(failure);

            m_project = project;
            m_failure = failure;
            m_pathTable = pathTable;
        }

        /// <inheritdoc/>
        public override BuildXLException CreateException()
        {
            return new BuildXLException(Describe());
        }

        /// <inheritdoc/>
        public override string Describe()
        {
            return I($"Project '{m_project.FullPath.ToString(m_pathTable)}' could not be added to the pip graph. Details: {m_failure}");
        }

        /// <inheritdoc/>
        public override BuildXLException Throw()
        {
            throw CreateException();
        }
    }
}
