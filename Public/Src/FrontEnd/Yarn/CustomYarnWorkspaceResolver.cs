// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading.Tasks;
using BuildXL.FrontEnd.JavaScript;
using BuildXL.FrontEnd.JavaScript.ProjectGraph;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.FrontEnd.Yarn.ProjectGraph;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BuildXL.FrontEnd.Yarn
{
    /// <summary>
    /// User customized Workspace resolver following the Yarn schema
    /// </summary>
    /// <remarks>
    /// There is not a graph construction tool available to call, the user provides the project-to-project graph via a custom literal or file
    /// </remarks>
    public class CustomYarnWorkspaceResolver : JavaScriptWorkspaceResolver<YarnConfiguration, ICustomJavaScriptResolverSettings>
    {
        /// <inheritdoc/>
        public CustomYarnWorkspaceResolver() : base(KnownResolverKind.YarnResolverKind)
        {
        }

        /// <summary>
        /// Compute the build graph by reading a user-specified package-to-package graph
        /// </summary>
        protected override Task<Possible<(JavaScriptGraph<YarnConfiguration>, GenericJavaScriptGraph<DeserializedJavaScriptProject, YarnConfiguration>)>> ComputeBuildGraphAsync(BuildParameters.IBuildParameters buildParameters)
        {
            if (m_resolverSettings.CustomProjectGraph == null)
            {
                Tracing.Logger.Log.ErrorReadingCustomProjectGraph(
                    m_context.LoggingContext, 
                    m_resolverSettings.Location(m_context.PathTable), 
                    "The custom project graph is undefined.");
                var failure = new Possible<(JavaScriptGraph<YarnConfiguration>, GenericJavaScriptGraph<DeserializedJavaScriptProject, YarnConfiguration>)>(new JavaScriptGraphConstructionFailure(m_resolverSettings, m_context.PathTable));

                return Task.FromResult(failure);
            }

            // The graph may come from a file or from a DScript literal following the Yarn schema
            Possible<GenericJavaScriptGraph<DeserializedJavaScriptProject, YarnConfiguration>> maybeGraph;
            if (m_resolverSettings.CustomProjectGraph.GetValue() is AbsolutePath graphFile)
            {
                Contract.Assert(graphFile.IsValid);
                maybeGraph = ReadGraphFromFile(graphFile);
            }
            else
            {
                var graphLiteral = m_resolverSettings.CustomProjectGraph.GetValue() as IReadOnlyDictionary<string, IJavaScriptCustomProjectGraphNode>;
                maybeGraph = BuildGraphFromLiteral(graphLiteral);
            }

            // The graph is always resolved with execution semantics, since Yarn doesn't provide execution semantics
            var maybeResult = maybeGraph
                .Then(graph => ResolveGraphWithExecutionSemantics(graph))
                .Then(resolvedGraph => (resolvedGraph, maybeGraph.Result));

            // There is actually no graph file to 'keep' in this case, but in order to honor
            // this option, let's serialize to a file the graph we just constructed
            if (m_resolverSettings.KeepProjectGraphFile == true && maybeResult.Succeeded)
            {
                SerializeComputedGraph(maybeResult.Result.Result);
            }

            return Task.FromResult(maybeResult);
        }

        private void SerializeComputedGraph(GenericJavaScriptGraph<DeserializedJavaScriptProject, YarnConfiguration> graph)
        {
            AbsolutePath outputDirectory = m_host.GetFolderForFrontEnd(Name);
            AbsolutePath outputFile = outputDirectory.Combine(m_context.PathTable, Guid.NewGuid().ToString());

            // Make sure the directories are there
            FileUtilities.CreateDirectory(outputDirectory.ToString(m_context.PathTable));

            try
            {
                File.WriteAllText(outputFile.ToString(m_context.PathTable), JObject.FromObject(graph, ConstructProjectGraphSerializer(JsonSerializerSettings)).ToString());
                // Graph-related files are requested to be left on disk. Let's print a message with their location.
                JavaScript.Tracing.Logger.Log.GraphBuilderFilesAreNotRemoved(m_context.LoggingContext, outputFile.ToString(m_context.PathTable));
            }
            catch (Exception ex)
            {
                // Serializing the graph is done on a best-effort basis. If there is any issues with it, just log it and move on.
                Tracing.Logger.Log.CannotSerializeGraphFile(m_context.LoggingContext, m_resolverSettings.Location(m_context.PathTable), outputFile.ToString(m_context.PathTable), ex.ToString());
            }
        }

        private Possible<GenericJavaScriptGraph<DeserializedJavaScriptProject, YarnConfiguration>> BuildGraphFromLiteral(IReadOnlyDictionary<string, IJavaScriptCustomProjectGraphNode> graphLiteral)
        {
            var projects = new List<DeserializedJavaScriptProject>(graphLiteral.Count);
            foreach (var kvp in graphLiteral)
            {
                if (!ValidateProject(kvp.Key, kvp.Value?.WorkspaceDependencies, kvp.Value?.Location))
                {
                    return new JavaScriptGraphConstructionFailure(m_resolverSettings, m_context.PathTable);
                }

                var maybeProject = CreateJavaScriptProject(kvp.Key, kvp.Value.WorkspaceDependencies, kvp.Value.Location);
                if (!maybeProject.Succeeded)
                {
                    return maybeProject.Failure;
                }
                
                projects.Add(maybeProject.Result);
            }

            return new GenericJavaScriptGraph<DeserializedJavaScriptProject, YarnConfiguration>(projects);
        }

        private Possible<GenericJavaScriptGraph<DeserializedJavaScriptProject, YarnConfiguration>> ReadGraphFromFile(AbsolutePath graphFile)
        {
            try
            {
                JsonSerializer serializer = ConstructProjectGraphSerializer(JsonSerializerSettings);

                if (!m_host.Engine.TryGetFrontEndFile(graphFile, Name, out var stream))
                {
                    Tracing.Logger.Log.ErrorReadingCustomProjectGraph(
                        m_context.LoggingContext,
                        m_resolverSettings.Location(m_context.PathTable),
                        $"Could not read file '{graphFile.ToString(m_context.PathTable)}'.");

                    return new JavaScriptGraphConstructionFailure(m_resolverSettings, m_context.PathTable);
                }

                using (var s = stream)
                using (var sr = new StreamReader(s))
                using (var reader = new JsonTextReader(sr))
                {
                    // Expected schema is here: https://classic.yarnpkg.com/en/docs/cli/workspaces/#toc-yarn-workspaces-info
                    var deserializedGraph = serializer.Deserialize<Dictionary<string, JToken>>(reader);

                    var projects = new List<DeserializedJavaScriptProject>(deserializedGraph.Count);

                    foreach (var kvp in deserializedGraph)
                    {
                        var dependencies = kvp.Value["workspaceDependencies"]?.ToObject<IReadOnlyCollection<string>>();
                        RelativePath relativeProjectFolder = (RelativePath)(kvp.Value["location"]?.ToObject(typeof(RelativePath), serializer) ?? RelativePath.Invalid);

                        if (!ValidateProject(kvp.Key, dependencies, relativeProjectFolder))
                        {
                            return new JavaScriptGraphConstructionFailure(m_resolverSettings, m_context.PathTable);
                        }

                        var maybeProject = CreateJavaScriptProject(kvp.Key, dependencies, relativeProjectFolder);
                        if (!maybeProject.Succeeded)
                        {
                            return maybeProject.Failure;
                        }

                        projects.Add(maybeProject.Result);
                    }

                    return new GenericJavaScriptGraph<DeserializedJavaScriptProject, YarnConfiguration>(projects);
                }
            }
            catch (Exception e) when (e is IOException || e is JsonReaderException || e is BuildXLException)
            {
                Tracing.Logger.Log.ErrorReadingCustomProjectGraph(
                    m_context.LoggingContext,
                    m_resolverSettings.Location(m_context.PathTable),
                    e.Message);

                return new JavaScriptGraphConstructionFailure(m_resolverSettings, m_context.PathTable);
            }
        }

        private Possible<DeserializedJavaScriptProject> CreateJavaScriptProject(string name, IReadOnlyCollection<string> dependencies, RelativePath projectFolder)
        {
            var maybeCustomScripts = new Possible<IReadOnlyDictionary<string, string>>((IReadOnlyDictionary<string, string>)null);
            
            // If there is a callback defined, give it a chance to retrieve custom scripts
            if (m_resolverSettings.CustomScripts != null)
            {
                maybeCustomScripts = ResolveCustomScripts(name, projectFolder);
                if (!maybeCustomScripts.Succeeded)
                {
                    return maybeCustomScripts.Failure;
                }
            }

            // The callback does not want to customize the scripts for this particular project or there is no callback. 
            // Let's try to find a package.json under the project folder
            if (maybeCustomScripts.Result == null)
            {
                var packageJsonPath = m_resolverSettings.Root.Combine(m_context.PathTable, projectFolder).Combine(m_context.PathTable, "package.json");
                maybeCustomScripts = GetScriptsFromPackageJson(packageJsonPath, m_resolverSettings.Location(m_context.PathTable));

                if (!maybeCustomScripts.Succeeded)
                {
                    return maybeCustomScripts.Failure;
                }
            }

            return new DeserializedJavaScriptProject(
                name: name,
                projectFolder: m_resolverSettings.Root.Combine(m_context.PathTable, projectFolder),
                dependencies: dependencies,
                availableScriptCommands: maybeCustomScripts.Result,
                tempFolder: m_resolverSettings.Root,
                outputDirectories: CollectionUtilities.EmptyArray<PathWithTargets>(),
                sourceFiles: CollectionUtilities.EmptyArray<PathWithTargets>()
            );
        }

        private bool ValidateProject(string projectName, IReadOnlyCollection<string> dependencies, RelativePath? projectFolder)
        {
            if (string.IsNullOrEmpty(projectName))
            {
                Tracing.Logger.Log.ErrorReadingCustomProjectGraph(
                        m_context.LoggingContext,
                        m_resolverSettings.Location(m_context.PathTable),
                        $"Project name is not defined.");

                return false;
            }

            if (dependencies == null)
            {
                Tracing.Logger.Log.ErrorReadingCustomProjectGraph(
                    m_context.LoggingContext,
                    m_resolverSettings.Location(m_context.PathTable),
                    $"Project '{projectName}' dependencies are not defined.");

                return false;
            }

            if (projectFolder == null || !projectFolder.Value.IsValid)
            {
                Tracing.Logger.Log.ErrorReadingCustomProjectGraph(
                    m_context.LoggingContext,
                    m_resolverSettings.Location(m_context.PathTable),
                    $"Project '{projectName}' location is not valid.");

                return false;
            }

            return true;
        }
    }
}
