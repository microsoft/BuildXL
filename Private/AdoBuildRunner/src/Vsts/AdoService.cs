// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.AdoBuildRunner.Vsts;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;

namespace BuildXL.AdoBuildRunner
{
    /// <summary>
    /// Concrete implementation of <see cref="IAdoService"/>
    /// </summary>
    public class AdoService : IAdoService
    {
        private readonly BuildHttpClient m_buildClient;

        private readonly ILogger m_logger;

        private readonly Guid m_projectId;

        /// <summary>
        /// We use bare HTTP for some methods that do not have a library interface
        /// </summary>
        private readonly VstsHttpRelay m_http;

        /// <summary>
        /// We need to query IMDS for some resource metadata 
        /// </summary>
        private readonly AzureInstanceMetadataService m_imds;

        /// <nodoc />
        public AdoService(IAdoEnvironment adoBuildRunnerEnv, ILogger logger)
        {
            m_logger = logger;
            m_projectId = new Guid(adoBuildRunnerEnv.TeamProjectId);
            m_http = new VstsHttpRelay(adoBuildRunnerEnv);
            m_imds = new AzureInstanceMetadataService();
            var server = new Uri(adoBuildRunnerEnv.ServerUri);
            var cred = new VssBasicCredential(string.Empty, adoBuildRunnerEnv.AccessToken);
            m_buildClient = new BuildHttpClient(server, cred);
        }

        /// <inheritdoc />
        public Task<Build> GetBuildAsync(int buildId)
        {
            return m_buildClient.GetBuildAsync(m_projectId, buildId);
        }

        /// <inheritdoc />
        public Task<PropertiesCollection> GetBuildPropertiesAsync(int buildId)
        {
            return m_buildClient.GetBuildPropertiesAsync(m_projectId, buildId);
        }

        /// <inheritdoc />
        public Task UpdateBuildPropertiesAsync(PropertiesCollection properties, int buildId)
        {
            return m_buildClient.UpdateBuildPropertiesAsync(properties, m_projectId, buildId);
        }

        /// <inheritdoc />
        public Task<Dictionary<string, string>> GetBuildTriggerInfoAsync()
        {
            return m_http.GetBuildTriggerInfoAsync();
        }

        /// <inheritdoc />
        public async Task<string> GetPoolNameAsync()
        {
            return await m_imds.GetPoolName(m_logger) ?? string.Empty;
        }
    }
}