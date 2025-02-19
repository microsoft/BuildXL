// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildToolsInstaller.Config
{
    // TODO [maly]: The configuration will be downloaded from the meta-package, and every installer will know what path to read / deserialize
    // for now we download the files one-by-one, respecting the directory structure that will be present in the configuration package.
    public class ConfigurationDownloader
    {
        private static readonly HttpClient s_httpClient = Utilities.HttpClientFactory.Create();
        private const string ConfigurationBaseUri = "https://bxlscripts.z20.web.core.windows.net/config/v1/";

        public static Task<string> DownloadConfigurationAsync(ILogger logger)
        {
            return DownloadConfigurationAsync(Path.Combine(Path.GetTempPath(), "config_downloads"), logger);
        }

        public static async Task<string> DownloadConfigurationAsync(string downloadPath, ILogger logger)
        {
            string[] filesToDownload = [
                "buildxl/rings.json",
                "buildxl/overrides.json"
            ];

            // Create a temporary directory
            string tempDir = downloadPath ?? Path.Combine(Path.GetTempPath(), "config_downloads");
            Directory.CreateDirectory(tempDir);

            // Create a list of tasks for downloading files in parallel
            var downloadTasks = filesToDownload.Select(r => DownloadConfigFileAsync(r, tempDir, logger));
            await Task.WhenAll(downloadTasks);
            logger.Info($"Configuration downloaded to {tempDir}");
            return tempDir; // Return the path to the directory with the downloaded files
        }

        private static async Task DownloadConfigFileAsync(string relativePath, string tempDir, ILogger logger)
        {
            await Task.Yield();
            string fileUrl = ConfigurationBaseUri + relativePath;
            string destinationPath = Path.Combine(tempDir, relativePath);

            // Download the file content
            var response = await s_httpClient.GetAsync(fileUrl);
            response.EnsureSuccessStatusCode(); // Throw an exception if the HTTP request was not successful

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            using (var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write))
            {
                await response.Content.CopyToAsync(fileStream);
            }

            logger.Info($"Downloaded: {fileUrl} => {destinationPath}");
        }
    }

}