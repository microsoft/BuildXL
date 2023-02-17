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
using BuildXL.Utilities.Core;
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
            if (ResolverSettings.CustomProjectGraph == null)
            {
                Tracing.Logger.Log.ErrorReadingCustomProjectGraph(
                    Context.LoggingContext, 
                    ResolverSettings.Location(Context.PathTable), 
                    "The custom project graph is undefined.");
                var failure = new Possible<(JavaScriptGraph<YarnConfiguration>, GenericJavaScriptGraph<DeserializedJavaScriptProject, YarnConfiguration>)>(new JavaScriptGraphConstructionFailure(ResolverSettings, Context.PathTable));

                return Task.FromResult(failure);
            }

            // The graph may come from a file or from a DScript literal following the Yarn schema
            Possible<GenericJavaScriptGraph<DeserializedJavaScriptProject, YarnConfiguration>> maybeGraph;
            if (ResolverSettings.CustomProjectGraph.GetValue() is AbsolutePath graphFile)
            {
                Contract.Assert(graphFile.IsValid);
                maybeGraph = ReadGraphFromFile(graphFile);
            }
            else
            {
                var graphLiteral = ResolverSettings.CustomProjectGraph.GetValue() as IReadOnlyDictionary<string, IJavaScriptCustomProjectGraphNode>;
                maybeGraph = BuildGraphFromLiteral(graphLiteral);
            }

            // The graph is always resolved with execution semantics, since Yarn doesn't provide execution semantics
            var maybeResult = maybeGraph
                .Then(graph => ResolveGraphWithExecutionSemantics(graph))
                .Then(resolvedGraph => (resolvedGraph, maybeGraph.Result));

            // There is actually no graph file to 'keep' in this case, but in order to honor
            // this option, let's serialize to a file the graph we just constructed
            if (ResolverSettings.KeepProjectGraphFile == true && maybeResult.Succeeded)
            {
                SerializeComputedGraph(maybeResult.Result.Result);
            }

            return Task.FromResult(maybeResult);
        }

        private void SerializeComputedGraph(GenericJavaScriptGraph<DeserializedJavaScriptProject, YarnConfiguration> graph)
        {
            AbsolutePath outputDirectory = Host.GetFolderForFrontEnd(Name);
            AbsolutePath outputFile = outputDirectory.Combine(Context.PathTable, Guid.NewGuid().ToString());

            // Make sure the directories are there
            FileUtilities.CreateDirectory(outputDirectory.ToString(Context.PathTable));

            try
            {
                File.WriteAllText(outputFile.ToString(Context.PathTable), JObject.FromObject(graph, ConstructProjectGraphSerializer(JsonSerializerSettings)).ToString());
                // Graph-related files are requested to be left on disk. Let's print a message with their location.
                JavaScript.Tracing.Logger.Log.GraphBuilderFilesAreNotRemoved(Context.LoggingContext, outputFile.ToString(Context.PathTable));
            }
            catch (Exception ex)
            {
                // Serializing the graph is done on a best-effort basis. If there is any issues with it, just log it and move on.
                Tracing.Logger.Log.CannotSerializeGraphFile(Context.LoggingContext, ResolverSettings.Location(Context.PathTable), outputFile.ToString(Context.PathTable), ex.ToString());
            }
        }

        private Possible<GenericJavaScriptGraph<DeserializedJavaScriptProject, YarnConfiguration>> BuildGraphFromLiteral(IReadOnlyDictionary<string, IJavaScriptCustomProjectGraphNode> graphLiteral)
        {
            var projects = new List<DeserializedJavaScriptProject>(graphLiteral.Count);
            foreach (var kvp in graphLiteral)
            {
                if (!ValidateProject(kvp.Key, kvp.Value?.WorkspaceDependencies, kvp.Value?.Location))
                {
                    return new JavaScriptGraphConstructionFailure(ResolverSettings, Context.PathTable);
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

                if (!Host.Engine.TryGetFrontEndFile(graphFile, Name, out var stream))
                {
                    Tracing.Logger.Log.ErrorReadingCustomProjectGraph(
                        Context.LoggingContext,
                        ResolverSettings.Location(Context.PathTable),
                        $"Could not read file '{graphFile.ToString(Context.PathTable)}'.");

                    return new JavaScriptGraphConstructionFailure(ResolverSettings, Context.PathTable);
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
                            return new JavaScriptGraphConstructionFailure(ResolverSettings, Context.PathTable);
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
                    Context.LoggingContext,
                    ResolverSettings.Location(Context.PathTable),
                    e.Message);

                return new JavaScriptGraphConstructionFailure(ResolverSettings, Context.PathTable);
            }
        }

        private Possible<DeserializedJavaScriptProject> CreateJavaScriptProject(string name, IReadOnlyCollection<string> dependencies, RelativePath projectFolder)
        {
            var maybeCustomScripts = new Possible<IReadOnlyDictionary<string, string>>((IReadOnlyDictionary<string, string>)null);
            
            // If there is a callback defined, give it a chance to retrieve custom scripts
            if (ResolverSettings.CustomScripts != null)
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
                var packageJsonPath = ResolverSettings.Root.Combine(Context.PathTable, projectFolder).Combine(Context.PathTable, "package.json");
                maybeCustomScripts = GetScriptsFromPackageJson(packageJsonPath, ResolverSettings.Location(Context.PathTable));

                if (!maybeCustomScripts.Succeeded)
                {
                    return maybeCustomScripts.Failure;
                }
            }

            return new DeserializedJavaScriptProject(
                name: name,
                projectFolder: ResolverSettings.Root.Combine(Context.PathTable, projectFolder),
                dependencies: dependencies,
                availableScriptCommands: maybeCustomScripts.Result,
                tempFolder: ResolverSettings.Root,
                outputDirectories: CollectionUtilities.EmptyArray<PathWithTargets>(),
                sourceFiles: CollectionUtilities.EmptyArray<PathWithTargets>()
            );
        }

        private bool ValidateProject(string projectName, IReadOnlyCollection<string> dependencies, RelativePath? projectFolder)
        {
            if (string.IsNullOrEmpty(projectName))
            {
                Tracing.Logger.Log.ErrorReadingCustomProjectGraph(
                        Context.LoggingContext,
                        ResolverSettings.Location(Context.PathTable),
                        $"Project name is not defined.");

                return false;
            }

            if (dependencies == null)
            {
                Tracing.Logger.Log.ErrorReadingCustomProjectGraph(
                    Context.LoggingContext,
                    ResolverSettings.Location(Context.PathTable),
                    $"Project '{projectName}' dependencies are not defined.");

                return false;
            }

            if (projectFolder == null || !projectFolder.Value.IsValid)
            {
                Tracing.Logger.Log.ErrorReadingCustomProjectGraph(
                    Context.LoggingContext,
                    ResolverSettings.Location(Context.PathTable),
                    $"Project '{projectName}' location is not valid.");

                return false;
            }

            return true;
        }
    }
}
