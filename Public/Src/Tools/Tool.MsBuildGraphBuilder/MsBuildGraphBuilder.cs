// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.FrontEnd.MsBuild.Serialization;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Instrumentation.Common;
using Microsoft.Build.Definition;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Exceptions;
using Microsoft.Build.Execution;
using Microsoft.Build.Graph;
using Microsoft.Build.Prediction;
using Newtonsoft.Json;
using ProjectGraphWithPredictionsResult = BuildXL.FrontEnd.MsBuild.Serialization.ProjectGraphWithPredictionsResult<string>;
using ProjectGraphWithPredictions = BuildXL.FrontEnd.MsBuild.Serialization.ProjectGraphWithPredictions<string>;
using ProjectWithPredictions = BuildXL.FrontEnd.MsBuild.Serialization.ProjectWithPredictions<string>;
using ProjectGraphBuilder;
using BuildXL.Utilities.Core;

namespace MsBuildGraphBuilderTool
{
    /// <summary>
    /// Capable of building a graph using the MsBuild static graph API, predicts inputs and outputs for each project using the BuildPrediction project, and serializes
    /// the result.
    /// </summary>
    public static class MsBuildGraphBuilder
    {
        // Well-known item that defines the protocol static targets.
        // See https://github.com/Microsoft/msbuild/blob/master/documentation/specs/static-graph.md#inferring-which-targets-to-run-for-a-project-within-the-graph
        // TODO: maybe the static graph API can provide this information in the future in a more native way
        private const string ProjectReferenceTargets = "ProjectReferenceTargets";

        /// <summary>
        /// Makes sure the required MsBuild assemblies are loaded from, uses the MsBuild static graph API to get a build graph starting
        /// at the project entry point and serializes it to an output file.
        /// </summary>
        /// <remarks>
        /// Legit errors while trying to load the MsBuild assemblies or constructing the graph are represented in the serialized result
        /// </remarks>
        public static void BuildGraphAndSerialize(MSBuildGraphBuilderArguments arguments)
        {
            Contract.Requires(arguments != null);
            BuildGraphAndSerialize(new BuildLocatorBasedMsBuildAssemblyLoader(), arguments);
        }

        internal static void BuildGraphAndSerialize(IMsBuildAssemblyLoader assemblyLoader, MSBuildGraphBuilderArguments arguments)
        {
            // Using the standard assembly loader and reporter
            // The output file is used as a unique name to identify the pipe
            using var reporter = new GraphBuilderReporter(Path.GetFileName(arguments.OutputPath));
            DoBuildGraphAndSerialize(assemblyLoader, reporter, arguments);
        }

        /// <summary>
        /// For tests only. Similar to <see cref="BuildGraphAndSerialize(MSBuildGraphBuilderArguments)"/>, but the assembly loader and reporter can be passed explicitly
        /// </summary>
        internal static void BuildGraphAndSerializeForTesting(
            IMsBuildAssemblyLoader assemblyLoader,
            GraphBuilderReporter reporter,
            MSBuildGraphBuilderArguments arguments,
            IReadOnlyCollection<IProjectPredictor> projectPredictorsForTesting = null)
        {
            DoBuildGraphAndSerialize(
                assemblyLoader,
                reporter,
                arguments,
                projectPredictorsForTesting);
        }

        private static void DoBuildGraphAndSerialize(
            IMsBuildAssemblyLoader assemblyLoader,
            GraphBuilderReporter reporter,
            MSBuildGraphBuilderArguments arguments,
            IReadOnlyCollection<IProjectPredictor> projectPredictorsForTesting = null)
        {
            reporter.ReportMessage("Starting MSBuild graph construction process...");
            var stopwatch = Stopwatch.StartNew();

            ProjectGraphWithPredictionsResult graphResult = BuildGraph(assemblyLoader, reporter, arguments, projectPredictorsForTesting);
            SerializeGraph(graphResult, arguments.OutputPath, reporter);

            reporter.ReportMessage($"Done constructing build graph in {stopwatch.ElapsedMilliseconds}ms.");
        }

