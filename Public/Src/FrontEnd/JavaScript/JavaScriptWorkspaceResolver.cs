// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildXL.FrontEnd.JavaScript.ProjectGraph;
using BuildXL.FrontEnd.Script;
using BuildXL.FrontEnd.Script.Ambients.Map;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.RuntimeModel.AstBridge;
using BuildXL.FrontEnd.Script.Util;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Utilities;
using BuildXL.FrontEnd.Utilities.GenericProjectGraphResolver;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Utilities;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using JetBrains.Annotations;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TypeScript.Net.DScript;
using TypeScript.Net.Types;
using ConfigurationConverter = BuildXL.FrontEnd.Script.Util.ConfigurationConverter;
using SourceFile = TypeScript.Net.Types.SourceFile;
using SyntaxKind = TypeScript.Net.Types.SyntaxKind;

namespace BuildXL.FrontEnd.JavaScript
{
    /// <summary>
    /// Workspace resolver for JavaScript based resolvers 
    /// </summary>
    public abstract class JavaScriptWorkspaceResolver<TGraphConfiguration, TResolverSettings> : ProjectGraphWorkspaceResolverBase<JavaScriptGraphResult<TGraphConfiguration>, TResolverSettings> 
        where TGraphConfiguration: class 
        where TResolverSettings : class, IJavaScriptResolverSettings
    {
        /// <summary>
        /// Name of the Bxl configuration file that can be dropped at the root of a JavaScript project
        /// </summary>
        internal const string BxlConfigurationFilename = "bxlconfig.json";

        /// <summary>
        /// Script commands to execute to list of dependencies
        /// </summary>
        protected IReadOnlyDictionary<string, IReadOnlyList<IJavaScriptCommandDependency>> ComputedCommands;
        
        private IReadOnlyDictionary<string, IReadOnlyList<string>> m_commandGroups;

        private FullSymbol AllProjectsSymbol { get; set; }

        /// <summary>
        /// Context used to evaluate callbacks specified in the resolver configuration (that is, the main config file)
        /// </summary>
        /// <remarks>
        /// This context should be used for non-concurrent evaluations (otherwise a MutableContext should be used)
        /// </remarks>
        private ContextTree m_configEvaluationContext;

        private readonly Script.Tracing.Logger m_logger;

        /// <summary>
        /// Preserves references for objects (so project references get correctly reconstructed), adds indentation for easier 
        /// debugging (at the cost of a slightly higher serialization size) and includes nulls explicitly
        /// </summary>
        protected static readonly JsonSerializerSettings JsonSerializerSettings = new JsonSerializerSettings
        {
            PreserveReferencesHandling = PreserveReferencesHandling.None,
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Include
        };

        /// <inheritdoc/>
        public JavaScriptWorkspaceResolver(string resolverKind)
        {
            Name = resolverKind;
            m_logger = Script.Tracing.Logger.CreateLogger(preserveLogEvents: true);
        }

        /// <inheritdoc/>
        public override string Kind => Name;

        /// <summary>
        /// The path to an 'exports' DScript file, with all the exported top-level values 
        /// </summary>
        /// <remarks>
        /// This file is added in addition to the current one-file-per-project
        /// approach, as a single place to contain all exported values.
        /// </remarks>
        public AbsolutePath ExportsFile { get; private set; }

        /// <summary>
        /// The path to an 'imports' DScript file, with values that get imported from configured modules
        /// </summary>
        /// <remarks>
        /// This file is added in addition to the current one-file-per-project
        /// approach, as a single place to contain all imported values.
        /// </remarks>
        public AbsolutePath ImportsFile { get; private set; }

        /// <summary>
        /// The path to an in-memory DScript file with all the LazyEval expressions
        /// </summary>
        /// <remarks>
        /// This file is added in addition to the current one-file-per-project
        /// approach, as a single place to contain all imported values.
        /// </remarks>
        public AbsolutePath LazyEvaluationsFile { get; private set; }

        /// <summary>
        /// Mapping between the ILazyEval instance specified in the resolver configuration with its corresponding statement in the lazy expression source file
        /// </summary>
        [CanBeNull]
        public IReadOnlyDictionary<IVariableStatement, ILazyEval> LazyEvaluationStatementMapping { get; private set; }

        /// <summary>
        /// Mapping between JavaScript projects and the additional lazy evaluation artifacts
        /// </summary>
        [CanBeNull]
        public IReadOnlyDictionary<JavaScriptProject, IReadOnlySet<ILazyEval>> AdditionalLazyArtifactsPerProject { get; private set; }

        /// <summary>
        /// If a custom scheduling callback is specified, this is the variable declaration (with function type) that has to be evaluated
        /// to get the result of the custom scheduling
        /// </summary>
        [CanBeNull]
        public VariableDeclaration CustomSchedulingCallback { get; private set; }

        /// <inheritdoc/>
        public override bool TryInitialize(FrontEndHost host, FrontEndContext context, IConfiguration configuration, IResolverSettings resolverSettings)
        {
            var success = base.TryInitialize(host, context, configuration, resolverSettings);
            
            if (!success)
            {
                return false;
            }

            if (!JavaScriptCommandsInterpreter.TryComputeAndValidateCommands(
                    Context.LoggingContext,
                    resolverSettings.Location(Context.PathTable),
                    ((IJavaScriptResolverSettings)resolverSettings).Execute,
                    out ComputedCommands,
                    out m_commandGroups))
            {
                // Error has been logged
                return false;
            }

            ExportsFile = ResolverSettings.Root.Combine(Context.PathTable, "exports.dsc");
            ImportsFile = ResolverSettings.Root.Combine(Context.PathTable, "imports.dsc");
            LazyEvaluationsFile = ResolverSettings.Root.Combine(Context.PathTable, "lazyEvals.dsc"); 
            AllProjectsSymbol = FullSymbol.Create(Context.SymbolTable, "all");

            // The context is initialized as if all evaluations happen on the main config file.
            // This is accurate since this is used to evaluate callbacks specified in the main config file
            // before the workspace is actually constructed.
            m_configEvaluationContext = new ContextTree(
                Host,
                Context,
                m_logger,
                new EvaluationStatistics(),
                new QualifierValueCache(),
                isBeingDebugged: false,
                decorator: null,
                ((ModuleRegistry)host.ModuleRegistry).GetUninstantiatedModuleInfoByPath(Configuration.Layout.PrimaryConfigFile).FileModuleLiteral,
                new EvaluatorConfiguration(
                    trackMethodInvocations: false,
                    cycleDetectorStartupDelay: TimeSpan.FromSeconds(Configuration.FrontEnd.CycleDetectorStartupDelay())),
                Host.DefaultEvaluationScheduler,
                FileType.GlobalConfiguration);
            
            return true;
        }

        /// <summary>
        /// Collects all JavaScript projects that need to be part of specified exports
        /// </summary>
        protected bool TryResolveExports(
            IReadOnlyCollection<JavaScriptProject> projects,
            IReadOnlyCollection<DeserializedJavaScriptProject> deserializedProjects,
            [CanBeNull] JavaScriptProjectSelector projectSelector,
            out IReadOnlyCollection<ResolvedJavaScriptExport> resolvedExports)
        {
            // Build dictionaries to speed up subsequent look-ups
            var nameToDeserializedProjects = deserializedProjects.ToDictionary(kvp => kvp.Name, kvp => kvp);
            var nameAndCommandToProjects = projects.ToDictionary(kvp => (kvp.Name, kvp.ScriptCommandName), kvp => kvp);

            var exports = new List<ResolvedJavaScriptExport>();
            resolvedExports = exports;

            // Add a baked-in 'all' symbol, with all the scheduled projects
            exports.Add(new ResolvedJavaScriptExport(AllProjectsSymbol,
                nameAndCommandToProjects.Values.Where(javaScriptProject => javaScriptProject.CanBeScheduled()).ToList()));

            if (ResolverSettings.Exports == null)
            {
                return true;
            }

            projectSelector ??= new JavaScriptProjectSelector(projects);

            foreach (var export in ResolverSettings.Exports)
            {
                // The export symbol cannot be one of the reserved ones (which for now is just one: 'all')
                if (export.SymbolName == AllProjectsSymbol)
                {
                    Tracing.Logger.Log.SpecifiedExportIsAReservedName(
                        Context.LoggingContext,
                        ResolverSettings.Location(Context.PathTable),
                        AllProjectsSymbol.ToString(Context.SymbolTable));
                    return false;
                }

                var projectsForSymbol = new List<JavaScriptProject>();

                foreach (var selector in export.Content)
                {
                    if (!projectSelector.TryGetMatches(selector, out var selectedProjects, out var failure))
                    {
                        Tracing.Logger.Log.InvalidRegexInProjectSelector(
                            Context.LoggingContext,
                            ResolverSettings.Location(Context.PathTable),
                            selector.GetValue().ToString(),
                            failure);

                        return false;
                    }

                    if (!selectedProjects.Any())
                    {
                        Tracing.Logger.Log.SpecifiedPackageForExportDoesNotExist(
                            Context.LoggingContext,
                            ResolverSettings.Location(Context.PathTable),
                            export.SymbolName.ToString(Context.SymbolTable),
                            selector.GetValue().ToString());

                        return false;
                    }

                    foreach (var selectedProject in selectedProjects)
                    {
                        if (!selectedProject.CanBeScheduled())
                        {
                            // The project/command is not there. This can happen if the corresponding command was
                            // not requested to be executed. So just log an informational message here
                            Tracing.Logger.Log.RequestedExportIsNotPresent(
                                Context.LoggingContext,
                                ResolverSettings.Location(Context.PathTable),
                                export.SymbolName.ToString(Context.SymbolTable),
                                selectedProject.Name,
                                selectedProject.ScriptCommandName);

                            continue;
                        }
                        projectsForSymbol.Add(selectedProject);
                    }
                }

                exports.Add(new ResolvedJavaScriptExport(export.SymbolName, projectsForSymbol));
            }

            return true;
        }

        /// <summary>
        /// Creates empty source files for all JavaScript projects, with the exception of the special 'exports' file, where
        /// all required top-level exports go
        /// </summary>
        protected override SourceFile DoCreateSourceFile(AbsolutePath path)
        {
            if (path == LazyEvaluationsFile)
            {
                return CreateLazyEvaluationsFile(path);
            }
            else
            {
                var sourceFile = SourceFile.Create(path.ToString(Context.PathTable));

                // We consider all files to be DScript files, even though the extension might not be '.dsc'
                // And additionally, we consider them to be external modules, so exported stuff is interpreted appropriately
                // by the type checker
                sourceFile.OverrideIsScriptFile = true;
                sourceFile.ExternalModuleIndicator = sourceFile;

                // If we need to generate the export file, add all specified export symbols
                PopulateExportsFileIfNeeded(path, sourceFile);

                // If a custom scheduling function is referenced, let's add the reference to the imports file
                PopulateImportsFileIfNeeded(path, sourceFile);

                return sourceFile;
            }
        }

        private SourceFile CreateLazyEvaluationsFile(AbsolutePath path)
        {
            if (ResolverSettings.AdditionalDependencies == null)
            {
                return SourceFile.Create(path.ToString(Context.PathTable));
            }

            // Collect all lazy expressions in declaration order
            ILazyEval[] lazyEvaluations = ResolverSettings.AdditionalDependencies
                .SelectMany(javaScriptDependency =>
                    javaScriptDependency.Dependencies
                        .Where(dependency => dependency.GetValue() is ILazyEval)
                        .Select(union => union.GetValue() as ILazyEval))
                .ToArray();

            // For each lazy expression, add a variable declaration with the shape
            // const lazyN : expectedReturnType = lazyExpression
            // In this way we validate the provided expression is legit and compatible with the declared expected type
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < lazyEvaluations.Length; i++)
            {
                var lazyEvaluation = lazyEvaluations[i];

                sb.AppendLine($"const lazy{i} : {lazyEvaluation.FormattedExpectedType} = {lazyEvaluation.Expression};");
            }

            // Parse the source file
            // As a result, all the statements of the file are variable declarations, corresponding to each lazy
            FrontEndUtilities.TryParseSourceFile(Context, LazyEvaluationsFile, sb.ToString(), out var sourceFile);

            // Create a mapping from statements to lazys for the resolver to consume, so they can be later linked
            // with the JavaScriptProjects they affect
            var lazyMapping = new Dictionary<IVariableStatement, ILazyEval>(lazyEvaluations.Length);
            for (int i = 0; i < lazyEvaluations.Length; i++)
            {
                lazyMapping[(IVariableStatement)sourceFile.Statements[i]] = lazyEvaluations[i];
            }

            LazyEvaluationStatementMapping = lazyMapping;

            return sourceFile;
        }

