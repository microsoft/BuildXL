// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.FrontEnd.Ninja;
using BuildXL.FrontEnd.Ninja.Serialization;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Sdk.Evaluation;
using BuildXL.FrontEnd.Sdk.Workspaces;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;

namespace BuildXL.FrontEnd.CMake
{
    /// <summary>
    /// Resolver for CMake based builds. This resolver generates Ninja specs from a CMake workspace
    /// and uses a Ninja resolver for the rest of the work.
    /// </summary>
    public class CMakeResolver : IResolver
    {
        private readonly string m_frontEndName;
        private readonly FrontEndContext m_context;
        private readonly FrontEndHost m_host;

        private CMakeWorkspaceResolver m_cMakeWorkspaceResolver;
        private ICMakeResolverSettings m_cMakeResolverSettings;

        // TODO: Multiple modules
        private ModuleDefinition ModuleDef => m_cMakeWorkspaceResolver.EmbeddedNinjaWorkspaceResolver.ComputedGraph.Result.ModuleDefinition;

        /// <nodoc />
        public string Name { get; private set; }

        /// <nodoc/>
        public CMakeResolver(
            FrontEndHost host,
            FrontEndContext context,
            string frontEndName)
        {
            Contract.Requires(!string.IsNullOrEmpty(frontEndName));
            m_frontEndName = frontEndName;
            m_host = host;
            m_context = context;
        }

        /// <inheritdoc/>
        public Task<bool> InitResolverAsync(IResolverSettings resolverSettings, object workspaceResolver)
        {
            Contract.Requires(resolverSettings != null);
            Name = resolverSettings.Name;

            m_cMakeResolverSettings = resolverSettings as ICMakeResolverSettings; 
            m_cMakeWorkspaceResolver = workspaceResolver as CMakeWorkspaceResolver;
            
            // TODO: Failure cases, logging
            return Task.FromResult<bool>(true);
        }


        /// <inheritdoc/>
        public void LogStatistics()
        {
        }

        /// <inheritdoc/>
        public Task<bool?> TryConvertModuleToEvaluationAsync(IModuleRegistry moduleRegistry, ParsedModule module, IWorkspace workspace)
        {
            // No conversion needed.
            return Task.FromResult<bool?>(true);
        }

        /// <inheritdoc/>
        public async Task<bool?> TryEvaluateModuleAsync(IEvaluationScheduler scheduler, ModuleDefinition iModule, QualifierId qualifierId)
        {
            if (!iModule.Equals(ModuleDef))
            {
                return null;
            }

            return await Task.FromResult(TryEvaluate(ModuleDef, qualifierId));
        }

        private bool? TryEvaluate(ModuleDefinition module, QualifierId qualifierId)    // TODO: Async?
        {
            NinjaGraphWithModuleDefinition result = m_cMakeWorkspaceResolver.ComputedGraph.Result;
            IReadOnlyCollection<NinjaNode> filteredNodes = result.Graph.Nodes;
            var graphConstructor = new NinjaPipGraphBuilder(m_context, m_host, ModuleDef, 
                m_cMakeWorkspaceResolver.EmbeddedNinjaWorkspaceResolver.ProjectRoot, 
                m_cMakeWorkspaceResolver.EmbeddedNinjaWorkspaceResolver.SpecFile, 
                qualifierId, 
                m_frontEndName, 
                m_cMakeResolverSettings.RemoveAllDebugFlags ?? false,
                m_cMakeResolverSettings.UntrackingSettings);
            return graphConstructor.TrySchedulePips(filteredNodes, qualifierId);
        }

        /// <inheritdoc/>
        public void NotifyEvaluationFinished()
        {
            // Nothing to do.
        }
       
    }
}
