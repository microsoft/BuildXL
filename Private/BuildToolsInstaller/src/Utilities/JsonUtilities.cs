// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;

namespace BuildToolsInstaller.Utilities
{
    /// <summary>
    /// Deserializing utilities
    /// </summary>
    public static class JsonUtilities
    {
        private static readonly HttpClient s_httpClient = HttpClientFactory.Create();
        private const int MaxRetries = 3;
        private static readonly TimeSpan s_delayBetweenRetries = TimeSpan.FromSeconds(2);
        internal static readonly JsonSerializerOptions DefaultSerializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        /// <summary>
        /// Deserialize from a file stored in an Azure Storage blob, logging an error and returning null if the operation fails
        /// </summary>
        public static async Task<T?> DeserializeFromBlobAsync<T>(Uri blobUri, ILogger logger, JsonSerializerOptions? serializerOptions = null, CancellationToken token = default)
        {
            // The storage account should have been configured with anonymous read access to the config blob,
            // so no need to provide any credentials.
            var client = new BlobClient(blobUri);

            logger.Debug($"Downloading JSON configs for {typeof(T).Name} from URI=[{blobUri}]");
            var downloadLocation = Directory.CreateTempSubdirectory();

            try
            {
                string downloadPath = Path.Combine(downloadLocation.FullName, "config.json");
                Response downloadResult = await client.DownloadToAsync(downloadPath, token);

                if (downloadResult.IsError)
                {
                    logger.Error($"Downloading blob failed: {downloadResult.Status} - {downloadResult.ReasonPhrase}");
                    return default;
                }

                return await DeserializeAsync<T>(downloadPath, logger, serializerOptions, token);
            }
            finally
            {
                try
                {
                    Directory.Delete(downloadLocation.FullName, true);
                }
                catch
                {
                    // Swallow - best effort deletion
                }
            }
        }

        /// <summary>
        /// Deserializes a JSON file pointed by <paramref name="uri"/>. The request is retried upon HTTP failures.
        /// </summary>
        public static async Task<T?> DeserializeFromHttpAsync<T>(Uri uri, ILogger logger, JsonSerializerOptions? serializerOptions = null, CancellationToken token = default)
        {
            int retryCount = 0;

            while (retryCount < MaxRetries)
            {
                try
                {
                    using (HttpResponseMessage response = await s_httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token))
                    {
                        response.EnsureSuccessStatusCode();
                        await using (Stream responseStream = await response.Content.ReadAsStreamAsync(token))
                        {
                            T? result = await JsonSerializer.DeserializeAsync<T>(responseStream, serializerOptions ?? DefaultSerializerOptions, token);
                            logger.Info($"Successfully deserialized JSON from {uri}.");
                            return result;
                        }
                    }
                }
                catch (HttpRequestException ex) when (retryCount < MaxRetries - 1)
                {
                    retryCount++;
                    logger.Warning(ex, $"Error occurred while trying to download and deserialize JSON from {uri}. Retrying...");
                    logger.Debug($"Attempt {retryCount} failed. Retrying in {s_delayBetweenRetries.TotalSeconds} seconds...");
                    await Task.Delay(s_delayBetweenRetries, token);
                }
                catch (HttpRequestException ex)
                {
                    logger.Error(ex, $"All attempts at deserializing {uri} failed.");
                    return default;
                }
                catch (Exception ex)
                {
                    logger.Error(ex, $"An exception occurred while deserializing {uri}: {ex}");
                    return default;
                }
            }

            logger.Error("Unable to download and deserialize JSON after multiple attempts.");
            return default;
        }

        /// <summary>
        /// Deserialize JSON from a file, logging an error and returning null if the operation fails
        /// </summary>
        public static async Task<T?> DeserializeAsync<T>(string filePath, ILogger logger, JsonSerializerOptions? serializerOptions = null, CancellationToken token = default)
        {
            try
            {
                using (FileStream openStream = File.OpenRead(filePath))
                {
                    return await JsonSerializer.DeserializeAsync<T>(openStream, serializerOptions ?? DefaultSerializerOptions, cancellationToken: token);
                }
            }
            catch (Exception e)
            {
                logger.Error($"Exception thrown while deserializing {typeof(T).Name}: {e}");
                return default;
            }
        }
    }
}