        private void PopulateImportsFileIfNeeded(AbsolutePath path, SourceFile sourceFile)
        {
            // The import file contains a reference to the custom scheduling function, if specified.
            // The provided DScript callback could be evaluated directly, but instead of that we declare a function in this module
            // with the proper type and assign it to the referenced callback. Reasons are:
            // 1) In this way validating the provided callback has the right type is naturally enforced by the type checker (since the function we declare in this module has explicit type
            // annotations
            // 2) The module literal and resolved entry to use for evaluation doesn't have to be discovered by asking the type checker and can be pinned directly to this imports.dsc file
            if (path == ImportsFile && ResolverSettings.CustomScheduling != null)
            {
                // The scheduling function can be a dotted identifier 
                var schedulingFunction = FullSymbol.Create(Context.SymbolTable, ResolverSettings.CustomScheduling.SchedulingFunction);

                // Add an import to the user defined module and scheduling function by adding  'import { schedulingFunctionRootIdentifier } from "module"'
                var import = new ImportDeclaration(new[] { schedulingFunction.GetRoot(Context.SymbolTable).ToString(Context.StringTable) }, ResolverSettings.CustomScheduling.Module)
                {
                    Pos = 1,
                    End = 2
                };
                sourceFile.Statements.Add(import);

                // Now construct the function type, that needs to be JavaScriptProject => TransformerExecuteResult
                var evaluatedProjectType = new TypeReferenceNode("JavaScriptProject");
                evaluatedProjectType.TypeName.Pos = 3;
                evaluatedProjectType.TypeName.End = 4;

                var executeResult = new TypeReferenceNode("TransformerExecuteResult");
                executeResult.TypeName.Pos = 3;
                executeResult.TypeName.End = 4;

                var functionType = new FunctionOrConstructorTypeNode
                {
                    Kind = SyntaxKind.FunctionType,
                    Type = executeResult,
                    Parameters = new NodeArray<IParameterDeclaration>(new ParameterDeclaration { Name = new IdentifierOrBindingPattern(new Identifier("project")), Type = evaluatedProjectType }),
                    Pos = 3,
                    End = 4
                };

                // The initializer is either a property access expression if a FQN is needed, or a simple identifier in case of referencing a top level value
                IExpression initializer;
                if (schedulingFunction.GetParent(Context.SymbolTable).IsValid)
                {
                    IReadOnlyList<string> parts = schedulingFunction.ToReadOnlyList(Context.SymbolTable).Select(atom => atom.ToString(Context.StringTable)).ToList();
                    initializer = new PropertyAccessExpression(parts, Enumerable.Repeat((3, 4), parts.Count).ToList<(int, int)>())
                    {
                        Pos = 3,
                        End = 4
                    };
                }
                else
                {
                    initializer = new Identifier(schedulingFunction.ToString(Context.SymbolTable))
                    {
                        Pos = 3,
                        End = 4
                    };
                }


                // Create a const declaration and assign it to the callback: const schedulingFunction : (JavaScriptProject) => TransformerExecuteResult = schedulingFunctionName;
                // Observe 'schedulingFunction' is an arbitrary name. This is a private value, so it is not visible to other modules.
                CustomSchedulingCallback = new VariableDeclaration("schedulingFunction", initializer, functionType)
                {
                    Pos = 3,
                    End = 4
                };
                CustomSchedulingCallback.Name.Pos = 3;
                CustomSchedulingCallback.Name.End = 4;

                // Final source file looks like
                //   import { schedulingFunctionName } from "module";
                //   const schedulingFunction : (JavaScriptProject) => Result = schedulingFunctionName;
                sourceFile.Statements.Add(new VariableStatement()
                {
                    DeclarationList = new VariableDeclarationList(
                            NodeFlags.Const,
                            CustomSchedulingCallback)
                });

                sourceFile.SetLineMap(new[] { 0, 4 });
            }
        }

