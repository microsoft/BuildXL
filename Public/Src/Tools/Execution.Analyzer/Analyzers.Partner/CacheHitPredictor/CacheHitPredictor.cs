// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#if FEATURE_VSTS_ARTIFACTSERVICES

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Execution.Analyzer.Analyzers;
using BuildXL.Pips;
using BuildXL.Pips.Graph;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Scheduler.IncrementalScheduling;
using BuildXL.Scheduler.Tracing;
using BuildXL.ToolSupport;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Instrumentation.Common;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;
using Microsoft.TeamFoundation.SourceControl.WebApi;
using Microsoft.VisualStudio.Services.Client;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using static BuildXL.Utilities.Core.FormattableStringEx;

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
        private readonly string m_tokenCacheDirectory;
        private readonly string m_tokenCacheFileName = "buildxl_msalcache";

        // Pips that were actually executed (as opposed to pips that are part of the pip graph)
        private readonly HashSet<PipId> m_executedPips = new HashSet<PipId>();

        // TODO: add these VSTS-related parameters to the optional list of parameters the analyzer can take
        // MSAL scope representation of the VSO service principal
        private const string Scope = $"499b84ac-1321-427f-aa17-267ca6975798/.default";
        // TenantId for Microsoft tenant
        private const string MicrosoftTenantId = "72f988bf-86f1-41af-91ab-2d7cd011db47";
        // Visual Studio IDE client ID originally provisioned by Azure Tools.
        private const string Client = "872cd9fa-d31f-45e0-9eab-6e460a02d1f1";
        // NET Core apps will need a redirect uri to retrieve the token, localhost picks an open port by default
        // https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/wiki/System-Browser-on-.Net-Core
        private const string RedirectUri = "http://localhost";
        // The type of Token returned
        private const string TokenType = "Bearer";

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

            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            m_tokenCacheDirectory = OperatingSystemHelper.IsWindowsOS
                ? Path.Combine(userProfile, "AppData", "Local", "BuildXL", "MsalTokenCache")
                : Path.Combine(userProfile, ".BuildXL", "MsalTokenCache");
            if (!Directory.Exists(m_tokenCacheDirectory))
            {
                Directory.CreateDirectory(m_tokenCacheDirectory);
            }
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
            var credential = CreateVssCredentialsWithAadAsync().GetAwaiter().GetResult();

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

        /// <summary>
        /// Calls MSAL to get authentication token with AAD. 
        /// CODESYNC: Public\src\Utilities\Authentication\VssCredentialsFactory.cs
        /// </summary>
        /// <remarks>https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/wiki/Acquiring-tokens-interactively</remarks>
        private async Task<VssCredentials> CreateVssCredentialsWithAadAsync()
        {
            // 1. Configuration
            var app = PublicClientApplicationBuilder
                .Create(Client)
                .WithTenantId(MicrosoftTenantId)
                .WithRedirectUri(RedirectUri)
                .Build();

            // 2. Token cache
            // https://docs.microsoft.com/en-us/azure/active-directory/develop/msal-net-token-cache-serialization?tabs=desktop
            // https://github.com/AzureAD/microsoft-authentication-extensions-for-dotnet/wiki/Cross-platform-Token-Cache
            var storageProperties = new StorageCreationPropertiesBuilder(m_tokenCacheFileName, m_tokenCacheDirectory).Build();
            var cacheHelper = await MsalCacheHelper.CreateAsync(storageProperties);
            cacheHelper.RegisterCache(app.UserTokenCache);

            // 3. Try silent authentication
            var accounts = await app.GetAccountsAsync();
            var scopes = new string[] { Scope };
            AuthenticationResult result = null;

            try
            {
                result = await app.AcquireTokenSilent(scopes, accounts.FirstOrDefault())
                    .ExecuteAsync();
                Console.WriteLine("Successfully acquired authentication token through silent AAD authentication.");
            }
            catch (MsalUiRequiredException)
            {
                // 4. Interactive Authentication
                Console.Write("Unable to acquire authentication token through silent AAD authentication.");
                // On Windows, we can try Integrated Windows Authentication which will fallback to interactive auth if that fails
                result = OperatingSystemHelper.IsWindowsOS
                    ? await CreateVssCredentialsWithAadForWindowsAsync(app, scopes)
                    : await CreateVssCredentialsWithAadInteractiveAsync(app, scopes);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unable to acquire credentials with AAD with the following exception: '{ex}'");
            }

            if (result == null)
            {
                // Something went wrong during AAD auth, return null
                Console.WriteLine($"Unable to acquire AAD token.");
                return new VssAadCredential();
            }

            return new VssAadCredential(new VssAadToken(TokenType, result.AccessToken));
        }

        /// <summary>
        /// Tries integrated windows auth first before trying interactive authentication.
        /// </summary>
        /// <remarks>https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/wiki/wam</remarks>
        private async Task<AuthenticationResult> CreateVssCredentialsWithAadForWindowsAsync(IPublicClientApplication app, string[] scopes)
        {
            AuthenticationResult result = null;
            try
            {
                result = await app.AcquireTokenByIntegratedWindowsAuth(scopes)
                                  .ExecuteAsync();
                Console.WriteLine("Integrated Windows Authentication was successful.");
            }
            catch (MsalUiRequiredException)
            {
                result = await CreateVssCredentialsWithAadInteractiveAsync(app, scopes);
            }
            catch (MsalServiceException serviceException)
            {
                Console.WriteLine($"Unable to acquire credentials with interactive Windows AAD auth with the following exception: '{serviceException}'");
            }
            catch (MsalClientException clientException)
            {
                Console.WriteLine($"Unable to acquire credentials with interactive Windows AAD auth with the following exception: '{clientException}'");
            }

            return result;
        }

        /// <summary>
        /// Interactive authentication that will open a browser window for a user to sign in if they are not able to get silent auth or integrated windows auth.
        /// </summary>
        private async Task<AuthenticationResult> CreateVssCredentialsWithAadInteractiveAsync(IPublicClientApplication app, string[] scopes)
        {
            Console.WriteLine("Using interactive AAD authentication.");
            var result = await app.AcquireTokenInteractive(scopes)
                .WithPrompt(Prompt.SelectAccount)
                .WithUseEmbeddedWebView(false)
                .ExecuteAsync();
            return result;
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
                UnsafeOptions.PreserveOutputsNotUsed,
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