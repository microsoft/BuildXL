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
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Utilities.Instrumentation.Common;
using Newtonsoft.Json;
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
        /// Keep in sync with the BuildXL deployment spec that places the tool
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

        /// <inheritdoc/>
        public RushWorkspaceResolver()
        {
            Name = RushResolverName;
        }

        /// <inheritdoc/>
        public override string Kind => KnownResolverKind.RushResolverKind;

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
                    ((IRushResolverSettings)resolverSettings).Commands,
                    out IReadOnlyDictionary<string, IReadOnlyList<IRushCommandDependency>> computedCommands))
            {
                // Error has been logged
                return false;
            }

            m_computedCommands = computedCommands;

            return true;
        }

        /// <summary>
        /// Creates an empty source file for now
        /// </summary>
        protected override SourceFile DoCreateSourceFile(AbsolutePath path)
        {
            return SourceFile.Create(path.ToString(m_context.PathTable));
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

            Possible<RushGraph> maybeRushGraph = await ComputeBuildGraphAsync(outputFile, buildParameters);

            if (!maybeRushGraph.Succeeded)
            {
                // A more specific error has been logged already
                return maybeRushGraph.Failure;
            }

            var rushGraph = maybeRushGraph.Result;

            if (m_resolverSettings.KeepProjectGraphFile != true)
            {
                DeleteGraphBuilderRelatedFiles(outputFile);
            }
            else
            {
                // Graph-related files are requested to be left on disk. Let's print a message with their location.
                Tracing.Logger.Log.GraphBuilderFilesAreNotRemoved(m_context.LoggingContext, outputFile.ToString(m_context.PathTable));
            }

            // The module contains all project files that are part of the graph
            var projectFiles = new HashSet<AbsolutePath>();
            foreach (RushProject project in rushGraph.Projects)
            {
                projectFiles.Add(project.ProjectPath(m_context.PathTable));
            }

            var moduleDescriptor = ModuleDescriptor.CreateWithUniqueId(m_context.StringTable, m_resolverSettings.ModuleName, this);
            var moduleDefinition = ModuleDefinition.CreateModuleDefinitionWithImplicitReferences(
                moduleDescriptor,
                m_resolverSettings.Root,
                m_resolverSettings.File,
                projectFiles,
                allowedModuleDependencies: null, // no module policies
                cyclicalFriendModules: null); // no whitelist of cycles

            return new RushGraphResult(rushGraph, moduleDefinition);
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

        private async Task<Possible<RushGraph>> ComputeBuildGraphAsync(
            AbsolutePath outputFile,
            BuildParameters.IBuildParameters buildParameters)
        {
            SandboxedProcessResult result = await RunRushGraphBuilderAsync(outputFile, buildParameters);

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

                return graph;
            }
        }

        private Task<SandboxedProcessResult> RunRushGraphBuilderAsync(
            AbsolutePath outputFile,
            BuildParameters.IBuildParameters buildParameters)
        {
            AbsolutePath toolPath = m_configuration.Layout.BuildEngineDirectory.Combine(m_context.PathTable, RelativePathToGraphConstructionTool);
            string outputDirectory = outputFile.GetParent(m_context.PathTable).ToString(m_context.PathTable);
            
            // We always use cmd.exe as the tool so if the node.exe location is not provided we can just pass 'node.exe' and let PATH do the work.
            var cmdExeArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(m_context.PathTable, Environment.GetEnvironmentVariable("COMSPEC")));
            string nodeExe = m_resolverSettings.NodeExeLocation.HasValue ?
                m_resolverSettings.NodeExeLocation.Value.Path.ToString(m_context.PathTable) :
                "node.exe";
            string pathToRushJson = m_resolverSettings.Root.Combine(m_context.PathTable, "rush.json").ToString(m_context.PathTable);

            // TODO: add qualifier support.
            // The graph construction tool expects: <path-to-rush.json> <path-to-output-graph> [<debug|release>]
            string toolArguments = $@"/C """"{nodeExe}"" ""{toolPath.ToString(m_context.PathTable)}"" ""{pathToRushJson}"" ""{outputFile.ToString(m_context.PathTable)}"" debug""";

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
                foreach (var flattenedProject in flattenedRushGraph.Projects)
                {
                    // If the requested script is not available on the project, log and skip it
                    if (!flattenedProject.AvailableScriptCommands.ContainsKey(command))
                    {
                        Tracing.Logger.Log.ProjectIsIgnoredScriptIsMissing(
                                    m_context.LoggingContext, Location.FromFile(flattenedProject.ProjectFolder.ToString(m_context.PathTable)), flattenedProject.Name, command);
                        continue;
                    }

                    var rushProject = RushProject.FromDeserializedProject(command, flattenedProject.AvailableScriptCommands[command], flattenedProject);

                    // Here we check for duplicate projects
                    if (resolvedProjects.ContainsKey((rushProject.Name, command)))
                    {
                        return new RushProjectSchedulingFailure(rushProject,
                            $"Duplicate project name '{rushProject.Name}' defined in '{rushProject.ProjectFolder.ToString(m_context.PathTable)}' " +
                            $"and '{resolvedProjects[(rushProject.Name, command)].rushProject.ProjectFolder.ToString(m_context.PathTable)}' for script command '{command}'");
                    }
                    
                    resolvedProjects.Add((rushProject.Name, command), (rushProject, flattenedProject));
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

            return new RushGraph(new List<RushProject>(resolvedProjects.Values.Select(kvp => kvp.rushProject)));
        }
    }
}
