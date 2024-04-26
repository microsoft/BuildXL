// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.FrontEnd.JavaScript.ProjectGraph;
using BuildXL.FrontEnd.Script.RuntimeModel.AstBridge;
using BuildXL.FrontEnd.Utilities;
using BuildXL.Native.IO;
using BuildXL.Processes;
using BuildXL.Utilities;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Configuration;
using Newtonsoft.Json;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities.Collections;

namespace BuildXL.FrontEnd.JavaScript
{
    /// <summary>
    /// Workspace resolver for JavaScript based resolvers where a tool is called to compute the graph
    /// </summary>
    /// <remarks>
    /// Extenders should define where the bxl graph construction tool is located and the parameters to pass to it
    /// Some graph (nodejs-based) construction tools may decide to produce an error file instead of using stderr. The convention is to append a .err to the output file. This is because if standard error is too big,
    /// the process may wait for it to be drained before it can exit. See https://nodejs.org/api/child_process.html#optionsstdio.
    /// </remarks>
    public abstract class ToolBasedJavaScriptWorkspaceResolver<TGraphConfiguration, TResolverSettings> : JavaScriptWorkspaceResolver<TGraphConfiguration, TResolverSettings>
        where TGraphConfiguration: class 
        where TResolverSettings : class, IJavaScriptResolverSettings
    {

        /// <summary>
        /// Some graph construction tools produce an error file. The convention is to append a .err to the output file name.
        /// </summary>
        private static string GetErrorFile(AbsolutePath outputFile, PathTable pathTable) => outputFile.ChangeExtension(pathTable, PathAtom.Create(pathTable.StringTable, ".err")).ToString(pathTable); 
        
        /// <summary>
        /// The BuildXL tool relative location that is used to construct the graph 
        /// </summary>
        protected abstract RelativePath RelativePathToGraphConstructionTool { get; }

        /// <inheritdoc/>
        public ToolBasedJavaScriptWorkspaceResolver(string resolverKind) : base(resolverKind)
        {
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

        /// <summary>
        /// Computes a build graph by calling an external tool in a sandboxed process. The particular tool and arguments are provided by implementors.
        /// </summary>
        protected override async Task<Possible<(JavaScriptGraph<TGraphConfiguration>, GenericJavaScriptGraph<DeserializedJavaScriptProject, TGraphConfiguration>)>> ComputeBuildGraphAsync(
            BuildParameters.IBuildParameters buildParameters)
        {
            // We create a unique output file on the obj folder associated with the current front end, and using a GUID as the file name
            AbsolutePath outputDirectory = Host.GetFolderForFrontEnd(Name);
            AbsolutePath outputFile = outputDirectory.Combine(Context.PathTable, Guid.NewGuid().ToString());

            // Make sure the directories are there
            FileUtilities.CreateDirectory(outputDirectory.ToString(Context.PathTable));

            Possible<(JavaScriptGraph<TGraphConfiguration> graph, GenericJavaScriptGraph<DeserializedJavaScriptProject, TGraphConfiguration> flattenedGraph)> maybeResult = await ComputeBuildGraphAsync(outputFile, buildParameters);

            if (!maybeResult.Succeeded)
            {
                // A more specific error has been logged already
                return maybeResult.Failure;
            }

            if (ResolverSettings.KeepProjectGraphFile != true)
            {
                DeleteGraphBuilderRelatedFiles(outputFile);
            }
            else
            {
                // Graph-related files are requested to be left on disk. Let's print a message with their location.
                Tracing.Logger.Log.GraphBuilderFilesAreNotRemoved(Context.LoggingContext, outputFile.ToString(Context.PathTable));
            }

            return maybeResult;
        }

        /// <summary>
        /// Defined the project name for a group based on its group members and group command name
        /// </summary>
        protected virtual string GetProjectNameForGroup(IReadOnlyCollection<JavaScriptProject> groupMembers, string groupCommandName)
        {
            throw new NotImplementedException("Subclasses are supposed to implement this");
        }

        private async Task<Possible<(JavaScriptGraph<TGraphConfiguration>, GenericJavaScriptGraph<DeserializedJavaScriptProject, TGraphConfiguration>)>> ComputeBuildGraphAsync(
            AbsolutePath outputFile,
            BuildParameters.IBuildParameters buildParameters)
        {
            // Determine the base location to use for finding the graph construction tool
            if (!TryFindGraphBuilderToolLocation(
                ResolverSettings, 
                buildParameters, 
                out AbsolutePath foundLocation, 
                out string failure))
            {
                Tracing.Logger.Log.CannotFindGraphBuilderTool(
                    Context.LoggingContext,
                    ResolverSettings.Location(Context.PathTable),
                    failure);

                return new JavaScriptGraphConstructionFailure(ResolverSettings, Context.PathTable);
            }

            string nodeExeLocation;
            if (ResolverSettings.NodeExeLocation != null)
            {
                var specifiedNodeExe = ResolverSettings.NodeExeLocation.GetValue();
                AbsolutePath nodeExeLocationPath;

                if (specifiedNodeExe is FileArtifact fileArtifact)
                {
                    nodeExeLocationPath = fileArtifact.Path;
                }
                else 
                {
                    var pathCollection = ((IReadOnlyList<DirectoryArtifact>)specifiedNodeExe).Select(dir => dir.Path);
                    if (!FrontEndUtilities.TryFindToolInPath(Context, Host, pathCollection, new[] { "node", "node.exe" }, out nodeExeLocationPath))
                    {
                        failure = $"'node' cannot be found under any of the provided paths '{string.Join(";", pathCollection.Select(path => path.ToString(Context.PathTable)))}'.";
                        Tracing.Logger.Log.CannotFindGraphBuilderTool(
                            Context.LoggingContext,
                            ResolverSettings.Location(Context.PathTable),
                            failure);

                        return new JavaScriptGraphConstructionFailure(ResolverSettings, Context.PathTable);
                    }
                }

                nodeExeLocation = nodeExeLocationPath.ToString(Context.PathTable);

                // Most graph construction tools (yarn, rush, etc.) rely on node.exe being on the PATH. Make sure
                // that's the case by appending the PATH exposed to the graph construction process with the location of the
                // specified node.exe. By prepending PATH with it, we also make sure yarn/rush will be using the same version
                // of node the user specified.
                string pathWithNode = buildParameters.ContainsKey("PATH") ? buildParameters["PATH"] : string.Empty;
                var nodeDirectory = nodeExeLocationPath.GetParent(Context.PathTable);
                if (nodeDirectory.IsValid)
                {
                    pathWithNode = nodeDirectory.ToString(Context.PathTable) + Path.PathSeparator + pathWithNode;
                }
                
                buildParameters = buildParameters.Override(new[] { new KeyValuePair<string, string>("PATH", pathWithNode) });
            }
            else
            {
                // We always use cmd/bash as the tool so if the node location is not provided we can just pass 'node.exe'/'node' and let PATH do the work.
                nodeExeLocation = OperatingSystemHelper.IsWindowsOS ? "node.exe" : "node";
            }

            SandboxedProcessResult result = await RunJavaScriptGraphBuilderAsync(nodeExeLocation, outputFile, buildParameters, foundLocation);

            string standardError = result.StandardError.CreateReader().ReadToEndAsync().GetAwaiter().GetResult();
            
            // Check whether the graph construction tool produced an error file, and in that case attach it to the standard error.
            string errorFile = GetErrorFile(outputFile, Context.PathTable);
            if (FileUtilities.Exists(errorFile) && await File.ReadAllTextAsync(errorFile) is var errorText && !string.IsNullOrEmpty(errorText))
            {
                standardError += Environment.NewLine + errorText;
            }

            if (result.ExitCode != 0)
            {
                Tracing.Logger.Log.ProjectGraphConstructionError(
                    Context.LoggingContext,
                    ResolverSettings.Location(Context.PathTable),
                    standardError);

                return new JavaScriptGraphConstructionFailure(ResolverSettings, Context.PathTable);
            }

            // If the tool exited gracefully, but standard error is not empty, that
            // is interpreted as a warning. We propagate that to the BuildXL log
            if (!string.IsNullOrEmpty(standardError))
            {
                Tracing.Logger.Log.GraphConstructionFinishedSuccessfullyButWithWarnings(
                    Context.LoggingContext,
                    ResolverSettings.Location(Context.PathTable),
                    standardError);
            }

            TrackFilesAndEnvironment(result.AllUnexpectedFileAccesses.Union(result.ExplicitlyReportedFileAccesses), outputFile.GetParent(Context.PathTable));

            JsonSerializer serializer = ConstructProjectGraphSerializer(JsonSerializerSettings);
            
            using (var sr = new StreamReader(outputFile.ToString(Context.PathTable)))
            using (var reader = new JsonTextReader(sr))
            {
                var flattenedJavaScriptGraph = serializer.Deserialize<GenericJavaScriptGraph<DeserializedJavaScriptProject, TGraphConfiguration>>(reader);

                // If a custom script command callback is specified, give it a chance to alter the script commands of 
                // each package
                if (ResolverSettings.CustomScripts != null)
                {
                    var projectsWithCustomScripts = new List<DeserializedJavaScriptProject>(flattenedJavaScriptGraph.Projects.Count);
                    foreach (var project in flattenedJavaScriptGraph.Projects)
                    {
                        ResolverSettings.Root.TryGetRelative(Context.PathTable, project.ProjectFolder, out var relativeFolder);
                        
                        var maybeCustomScripts = ResolveCustomScripts(project.Name, relativeFolder);
                        if (!maybeCustomScripts.Succeeded)
                        {
                            return maybeCustomScripts.Failure;
                        }
                        var customScripts = maybeCustomScripts.Result;
                        
                        // A null customScript means the callback did not provide any customization
                        projectsWithCustomScripts.Add(customScripts == null ? project : project.WithCustomScripts(customScripts));
                    }

                    flattenedJavaScriptGraph = new GenericJavaScriptGraph<DeserializedJavaScriptProject, TGraphConfiguration>(
                        projectsWithCustomScripts, flattenedJavaScriptGraph.Configuration);
                }
                
                Possible<JavaScriptGraph<TGraphConfiguration>> graph = ApplyBxlExecutionSemantics() ? ResolveGraphWithExecutionSemantics(flattenedJavaScriptGraph) : ResolveGraphWithoutExecutionSemantics(flattenedJavaScriptGraph);

                return graph.Then(graph => new Possible<(JavaScriptGraph<TGraphConfiguration>, GenericJavaScriptGraph<DeserializedJavaScriptProject, TGraphConfiguration>)>((graph, flattenedJavaScriptGraph)));
            }
        }

        /// <summary>
        /// Resolves a JavaScript graph without execution semantics. Assumes each JS project has at most one script command.
        /// </summary>
        protected Possible<JavaScriptGraph<TGraphConfiguration>> ResolveGraphWithoutExecutionSemantics(GenericJavaScriptGraph<DeserializedJavaScriptProject, TGraphConfiguration> flattenedJavaScriptGraph)
        {
            // Compute the inverse relationship between commands and their groups
            var commandGroupMembership = BuildCommandGroupMembership();

            // Here we put all resolved projects (including the ones belonging to a group command)
            var resolvedProjects = new Dictionary<string, (JavaScriptProject JavaScriptProject, IReadOnlyCollection<string> dependencies)>(flattenedJavaScriptGraph.Projects.Count);
            // Here we put the resolved projects that belong to a given group. Use the project folder + command group as the key for a group. The assumption here is that all resolved projects that belong to the
            // same group share the project folder (since grouping is about bundling together script commands coming from the same package.json)
            var resolvedGroups = new MultiValueDictionary<(AbsolutePath projectFolder, string commandGroup), JavaScriptProject>(CommandGroups.Keys.Count());
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
                    resolvedGroups.Add((javaScriptProject.ProjectFolder, commandGroup), javaScriptProject);
                }

                resolvedProjects.Add(javaScriptProject.Name, (javaScriptProject, deserializedProject.Dependencies));
            }

            // Here we build a map between each group member to its group project
            var resolvedCommandGroupMembership = new Dictionary<JavaScriptProject, JavaScriptProject>();

            // Now add groups commands
            foreach (var kvp in resolvedGroups)
            {
                string commandName = kvp.Key.commandGroup;
                AbsolutePath projectFolder = kvp.Key.projectFolder;
                IReadOnlyList<JavaScriptProject> members = kvp.Value;

                Contract.Assert(members.Count > 0);

                // All members of a group are expected to share the same project and temp folder (since
                // they all come from the same package.json)
                var firstMember = members.First();

                var groupProject = CreateGroupProject(commandName, GetProjectNameForGroup(members, commandName), members, firstMember.ProjectFolder, firstMember.TempFolder);

                // Here we check for duplicate projects
                if (resolvedProjects.ContainsKey(groupProject.Name))
                {
                    return new JavaScriptProjectSchedulingFailure(groupProject,
                        $"Duplicate project name '{groupProject.Name}' defined in '{groupProject.ProjectFolder.ToString(Context.PathTable)}' " +
                        $"and '{resolvedProjects[groupProject.Name].JavaScriptProject.ProjectFolder.ToString(Context.PathTable)}' for script command '{commandName}'");
                }

                // Group dependencies are defined by the union of the dependencies of their members. 
                // Turn this into a set to dedup them
                var groupedDependencies = members.SelectMany(member => resolvedProjects[member.Name].dependencies).ToReadOnlySet();

                resolvedProjects.Add(groupProject.Name, (groupProject, groupedDependencies));
                // This project group should be part of the final list of projects
                resultingProjects.Add(groupProject);

                // Update the resolved membership so each member points to its group project
                foreach (var member in members)
                {
                    resolvedCommandGroupMembership[member] = groupProject;
                }
            }

            // Now resolve dependencies
            foreach (var resultingProject in resultingProjects)
            {
                var dependencies = resolvedProjects[resultingProject.Name].dependencies;

                // Let's use a set so we make sure dependencies are deduped
                var projectDependencies = new HashSet<JavaScriptProject>();
                foreach (string dependency in dependencies)
                {
                    // Some providers (e.g. Lage) list dependencies to nodes that don't actually exist. This is typically when, for example, project A depends on B, A has a 'build' verb
                    // but B doesn't. B#build will be listed as a dependency for A#build, but B#build won't be defined as a node in the graph. The dependency in the case should be ignored.
                    if (!resolvedProjects.TryGetValue(dependency, out var value))
                    {
                        Tracing.Logger.Log.IgnoredDependency(Context.LoggingContext, ResolverSettings.Location(Context.PathTable), dependency, resultingProject.Name);
                        continue;
                    }

                    var resolvedDependency = GetGroupProjectIfDefined(resolvedCommandGroupMembership, value.JavaScriptProject);
                    // Let's avoid a reference to itself. This can happen when two dependencies point to different scripts of the same project
                    if (resolvedDependency != resultingProject)
                    {
                        projectDependencies.Add(resolvedDependency);
                    }
                }

                resultingProject.SetDependencies(projectDependencies);
            }

            return new JavaScriptGraph<TGraphConfiguration>(
                new List<JavaScriptProject>(resultingProjects),
                flattenedJavaScriptGraph.Configuration);
        }

