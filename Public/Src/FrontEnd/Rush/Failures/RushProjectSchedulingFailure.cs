// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Rush.ProjectGraph;
using BuildXL.Utilities;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.MsBuild
{
    /// <summary>
    /// Failure when scheduling a given Rush project
    /// </summary>
    public class RushProjectSchedulingFailure : Failure
    {
        private readonly RushProject m_project;
        private readonly string m_failure;

        /// <nodoc/>
        public RushProjectSchedulingFailure(RushProject project, string failure)
        {
            Contract.RequiresNotNull(project);
            Contract.RequiresNotNullOrEmpty(failure);

            m_project = project;
            m_failure = failure;
        }

        /// <inheritdoc/>
        public override BuildXLException CreateException()
        {
            return new BuildXLException(Describe());
        }

        /// <inheritdoc/>
        public override string Describe()
        {
            return I($"Project '{m_project.Name}' could not be added to the pip graph. Details: {m_failure}");
        }

        /// <inheritdoc/>
        public override BuildXLException Throw()
        {
            throw CreateException();
        }
    }
}
