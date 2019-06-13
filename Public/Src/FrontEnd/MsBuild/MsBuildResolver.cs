// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.FrontEnd.MsBuild.Serialization;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Sdk.Evaluation;
using BuildXL.FrontEnd.Sdk.Workspaces;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using static BuildXL.Utilities.FormattableStringEx;
using ProjectWithPredictions = BuildXL.FrontEnd.MsBuild.Serialization.ProjectWithPredictions<BuildXL.Utilities.AbsolutePath>;

namespace BuildXL.FrontEnd.MsBuild
{
    /// <summary>
    /// Resolver for static graph MsBuild based builds.
    /// </summary>
    public class MsBuildResolver : IResolver
    {
        private readonly string m_frontEndName;
        private readonly FrontEndContext m_context;
        private readonly FrontEndHost m_host;

        private MsBuildWorkspaceResolver m_msBuildWorkspaceResolver;
        private IMsBuildResolverSettings m_msBuildResolverSettings;

        private ModuleDefinition ModuleDefinition => m_msBuildWorkspaceResolver.ComputedProjectGraph.Result.ModuleDefinition;

        /// <nodoc />
        public string Name { get; private set; }

        /// <nodoc/>
        public MsBuildResolver(
            FrontEndHost host,
            FrontEndContext context,
            string frontEndName)
        {
            Contract.Requires(!string.IsNullOrEmpty(frontEndName));

            m_frontEndName = frontEndName;
            m_context = context;
            m_host = host;
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
            var pathToFile = msBuildResolverSettings.File.ToString(m_context.PathTable);

            if (!msBuildResolverSettings.Root.IsValid)
            {
                Tracing.Logger.Log.InvalidResolverSettings(m_context.LoggingContext, Location.FromFile(pathToFile), "The root must be specified.");
                return false;
            }

            if (string.IsNullOrEmpty(msBuildResolverSettings.ModuleName))
            {
                Tracing.Logger.Log.InvalidResolverSettings(m_context.LoggingContext, Location.FromFile(pathToFile), "The module name must not be empty.");
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

                Tracing.Logger.Log.InvalidResolverSettings(m_context.LoggingContext, Location.FromFile(pathToFile), 
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
        public Task<bool?> TryConvertModuleToEvaluationAsync(IModuleRegistry moduleRegistry, ParsedModule module, IWorkspace workspace)
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

            GlobalProperties qualifier = MsBuildResolverUtils.CreateQualifierAsGlobalProperties(qualifierId, m_context);

            IReadOnlySet<ProjectWithPredictions> filteredBuildFiles = result.ProjectGraph.ProjectNodes
                            .Where(project => evaluationGoals.Contains(project.FullPath))
                            .Where(project => ProjectMatchesQualifier(project, qualifier))
                            .ToReadOnlySet();

            var graphConstructor = new PipGraphConstructor(
                m_context, 
                m_host, 
                result.ModuleDefinition, 
                m_msBuildResolverSettings, 
                result.MsBuildExeLocation, 
                m_frontEndName, 
                m_msBuildWorkspaceResolver.UserDefinedEnvironment, 
                m_msBuildWorkspaceResolver.UserDefinedPassthroughVariables);

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
