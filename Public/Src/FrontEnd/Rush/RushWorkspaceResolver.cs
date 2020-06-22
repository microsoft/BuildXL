// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.FrontEnd.MsBuild;
using BuildXL.FrontEnd.Rush.ProjectGraph;
using BuildXL.FrontEnd.Script.RuntimeModel.AstBridge;
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

namespace BuildXL.FrontEnd.Rush
{
    /// <summary>
    /// Workspace resolver for Rush
    /// </summary>
    public class RushWorkspaceResolver : ProjectGraphWorkspaceResolverBase<RushGraphResult, RushResolverSettings>
    {
        internal const string RushResolverName = "Rush";

        /// <summary>
        /// Name of the Bxl Rush configuration file that can be dropped at the root of a rush project
        /// </summary>
        /// <remarks>
        /// CODESYNC: Public\Src\Tools\Tool.RushGraphBuilder\src\BuildXLConfigurationReader.ts
        /// </remarks>
        internal const string BxlConfigurationFilename = "bxlconfig.json";

        /// <summary>
        /// CODESYNC: the BuildXL deployment spec that places the tool
        /// </summary>
        private RelativePath RelativePathToGraphConstructionTool => RelativePath.Create(m_context.StringTable, @"tools\RushGraphBuilder\main.js");

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
        
        private IReadOnlyDictionary<string, IReadOnlyList<IRushCommandDependency>> m_computedCommands;

        private FullSymbol AllProjectsSymbol { get; set; }

        /// <inheritdoc/>
        public RushWorkspaceResolver()
        {
            Name = RushResolverName;
        }

        /// <inheritdoc/>
        public override string Kind => KnownResolverKind.RushResolverKind;

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

            if (!RushCommandsInterpreter.TryComputeAndValidateCommands(
                    m_context.LoggingContext,
                    resolverSettings.Location(m_context.PathTable),
                    ((IRushResolverSettings)resolverSettings).Execute,
                    out IReadOnlyDictionary<string, IReadOnlyList<IRushCommandDependency>> computedCommands))
            {
                // Error has been logged
                return false;
            }

            m_computedCommands = computedCommands;

            ExportsFile = m_resolverSettings.Root.Combine(m_context.PathTable, "exports.dsc");
            AllProjectsSymbol = FullSymbol.Create(m_context.SymbolTable, "all");

            return true;
        }

