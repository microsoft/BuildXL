// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.JavaScript
{
    /// <summary>
    /// General failure for constructing a pip graph
    /// </summary>
    /// <remarks>
    /// Used by the corresponding workspace resolver to indicate a failure to the host.
    /// </remarks>
    public class JavaScriptGraphConstructionFailure : Failure
    {
        private readonly IProjectGraphResolverSettings m_settings;
        private readonly PathTable m_pathTable;

        /// <nodoc/>
        public JavaScriptGraphConstructionFailure(IProjectGraphResolverSettings settings, PathTable pathTable)
        {
            Contract.Requires(settings != null);
            Contract.Requires(pathTable != null);

            m_settings = settings;
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
            return I($"A project graph could not be constructed when parsing module '{m_settings.ModuleName}' starting at root '{m_settings.Root.ToString(m_pathTable)}'. Detailed errors should have already been logged.");
        }

        /// <inheritdoc/>
        public override BuildXLException Throw()
        {
            throw CreateException();
        }
    }
}