        private static ProjectGraphWithPredictionsResult BuildGraph(
            IMsBuildAssemblyLoader assemblyLoader,
            GraphBuilderReporter reporter,
            MSBuildGraphBuilderArguments arguments,
            IReadOnlyCollection<IProjectPredictor> projectPredictorsForTesting)
        {
            reporter.ReportMessage("Looking for MSBuild toolset...");

            if (!assemblyLoader.TryLoadMsBuildAssemblies(arguments.MSBuildSearchLocations, reporter, out string failure, out IReadOnlyDictionary<string, string> locatedAssemblyPaths, out string locatedMsBuildPath))
            {
                return ProjectGraphWithPredictionsResult.CreateFailure(
                    GraphConstructionError.CreateFailureWithoutLocation(failure),
                    locatedAssemblyPaths,
                    locatedMsBuildPath);
            }

            reporter.ReportMessage("Done looking for MSBuild toolset.");

            return BuildGraphInternal(
                reporter,
                locatedAssemblyPaths ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                locatedMsBuildPath ?? string.Empty,
                arguments,
                projectPredictorsForTesting);
        }

        /// <summary>
        /// Assumes the proper MsBuild assemblies are loaded already
        /// </summary>
        private static ProjectGraphWithPredictionsResult BuildGraphInternal(
            GraphBuilderReporter reporter,
            IReadOnlyDictionary<string, string> assemblyPathsToLoad,
            string locatedMsBuildPath,
            MSBuildGraphBuilderArguments graphBuildArguments,
            IReadOnlyCollection<IProjectPredictor> projectPredictorsForTesting)
        {
            try
            {
                if (string.IsNullOrEmpty(locatedMsBuildPath))
                {
                    // When using BuildLocatorBasedMsBuildAssemblyLoader, the located MsBuild path will still be empty because we cannot
                    // reference to MsBuild type while the assemblies were being loaded. One approved method to get the needed MsBuild path is
                    // to get the path to assembly containing ProjectGraph, and use that path to infer the MsBuild path.
                    var projectGraphType = typeof(ProjectGraph);
                    string msBuildFile = graphBuildArguments.MsBuildRuntimeIsDotNetCore ? "MSBuild.dll" : "MSBuild.exe";
                    locatedMsBuildPath = Path.Combine(Path.GetDirectoryName(projectGraphType.Assembly.Location), msBuildFile);
                    if (!File.Exists(locatedMsBuildPath))
                    {
                        return ProjectGraphWithPredictionsResult.CreateFailure(
                            GraphConstructionError.CreateFailureWithoutLocation($"{locatedMsBuildPath} cannot be found"),
                            new Dictionary<string, string>(),
                            locatedMsBuildPath);
                    }

                    reporter.ReportMessage($"MSBuild is located at '{locatedMsBuildPath}'");
                }

                reporter.ReportMessage("Parsing MSBuild specs and constructing the build graph...");

                var projectInstanceToProjectCache = new ConcurrentDictionary<ProjectInstance, Project>();

                if (!TryBuildEntryPoints(
                    graphBuildArguments.ProjectsToParse,
                    graphBuildArguments.RequestedQualifiers,
                    graphBuildArguments.GlobalProperties,
                    out List<ProjectGraphEntryPoint> entryPoints,
                    out string failure))
                {
                    return ProjectGraphWithPredictionsResult.CreateFailure(
                        GraphConstructionError.CreateFailureWithoutLocation(failure),
                        assemblyPathsToLoad,
                        locatedMsBuildPath);
                }

                var projectGraph = new ProjectGraph(
                    entryPoints,
                    // The project collection doesn't need any specific global properties, since entry points already contain all the ones that are needed, and the project graph will merge them
                    new ProjectCollection(),
                    (projectPath, globalProps, projectCollection) => ProjectInstanceFactory(projectPath, globalProps, projectCollection, projectInstanceToProjectCache));

                reporter.ReportMessage("Done parsing MSBuild specs.");

                if (!TryConstructGraph(
                    projectGraph,
                    reporter,
                    projectInstanceToProjectCache,
                    graphBuildArguments,
                    projectPredictorsForTesting,
                    out ProjectGraphWithPredictions projectGraphWithPredictions,
                    out failure))
                {
                    return ProjectGraphWithPredictionsResult.CreateFailure(
                        GraphConstructionError.CreateFailureWithoutLocation(failure),
                        assemblyPathsToLoad,
                        locatedMsBuildPath);
                }

                return ProjectGraphWithPredictionsResult.CreateSuccessfulGraph(projectGraphWithPredictions, assemblyPathsToLoad, locatedMsBuildPath);
            }
            catch (InvalidProjectFileException e)
            {
                return CreateFailureFromInvalidProjectFile(assemblyPathsToLoad, locatedMsBuildPath, e);
            }
            catch (AggregateException e)
            {
                // If there is an invalid project file exception, use that one since it contains the location.
                var invalidProjectFileException = (InvalidProjectFileException) e.Flatten().InnerExceptions.FirstOrDefault(ex => ex is InvalidProjectFileException);
                if (invalidProjectFileException != null)
                {
                    return CreateFailureFromInvalidProjectFile(assemblyPathsToLoad, locatedMsBuildPath, invalidProjectFileException);
                }

                // Otherwise, we don't have a location, so we use the message of the originating exception
                return ProjectGraphWithPredictionsResult.CreateFailure(
                    GraphConstructionError.CreateFailureWithoutLocation(
                        e.InnerException != null ? e.InnerException.Message : e.Message),
                    assemblyPathsToLoad,
                    locatedMsBuildPath);
            }
        }

