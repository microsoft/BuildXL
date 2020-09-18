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
            private HttpClient _client = new HttpClient();

            public async Task<LauncherManifest> GetLaunchManifestAsync(OperationContext context, LauncherSettings settings)
            {
                var requestContent = JsonSerializer.Serialize(settings.DeploymentParameters);

                // Query for launcher manifest from remote service
                var response = await _client.PostAsync(
                    settings.ServiceUrl,
                    new StringContent(requestContent, System.Text.Encoding.UTF8, "application/json"),
                    context.Token);

                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<LauncherManifest>(content, DeploymentUtilities.ConfigurationSerializationOptions);
            }

            public Task<Stream> GetStreamAsync(OperationContext context, FileSpec fileInfo)
            {
                // TODO: retry?
                return _client.GetStreamAsync(fileInfo.DownloadUrl);
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