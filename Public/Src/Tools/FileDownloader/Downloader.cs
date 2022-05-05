// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Native.IO;
using BuildXL.Storage;
using BuildXL.ToolSupport;
using BuildXL.Utilities;
using BuildXL.Utilities.VstsAuthentication;

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

                    var logger = new StringBuilder();

                    // If the download URI is pointing to a VSTS feed and we get a valid auth token, make it part of the request
                    // We only want to send the token over HTTPS and to a VSTS domain to avoid security issues
                    if (VSTSAuthenticationHelper.IsVSTSPackageSecureURI(arguments.Url) &&
                        await VSTSAuthenticationHelper.IsAuthenticationRequiredAsync(arguments.Url, CancellationToken.None, logger) &&
                        await VSTSAuthenticationHelper.TryGetAuthenticationCredentialsAsync(arguments.Url, CancellationToken.None) is var maybeAuthCredentials &&
                        maybeAuthCredentials.Succeeded &&
                        VSTSAuthenticationHelper.GetAuthenticationHeaderFromPAT(maybeAuthCredentials.Result.pat) is var authHeader)
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
    }
}