        /// <summary>
        /// Each entry point is a starting project associated (for each requested qualifier) with global properties
        /// </summary>
        /// <remarks>
        /// The global properties for each starting project is computed as a combination of the global properties specified for the whole build (in the resolver
        /// configuration) plus the particular qualifier, which is passed to MSBuild as properties as well.
        /// </remarks>
        private static bool TryBuildEntryPoints(
            IReadOnlyCollection<string> projectsToParse,
            IReadOnlyCollection<GlobalProperties> requestedQualifiers,
            GlobalProperties globalProperties,
            out List<ProjectGraphEntryPoint> entryPoints,
            out string failure)
        {
            entryPoints = new List<ProjectGraphEntryPoint>(projectsToParse.Count * requestedQualifiers.Count);
            failure = string.Empty;

            foreach (GlobalProperties qualifier in requestedQualifiers)
            {
                // Merge the qualifier first
                var mergedProperties = new Dictionary<string, string>(qualifier);

                // Go through global properties of the build and merge, making sure there are no incompatible values
                foreach (var kvp in globalProperties)
                {
                    string key = kvp.Key;
                    string value = kvp.Value;

                    if (qualifier.TryGetValue(key, out string duplicateValue))
                    {
                        // Property names are case insensitive, but property values are not!
                        if (!value.Equals(duplicateValue, StringComparison.Ordinal))
                        {
                            string displayKey = key.ToUpperInvariant();
                            failure = $"The qualifier {qualifier} is requested, but that is incompatible with the global property '{displayKey}={value}' since the specified values for '{displayKey}' do not agree.";
                            return false;
                        }
                    }
                    else
                    {
                        mergedProperties.Add(key, value);
                    }
                }

                entryPoints.AddRange(projectsToParse.Select(entryPoint => new ProjectGraphEntryPoint(entryPoint, mergedProperties)));
            }

            return true;
        }

        private static ProjectGraphWithPredictionsResult<string> CreateFailureFromInvalidProjectFile(IReadOnlyDictionary<string, string> assemblyPathsToLoad, string locatedMsBuildPath, InvalidProjectFileException e) =>
            ProjectGraphWithPredictionsResult.CreateFailure(
                GraphConstructionError.CreateFailureWithLocation(
                    new Location { File = e.ProjectFile, Line = e.LineNumber, Position = e.ColumnNumber },
                    e.Message),
                assemblyPathsToLoad,
                locatedMsBuildPath);

