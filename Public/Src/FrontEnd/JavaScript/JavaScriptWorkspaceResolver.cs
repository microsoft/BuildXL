// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.FrontEnd.JavaScript.ProjectGraph;
using BuildXL.FrontEnd.Script.RuntimeModel.AstBridge;
using BuildXL.FrontEnd.Script.Util;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Utilities;
using BuildXL.FrontEnd.Utilities.GenericProjectGraphResolver;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Native.IO;
using BuildXL.Processes;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Utilities.Instrumentation.Common;
using Newtonsoft.Json;
using TypeScript.Net.DScript;
using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.JavaScript
{
    /// <summary>
    /// Workspace resolver for JavaScript based resolvers
    /// </summary>
    /// <remarks>
    /// Extenders should define where the bxl graph construction tool is located and the parameters to pass to it
    /// </remarks>
    public abstract class JavaScriptWorkspaceResolver<TGraphConfiguration, TResolverSettings> : ProjectGraphWorkspaceResolverBase<JavaScriptGraphResult<TGraphConfiguration>, TResolverSettings> 
        where TGraphConfiguration: class 
        where TResolverSettings : class, IJavaScriptResolverSettings
    {
        /// <summary>
        /// Name of the Bxl configuration file that can be dropped at the root of a JavaScript project
        /// </summary>
        internal const string BxlConfigurationFilename = "bxlconfig.json";

        /// <summary>
        /// The BuildXL tool relative location that is used to construct the graph 
        /// </summary>
        protected abstract RelativePath RelativePathToGraphConstructionTool { get; }

        /// <summary>
        /// Preserves references for objects (so project references get correctly reconstructed), adds indentation for easier 
        /// debugging (at the cost of a slightly higher serialization size) and includes nulls explicitly
        /// </summary>
        private static readonly JsonSerializerSettings s_jsonSerializerSettings = new JsonSerializerSettings
        {
            PreserveReferencesHandling = PreserveReferencesHandling.None,
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Include
        };
        
        private IReadOnlyDictionary<string, IReadOnlyList<IJavaScriptCommandDependency>> m_computedCommands;
        private IReadOnlyDictionary<string, IReadOnlyList<string>> m_commandGroups;

        private FullSymbol AllProjectsSymbol { get; set; }

        /// <inheritdoc/>
        public JavaScriptWorkspaceResolver(string resolverKind)
        {
            Name = resolverKind;
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

        /// <inheritdoc/>
        public override bool TryInitialize(FrontEndHost host, FrontEndContext context, IConfiguration configuration, IResolverSettings resolverSettings)
        {
            var success = base.TryInitialize(host, context, configuration, resolverSettings);
            
            if (!success)
            {
                return false;
            }

            if (!JavaScriptCommandsInterpreter.TryComputeAndValidateCommands(
                    m_context.LoggingContext,
                    resolverSettings.Location(m_context.PathTable),
                    ((IJavaScriptResolverSettings)resolverSettings).Execute,
                    out IReadOnlyDictionary<string, IReadOnlyList<IJavaScriptCommandDependency>> computedCommands,
                    out IReadOnlyDictionary<string, IReadOnlyList<string>> commandGroups))
            {
                // Error has been logged
                return false;
            }

            m_computedCommands = computedCommands;
            m_commandGroups = commandGroups;

            ExportsFile = m_resolverSettings.Root.Combine(m_context.PathTable, "exports.dsc");
            AllProjectsSymbol = FullSymbol.Create(m_context.SymbolTable, "all");

            return true;
        }

        /// <summary>
        /// Collects all JavaScript projects that need to be part of specified exports
        /// </summary>
        private bool TryResolveExports(
            IReadOnlyCollection<JavaScriptProject> projects,
            IReadOnlyCollection<DeserializedJavaScriptProject> deserializedProjects,
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

            if (m_resolverSettings.Exports == null)
            {
                return true;
            }

            foreach (var export in m_resolverSettings.Exports)
            {
                // The export symbol cannot be one of the reserved ones (which for now is just one: 'all')
                if (export.SymbolName == AllProjectsSymbol)
                {
                    Tracing.Logger.Log.SpecifiedExportIsAReservedName(
                                        m_context.LoggingContext,
                                        m_resolverSettings.Location(m_context.PathTable),
                                        AllProjectsSymbol.ToString(m_context.SymbolTable));
                    return false;
                }

                var projectsForSymbol = new List<JavaScriptProject>();

                foreach (var project in export.Content)
                {
                    // The project outputs can be a plain string, meaning all build commands, or an IJavaScriptProjectOutputs
                    IJavaScriptProjectOutputs projectOutputs;
                    object projectValue = project.GetValue();
                    if (projectValue is IJavaScriptProjectOutputs outputs)
                    {
                        projectOutputs = outputs;
                    }
                    else
                    {
                        projectOutputs = new JavaScriptProjectOutputs { PackageName = (string)projectValue, Commands = null };
                    }

                    // Let's retrieve the deserialized project. If it is not there, that's an error, since the package name
                    // does not exist at all (regardless of any build filter)
                    if (nameToDeserializedProjects.TryGetValue(projectOutputs.PackageName, out var deserializedJavaScriptProject))
                    {
                        // If no commands were specified, add all available scripts that can be scheduled
                        if (projectOutputs.Commands == null)
                        {
                            foreach (string command in deserializedJavaScriptProject.AvailableScriptCommands.Keys)
                            {
                                // If the project/command is there, add it to the export symbol if it can be scheduled
                                if (nameAndCommandToProjects.TryGetValue((projectOutputs.PackageName, command), out JavaScriptProject javaScriptProject) && javaScriptProject.CanBeScheduled())
                                {
                                    projectsForSymbol.Add(javaScriptProject);
                                }
                                else
                                {
                                    // The project/command is not there. This can happen if the corresponding command was
                                    // not requested to be executed. So just log an informational message here
                                    Tracing.Logger.Log.RequestedExportIsNotPresent(
                                        m_context.LoggingContext,
                                        m_resolverSettings.Location(m_context.PathTable),
                                        export.SymbolName.ToString(m_context.SymbolTable),
                                        projectOutputs.PackageName,
                                        command);
                                }
                            }
                        }
                        else
                        {
                            // Otherwise, add just the specified ones
                            foreach (string commandName in projectOutputs.Commands)
                            {
                                if (nameAndCommandToProjects.TryGetValue((projectOutputs.PackageName, commandName), out var javaScriptProject))
                                {
                                    projectsForSymbol.Add(javaScriptProject);
                                }
                                else
                                {
                                    Tracing.Logger.Log.SpecifiedCommandForExportDoesNotExist(
                                        m_context.LoggingContext,
                                        m_resolverSettings.Location(m_context.PathTable),
                                        export.SymbolName.ToString(m_context.SymbolTable),
                                        projectOutputs.PackageName,
                                        commandName,
                                        string.Join(", ", deserializedJavaScriptProject.AvailableScriptCommands.Keys.Select(command => $"'{command}'")));

                                    return false;
                                }
                            }
                        }
                    }
                    else
                    {
                        Tracing.Logger.Log.SpecifiedPackageForExportDoesNotExist(
                                        m_context.LoggingContext,
                                        m_resolverSettings.Location(m_context.PathTable),
                                        export.SymbolName.ToString(m_context.SymbolTable),
                                        projectOutputs.PackageName);

                        return false;
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
            var sourceFile = SourceFile.Create(path.ToString(m_context.PathTable));
            
            // We consider all files to be DScript files, even though the extension might not be '.dsc'
            // And additionally, we consider them to be external modules, so exported stuff is interpreted appropriately
            // by the type checker
            sourceFile.OverrideIsScriptFile = true;
            sourceFile.ExternalModuleIndicator = sourceFile;

            // If we need to generate the export file, add all specified export symbols
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
                        export.FullSymbol.ToString(m_context.SymbolTable),
                        new ArrayTypeNode { ElementType = typeReference },
                        pos,
                        pos + 1);
                    pos += 2;
                }

                // As if all declarations happened on the same line
                sourceFile.SetLineMap(new[] { 0, Math.Max(pos - 2, 2) });
            }

            return sourceFile;
        }
        
        /// <inheritdoc/>
        protected override Task<Possible<JavaScriptGraphResult<TGraphConfiguration>>> TryComputeBuildGraphAsync()
        {
            BuildParameters.IBuildParameters buildParameters = RetrieveBuildParameters();

            return TryComputeBuildGraphAsync(buildParameters);
        }

        private async Task<Possible<JavaScriptGraphResult<TGraphConfiguration>>> TryComputeBuildGraphAsync(BuildParameters.IBuildParameters buildParameters)
        {
            // We create a unique output file on the obj folder associated with the current front end, and using a GUID as the file name
            AbsolutePath outputDirectory = m_host.GetFolderForFrontEnd(Name);
            AbsolutePath outputFile = outputDirectory.Combine(m_context.PathTable, Guid.NewGuid().ToString());

            // Make sure the directories are there
            FileUtilities.CreateDirectory(outputDirectory.ToString(m_context.PathTable));

            Possible<(JavaScriptGraph<TGraphConfiguration> graph, GenericJavaScriptGraph<DeserializedJavaScriptProject, TGraphConfiguration> flattenedGraph)> maybeResult = await ComputeBuildGraphAsync(outputFile, buildParameters);

            if (!maybeResult.Succeeded)
            {
                // A more specific error has been logged already
                return maybeResult.Failure;
            }

            var javaScriptGraph = maybeResult.Result.graph;
            var flattenedGraph = maybeResult.Result.flattenedGraph;

            if (m_resolverSettings.KeepProjectGraphFile != true)
            {
                DeleteGraphBuilderRelatedFiles(outputFile);
            }
            else
            {
                // Graph-related files are requested to be left on disk. Let's print a message with their location.
                Tracing.Logger.Log.GraphBuilderFilesAreNotRemoved(m_context.LoggingContext, outputFile.ToString(m_context.PathTable));
            }

            if (!TryResolveExports(javaScriptGraph.Projects, flattenedGraph.Projects, out var exports))
            {
                // Specific error should have been logged
                return new JavaScriptGraphConstructionFailure(m_resolverSettings, m_context.PathTable);
            }

            // The module contains all project files that are part of the graph
            var projectFiles = new HashSet<AbsolutePath>();
            foreach (JavaScriptProject project in javaScriptGraph.Projects)
            {
                projectFiles.Add(project.PackageJsonFile(m_context.PathTable));
            }

            // Add an 'exports' source file at the root of the repo that will contain all top-level exported values
            // Since all the specs are exposed as part of a single module, it actually doesn't matter where
            // all these values are declared, but by defining a fixed source file for it, we keep it deterministic
            projectFiles.Add(ExportsFile);

            var moduleDescriptor = ModuleDescriptor.CreateWithUniqueId(m_context.StringTable, m_resolverSettings.ModuleName, this);
            var moduleDefinition = ModuleDefinition.CreateModuleDefinitionWithImplicitReferences(
                moduleDescriptor,
                m_resolverSettings.Root,
                m_resolverSettings.File,
                projectFiles,
                allowedModuleDependencies: null, // no module policies
                cyclicalFriendModules: null); // no allowlist of cycles

            return new JavaScriptGraphResult<TGraphConfiguration>(javaScriptGraph, moduleDefinition, exports);
        }

        private void DeleteGraphBuilderRelatedFiles(AbsolutePath outputFile)
        {
            // Remove the file with the serialized graph so we leave no garbage behind
            // If there is a problem deleting these file, unlikely to happen (the process that created it should be gone by now), log as a warning and move on, this is not
            // a blocking problem
            try
            {
                FileUtilities.DeleteFile(outputFile.ToString(m_context.PathTable));
            }
            catch (BuildXLException ex)
            {
                Tracing.Logger.Log.CannotDeleteSerializedGraphFile(m_context.LoggingContext, m_resolverSettings.Location(m_context.PathTable), outputFile.ToString(m_context.PathTable), ex.Message);
            }
        }

        /// <summary>
        /// Tries to find the JavaScript-based tool that will be pass as a parameter to the Bxl graph construction tool
        /// </summary>
        /// <remarks>
        /// For example, for Rush this is the location of rush-lib. For Yarn, the location of yarn
        /// </remarks>
        protected abstract bool TryFindGraphBuilderToolLocation(
            TResolverSettings resolverSettings,
            BuildParameters.IBuildParameters buildParameters,
            out AbsolutePath location,
            out string failure);

        private async Task<Possible<(JavaScriptGraph<TGraphConfiguration>, GenericJavaScriptGraph<DeserializedJavaScriptProject, TGraphConfiguration>)>> ComputeBuildGraphAsync(
            AbsolutePath outputFile,
            BuildParameters.IBuildParameters buildParameters)
        {
            // Determine the base location to use for finding the graph construction tool
            if (!TryFindGraphBuilderToolLocation(
                m_resolverSettings, 
                buildParameters, 
                out AbsolutePath foundLocation, 
                out string failure))
            {
                Tracing.Logger.Log.CannotFindGraphBuilderTool(
                    m_context.LoggingContext,
                    m_resolverSettings.Location(m_context.PathTable),
                    failure);

                return new JavaScriptGraphConstructionFailure(m_resolverSettings, m_context.PathTable);
            }

            SandboxedProcessResult result = await RunJavaScriptGraphBuilderAsync(outputFile, buildParameters, foundLocation);

            string standardError = result.StandardError.CreateReader().ReadToEndAsync().GetAwaiter().GetResult();

            if (result.ExitCode != 0)
            {
                Tracing.Logger.Log.ProjectGraphConstructionError(
                    m_context.LoggingContext,
                    m_resolverSettings.Location(m_context.PathTable),
                    standardError);

                return new JavaScriptGraphConstructionFailure(m_resolverSettings, m_context.PathTable);
            }

            // If the tool exited gracefully, but standard error is not empty, that
            // is interpreted as a warning. We propagate that to the BuildXL log
            if (!string.IsNullOrEmpty(standardError))
            {
                Tracing.Logger.Log.GraphConstructionFinishedSuccessfullyButWithWarnings(
                    m_context.LoggingContext,
                    m_resolverSettings.Location(m_context.PathTable),
                    standardError);
            }

            TrackFilesAndEnvironment(result.AllUnexpectedFileAccesses, outputFile.GetParent(m_context.PathTable));

            JsonSerializer serializer = ConstructProjectGraphSerializer(s_jsonSerializerSettings);
            
            using (var sr = new StreamReader(outputFile.ToString(m_context.PathTable)))
            using (var reader = new JsonTextReader(sr))
            {
                var flattenedJavaScriptGraph = serializer.Deserialize<GenericJavaScriptGraph<DeserializedJavaScriptProject, TGraphConfiguration>>(reader);

                Possible<JavaScriptGraph<TGraphConfiguration>> graph = ApplyBxlExecutionSemantics() ? ResolveGraphWithExecutionSemantics(flattenedJavaScriptGraph) : ResolveGraphWithoutExecutionSemantics(flattenedJavaScriptGraph);

                return graph.Then(graph => new Possible<(JavaScriptGraph<TGraphConfiguration>, GenericJavaScriptGraph<DeserializedJavaScriptProject, TGraphConfiguration>)>((graph, flattenedJavaScriptGraph)));
            }
        }

        private Task<SandboxedProcessResult> RunJavaScriptGraphBuilderAsync(
            AbsolutePath outputFile,
            BuildParameters.IBuildParameters buildParameters,
            AbsolutePath toolLocation)
        {
            AbsolutePath toolPath = m_configuration.Layout.BuildEngineDirectory.Combine(m_context.PathTable, RelativePathToGraphConstructionTool);
            string outputDirectory = outputFile.GetParent(m_context.PathTable).ToString(m_context.PathTable);

            // We always use cmd.exe as the tool so if the node.exe location is not provided we can just pass 'node.exe' and let PATH do the work.
            var cmdExeArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(m_context.PathTable, Environment.GetEnvironmentVariable("COMSPEC")));
            string nodeExeLocation = m_resolverSettings.NodeExeLocation.HasValue ?
                m_resolverSettings.NodeExeLocation.Value.Path.ToString(m_context.PathTable) :
                "node.exe";
            var toolArguments = GetGraphConstructionToolArguments(outputFile, toolLocation, toolPath, nodeExeLocation);

            Tracing.Logger.Log.ConstructingGraphScript(m_context.LoggingContext, toolArguments);

            return FrontEndUtilities.RunSandboxedToolAsync(
               m_context,
               cmdExeArtifact.Path.ToString(m_context.PathTable),
               buildStorageDirectory: outputDirectory,
               fileAccessManifest: FrontEndUtilities.GenerateToolFileAccessManifest(m_context, outputFile.GetParent(m_context.PathTable)),
               arguments: toolArguments,
               workingDirectory: m_configuration.Layout.SourceDirectory.ToString(m_context.PathTable),
               description: $"{Name} graph builder",
               buildParameters);
        }

        /// <summary>
        /// Generates the arguments that the Bxl graph construction tool expects
        /// </summary>
        /// <param name="outputFile">The file to write the JSON serialized build graph to</param>
        /// <param name="toolLocation">The location of the tool that actually knows how to generate the graph</param>
        /// <param name="bxlGraphConstructionToolPath">The location of the Bxl graph construction tool</param>
        /// <param name="nodeExeLocation">The location of node.exe</param>
        /// <returns></returns>
        protected abstract string GetGraphConstructionToolArguments(AbsolutePath outputFile, AbsolutePath toolLocation, AbsolutePath bxlGraphConstructionToolPath, string nodeExeLocation);

        /// <summary>
        /// Whether the graph produced by the corresponding graph construction tool needs BuildXL to add execution semantics.
        /// </summary>
        /// <remarks>
        /// If not, bxl will define the graph based on the specification of 'execute'
        /// </remarks>
        protected abstract bool ApplyBxlExecutionSemantics();

        private Possible<JavaScriptGraph<TGraphConfiguration>> ResolveGraphWithExecutionSemantics(GenericJavaScriptGraph<DeserializedJavaScriptProject, TGraphConfiguration> flattenedJavaScriptGraph)
        {
            // The way we deal with command groups is the following. We flatten those into its regular command components and put them together with regular commands. Dependency resolution happens among
            // both group and regular commands. The logic is: if there is a dependency on a command member, then that's a dependency on the containing command group. And the command group dependencies are also the 
            // dependencies of its command members. Command members are not included as part of the final collections of projects, only their corresponding command groups.

            // Compute the inverse relationship between commands and their groups
            var commandGroupMembership = BuildCommandGroupMembership();

            // Get the list of all regular commands
            var allFlattenedCommands = m_computedCommands.Keys.Where(command => !m_commandGroups.ContainsKey(command)).Union(m_commandGroups.Values.SelectMany(commandMembers => commandMembers)).ToList();

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
                    // If the requested script is not available on the project, log and skip it
                    if (!deserializedProject.AvailableScriptCommands.ContainsKey(command))
                    {
                        Tracing.Logger.Log.ProjectIsIgnoredScriptIsMissing(
                                    m_context.LoggingContext, Location.FromFile(deserializedProject.ProjectFolder.ToString(m_context.PathTable)), deserializedProject.Name, command);
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
                            $"Duplicate project name '{javaScriptProject.Name}' defined in '{javaScriptProject.ProjectFolder.ToString(m_context.PathTable)}' " +
                            $"and '{resolvedProjects[(javaScriptProject.Name, command)].JavaScriptProject.ProjectFolder.ToString(m_context.PathTable)}' for script command '{command}'");
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
                        $"Duplicate project name '{groupProject.Name}' defined in '{groupProject.ProjectFolder.ToString(m_context.PathTable)}' " +
                        $"and '{resolvedProjects[(groupProject.Name, commandName)].JavaScriptProject.ProjectFolder.ToString(m_context.PathTable)}' for script command '{commandName}'");
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
                            // If it is not defined verbose log it and try to find its closest transitive dependencies
                            if (!resolvedProjects.TryGetValue((javaScriptProject.Name, dependency.Command), out var value))
                            {
                                Tracing.Logger.Log.DependencyIsIgnoredScriptIsMissing(
                                    m_context.LoggingContext, Location.FromFile(javaScriptProject.ProjectFolder.ToString(m_context.PathTable)), javaScriptProject.Name, javaScriptProject.ScriptCommandName, 
                                    javaScriptProject.Name, dependency.Command);

                                AddClosestPresentDependencies(
                                    javaScriptProject.Name, 
                                    dependency.Command, 
                                    resolvedProjects, 
                                    deserializedProjectsByName, 
                                    projectDependencies, 
                                    closestDependenciesCache, 
                                    resolvedCommandGroupMembership,
                                    commandGroupMembership);
                            }
                            else
                            {
                                projectDependencies.Add(GetGroupProjectIfDefined(resolvedCommandGroupMembership, value.JavaScriptProject));
                            }
                        }
                        else
                        {
                            // Otherwise add a dependency on all the package dependencies with the specified command
                            var packageDependencies = resolvedProjects[(javaScriptProject.Name, command)].deserializedJavaScriptProject.Dependencies;

                            foreach (string packageDependencyName in packageDependencies)
                            {
                                // If it is not defined verbose log it and try to find its closest transitive dependencies
                                if (!resolvedProjects.TryGetValue((packageDependencyName, dependency.Command), out var value))
                                {
                                    Tracing.Logger.Log.DependencyIsIgnoredScriptIsMissing(
                                        m_context.LoggingContext, Location.FromFile(javaScriptProject.ProjectFolder.ToString(m_context.PathTable)), javaScriptProject.Name, javaScriptProject.ScriptCommandName, packageDependencyName, dependency.Command);

                                    AddClosestPresentDependencies(
                                        packageDependencyName, 
                                        dependency.Command, 
                                        resolvedProjects, 
                                        deserializedProjectsByName, 
                                        projectDependencies, 
                                        closestDependenciesCache, 
                                        resolvedCommandGroupMembership,
                                        commandGroupMembership);
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

            if (!m_computedCommands.TryGetValue(command, out IReadOnlyList<IJavaScriptCommandDependency> dependencies))
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
            var sourceFiles = members.SelectMany(member => member.SourceFiles).ToHashSet();
            var outputDirectories = members.SelectMany(member => member.OutputDirectories).ToHashSet();

            // The script sequence is composed from every script command
            string computedScript = ComputeScriptSequence(members.Select(member => member.ScriptCommand));
            
            var projectGroup = new JavaScriptProject(projectName, deserializedProject.ProjectFolder, commandName, computedScript, deserializedProject.TempFolder, outputDirectories, sourceFiles);
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

        private string ComputeScriptSequence(IEnumerable<string> scripts)
        {
            // Concatenate all scripts command with a &&
            return string.Join("&&", scripts.Select(script => $"({script})"));
        }

        private void AddClosestPresentDependencies(
            string projectName,
            string command,
            Dictionary<(string projectName, string command), (JavaScriptProject JavaScriptProject, DeserializedJavaScriptProject deserializedJavaScriptProject)> resolvedProjects,
            Dictionary<string, DeserializedJavaScriptProject> deserializedProjects,
            HashSet<JavaScriptProject> closestDependencies,
            Dictionary<(string name, string command), HashSet<JavaScriptProject>> closestDependenciesCache,
            Dictionary<JavaScriptProject, JavaScriptProject> resolvedCommandGroupMembership,
            Dictionary<string, string> commandGroupMembership)
        {
            // Check the cache to see if we resolved this project/command before
            if (closestDependenciesCache.TryGetValue((projectName, command), out var closestCachedDependencies))
            {
                closestDependencies.AddRange(closestCachedDependencies);
                return;
            }

            closestCachedDependencies = new HashSet<JavaScriptProject>();

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
                            commandGroupMembership);
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
                                commandGroupMembership);
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
        }

        private Possible<JavaScriptGraph<TGraphConfiguration>> ResolveGraphWithoutExecutionSemantics(GenericJavaScriptGraph<DeserializedJavaScriptProject, TGraphConfiguration> flattenedJavaScriptGraph)
        {
            // Compute the inverse relationship between commands and their groups
            var commandGroupMembership = BuildCommandGroupMembership();

            // Get the list of all regular commands
            var allFlattenedCommands = m_computedCommands.Keys.Where(command => !m_commandGroups.ContainsKey(command)).Union(m_commandGroups.Values.SelectMany(commandMembers => commandMembers)).ToList();

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
                        $"Duplicate project name '{javaScriptProject.Name}' defined in '{javaScriptProject.ProjectFolder.ToString(m_context.PathTable)}' " +
                        $"and '{resolvedProjects[javaScriptProject.Name].JavaScriptProject.ProjectFolder.ToString(m_context.PathTable)}' for script command '{command}'");
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
                        $"Duplicate project name '{groupProject.Name}' defined in '{groupProject.ProjectFolder.ToString(m_context.PathTable)}' " +
                        $"and '{resolvedProjects[groupProject.Name].JavaScriptProject.ProjectFolder.ToString(m_context.PathTable)}' for script command '{commandName}'");
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
                    // When the execution semantics is provided by the graph builder tool, it is expected to be complete
                    if (!resolvedProjects.TryGetValue(dependency, out var value))
                    {
                        return new JavaScriptProjectSchedulingFailure(javaScriptProject,
                            $"Project dependency '{dependency}' is missing. Dependency required by '{javaScriptProject.ProjectFolder.ToString(m_context.PathTable)}'");
                    }

                    projectDependencies.Add(GetGroupProjectIfDefined(resolvedCommandGroupMembership, value.JavaScriptProject));
                }

                javaScriptProject.SetDependencies(projectDependencies);
            }

            return new JavaScriptGraph<TGraphConfiguration>(
                new List<JavaScriptProject>(resultingProjects),
                flattenedJavaScriptGraph.Configuration);
        }

        private bool TryValidateAndCreateProject(
            string command, 
            DeserializedJavaScriptProject deserializedProject, 
            out JavaScriptProject javaScriptProject,
            out Failure failure)
        {
            javaScriptProject = JavaScriptProject.FromDeserializedProject(command, deserializedProject.AvailableScriptCommands[command], deserializedProject, m_context.PathTable);

            if (!ValidateDeserializedProject(javaScriptProject, out string reason))
            {
                Tracing.Logger.Log.ProjectGraphConstructionError(
                    m_context.LoggingContext,
                    m_resolverSettings.Location(m_context.PathTable),
                    $"The project '{deserializedProject.Name}' defined in '{deserializedProject.ProjectFolder.ToString(m_context.PathTable)}' is invalid. {reason}");

                failure =  new JavaScriptGraphConstructionFailure(m_resolverSettings, m_context.PathTable);
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
                failure = $"Specified output directory in '{project.ProjectFolder.Combine(m_context.PathTable, BxlConfigurationFilename).ToString(m_context.PathTable)}' is invalid.";
                return false;
            }

            if (project.SourceFiles.Any(path => !path.IsValid))
            {
                failure = $"Specified source file in '{project.ProjectFolder.Combine(m_context.PathTable, BxlConfigurationFilename).ToString(m_context.PathTable)}' is invalid.";
                return false;
            }

            failure = string.Empty;
            return true;
        }
    }
}
