// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Host.Configuration;

namespace BuildXL.Cache.Host.Service
{
    /// <summary>
    /// Host class for providing ability to launch processes and contact deployment service
    /// </summary>
    public class DeploymentLauncherHost : IDeploymentLauncherHost
    {
        /// <nodoc />
        public static DeploymentLauncherHost Instance { get; } = new DeploymentLauncherHost();

        private readonly IDeploymentServiceClient _client = new DeploymentServiceClient();

        /// <inheritdoc />
        public ILauncherProcess CreateProcess(ProcessStartInfo info)
        {
            return new LauncherProcess(info);
        }

        /// <inheritdoc />
        public IDeploymentServiceClient CreateServiceClient()
        {
            return _client;
        }

        private class DeploymentServiceClient : IDeploymentServiceClient
        {
            private readonly HttpClient _client = new HttpClient();

            private LauncherManifest _lastManifest;

            public async Task<LauncherManifest> GetLaunchManifestAsync(OperationContext context, LauncherSettings settings)
            {
                if (settings.OverrideTool != null && settings.ServiceUrl == null)
                {
                    return new LauncherManifest()
                    {
                        ContentId = "OverrideTool",
                        DeploymentManifestChangeId = "OverrideTool",
                        IsComplete = true,
                        Tool = settings.OverrideTool
                    };
                }

                if (!settings.DeploymentParameters.ForceUpdate)
                {
                    // First query for change id to detect if deployment manifest has changed. If
                    // deployment manifest has not changed then, launcher manifest which is derived from it
                    // also has not changed, so just return prior launcher manifest in that case. This avoids
                    // a lot of unnecessary computation on the server.
                    string newChangeId = await _client.GetStringAsync($"{settings.ServiceUrl}/deploymentChangeId");
                    var lastManifest = _lastManifest;
                    if (lastManifest?.DeploymentManifestChangeId == newChangeId)
                    {
                        return lastManifest;
                    }
                }

                // Query for launcher manifest from remote service
                var content = await PostJsonAsync(context, $"{settings.ServiceUrl}/deployment", settings.DeploymentParameters);
                var manifest = JsonSerializer.Deserialize<LauncherManifest>(content, DeploymentUtilities.ConfigurationSerializationOptions);

                if (manifest.IsComplete)
                {
                    _lastManifest = manifest;
                }

                if (settings.OverrideTool != null)
                {
                    manifest.Tool = settings.OverrideTool;
                }

                return manifest;
            }

            public Task<Stream> GetStreamAsync(OperationContext context, string downloadUrl)
            {
                // TODO: retry?
                return _client.GetStreamAsync(downloadUrl);
            }

            public Task<string> GetProxyBaseAddress(OperationContext context, string serviceUrl, HostParameters parameters, string token)
            {
                return PostJsonAsync(context, GetProxyBaseAddressQueryUrl(context, serviceUrl, token), parameters);
            }

            internal static string GetProxyBaseAddressQueryUrl(OperationContext context, string baseAddress, string accessToken)
            {
                static string escape(string value) => Uri.EscapeDataString(value);

                return $"{baseAddress}/getproxyaddress?contextId={escape(context.TracingContext.TraceId)}&accessToken={escape(accessToken)}";
            }

            private async Task<string> PostJsonAsync<TBody>(OperationContext context, string url, TBody body)
            {
                var requestContent = JsonSerializer.Serialize(body);

                var response = await _client.PostAsync(
                    url,
                    new StringContent(requestContent, System.Text.Encoding.UTF8, "application/json"),
                    context.Token);

                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                return content;
            }

            public void Dispose()
            {
                // Do nothing. This instance is reused
            }
        }
    }
}