        private static ProjectInstance ProjectInstanceFactory(
            string projectPath,
            Dictionary<string, string> globalProperties,
            ProjectCollection projectCollection,
            ConcurrentDictionary<ProjectInstance, Project> projectInstanceToProjectCache)
        {
            // BuildPrediction needs a Project and ProjectInstance per proj file,
            // as each contains different information. To minimize memory usage,
            // we create a Project first, then return an immutable ProjectInstance
            // to the Static Graph.
            //
            // TODO: Ideally we would create the Project and ProjInstance,
            // use them immediately, then release the Project reference for gen0/1 cleanup, to handle large codebases.
            // We must not keep Project refs around for longer than we absolutely need them as they are large.
            Project project = Project.FromFile(
                projectPath,
                new ProjectOptions
                {
                    GlobalProperties = globalProperties,
                    ProjectCollection = projectCollection,
                });

            ProjectInstance projectInstance = project.CreateProjectInstance(ProjectInstanceSettings.ImmutableWithFastItemLookup);

            // Static Graph does not give us a context object reference, so we keep a lookup table for later.
            projectInstanceToProjectCache[projectInstance] = project;
            return projectInstance;
        }

        private static bool TryConstructGraph(
            ProjectGraph projectGraph,
            GraphBuilderReporter reporter,
            ConcurrentDictionary<ProjectInstance, Project> projectInstanceToProjectMap,
            MSBuildGraphBuilderArguments graphBuilderArguments,
            IReadOnlyCollection<IProjectPredictor> projectPredictorsForTesting,
            out ProjectGraphWithPredictions projectGraphWithPredictions,
            out string failure)
        {
            Contract.Assert(projectGraph != null);

            var projectNodes = new MutableProjectWithPredictions[projectGraph.ProjectNodes.Count];

            var nodes = projectGraph.ProjectNodes.ToArray();

            // Compute the list of targets to run per project
            reporter.ReportMessage("Computing targets to execute for each project...");

            // This dictionary should be exclusively read only at this point, and therefore thread safe
            var targetsPerProject = projectGraph.GetTargetLists(graphBuilderArguments.EntryPointTargets.ToArray());

            // Bidirectional access from nodes with predictions to msbuild nodes in order to compute node references in the second pass
            // TODO: revisit the structures, since the projects are known upfront we might be able to use lock-free structures
            var nodeWithPredictionsToMsBuildNodes = new ConcurrentDictionary<MutableProjectWithPredictions, ProjectGraphNode>(Environment.ProcessorCount, projectNodes.Length);
            var msBuildNodesToNodeWithPredictionIndex = new ConcurrentDictionary<ProjectGraphNode, MutableProjectWithPredictions>(Environment.ProcessorCount, projectNodes.Length);

            reporter.ReportMessage("Statically predicting inputs and outputs...");

            // Create the registered predictors and initialize the prediction executor
            // The prediction executor potentially initializes third-party predictors, which may contain bugs. So let's be very defensive here
            IReadOnlyCollection<IProjectPredictor> predictors;
            try
            {
                predictors = projectPredictorsForTesting ?? ProjectPredictors.AllPredictors;
            }
            catch(Exception ex)
            {
                failure = $"Cannot create standard predictors. An unexpected error occurred. Please contact BuildPrediction project owners with this stack trace: {ex}";
                projectGraphWithPredictions = new ProjectGraphWithPredictions(Array.Empty<ProjectWithPredictions>());
                return false;
            }

            // Using single-threaded prediction since we're parallelizing on project nodes instead.
            var predictionExecutor = new ProjectPredictionExecutor(predictors, new ProjectPredictionOptions { MaxDegreeOfParallelism = 1 });

            // Each predictor may return unexpected/incorrect results and targets may not be able to be predicted. We put those failures here for post-processing.
            ConcurrentQueue<(string predictorName, string failure)> predictionFailures = new ConcurrentQueue<(string, string)>();
            var predictedTargetFailures = new ConcurrentQueue<string>();

            // The predicted targets to execute (per project) go here
            var computedTargets = new ConcurrentBigMap<ProjectGraphNode, PredictedTargetsToExecute>();

            // When projects are allowed to not implement the target protocol, its references need default targets as a post-processing step
            var pendingAddDefaultTargets = new ConcurrentBigSet<ProjectGraphNode>();

            // First pass
            // Predict all projects in the graph in parallel and populate ProjectNodes
            Parallel.For(0, projectNodes.Length, (int i) => {

                ProjectGraphNode msBuildNode = nodes[i];
                ProjectInstance projectInstance = msBuildNode.ProjectInstance;
                Project project = projectInstanceToProjectMap[projectInstance];

                var outputFolderPredictions = new HashSet<string>(OperatingSystemHelper.PathComparer);
                var predictionCollector = new MsBuildOutputPredictionCollector(outputFolderPredictions, predictionFailures);

                try
                {
                    // Again, be defensive when using arbitrary predictors
                    predictionExecutor.PredictInputsAndOutputs(project, predictionCollector);
                }
                catch(Exception ex)
                {
                    predictionFailures.Enqueue((
                        "Unknown predictor",
                        $"Cannot run static predictor on project '{project.FullPath ?? "Unknown project"}'. An unexpected error occurred. Please contact BuildPrediction project owners with this stack trace: {ex}"));
                }

                 if (!TryGetPredictedTargetsAndPropertiesToExecute(
                    projectInstance,
                    msBuildNode,
                    targetsPerProject,
                    computedTargets,
                    pendingAddDefaultTargets,
                    graphBuilderArguments.AllowProjectsWithoutTargetProtocol,
                    out GlobalProperties globalProperties,
                    out string protocolMissingFailure))
                {
                    predictedTargetFailures.Enqueue(protocolMissingFailure);
                    return;
                }

                // The project file itself and all its imports are considered inputs to this project.
                // Predicted inputs are not actually used.
                var inputs = new HashSet<string>(OperatingSystemHelper.PathComparer) { project.FullPath };
                inputs.UnionWith(project.Imports.Select(i => i.ImportedProject.FullPath));

                projectNodes[i] = new MutableProjectWithPredictions(
                    projectInstance.FullPath,
                    projectInstance.GetItems(ProjectReferenceTargets).Count > 0,
                    globalProperties,
                    inputs,
                    outputFolderPredictions);

                // If projects not implementing the target protocol are blocked, then the list of computed targets is final. So we set it right here
                // to avoid wasted allocations
                // Otherwise, we leave it as a post-processing step. Since we are visiting the graph in parallel, without considering edges at all,
                // we need to wait until all nodes are visited to know the full set of projects that have pending default targets to be added
                if (!graphBuilderArguments.AllowProjectsWithoutTargetProtocol)
                {
                    projectNodes[i].PredictedTargetsToExecute = computedTargets[msBuildNode];
                }

                nodeWithPredictionsToMsBuildNodes[projectNodes[i]] = msBuildNode;
                msBuildNodesToNodeWithPredictionIndex[msBuildNode] = projectNodes[i];
            });

            // There were IO prediction errors.
            if (!predictionFailures.IsEmpty)
            {
                projectGraphWithPredictions = new ProjectGraphWithPredictions(Array.Empty<ProjectWithPredictions>());
                failure = $"Errors found during static prediction of inputs and outputs. " +
                    $"{string.Join(", ", predictionFailures.Select(failureWithCulprit => $"[Predicted by: {failureWithCulprit.predictorName}] {failureWithCulprit.failure}"))}";
                return false;
            }

            // There were target prediction errors.
            if (!predictedTargetFailures.IsEmpty)
            {
                projectGraphWithPredictions = new ProjectGraphWithPredictions(Array.Empty<ProjectWithPredictions>());
                failure = $"Errors found during target prediction. {string.Join(", ", predictedTargetFailures) }";
                return false;
            }

            // If there are references from (allowed) projects not implementing the target protocol, then we may need to change the already computed targets to add default targets to them
            if (graphBuilderArguments.AllowProjectsWithoutTargetProtocol)
            {
                Parallel.ForEach(computedTargets.Keys, (ProjectGraphNode projectNode) =>
                {
                    PredictedTargetsToExecute targets = computedTargets[projectNode];

                    if (pendingAddDefaultTargets.Contains(projectNode))
                    {
                        targets = targets.WithDefaultTargetsAppended(projectNode.ProjectInstance.DefaultTargets);
                    }

                    msBuildNodesToNodeWithPredictionIndex[projectNode].PredictedTargetsToExecute = targets;
                });
            }

            // Second pass
            // Reconstruct all references. A two-pass approach avoids needing to do more complicated reconstruction of references that would need traversing the graph
            foreach (var projectWithPredictions in projectNodes)
            {
                var references = nodeWithPredictionsToMsBuildNodes[projectWithPredictions]
                    .ProjectReferences
                    .Select(projectReference => msBuildNodesToNodeWithPredictionIndex[projectReference]);

                var referencing = nodeWithPredictionsToMsBuildNodes[projectWithPredictions]
                    .ReferencingProjects
                    .Select(referencingProject => msBuildNodesToNodeWithPredictionIndex[referencingProject]);

                projectWithPredictions.AddDependencies(references);
                projectWithPredictions.AddDependents(referencing);
            }

            if (graphBuilderArguments.UseLegacyProjectIsolation)
            {
                Possible<MutableProjectWithPredictions[]> maybeProjectNodes = TryMergeProjectNodes(
                    projectNodes,
                    nodeWithPredictionsToMsBuildNodes);

                if (!maybeProjectNodes.Succeeded)
                {
                    projectGraphWithPredictions = new ProjectGraphWithPredictions(Array.Empty<ProjectWithPredictions>());
                    failure = maybeProjectNodes.Failure.DescribeIncludingInnerFailures();
                    return false;
                }

                projectNodes = maybeProjectNodes.Result;
            }

            reporter.ReportMessage("Done predicting inputs and outputs.");

            projectGraphWithPredictions = new MutableProjectGraphWithPredictions(projectNodes).ToImmutable();
            failure = string.Empty;
            return true;
        }

