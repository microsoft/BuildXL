// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net.Http.Headers;
using System.Net.Http;
using System.Threading.Tasks;
using System;
using AdoBuildRunner.Vsts;
using System.Collections.Generic;
using System.Net.Http.Json;
using System.Text.Json;

#nullable enable

namespace BuildXL.AdoBuildRunner.Vsts
{
    /// <summary>
    /// Static helpers to interact with VSTS directly through the REST HTTP endpoints
    /// </summary>
    public class VstsHttpRelay
    {
        private readonly string m_accessToken;
        private readonly ILogger m_logger;
        private HttpClient Client => (m_httpClient ??= GetClient());
        private HttpClient? m_httpClient;
        private readonly static JsonSerializerOptions s_jsonSerializerOptions = new() { PropertyNameCaseInsensitive = true };
        private readonly static string s_endpoint = $"build/builds/{Environment.GetEnvironmentVariable(Constants.BuildIdVarName)}";

        /// <nodoc />
        private static string GetVstsCollectionUri() => Environment.GetEnvironmentVariable("SYSTEM_COLLECTIONURI")!;

        /// <nodoc />
        private static string GetProject() => Environment.GetEnvironmentVariable("SYSTEM_TEAMPROJECT")!;

        /// <nodoc />
        public VstsHttpRelay(string accessToken, ILogger logger)
        {
            m_accessToken = accessToken;
            m_logger = logger;
        }

        /// <summary>
        /// Extracts the trigger info from the build information. We use this to communicate information from
        /// the triggering orchestrator to the worker pipeline.
        /// https://learn.microsoft.com/en-us/rest/api/azure/devops/build/builds/get?view=azure-devops-rest-7.1
        /// </summary>
        public async Task<Dictionary<string, string>> GetBuildTriggerInfoAsync()
        {
            try
            {
                var vstsUri = GetVstsCollectionUri();
                var uri = $"{vstsUri}{GetProject()}/_apis/{s_endpoint}?api-version=7.1-preview.7";
                var res = await Client.GetAsync(uri);

                if (!res.IsSuccessStatusCode)
                {
                    var response = await res.Content.ReadAsStringAsync();
                    throw new Exception($"QueuePipelineAsync failed: {response}");
                }

                var buildParamsData = await res.Content.ReadFromJsonAsync<BuildData>(s_jsonSerializerOptions);
                return buildParamsData!.TriggerInfo ?? new Dictionary<string, string>();
            }
            catch (Exception ex)
            {
                throw new CoordinationException(ex);
            }
        }

        private HttpClient GetClient()
        {
            var client = new HttpClient(new HttpClientHandler() { UseDefaultCredentials = true });
            client.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic",
                    Convert.ToBase64String(System.Text.ASCIIEncoding.ASCII.GetBytes(string.Format("{0}:{1}", "", m_accessToken))));

            return client;
        }
    }
}