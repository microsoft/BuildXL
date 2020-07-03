// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.JavaScript.ProjectGraph;
using BuildXL.Utilities;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.JavaScript
{
    /// <summary>
    /// Failure when scheduling a given JavaScript project
    /// </summary>
    public class JavaScriptProjectSchedulingFailure : Failure
    {
        private readonly JavaScriptProject m_project;
        private readonly string m_failure;

        /// <nodoc/>
        public JavaScriptProjectSchedulingFailure(JavaScriptProject project, string failure)
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