        private void PopulateExportsFileIfNeeded(AbsolutePath path, SourceFile sourceFile)
        {
            if (path == ExportsFile)
            {
                // By forcing the default qualifier declaration to be in the exports file, we save us the work
                // to register all specs as corresponding uninstantiated modules (since otherwise the default qualifier
                // is placed on a random spec in the module, and we have to account for that randomness)
                sourceFile.AddDefaultQualifierDeclaration();

                // For each exported symbol, add a top-level value with type SharedOpaqueDirectory[]
                // The initializer value is defined as 'undefined', but that's not really important
                // since the evaluation of these symbols is customized in the resolver
                int pos = 1;
                foreach (var export in ComputedProjectGraph.Result.Exports)
                {
                    // The type reference needs pos and end set, otherwise the type checker believes this is a missing node
                    var typeReference = new TypeReferenceNode("SharedOpaqueDirectory");
                    typeReference.TypeName.Pos = pos;
                    typeReference.TypeName.End = pos + 1;

                    // Each symbol is added at position (start,end) = (n, n+1), with n starting in 1 for the first symbol
                    FrontEndUtilities.AddExportToSourceFile(
                        sourceFile,
                        export.FullSymbol.ToString(Context.SymbolTable),
                        new ArrayTypeNode { ElementType = typeReference },
                        pos,
                        pos + 1);
                    pos += 2;
                }

                // As if all declarations happened on the same line
                sourceFile.SetLineMap(new[] { 0, Math.Max(pos - 2, 2) });
            }
        }

        /// <inheritdoc/>
        protected override Task<Possible<JavaScriptGraphResult<TGraphConfiguration>>> TryComputeBuildGraphAsync()
        {
            BuildParameters.IBuildParameters buildParameters = RetrieveBuildParameters();

            return TryComputeBuildGraphAsync(buildParameters);
        }

        private async Task<Possible<JavaScriptGraphResult<TGraphConfiguration>>> TryComputeBuildGraphAsync(BuildParameters.IBuildParameters buildParameters)
        {
            Possible<(JavaScriptGraph<TGraphConfiguration> graph, GenericJavaScriptGraph<DeserializedJavaScriptProject, TGraphConfiguration> flattenedGraph)> maybeResult = await ComputeBuildGraphAsync(buildParameters);

            if (!maybeResult.Succeeded)
            {
                // A more specific error has been logged already
                return maybeResult.Failure;
            }

            var javaScriptGraph = maybeResult.Result.graph;
            var flattenedGraph = maybeResult.Result.flattenedGraph;

            JavaScriptProjectSelector projectSelector = null;

            // If additional dependencies are specified, let's extend projects with them
            if (ResolverSettings.AdditionalDependencies != null)
            {
                projectSelector = new JavaScriptProjectSelector(javaScriptGraph.Projects);
                if (!TryUpdateProjectsWithAdditionalDependencies(projectSelector))
                {
                    // Specific error should have been logged
                    return new JavaScriptGraphConstructionFailure(ResolverSettings, Context.PathTable);
                }
            }

            if (!TryResolveExports(javaScriptGraph.Projects, flattenedGraph.Projects, projectSelector, out var exports))
            {
                // Specific error should have been logged
                return new JavaScriptGraphConstructionFailure(ResolverSettings, Context.PathTable);
            }

            // The module contains all project files that are part of the graph
            var projectFiles = new HashSet<AbsolutePath>();
            foreach (JavaScriptProject project in javaScriptGraph.Projects)
            {
                projectFiles.Add(project.PackageJsonFile(Context.PathTable));
            }

            // Add an 'exports' source file at the root of the repo that will contain all top-level exported values
            // Since all the specs are exposed as part of a single module, it actually doesn't matter where
            // all these values are declared, but by defining a fixed source file for it, we keep it deterministic
            projectFiles.Add(ExportsFile);

            // Add an 'imports' source file at the root of the repo that will contain all top-level imported values
            projectFiles.Add(ImportsFile);

            // Add a file with all the lazy evaluations
            projectFiles.Add(LazyEvaluationsFile);

            var moduleDescriptor = ModuleDescriptor.CreateWithUniqueId(Context.StringTable, ResolverSettings.ModuleName, this);
            var moduleDefinition = ModuleDefinition.CreateModuleDefinitionWithImplicitReferences(
                moduleDescriptor,
                ResolverSettings.Root,
                ResolverSettings.File,
                projectFiles,
                allowedModuleDependencies: null, // no module policies
                cyclicalFriendModules: null, // no allowlist of cycles
                mounts: null);

            return new JavaScriptGraphResult<TGraphConfiguration>(javaScriptGraph, moduleDefinition, exports);
        }

