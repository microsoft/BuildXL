// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net.Http.Headers;
using System.Net.Http;
using System.Threading.Tasks;
using System;
using AdoBuildRunner.Vsts;
using System.Collections.Generic;
using System.Net.Http.Json;

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

        /// <nodoc />
        public static string GetVstsCollectionUri()
        {
            return Environment.GetEnvironmentVariable("SYSTEM_COLLECTIONURI")!;
        }

        /// <nodoc />
        public static string GetProject()
        {
            return Environment.GetEnvironmentVariable("SYSTEM_TEAMPROJECT")!;
        }

        /// <nodoc />
        public VstsHttpRelay(string accessToken, ILogger logger)
        {
            m_accessToken = accessToken;
            m_logger = logger;
        }

        /// <summary>
        /// Queue a build - see https://learn.microsoft.com/en-us/rest/api/azure/devops/build/builds/queue?view=azure-devops-rest-7.1
        /// The pipeline is assumed to be in the same project and organization that the one running this build
        /// </summary>
        /// <param name="pipelineId">The pipeline id to queue</param>
        /// <param name="parameters">These correspond to variables that can be set at queue time</param>
        /// <param name="templateParameters">These correspond to template parameters that can be set at queue time</param>
        /// <param name="sourceBranch">Source branch for the triggered build</param>
        /// <param name="sourceVersion">Source verson for the triggered build</param>
        /// <param name="triggerInfo">Arbitrary key-value pairs that will be available in the triggered build data. Use this for payloads</param>
        public async Task QueuePipelineAsync(int pipelineId, 
            string sourceBranch, 
            string sourceVersion, Dictionary<string, string>? parameters,
            Dictionary<string, string>? templateParameters,
            Dictionary<string, string>? triggerInfo)
        {
            const string Endpoint = $"build/builds";

            var payload = new QueueBuildRequest
            {
                Definition = new QueueBuildDefinition() { Id = pipelineId },
                Parameters = parameters == null ? null : System.Text.Json.JsonSerializer.Serialize(parameters),
                TemplateParameters = templateParameters,
                TriggerInfo = triggerInfo,
                SourceBranch = sourceBranch,
                SourceVersion = sourceVersion
            };

            try
            {
                var vstsUri = GetVstsCollectionUri();

                var uri = $"{vstsUri}{GetProject()}/_apis/{Endpoint}?api-version=7.1-preview.7";
                m_logger?.Info($"[QueuePipelineAsync] POST {uri}: {Environment.NewLine}{Newtonsoft.Json.JsonConvert.SerializeObject(payload)}");

                var respo = await Client.PostAsJsonAsync(uri, payload);
                var responseStr = await respo.Content.ReadAsStringAsync();

                if (!respo.IsSuccessStatusCode)
                {
                    throw new Exception($"QueuePipelineAsync failed: {responseStr}");
                }
                else
                {
                    m_logger?.Info("[QueuePipelineAsync] Response:");
                    m_logger?.Info(responseStr);
                }
            }
            catch (Exception ex)
            {
                throw new CoordinationException(ex);
            }
        }

        /// <summary>
        /// Extracts the trigger info from the build information. We use this to communicate information from
        /// the triggering orchestrator to the worker pipeline.
        /// https://learn.microsoft.com/en-us/rest/api/azure/devops/build/builds/get?view=azure-devops-rest-7.1
        /// </summary>
        public async Task<Dictionary<string,string>> GetBuildTriggerInfoAsync()
        {
            var endpoint = $"build/builds/{Environment.GetEnvironmentVariable(Constants.BuildIdVarName)}";

            try
            {
                var vstsUri = GetVstsCollectionUri();

                var uri = $"{vstsUri}{GetProject()}/_apis/{endpoint}?api-version=7.1-preview.7";
                var res = await Client.GetAsync(uri);

                var response = await res.Content.ReadAsStringAsync();

                if (!res.IsSuccessStatusCode)
                {
                    throw new Exception($"QueuePipelineAsync failed: {response}");
                }


                var buildParamsData = await res.Content.ReadFromJsonAsync<BuildData>();
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