// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Utilities.Collections;
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

        private class LauncherProcess : ILauncherProcess
        {
            private static readonly Tracer _tracer = new Tracer(nameof(LauncherProcess));

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
                // Using nagle queues to "batch" messages together and to avoid writing them to the logs one by one.
                var outputMessagesNagleQueue = NagleQueue<string>.Create(
                    messages =>
                        {
                            _tracer.Debug(context, $"Service Output: {string.Join(Environment.NewLine, messages)}");
                            return Task.CompletedTask;
                        },
                    maxDegreeOfParallelism: 1, interval: TimeSpan.FromSeconds(10), batchSize: 1024);

                var errorMessagesNagleQueue = NagleQueue<string>.Create(
                    messages =>
                    {
                        _tracer.Error(context, $"Service Error: {string.Join(Environment.NewLine, messages)}");
                        return Task.CompletedTask;
                    },
                    maxDegreeOfParallelism: 1, interval: TimeSpan.FromSeconds(10), batchSize: 1024);

                _process.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        outputMessagesNagleQueue.Enqueue(e.Data);
                    }
                };

                _process.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        errorMessagesNagleQueue.Enqueue(e.Data);
                    }
                };

                _process.Exited += (sender, args) =>
                                   {
                                       // Dispose will drain all the existing items from the message queues.
                                       outputMessagesNagleQueue.Dispose();
                                       errorMessagesNagleQueue.Dispose();
                                   };

                _process.Start();

                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();
            }
        }
    }
}