        /// <summary>
        /// Adds all project-to-project additional dependencies to the existing graph
        /// </summary>
        /// <remarks>
        /// Lazy artifacts cannot be added at this point because the workspace is not constructed yet.
        /// This function keeps track of them by populating <see cref="AdditionalLazyArtifactsPerProject"/> as a side
        /// effect
        /// </remarks>
        private bool TryUpdateProjectsWithAdditionalDependencies(JavaScriptProjectSelector projectSelector)
        {
            // A given project can be extended by multiple additional dependencies, so let's keep a dictionary with the additions
            var additionalProjectDependenciesByProject = new Dictionary<JavaScriptProject, HashSet<JavaScriptProject>>();
            var additionalLazyArtifactDependenciesByProject = new Dictionary<JavaScriptProject, HashSet<ILazyEval>>();

            // Each IJavaScriptDependency defines a set of dependents and dependencies where all dependents depend on all dependencies
            foreach (var additionalDependency in ResolverSettings.AdditionalDependencies)
            {
                using (var inputProjectsWrapper = JavaScriptPools.JavaScriptProjectSet.GetInstance())
                using (var inputLazyArtifactsWrapper = JavaScriptPools.LazyEvalSet.GetInstance())
                {
                    var inputProjects = inputProjectsWrapper.Instance;
                    var inputLazyArtifacts = inputLazyArtifactsWrapper.Instance;

                    // Each dependency can be a selector over JavaScript projects or a lazy evaluation representing an artifact. 
                    // Here we care only about project dependencies
                    foreach (var dependency in additionalDependency.Dependencies)
                    {
                        if (dependency.GetValue() is ILazyEval eval)
                        {
                            inputLazyArtifacts.Add(eval);
                        }
                        else
                        {
                            // Let's project the discriminating union to the types we know so we can reuse the project selector
                            var selector = new DiscriminatingUnion<string, IJavaScriptProjectSimpleSelector, IJavaScriptProjectRegexSelector>();
                            selector.TrySetValue(dependency.GetValue());
                            if (!projectSelector.TryGetMatches(selector, out var matches, out var failure))
                            {
                                Tracing.Logger.Log.InvalidRegexInProjectSelector(
                                        Context.LoggingContext,
                                        ResolverSettings.Location(Context.PathTable),
                                        selector.GetValue().ToString(),
                                        failure);
                                return false;
                            }

                            inputProjects.AddRange(matches);
                        }
                    }

                    // If there are no dependencies to add, shortcut the search
                    if (inputProjects.Count == 0 && inputLazyArtifacts.Count == 0)
                    {
                        continue;
                    }

                    // Dependents are all JS project selectors. Each dependent depends on all the computed dependencies
                    foreach (var dependent in additionalDependency.Dependents.SelectMany(dependent => { 
                        if (!projectSelector.TryGetMatches(dependent, out var matches, out var failure))
                        {
                            // If the regex is not legit, log and return a null singleton
                            Tracing.Logger.Log.InvalidRegexInProjectSelector(
                                        Context.LoggingContext,
                                        ResolverSettings.Location(Context.PathTable),
                                        dependent.GetValue().ToString(),
                                        failure);
                            return new JavaScriptProject[] { null };
                        }

                        return matches;
                    }))
                    {
                        // This is the case where the regex is not legit. Error has been logged already, just return.
                        if (dependent == null)
                        {
                            return false;
                        }

                        if (additionalProjectDependenciesByProject.TryGetValue(dependent, out var additionalDependencies))
                        {
                            additionalDependencies.AddRange(inputProjects);
                        }
                        else
                        {
                            additionalProjectDependenciesByProject[dependent] = new HashSet<JavaScriptProject>(inputProjects);
                        }

                        if (additionalLazyArtifactDependenciesByProject.TryGetValue(dependent, out var lazyArtifacts))
                        {
                            lazyArtifacts.AddRange(inputLazyArtifacts);
                        }
                        else
                        {
                            additionalLazyArtifactDependenciesByProject[dependent] = new HashSet<ILazyEval>(inputLazyArtifacts);
                        }
                    }
                }

                AdditionalLazyArtifactsPerProject = additionalLazyArtifactDependenciesByProject.ToDictionary(kvp => kvp.Key, kvp => (IReadOnlySet<ILazyEval>)kvp.Value.ToReadOnlySet());
            }

            // We update all the additional project dependencies here. LazyArtifacts need to be resolved after the workspace is constructed since those
            // expressions might reference other modules
            foreach (var kvp in additionalProjectDependenciesByProject)
            {
                kvp.Key.SetDependencies(kvp.Value.AddRange(kvp.Key.Dependencies));
            }

            return true;
        }

        /// <summary>
        /// Implementors should return the build graph 
        /// </summary>
        protected abstract Task<Possible<(JavaScriptGraph<TGraphConfiguration>, GenericJavaScriptGraph<DeserializedJavaScriptProject, TGraphConfiguration>)>> ComputeBuildGraphAsync(BuildParameters.IBuildParameters buildParameters);

