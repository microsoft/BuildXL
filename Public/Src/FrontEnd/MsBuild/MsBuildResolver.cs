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
using BuildXL.FrontEnd.MsBuild.Serialization;

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

            // Global property keys for MSBuild are case insensitive, but unfortunately we don't have maps with explicit comparers in dscript. 
            // So we need to validate there are not two property keys that only differ in case
            if (msBuildResolverSettings.GlobalProperties != null)
            {
                var globalKeys = msBuildResolverSettings.GlobalProperties.Keys;

                // Store all keys in a case insensitive dictionary. If any of them get collapsed, there are keys that only differ in case
                var caseInsensitiveKeys = new HashSet<string>(globalKeys, StringComparer.OrdinalIgnoreCase);
                if (caseInsensitiveKeys.Count == globalKeys.Count())
                {
                    return true;
                }

                // So there are some keys that only differ in case. Each case insensitive key that is not in the original set of keys is problematic
                var problematicKeys = globalKeys.Except(caseInsensitiveKeys);

                Tracing.Logger.Log.InvalidResolverSettings(Context.LoggingContext, Location.FromFile(pathToFile), 
                    $"Global property key(s) '{string.Join(", ", problematicKeys)}' specified multiple times with casing differences only. MSBuild global property keys are case insensitive.");
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

            GlobalProperties qualifier = MsBuildResolverUtils.CreateQualifierAsGlobalProperties(qualifierId, Context);

            IReadOnlySet<ProjectWithPredictions> filteredBuildFiles = result.ProjectGraph.ProjectNodes
                            .Where(project => evaluationGoals.Contains(project.FullPath))
                            .Where(project => ProjectMatchesQualifier(project, qualifier))
                            .ToReadOnlySet();

            var graphConstructor = new PipGraphConstructor(Context, FrontEndHost, result.ModuleDefinition, m_msBuildResolverSettings, result.MsBuildExeLocation, m_frontEndName);

            return graphConstructor.TrySchedulePipsForFilesAsync(filteredBuildFiles, qualifierId);
        }

        private bool ProjectMatchesQualifier(ProjectWithPredictions project, GlobalProperties qualifier)
        {
            return qualifier.All(kvp =>
                    // The project properties should contain all qualifier keys
                    project.GlobalProperties.TryGetValue(kvp.Key, out string value) &&
                    // The values should be the same. Not that values are case sensitive.
                    kvp.Value.Equals(value, StringComparison.Ordinal));
        }
    }
}
