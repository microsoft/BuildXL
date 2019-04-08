// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.MsBuild
{
    /// <summary>
    /// General failure for constructing a pip graph using the MsBuild static graph API
    /// </summary>
    /// <remarks>
    /// Used by the corresponding workspace resolver to indicate a failure to the host.
    /// </remarks>
    public class MsBuildGraphConstructionFailure : Failure
    {
        private readonly IMsBuildResolverSettings m_settings;
        private readonly PathTable m_pathTable;

        /// <nodoc/>
        public MsBuildGraphConstructionFailure(IMsBuildResolverSettings settings, PathTable pathTable)
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
            return I($"A project graph could not be constructed when parsing module '{m_settings.ModuleName}' starting at root '{m_settings.RootTraversal.ToString(m_pathTable)}'. Detailed errors should have already been logged.");
        }

        /// <inheritdoc/>
        public override BuildXLException Throw()
        {
            throw CreateException();
        }
    }
}