        /// <summary>
        /// Resolves a JavaScript graph with execution semantics, as specified in 'execute'
        /// </summary>
        /// <param name="flattenedJavaScriptGraph"></param>
        /// <returns></returns>
        protected Possible<JavaScriptGraph<TGraphConfiguration>> ResolveGraphWithExecutionSemantics(GenericJavaScriptGraph<DeserializedJavaScriptProject, TGraphConfiguration> flattenedJavaScriptGraph)
        {
            // The way we deal with command groups is the following. We flatten those into its regular command components and put them together with regular commands. Dependency resolution happens among
            // both group and regular commands. The logic is: if there is a dependency on a command member, then that's a dependency on the containing command group. And the command group dependencies are also the 
            // dependencies of its command members. Command members are not included as part of the final collections of projects, only their corresponding command groups.

            // Compute the inverse relationship between commands and their groups
            var commandGroupMembership = BuildCommandGroupMembership();

            // Get the list of all regular commands
            var allFlattenedCommands = ComputedCommands.Keys.Where(command => !m_commandGroups.ContainsKey(command)).Union(m_commandGroups.Values.SelectMany(commandMembers => commandMembers)).ToList();

            // Here we put all resolved projects (including the ones belonging to a group command)
            var resolvedProjects = new Dictionary<(string projectName, string command), (JavaScriptProject JavaScriptProject, DeserializedJavaScriptProject deserializedJavaScriptProject)>(flattenedJavaScriptGraph.Projects.Count * allFlattenedCommands.Count);
            // Here we put the resolved projects that belong to a given group
            var resolvedGroups = new MultiValueDictionary<(string projectName, string commandGroup), JavaScriptProject>(m_commandGroups.Keys.Count());
            // This is the final list of projects
            var resultingProjects = new List<JavaScriptProject>();

            // Each requested script command defines a JavaScript project
            foreach (var command in allFlattenedCommands)
            {
                foreach (var deserializedProject in flattenedJavaScriptGraph.Projects)
                {
                    // If the requested script is not available on the project, skip it
                    if (!deserializedProject.AvailableScriptCommands.ContainsKey(command))
                    {
                        continue;
                    }

                    if (!TryValidateAndCreateProject(command, deserializedProject, out JavaScriptProject javaScriptProject, out Failure failure))
                    {
                        return failure;
                    }

                    // Here we check for duplicate projects
                    if (resolvedProjects.ContainsKey((javaScriptProject.Name, command)))
                    {
                        return new JavaScriptProjectSchedulingFailure(javaScriptProject,
                            $"Duplicate project name '{javaScriptProject.Name}' defined in '{javaScriptProject.ProjectFolder.ToString(Context.PathTable)}' " +
                            $"and '{resolvedProjects[(javaScriptProject.Name, command)].JavaScriptProject.ProjectFolder.ToString(Context.PathTable)}' for script command '{command}'");
                    }

                    resolvedProjects.Add((javaScriptProject.Name, command), (javaScriptProject, deserializedProject));

                    // If the command does not belong to any group, we know it is already part of the final list of projects
                    if (!commandGroupMembership.TryGetValue(command, out string commandGroup))
                    {
                        resultingProjects.Add(javaScriptProject);
                    }
                    else
                    {
                        // Otherwise, group it so we can inspect it later
                        resolvedGroups.Add((javaScriptProject.Name, commandGroup), javaScriptProject);
                    }
                }
            }

            var deserializedProjectsByName = flattenedJavaScriptGraph.Projects.ToDictionary(project => project.Name);
            // Here we build a map between each group member to its group project
            var resolvedCommandGroupMembership = new Dictionary<JavaScriptProject, JavaScriptProject>();

            // Now add groups commands
            foreach (var kvp in resolvedGroups)
            {
                string commandName = kvp.Key.commandGroup;
                string projectName = kvp.Key.projectName;
                IReadOnlyList<JavaScriptProject> members = kvp.Value;

                Contract.Assert(members.Count > 0);

                var deserializedProject = deserializedProjectsByName[projectName];
                var groupProject = CreateGroupProject(commandName, projectName, members, deserializedProject);

                // Here we check for duplicate projects
                if (resolvedProjects.ContainsKey((groupProject.Name, commandName)))
                {
                    return new JavaScriptProjectSchedulingFailure(groupProject,
                        $"Duplicate project name '{groupProject.Name}' defined in '{groupProject.ProjectFolder.ToString(Context.PathTable)}' " +
                        $"and '{resolvedProjects[(groupProject.Name, commandName)].JavaScriptProject.ProjectFolder.ToString(Context.PathTable)}' for script command '{commandName}'");
                }

                resolvedProjects.Add((groupProject.Name, commandName), (groupProject, deserializedProject));
                // This project group should be part of the final list of projects
                resultingProjects.Add(groupProject);

                // Update the resolved membership so each member points to its group project
                foreach (var member in members)
                {
                    resolvedCommandGroupMembership[member] = groupProject;
                }
            }

            // Start with an empty cache for closest present dependencies
            var closestDependenciesCache = new Dictionary<(string name, string command), HashSet<JavaScriptProject>>();
            // Used to make sure a cycle in the package-to-package graph (that will be caught later in ProjectGraphToPipGraphConstructor.TrySchedulePipsForFilesAsync)
            // does not induce a cycle when looking for closest dependencies of absent commands.
            var cycleDetector = new HashSet<(string name, string command)>();

            // Now resolve dependencies
            foreach (var kvp in resolvedProjects)
            {
                string command = kvp.Key.command;
                JavaScriptProject javaScriptProject = kvp.Value.JavaScriptProject;

                IReadOnlyList<IJavaScriptCommandDependency> dependencies = GetCommandDependencies(command, commandGroupMembership);

                // Let's use a pooled set for computing the dependencies to make sure we are not adding duplicates
                using (var projectDependenciesWrapper = Pools.CreateSetPool<JavaScriptProject>().GetInstance())
                {
                    var projectDependencies = projectDependenciesWrapper.Instance;
                    foreach (IJavaScriptCommandDependency dependency in dependencies)
                    {
                        // If it is a local dependency, add a dependency to the same JavaScript project and the specified command
                        if (dependency.IsLocalKind())
                        {
                            // If it is not defined try to find its closest transitive dependencies
                            if (!resolvedProjects.TryGetValue((javaScriptProject.Name, dependency.Command), out var value))
                            {
                                AddClosestPresentDependencies(
                                    javaScriptProject.Name, 
                                    dependency.Command, 
                                    resolvedProjects, 
                                    deserializedProjectsByName, 
                                    projectDependencies, 
                                    closestDependenciesCache, 
                                    resolvedCommandGroupMembership,
                                    commandGroupMembership,
                                    cycleDetector);
                            }
                            else
                            {
                                projectDependencies.Add(GetGroupProjectIfDefined(resolvedCommandGroupMembership, value.JavaScriptProject));
                            }
                        }
                        else
                        {
                            // Otherwise add a dependency on all the package dependencies with the specified command
                            var resolvedProject = resolvedProjects[(javaScriptProject.Name, command)];
                            var packageDependencies = resolvedProject.deserializedJavaScriptProject.Dependencies;

                            foreach (string packageDependencyName in packageDependencies)
                            {
                                // Let's validate dependencies point to existing packages. This should always
                                // be the case when talking to well-known coordinators, but it might not be
                                // the case for user-specified custom build graphs
                                if (!deserializedProjectsByName.ContainsKey(packageDependencyName))
                                {
                                    return new JavaScriptProjectSchedulingFailure(resolvedProject.JavaScriptProject,
                                        $"Specified dependency '{packageDependencyName}' does not exist.");
                                }

                                // If it is not defined try to find its closest transitive dependencies
                                if (!resolvedProjects.TryGetValue((packageDependencyName, dependency.Command), out var value))
                                {
                                    AddClosestPresentDependencies(
                                        packageDependencyName, 
                                        dependency.Command, 
                                        resolvedProjects, 
                                        deserializedProjectsByName, 
                                        projectDependencies, 
                                        closestDependenciesCache, 
                                        resolvedCommandGroupMembership,
                                        commandGroupMembership,
                                        cycleDetector);
                                }
                                else
                                {
                                    projectDependencies.Add(GetGroupProjectIfDefined(resolvedCommandGroupMembership, value.JavaScriptProject));
                                }
                            }
                        }
                    }

                    javaScriptProject.SetDependencies(projectDependencies.ToList());
                }
            }

            return new JavaScriptGraph<TGraphConfiguration>(
                new List<JavaScriptProject>(resultingProjects),
                flattenedJavaScriptGraph.Configuration);
        }

