// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.FrontEnd.Rush.ProjectGraph;
using BuildXL.FrontEnd.Script;
using BuildXL.FrontEnd.Script.Declarations;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Sdk.Evaluation;
using BuildXL.FrontEnd.Sdk.Workspaces;
using BuildXL.FrontEnd.Utilities;
using BuildXL.FrontEnd.Utilities.GenericProjectGraphResolver;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Native.IO;
using BuildXL.Pips;
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
        
        private IReadOnlyCollection<ResolvedRushExport> Exports => m_rushWorkspaceResolver.ComputedProjectGraph.Result.Exports;

        private readonly ConcurrentDictionary<RushProject, List<Pips.Operations.Process>> m_scheduledProcesses = new ConcurrentDictionary<RushProject, List<Pips.Operations.Process>>();

        private readonly SemaphoreSlim m_evaluationSemaphore = new SemaphoreSlim(1, 1);
        private bool? m_evaluationResult = null;

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

            if (rushResolverSettings.Exports != null)
            {
                var symbolNames = new HashSet<FullSymbol>();
                foreach (var rushExport in rushResolverSettings.Exports)
                {
                    if (!rushExport.SymbolName.IsValid)
                    {
                        Tracing.Logger.Log.InvalidResolverSettings(m_context.LoggingContext, Location.FromFile(pathToFile), $"Symbol name is undefined.");
                        return false;
                    }

                    if (!symbolNames.Add(rushExport.SymbolName))
                    {
                        Tracing.Logger.Log.InvalidResolverSettings(m_context.LoggingContext, Location.FromFile(pathToFile), $"Duplicate symbol name '{rushExport.SymbolName.ToString(m_context.SymbolTable)}'.");
                        return false;
                    }

                    // Each specified project must be non-empty
                    foreach (var project in rushExport.Content)
                    {
                        object projectValue = project.GetValue();

                        string packageName = projectValue is string ? (string)projectValue : ((IRushProjectOutputs)projectValue).PackageName;
                        if (string.IsNullOrEmpty(packageName))
                        {
                            Tracing.Logger.Log.InvalidResolverSettings(m_context.LoggingContext, Location.FromFile(pathToFile), "Package name must be defined.");
                            return false;
                        }

                        if (projectValue is IRushProjectOutputs rushProjectCommand)
                        { 
                            if (rushProjectCommand.Commands == null)
                            {
                                Tracing.Logger.Log.InvalidResolverSettings(m_context.LoggingContext, Location.FromFile(pathToFile), $"Commands for Rush export '{rushExport.SymbolName.ToString(m_context.SymbolTable)}' must be defined.");
                                return false;
                            }

                            foreach (var command in rushProjectCommand.Commands)
                            {
                                if (string.IsNullOrEmpty(command))
                                {
                                    Tracing.Logger.Log.InvalidResolverSettings(m_context.LoggingContext, Location.FromFile(pathToFile), $"Command name for Rush export '{rushExport.SymbolName.ToString(m_context.SymbolTable)}' must be defined.");
                                    return false;
                                }
                            }
                        }
                    }
                }

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
            
            var exportsFileModule = ModuleLiteral.CreateFileModule(
                    m_rushWorkspaceResolver.ExportsFile,
                    moduleRegistry,
                    FrontEndUtilities.CreatePackage(module.Definition, m_context.StringTable),
                    module.Specs[m_rushWorkspaceResolver.ExportsFile].LineMap);

            // For each symbol defined in the rush resolver settings for exports, add all specified project outputs
            int pos = 1;
            foreach(var export in Exports)
            {
                FrontEndUtilities.AddEvaluationCallbackToFileModule(
                    exportsFileModule,
                    (context, moduleLiteral, evaluationStackFrame) => 
                        CollectProjectOutputsAsync(module.Definition.Specs, moduleLiteral.Qualifier.QualifierId, export),
                    export.FullSymbol,
                    pos);
                
                pos += 2;
            }

            var moduleInfo = new UninstantiatedModuleInfo(
                // We can register an empty one since we have the module populated properly
                new SourceFile(
                    m_rushWorkspaceResolver.ExportsFile,
                    new Declaration[] {}),
                exportsFileModule,
                m_context.QualifierTable.EmptyQualifierSpaceId);

            moduleRegistry.AddUninstantiatedModuleInfo(moduleInfo);

            return Task.FromResult<bool?>(true);
        }

        private async Task<EvaluationResult> CollectProjectOutputsAsync(IReadOnlySet<AbsolutePath> evaluationGoals, QualifierId qualifierId, ResolvedRushExport export)
        {
            // Make sure all project files are evaluated before collecting their outputs
            if (!await EvaluateAllFilesOnceAsync(evaluationGoals, qualifierId))
            {
                return EvaluationResult.Error;
            }

            var processes = export.ExportedProjects.SelectMany(project => {
                if (!m_scheduledProcesses.TryGetValue(project, out var projectProcesses))
                {
                    // The requested project was not scheduled. This can happen when a filter gets applied, so even
                    // though the export points to a valid project (which is already validated), the project is not part
                    // of the graph. In this case just log an informational message.
                    Tracing.Logger.Log.RequestedExportIsNotPresent(
                        m_context.LoggingContext, 
                        m_rushResolverSettings.Location(m_context.PathTable), 
                        export.FullSymbol.ToString(m_context.SymbolTable), 
                        project.Name, 
                        project.ScriptCommandName);
                }

                return projectProcesses;
            });

            // Let's put together all output directories for all pips under this project
            var sealedDirectories = processes.SelectMany(process => process.DirectoryOutputs.Select(directoryArtifact => {
                bool success = m_host.PipGraph.TryGetSealDirectoryKind(directoryArtifact, out var kind);
                Contract.Assert(success);

                return new EvaluationResult(new StaticDirectory(
                    directoryArtifact,
                    kind,
                    CollectionUtilities.EmptySortedReadOnlyArray<FileArtifact, OrdinalPathOnlyFileArtifactComparer>(OrdinalPathOnlyFileArtifactComparer.Instance)));
            })).ToArray();

            return new EvaluationResult(new EvaluatedArrayLiteral(sealedDirectories, default, m_rushWorkspaceResolver.ExportsFile));
        }

        /// <inheritdoc/>
        public async Task<bool?> TryEvaluateModuleAsync(IEvaluationScheduler scheduler, ModuleDefinition iModule, QualifierId qualifierId)
        {
            if (!iModule.Equals(ModuleDefinition))
            {
                return null;
            }

            var module = (ModuleDefinition)iModule;

            return await EvaluateAllFilesOnceAsync(module.Specs, qualifierId);
        }

        /// <inheritdoc/>
        public void NotifyEvaluationFinished()
        {
            m_evaluationSemaphore.Dispose();
        }

        private async Task<bool> EvaluateAllFilesOnceAsync(IReadOnlySet<AbsolutePath> evaluationGoals, QualifierId qualifierId)
        {
            if (m_evaluationResult.HasValue)
            {
                return m_evaluationResult.Value;
            }

            // First time we hit this we should be able to acquire the semaphore (since the initial count is 1)
            // It protects evaluation from other potential threads trying to evaluate
            // Once evaluation is done, the semaphore gets released, and since m_evaluationResult is assigned already
            // we shouldn't wait on this anymore
            await m_evaluationSemaphore.WaitAsync();

            try
            {
                if (m_evaluationResult.HasValue)
                {
                    return m_evaluationResult.Value;
                }

                m_evaluationResult = await EvaluateAllFilesAsync(evaluationGoals, qualifierId);

                return m_evaluationResult.Value;
            }
            finally 
            {
                m_evaluationSemaphore.Release();
            }
        }

        private async Task<bool> EvaluateAllFilesAsync(IReadOnlySet<AbsolutePath> evaluationGoals, QualifierId qualifierId)
        {
            // TODO: consider revisiting this and keeping track of individually evaluated projects, so partial
            // evaluation is possible
            Contract.Assert(m_rushWorkspaceResolver.ComputedProjectGraph.Succeeded);

            RushGraphResult result = m_rushWorkspaceResolver.ComputedProjectGraph.Result;

            IReadOnlySet<RushProject> filteredBuildFiles = result.RushGraph.Projects
                            .Where(project => evaluationGoals.Contains(project.PackageJsonFile(m_context.PathTable)))
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
                result.RushGraph.Configuration,
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
            else
            {
                // On success, store the association between a rush project and its corresponding
                // scheduled processes
                foreach (var kvp in scheduleResult.Result.ScheduledProcesses)
                {
                    m_scheduledProcesses.AddOrUpdate(kvp.Key,
                        _ => new List<Pips.Operations.Process>() { kvp.Value },
                        (key, processes) => { processes.Add(kvp.Value); return processes; });
                }
            }

            return scheduleResult.Succeeded;
        }
    }
}