        private Task<SandboxedProcessResult> RunJavaScriptGraphBuilderAsync(
           string nodeExeLocation,
           AbsolutePath outputFile,
           BuildParameters.IBuildParameters buildParameters,
           AbsolutePath toolLocation)
        {
            AbsolutePath toolPath = Configuration.Layout.BuildEngineDirectory.Combine(Context.PathTable, RelativePathToGraphConstructionTool);
            string outputDirectory = outputFile.GetParent(Context.PathTable).ToString(Context.PathTable);

            var cmdExeArtifact = FileArtifact.CreateSourceFile(JavaScriptUtilities.GetCommandLineToolPath(Context.PathTable));
            
            var toolArguments = GetGraphConstructionToolArguments(outputFile, toolLocation, toolPath, nodeExeLocation);

            Tracing.Logger.Log.ConstructingGraphScript(Context.LoggingContext, toolArguments);

            return FrontEndUtilities.RunSandboxedToolAsync(
               Context,
               cmdExeArtifact.Path.ToString(Context.PathTable),
               buildStorageDirectory: outputDirectory,
               fileAccessManifest: FrontEndUtilities.GenerateToolFileAccessManifest(Context, outputFile.GetParent(Context.PathTable)),
               arguments: toolArguments,
               workingDirectory: ResolverSettings.Root.ToString(Context.PathTable),
               description: $"{Name} graph builder",
               buildParameters);
        }

        private void DeleteGraphBuilderRelatedFiles(AbsolutePath outputFile)
        {
            // Remove the file with the serialized graph so we leave no garbage behind
            // If there is a problem deleting these file, unlikely to happen (the process that created it should be gone by now), log as a warning and move on, this is not
            // a blocking problem
            try
            {
                FileUtilities.DeleteFile(outputFile.ToString(Context.PathTable));
                // The error file may not always be present, but we want to delete it if that's the case.
                FileUtilities.DeleteFile(GetErrorFile(outputFile, Context.PathTable));
            }
            catch (BuildXLException ex)
            {
                Tracing.Logger.Log.CannotDeleteSerializedGraphFile(Context.LoggingContext, ResolverSettings.Location(Context.PathTable), outputFile.ToString(Context.PathTable), ex.Message);
            }
        }
    }
}