        private IReadOnlyList<IJavaScriptCommandDependency> GetCommandDependencies(string command, Dictionary<string, string> commandGroupMembership)
        {
            // If the command is part of a group command, then it inherits the dependencies of it
            if (commandGroupMembership.TryGetValue(command, out string commandGroup))
            {
                command = commandGroup;
            }

            if (!ComputedCommands.TryGetValue(command, out IReadOnlyList<IJavaScriptCommandDependency> dependencies))
            {
                Contract.Assume(false, $"The command {command} is expected to be part of the computed commands");
            }

            return dependencies;
        }

        private Dictionary<string, string> BuildCommandGroupMembership()
        {

            var commandGroupMembership = new Dictionary<string, string>();
            foreach (var kvp in m_commandGroups)
            {
                string groupName = kvp.Key;
                foreach (string command in kvp.Value)
                {
                    // If a command belongs to more than one group, that will be spotted below, here we just override it
                    commandGroupMembership[command] = groupName;
                }
            }

            return commandGroupMembership;
        }

        private JavaScriptProject CreateGroupProject(string commandName, string projectName, IReadOnlyList<JavaScriptProject> members, DeserializedJavaScriptProject deserializedProject)
        {
            // Source files and output directories are the union of the corresponding ones of each member
            var inputFiles = members.SelectMany(member => member.InputFiles).ToHashSet();
            var inputDirectories = members.SelectMany(member => member.InputDirectories).ToHashSet();
            var outputDirectories = members.SelectMany(member => member.OutputDirectories).ToHashSet();

            // The script sequence is composed from every script command
            string computedScript = ComputeScriptSequence(members.Select(member => member.ScriptCommand));
            
            var projectGroup = new JavaScriptProject(
                projectName, 
                deserializedProject.ProjectFolder, 
                commandName, 
                computedScript, 
                deserializedProject.TempFolder, 
                outputDirectories, 
                inputFiles,
                inputDirectories);

            return projectGroup;
        }

        private static JavaScriptProject GetGroupProjectIfDefined(Dictionary<JavaScriptProject, JavaScriptProject> resolvedCommandGroupMembership, JavaScriptProject project)
        {
            if (resolvedCommandGroupMembership.TryGetValue(project, out JavaScriptProject groupProject))
            {
                return groupProject;
            }

            return project;
        }

        private static string ComputeScriptSequence(IEnumerable<string> scripts) =>
            // Concatenate all scripts command with a &&
            string.Join("&&", scripts.Select(script => $"({script})"));

        private void AddClosestPresentDependencies(
            string projectName,
            string command,
            Dictionary<(string projectName, string command), (JavaScriptProject JavaScriptProject, DeserializedJavaScriptProject deserializedJavaScriptProject)> resolvedProjects,
            Dictionary<string, DeserializedJavaScriptProject> deserializedProjects,
            HashSet<JavaScriptProject> closestDependencies,
            Dictionary<(string name, string command), HashSet<JavaScriptProject>> closestDependenciesCache,
            Dictionary<JavaScriptProject, JavaScriptProject> resolvedCommandGroupMembership,
            Dictionary<string, string> commandGroupMembership,
            HashSet<(string projectName, string command)> cycleDetector)
        {
            // If we are seeing the same project/command being resolved, that means there is a cycle in the project-to-project graph.
            // The cycle will be properly detected and communicated when building the pip graph if present script commands actually realize it.
            // We break the cycle here by just not adding any closest present dependencies for that script command.
            if (cycleDetector.Contains((projectName, command)))
            {
                return;
            }

            // Check the cache to see if we resolved this project/command before
            if (closestDependenciesCache.TryGetValue((projectName, command), out var closestCachedDependencies))
            {
                closestDependencies.AddRange(closestCachedDependencies);
                return;
            }

            closestCachedDependencies = new HashSet<JavaScriptProject>();
            cycleDetector.Add((projectName, command));

            // The assumption is that projectName and command represent an absent project
            // Get all its dependencies to retrieve the 'frontier' of present projects
            var dependencies = GetCommandDependencies(command, commandGroupMembership);
            foreach (var dependency in dependencies)
            {
                if (dependency.IsLocalKind())
                {
                    // If it is a local dependency, check for the presence of the current project name and the dependency command
                    if (!resolvedProjects.TryGetValue((projectName, dependency.Command), out var dependencyProject))
                    {
                        AddClosestPresentDependencies(
                            projectName, 
                            dependency.Command, 
                            resolvedProjects, 
                            deserializedProjects, 
                            closestCachedDependencies, 
                            closestDependenciesCache, 
                            resolvedCommandGroupMembership,
                            commandGroupMembership,
                            cycleDetector);
                    }
                    else
                    {
                        closestCachedDependencies.Add(GetGroupProjectIfDefined(resolvedCommandGroupMembership, dependencyProject.JavaScriptProject));
                    }
                }
                else
                {
                    // If it is a package dependency, check if there is any of those missing
                    IReadOnlyCollection<string> projectDependencyNames = deserializedProjects[projectName].Dependencies;
                    foreach (string projectDependencyName in projectDependencyNames)
                    {
                        if (!resolvedProjects.TryGetValue((projectDependencyName, dependency.Command), out var dependencyProject))
                        {
                            AddClosestPresentDependencies(
                                projectDependencyName, 
                                dependency.Command, 
                                resolvedProjects, 
                                deserializedProjects, 
                                closestCachedDependencies, 
                                closestDependenciesCache, 
                                resolvedCommandGroupMembership,
                                commandGroupMembership,
                                cycleDetector);
                        }
                        else
                        {
                            closestCachedDependencies.Add(GetGroupProjectIfDefined(resolvedCommandGroupMembership, dependencyProject.JavaScriptProject));
                        }
                    }
                }
            }

            // Populate the result and update the cache
            closestDependencies.AddRange(closestCachedDependencies);
            closestDependenciesCache[(projectName, command)] = closestCachedDependencies;
            cycleDetector.Remove((projectName, command));
        }