        /// <summary>
        /// Collects all rush projects that need to be part of specified exports
        /// </summary>
        private bool TryResolveExports(
            IReadOnlyCollection<RushProject> projects,
            IReadOnlyCollection<DeserializedRushProject> deserializedProjects,
            out IReadOnlyCollection<ResolvedRushExport> resolvedExports)
        {
            // Build dictionaries to speed up subsequent look-ups
            var nameToDeserializedProjects = deserializedProjects.ToDictionary(kvp => kvp.Name, kvp => kvp);
            var nameAndCommandToProjects = projects.ToDictionary(kvp => (kvp.Name, kvp.ScriptCommandName), kvp => kvp);

            var exports = new List<ResolvedRushExport>();
            resolvedExports = exports;

            // Add a baked-in 'all' symbol, with all the scheduled projects
            exports.Add(new ResolvedRushExport(AllProjectsSymbol,
                nameAndCommandToProjects.Values.Where(rushProject => rushProject.CanBeScheduled()).ToList()));

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

                var projectsForSymbol = new List<RushProject>();

                foreach (var project in export.Content)
                {
                    // The project outputs can be a plain string, meaning all build commands, or an IRushProjectOutputs
                    IRushProjectOutputs projectOutputs;
                    object projectValue = project.GetValue();
                    if (projectValue is IRushProjectOutputs outputs)
                    {
                        projectOutputs = outputs;
                    }
                    else
                    {
                        projectOutputs = new RushProjectOutputs { PackageName = (string)projectValue, Commands = null };
                    }

                    // Let's retrieve the deserialized project. If it is not there, that's an error, since the package name
                    // does not exist at all (regardless of any build filter)
                    if (nameToDeserializedProjects.TryGetValue(projectOutputs.PackageName, out var deserializedRushProject))
                    {
                        // If no commands were specified, add all available scripts that can be scheduled
                        if (projectOutputs.Commands == null)
                        {
                            foreach (string command in deserializedRushProject.AvailableScriptCommands.Keys)
                            {
                                // If the project/command is there, add it to the export symbol if it can be scheduled
                                if (nameAndCommandToProjects.TryGetValue((projectOutputs.PackageName, command), out RushProject rushProject) && rushProject.CanBeScheduled())
                                {
                                    projectsForSymbol.Add(rushProject);
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
                                if (nameAndCommandToProjects.TryGetValue((projectOutputs.PackageName, commandName), out var rushProject))
                                {
                                    projectsForSymbol.Add(rushProject);
                                }
                                else
                                {
                                    Tracing.Logger.Log.SpecifiedCommandForExportDoesNotExist(
                                        m_context.LoggingContext,
                                        m_resolverSettings.Location(m_context.PathTable),
                                        export.SymbolName.ToString(m_context.SymbolTable),
                                        projectOutputs.PackageName,
                                        commandName,
                                        string.Join(", ", deserializedRushProject.AvailableScriptCommands.Keys.Select(command => $"'{command}'")));

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

                exports.Add(new ResolvedRushExport(export.SymbolName, projectsForSymbol));
            }

            return true;
        }

        /// <summary>
        /// Creates empty source files for all rush projects, with the exception of the special 'exports' file, where
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
        protected override Task<Possible<RushGraphResult>> TryComputeBuildGraphAsync()
        {
            BuildParameters.IBuildParameters buildParameters = RetrieveBuildParameters();

            return TryComputeBuildGraphAsync(buildParameters);
        }

        private async Task<Possible<RushGraphResult>> TryComputeBuildGraphAsync(BuildParameters.IBuildParameters buildParameters)
        {
            // We create a unique output file on the obj folder associated with the current front end, and using a GUID as the file name
            AbsolutePath outputDirectory = m_host.GetFolderForFrontEnd(Name);
            AbsolutePath outputFile = outputDirectory.Combine(m_context.PathTable, Guid.NewGuid().ToString());

            // Make sure the directories are there
            FileUtilities.CreateDirectory(outputDirectory.ToString(m_context.PathTable));

            Possible<(RushGraph graph, GenericRushGraph<DeserializedRushProject> flattenedGraph)> maybeResult = await ComputeBuildGraphAsync(outputFile, buildParameters);

            if (!maybeResult.Succeeded)
            {
                // A more specific error has been logged already
                return maybeResult.Failure;
            }

            var rushGraph = maybeResult.Result.graph;
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

            if (!TryResolveExports(rushGraph.Projects, flattenedGraph.Projects, out var exports))
            {
                // Specific error should have been logged
                return new RushGraphConstructionFailure(m_resolverSettings, m_context.PathTable);
            }

            // The module contains all project files that are part of the graph
            var projectFiles = new HashSet<AbsolutePath>();
            foreach (RushProject project in rushGraph.Projects)
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

            return new RushGraphResult(rushGraph, moduleDefinition, exports);
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

        private async Task<Possible<(RushGraph, GenericRushGraph<DeserializedRushProject>)>> ComputeBuildGraphAsync(
            AbsolutePath outputFile,
            BuildParameters.IBuildParameters buildParameters)
        {
            // Determine the base location to use for finding rush-lib
            if (!TryFindRushLibLocation(
                m_resolverSettings.RushLibBaseLocation, 
                buildParameters, 
                out AbsolutePath rushLibBaseLocation, 
                out string failure))
            {
                Tracing.Logger.Log.CannotFindRushLib(
                    m_context.LoggingContext,
                    m_resolverSettings.Location(m_context.PathTable),
                    failure);

                return new RushGraphConstructionFailure(m_resolverSettings, m_context.PathTable);
            }

            // Just verbose log this
            Tracing.Logger.Log.UsingRushLibBaseAt(m_context.LoggingContext, m_resolverSettings.Location(m_context.PathTable), rushLibBaseLocation.ToString(m_context.PathTable));

            SandboxedProcessResult result = await RunRushGraphBuilderAsync(outputFile, buildParameters, rushLibBaseLocation);

            string standardError = result.StandardError.CreateReader().ReadToEndAsync().GetAwaiter().GetResult();

            if (result.ExitCode != 0)
            {
                Tracing.Logger.Log.ProjectGraphConstructionError(
                    m_context.LoggingContext,
                    m_resolverSettings.Location(m_context.PathTable),
                    standardError);

                return new RushGraphConstructionFailure(m_resolverSettings, m_context.PathTable);
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
                var flattenedRushGraph = serializer.Deserialize<GenericRushGraph<DeserializedRushProject>>(reader);

                Possible<RushGraph> graph = ResolveGraph(flattenedRushGraph);

                return graph.Then(graph => new Possible<(RushGraph, GenericRushGraph<DeserializedRushProject>)>((graph, flattenedRushGraph)));
            }
        }

        private bool TryFindRushLibLocation(
            DirectoryArtifact? configuredRushLibBaseLocation,
            BuildParameters.IBuildParameters buildParameters,
            out AbsolutePath finalRushLibBaseLocation, 
            out string failure)
        {
            // If the base location was provided at configuration time, we honor it as is
            if (configuredRushLibBaseLocation.HasValue)
            {
                finalRushLibBaseLocation = configuredRushLibBaseLocation.Value.Path;
                failure = string.Empty;
                return true;
            }

            finalRushLibBaseLocation = AbsolutePath.Invalid;

            // If the location was not provided, let's try to see if Rush is installed, since rush-lib comes as part of it
            // Look in %PATH% (as exposed in build parameters) for rush
            string paths = buildParameters["PATH"];

            AbsolutePath foundPath = AbsolutePath.Invalid;
            foreach (string path in paths.Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries))
            {
                var nonEscapedPath = path.Trim('"');
                // Sometimes PATH is not well-formed, so make sure we can actually recognize an absolute path there
                if (AbsolutePath.TryCreate(m_context.PathTable, nonEscapedPath, out var absolutePath))
                {
                    if (m_host.Engine.FileExists(absolutePath.Combine(m_context.PathTable, "rush")))
                    {
                        foundPath = absolutePath;
                        break;
                    }
                }
            }

            if (!foundPath.IsValid)
            {
                failure = "A location for 'rush-lib' is not explicitly specified, so trying to find a Rush installation to use instead. " +
                    "However, 'rush' doesn't seem to be part of PATH. You can either specify the location explicitly using 'rushLibBaseLocation' field in " +
                    $"the Rush resolver configuration, or make sure 'rush' is part of your PATH. Current PATH is '{paths}'.";
                return false;
            }

            // We found where Rush is located. So rush-lib is a known dependency of it, so should be nested within Rush module
            // Observe that even if that's not the case the final validation will occur under the rush graph builder tool, when
            // the module is tried to be loaded
            failure = string.Empty;
            finalRushLibBaseLocation = foundPath.Combine(m_context.PathTable, 
                RelativePath.Create(m_context.StringTable, "node_modules/@microsoft/rush/node_modules"));
            
            return true;
        }

        private Task<SandboxedProcessResult> RunRushGraphBuilderAsync(
            AbsolutePath outputFile,
            BuildParameters.IBuildParameters buildParameters,
            AbsolutePath rushLibBaseLocation)
        {
            AbsolutePath toolPath = m_configuration.Layout.BuildEngineDirectory.Combine(m_context.PathTable, RelativePathToGraphConstructionTool);
            string outputDirectory = outputFile.GetParent(m_context.PathTable).ToString(m_context.PathTable);
            
            // We always use cmd.exe as the tool so if the node.exe location is not provided we can just pass 'node.exe' and let PATH do the work.
            var cmdExeArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(m_context.PathTable, Environment.GetEnvironmentVariable("COMSPEC")));
            string nodeExe = m_resolverSettings.NodeExeLocation.HasValue ?
                m_resolverSettings.NodeExeLocation.Value.Path.ToString(m_context.PathTable) :
                "node.exe";
            string pathToRushJson = m_resolverSettings.Root.Combine(m_context.PathTable, "rush.json").ToString(m_context.PathTable);

            // The graph construction tool expects: <path-to-rush.json> <path-to-output-graph> <path-to-rush-lib>
            string toolArguments = $@"/C """"{nodeExe}"" ""{toolPath.ToString(m_context.PathTable)}"" ""{pathToRushJson}"" ""{outputFile.ToString(m_context.PathTable)}"" ""{rushLibBaseLocation.ToString(m_context.PathTable)}""";

            return FrontEndUtilities.RunSandboxedToolAsync(
               m_context,
               cmdExeArtifact.Path.ToString(m_context.PathTable),
               buildStorageDirectory: outputDirectory,
               fileAccessManifest: FrontEndUtilities.GenerateToolFileAccessManifest(m_context, outputFile.GetParent(m_context.PathTable)),
               arguments: toolArguments,
               workingDirectory: m_configuration.Layout.SourceDirectory.ToString(m_context.PathTable),
               description: "Rush graph builder",
               buildParameters);
        }

        private Possible<RushGraph> ResolveGraph(GenericRushGraph<DeserializedRushProject> flattenedRushGraph)
        {
            var resolvedProjects = new Dictionary<(string projectName, string command), (RushProject rushProject, DeserializedRushProject deserializedRushProject)>(flattenedRushGraph.Projects.Count * m_computedCommands.Count);

            // Each requested script command defines a Rush project
            foreach (var command in m_computedCommands.Keys)
            {
                // Add all unresolved projects first
                foreach (var deserializedProject in flattenedRushGraph.Projects)
                {
                    // If the requested script is not available on the project, log and skip it
                    if (!deserializedProject.AvailableScriptCommands.ContainsKey(command))
                    {
                        Tracing.Logger.Log.ProjectIsIgnoredScriptIsMissing(
                                    m_context.LoggingContext, Location.FromFile(deserializedProject.ProjectFolder.ToString(m_context.PathTable)), deserializedProject.Name, command);
                        continue;
                    }

                    var rushProject = RushProject.FromDeserializedProject(command, deserializedProject.AvailableScriptCommands[command], deserializedProject, m_context.PathTable);

                    if (!ValidateDeserializedProject(rushProject, out string failure))
                    {
                        Tracing.Logger.Log.ProjectGraphConstructionError(
                            m_context.LoggingContext,
                            m_resolverSettings.Location(m_context.PathTable),
                            $"The project '{deserializedProject.Name}' defined in '{deserializedProject.ProjectFolder.ToString(m_context.PathTable)}' is invalid. {failure}");

                        return new RushGraphConstructionFailure(m_resolverSettings, m_context.PathTable);
                    }

                    // Here we check for duplicate projects
                    if (resolvedProjects.ContainsKey((rushProject.Name, command)))
                    {
                        return new RushProjectSchedulingFailure(rushProject,
                            $"Duplicate project name '{rushProject.Name}' defined in '{rushProject.ProjectFolder.ToString(m_context.PathTable)}' " +
                            $"and '{resolvedProjects[(rushProject.Name, command)].rushProject.ProjectFolder.ToString(m_context.PathTable)}' for script command '{command}'");
                    }
                    
                    resolvedProjects.Add((rushProject.Name, command), (rushProject, deserializedProject));
                }
            }

            // Now resolve dependencies
            foreach (var kvp in resolvedProjects)
            {
                string command = kvp.Key.command;
                RushProject rushProject = kvp.Value.rushProject;
                DeserializedRushProject deserializedProject = kvp.Value.deserializedRushProject;

                if (!m_computedCommands.TryGetValue(command, out IReadOnlyList<IRushCommandDependency> dependencies))
                {
                    Contract.Assume(false, $"The command {command} is expected to be part of the computed commands");
                }

                var projectDependencies = new List<RushProject>();
                foreach (IRushCommandDependency dependency in dependencies)
                {
                    // If it is a local dependency, add a dependency to the same rush project and the specified command
                    if (dependency.IsLocalKind())
                    {
                        // Skip if it is not defined but log
                        if (!resolvedProjects.TryGetValue((rushProject.Name, dependency.Command), out var value))
                        {
                            Tracing.Logger.Log.DependencyIsIgnoredScriptIsMissing(
                                m_context.LoggingContext, Location.FromFile(rushProject.ProjectFolder.ToString(m_context.PathTable)), rushProject.Name, rushProject.ScriptCommandName, rushProject.Name, dependency.Command);
                            continue;
                        }

                        projectDependencies.Add(value.rushProject);
                    }
                    else
                    {
                        // Otherwise add a dependency on all the package dependencies with the specified command
                        var packageDependencies = resolvedProjects[(rushProject.Name, command)].deserializedRushProject.Dependencies;

                        foreach (string packageDependencyName in packageDependencies)
                        {
                            // Skip if it is not defined but log
                            if (!resolvedProjects.TryGetValue((packageDependencyName, dependency.Command), out var value))
                            {
                                Tracing.Logger.Log.DependencyIsIgnoredScriptIsMissing(
                                    m_context.LoggingContext, Location.FromFile(rushProject.ProjectFolder.ToString(m_context.PathTable)), rushProject.Name, rushProject.ScriptCommandName, packageDependencyName, dependency.Command);
                                continue;
                            }

                            projectDependencies.Add(value.rushProject);
                        }
                    }
                }
                
                rushProject.SetDependencies(projectDependencies);
            }

            return new RushGraph(
                new List<RushProject>(resolvedProjects.Values.Select(kvp => kvp.rushProject)), 
                flattenedRushGraph.Configuration);
        }

        private bool ValidateDeserializedProject(RushProject project, out string failure)
        {
            // Check the information that comes from the Bxl Rush configuration file
            // The rest comes from rush-lib, and therefore we shouldn't need to validate again

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
