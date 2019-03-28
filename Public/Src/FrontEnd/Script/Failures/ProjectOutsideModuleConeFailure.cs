// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.Globalization;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.FrontEnd.Sdk;

namespace BuildXL.FrontEnd.Script.Failures
{
    /// <summary>
    /// A project is outside of the module cone
    /// </summary>
    public sealed class ProjectOutsideModuleConeFailure : WorkspaceFailure
    {
        private readonly AbsolutePath m_outOfConeProject;
        private readonly Package m_package;
        private readonly PathTable m_pathTable;

        /// <nodoc/>
        public ProjectOutsideModuleConeFailure(PathTable pathTable, Package package, AbsolutePath outOfConeProject)
        {
            Contract.Requires(pathTable != null);
            Contract.Requires(package != null);
            Contract.Requires(outOfConeProject.IsValid);

            m_pathTable = pathTable;
            m_package = package;
            m_outOfConeProject = outOfConeProject;
        }

        /// <inheritdoc/>
        public override string Describe()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "Project '{0}' is outside of module '{1}' rooted at  '{2}'. Project files should be physically within its module root.",
                m_outOfConeProject.ToString(m_pathTable),
                m_package.Descriptor.Name,
                m_package.Path.GetParent(m_pathTable).ToString(m_pathTable));
        }
    }
}