        /// <summary>
        /// Resolves a JavaScript graph without execution semantics. Assumes each JS project has at most one script command.
        /// </summary>
        protected Possible<JavaScriptGraph<TGraphConfiguration>> ResolveGraphWithoutExecutionSemantics(GenericJavaScriptGraph<DeserializedJavaScriptProject, TGraphConfiguration> flattenedJavaScriptGraph)
        {
            // Compute the inverse relationship between commands and their groups
            var commandGroupMembership = BuildCommandGroupMembership();

            // Get the list of all regular commands
            var allFlattenedCommands = ComputedCommands.Keys.Where(command => !m_commandGroups.ContainsKey(command)).Union(m_commandGroups.Values.SelectMany(commandMembers => commandMembers)).ToList();

            // Here we put all resolved projects (including the ones belonging to a group command)
            var resolvedProjects = new Dictionary<string, (JavaScriptProject JavaScriptProject, DeserializedJavaScriptProject deserializedJavaScriptProject)>(flattenedJavaScriptGraph.Projects.Count);
            // Here we put the resolved projects that belong to a given group
            var resolvedGroups = new MultiValueDictionary<(string projectName, string commandGroup), JavaScriptProject>(m_commandGroups.Keys.Count());
            // This is the final list of projects
            var resultingProjects = new List<JavaScriptProject>();

            foreach (var deserializedProject in flattenedJavaScriptGraph.Projects)
            {
                Contract.Assert(deserializedProject.AvailableScriptCommands.Count == 1, "If the graph builder tool is already adding the execution semantics, each deserialized project should only have one script command");
                string command = deserializedProject.AvailableScriptCommands.Keys.Single();

                if (!TryValidateAndCreateProject(command, deserializedProject, out JavaScriptProject javaScriptProject, out Failure failure))
                {
                    return failure;
                }

                // Here we check for duplicate projects
                if (resolvedProjects.ContainsKey((javaScriptProject.Name)))
                {
                    return new JavaScriptProjectSchedulingFailure(javaScriptProject,
                        $"Duplicate project name '{javaScriptProject.Name}' defined in '{javaScriptProject.ProjectFolder.ToString(Context.PathTable)}' " +
                        $"and '{resolvedProjects[javaScriptProject.Name].JavaScriptProject.ProjectFolder.ToString(Context.PathTable)}' for script command '{command}'");
                }

                // If the command does not belong to any group, we know it is already part of the final list of projects
                if (!commandGroupMembership.TryGetValue(command, out string commandGroup))
                {
                    resultingProjects.Add(javaScriptProject);
                }
                else
                {
                    // Otherwise, group it so we can inspect it later
                    resolvedGroups.Add((javaScriptProject.Name, commandGroup), javaScriptProject);
                }

                resolvedProjects.Add(javaScriptProject.Name, (javaScriptProject, deserializedProject));
            }

            // Here we build a map between each group member to its group project
            var resolvedCommandGroupMembership = new Dictionary<JavaScriptProject, JavaScriptProject>();

            // Now add groups commands
            foreach (var kvp in resolvedGroups)
            {
                string commandName = kvp.Key.commandGroup;
                string projectName = kvp.Key.projectName;
                IReadOnlyList<JavaScriptProject> members = kvp.Value;

                Contract.Assert(members.Count > 0);

                var deserializedProject = resolvedProjects[projectName].deserializedJavaScriptProject;
                var groupProject = CreateGroupProject(commandName, projectName, members, deserializedProject);

                // Here we check for duplicate projects
                if (resolvedProjects.ContainsKey(groupProject.Name))
                {
                    return new JavaScriptProjectSchedulingFailure(groupProject,
                        $"Duplicate project name '{groupProject.Name}' defined in '{groupProject.ProjectFolder.ToString(Context.PathTable)}' " +
                        $"and '{resolvedProjects[groupProject.Name].JavaScriptProject.ProjectFolder.ToString(Context.PathTable)}' for script command '{commandName}'");
                }

                resolvedProjects.Add(groupProject.Name, (groupProject, deserializedProject));
                // This project group should be part of the final list of projects
                resultingProjects.Add(groupProject);

                // Update the resolved membership so each member points to its group project
                foreach (var member in members)
                {
                    resolvedCommandGroupMembership[member] = groupProject;
                }
            }

            // Now resolve dependencies
            foreach (var kvp in resolvedProjects)
            {
                JavaScriptProject javaScriptProject = kvp.Value.JavaScriptProject;
                DeserializedJavaScriptProject deserializedProject = kvp.Value.deserializedJavaScriptProject;

                var projectDependencies = new List<JavaScriptProject>();
                foreach (string dependency in deserializedProject.Dependencies)
                {
                    // Some providers (e.g. Lage) list dependencies to nodes that don't actually exist. This is typically when, for example, project A depends on B, A has a 'build' verb
                    // but B doesn't. B#build will be listed as a dependency for A#build, but B#build won't be defined as a node in the graph. The dependency in the case should be ignored.
                    if (!resolvedProjects.TryGetValue(dependency, out var value))
                    {
                        Tracing.Logger.Log.IgnoredDependency(Context.LoggingContext, ResolverSettings.Location(Context.PathTable), dependency, deserializedProject.Name);
                        continue;
                    }

                    projectDependencies.Add(GetGroupProjectIfDefined(resolvedCommandGroupMembership, value.JavaScriptProject));
                }

                javaScriptProject.SetDependencies(projectDependencies);
            }

            return new JavaScriptGraph<TGraphConfiguration>(
                new List<JavaScriptProject>(resultingProjects),
                flattenedJavaScriptGraph.Configuration);
        }

        /// <summary>
        /// The owning resolver calls this to notify evaluation is done
        /// </summary>
        public void NotifyEvaluationFinished() => m_configEvaluationContext?.Dispose();

