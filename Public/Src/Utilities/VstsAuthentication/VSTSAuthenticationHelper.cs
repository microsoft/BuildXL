// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics.ContractsLight;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using System.Net.Http;
using BuildXL.Native.IO;
using System.Text;

namespace BuildXL.Utilities.VstsAuthentication
{
    /// <summary>
    /// Helper to get authentication credentials against VSTS connections
    /// </summary>
    public static class VSTSAuthenticationHelper
    {
        // On non-Windows OS-es the nuget credential provider may not be executed concurrently (otherwise the following exception happens:
        // System.PlatformNotSupportedException: Wait operations on multiple wait handles including a named synchronization primitive are not supported on this platform.")
        // The credential provider is used in both a multi-threaded and multi-process scenario, so use a named muted that covers both cases
        private static readonly AsyncMutex g_authProviderMutex = OperatingSystemHelper.IsWindowsOS
            ? null
            : new AsyncMutex("Global\\BuildXL_CredentialProviderMutex");

        /// <summary>
        /// Creates a collection of <see cref="SourceRepository"/> given a collection of URIs
        /// </summary>
        /// <remarks>
        /// The credential provider is discovered by searching under the paths specified in NUGET_CREDENTIALPROVIDERS_PATH environment variable
        /// </remarks>
        public static Task<Possible<IReadOnlyCollection<SourceRepository>>> TryCreateSourceRepositories(
           IEnumerable<(string repositoryName, Uri repositoryUri)> repositories,
           CancellationToken cancellationToken,
           StringBuilder logger,
           bool isRetry)
        {
            return TryCreateSourceRepositories(repositories, TryDiscoverCredentialProvider, cancellationToken, logger, isRetry);
        }

        /// <summary>
        /// Creates a collection of <see cref="SourceRepository"/> given a collection of URIs
        /// </summary>
        /// <remarks>
        /// For the repositories identified as VSTS ones, credential provider authentication is performed
        /// </remarks>
        public static async Task<Possible<IReadOnlyCollection<SourceRepository>>> TryCreateSourceRepositories(
            IEnumerable<(string repositoryName, Uri repositoryUri)> repositories,
            Func<Possible<string>> discoverCredentialProvider,
            CancellationToken cancellationToken,
            StringBuilder logger,
            bool isRetry)
        {
            Contract.RequiresNotNull(repositories);

            var sourceRepositories = new List<SourceRepository>();

            Lazy<Possible<string>> credentialProviderPath = new Lazy<Possible<string>>(discoverCredentialProvider);

            // For each repository, authenticate if we are dealing with a VSTS feed and retrieve the base addresses
            foreach (var repository in repositories)
            {
                Uri uri = repository.repositoryUri;
                string uriAsString = uri.AbsoluteUri;
                var packageSource = new PackageSource(uriAsString, repository.repositoryName);

                logger.AppendLine($"Analyzing {uri.AbsoluteUri}");
                // For now we only support authentication against VSTS feeds. Feeds outside of that will attempt to connect unauthenticated
                if (IsVSTSPackageSecureURI(uri) && await IsAuthenticationRequiredAsync(uri, cancellationToken, logger))
                {
                    logger.AppendLine($"Authentication required for {uri.AbsoluteUri}. Trying to acquire credentials.");

                    if (!credentialProviderPath.Value.Succeeded)
                    {
                        return credentialProviderPath.Value.Failure;
                    }

                    if (await TryGetAuthenticationCredentialsAsync(uri, credentialProviderPath.Value.Result, cancellationToken, isRetry) is var maybeCredentials)
                    {
                        var maybePackageSourceCredential = maybeCredentials
                            .Then(credentials => new PackageSourceCredential(uriAsString, credentials.username, credentials.pat, true, string.Empty));

                        if (maybePackageSourceCredential.Succeeded)
                        {
                            logger.AppendLine($"Authentication successfully acquired for {uri.AbsoluteUri}");

                            packageSource.Credentials = maybePackageSourceCredential.Result;
                        }
                        else
                        {
                            logger.AppendLine($"Failed to authenticate {uri.AbsoluteUri}: {maybePackageSourceCredential.Failure.DescribeIncludingInnerFailures()}");
                            return maybePackageSourceCredential.Failure;
                        }
                    }
                }
                else
                {
                    logger.AppendLine($"Authentication not required for {uri.AbsoluteUri}");
                }

                var sourceRepository = Repository.Factory.GetCoreV3(packageSource);
                sourceRepositories.Add(sourceRepository);
            }

            return sourceRepositories;
        }