        private static Possible<MutableProjectWithPredictions[]> TryMergeProjectNodes(
            MutableProjectWithPredictions[] projectNodes,
            IReadOnlyDictionary<MutableProjectWithPredictions, ProjectGraphNode> nodeWithPredictionsToMsBuildNodes)
        {
            string failure = string.Empty;

            // Step 1: Group project nodes based on dimension tuple (file, configuration, platform).
            var projectFileDimensionToProjectNodes = new Dictionary<ProjectFileAndDimension, List<MutableProjectWithPredictions>>();
            foreach (var projectNode in projectNodes)
            {
                ProjectInstance projectInstance = nodeWithPredictionsToMsBuildNodes[projectNode].ProjectInstance;
                ProjectFileAndDimension projectFileAndDimension = ProjectFileAndDimension.CreateFrom(projectInstance) ?? ProjectFileAndDimension.CreateFakeUnique(projectInstance);

                if (!projectFileDimensionToProjectNodes.TryGetValue(projectFileAndDimension, out List<MutableProjectWithPredictions> projectNodesForDimension))
                {
                    projectNodesForDimension = new List<MutableProjectWithPredictions>();
                    projectFileDimensionToProjectNodes.Add(projectFileAndDimension, projectNodesForDimension);
                }

                projectNodesForDimension.Add(projectNode);
            }

            if (!string.IsNullOrEmpty(failure))
            {
                return new Failure<string>(failure);
            }

            var newProjectNodes = new List<MutableProjectWithPredictions>(projectFileDimensionToProjectNodes.Count);

            // Step 2: Identify outer and inner builds.
            var innerBuilds = new List<MutableProjectWithPredictions>();

            foreach (KeyValuePair<ProjectFileAndDimension, List<MutableProjectWithPredictions>> kvp in projectFileDimensionToProjectNodes)
            {
                innerBuilds.Clear();

                List<MutableProjectWithPredictions> projectNodesForDimension = kvp.Value;
                if (projectNodesForDimension.Count == 1)
                {
                    newProjectNodes.Add(projectNodesForDimension[0]);
                }
                else
                {
                    // Found multiple project instances for the tuple (file, configuration, platform). These instances could be from a multi-targeting project.
                    // Identify the outer build, and merge the inner builds into it.
                    MutableProjectWithPredictions outerBuild = null;

                    foreach (var projectNode in projectNodesForDimension) 
                    {
                        ProjectInstance projectInstance = nodeWithPredictionsToMsBuildNodes[projectNode].ProjectInstance;

                        // See: https://github.com/microsoft/msbuild/blob/master/src/Build/Graph/ProjectInterpretation.cs
                        bool isInnerBuild = !string.IsNullOrWhiteSpace(projectInstance.GetPropertyValue(projectInstance.GetPropertyValue("InnerBuildProperty")));
                        bool isOuterBuild = !isInnerBuild && !string.IsNullOrWhiteSpace(projectInstance.GetPropertyValue(projectInstance.GetPropertyValue("InnerBuildPropertyValues")));

                        if (!isInnerBuild && !isOuterBuild)
                        {
                            return new Failure<string>($"Project at {projectInstance.FullPath}" +
                                $" with global properties {DictionaryToString(projectInstance.GlobalProperties)}" +
                                $" is neither an outer nor an inner build");
                        }

                        if (isInnerBuild)
                        {
                            innerBuilds.Add(projectNode);
                        }

                        if (isOuterBuild)
                        {
                            if (outerBuild != null)
                            {
                                return new Failure<string>($"Project at {projectInstance.FullPath} has multiple outer builds");
                            }

                            outerBuild = projectNode;
                        }
                    }

                    if (outerBuild == null)
                    {
                        return new Failure<string>($"Project at {kvp.Key.ProjectFile} (Configuration: {kvp.Key.Configuration}, Platform: {kvp.Key.Platform}) does not have an outer build");
                    }

                    // Step 3: Merge inner builds into outer build.
                    foreach (var innerBuild in innerBuilds)
                    {
                        // Merge dependencies and file/folder predictions.
                        outerBuild.Merge(innerBuild);

                        innerBuild.MakeOrphan();
                    }

                    // Set proper target.
                    outerBuild.PredictedTargetsToExecute = outerBuild.PredictedTargetsToExecute.WithDefaultTargetsAppended(
                        nodeWithPredictionsToMsBuildNodes[outerBuild].ProjectInstance.DefaultTargets);

                    newProjectNodes.Add(outerBuild);
                }
            }

            return newProjectNodes.ToArray();

            static string DictionaryToString(IDictionary<string, string> dictionary) =>
                string.Join(
                    ", ",
                    dictionary.Select(kvp => $"{kvp.Key}={kvp.Value}"));
        }

