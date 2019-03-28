// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using JetBrains.Annotations;

namespace BuildXL.FrontEnd.Workspaces.Core
{
    /// <summary>
    /// Workspace that provides semantic information to the user.
    /// </summary>
    internal sealed class SemanticWorkspace : Workspace
    {
        private readonly ISemanticModel m_semanticModel;

        public SemanticWorkspace(
            IWorkspaceProvider workspaceProvider,
            WorkspaceConfiguration workspaceConfiguration,
            IEnumerable<ParsedModule> modules,
            [CanBeNull] ParsedModule preludeModule,
            [CanBeNull] ParsedModule configurationModule,
            ISemanticModel semanticModel,
            IReadOnlyCollection<Failure> failures)
            : base(workspaceProvider, workspaceConfiguration, modules, failures, preludeModule, configurationModule)
        {
            Contract.Requires(semanticModel != null);

            m_semanticModel = semanticModel;
        }

        /// <inheritdoc/>
        public override ISemanticModel GetSemanticModel()
        {
            return m_semanticModel;
        }

        /// <inheritdoc/>
        public override Workspace WithExtraFailures(IEnumerable<Failure> failures)
        {
            Contract.Requires(failures != null);
            return new SemanticWorkspace(WorkspaceProvider, WorkspaceConfiguration, SpecModules, PreludeModule, ConfigurationModule, m_semanticModel, Failures.Union(failures).ToList());
        }
    }
}
