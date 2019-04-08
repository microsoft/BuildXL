// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Utilities;
using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Workspaces.Core
{
    /// <summary>
    /// Decorates <see cref="IWorkspaceProvider"/> with writes to <see cref="IWorkspaceStatistics"/>.
    /// </summary>
    internal sealed class WorkspaceProviderStatisticsDecorator : IWorkspaceProvider
    {
        private readonly IWorkspaceStatistics m_statistics;
        private readonly IWorkspaceProvider m_decoratee;

        /// <inheritdoc />
        public WorkspaceConfiguration Configuration => m_decoratee.Configuration;

        /// <nodoc />
        public WorkspaceProviderStatisticsDecorator(IWorkspaceStatistics statistics, IWorkspaceProvider decoratee)
        {
            m_statistics = statistics;
            m_decoratee = decoratee;
        }

        /// <inheritdoc />
        public async Task<Workspace> CreateWorkspaceFromSpecAsync(AbsolutePath pathToSpec)
        {
            using (m_statistics.EndToEndParsing.Start())
            {
                return await m_decoratee.CreateWorkspaceFromSpecAsync(pathToSpec);
            }
        }

        /// <inheritdoc />
        public async Task<Workspace> CreateWorkspaceFromModuleAsync(ModuleDescriptor moduleDescriptor)
        {
            using (m_statistics.EndToEndParsing.Start())
            {
                return await m_decoratee.CreateWorkspaceFromModuleAsync(moduleDescriptor);
            }
        }

        /// <inheritdoc />
        public async Task<Workspace> CreateWorkspaceFromAllKnownModulesAsync()
        {
            using (m_statistics.EndToEndParsing.Start())
            {
                return await m_decoratee.CreateWorkspaceFromAllKnownModulesAsync();
            }
        }

        /// <inheritdoc />
        public async Task<Workspace> CreateIncrementalWorkspaceForAllKnownModulesAsync(
            IEnumerable<ParsedModule> parsedModules,
            ModuleUnderConstruction moduleUnderConstruction,
            IEnumerable<Failure> failures,
            ParsedModule preludeModule)
        {
            using (m_statistics.EndToEndParsing.Start())
            {
                return await m_decoratee.CreateIncrementalWorkspaceForAllKnownModulesAsync(parsedModules, moduleUnderConstruction, failures, preludeModule);
            }
        }

        /// <inheritdoc />
        public Task<Possible<WorkspaceDefinition>> GetWorkspaceDefinitionForAllResolversAsync()
        {
            return m_decoratee.GetWorkspaceDefinitionForAllResolversAsync();
        }

        /// <inheritdoc />
        public Task<Possible<WorkspaceDefinition>> RecomputeWorkspaceDefinitionForAllResolversAsync()
        {
            return m_decoratee.RecomputeWorkspaceDefinitionForAllResolversAsync();
        }

        /// <inheritdoc />
        public async Task<Workspace> CreateWorkspaceAsync(WorkspaceDefinition workspaceDefinition, bool userFilterWasApplied)
        {
            using (m_statistics.EndToEndParsing.Start())
            {
                return await m_decoratee.CreateWorkspaceAsync(workspaceDefinition, userFilterWasApplied);
            }
        }

        /// <inheritdoc />
        public Task<Possible<ISourceFile>[]> ParseAndBindSpecsAsync(SpecWithOwningModule[] specs)
        {
            return m_decoratee.ParseAndBindSpecsAsync(specs);
        }

        /// <inheritdoc />
        public ParsedModule GetConfigurationModule()
        {
            return m_decoratee.GetConfigurationModule();
        }
    }
}