        private static bool TryGetPredictedTargetsAndPropertiesToExecute(
            ProjectInstance projectInstance,
            ProjectGraphNode projectNode,
            IReadOnlyDictionary<ProjectGraphNode, ImmutableList<string>> targetsPerProject,
            ConcurrentBigMap<ProjectGraphNode, PredictedTargetsToExecute> computedTargets,
            ConcurrentBigSet<ProjectGraphNode> pendingAddDefaultTargets,
            bool allowProjectsWithoutTargetProtocol,
            out GlobalProperties globalPropertiesForNode,
            out string failure)
        {
            // If the project instance contains a definition for project reference targets, that means it is complying to the static graph protocol for defining
            // the targets that will be executed on its children. Otherwise, the target prediction is not there.
            // In the future we may be able to query the project using the MSBuild static graph APIs to know if it is complying with the protocol, or even what level of compliance
            // is there
            // If the project has no references, then not specifying the protocol is not a problem (or even required)
            var projectImplementsProtocol = projectInstance.GetItems(ProjectReferenceTargets).Count > 0;
            if (projectImplementsProtocol || projectNode.ProjectReferences.Count == 0 || allowProjectsWithoutTargetProtocol)
            {
                // If the project does not implement the protocol (but that's allowed) and has references
                // add all the references to the 'without target protocol' map, so we can add default targets
                // to those as a post-processing step
                if (!projectImplementsProtocol && projectNode.ProjectReferences.Count > 0)
                {
                    foreach (var reference in projectNode.ProjectReferences)
                    {
                        pendingAddDefaultTargets.Add(reference);
                    }
                }

                var targets = targetsPerProject[projectNode];

                // The global properties to use are an augmented version of the original project properties
                globalPropertiesForNode = new GlobalProperties(projectInstance.GlobalProperties);
                failure = string.Empty;
                computedTargets.Add(projectNode, PredictedTargetsToExecute.Create(targets));

                return true;
            }

            // This is the case where the project doesn't implement the protocol, it has non-empty references
            // and projects without a protocol are not allowed.

            failure = $"Project '{projectInstance.FullPath}' is not specifying its project reference protocol. For more details see https://github.com/Microsoft/msbuild/blob/master/documentation/specs/static-graph.md";
            globalPropertiesForNode = GlobalProperties.Empty;

            return false;
        }

