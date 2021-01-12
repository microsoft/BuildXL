// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.FrontEnd.Download.Tracing;
using BuildXL.FrontEnd.Script;
using BuildXL.FrontEnd.Script.Declarations;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Sdk.Evaluation;
using BuildXL.FrontEnd.Sdk.Mutable;
using BuildXL.FrontEnd.Sdk.Workspaces;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Native.IO;
using BuildXL.Pips;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Tar;
using ICSharpCode.SharpZipLib.Zip;
using JetBrains.Annotations;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.Download
{
    /// <summary>
    /// NuGet resolver frontend
    /// </summary>
    public sealed class DownloadResolver : IResolver
    {
        // Constant value to target Azure DevOps.
        private const string Resource = "499b84ac-1321-427f-aa17-267ca6975798";
        // Visual Studio IDE client ID originally provisioned by Azure Tools.
        private const string Client = "872cd9fa-d31f-45e0-9eab-6e460a02d1f1";
        // Microsoft tenant
        private const string Tenant = "microsoft.com";
        // Microsoft authority
        private readonly string Authority = $"https://login.windows.net/{Tenant}";

        private readonly FrontEndHost m_frontEndHost;
        private readonly FrontEndContext m_context;
        private readonly Logger m_logger;
        private DownloadWorkspaceResolver m_workspaceResolver;

        private readonly ConcurrentDictionary<string, Lazy<Task<EvaluationResult>>> m_downloadResults =
            new ConcurrentDictionary<string, Lazy<Task<EvaluationResult>>>(StringComparer.Ordinal);

        private readonly ConcurrentDictionary<string, Lazy<Task<EvaluationResult>>> m_extractResults =
            new ConcurrentDictionary<string, Lazy<Task<EvaluationResult>>>(StringComparer.Ordinal);

        /// <nodoc />
        public string Name { get; private set; }

        /// <nodoc />
        internal Statistics Statistics { get; }

        /// <nodoc/>
        public DownloadResolver(
            Statistics statistics,
            FrontEndHost frontEndHost,
            FrontEndContext context,
            Logger logger,
            string frontEndName)
        {
            Contract.Requires(!string.IsNullOrEmpty(frontEndName));

            Name = frontEndName;
            Statistics = statistics;
            m_frontEndHost = frontEndHost;
            m_context = context;
            m_logger = logger;
        }

        /// <inheritdoc />
        public Task<bool> InitResolverAsync([NotNull] IResolverSettings resolverSettings, object workspaceResolver)
        {
            m_workspaceResolver = workspaceResolver as DownloadWorkspaceResolver;

            if (m_workspaceResolver == null)
            {
                Contract.Assert(false, I($"Wrong type for resolver, expected {nameof(DownloadWorkspaceResolver)} but got {nameof(workspaceResolver.GetType)}"));
            }

            Name = resolverSettings.Name;

            return Task.FromResult(true);
        }

        /// <inheritdoc />
        public void LogStatistics()
        {
            // Statistics are logged in the FrontEnd.
        }

        /// <inheritdoc />
        public void NotifyEvaluationFinished()
        {
            // Nothing to do
        }

        /// <inheritdoc />
        public Task<bool?> TryConvertModuleToEvaluationAsync(IModuleRegistry moduleRegistry, ParsedModule module, IWorkspace workspace)
        {
            if (!string.Equals(module.Descriptor.ResolverName, Name, StringComparison.Ordinal))
            {
                return Task.FromResult<bool?>(null);
            }

            var downloadData = m_workspaceResolver.Downloads[module.Descriptor.Name];

            var package = CreatePackage(module.Definition);

            Contract.Assert(module.Specs.Count == 1, "This resolver generated the module, so we expect a single spec.");
            var sourceKv = module.Specs.First();

            var sourceFilePath = sourceKv.Key;
            var sourceFile = sourceKv.Value;

            var currentFileModule = ModuleLiteral.CreateFileModule(
                sourceFilePath,
                moduleRegistry,
                package,
                sourceFile.LineMap);

            // Download
            var downloadSymbol = FullSymbol.Create(m_context.SymbolTable, downloadData.DownloadedValueName);
            var downloadResolvedEntry = new ResolvedEntry(
                downloadSymbol,
                (Context context, ModuleLiteral env, EvaluationStackFrame args) => DownloadFile(downloadData),
                // The following position is a contract right now iwtht he generated ast in the workspace resolver
                // we have to find a nicer way to handle and register these.
                TypeScript.Net.Utilities.LineInfo.FromLineAndPosition(0, 1)
            );
            currentFileModule.AddResolvedEntry(downloadSymbol, downloadResolvedEntry);
            currentFileModule.AddResolvedEntry(new FilePosition(1, sourceFilePath), downloadResolvedEntry);

            // Contents.All
            var extractedSymbol = FullSymbol.Create(m_context.SymbolTable, downloadData.ExtractedValueName);
            var contentsResolvedEntry = new ResolvedEntry(
                extractedSymbol,
                (Context context, ModuleLiteral env, EvaluationStackFrame args) => ExtractFile(downloadData),
                // The following position is a contract right now iwtht he generated ast in the workspace resolver
                // we have to find a nicer way to handle and register these.
                TypeScript.Net.Utilities.LineInfo.FromLineAndPosition(0, 3)
            );
            currentFileModule.AddResolvedEntry(extractedSymbol, contentsResolvedEntry);
            currentFileModule.AddResolvedEntry(new FilePosition(3, sourceFilePath), contentsResolvedEntry);

            var moduleInfo = new UninstantiatedModuleInfo(
                // We can register an empty one since we have the module populated properly
                new SourceFile(
                    sourceFilePath,
                    new Declaration[]
                    {
                    }),
                currentFileModule,
                m_context.QualifierTable.EmptyQualifierSpaceId);

            moduleRegistry.AddUninstantiatedModuleInfo(moduleInfo);

            return Task.FromResult<bool?>(true);
        }

        #region Download logic

        /// <summary>
        /// Downloads a file with in-memory caching for evaluation.
        /// </summary>
        internal Task<EvaluationResult> DownloadFile(DownloadData downloadData)
        {
            var result = m_downloadResults.GetOrAdd(
                downloadData.Settings.ModuleName,
                _ => Lazy.Create(() => PerformDownloadOrIncrementalCheckAsync(downloadData)));

            return result.Value;
        }

        /// <summary>
        /// Downloads a file with file backed manifest incremental check
        /// </summary>
        internal async Task<EvaluationResult> PerformDownloadOrIncrementalCheckAsync(DownloadData downloadData)
        {
            if (m_context.CancellationToken.IsCancellationRequested)
            {
                return EvaluationResult.Canceled;
            }

            Statistics.Downloads.Total.Increment();

            using (Statistics.Downloads.UpToDateCheckDuration.Start(downloadData.Settings.Url))
            {
                var result = await CheckIfDownloadIsNeededAsync(downloadData);
                if (result.IsErrorValue)
                {
                    Statistics.Downloads.Failures.Increment();
                }

                if (result != EvaluationResult.Continue)
                {
                    Statistics.Downloads.SkippedDueToManifest.Increment();
                    return result;
                }
            }

            using (Statistics.Downloads.Duration.Start(downloadData.Settings.Url))
            {
                var result = await TryDownloadFileToDiskAsync(downloadData);
                if (result.IsErrorValue)
                {
                    Statistics.Downloads.Failures.Increment();
                }

                if (result != EvaluationResult.Continue)
                {
                    return result;
                }
            }

            using (Statistics.Downloads.UpToDateCheckDuration.Start(downloadData.Settings.Url))
            {
                var result = await ValidateAndStoreIncrementalDownloadStateAsync(downloadData);
                if (result.IsErrorValue)
                {
                    Statistics.Downloads.Failures.Increment();
                }

                return result;
            }
        }


        /// <summary>
        /// Checks if we actually need to download of if the disk is up to date.
        /// </summary>
        /// <returns>Returns EvaluationResult.Continue if we still need to download, else the result will be what should be returned</returns>
        private async Task<EvaluationResult> CheckIfDownloadIsNeededAsync(DownloadData downloadData)
        {
            try
            {
                var downloadFilePath = downloadData.DownloadedFilePath.ToString(m_context.PathTable);
                // Check if the file already exists and matches the expected hash.
                if (File.Exists(downloadFilePath))
                {
                    var expectedHashType = downloadData.ContentHash.HasValue
                        ? downloadData.ContentHash.Value.HashType
                        : HashTypeParser(downloadData.Settings.Hash);
                    
                    // We don't record the file until we know it is the one used in this build.
                    var recordFileAccess = false;
                    var actualHash = await GetContentHashAsync(downloadData.DownloadedFilePath, expectedHashType, recordFileAccess);

                    // Compare actual hash to compare if we need to download again.
                    // Compare against the static hash value.
                    if (downloadData.ContentHash.HasValue && actualHash == downloadData.ContentHash.Value)
                    {
                        // Record the file with the build's default hasher.
                        m_frontEndHost.Engine.RecordFrontEndFile(downloadData.DownloadedFilePath, Name);
                        return new EvaluationResult(FileArtifact.CreateSourceFile(downloadData.DownloadedFilePath));
                    }

                    var incrementalState = await DownloadIncrementalState.TryLoadAsync(m_logger, m_context, downloadData);
                    if (incrementalState != null && incrementalState.ContentHash == actualHash)
                    {
                        // Record the file with the build's default hasher.
                        m_frontEndHost.Engine.RecordFrontEndFile(downloadData.DownloadedFilePath, Name);
                        return new EvaluationResult(FileArtifact.CreateSourceFile(downloadData.DownloadedFilePath));
                    }
                }
            }
            catch (IOException e)
            {
                m_logger.ErrorCheckingIncrementality(m_context.LoggingContext, downloadData.Settings.ModuleName, e.Message);
                return EvaluationResult.Error;
            }
            catch (UnauthorizedAccessException e)
            {
                m_logger.ErrorCheckingIncrementality(m_context.LoggingContext, downloadData.Settings.ModuleName, e.Message);
                return EvaluationResult.Error;
            }

            // Download is needed
            return EvaluationResult.Continue;
        }

        /// <summary>
        /// Attempts to downoad the file to disk.
        /// </summary>
        /// <returns>Returns EvaluationResult.Continue if we sucessfully downloaded and need to continue to store the incremental information, else the result will be what should be returned</returns>
        private async Task<EvaluationResult> TryDownloadFileToDiskAsync(DownloadData downloadData)
        {
            var downloadFilePathAsString = downloadData.DownloadedFilePath.ToString(m_context.PathTable);

            try
            {
                FileUtilities.CreateDirectory(Path.GetDirectoryName(downloadFilePathAsString));
                FileUtilities.DeleteFile(downloadFilePathAsString, retryOnFailure: true);
            }
            catch (BuildXLException e)
            {
                m_logger.ErrorPreppingForDownload(m_context.LoggingContext, downloadData.Settings.ModuleName, e.Message);
                return EvaluationResult.Error;
            }

            // We have to download the file.
            m_logger.StartDownload(m_context.LoggingContext, downloadData.Settings.ModuleName, downloadData.Settings.Url);
            var stopwatch = Stopwatch.StartNew();
            try
            {
                using (var httpClient = new HttpClient())
                {
                    httpClient.Timeout = TimeSpan.FromMinutes(10);

                    var httpRequest = new HttpRequestMessage(HttpMethod.Get, downloadData.DownloadUri);

                    // If the download URI is pointing to a VSTS feed and we get a valid auth token, make it part of the request
                    // We only want to send the token over HTTPS and to a VSTS domain to avoid security issues
                    if (IsVSTSPackageSecureURI(downloadData.DownloadUri) && 
                        await TryGetAuthenticationHeaderAsync(downloadData.DownloadUri) is var authHeader &&
                        authHeader != null)
                    {
                        httpRequest.Headers.Accept.Clear();
                        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                        httpRequest.Headers.Authorization = authHeader;
                    }

                    var response = await httpClient.SendAsync(httpRequest, m_context.CancellationToken);
                    response.EnsureSuccessStatusCode();
                    var stream = await response.Content.ReadAsStreamAsync();

                    using (var targetStream = new FileStream(downloadFilePathAsString, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await stream.CopyToAsync(targetStream);
                    }

                    m_logger.Downloaded(
                        m_context.LoggingContext,
                        downloadData.Settings.ModuleName,
                        downloadData.Settings.Url,
                        stopwatch.ElapsedMilliseconds,
                        new FileInfo(downloadFilePathAsString).Length);
                }
            }
            catch (TaskCanceledException e)
            {
                string message = m_context.CancellationToken.IsCancellationRequested ? "Download manually canceled." : e.Message;
                m_logger.DownloadFailed(m_context.LoggingContext, downloadData.Settings.ModuleName, downloadData.Settings.Url, message);
                return EvaluationResult.Canceled;
            }
            catch (HttpRequestException e)
            {
                var message = e.InnerException == null
                    ? e.Message
                    : e.Message + " " + e.InnerException?.Message;
                m_logger.DownloadFailed(m_context.LoggingContext, downloadData.Settings.ModuleName, downloadData.Settings.Url, message);
                return EvaluationResult.Error;
            }
            catch (IOException e)
            {
                m_logger.DownloadFailed(m_context.LoggingContext, downloadData.Settings.ModuleName, downloadData.Settings.Url, e.Message);
                return EvaluationResult.Error;
            }
            catch (UnauthorizedAccessException e)
            {
                m_logger.DownloadFailed(m_context.LoggingContext, downloadData.Settings.ModuleName, downloadData.Settings.Url, e.Message);
                return EvaluationResult.Error;
            }

            // Indicate we should continue to store the incremental information
            return EvaluationResult.Continue;
        }

        private bool IsVSTSPackageSecureURI(Uri downloadURI)
        {
            return downloadURI.Host.EndsWith(".pkgs.visualstudio.com", StringComparison.OrdinalIgnoreCase) && downloadURI.Scheme == "https";
        }

        /// <summary>
        /// Tries to authenticate using a credential provider and fallback to IWA if that fails.
        /// </summary>
        private async Task<AuthenticationHeaderValue> TryGetAuthenticationHeaderAsync(Uri uri)
        {
            var result = await TryGetAuthenticationHeaderValueWithCredentialProviderAsync(uri);
            if (result != null)
            {
                return result;
            }

            return await TryGetAuthenticationHeaderWithIWAAsync(uri);
        }

        /// <summary>
        /// Tries to get an authentication token using Integrated Windows Authentication with the current logged in user
        /// </summary>
        /// <returns>Null if authentication fails</returns>
        /// <remarks>
        /// The auth token is acquired to address simple auth cases for retrieving packages from VSTS feeds from Windows domain
        /// joined machines where IWA is enabled.
        /// See https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/wiki/Integrated-Windows-Authentication
        /// </remarks>
        private async Task<AuthenticationHeaderValue> TryGetAuthenticationHeaderWithIWAAsync(Uri uri)
        {
            var authenticationContext = new AuthenticationContext(Authority);

            try
            {
                var userCredential = new UserCredential($"{Environment.UserName}@{Tenant}");
 
                // Many times the user UPN cannot be automatically retrieved, so we build it based on the current username and tenant. This might
                // not be perfect but should work for most cases. Getting the UPN for a given user from AD requires authentication as well.
                var result = await authenticationContext.AcquireTokenAsync(Resource, Client, userCredential); ;
                return new AuthenticationHeaderValue("Bearer", result.AccessToken);
            }
            catch (AdalException ex)
            {
                m_logger.AuthenticationViaIWAFailed(m_context.LoggingContext, uri.AbsoluteUri, $"Failed to authorize via IWA: {ex.Message}");
                // Getting an auth token via IWA is on a best effort basis. If anything fails, we silently continue without auth
                return null;
            }
        }

        /// <summary>
        /// Tries to get an authentication token using a credential provider
        /// </summary>
        /// <returns>Null if authentication fails</returns>
        /// <remarks>
        /// The credential provider is discovered following the definition here: https://docs.microsoft.com/en-us/nuget/reference/extensibility/nuget-exe-credential-providers
        /// using an environment variable.
        /// Observe the link above is for nuget specifically, but the authentication here is used for VSTS feeds in general
        /// </remarks>
        private async Task<AuthenticationHeaderValue> TryGetAuthenticationHeaderValueWithCredentialProviderAsync(Uri uri)
        {
            string credentialProviderPath = DiscoverCredentialProvider(uri);

            if (credentialProviderPath == null)
            {
                // Failure has been logged already
                return null;
            }

            // Call the provider with the requested URI in non-interactive mode.
            var processInfo = new ProcessStartInfo
            {
                FileName = credentialProviderPath,
                Arguments = $"-Uri {uri.AbsoluteUri} -NonInteractive",
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };

            using (var process = Process.Start(processInfo))
            {
                try
                {
                    // Register into the cancellation token to kill the process on cancellation
                    using (var registration = m_context.CancellationToken.Register(() =>
                    {
                        try
                        {
                            process?.Kill();
                        }
                        catch (InvalidOperationException)
                        {
                            // The process has already been terminated
                        }
                    }))
                    {
                        process?.WaitForExit();
                    }
                }
                catch (Exception e) when (e is SystemException || e is Win32Exception)
                {
                    m_logger.AuthenticationViaCredentialProviderFailed(m_context.LoggingContext, uri.AbsoluteUri, $"Credential provider execution '{credentialProviderPath}' failed. Error: {e.Message}");
                    return null;
                }

                if (process == null)
                {
                    // The process was not started
                    m_logger.AuthenticationViaCredentialProviderFailed(m_context.LoggingContext, uri.AbsoluteUri, $"Could not start credential provider process '{credentialProviderPath}'.");
                    return null;
                }

                // Check whether the authorization succeeded
                if (process.ExitCode == 0)
                {
                    try
                    {
                        // The response should be a well-formed JSON with a 'password' entry representing the PAT
                        var response = (JObject)await JToken.ReadFromAsync(new JsonTextReader(process.StandardOutput));
                        var pat = response.GetValue("Password");

                        if (pat == null)
                        {
                            m_logger.AuthenticationViaCredentialProviderFailed(m_context.LoggingContext, uri.AbsoluteUri, $"Could not find a 'password' entry in the JSON response of '{credentialProviderPath}'.");
                            return null;
                        }

                        // We got a PAT back. Create an authentication header with a base64 encoding of the retrieved PAT
                        return new AuthenticationHeaderValue("Basic",
                            Convert.ToBase64String(
                                System.Text.ASCIIEncoding.ASCII.GetBytes(
                                string.Format("{0}:{1}", "", pat))));
                    }
                    catch (JsonReaderException)
                    {
                        m_logger.AuthenticationViaCredentialProviderFailed(m_context.LoggingContext, uri.AbsoluteUri, $"The credential provider '{credentialProviderPath}' response is not a well-formed JSON.");
                        return null;
                    }
                }

                m_logger.AuthenticationViaCredentialProviderFailed(m_context.LoggingContext, uri.AbsoluteUri, $"The credential provider '{credentialProviderPath}' returned a non-succesful exit code: {process.ExitCode}.");
            }

            return null;
        }

        private string DiscoverCredentialProvider(Uri uri)
        {
            var paths = Environment.GetEnvironmentVariable("NUGET_CREDENTIALPROVIDERS_PATH");

            if (paths == null)
            {
                m_logger.AuthenticationViaCredentialProviderFailed(m_context.LoggingContext, uri.AbsoluteUri, $"NUGET_CREDENTIALPROVIDERS_PATH is not set.");
                return null;
            }

            // Here we do something slightly simpler than what NuGet does and just look for the first credential
            // provider we can find
            string credentialProviderPath = null;
            foreach (string path in paths.Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries))
            {
                credentialProviderPath = Directory.EnumerateFiles(path, "credentialprovider*.exe", SearchOption.TopDirectoryOnly).FirstOrDefault();
                if (credentialProviderPath != null)
                {
                    break;
                }
            }

            if (credentialProviderPath == null)
            {
                m_logger.AuthenticationViaCredentialProviderFailed(m_context.LoggingContext, uri.AbsoluteUri, $"Credential provider was not found under '{paths}'.");

                return null;
            }

            return credentialProviderPath;
        }

        private HashType HashTypeParser(string hash)
        {
            if (!string.IsNullOrEmpty(hash))
            {
                var hashTypeString = hash?.Split(':')[0];
                return hashTypeString.FindHashTypeByName();
            }

            return HashType.Unknown;
        }

        /// <nodoc />
        private async Task<EvaluationResult> ValidateAndStoreIncrementalDownloadStateAsync(DownloadData downloadData)
        {
            // If the hash is given in the download setting, use the corresponding hashType(hash algorithm) to get the content hash of the downloaded file.
            // We don't record the file until we know it is the correct one and will be used in this build.
            var recordFileAccess = false;
            var downloadedHash = await GetContentHashAsync(downloadData.DownloadedFilePath, HashTypeParser(downloadData.Settings.Hash), recordFileAccess);
            
            if (downloadData.ContentHash.HasValue)
            {
                // Validate downloaded hash if specified
                if (downloadData.ContentHash != downloadedHash)
                {
                    m_logger.DownloadMismatchedHash(
                        m_context.LoggingContext,
                        downloadData.Settings.ModuleName,
                        downloadData.Settings.Url,
                        downloadData.Settings.Hash,
                        downloadedHash.ToString());
                    return EvaluationResult.Error;
                }
            }
            else
            {
                try
                {
                    var incrementalState = new DownloadIncrementalState(downloadData, downloadedHash);
                    await incrementalState.SaveAsync(m_context);
                }
                catch (BuildXLException e)
                {
                    m_logger.ErrorStoringIncrementality(m_context.LoggingContext, downloadData.Settings.ModuleName, e.Message);
                    return EvaluationResult.Error;
                }
            }

            // Record the file with the build's default hasher.
            m_frontEndHost.Engine.RecordFrontEndFile(downloadData.DownloadedFilePath, Name);
            return new EvaluationResult(FileArtifact.CreateSourceFile(downloadData.DownloadedFilePath));
        }

        #endregion

        #region Extract

        /// <summary>
        /// Extracts a file into a folder with in memory caching for graph construction.
        /// </summary>
        internal Task<EvaluationResult> ExtractFile(DownloadData downloadData)
        {
            var result = m_extractResults.GetOrAdd(
                downloadData.Settings.ModuleName,
                _ => Lazy.Create(() => PerformExtractOrIncrementalCheckAsync(downloadData)));

            return result.Value;
        }

        /// <summary>
        /// Extracts a file into a folder with in manifest based incrementality.
        /// </summary>
        internal async Task<EvaluationResult> PerformExtractOrIncrementalCheckAsync(DownloadData downloadData)
        {
            if (m_context.CancellationToken.IsCancellationRequested)
            {
                return EvaluationResult.Canceled;
            }

            // Ensure file is downloaded
            var extractedFileResult = await DownloadFile(downloadData);
            if (extractedFileResult.IsErrorValue)
            {
                return extractedFileResult;
            }

            var extractedFile = (FileArtifact)extractedFileResult.Value;

            var moduleDescriptor = m_workspaceResolver.GetModuleDescriptor(downloadData);

            var pipConstructionHelper = PipConstructionHelper.Create(
                m_context,
                m_frontEndHost.Engine.Layout.ObjectDirectory,
                m_frontEndHost.Engine.Layout.RedirectedDirectory,
                m_frontEndHost.Engine.Layout.TempDirectory,
                m_frontEndHost.PipGraph,
                moduleDescriptor.Id,
                moduleDescriptor.Name,
                RelativePath.Create(downloadData.ModuleSpecFile.GetName(m_context.PathTable)),
                FullSymbol.Create(m_context.SymbolTable, downloadData.ExtractedValueName),
                new LocationData(downloadData.ModuleSpecFile, 0, 0),
                m_context.QualifierTable.EmptyQualifierId);


            // When we don't have to extract we'll expose the downloaded file in the contents.
            if (downloadData.Settings.ArchiveType == DownloadArchiveType.File)
            {
                return SealDirectory(
                    pipConstructionHelper,
                    downloadData,
                    DirectoryArtifact.CreateWithZeroPartialSealId(downloadData.DownloadedFilePath.GetParent(m_context.PathTable)),
                    SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer>.FromSortedArrayUnsafe(
                        ReadOnlyArray<FileArtifact>.FromWithoutCopy(new[] {extractedFile}),
                        OrdinalFileArtifactComparer.Instance));
            }

            Statistics.Extractions.Total.Increment();

            using (Statistics.Extractions.UpToDateCheckDuration.Start(downloadData.Settings.Url))
            {
                var result = await CheckIfExtractIsNeededAsync(pipConstructionHelper, downloadData);
                if (result.IsErrorValue)
                {
                    Statistics.Extractions.Failures.Increment();
                }
                if (result != EvaluationResult.Continue)
                {
                    Statistics.Extractions.SkippedDueToManifest.Increment();
                    return result;
                }
            }

            using (Statistics.Extractions.Duration.Start(downloadData.Settings.Url))
            {
                try
                {
                    if (!await Task.Run(
                        () => TryExtractToDisk(downloadData),
                        m_context.CancellationToken))
                    {
                        Statistics.Extractions.Failures.Increment();
                        return EvaluationResult.Error;
                    }
                }
                catch (TaskCanceledException)
                {
                    return EvaluationResult.Canceled;
                }
            }

            using (Statistics.Extractions.UpToDateCheckDuration.Start(downloadData.Settings.Url))
            {
                var result = await ValidateAndStoreIncrementalExtractState(pipConstructionHelper, downloadData);
                if (result.IsErrorValue)
                {
                    Statistics.Extractions.Failures.Increment();
                }
                return result;
            }
        }

        /// <nodoc />
        private async Task<EvaluationResult> CheckIfExtractIsNeededAsync(PipConstructionHelper pipConstructionHelper, DownloadData downloadData)
        {
            try
            {
                if (m_context.FileSystem.IsDirectory(downloadData.ContentsFolder))
                {
                    var incrementalState = await ExtractIncrementalState.TryLoadAsync(m_logger, m_context, downloadData);
                    if (incrementalState != null)
                    {
                        // Check all files still have the same hash. This should use the hash cache based on USN so be very fast.
                        foreach (var hashKv in incrementalState.Hashes)
                        {
                            if (!m_context.FileSystem.Exists(hashKv.Key))
                            {
                                // File is not present, extraction is needed.
                                return EvaluationResult.Continue;
                            }

                            var hash = await GetContentHashAsync(hashKv.Key);
                            if (hash != hashKv.Value)
                            {
                                // File has changed, extraction is needed.
                                return EvaluationResult.Continue;
                            }
                        }

                        // All hashes verified, update the manifest
                        await incrementalState.SaveAsync(m_context);
                        return SealDirectory(pipConstructionHelper, downloadData, downloadData.ContentsFolder, incrementalState.Files);
                    }
                }
            }
            catch (IOException e)
            {
                m_logger.ErrorCheckingIncrementality(m_context.LoggingContext, downloadData.Settings.ModuleName, e.Message);
                return EvaluationResult.Error;
            }
            catch (UnauthorizedAccessException e)
            {
                m_logger.ErrorCheckingIncrementality(m_context.LoggingContext, downloadData.Settings.ModuleName, e.Message);
                return EvaluationResult.Error;
            }

            // Extraction is needed
            return EvaluationResult.Continue;
        }

        /// <summary>
        /// Extract files to disk
        /// </summary>
        /// <remarks>
        /// At the point of authoring (Jan 2019) the BCL does not implement tar-files or bz2.
        /// https://github.com/dotnet/corefx/issues/3253 has been discussed since Sept 2015
        /// Therefore we rely here on 3rd party library: https://github.com/icsharpcode/SharpZipLib
        /// </remarks>
        private bool TryExtractToDisk(DownloadData downloadData)
        {
            var archive = downloadData.DownloadedFilePath.ToString(m_context.PathTable);
            var target = downloadData.ContentsFolder.Path.ToString(m_context.PathTable);

            try
            {
                FileUtilities.DeleteDirectoryContents(target, false);
                FileUtilities.CreateDirectory(target);
            }
            catch (BuildXLException e)
            {
                m_logger.ErrorExtractingArchive(m_context.LoggingContext, downloadData.Settings.ModuleName, archive, target, e.Message);
                return false;
            }

            switch (downloadData.Settings.ArchiveType)
            {
                case DownloadArchiveType.Zip:
                    try
                    {
                        new FastZip().ExtractZip(archive, target, null);
                    }
                    catch (ZipException e)
                    {
                        m_logger.ErrorExtractingArchive(m_context.LoggingContext, downloadData.Settings.ModuleName, archive, target, e.Message);
                        return false;
                    }

                    break;
                case DownloadArchiveType.Gzip:
                    try
                    {
                        var targetFile = Path.Combine(
                            target,
                            downloadData.DownloadedFilePath.GetName(m_context.PathTable).RemoveExtension(m_context.StringTable)
                                .ToString(m_context.StringTable));

                        using (var reader = m_context.FileSystem.OpenText(downloadData.DownloadedFilePath))
                        using (var gzipStream = new GZipInputStream(reader.BaseStream))
                        using (var output = FileUtilities.CreateFileStream(targetFile, FileMode.Create, FileAccess.Write, FileShare.Read))
                        {
                            byte[] buffer = new byte[4096];
                            StreamUtils.Copy(gzipStream, output, buffer);
                        }
                    }
                    catch (GZipException e)
                    {
                        m_logger.ErrorExtractingArchive(m_context.LoggingContext, downloadData.Settings.ModuleName, archive, target, e.Message);
                        return false;
                    }

                    break;
                case DownloadArchiveType.Tar:
                    try
                    {
                        using (var reader = m_context.FileSystem.OpenText(downloadData.DownloadedFilePath))
                        using (var tar = TarArchive.CreateInputTarArchive(reader.BaseStream))
                        {
                            tar.ExtractContents(target);
                        }
                    }
                    catch (TarException e)
                    {
                        m_logger.ErrorExtractingArchive(m_context.LoggingContext, downloadData.Settings.ModuleName, archive, target, e.Message);
                        return false;
                    }

                    break;
                case DownloadArchiveType.Tgz:
                    try
                    {
                        using (var reader = m_context.FileSystem.OpenText(downloadData.DownloadedFilePath))
                        using (var gzipStream = new GZipInputStream(reader.BaseStream))
                        using (var tar = TarArchive.CreateInputTarArchive(gzipStream))
                        {
                            tar.ExtractContents(target);
                        }
                    }
                    catch (GZipException e)
                    {
                        m_logger.ErrorExtractingArchive(m_context.LoggingContext, downloadData.Settings.ModuleName, archive, target, e.Message);
                        return false;
                    }
                    catch (TarException e)
                    {
                        m_logger.ErrorExtractingArchive(m_context.LoggingContext, downloadData.Settings.ModuleName, archive, target, e.Message);
                        return false;
                    }

                    break;
                default:
                    throw Contract.AssertFailure("Unexpected archive type");
            }

            try
            {
                if (!FileUtilities.DirectoryExistsNoFollow(target))
                {
                    m_logger.ErrorNothingExtracted(m_context.LoggingContext, downloadData.Settings.ModuleName, archive, target);
                    return false;
                }
            }
            catch (BuildXLException e)
            {
                m_logger.ErrorExtractingArchive(m_context.LoggingContext, downloadData.Settings.ModuleName, archive, target, e.Message);
                return false;
            }

            return true;
        }

        /// <nodoc />
        private async Task<EvaluationResult> ValidateAndStoreIncrementalExtractState(PipConstructionHelper pipConstructionHelper, DownloadData downloadData)
        {
            var archive = downloadData.DownloadedFilePath.ToString(m_context.PathTable);
            var target = downloadData.ContentsFolder.Path.ToString(m_context.PathTable);

            try
            {
                var allFiles = new List<FileArtifact>();

                var enumeratResult = FileUtilities.EnumerateDirectoryEntries(target, true, "*",
                    (dir, fileName, attributes) =>
                    {
                        if ((attributes & FileAttributes.Directory) == 0)
                        {
                            var filePath = Path.Combine(dir, fileName);
                            allFiles.Add(FileArtifact.CreateSourceFile(AbsolutePath.Create(m_context.PathTable, filePath)));
                        }
                    });

                if (!enumeratResult.Succeeded)
                {
                    var error = new Win32Exception(enumeratResult.NativeErrorCode);
                    m_logger.ErrorListingPackageContents(m_context.LoggingContext, downloadData.Settings.ModuleName, archive, target, error.Message);
                    return EvaluationResult.Error;
                }

                if (allFiles.Count == 0)
                {
                    m_logger.ErrorListingPackageContents(m_context.LoggingContext, downloadData.Settings.ModuleName, archive, target, "file list is empty");
                    return EvaluationResult.Error;
                }

                var sortedFiles = SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer>.CloneAndSort(
                    allFiles,
                    OrdinalFileArtifactComparer.Instance);

                var hashes = new Dictionary<AbsolutePath, ContentHash>();
                foreach (var file in allFiles)
                {
                    var hash = await GetContentHashAsync(file);
                    hashes.Add(file.Path, hash);
                }

                var incrementalState = new ExtractIncrementalState(downloadData, sortedFiles, hashes);
                await incrementalState.SaveAsync(m_context);

                return SealDirectory(pipConstructionHelper, downloadData, downloadData.ContentsFolder, sortedFiles);
            }
            catch (Exception e)
                when (e is BuildXLException || e is IOException || e is UnauthorizedAccessException)
            {
                m_logger.ErrorExtractingArchive(m_context.LoggingContext, downloadData.Settings.ModuleName, archive, target, e.Message);
                return EvaluationResult.Error;
            }
        }
        #endregion

        private Package CreatePackage(ModuleDefinition moduleDefinition)
        {
            var moduleDescriptor = moduleDefinition.Descriptor;

            var packageId = PackageId.Create(StringId.Create(m_context.StringTable, moduleDescriptor.Name));
            var packageDescriptor = new PackageDescriptor
            {
                Name = moduleDescriptor.Name,
                Main = moduleDefinition.MainFile,
                NameResolutionSemantics = NameResolutionSemantics.ImplicitProjectReferences,
                Publisher = null,
                Version = moduleDescriptor.Version,
                Projects = new List<AbsolutePath>(moduleDefinition.Specs),
            };

            return Package.Create(packageId, moduleDefinition.ModuleConfigFile, packageDescriptor, moduleId: moduleDescriptor.Id);
        }

        /// <inheritdoc />
        public Task<bool?> TryEvaluateModuleAsync([NotNull] IEvaluationScheduler scheduler, [NotNull] ModuleDefinition module, QualifierId qualifierId)
        {
            // Abstraction between SDK/Workspace/Core/Resolvers is broken here...
            var moduleDefinition = (ModuleDefinition)module;

            if (!string.Equals(moduleDefinition.Descriptor.ResolverName, Name, StringComparison.Ordinal))
            {
                return Task.FromResult<bool?>(null);
            }

            // Downloads are not individually requested to be evaluated, we want them to be on demand.
            return Task.FromResult<bool?>(true);
        }

        private async Task<ContentHash> GetContentHashAsync(AbsolutePath path, HashType hashType = HashType.Unknown, bool recordFile = true)
        {
            if (recordFile)
            {
                m_frontEndHost.Engine.RecordFrontEndFile(path, Name);
            }
            
            // We don't call GetFileContentHashAsync() to get the existing hash, since it register the file. 
            // This has been done in RecordFrontEndFile with the default hasher, re-register it with the specified hasher will cause error.
            using (
            var fs = FileUtilities.CreateFileStream(
                path.ToString(m_context.PathTable),
                FileMode.Open,
                FileAccess.Read,
                FileShare.Delete | FileShare.Read,
                FileOptions.SequentialScan))
            {
                return await ContentHashingUtilities.HashContentStreamAsync(fs, hashType);
            }

        }

        private EvaluationResult SealDirectory(PipConstructionHelper pipConstructionHelper, DownloadData downloadData, DirectoryArtifact directory, SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer> files)
        {
            if (!pipConstructionHelper.TrySealDirectory(
                directory,
                files,
                CollectionUtilities.EmptySortedReadOnlyArray<DirectoryArtifact, OrdinalDirectoryArtifactComparer>(OrdinalDirectoryArtifactComparer.Instance),
                Pips.Operations.SealDirectoryKind.Partial,
                null,
                null,
                null,
                out var directoryArtifact)
            )
            {
                return EvaluationResult.Error;
            }

            var staticDirectory = new StaticDirectory(directoryArtifact, Pips.Operations.SealDirectoryKind.Partial, files.WithCompatibleComparer(OrdinalPathOnlyFileArtifactComparer.Instance));

            return new EvaluationResult(staticDirectory);
        }
    }
}