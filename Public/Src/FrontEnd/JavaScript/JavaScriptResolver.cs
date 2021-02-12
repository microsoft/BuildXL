// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.FrontEnd.Core;
using BuildXL.FrontEnd.JavaScript.ProjectGraph;
using BuildXL.FrontEnd.Script;
using BuildXL.FrontEnd.Script.Ambients.Transformers;
using BuildXL.FrontEnd.Script.Declarations;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.RuntimeModel.AstBridge;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Sdk.Evaluation;
using BuildXL.FrontEnd.Sdk.Workspaces;
using BuildXL.FrontEnd.Utilities;
using BuildXL.FrontEnd.Utilities.GenericProjectGraphResolver;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Pips;
using BuildXL.Pips.Builders;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using static BuildXL.FrontEnd.Script.Values.Thunk;
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
        private readonly Script.Tracing.Logger m_logger;
        private JavaScriptWorkspaceResolver<TGraphConfiguration, TResolverSettings> m_javaScriptWorkspaceResolver;
        private TResolverSettings m_resolverSettings;

        private ModuleDefinition ModuleDefinition => m_javaScriptWorkspaceResolver.ComputedProjectGraph.Result.ModuleDefinition;
        
        private IReadOnlyCollection<ResolvedJavaScriptExport> Exports => m_javaScriptWorkspaceResolver.ComputedProjectGraph.Result.Exports;

        private readonly ConcurrentDictionary<JavaScriptProject, List<ProcessOutputs>> m_scheduledProcessOutputs = new ConcurrentDictionary<JavaScriptProject, List<ProcessOutputs>>();

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
            m_logger = Script.Tracing.Logger.CreateLogger(preserveLogEvents: true);
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

            if (resolverSettings.CustomScheduling != null)
            {
                if (string.IsNullOrEmpty(resolverSettings.CustomScheduling.Module))
                {
                    Tracing.Logger.Log.InvalidResolverSettings(Context.LoggingContext, Location.FromFile(pathToFile), $"Module name for the custom scheduling entry must be defined.");
                    return false;
                }

                if (string.IsNullOrEmpty(resolverSettings.CustomScheduling.SchedulingFunction))
                {
                    Tracing.Logger.Log.InvalidResolverSettings(Context.LoggingContext, Location.FromFile(pathToFile), $"Custom scheduling function name must be defined.");
                    return false;
                }

                if (FullSymbol.TryCreate(Context.SymbolTable, resolverSettings.CustomScheduling.SchedulingFunction, out _, out _) != FullSymbol.ParseResult.Success)
                {
                    Tracing.Logger.Log.InvalidResolverSettings(Context.LoggingContext, Location.FromFile(pathToFile), $"Custom scheduling function name is not a valid dotted identifier.");
                    return false;
                }
            }

            return true;
        }

        /// <inheritdoc/>
        public void LogStatistics()
        {
        }

        /// <inheritdoc/>
        public async Task<bool?> TryConvertModuleToEvaluationAsync(IModuleRegistry moduleRegistry, ParsedModule module, IWorkspace workspace)
        {
            // This resolver owns only one module.
            if (!module.Definition.Equals(ModuleDefinition))
            {
                return null;
            }

            var package = FrontEndUtilities.CreatePackage(module.Definition, Context.StringTable);

            ConvertExportsFile(moduleRegistry, module, package);
            await ConvertImportsFileAsync(package);

            return true;
        }

        private async Task ConvertImportsFileAsync(Package package)
        {
            if (m_javaScriptWorkspaceResolver.CustomSchedulingCallback != null)
            {
                // The imports file does not need any special callbacks and it is regular DScript. Run the normal AST conversion process on it.
                var conversionResult = await FrontEndUtilities.RunAstConversionAsync(m_host, Context, m_logger, new FrontEndStatistics(), package, m_javaScriptWorkspaceResolver.ImportsFile);
                Contract.Assert(conversionResult.Success);

                var moduleData = new UninstantiatedModuleInfo(
                    conversionResult.SourceFile,
                    conversionResult.Module,
                    conversionResult.QualifierSpaceId.IsValid ? conversionResult.QualifierSpaceId : Context.QualifierTable.EmptyQualifierSpaceId);

                m_host.ModuleRegistry.AddUninstantiatedModuleInfo(moduleData);
            }
        }

        private void ConvertExportsFile(IModuleRegistry moduleRegistry, ParsedModule module, Package package)
        {
            var exportsFileModule = ModuleLiteral.CreateFileModule(
                                m_javaScriptWorkspaceResolver.ExportsFile,
                                moduleRegistry,
                                package,
                                module.Specs[m_javaScriptWorkspaceResolver.ExportsFile].LineMap);

            // For each symbol defined in the resolver settings for exports, add all specified project outputs
            int pos = 1;
            foreach (var export in Exports)
            {
                FrontEndUtilities.AddEvaluationCallbackToFileModule(
                    exportsFileModule,
                    (context, moduleLiteral, evaluationStackFrame) =>
                        CollectProjectOutputsAsync(module.Definition.Specs, moduleLiteral.Qualifier.QualifierId, export, context.EvaluationScheduler),
                    export.FullSymbol,
                    pos);

                pos += 2;
            }

            var moduleInfo = new UninstantiatedModuleInfo(
                // We can register an empty one since we have the module populated properly
                new Script.SourceFile(
                    m_javaScriptWorkspaceResolver.ExportsFile,
                    new Declaration[] { }),
                exportsFileModule,
                Context.QualifierTable.EmptyQualifierSpaceId);

            moduleRegistry.AddUninstantiatedModuleInfo(moduleInfo);
        }

        private async Task<EvaluationResult> CollectProjectOutputsAsync(IReadOnlySet<AbsolutePath> evaluationGoals, QualifierId qualifierId, ResolvedJavaScriptExport export, IEvaluationScheduler scheduler)
        {
            // Make sure all project files are evaluated before collecting their outputs
            if (!await EvaluateAllFilesOnceAsync(evaluationGoals, qualifierId, scheduler))
            {
                return EvaluationResult.Error;
            }

            var processOutputs = export.ExportedProjects.SelectMany(project => {
                if (!m_scheduledProcessOutputs.TryGetValue(project, out var projectOutputs))
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

                return projectOutputs;
            });

            // Let's put together all output directories for all pips under this project
            var sealedDirectories = processOutputs.SelectMany(process => process.GetOutputDirectories().Select(staticDirectory => new EvaluationResult(staticDirectory))).ToArray();

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

            return await EvaluateAllFilesOnceAsync(module.Specs, qualifierId, scheduler);
        }

        /// <inheritdoc/>
        public void NotifyEvaluationFinished()
        {
            m_evaluationSemaphore.Dispose();
            m_javaScriptWorkspaceResolver?.NotifyEvaluationFinished();
        }

        private async Task<bool> EvaluateAllFilesOnceAsync(IReadOnlySet<AbsolutePath> evaluationGoals, QualifierId qualifierId, IEvaluationScheduler scheduler)
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

                m_evaluationResult = await EvaluateAllFilesAsync(evaluationGoals, qualifierId, scheduler);

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
            IReadOnlyDictionary<string, IReadOnlyList<JavaScriptArgument>> customCommands,
            IReadOnlyCollection<JavaScriptProject> allProjectsToBuild)
        {
            return new JavaScriptPipConstructor(
                Context,
                host,
                moduleDefinition,
                resolverSettings,
                userDefinedEnvironment,
                userDefinedPassthroughVariables,
                customCommands,
                allProjectsToBuild);
        }

        private Task<bool> EvaluateAllFilesAsync(IReadOnlySet<AbsolutePath> evaluationGoals, QualifierId qualifierId, IEvaluationScheduler scheduler)
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
                customCommands,
                filteredBuildFiles);

            var graphConstructor = new ProjectGraphToPipGraphConstructor<JavaScriptProject>(pipConstructor, m_host.Configuration.FrontEnd.MaxFrontEndConcurrency());

            return TrySchedulePipsForFileAsync(qualifierId, scheduler, filteredBuildFiles, graphConstructor);
        }

        private async Task<bool> TrySchedulePipsForFileAsync(QualifierId qualifierId, IEvaluationScheduler scheduler, IReadOnlySet<JavaScriptProject> filteredBuildFiles, ProjectGraphToPipGraphConstructor<JavaScriptProject> graphConstructor)
        {
            // If custom scheduling is specified, get the scheduler callback. The result is null if no custom callback is provided.
            Func<ProjectCreationResult<JavaScriptProject>, Possible<ProcessOutputs>> customScheduler = GetCustomSchedulerIfConfigured(scheduler, out ContextTree context);

            using (context)
            {
                var scheduleResult = await graphConstructor.TrySchedulePipsForFilesAsync(filteredBuildFiles, qualifierId, customScheduler);

                if (!scheduleResult.Succeeded)
                {
                    Tracing.Logger.Log.ProjectGraphConstructionError(Context.LoggingContext, m_resolverSettings.Location(Context.PathTable), scheduleResult.Failure.Describe());
                }
                else
                {
                    // On success, store the association between a JavaScript project and its corresponding
                    // scheduled process outputs
                    foreach (var kvp in scheduleResult.Result.ScheduledProcessOutputs)
                    {
                        m_scheduledProcessOutputs.AddOrUpdate(kvp.Key,
                            _ => new List<ProcessOutputs>() { kvp.Value },
                            (key, processes) => { processes.Add(kvp.Value); return processes; });
                    }
                }

                return scheduleResult.Succeeded;
            }
        }

        private EvaluationResult CreateJavaScriptProject(JavaScriptProject project, Pips.Operations.Process process)
        {
            var inputs = process
                .Dependencies
                .Select(input => new EvaluationResult(input))
                .Union(process
                    .DirectoryDependencies
                    .Select(dirInput => new EvaluationResult(StaticDirectory.CreateForOutputDirectory(dirInput))))
                .ToArray();
            
            var outputs = process
                .FileOutputs
                .Select(output => new EvaluationResult(output.Path))
                .Union(process
                    .DirectoryOutputs
                    .Select(dirOutput => new EvaluationResult(DirectoryArtifact.CreateWithZeroPartialSealId(dirOutput.Path))))
                .ToArray();

            // CODESYNC: Public\Sdk\Public\Prelude\Prelude.Configuration.Resolvers.dsc (JavaScriptProject)
            var envVars = process.EnvironmentVariables
                .Where(var => !var.IsPassThrough)
                .Select(var => (Name : var.Name.ToString(Context.StringTable), Value: var.Value.ToString(Context.PathTable)))
                .Where(tuple => !BuildParameters.DisallowedTempVariables.Contains(tuple.Name.ToUpper()))
                .Select(tuple =>
                    new EvaluationResult(ObjectLiteral.Create(new List<Binding> {
                        new Binding(StringId.Create(Context.StringTable, "name"), new EvaluationResult(tuple.Name), location: default),
                        new Binding(StringId.Create(Context.StringTable, "value"), new EvaluationResult(tuple.Value), location: default),
                    }, default, m_resolverSettings.File)))
                .ToArray();

            var passThroughVars = process.EnvironmentVariables
                .Where(var => var.IsPassThrough)
                .Select(var => (Name: var.Name.ToString(Context.StringTable), Value: var.Value.ToString(Context.PathTable)))
                .Where(tuple => !BuildParameters.DisallowedTempVariables.Contains(tuple.Name.ToUpper()))
                .Select(tuple => new EvaluationResult(tuple.Name))
                .ToArray();

            // CODESYNC: Public\Sdk\Public\Prelude\Prelude.Configuration.Resolvers.dsc (JavaScriptProject)
            var bindings = new List<Binding>
            {
                new Binding(StringId.Create(Context.StringTable, "name"), new EvaluationResult(project.Name), location: default),
                new Binding(StringId.Create(Context.StringTable, "scriptCommandName"), new EvaluationResult(project.ScriptCommandName), location: default),
                new Binding(StringId.Create(Context.StringTable, "scriptCommand"), new EvaluationResult(project.ScriptCommand), location: default),
                new Binding(StringId.Create(Context.StringTable, "projectFolder"), new EvaluationResult(DirectoryArtifact.CreateWithZeroPartialSealId(project.ProjectFolder)), location: default),
                new Binding(StringId.Create(Context.StringTable, "inputs"), new EvaluationResult(new EvaluatedArrayLiteral(inputs, default, m_javaScriptWorkspaceResolver.ExportsFile)), location: default),
                new Binding(StringId.Create(Context.StringTable, "outputs"), new EvaluationResult(new EvaluatedArrayLiteral(outputs, default, m_javaScriptWorkspaceResolver.ExportsFile)), location: default),
                new Binding(StringId.Create(Context.StringTable, "environmentVariables"), new EvaluationResult(new EvaluatedArrayLiteral(envVars, default, m_javaScriptWorkspaceResolver.ExportsFile)), location: default),
                new Binding(StringId.Create(Context.StringTable, "passThroughEnvironmentVariables"), new EvaluationResult(new EvaluatedArrayLiteral(passThroughVars, default, m_javaScriptWorkspaceResolver.ExportsFile)), location: default),
                new Binding(StringId.Create(Context.StringTable, "tempDirectory"), new EvaluationResult(DirectoryArtifact.CreateWithZeroPartialSealId(process.TempDirectory)), location: default),
            };

            return new EvaluationResult(ObjectLiteral.Create(bindings, default, m_resolverSettings.File));
        }

        private Func<ProjectCreationResult<JavaScriptProject>, Possible<ProcessOutputs>> GetCustomSchedulerIfConfigured(IEvaluationScheduler scheduler, out ContextTree evaluationContext)
        {
            if (m_resolverSettings.CustomScheduling == null)
            {
                evaluationContext = null;
                return null;
            }

            var moduleRegistry = (ModuleRegistry)m_host.ModuleRegistry;

            // The imports file is always evaluated with the empty qualifier. Instantiate it using the module registry
            var importsModule = moduleRegistry
                .GetUninstantiatedModuleInfoByPath(m_javaScriptWorkspaceResolver.ImportsFile)
                .FileModuleLiteral
                .InstantiateFileModuleLiteral(moduleRegistry, QualifierValue.CreateEmpty(Context.QualifierTable));

            // Instead of asking the type checker for the location of the function to evaluate, directly use the top-level variable declaration that represents the callback
            // we computed during workspace construction
            bool success = importsModule.TryGetResolvedEntry(
                moduleRegistry,
                new FilePosition(m_javaScriptWorkspaceResolver.CustomSchedulingCallback.Pos, m_javaScriptWorkspaceResolver.ImportsFile),
                out var schedulingCallbackEntry,
                out _);
            // The entry should be there because we added it in JavaScriptWorkspaceResolver to imports.dsc
            Contract.Assert(success);
            // The entry should be a thunk (i.e. a top level value in imports.dsc)
            Contract.AssertNotNull(schedulingCallbackEntry.Thunk);

            // Create the root context that will be used for evaluating all the DScript scheduling callbacks
            // Observe this context needs to be disposed, but that has to happen after all evaluations are done
            var contextTree = new ContextTree(
                m_host,
                Context,
                m_logger,
                new EvaluationStatistics(),
                new QualifierValueCache(),
                isBeingDebugged: false,
                decorator: null,
                importsModule,
                new EvaluatorConfiguration(trackMethodInvocations: false, cycleDetectorStartupDelay: TimeSpanUtilities.MillisecondsToTimeSpan(10)),
                scheduler,
                FileType.Project);

            evaluationContext = contextTree;

            // The result of Transformer.execute() is an object literal constructed off a ProcessOutputs instance. The original instance is kept
            // in a member of the object literal that AmbientTransformer advertises
            var processOutputsKey = SymbolAtom.Create(Context.StringTable, AmbientTransformerBase.ProcessOutputsSymbolName);

            var schedulingCallbackName = FullSymbol.Create(Context.SymbolTable, SymbolAtom.Create(Context.StringTable, m_javaScriptWorkspaceResolver.CustomSchedulingCallback.Name.GetText()));

            var factory = new MutableContextFactory(
                        schedulingCallbackEntry.Thunk,
                        schedulingCallbackName,
                        importsModule,
                        templateValue: null,
                        TypeScript.Net.Utilities.LineInfo.FromLineAndPosition(m_javaScriptWorkspaceResolver.CustomSchedulingCallback.Pos, position: 1));

            // Evaluate the thunk. Observe this means evaluating the top level value, which has type closure. By evaluating it, we get access to the referenced function.
            var thunkEvaluation = schedulingCallbackEntry.Thunk.Evaluate(contextTree.RootContext, importsModule, EvaluationStackFrame.Empty(), ref factory);
            // This thunk in particular is a value assignment of a property access expression, so nothing should fail
            Contract.Assert(!thunkEvaluation.IsErrorValue);
            
            // If the thunk evaluation comes back as undefined, this is sort of a corner case where the definition of the function callback is a lambda with an
            // undefined body. We could error here, but instead we consider this an indication that no custom scheduling should happen
            if (thunkEvaluation.IsUndefined)
            {
                return (ProjectCreationResult<JavaScriptProject> project) => new Possible<ProcessOutputs>((ProcessOutputs)null);
            }

            // The result of evaluating the thunk should be a closure representing the scheduling callback.
            var closure = thunkEvaluation.Value as Closure;
            Contract.AssertNotNull(closure);

            return (ProjectCreationResult<JavaScriptProject> createdProject) =>
            {
                // Create the argument that will be passed to the DScript callback
                EvaluationResult javaScriptProject = CreateJavaScriptProject(createdProject.Project, createdProject.Process);
                
                // For each JavaScript project, callbacks may be called concurrently. Create a mutable child context for each invocation
                // using for the full symbol 'projectName_ScriptCommandName', which is unique per resolver
                var childFactory = new MutableContextFactory(
                        schedulingCallbackEntry.Thunk,
                        FullSymbol.Create(Context.SymbolTable, PipConstructionUtilities.SanitizeStringForSymbol($"{createdProject.Project.Name}_{createdProject.Project.ScriptCommandName}")),
                        importsModule,
                        templateValue: null,
                        TypeScript.Net.Utilities.LineInfo.FromLineAndPosition(m_javaScriptWorkspaceResolver.CustomSchedulingCallback.Pos, position: 1));

                using (var childContext = childFactory.Create(contextTree.RootContext))
                // Create a stack frame with no captures and push the JavaScript project as an argument
                using (var args = EvaluationStackFrame.Create(closure.Function, CollectionUtilities.EmptyArray<EvaluationResult>()))
                using (childContext.PushStackEntry(closure.Function, closure.Env, importsModule, schedulingCallbackEntry.Location, args))
                {
                    args.TrySetArguments(1, javaScriptProject);
                    var closureEvaluation = childContext.InvokeClosure(closure, args);

                    if (closureEvaluation.IsErrorValue)
                    {
                        var functionLocation = closure.Function.Location;
                        return new JavaScriptProjectSchedulingFailure(createdProject.Project, $"A custom scheduler was specified at '{closure.Env.Path.ToString(Context.PathTable)}({functionLocation.Line},{functionLocation.Position})' to handle the " +
                            "evaluation of this project, but it failed with an evaluation error. Details should have been logged already.");
                    }

                    // If the result is undefined, that's the indication the callback is not picking up this particular project
                    // This is indicated to the underling engine as a null
                    if (closureEvaluation.IsUndefined)
                    {
                        return new Possible<ProcessOutputs>((ProcessOutputs)null);
                    }

                    // The result of executing the callback should be an object literal representing process outputs.
                    // This literal is ultimately the result of running Transformer.execute()
                    var executeResult = closureEvaluation.Value as ObjectLiteral;
                    Contract.AssertNotNull(executeResult);

                    // Retrieve process outputs from the object literal
                    var processOutputs = executeResult[processOutputsKey].Value as ProcessOutputs;
                    Contract.AssertNotNull(processOutputs);

                    return processOutputs;
                }
            };
        }
    }
}
