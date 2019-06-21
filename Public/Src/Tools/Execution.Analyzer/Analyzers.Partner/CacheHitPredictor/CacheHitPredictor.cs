// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
#if FEATURE_VSTS_ARTIFACTSERVICES

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using BuildXL.Execution.Analyzer.Analyzers;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Graph;
using BuildXL.Scheduler.IncrementalScheduling;
using BuildXL.Scheduler.Tracing;
using BuildXL.ToolSupport;
using BuildXL.Utilities.Instrumentation.Common;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Microsoft.TeamFoundation.SourceControl.WebApi;
using Microsoft.VisualStudio.Services.Client;
using Microsoft.VisualStudio.Services.WebApi;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        public Analyzer InitializeCacheHitPredictor()
        {
            string outputFilePath = null;
            string vstsURL = null;
            string cachedBuildCommitId = null;
            string buildToAnalyzeCommitId = null;
            string repositoryName = null;
            
            foreach (var opt in AnalyzerOptions)
            {
                if (opt.Name.Equals("outputFile", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.Equals("o", StringComparison.OrdinalIgnoreCase))
                {
                    outputFilePath = ParseSingletonPathOption(opt, outputFilePath);
                }
                else if (opt.Name.Equals("vstsURL", StringComparison.OrdinalIgnoreCase))
                {
                    vstsURL = ParseStringOption(opt);
                    if (string.IsNullOrEmpty(vstsURL))
                    {
                        throw Error("Invalid base URL.");
                    }
                }
                else if (opt.Name.Equals("cachedBuildCommitId", StringComparison.OrdinalIgnoreCase))
                {
                    cachedBuildCommitId = ParseStringOption(opt);
                    if (string.IsNullOrEmpty(cachedBuildCommitId))
                    {
                        throw Error("Invalid cachedBuildCommitId");
                    }
                }
                else if (opt.Name.Equals("buildToAnalyzeCommitId", StringComparison.OrdinalIgnoreCase))
                {
                    buildToAnalyzeCommitId = ParseStringOption(opt);
                    if (string.IsNullOrEmpty(buildToAnalyzeCommitId))
                    {
                        throw Error("Invalid buildToAnalyzeCommitId");
                    }
                }
                else if (opt.Name.Equals("repositoryName", StringComparison.OrdinalIgnoreCase))
                {
                    repositoryName = ParseStringOption(opt);
                    if (string.IsNullOrEmpty(repositoryName))
                    {
                        throw Error("Invalid repositoryName");
                    }
                }
                else
                {
                    throw Error("Unknown option for cache hit predictor: {0}", opt.Name);
                }
            }

            if (string.IsNullOrEmpty(outputFilePath))
            {
                throw Error("/outputFile parameter is required");
            }

            return new CacheHitPredictor(GetAnalysisInput(), outputFilePath, vstsURL, repositoryName, cachedBuildCommitId, buildToAnalyzeCommitId);
        }

        private static void WriteCacheHitPredictorHelp(HelpWriter writer)
        {
            writer.WriteBanner("Cache Hit Predictor");
            writer.WriteModeOption(nameof(AnalysisMode.CacheHitPredictor), "Compares two commits of a git repo and estimates how many cache hits the second build will get assuming the first one was cached." +
                "The assumption of this predictor is that BuildXL is invoked directly on the sources that the commit IDs represent: if other tools run before that and introduce file changes that affect the inputs" +
                "for that build, this predictor will miss those changes that may lower the cache hit rate.");
            writer.WriteOption("outputFile", "Required. The location of the output file for critical path analysis.", shortName: "o");
            writer.WriteOption("vstsURL", "Required. The base URL of the VSTS project where the comparison takes place.");
            writer.WriteOption("repositoryName", "Required. The name of the VSTS repository where the comparison takes place.");
            writer.WriteOption("cachedBuildCommitId", "Required. The commit ID (in SHA form) of the build that is presumably cached. " +
                "This commit ID must match the state on disk that the build whose XLG or graph directory being provided found when the build started.");
            writer.WriteOption("buildToAnalyzeCommitId", "Required. The commit ID (in SHA form) of the build whose cache hit rate is to be predicted.");
        }
    }

    /// <summary>
    /// Provides a cache hit estimation based on diffing two VSTS git commits and feeding the changes to incremental scheduler
    /// </summary>
    /// <remarks>
    /// The provided XLG must match a build running against the provided cached build commit ID.
    /// The assumption of this predictor is that BuildXL is invoked directly on the sources that the commit IDs represent: if other tools run before that and introduce file changes that affect the inputs
    /// for that build, this predictor will miss those changes that may lower the cache hit rate
    /// </remarks>
    internal sealed class CacheHitPredictor : Analyzer
    {
        private readonly string m_cachedGraphDirectory;
        private string m_outputFilePath;
        private readonly string m_vstsURL;
        private readonly string m_cachedBuildCommitId;
        private readonly string m_buildToAnalyzeCommitId;
        private readonly string m_repositoryName;

        // Pips that were actually executed (as opposed to pips that are part of the pip graph)
        private readonly HashSet<PipId> m_executedPips = new HashSet<PipId>();

        // TODO: add these VSTS-related parameters to the optional list of parameters the analyzer can take
        // VSTS service principal.
        private string Resource = "499b84ac-1321-427f-aa17-267ca6975798";
        // Visual Studio IDE client ID originally provisioned by Azure Tools.
        private string Client = "872cd9fa-d31f-45e0-9eab-6e460a02d1f1";
        private Uri RedirectUri = new Uri("urn:ietf:wg:oauth:2.0:oob");
        // Microsoft authority
        private string Authority = "https://login.windows.net/microsoft.com";

        internal CacheHitPredictor(AnalysisInput input, string outputFilePath, string vstsURL, string repositoryName, string cachedBuildCommitId, string buildToAnalyzeCommitId)
            : base(input)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(outputFilePath));
            Contract.Requires(!string.IsNullOrWhiteSpace(vstsURL));
            Contract.Requires(!string.IsNullOrWhiteSpace(cachedBuildCommitId));
            Contract.Requires(!string.IsNullOrWhiteSpace(buildToAnalyzeCommitId));
            Contract.Requires(!string.IsNullOrWhiteSpace(repositoryName));
            
            m_outputFilePath = outputFilePath;
            m_vstsURL = vstsURL;
            m_cachedBuildCommitId = cachedBuildCommitId;
            m_buildToAnalyzeCommitId = buildToAnalyzeCommitId;
            m_cachedGraphDirectory = input.CachedGraphDirectory;
            m_repositoryName = repositoryName;
        }

        public override int Analyze()
        {
            var incrementalSchedulingState = LoadIncrementalSchedulingState();

            if (incrementalSchedulingState == null)
            {
                Console.Error.WriteLine("Unable to load incremental scheduling state.");
                return 1;
            }

            Console.WriteLine(I($"Getting VSTS credentials."));
            var credential = GetVSTSCredential();

            var vstsUrl = new Uri(this.m_vstsURL);

            Console.WriteLine(I($"Connecting to VSTS project '{vstsUrl}'"));

            using (VssConnection connection = new VssConnection(vstsUrl, credential))
            using (var client = connection.GetClient<GitHttpClient>())
            {
                // Find the repository and diff the provided commits
                var repositories = client.GetRepositoriesAsync().GetAwaiter().GetResult();
                var repository = repositories.FirstOrDefault(repo => repo.Name == m_repositoryName);

                if (repository == null)
                {
                    Console.WriteLine(I($"Repository '{m_repositoryName}' was not found in '{this.m_vstsURL}'."));
                    return 1;
                }

                Console.WriteLine(I($"Found repository {repository.RemoteUrl}"));
                Console.WriteLine(I($"Diffing '{m_cachedBuildCommitId}' and '{m_buildToAnalyzeCommitId}'"));

                var treeDiff = client.GetTreeDiffsAsync(repository.ProjectReference.Id, repository.Id, m_cachedBuildCommitId, m_buildToAnalyzeCommitId).GetAwaiter().GetResult();

                // Map the changes into events the incremental scheduling state can observe
                var observableChanges = new GitFileChangeObservable(treeDiff, PathTable);

                Console.WriteLine(I($"Found {observableChanges.Changes.Count} relevant changes."));

                // 'Advance' the incremental scheduling state based on those events
                observableChanges.Subscribe(incrementalSchedulingState);

                ReportCacheHitEstimates(incrementalSchedulingState);
            }

            return 0;
        }

        private void ReportCacheHitEstimates(IIncrementalSchedulingState incrementalSchedulingState)
        {
            var allDirtyNodes =
                                incrementalSchedulingState.DirtyNodeTracker.AllDirtyNodes.Union(
                                    incrementalSchedulingState.DirtyNodeTracker.AllPerpertuallyDirtyNodes);

            var allDirtyProcesses =
                allDirtyNodes.Where(nodeId => incrementalSchedulingState.PipGraph.PipTable.GetPipType(nodeId.ToPipId()) == PipType.Process);
            var allDirtyProcessesCount = allDirtyProcesses.Count();

            var allProcesses = incrementalSchedulingState.PipGraph.PipTable.Keys.Where(
                pipId => incrementalSchedulingState.PipGraph.PipTable.GetPipType(pipId) == PipType.Process);
            var allProcessesCount = allProcesses.Count();

            Console.WriteLine(I($"Total processes: {allProcessesCount}."));
            Console.WriteLine(I($"Dirty processes: {allDirtyProcessesCount}."));
            Console.WriteLine(I($"Estimated cache hit rate for an unfiltered build: {(double)(allProcessesCount - allDirtyProcessesCount) / allProcessesCount * 100:0.##}%."));

            if (Input.ExecutionLogPath != null)
            {
                var allDirtyProcessesToExecute = allDirtyProcesses.Where(process => m_executedPips.Contains(process.ToPipId()));
                var allDirtyProcessesToExecuteCount = allDirtyProcessesToExecute.Count();
                Console.WriteLine(I($"Dirty processes included in the filter: {allDirtyProcessesToExecuteCount}."));
                Console.WriteLine(
                    I($"Estimated cache hit rate for build with same filter: {(double)(allProcessesCount - allDirtyProcessesToExecuteCount) / allProcessesCount * 100:0.##}%."));
            }
            else
            {
                Console.WriteLine($"Execution log not available. If next time you provide the execution log (using /executionLog), an additional estimation can be provided including the cached build filter.");
            }
        }

        private VssAadCredential GetVSTSCredential()
        {
            var authenticationContext = new AuthenticationContext(Authority);
            var platformParameters = new PlatformParameters(PromptBehavior.Auto, customWebUi: null);

            var authenticationResult = authenticationContext.AcquireTokenAsync(Resource, Client, RedirectUri, platformParameters).GetAwaiter().GetResult();
            var credential = new VssAadCredential(new VssAadToken(authenticationResult));
            return credential;
        }

        private IIncrementalSchedulingState LoadIncrementalSchedulingState()
        {
            Console.Error.WriteLine("Loading incremental scheduling state from '" + m_cachedGraphDirectory);

            var incrementalSchedulingStateFile = Path.Combine(m_cachedGraphDirectory, Scheduler.Scheduler.DefaultIncrementalSchedulingStateFile);
            var loggingContext = new LoggingContext(nameof(CacheHitPredictor));
            var factory = new IncrementalSchedulingStateFactory(loggingContext, analysisMode: true);

            var incrementalSchedulingState = factory.LoadOrReuseIgnoringFileEnvelope(
                CachedGraph.PipGraph,
                null,
                WellKnownContentHashes.AbsentFile,
                incrementalSchedulingStateFile,
                schedulerState: null);

            return incrementalSchedulingState;
        }

        public override void PipExecutionStepPerformanceReported(PipExecutionStepPerformanceEventData data)
        {
            m_executedPips.Add(data.PipId);
        }
    }
}

#endif