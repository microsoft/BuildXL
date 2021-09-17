// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Native.IO;
using BuildXL.Storage;
using BuildXL.ToolSupport;
using BuildXL.Utilities;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Tool.Download
{
    /// <summary>
    /// Downloads a file into a given directory from a specified URL via HTTP
    /// </summary>
    internal sealed class Downloader : ToolProgram<DownloaderArgs>
    {
        // Constant value to target Azure DevOps.
        private const string Resource = "499b84ac-1321-427f-aa17-267ca6975798";
        // Visual Studio IDE client ID originally provisioned by Azure Tools.
        private const string Client = "872cd9fa-d31f-45e0-9eab-6e460a02d1f1";
        // Microsoft tenant
        private const string Tenant = "microsoft.com";
        // Microsoft authority
        private readonly string m_authority = $"https://login.windows.net/{Tenant}";

        private Downloader() : base("Downloader")
        {
        }

        /// <nodoc />
        public static int Main(string[] arguments)
        {
            return new Downloader().MainHandler(arguments);
        }

        /// <inheritdoc />
        public override bool TryParse(string[] rawArgs, out DownloaderArgs arguments)
        {
            try
            {
                arguments = new DownloaderArgs(rawArgs);
                return true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.GetLogEventMessage());
                arguments = null;
                return false;
            }
        }

        /// <inheritdoc />
        public override int Run(DownloaderArgs arguments)
        {
            return TryDownloadFileToDiskAsync(arguments).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Attempts to downoad the file to disk.
        /// </summary>
        private async Task<int> TryDownloadFileToDiskAsync(DownloaderArgs arguments)
        {
            try
            {
                FileUtilities.CreateDirectory(arguments.DownloadDirectory);
                FileUtilities.DeleteFile(arguments.DownloadPath, retryOnFailure: true);
            }
            catch (BuildXLException e)
            {
                Console.Error.WriteLine(e.GetLogEventMessage());
                return 1;
            }

            // We have to download the file.
            Console.WriteLine($"Starting download from url '{arguments.Url}' to '{arguments.DownloadPath}'.");

            var stopwatch = Stopwatch.StartNew();
            try
            {
                using (var httpClient = new HttpClient())
                {
                    httpClient.Timeout = TimeSpan.FromMinutes(10);

                    var httpRequest = new HttpRequestMessage(HttpMethod.Get, arguments.Url);

                    // If the download URI is pointing to a VSTS feed and we get a valid auth token, make it part of the request
                    // We only want to send the token over HTTPS and to a VSTS domain to avoid security issues
                    if (IsVSTSPackageSecureURI(arguments.Url) &&
                        await TryGetAuthenticationHeaderAsync(arguments.Url) is var authHeader &&
                        authHeader != null)
                    {
                        httpRequest.Headers.Accept.Clear();
                        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                        httpRequest.Headers.Authorization = authHeader;
                    }

                    var response = await httpClient.SendAsync(httpRequest);
                    response.EnsureSuccessStatusCode();
                    var stream = await response.Content.ReadAsStreamAsync();

                    using (var targetStream = new FileStream(arguments.DownloadPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await stream.CopyToAsync(targetStream);
                    }

                    Console.WriteLine($"Finished download from url '{arguments.Url}' in {stopwatch.ElapsedMilliseconds}ms with {new FileInfo(arguments.DownloadPath).Length} bytes.");
                }
            }
            catch (HttpRequestException e)
            {
                var message = e.InnerException == null
                    ? e.Message
                    : e.Message + " " + e.InnerException?.Message;
                Console.Error.WriteLine($"Failed to download from url '{arguments.Url}': {message}.");
                return 1;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"Failed to download from url '{arguments.Url}': {e.Message}.");
                return 1;
            }

            if (arguments.Hash.HasValue)
            {
                // If the hash is given in the download setting, use the corresponding hashType (hash algorithm) to get the content hash of the downloaded file.
                // We don't record the file until we know it is the correct one and will be used in this build.
                var downloadedHash = await GetContentHashAsync(arguments.DownloadPath, arguments.Hash.Value.HashType);

                // Validate downloaded hash if specified
                if (arguments.Hash != downloadedHash)
                {
                    Console.Error.WriteLine($"Invalid content for url '{arguments.Url}'. The content hash was expected to be: '{arguments.Hash}' but the downloaded files hash was '{downloadedHash}'. " +
                        "This means that the data on the server has been altered and is not trusted.");
                    return 1;
                }
            }

            return 0;
        }

        private async Task<ContentHash> GetContentHashAsync(string path, HashType hashType)
        {
            using var fs = FileUtilities.CreateFileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Delete | FileShare.Read,
                FileOptions.SequentialScan);
            
            ContentHashingUtilities.SetDefaultHashType(hashType);
            return await ContentHashingUtilities.HashContentStreamAsync(fs, hashType);

        }

        private bool IsVSTSPackageSecureURI(Uri downloadURI)
        {
            return downloadURI.Scheme == "https" && 
                (downloadURI.Host.EndsWith(".pkgs.visualstudio.com", StringComparison.OrdinalIgnoreCase) || downloadURI.Host.EndsWith(".pkgs.dev.azure.com", StringComparison.OrdinalIgnoreCase)) ;
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
            var authenticationContext = new AuthenticationContext(m_authority);

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
                Console.WriteLine($"Download resolver was not able to authenticate using Integrated Windows Authentication for '{uri}': {ex.Message}");
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
                    process?.WaitForExit();
                }
                catch (Exception e) when (e is SystemException || e is Win32Exception)
                {
                    ReportAuthenticationViaCredentialProviderFailed(uri, $"Credential provider execution '{credentialProviderPath}' failed. Error: {e.Message}");
                    return null;
                }

                if (process == null)
                {
                    // The process was not started
                    ReportAuthenticationViaCredentialProviderFailed(uri, $"Could not start credential provider process '{credentialProviderPath}'.");
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
                            ReportAuthenticationViaCredentialProviderFailed(uri, $"Could not find a 'password' entry in the JSON response of '{credentialProviderPath}'.");
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
                        ReportAuthenticationViaCredentialProviderFailed(uri, $"The credential provider '{credentialProviderPath}' response is not a well-formed JSON.");
                        return null;
                    }
                }

                ReportAuthenticationViaCredentialProviderFailed(uri, $"The credential provider '{credentialProviderPath}' returned a non-succesful exit code: {process.ExitCode}.");
            }

            return null;
        }

        private string DiscoverCredentialProvider(Uri uri)
        {
            var paths = Environment.GetEnvironmentVariable("NUGET_CREDENTIALPROVIDERS_PATH");

            if (paths == null)
            {
                ReportAuthenticationViaCredentialProviderFailed(uri, $"NUGET_CREDENTIALPROVIDERS_PATH is not set.");
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
                ReportAuthenticationViaCredentialProviderFailed(uri, $"Credential provider was not found under '{paths}'.");

                return null;
            }

            return credentialProviderPath;
        }

        private void ReportAuthenticationViaCredentialProviderFailed(Uri url, string details)
        {
            Console.WriteLine($"Download resolver was not able to authenticate using a credential provider for '{url}': {details}") ;
        }
    }
}
