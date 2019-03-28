// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Utilities.Configuration;
using BuildXL.FrontEnd.Core;
using BuildXL.FrontEnd.Script;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Sdk.Mutable;
using BuildXL.FrontEnd.Sdk.Workspaces;
using ProjectWithPredictions = BuildXL.FrontEnd.MsBuild.Serialization.ProjectWithPredictions<BuildXL.Utilities.AbsolutePath>;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.MsBuild
{
    /// <summary>
    /// Resolver for static graph MsBuild based builds.
    /// </summary>
    public class MsBuildResolver : DScriptInterpreterBase, IResolver
    {
        private readonly string m_frontEndName;
        private MsBuildWorkspaceResolver m_msBuildWorkspaceResolver;
        private IMsBuildResolverSettings m_msBuildResolverSettings;

        private ModuleDefinition ModuleDefinition => m_msBuildWorkspaceResolver.ComputedProjectGraph.Result.ModuleDefinition;

        /// <nodoc/>
        public MsBuildResolver(
            GlobalConstants constants,
            ModuleRegistry sharedModuleRegistry,
            IFrontEndStatistics statistics,
            FrontEndHost host,
            FrontEndContext context,
            IConfiguration configuration,
            Script.Tracing.Logger logger,
            string frontEndName)
            : base(constants, sharedModuleRegistry, statistics, logger, host, context, configuration)
        {
            Contract.Requires(!string.IsNullOrEmpty(frontEndName));

            m_frontEndName = frontEndName;
        }

        /// <inheritdoc/>
        public Task<bool> InitResolverAsync(IResolverSettings resolverSettings, object workspaceResolver)
        {
            Name = resolverSettings.Name;
            m_msBuildResolverSettings = resolverSettings as IMsBuildResolverSettings;
            Contract.Assert(
                m_msBuildResolverSettings != null,
                I($"Wrong type for resolver settings, expected {nameof(IMsBuildResolverSettings)} but got {nameof(resolverSettings.GetType)}"));

            m_msBuildWorkspaceResolver = workspaceResolver as MsBuildWorkspaceResolver;
            Contract.Assert(m_msBuildWorkspaceResolver != null, I($"Wrong type for resolver, expected {nameof(MsBuildWorkspaceResolver)} but got {nameof(workspaceResolver.GetType)}"));

            if (!ValidateResolverSettings(m_msBuildResolverSettings))
            {
                return Task.FromResult(false);
            }

            return Task.FromResult(true);
        }

        private bool ValidateResolverSettings(IMsBuildResolverSettings msBuildResolverSettings)
        {
            var pathToFile = msBuildResolverSettings.File.ToString(Context.PathTable);

            if (!msBuildResolverSettings.Root.IsValid)
            {
                Tracing.Logger.Log.InvalidResolverSettings(Context.LoggingContext, Location.FromFile(pathToFile), "The root must be specified.");
                return false;
            }

            if (string.IsNullOrEmpty(msBuildResolverSettings.ModuleName))
            {
                Tracing.Logger.Log.InvalidResolverSettings(Context.LoggingContext, Location.FromFile(pathToFile), "The module name must not be empty.");
                return false;
            }

            return true;
        }

        /// <inheritdoc/>
        public void LogStatistics()
        {
        }

        /// <inheritdoc/>
        public Task<bool?> TryConvertModuleToEvaluationAsync(ParsedModule module, IWorkspace workspace)
        {
            // No conversion needed.
            return Task.FromResult<bool?>(true);
        }

        /// <inheritdoc/>
        public async Task<bool?> TryEvaluateModuleAsync(IEvaluationScheduler scheduler, ModuleDefinition iModule, QualifierId qualifierId)
        {
            if (!iModule.Equals(ModuleDefinition))
            {
                return null;
            }

            var module = (ModuleDefinition)iModule;
            return await EvaluateAllFilesAsync(module.Specs, qualifierId);
        }

        /// <inheritdoc/>
        public void NotifyEvaluationFinished()
        {
            // Nothing to do.
        }

        private Task<bool> EvaluateAllFilesAsync(IReadOnlySet<AbsolutePath> evaluationGoals, QualifierId qualifierId)
        {
            Contract.Assert(m_msBuildWorkspaceResolver.ComputedProjectGraph.Succeeded);

            ProjectGraphResult result = m_msBuildWorkspaceResolver.ComputedProjectGraph.Result;

            // TODO: Filter out projects with non-matching qualifiers
            IReadOnlySet<ProjectWithPredictions> filteredBuildFiles = result.ProjectGraph.ProjectNodes
                            .Where(project => evaluationGoals.Contains(project.FullPath))
                            .ToReadOnlySet();

            var graphConstructor = new PipGraphConstructor(Context, FrontEndHost, result.ModuleDefinition, m_msBuildResolverSettings, result.MsBuildExeLocation, m_frontEndName);

            return graphConstructor.TrySchedulePipsForFilesAsync(filteredBuildFiles, qualifierId);
        }
    }
}
