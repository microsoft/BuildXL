// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace BuildXL.AdoBuildRunner.Vsts
{
    /// <summary>
    /// https://learn.microsoft.com/en-us/azure/virtual-machines/instance-metadata-service?tabs=windows
    /// </summary>
    internal class AzureInstanceMetadataService
    {
        #region HttpClient
        private HttpClient Client => (m_httpClient ??= GetClient());
        private HttpClient? m_httpClient;
        private HttpClient GetClient()
        {
            var client = new HttpClient(new HttpClientHandler { UseProxy = false })
            {
                Timeout = TimeSpan.FromSeconds(5) // The server is local to the VM, so it should be more than enough
            };
            client.DefaultRequestHeaders.Add("Metadata", "true");
            client.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            return client;
        }
        #endregion

        /// <summary>
        /// A well-known endpoint where the metadata is served from (through HTTP but locally within the VM).
        /// </summary>
        private const string ImdsUri = "http://169.254.169.254/metadata/instance?api-version=2021-02-01";

        private InstanceMetadata? m_cachedMetadata;

        private async Task<InstanceMetadata> GetInstanceMetadata(ILogger logger)
        {
#if DEBUG
            if (Environment.GetEnvironmentVariable("__ADOBR_INTERNAL_MOCK_ADO") == "1")
            {
                return new();
            }
#endif
            if (m_cachedMetadata is null)
            {
                try
                {
                    HttpResponseMessage response = await Client.GetAsync(ImdsUri);

                    if (response.StatusCode == HttpStatusCode.Gone || response.StatusCode == HttpStatusCode.InternalServerError)
                    {
                        // IMDS docs say to 'retry after some time' upon these status codes. Wait for 20 seconds and try again
                        // https://learn.microsoft.com/en-us/azure/virtual-machines/instance-metadata-service?tabs=windows#errors-and-debugging
                        logger.Warning($"IMDS operation failed with status code {response.StatusCode}. Retrying after 20 seconds...");
                        await Task.Delay(TimeSpan.FromSeconds(20));
                        response = await Client.GetAsync(ImdsUri);
                    }

                    response.EnsureSuccessStatusCode();
                    m_cachedMetadata = await response.Content.ReadFromJsonAsync<InstanceMetadata>();
                }
                catch (Exception ex)
                {
                    logger.Error($"Error deserializing IMDS: {ex}");
                    throw new CoordinationException(ex);
                }
            }

            return m_cachedMetadata!;
        }

        public async Task<string?> GetPoolName(ILogger logger)
        {
#if DEBUG
            if (Environment.GetEnvironmentVariable("__ADOBR_INTERNAL_MOCK_ADO") == "1")
            {
                return "Pool";
            }
#endif
            var taskMetadata = await GetInstanceMetadata(logger);
            return taskMetadata.Compute.TagsList.Find(t => t.Name.Equals("PoolId", StringComparison.OrdinalIgnoreCase))?.Value;
        }
    }
}
