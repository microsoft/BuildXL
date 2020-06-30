// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.FrontEnd.JavaScript.ProjectGraph;
using BuildXL.FrontEnd.Script;
using BuildXL.FrontEnd.Script.Declarations;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Sdk.Evaluation;
using BuildXL.FrontEnd.Sdk.Workspaces;
using BuildXL.FrontEnd.Utilities;
using BuildXL.FrontEnd.Utilities.GenericProjectGraphResolver;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Pips;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.JavaScript
{
    /// <summary>
    /// Resolver for JavaScript based builds.
    /// </summary>
    public class JavaScriptResolver<TGraphConfiguration, TResolverSettings> : IResolver 
        where TGraphConfiguration : class
        where TResolverSettings : class, IJavaScriptResolverSettings
    {
        private readonly string m_frontEndName;
        
        /// <nodoc/>
        protected readonly FrontEndContext Context;
        
        private readonly FrontEndHost m_host;

        private JavaScriptWorkspaceResolver<TGraphConfiguration, TResolverSettings> m_javaScriptWorkspaceResolver;
        private TResolverSettings m_resolverSettings;

        private ModuleDefinition ModuleDefinition => m_javaScriptWorkspaceResolver.ComputedProjectGraph.Result.ModuleDefinition;
        
        private IReadOnlyCollection<ResolvedJavaScriptExport> Exports => m_javaScriptWorkspaceResolver.ComputedProjectGraph.Result.Exports;

        private readonly ConcurrentDictionary<JavaScriptProject, List<Pips.Operations.Process>> m_scheduledProcesses = new ConcurrentDictionary<JavaScriptProject, List<Pips.Operations.Process>>();

        private readonly SemaphoreSlim m_evaluationSemaphore = new SemaphoreSlim(1, 1);
        private bool? m_evaluationResult = null;

        /// <nodoc />
        public string Name { get; private set; }

        /// <nodoc/>
        public JavaScriptResolver(
            FrontEndHost host,
            FrontEndContext context,
            string frontEndName)
        {
            Contract.Requires(!string.IsNullOrEmpty(frontEndName));

            m_frontEndName = frontEndName;
            Context = context;
            m_host = host;
        }

        /// <inheritdoc/>
        public Task<bool> InitResolverAsync(IResolverSettings resolverSettings, object workspaceResolver)
        {
            Name = resolverSettings.Name;
            m_resolverSettings = resolverSettings as TResolverSettings;
            Contract.Assert(
                m_resolverSettings != null,
                I($"Wrong type for resolver settings, expected {nameof(TResolverSettings)} but got {nameof(resolverSettings.GetType)}"));

            m_javaScriptWorkspaceResolver = workspaceResolver as JavaScriptWorkspaceResolver<TGraphConfiguration, TResolverSettings>;
            if (m_javaScriptWorkspaceResolver == null)
            {
                Contract.Assert(false, I($"Wrong type for resolver, expected {nameof(JavaScriptWorkspaceResolver<TGraphConfiguration, TResolverSettings>)} but got {nameof(workspaceResolver.GetType)}"));
            }

            if (!ValidateResolverSettings(m_resolverSettings))
            {
                return Task.FromResult(false);
            }

            return Task.FromResult(true);
        }

        /// <nodoc/>
        protected virtual bool ValidateResolverSettings(TResolverSettings resolverSettings)
        {
            var pathToFile = resolverSettings.File.ToString(Context.PathTable);

            if (!resolverSettings.Root.IsValid)
            {
                Tracing.Logger.Log.InvalidResolverSettings(Context.LoggingContext, Location.FromFile(pathToFile), "The root must be specified.");
                return false;
            }

            if (string.IsNullOrEmpty(resolverSettings.ModuleName))
            {
                Tracing.Logger.Log.InvalidResolverSettings(Context.LoggingContext, Location.FromFile(pathToFile), "The module name must not be empty.");
                return false;
            }

            if (resolverSettings.CustomCommands != null)
            {
                var commandNames = new HashSet<string>();
                foreach (var customCommand in resolverSettings.CustomCommands)
                {
                    if (string.IsNullOrEmpty(customCommand.Command))
                    {
                        Tracing.Logger.Log.InvalidResolverSettings(Context.LoggingContext, Location.FromFile(pathToFile), "A non-empty custom command name must be defined.");
                        return false;
                    }

                    if (!commandNames.Add(customCommand.Command))
                    {
                        Tracing.Logger.Log.InvalidResolverSettings(Context.LoggingContext, Location.FromFile(pathToFile), $"Duplicated custom command name '{customCommand.Command}'.");
                        return false;
                    }

                    if (customCommand.ExtraArguments == null)
                    {
                        Tracing.Logger.Log.InvalidResolverSettings(Context.LoggingContext, Location.FromFile(pathToFile), $"Extra arguments for custom command '{customCommand.Command}' must be defined.");
                        return false;
                    }
                }
            }

            if (resolverSettings.Exports != null)
            {
                var symbolNames = new HashSet<FullSymbol>();
                foreach (var javaScriptExport in resolverSettings.Exports)
                {
                    if (!javaScriptExport.SymbolName.IsValid)
                    {
                        Tracing.Logger.Log.InvalidResolverSettings(Context.LoggingContext, Location.FromFile(pathToFile), $"Symbol name is undefined.");
                        return false;
                    }

                    if (!symbolNames.Add(javaScriptExport.SymbolName))
                    {
                        Tracing.Logger.Log.InvalidResolverSettings(Context.LoggingContext, Location.FromFile(pathToFile), $"Duplicate symbol name '{javaScriptExport.SymbolName.ToString(Context.SymbolTable)}'.");
                        return false;
                    }

                    // Each specified project must be non-empty
                    foreach (var project in javaScriptExport.Content)
                    {
                        object projectValue = project.GetValue();

                        string packageName = projectValue is string ? (string)projectValue : ((IJavaScriptProjectOutputs)projectValue).PackageName;
                        if (string.IsNullOrEmpty(packageName))
                        {
                            Tracing.Logger.Log.InvalidResolverSettings(Context.LoggingContext, Location.FromFile(pathToFile), "Package name must be defined.");
                            return false;
                        }

                        if (projectValue is IJavaScriptProjectOutputs javaScriptProjectCommand)
                        { 
                            if (javaScriptProjectCommand.Commands == null)
                            {
                                Tracing.Logger.Log.InvalidResolverSettings(Context.LoggingContext, Location.FromFile(pathToFile), $"Commands for JavaScript export '{javaScriptExport.SymbolName.ToString(Context.SymbolTable)}' must be defined.");
                                return false;
                            }

                            foreach (var command in javaScriptProjectCommand.Commands)
                            {
                                if (string.IsNullOrEmpty(command))
                                {
                                    Tracing.Logger.Log.InvalidResolverSettings(Context.LoggingContext, Location.FromFile(pathToFile), $"Command name for JavaScript export '{javaScriptExport.SymbolName.ToString(Context.SymbolTable)}' must be defined.");
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
                    m_javaScriptWorkspaceResolver.ExportsFile,
                    moduleRegistry,
                    FrontEndUtilities.CreatePackage(module.Definition, Context.StringTable),
                    module.Specs[m_javaScriptWorkspaceResolver.ExportsFile].LineMap);

            // For each symbol defined in the resolver settings for exports, add all specified project outputs
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
                    m_javaScriptWorkspaceResolver.ExportsFile,
                    new Declaration[] {}),
                exportsFileModule,
                Context.QualifierTable.EmptyQualifierSpaceId);

            moduleRegistry.AddUninstantiatedModuleInfo(moduleInfo);

            return Task.FromResult<bool?>(true);
        }

        private async Task<EvaluationResult> CollectProjectOutputsAsync(IReadOnlySet<AbsolutePath> evaluationGoals, QualifierId qualifierId, ResolvedJavaScriptExport export)
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
                        Context.LoggingContext, 
                        m_resolverSettings.Location(Context.PathTable), 
                        export.FullSymbol.ToString(Context.SymbolTable), 
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

            return new EvaluationResult(new EvaluatedArrayLiteral(sealedDirectories, default, m_javaScriptWorkspaceResolver.ExportsFile));
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

        /// <summary>
        /// Creates a basic <see cref="JavaScriptPipConstructor"/>
        /// </summary>
        /// <remarks>
        /// Allows extenders to construct a more specialized pip graph constructor
        /// </remarks>
        protected virtual IProjectToPipConstructor<JavaScriptProject> CreateGraphToPipGraphConstructor(
            FrontEndHost host, 
            ModuleDefinition moduleDefinition,
            TResolverSettings resolverSettings,
            TGraphConfiguration graphConfiguration,
            IEnumerable<KeyValuePair<string, string>> userDefinedEnvironment,
            IEnumerable<string> userDefinedPassthroughVariables,
            IReadOnlyDictionary<string, IReadOnlyList<JavaScriptArgument>> customCommands)
        {
            return new JavaScriptPipConstructor(
                Context,
                host,
                moduleDefinition,
                resolverSettings,
                userDefinedEnvironment,
                userDefinedPassthroughVariables,
                customCommands);
        }

        private async Task<bool> EvaluateAllFilesAsync(IReadOnlySet<AbsolutePath> evaluationGoals, QualifierId qualifierId)
        {
            // TODO: consider revisiting this and keeping track of individually evaluated projects, so partial
            // evaluation is possible
            Contract.Assert(m_javaScriptWorkspaceResolver.ComputedProjectGraph.Succeeded);

            JavaScriptGraphResult<TGraphConfiguration> result = m_javaScriptWorkspaceResolver.ComputedProjectGraph.Result;

            IReadOnlySet<JavaScriptProject> filteredBuildFiles = result.JavaScriptGraph.Projects
                            .Where(project => evaluationGoals.Contains(project.PackageJsonFile(Context.PathTable)))
                            .ToReadOnlySet();
            
            // Massage custom commands defined in the resolver settings for easier access
            // We know each custom command is unique since it was validated in ValidateResolverSettings
            IReadOnlyDictionary<string, IReadOnlyList<JavaScriptArgument>> customCommands = m_resolverSettings.CustomCommands?.ToDictionary(
                customCommand => customCommand.Command,
                // Extra arguments support both a list of arguments or a single one. Expose both cases as lists
                // to simplify consumption
                customCommand => customCommand.ExtraArguments.GetValue() is JavaScriptArgument value ?
                    new[] { value } :
                    (IReadOnlyList<JavaScriptArgument>)customCommand.ExtraArguments.GetValue());

            customCommands ??= CollectionUtilities.EmptyDictionary<string, IReadOnlyList<JavaScriptArgument>>();

            var pipConstructor = CreateGraphToPipGraphConstructor(
                m_host, 
                ModuleDefinition, 
                m_resolverSettings, 
                result.JavaScriptGraph.Configuration,
                m_javaScriptWorkspaceResolver.UserDefinedEnvironment, 
                m_javaScriptWorkspaceResolver.UserDefinedPassthroughVariables, 
                customCommands);

            var graphConstructor = new ProjectGraphToPipGraphConstructor<JavaScriptProject>(pipConstructor, m_host.Configuration.FrontEnd.MaxFrontEndConcurrency());

            var scheduleResult = await graphConstructor.TrySchedulePipsForFilesAsync(filteredBuildFiles, qualifierId);

            if (!scheduleResult.Succeeded)
            {
                Tracing.Logger.Log.ProjectGraphConstructionError(Context.LoggingContext, default(Location), scheduleResult.Failure.Describe());
            }
            else
            {
                // On success, store the association between a JavaScript project and its corresponding
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
