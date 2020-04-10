// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.FrontEnd.Rush.ProjectGraph;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Sdk.Evaluation;
using BuildXL.FrontEnd.Sdk.Workspaces;
using BuildXL.FrontEnd.Utilities.GenericProjectGraphResolver;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.Rush
{
    /// <summary>
    /// Resolver for Rush based builds.
    /// </summary>
    public class RushResolver : IResolver
    {
        private readonly string m_frontEndName;
        private readonly FrontEndContext m_context;
        private readonly FrontEndHost m_host;

        private RushWorkspaceResolver m_rushWorkspaceResolver;
        private IRushResolverSettings m_rushResolverSettings;

        private ModuleDefinition ModuleDefinition => m_rushWorkspaceResolver.ComputedProjectGraph.Result.ModuleDefinition;

        /// <nodoc />
        public string Name { get; private set; }

        /// <nodoc/>
        public RushResolver(
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
            m_rushResolverSettings = resolverSettings as IRushResolverSettings;
            Contract.Assert(
                m_rushResolverSettings != null,
                I($"Wrong type for resolver settings, expected {nameof(IRushResolverSettings)} but got {nameof(resolverSettings.GetType)}"));

            m_rushWorkspaceResolver = workspaceResolver as RushWorkspaceResolver;
            if (m_rushWorkspaceResolver == null)
            {
                Contract.Assert(false, I($"Wrong type for resolver, expected {nameof(RushWorkspaceResolver)} but got {nameof(workspaceResolver.GetType)}"));
            }

            if (!ValidateResolverSettings(m_rushResolverSettings))
            {
                return Task.FromResult(false);
            }

            return Task.FromResult(true);
        }

        private bool ValidateResolverSettings(IRushResolverSettings rushResolverSettings)
        {
            var pathToFile = rushResolverSettings.File.ToString(m_context.PathTable);

            if (!rushResolverSettings.Root.IsValid)
            {
                Tracing.Logger.Log.InvalidResolverSettings(m_context.LoggingContext, Location.FromFile(pathToFile), "The root must be specified.");
                return false;
            }

            string rushJson = rushResolverSettings.Root.Combine(m_context.PathTable, "rush.json").ToString(m_context.PathTable);
            if (!FileUtilities.Exists(rushJson))
            {
                Tracing.Logger.Log.InvalidResolverSettings(m_context.LoggingContext, Location.FromFile(pathToFile), 
                    $"Rush configuration file 'rush.json' was not found under the specified root '{rushJson}'.");
                return false;
            }

            if (string.IsNullOrEmpty(rushResolverSettings.ModuleName))
            {
                Tracing.Logger.Log.InvalidResolverSettings(m_context.LoggingContext, Location.FromFile(pathToFile), "The module name must not be empty.");
                return false;
            }

            if (rushResolverSettings.CustomCommands != null)
            {
                var commandNames = new HashSet<string>();
                foreach (var customCommand in rushResolverSettings.CustomCommands)
                {
                    if (string.IsNullOrEmpty(customCommand.Command))
                    {
                        Tracing.Logger.Log.InvalidResolverSettings(m_context.LoggingContext, Location.FromFile(pathToFile), "A non-empty custom command name must be defined.");
                        return false;
                    }

                    if (!commandNames.Add(customCommand.Command))
                    {
                        Tracing.Logger.Log.InvalidResolverSettings(m_context.LoggingContext, Location.FromFile(pathToFile), $"Duplicated custom command name '{customCommand.Command}'.");
                        return false;
                    }

                    if (customCommand.ExtraArguments == null)
                    {
                        Tracing.Logger.Log.InvalidResolverSettings(m_context.LoggingContext, Location.FromFile(pathToFile), $"Extra arguments for custom command '{customCommand.Command}' must be defined.");
                        return false;
                    }
                }
            }

            // If the rush-lib base location is specified, it has to be valid
            if (rushResolverSettings.RushLibBaseLocation?.IsValid == false)
            {
                Tracing.Logger.Log.InvalidResolverSettings(m_context.LoggingContext, Location.FromFile(pathToFile), "The specified rush-lib base location is invalid.");
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
            // This resolver owns only one module.
            if (!module.Definition.Equals(ModuleDefinition))
            {
                return Task.FromResult<bool?>(null);
            }

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

        private async Task<bool> EvaluateAllFilesAsync(IReadOnlySet<AbsolutePath> evaluationGoals, QualifierId qualifierId)
        {
            // TODO: consider revisiting this and keeping track of individually evaluated projects, so partial
            // evaluation is possible
            Contract.Assert(m_rushWorkspaceResolver.ComputedProjectGraph.Succeeded);

            RushGraphResult result = m_rushWorkspaceResolver.ComputedProjectGraph.Result;

            // TODO add support for qualifiers
            IReadOnlySet<RushProject> filteredBuildFiles = result.RushGraph.Projects
                            .Where(project => evaluationGoals.Contains(project.ProjectPath(m_context.PathTable)))
                            .ToReadOnlySet();

            // Massage custom commands for easier access
            // We know each custom command is unique since it was validated in ValidateResolverSettings
            IReadOnlyDictionary<string, IReadOnlyList<RushArgument>> customCommands = m_rushResolverSettings.CustomCommands?.ToDictionary(
                customCommand => customCommand.Command, 
                // Extra arguments support both a list of arguments or a single one. Expose both cases as lists
                // to simplify consumption
                customCommand => customCommand.ExtraArguments.GetValue() is RushArgument value ? 
                    new[] { value } : 
                    (IReadOnlyList<RushArgument>)customCommand.ExtraArguments.GetValue());

            customCommands ??= CollectionUtilities.EmptyDictionary<string, IReadOnlyList<RushArgument>>();

            var pipConstructor = new RushPipConstructor(m_context,
                m_host,
                result.ModuleDefinition,
                m_rushResolverSettings,
                m_rushWorkspaceResolver.UserDefinedEnvironment,
                m_rushWorkspaceResolver.UserDefinedPassthroughVariables,
                customCommands);

            var graphConstructor = new ProjectGraphToPipGraphConstructor<RushProject>(pipConstructor, m_host.Configuration.FrontEnd.MaxFrontEndConcurrency());

            var scheduleResult = await graphConstructor.TrySchedulePipsForFilesAsync(filteredBuildFiles, qualifierId);

            if (!scheduleResult.Succeeded)
            {
                Tracing.Logger.Log.ProjectGraphConstructionError(m_context.LoggingContext, default(Location), scheduleResult.Failure.Describe());
            }

            return scheduleResult.Succeeded;
        }
    }
}
