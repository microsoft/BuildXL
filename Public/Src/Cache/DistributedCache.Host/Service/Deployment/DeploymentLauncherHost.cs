// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.Host.Configuration;
using static BuildXL.Cache.Host.Configuration.DeploymentManifest;

namespace BuildXL.Cache.Host.Service
{
    /// <summary>
    /// Host class for providing ability to launch processes and contact deployment service
    /// </summary>
    public class DeploymentLauncherHost : IDeploymentLauncherHost
    {
        /// <nodoc />
        public static DeploymentLauncherHost Instance { get; } = new DeploymentLauncherHost();

        /// <inheritdoc />
        public ILauncherProcess CreateProcess(ProcessStartInfo info)
        {
            return new LauncherProcess(info);
        }

        /// <inheritdoc />
        public IDeploymentServiceClient CreateServiceClient()
        {
            return new DeploymentServiceClient();
        }

        private class DeploymentServiceClient : IDeploymentServiceClient
        {
            private readonly HttpClient _client = new HttpClient();

            public async Task<LauncherManifest> GetLaunchManifestAsync(OperationContext context, LauncherSettings settings)
            {
                // Query for launcher manifest from remote service
                var content = await PostJsonAsync(context, settings.ServiceUrl, settings.DeploymentParameters);
                return JsonSerializer.Deserialize<LauncherManifest>(content, DeploymentUtilities.ConfigurationSerializationOptions);
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

            internal static string GetProxyBaseAddressQueryUrl(OperationContext context, string baseAddress, string token)
            {
                static string escape(string value) => Uri.EscapeDataString(value);

                return $"{baseAddress}/getproxyaddress?contextId={escape(context.TracingContext.Id.ToString())}&token={escape(token)}";
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
                _client.Dispose();
            }
        }

        private class LauncherProcess : ILauncherProcess
        {
            private readonly Process _process;

            public LauncherProcess(ProcessStartInfo info)
            {
                info.RedirectStandardOutput = true;
                info.RedirectStandardError = true;

                _process = new Process()
                {
                    StartInfo = info,
                    EnableRaisingEvents = true
                };

                _process.Exited += (sender, e) => Exited?.Invoke();
            }

            public int ExitCode => _process.ExitCode;

            public int Id => _process.Id;

            public bool HasExited => _process.HasExited;

            public event Action Exited;

            public void Kill(OperationContext context)
            {
                _process.Kill();
            }

            public void Start(OperationContext context)
            {
                _process.OutputDataReceived += (s, e) =>
                {
                    context.TracingContext.Debug("Service Output: " + e.Data);
                };

                _process.ErrorDataReceived += (s, e) =>
                {
                    context.TracingContext.Error("Service Error: " + e.Data);
                };

                _process.Start();

                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();
            }
        }
    }
}