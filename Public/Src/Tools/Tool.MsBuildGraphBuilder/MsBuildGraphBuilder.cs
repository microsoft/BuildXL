// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BuildXL.FrontEnd.MsBuild.Serialization;
using BuildXL.Utilities.Instrumentation.Common;
using Microsoft.Build.Definition;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Exceptions;
using Microsoft.Build.Execution;
using Microsoft.Build.Experimental.Graph;
using Microsoft.Build.Prediction;
using Newtonsoft.Json;
using ProjectGraphWithPredictionsResult = BuildXL.FrontEnd.MsBuild.Serialization.ProjectGraphWithPredictionsResult<string>;
using ProjectGraphWithPredictions = BuildXL.FrontEnd.MsBuild.Serialization.ProjectGraphWithPredictions<string>;
using ProjectWithPredictions = BuildXL.FrontEnd.MsBuild.Serialization.ProjectWithPredictions<string>;
using BuildXL.Utilities.Collections;

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
        public static void BuildGraphAndSerialize(
            MSBuildGraphBuilderArguments arguments)
        {
            Contract.Requires(arguments != null);

            // Using the standard assembly loader and reporter
            // The output file is used as a unique name to identify the pipe
            using (var reporter = new GraphBuilderReporter(Path.GetFileName(arguments.OutputPath)))
            {
                DoBuildGraphAndSerialize(MsBuildAssemblyLoader.Instance, reporter, arguments);
            }
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
                locatedAssemblyPaths,
                locatedMsBuildPath,
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

                // This is a defensive check to make sure the assembly loader actually honored the search locations provided by the user. The path of the assembly where ProjectGraph
                // comes from has to be one of the provided search locations.
                // If that's not the case, this is really an internal error. For example, the MSBuild dlls we use to compile against (that shouldn't be deployed) somehow sneaked into
                // the deployment. This happened in the past, and it prevents the loader to redirect appropriately.
                Assembly assembly = Assembly.GetAssembly(projectGraph.GetType());
                string assemblylocation = assembly.Location;
                if (!assemblyPathsToLoad.Values.Contains(assemblylocation, StringComparer.InvariantCultureIgnoreCase))
                {
                    return ProjectGraphWithPredictionsResult.CreateFailure(
                        GraphConstructionError.CreateFailureWithoutLocation($"Internal error: the assembly '{assembly.GetName().Name}' was loaded from '{assemblylocation}'. This path doesn't match any of the provided search locations. Please contact the BuildXL team."),
                        assemblyPathsToLoad,
                        locatedMsBuildPath);
                }

                reporter.ReportMessage("Done parsing MSBuild specs.");

                if (!TryConstructGraph(
                    projectGraph,
                    reporter,
                    projectInstanceToProjectCache,
                    graphBuildArguments.EntryPointTargets,
                    projectPredictorsForTesting,
                    graphBuildArguments.AllowProjectsWithoutTargetProtocol,
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
                            failure = $"The qualifier {qualifier.ToString()} is requested, but that is incompatible with the global property '{displayKey}={value}' since the specified values for '{displayKey}' do not agree.";
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

        private static ProjectGraphWithPredictionsResult<string> CreateFailureFromInvalidProjectFile(IReadOnlyDictionary<string, string> assemblyPathsToLoad, string locatedMsBuildPath, InvalidProjectFileException e)
        {
            return ProjectGraphWithPredictionsResult.CreateFailure(
                                GraphConstructionError.CreateFailureWithLocation(
                                    new Location { File = e.ProjectFile, Line = e.LineNumber, Position = e.ColumnNumber },
                                    e.Message),
                                assemblyPathsToLoad,
                                locatedMsBuildPath);
        }

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
            ConcurrentDictionary<ProjectInstance, Project> projectInstanceToProjectCache,
            IReadOnlyCollection<string> entryPointTargets,
            IReadOnlyCollection<IProjectPredictor> projectPredictorsForTesting,
            bool allowProjectsWithoutTargetProtocol,
            out ProjectGraphWithPredictions projectGraphWithPredictions,
            out string failure)
        {
            Contract.Assert(projectGraph != null);

            var projectNodes = new ProjectWithPredictions[projectGraph.ProjectNodes.Count];

            var nodes = projectGraph.ProjectNodes.ToArray();

            // Compute the list of targets to run per project
            reporter.ReportMessage("Computing targets to execute for each project...");

            // This dictionary should be exclusively read only at this point, and therefore thread safe
            var targetsPerProject = projectGraph.GetTargetLists(entryPointTargets.ToArray());

            // Bidirectional access from nodes with predictions to msbuild nodes in order to compute node references in the second pass
            // TODO: revisit the structures, since the projects are known upfront we might be able to use lock-free structures
            var nodeWithPredictionsToMsBuildNodes = new ConcurrentDictionary<ProjectWithPredictions, ProjectGraphNode>(Environment.ProcessorCount, projectNodes.Length);
            var msBuildNodesToNodeWithPredictionIndex = new ConcurrentDictionary<ProjectGraphNode, ProjectWithPredictions>(Environment.ProcessorCount, projectNodes.Length);

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
                failure = $"Cannot create standard predictors. An unexpected error occurred. Please contact BuildPrediction project owners with this stack trace: {ex.ToString()}";
                projectGraphWithPredictions = new ProjectGraphWithPredictions(new ProjectWithPredictions<string>[] { });
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
                Project project = projectInstanceToProjectCache[projectInstance];

                var inputFilePredictions = new List<string>();
                var outputFolderPredictions = new List<string>();

                var predictionCollector = new MsBuildPredictionCollector(inputFilePredictions, outputFolderPredictions, predictionFailures);
                try
                {
                    // Again, be defensive when using arbitrary predictors
                    predictionExecutor.PredictInputsAndOutputs(project, predictionCollector);
                }
                catch(Exception ex)
                {
                    predictionFailures.Enqueue((
                        "Unknown predictor",
                        $"Cannot run static predictor on project '{project.FullPath ?? "Unknown project"}'. An unexpected error occurred. Please contact BuildPrediction project owners with this stack trace: {ex.ToString()}"));
                }

                 if (!TryGetPredictedTargetsAndPropertiesToExecute(
                    projectInstance,
                    msBuildNode,
                    targetsPerProject,
                    computedTargets,
                    pendingAddDefaultTargets,
                    allowProjectsWithoutTargetProtocol,
                    out GlobalProperties globalProperties,
                    out string protocolMissingFailure))
                {
                    predictedTargetFailures.Enqueue(protocolMissingFailure);
                    return;
                }

                projectNodes[i] = new ProjectWithPredictions(
                    projectInstance.FullPath,
                    projectInstance.GetItems(ProjectReferenceTargets).Count > 0,
                    globalProperties,
                    inputFilePredictions,
                    outputFolderPredictions);

                // If projects not implementing the target protocol are blocked, then the list of computed targets is final. So we set it right here
                // to avoid wasted allocations
                // Otherwise, we leave it as a post-processing step. Since we are visiting the graph in parallel, without considering edges at all,
                // we need to wait until all nodes are visited to know the full set of projects that have pending default targets to be added
                if (!allowProjectsWithoutTargetProtocol)
                {
                    projectNodes[i].SetTargetsToExecute(computedTargets[msBuildNode]);
                }

                nodeWithPredictionsToMsBuildNodes[projectNodes[i]] = msBuildNode;
                msBuildNodesToNodeWithPredictionIndex[msBuildNode] = projectNodes[i];
            });

            // There were IO prediction errors.
            if (!predictionFailures.IsEmpty)
            {
                projectGraphWithPredictions = new ProjectGraphWithPredictions(new ProjectWithPredictions<string>[] { });
                failure = $"Errors found during static prediction of inputs and outputs. " +
                    $"{string.Join(", ", predictionFailures.Select(failureWithCulprit => $"[Predicted by: {failureWithCulprit.predictorName}] {failureWithCulprit.failure}"))}";
                return false;
            }

            // There were target prediction errors.
            if (!predictedTargetFailures.IsEmpty)
            {
                projectGraphWithPredictions = new ProjectGraphWithPredictions(new ProjectWithPredictions<string>[] { });
                failure = $"Errors found during target prediction. {string.Join(", ", predictedTargetFailures) }";
                return false;
            }

            // If there are references from (allowed) projects not implementing the target protocol, then we may need to change the already computed targets to add default targets to them
            if (allowProjectsWithoutTargetProtocol)
            {
                Parallel.ForEach(computedTargets.Keys, (ProjectGraphNode projectNode) =>
                {
                    PredictedTargetsToExecute targets = computedTargets[projectNode];

                    if (pendingAddDefaultTargets.Contains(projectNode))
                    {
                        targets = targets.WithDefaultTargetsAppended(projectNode.ProjectInstance.DefaultTargets);
                    }

                    msBuildNodesToNodeWithPredictionIndex[projectNode].SetTargetsToExecute(targets);
                });
            }

            // Second pass
            // Reconstruct all references. A two-pass approach avoids needing to do more complicated reconstruction of references that would need traversing the graph
            foreach (var projectWithPredictions in projectNodes)
            {
                // TODO: temporarily getting this as a set due to an MSBuild bug where edges are sometimes duplicated. We can just
                // treat this as an array once the bug is fixed on MSBuild side
                var references = nodeWithPredictionsToMsBuildNodes[projectWithPredictions]
                    .ProjectReferences
                    .Select(projectReference => msBuildNodesToNodeWithPredictionIndex[projectReference])
                    .ToHashSet();

                projectWithPredictions.SetReferences(references);
            }

            reporter.ReportMessage("Done predicting inputs and outputs.");

            projectGraphWithPredictions = new ProjectGraphWithPredictions(projectNodes);
            failure = string.Empty;
            return true;
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
            computedTargets = null;
            globalPropertiesForNode = GlobalProperties.Empty;

            return false;
        }

        private static void SerializeGraph(ProjectGraphWithPredictionsResult projectGraphWithPredictions, string outputFile, GraphBuilderReporter reporter)
        {
            reporter.ReportMessage("Serializing graph...");

            var serializer = JsonSerializer.Create(ProjectGraphSerializationSettings.Settings);

            using (StreamWriter sw = new StreamWriter(outputFile))
            using (JsonWriter writer = new JsonTextWriter(sw))
            {
                serializer.Serialize(writer, projectGraphWithPredictions);
            }

            reporter.ReportMessage("Done serializing graph.");
        }
    }
}