        /// <summary>
        /// Evaluates 'customScripts' field for a given package name and relative location
        /// </summary>
        /// <remarks>
        /// The returned dictionary can be null, matching the 'undefined' case on DScript side. This is the indication
        /// the callback has no saying for this particular package.
        /// </remarks>
        protected Possible<IReadOnlyDictionary<string, string>> ResolveCustomScripts(string packageName, RelativePath location)
        {
            Contract.RequiresNotNull(ResolverSettings.CustomScripts);
            Contract.RequiresNotNullOrEmpty(packageName);
            Contract.Requires(location.IsValid);

            // This is enforced by the type checker
            var closure = (Closure)ResolverSettings.CustomScripts;

            // Create a stack frame and push the package name and location as arguments
            using (var args = EvaluationStackFrame.Create(closure.Function, CollectionUtilities.EmptyArray<EvaluationResult>()))
            using (m_configEvaluationContext.RootContext.PushStackEntry(
                closure.Function, 
                closure.Env, 
                closure.Env.CurrentFileModule, 
                TypeScript.Net.Utilities.LineInfo.FromLineAndPosition(ResolverSettings.Location.Line, ResolverSettings.Location.Position), 
                args))
            {
                args.SetArgument(0, EvaluationResult.Create(packageName));
                args.SetArgument(1, EvaluationResult.Create(location));

                // Invoke the callback and interpret the result
                var closureEvaluation = m_configEvaluationContext.RootContext.InvokeClosure(closure, args);

                if (closureEvaluation.IsErrorValue)
                {
                    return LogCallbackErrorAndCreateFailure(
                        closure, 
                        packageName, 
                        "Callback produced an evaluation error. Details should have been logged already.");
                }

                // If the result is undefined, that's the indication the callback is not interested in picking up this particular project.
                // Null is returned in that case.
                if (closureEvaluation.IsUndefined)
                {
                    return new Possible<IReadOnlyDictionary<string, string>>((IReadOnlyDictionary<string, string>)null);
                }

                // The callback returned a File, meaning we should look at the 'scripts' section of it
                if (closureEvaluation.Value is FileArtifact pathToJson)
                {
                    if (!pathToJson.IsValid)
                    {
                        return LogCallbackErrorAndCreateFailure(
                            closure,
                            packageName,
                            "Callback returned an invalid path to a JSON file.");
                    }

                    return GetScriptsFromPackageJson(pathToJson, closure.Function.Location.ToLocation(closure.Env.Path.ToString(Context.PathTable)));
                }

                // Otherwise, the callback returned a map representing the custom scripts
                var orderedMap = (OrderedMap)closureEvaluation.Value;
                var result = new Dictionary<string, string>(orderedMap.Count);
                foreach (var elem in orderedMap)
                {
                    string scriptName = elem.Key.IsUndefined? null : (string)elem.Key.Value;
                    if (string.IsNullOrEmpty(scriptName))
                    {
                        return LogCallbackErrorAndCreateFailure(
                            closure,
                            packageName,
                            "Script name is not defined.");
                    }

                    EvaluationResult scriptEvaluation = elem.Value;
                    if (scriptEvaluation.IsUndefined)
                    {
                        return LogCallbackErrorAndCreateFailure(
                            closure,
                            packageName,
                            "Script value is not defined.");
                    }

                    string script = null;
                    try
                    {
                        script = ConfigurationConverter.CreatePipDataFromFileContent(Context, scriptEvaluation.Value, string.Empty).ToString(Context.PathTable);
                    }
                    catch(ConvertException e)
                    {
                        return LogCallbackErrorAndCreateFailure(
                            closure,
                            packageName,
                            e.Message);
                    }

                    var success = result.TryAdd(scriptName, script);
                    Contract.Assert(success);
                }

                return result;
            }
        }

        private JavaScriptGraphConstructionFailure LogCallbackErrorAndCreateFailure(Closure closure, string packageName, string message)
        {
            var functionLocation = closure.Function.Location.ToLocation(closure.Env.Path.ToString(Context.PathTable));
            Tracing.Logger.Log.CustomScriptsFailure(
                Context.LoggingContext,
                functionLocation,
                packageName,
                message);

            return new JavaScriptGraphConstructionFailure(ResolverSettings, Context.PathTable);
        }

        /// <summary>
        /// Retrieves the 'scripts' section of a JSON file
        /// </summary>
        protected Possible<IReadOnlyDictionary<string, string>> GetScriptsFromPackageJson(AbsolutePath absolutePath, Location provenance)
        {
            JsonSerializer serializer = ConstructProjectGraphSerializer(JsonSerializerSettings);

            try
            {
                if (!Host.Engine.TryGetFrontEndFile(absolutePath, Name, out var stream))
                {
                    Tracing.Logger.Log.CannotLoadScriptsFromJsonFile(
                        Context.LoggingContext,
                        provenance,
                        absolutePath.ToString(Context.PathTable),
                        "Couldn't open file.");
                    return new JavaScriptGraphConstructionFailure(ResolverSettings, Context.PathTable);
                }

                using (stream)
                using (var sr = new StreamReader(stream))
                using (var reader = new JsonTextReader(sr))
                {
                    var jobject = serializer.Deserialize<JObject>(reader);
                    var scripts = jobject["scripts"];
                    if (scripts == null)
                    {
                        Tracing.Logger.Log.CannotLoadScriptsFromJsonFile(
                        Context.LoggingContext,
                        provenance,
                        absolutePath.ToString(Context.PathTable),
                        "Section 'scripts' not found in JSON file.");
                        
                        return new JavaScriptGraphConstructionFailure(ResolverSettings, Context.PathTable);
                    }

                    var result = scripts.ToObject<IReadOnlyDictionary<string, string>>();

                    // Validate all script names are not empty
                    if (result.Any(kvp => string.IsNullOrEmpty(kvp.Key)))
                    {
                        Tracing.Logger.Log.CannotLoadScriptsFromJsonFile(
                            Context.LoggingContext,
                            provenance,
                            absolutePath.ToString(Context.PathTable),
                            "Script name is not defined.");

                        return new JavaScriptGraphConstructionFailure(ResolverSettings, Context.PathTable);
                    }

                    return new Possible<IReadOnlyDictionary<string, string>>(result);
                }
            }
            catch (Exception e) when (e is IOException || e is JsonReaderException || e is BuildXLException)
            {
                Tracing.Logger.Log.CannotLoadScriptsFromJsonFile(
                        Context.LoggingContext,
                        provenance,
                        absolutePath.ToString(Context.PathTable),
                        e.Message);
                return new JavaScriptGraphConstructionFailure(ResolverSettings, Context.PathTable);
            }
        }

        private bool TryValidateAndCreateProject(
            string command, 
            DeserializedJavaScriptProject deserializedProject, 
            out JavaScriptProject javaScriptProject,
            out Failure failure)
        {
            javaScriptProject = JavaScriptProject.FromDeserializedProject(
                command, 
                deserializedProject.AvailableScriptCommands[command], 
                deserializedProject);

            if (!ValidateDeserializedProject(javaScriptProject, out string reason))
            {
                Tracing.Logger.Log.ProjectGraphConstructionError(
                    Context.LoggingContext,
                    ResolverSettings.Location(Context.PathTable),
                    $"The project '{deserializedProject.Name}' defined in '{deserializedProject.ProjectFolder.ToString(Context.PathTable)}' is invalid. {reason}");

                failure =  new JavaScriptGraphConstructionFailure(ResolverSettings, Context.PathTable);
                return false;
            }

            failure = null;

            return true;
        }

        private bool ValidateDeserializedProject(JavaScriptProject project, out string failure)
        {
            // Check the information that comes from the Bxl configuration file
            if (project.OutputDirectories.Any(path => !path.IsValid))
            {
                failure = $"Specified output directory in '{project.ProjectFolder.Combine(Context.PathTable, BxlConfigurationFilename).ToString(Context.PathTable)}' is invalid.";
                return false;
            }

            if (project.InputFiles.Any(path => !path.IsValid))
            {
                failure = $"Specified source file in '{project.ProjectFolder.Combine(Context.PathTable, BxlConfigurationFilename).ToString(Context.PathTable)}' is invalid.";
                return false;
            }

            failure = string.Empty;
            return true;
        }
    }
}