        /// <summary>
        /// Whether the provided Uri requires authentication
        /// </summary>
        public static async Task<bool> IsAuthenticationRequiredAsync(Uri uri, CancellationToken cancellationToken, StringBuilder logger)
        {
            logger.AppendLine($"Checking whether authentication is required for {uri.AbsoluteUri}");
            try
            {
                var handler = new HttpClientHandler()
                {
                    AllowAutoRedirect = false
                };

                using (var httpClient = new HttpClient(handler))
                {
                    httpClient.Timeout = TimeSpan.FromSeconds(5);

                    int retries = 3;

                    while (true)
                    {
                        try
                        {
                            var httpResponse = await httpClient.GetAsync(uri, cancellationToken);


                            logger.AppendLine($"Response for {uri.AbsoluteUri} is: {httpResponse.StatusCode}");

                            return (httpResponse.StatusCode == System.Net.HttpStatusCode.Unauthorized || httpResponse.StatusCode == System.Net.HttpStatusCode.Redirect) && httpResponse.Headers.WwwAuthenticate.Any();
                        }
                        catch (TaskCanceledException)
                        {
                            logger.AppendLine($"Request for {uri.AbsoluteUri} timed out. Retries left: {retries}");

                            retries--;

                            // The service seems to be unavailable. Let the pipeline deal with the unavailability, since it will likely result in a better error message
                            if (retries == 0)
                            {
                                return false;
                            }
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is AggregateException)
            {
                logger.AppendLine($"Exception for {uri.AbsoluteUri}: {ex.Message}");

                // The service seems to be have a problem. Let's pretend it does need auth and let the rest of the pipeline deal with the unavailability, since it will likely result in a better error message
                return true;
            }
        }

        /// <summary>
        /// Whether the URI is pointing to a VSTS URI
        /// </summary>
        public static bool IsVSTSPackageSecureURI(Uri downloadURI)
        {
            return downloadURI.Scheme == "https" &&
                (downloadURI.Host.EndsWith("pkgs.visualstudio.com", StringComparison.OrdinalIgnoreCase) || downloadURI.Host.EndsWith("pkgs.dev.azure.com", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Returns an HTTP-based header from a PAT, which can be injected in an HTTP header request message
        /// </summary>
        public static AuthenticationHeaderValue GetAuthenticationHeaderFromPAT(string pat)
        {
            return new AuthenticationHeaderValue("Basic",
                            Convert.ToBase64String(
                                System.Text.ASCIIEncoding.ASCII.GetBytes(
                                string.Format("{0}:{1}", "", pat))));
        }

        /// <summary>
        /// <see cref="TryGetAuthenticationCredentialsAsync(Uri, string, CancellationToken, bool)"/>
        /// </summary>
        /// <remarks>
        /// The credential provider is discovered by searching under the paths specified in NUGET_CREDENTIALPROVIDERS_PATH environment variable
        /// </remarks>
        public static Task<Possible<(string username, string pat)>> TryGetAuthenticationCredentialsAsync(
            Uri uri,
            bool isRetry,
            CancellationToken cancellationToken)
        {
            return TryDiscoverCredentialProvider()
                .ThenAsync(async credentialProviderPath => await TryGetAuthenticationCredentialsAsync(uri, credentialProviderPath, cancellationToken, isRetry));
        }

        /// <summary>
        /// Tries to get an authentication token using a credential provider
        /// </summary>
        /// <remarks>
        /// The credential provider is discovered following the definition here: https://docs.microsoft.com/en-us/nuget/reference/extensibility/nuget-exe-credential-providers
        /// using an environment variable.
        /// Observe the link above is for nuget specifically, but the authentication here is used for VSTS feeds in general
        /// </remarks>
        public static Task<Possible<(string username, string pat)>> TryGetAuthenticationCredentialsAsync(
            Uri uri,
            string credentialProviderPath,
            CancellationToken cancellationToken,
            bool isRetry)
        {
            var isWindowsCredentialProvider = Path.GetFileNameWithoutExtension(credentialProviderPath) == "CredentialProvider.Microsoft";

            // Call the provider with the requested URI in non-interactive mode.
            // The Windows Credential Provider needs a -F JSON option as the output format in order to get a JSON object as the output (other providers don't support it, but produce JSON as the default)
            var processInfo = new ProcessStartInfo
            {
                FileName = credentialProviderPath,
                Arguments = $"-Uri {uri.AbsoluteUri} -NonInteractive {(isRetry ? "-IsRetry" : string.Empty)} {(isWindowsCredentialProvider ? "-F JSON" : string.Empty)}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                UseShellExecute = false,
                ErrorDialog = false,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            return RunCredentialProvider(uri, credentialProviderPath, processInfo, isRetry, cancellationToken);
        }

        private static async Task<Possible<(string username, string pat)>> RunCredentialProvider(
            Uri uri,
            string credentialProviderPath,
            ProcessStartInfo processInfo,
            bool isRetry,
            CancellationToken cancellationToken)
        {
            var failureCommon = $"Unable to authenticate using a credential provider for '{uri}':";

            // on Linux/Mac only one call to the credential provider may happen at a time
            if (g_authProviderMutex != null)
            {
                try
                {
                    await g_authProviderMutex.WaitOneAsync(cancellationToken);
                }
                catch (AbandonedMutexException)
                {
                    // An abandoned mutex means there was a previous crash that prevented the mutex release.
                    // The mutex is now acquired and there shouldn't be any further consequences since this mutex is
                    // only used to prevent concurrent executions of the credential provider. Therefore, we just
                    // ignore the exception
                }
            }

            try
            {
                using (var process = Process.Start(processInfo))
                {
                    if (process == null)
                    {
                        // The process was not started
                        return new AuthenticationFailure($"{failureCommon} Could not start credential provider process '{credentialProviderPath}'.");
                    }

                    try
                    {
#pragma warning disable AsyncFixer02 // WaitForExitAsync should be used instead
                        if (!process.WaitForExit(60 * 1000))
                        {
                            Kill(process);
                            return new AuthenticationFailure($"{failureCommon} Credential provider execution '{credentialProviderPath}' failed due to timeout. The credential provider call didn't finish within 60 seconds.");
                        }

                        // Give time for the Async event handlers to finish by calling WaitForExit again.
                        // if the first one succeeded
                        // Note: Read remarks from https://msdn.microsoft.com/en-us/library/ty0d8k56(v=vs.110).aspx
                        // for reason.
                        process.WaitForExit();
#pragma warning restore AsyncFixer02
                    }
                    catch (Exception e) when (e is SystemException || e is Win32Exception)
                    {
                        return new AuthenticationFailure($"{failureCommon} Credential provider execution '{credentialProviderPath}' failed. Error: {e.Message}");
                    }

                    // Check whether the authorization succeeded
                    if (process.ExitCode == 0)
                    {
                        try
                        {
                            // The response should be a well-formed JSON with a 'password' entry representing the PAT
                            var response = (JObject)await JToken.ReadFromAsync(new JsonTextReader(process.StandardOutput), cancellationToken);
                            var pat = response.GetValue("Password");

                            if (pat == null)
                            {
                                return new AuthenticationFailure($"{failureCommon} Could not find a 'password' entry in the JSON response of '{credentialProviderPath}'.");
                            }

                            // We got a PAT back. Create an authentication header with a base64 encoding of the retrieved PAT
                            return (response.GetValue("Username")?.ToString(), pat.ToString());

                        }
                        catch (JsonReaderException)
                        {
                            // The JSON is malformed
                            return new AuthenticationFailure($"{failureCommon} The credential provider '{credentialProviderPath}' response is not a well-formed JSON.");
                        }
                    }

                    return new AuthenticationFailure($"{failureCommon} The credential provider '{credentialProviderPath}' returned a non-succesful exit code: {process.ExitCode}. Details: {await process.StandardError.ReadToEndAsync()}");
                }
            }
            finally
            {
                g_authProviderMutex?.ReleaseMutex();
            }
        }

        private static void Kill(Process p)
        {
            if (p == null || p.HasExited)
            {
                return;
            }

            try
            {
                p.Kill();
            }
            catch (InvalidOperationException)
            {
                // the process may have exited,
                // in this case ignore the exception
            }
        }

        private static Possible<string> TryDiscoverCredentialProvider()
        {
            var credentialProviderPaths = Environment.GetEnvironmentVariable("NUGET_CREDENTIALPROVIDERS_PATH");

            if (credentialProviderPaths == null)
            {
                return new AuthenticationFailure("Unable to authenticate using a credential provider: NUGET_CREDENTIALPROVIDERS_PATH is not set.");
            }

            // Here we do something slightly simpler than what NuGet does and just look for the first credential
            // provider we can find
            string credentialProviderPath = null;
            foreach (string path in credentialProviderPaths.Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries))
            {
                string directory = path;

                // In some cases the entry can point directly to the credential .exe. Let's treat that uniformly and enumerate its parent directory
                if (FileUtilities.TryProbePathExistence(path, followSymlink: false) is var maybeExistence &&
                    maybeExistence.Succeeded &&
                    maybeExistence.Result == PathExistence.ExistsAsFile)
                {
                    directory = Directory.GetParent(path).FullName;
                }

                credentialProviderPath = Directory.EnumerateFiles(directory, "CredentialProvider*.exe", SearchOption.TopDirectoryOnly).FirstOrDefault();
                if (credentialProviderPath != null)
                {
                    break;
                }
            }

            if (credentialProviderPath == null)
            {
                return new AuthenticationFailure($"Unable to authenticate using a credential provider: Credential provider was not found under '{credentialProviderPaths}'.");
            }

            return credentialProviderPath;
        }
    }
}