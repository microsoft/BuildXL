// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.FrontEnd.Rush.ProjectGraph;
using BuildXL.FrontEnd.Utilities;
using BuildXL.FrontEnd.Utilities.GenericProjectGraphResolver;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Native.IO;
using BuildXL.Processes;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
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

        /// <inheritdoc/>
        public RushWorkspaceResolver()
        {
            Name = RushResolverName;
        }

        /// <inheritdoc/>
        public override string Kind => KnownResolverKind.RushResolverKind;

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
                // In case of a cancellation, the tool may have exited with a non-zero
                // code, but that's expected
                if (!m_context.CancellationToken.IsCancellationRequested)
                {
                    // This should never happen! Report the standard error and exit gracefully
                    Tracing.Logger.Log.GraphConstructionInternalError(
                        m_context.LoggingContext,
                        m_resolverSettings.Location(m_context.PathTable),
                        standardError);
                }

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
                var flattenedRushGraph = serializer.Deserialize<GenericRushGraph<GenericRushProject<string>>>(reader);

                RushGraph graph = ResolveDependencies(flattenedRushGraph);

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

        private RushGraph ResolveDependencies(GenericRushGraph<GenericRushProject<string>> flattenedRushGraph)
        {
            var resolvedProjects = new Dictionary<string, RushProject>(flattenedRushGraph.Projects.Count);
            
            // Add all unresolved projects first
            foreach (var flattenedProject in flattenedRushGraph.Projects)
            {
                var rushProject = RushProject.FromGenericRushProject(flattenedProject);
                resolvedProjects.Add(flattenedProject.Name, rushProject);
            }

            // Now resolve dependencies
            foreach (var flattenedProject in flattenedRushGraph.Projects)
            {
                var resolvedProject = resolvedProjects[flattenedProject.Name];
                resolvedProject.SetDependencies(flattenedProject.Dependencies.Select(name => resolvedProjects[name]).ToReadOnlyArray());
            }

            return new RushGraph(resolvedProjects.Values);
        }
    }
}
