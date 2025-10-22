﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.FrontEnd.MsBuild.Serialization;
using BuildXL.FrontEnd.Script;
using BuildXL.FrontEnd.Script.Declarations;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Sdk.Evaluation;
using BuildXL.FrontEnd.Sdk.Mutable;
using BuildXL.FrontEnd.Sdk.Workspaces;
using BuildXL.FrontEnd.Utilities.GenericProjectGraphResolver;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Pips;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using static BuildXL.Utilities.Core.FormattableStringEx;
using ProjectWithPredictions = BuildXL.FrontEnd.MsBuild.Serialization.ProjectWithPredictions<BuildXL.Utilities.Core.AbsolutePath>;

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
        private readonly ConcurrentDictionary<(QualifierId, AbsolutePath), List<ProcessOutputs>> m_scheduledProcessOutputsByPath = new();
        private readonly ConcurrentDictionary<(ModuleDefinition, QualifierId), Lazy<Task<bool>>> m_evaluatedModules = new();

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
                $"Wrong type for resolver settings, expected {nameof(IMsBuildResolverSettings)} but got {nameof(resolverSettings.GetType)}");

            m_msBuildWorkspaceResolver = workspaceResolver as MsBuildWorkspaceResolver;
            Contract.Assert(m_msBuildWorkspaceResolver != null, $"Wrong type for resolver, expected {nameof(MsBuildWorkspaceResolver)} but got {nameof(workspaceResolver.GetType)}");

            return !ValidateResolverSettings(m_msBuildResolverSettings) ? Task.FromResult(false) : Task.FromResult(true);
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

            // Global property keys for MSBuild are case insensitive, but unfortunately we don't have maps with explicit comparers in DScript. 
            // So we need to validate that there are no two property keys that differ only in casing.
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

                Tracing.Logger.Log.InvalidResolverSettings(
                    m_context.LoggingContext,
                    Location.FromFile(pathToFile), 
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
            // This resolver owns only one module.
            if (!module.Definition.Equals(ModuleDefinition))
            {
                return Task.FromResult<bool?>(null);
            }

            // TODO: factor out common logic into utility functions, we are duplicating
            // code that can be found on the download resolver
            var package = CreatePackage(module.Definition);

            foreach (var spec in module.Specs)
            {
                var sourceFilePath = spec.Key;
                var sourceFile = spec.Value;

                var currentFileModule = ModuleLiteral.CreateFileModule(
                    sourceFilePath,
                    moduleRegistry,
                    package,
                    sourceFile.LineMap);
                
                string shortName = m_msBuildWorkspaceResolver.GetIdentifierForProject(sourceFilePath);

                // All shared opaques produced by this project constitute its outputs
                var outputSymbol = FullSymbol.Create(m_context.SymbolTable, shortName);
                var outputResolvedEntry = new ResolvedEntry(
                    outputSymbol,
                    (Context context, ModuleLiteral env, EvaluationStackFrame args) =>
                        GetProjectOutputsAsync(env.CurrentFileModule.Path, env.Qualifier.QualifierId),
                        // The following position is a contract right now with he generated ast in the workspace resolver
                        // we have to find a nicer way to handle and register these.
                        TypeScript.Net.Utilities.LineInfo.FromLineAndPosition(0, 2)
                );

                currentFileModule.AddResolvedEntry(outputSymbol, outputResolvedEntry);
                currentFileModule.AddResolvedEntry(new FilePosition(1, sourceFilePath), outputResolvedEntry);

                var moduleInfo = new UninstantiatedModuleInfo(
                    // We can register an empty one since we have the module populated properly
                    new SourceFile(
                        sourceFilePath,
                        new Declaration[]
                        {
                        }),
                    currentFileModule,
                    m_context.QualifierTable.EmptyQualifierSpaceId);

                moduleRegistry.AddUninstantiatedModuleInfo(moduleInfo);
            }

            return Task.FromResult<bool?>(true);
        }

        private async Task<EvaluationResult> GetProjectOutputsAsync(AbsolutePath path, QualifierId qualifierId)
        {
            // TODO: we don't really need to evaluate all the specs in the module, we could evaluate a
            // partial graph here. 

            // Let's make sure the path and qualifier references a uniquely identified project in the graph. We are creating unique identifiers per project, but there can be multiple project instances with a different combination
            // of global properties (aka qualifier in this context) for the same project path. Let's make sure the specified qualifier matches exactly one project.

            // Construct the global properties for the qualifier. Remove the 'is graph' property since that is everywhere and we want to hide it for the user.
            var referencedGlobalProperties = MsBuildResolverUtils.CreateQualifierAsGlobalProperties(qualifierId, m_context, injectGraphBuildProperty: true);

            // Get all nodes that match the path and the requested qualifier
            var graph = m_msBuildWorkspaceResolver.ComputedProjectGraph.Result;
            var referencedNodes = graph.ProjectGraph.ProjectNodes
                .Where(node => node.FullPath == path)
                .Select(node => node.GlobalProperties)
                // Check that qualifiers 'coerce' in terms of global properties: all keys in the referenced qualifier must be a superset of the project's global properties, and the values must be the same
                .Where(globalProperties => 
                    globalProperties.Keys.All(key => referencedGlobalProperties.ContainsKey(key)) && globalProperties.Keys.All(key => globalProperties[key].Equals(referencedGlobalProperties[key], StringComparison.Ordinal)));

            // The referenced node is contextual based on the current qualifier. If there is no node matching the qualifier, then we cannot produce outputs. If there are multiple nodes matching, the reference is ambiguous and 
            // we cannot proceed either.
            if (!referencedNodes.Any() || referencedNodes.Count() > 1)
            {
                var availableQualifiers = graph.ProjectGraph.ProjectNodes
                    .Where(node => node.FullPath == path)
                    .Select(node => MsBuildResolverUtils.CreateQualifierIdFromGlobalProperties(node.GlobalProperties, m_context, removeGraphBuildProperty: true));

                var availableQualifiersDisplay = string.Join(",", availableQualifiers.Select(availableQualifier => m_context.QualifierTable.GetCanonicalDisplayString(availableQualifier)));

                if (!referencedNodes.Any())
                {
                    Tracing.Logger.Log.CannotFindProjectForQualifier(
                        m_context.LoggingContext,
                        path.ToString(m_context.PathTable),
                        m_context.QualifierTable.GetCanonicalDisplayString(qualifierId),
                        availableQualifiersDisplay);
                }
                else
                {
                    Tracing.Logger.Log.AmbiguousQualifierReferenceForProject(
                        m_context.LoggingContext,
                        path.ToString(m_context.PathTable),
                        m_context.QualifierTable.GetCanonicalDisplayString(qualifierId),
                        availableQualifiersDisplay);
                }

                return EvaluationResult.Error;
            }

            List<ProcessOutputs> processOutputs = null;
            if (!m_scheduledProcessOutputsByPath.TryGetValue((qualifierId, path), out processOutputs))
            {
                var success = await EvaluateAllFilesAsync(ModuleDefinition, qualifierId);
                if (!success)
                {
                    return EvaluationResult.Error;
                }

                processOutputs = m_scheduledProcessOutputsByPath[(qualifierId, path)];
            }

            // Let's put together all output directories for all pips under this project
            var sealedDirectories = processOutputs.SelectMany(process => process.GetOutputDirectories().Select(staticDirectory => new EvaluationResult(staticDirectory))).ToArray();

            return new EvaluationResult(new EvaluatedArrayLiteral(sealedDirectories, default(TypeScript.Net.Utilities.LineInfo), path));
        }

        private Package CreatePackage(ModuleDefinition moduleDefinition)
        {
            var moduleDescriptor = moduleDefinition.Descriptor;

            var packageId = PackageId.Create(StringId.Create(m_context.StringTable, moduleDescriptor.Name));
            var packageDescriptor = new PackageDescriptor
            {
                Name = moduleDescriptor.Name,
                Main = moduleDefinition.MainFile,
                NameResolutionSemantics = NameResolutionSemantics.ImplicitProjectReferences,
                Publisher = null,
                Version = moduleDescriptor.Version,
                Projects = new List<AbsolutePath>(moduleDefinition.Specs),
                ScrubDirectories = new List<AbsolutePath>(moduleDefinition.ScrubDirectories)
            };

            return Package.Create(packageId, moduleDefinition.ModuleConfigFile, packageDescriptor, moduleId: moduleDescriptor.Id);
        }

        /// <inheritdoc/>
        public async Task<bool?> TryEvaluateModuleAsync(IEvaluationScheduler scheduler, ModuleDefinition iModule, QualifierId qualifierId)
        {
            if (!iModule.Equals(ModuleDefinition))
            {
                return null;
            }

            var module = (ModuleDefinition)iModule;
            return await EvaluateAllFilesAsync(module, qualifierId);
        }

        /// <inheritdoc/>
        public void NotifyEvaluationFinished()
        {
            // Nothing to do.
        }

        private Task<bool> EvaluateAllFilesAsync(ModuleDefinition module, QualifierId qualifierId)
        {
            // Dedupe per (module, qualifier) pair, i.e., only happens one at a time, and only happens once.
            return m_evaluatedModules.GetOrAdd(
                (module, qualifierId),
                new Lazy<Task<bool>>(() => EvaluateAllFilesNoDedupeAsync(module, qualifierId), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        }

        private async Task<bool> EvaluateAllFilesNoDedupeAsync(ModuleDefinition module, QualifierId qualifierId)
        {
            // TODO: consider revisiting this and keeping track of individually evaluated projects, so partial
            // evaluation is possible
            Contract.Assert(m_msBuildWorkspaceResolver.ComputedProjectGraph.Succeeded);

            IReadOnlySet<AbsolutePath> evaluationGoals = module.Specs;

            ProjectGraphResult result = m_msBuildWorkspaceResolver.ComputedProjectGraph.Result;

            GlobalProperties qualifier = MsBuildResolverUtils.CreateQualifierAsGlobalProperties(qualifierId, m_context);

            IReadOnlySet<ProjectWithPredictions> filteredBuildFiles = result.ProjectGraph.ProjectNodes
                .Where(project => evaluationGoals.Contains(project.FullPath))
                .Where(project => ProjectMatchesQualifier(project, qualifier))
                .ToReadOnlySet();

            var pipConstructor = new PipConstructor(
                m_context, 
                m_host, 
                result.ModuleDefinition, 
                m_msBuildResolverSettings, 
                result.MsBuildLocation, 
                result.DotNetExeLocation, 
                m_frontEndName, 
                m_msBuildWorkspaceResolver.TrackedEnvironmentVariables, 
                m_msBuildWorkspaceResolver.UserDefinedPassthroughEnvironmentVariables,
                filteredBuildFiles);

            var graphConstructor = new ProjectGraphToPipGraphConstructor<ProjectWithPredictions>(pipConstructor, m_host.Configuration.FrontEnd.MaxFrontEndConcurrency());
            var maybeScheduleResult = await graphConstructor.TrySchedulePipsForFilesAsync(filteredBuildFiles, qualifierId);

            if (maybeScheduleResult.Succeeded)
            {
                foreach (var kvp in maybeScheduleResult.Result.ScheduledProcessOutputs)
                {
                    m_scheduledProcessOutputsByPath.AddOrUpdate((qualifierId, kvp.Key.FullPath),
                        _ => new List<ProcessOutputs>() { kvp.Value },
                        (key, processes) => { processes.Add(kvp.Value); return processes; });
                }
            }
            else
            {
                if (maybeScheduleResult.Failure is CycleInProjectsFailure<ProjectWithPredictions> cycleFailure)
                {
                    var cycleDescription = string.Join(" -> ", cycleFailure.Cycle.Select(project => project.FullPath.ToString(m_context.PathTable)));
                    Tracing.Logger.Log.CycleInBuildTargets(m_context.LoggingContext, cycleDescription);
                }
                // Else, error has already been logged by the pip constructor
            }

            return maybeScheduleResult.Succeeded;
        }

        private static bool ProjectMatchesQualifier(ProjectWithPredictions project, GlobalProperties qualifier)
        {
            return qualifier.All(kvp =>
                    // The project properties should contain all qualifier keys
                    project.GlobalProperties.TryGetValue(kvp.Key, out string value) &&
                    // The values should be the same. Not that values are case sensitive.
                    kvp.Value.Equals(value, StringComparison.Ordinal));
        }
    }
}