        private static void SerializeGraph(ProjectGraphWithPredictionsResult projectGraphWithPredictions, string outputFile, GraphBuilderReporter reporter)
        {
            reporter.ReportMessage("Serializing graph...");

            var serializer = JsonSerializer.Create(ProjectGraphSerializationSettings.Settings);

            using var sw = new StreamWriter(outputFile);
            using var writer = new JsonTextWriter(sw);
            serializer.Serialize(writer, projectGraphWithPredictions);

            reporter.ReportMessage("Done serializing graph.");
        }

        private record ProjectFileAndDimension(string ProjectFile, string Configuration, string Platform)
        {
            public const string ConfigurationProperty = "Configuration";
            public const string PlatformProperty = "Platform";
            private const string FakeDimensionPrefix = "__Dummy__";
            private static int s_freshId = 0;

            /// <summary>
            /// Creates an instance of <see cref="ProjectFileAndDimension"/> from properties <see cref="ConfigurationProperty"/> and <see cref="PlatformProperty"/>,
            /// and returns <code>null</code> if either of the properties is not specified.
            /// </summary>
            public static ProjectFileAndDimension CreateFrom(ProjectInstance projectInstance, IDictionary<string, string> globalProperties = null)
            {
                string projectFile = projectInstance.FullPath;
                globalProperties ??= projectInstance.GlobalProperties;
                string configuration = GetPropertyValue(ConfigurationProperty, projectInstance, globalProperties);
                string platform = GetPropertyValue(PlatformProperty, projectInstance, globalProperties);

                return string.IsNullOrEmpty(configuration) || string.IsNullOrEmpty(platform)
                    ? null
                    : new ProjectFileAndDimension(OperatingSystemHelper.IsUnixOS ? projectFile : projectFile.ToUpperInvariant(), configuration, platform);

                static string GetPropertyValue(string property, ProjectInstance project, IDictionary<string, string> properties) =>
                    properties.TryGetValue(property, out string value)
                        ? value
                        : project.GetPropertyValue(property);
            }

            /// <summary>
            /// Creates a fake and unique <see cref="ProjectFileAndDimension"/>.
            /// </summary>
            public static ProjectFileAndDimension CreateFakeUnique(ProjectInstance projectInstance)
            {
                string projectFile = projectInstance.FullPath;
                int id = s_freshId++;
                return new ProjectFileAndDimension(
                    OperatingSystemHelper.IsUnixOS ? projectFile : projectFile.ToUpperInvariant(),
                    $"{FakeDimensionPrefix}{ConfigurationProperty}{id}",
                    $"{FakeDimensionPrefix}{PlatformProperty}{id}");
            }

            /// <summary>
            /// Checks if this instance is a fake one or not.
            /// </summary>
            public bool IsFake => 
                Configuration.StartsWith(FakeDimensionPrefix, StringComparison.OrdinalIgnoreCase)
                || Platform.StartsWith(FakeDimensionPrefix, StringComparison.OrdinalIgnoreCase);
        }
    }
}
