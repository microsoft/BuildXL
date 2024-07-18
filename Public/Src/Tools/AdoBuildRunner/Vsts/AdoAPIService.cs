// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.AdoBuildRunner.Vsts;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;

namespace AdoBuildRunner
{
    /// <summary>
    /// Concrete implementation of the ADO API interface for interacting with ADO build services.
    /// </summary>
    public class AdoApiService : IAdoAPIService
    {
        private readonly BuildHttpClient m_buildClient;

        private readonly ILogger m_logger;

        private readonly Guid m_projectId;

        /// <summary>
        /// We use bare HTTP for some methods that do not have a library interface
        /// </summary>
        private readonly VstsHttpRelay m_http;

        /// <nodoc />
        public AdoApiService(ILogger logger, IAdoEnvironment adoBuildRunnerEnvConfig, IAdoBuildRunnerConfig adoBuildRunnerUserConfig)
        {
            m_logger = logger;
            m_projectId = new Guid(adoBuildRunnerEnvConfig.TeamProjectId);
            m_http = new VstsHttpRelay(adoBuildRunnerEnvConfig, adoBuildRunnerUserConfig, logger);
            var server = new Uri(adoBuildRunnerEnvConfig.ServerUri);
            var cred = new VssBasicCredential(string.Empty, adoBuildRunnerUserConfig.AccessToken);
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
    }
}