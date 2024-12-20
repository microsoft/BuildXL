// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Services.WebApi;

namespace BuildXL.AdoBuildRunner.Utilities.Mocks
{
    /// <summary>
    /// Provides a mock implementation of the Ado API's for testing purpose.
    /// </summary>
    public class MockAdoService : IAdoService
    {
        /// <nodoc />
        public readonly Dictionary<int, PropertiesCollection> BuildProperties = new Dictionary<int, PropertiesCollection>();
        /// <nodoc />
        public string PoolName => GetPoolName?.Invoke() ?? "AgentPoolName";
        /// <nodoc />
        public Func<string>? GetPoolName { get; set; } = null;

        private readonly Dictionary<int, Microsoft.TeamFoundation.Build.WebApi.Build> m_adoBuilds = new Dictionary<int, Microsoft.TeamFoundation.Build.WebApi.Build>();

        private readonly Dictionary<string, string> m_buildTriggerProperties = new Dictionary<string, string>();

        private readonly bool m_mockApiException;

        /// <nodoc/>
        public MockAdoService(IAdoEnvironment adoEnvironment)
        {
            Microsoft.TeamFoundation.Build.WebApi.Build build = new();
            build.SourceBranch = adoEnvironment.LocalSourceBranch;
            build.SourceVersion = adoEnvironment.LocalSourceVersion;
            build.Id = adoEnvironment.BuildId;
            AddBuild(adoEnvironment.BuildId, build);
        }

        /// <summary>
        /// Intialize MockAdoAPIService with a mock exception to simulate an error for some API's.
        /// </summary>
        public MockAdoService(bool setMockException)
        {
            m_mockApiException = setMockException;
        }

        /// <nodoc />
        public MockAdoService()
        {
        }

        /// <summary>
        /// Retrieves the build properties for the specificied buildId and throws if buildId does not exist.
        /// </summary>
        Task<PropertiesCollection> IAdoService.GetBuildPropertiesAsync(int buildId)
        {
            if (m_mockApiException)
            {
                throw new Exception("Failed to extract build information");
            }

            if (BuildProperties.ContainsKey(buildId))
            {
                return Task.FromResult(BuildProperties[buildId]);
            }

            throw new Exception($"Build properties not found for the build with id {buildId}");
        }

        /// <summary>
        /// Retrieves a build for the specified buildId and throws if the buildId does not exist.
        /// </summary>
        Task<Microsoft.TeamFoundation.Build.WebApi.Build> IAdoService.GetBuildAsync(int buildId)
        {
            if (m_mockApiException)
            {
                throw new Exception("Failed to extract build information");
            }

            if (m_adoBuilds.ContainsKey(buildId))
            {
                return Task.FromResult(m_adoBuilds[buildId]);
            }

            throw new Exception($"Build not found for the build: {buildId}");
        }

        /// <summary>
        /// Updates the build properties for the specified buildId and throws an exception if the buildId does not exist.
        /// </summary>
        Task IAdoService.UpdateBuildPropertiesAsync(PropertiesCollection properties, int buildId)
        {
            if (m_mockApiException)
            {
                throw new Exception("Failed to extract build information");
            }

            if (!BuildProperties.ContainsKey(buildId))
            {
                throw new Exception($"Build properties not found for the: {buildId}");
            }

            foreach (var property in properties)
            {
                BuildProperties[buildId][property.Key] = property.Value;
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Retrieves build trigger information. Simulates an API exception if configured to do so.
        /// </summary>
        Task<Dictionary<string, string>> IAdoService.GetBuildTriggerInfoAsync()
        {
            if (m_mockApiException)
            {
                throw new Exception("Failed to extract build information");
            }

            return Task.FromResult(m_buildTriggerProperties);
        }

        /// <summary>
        /// Adds build properties associated with a specified buildId.
        /// </summary>
        public void AddBuildProperties(int buildId, PropertiesCollection properties)
        {
            if (!BuildProperties.ContainsKey(buildId))
            {
                BuildProperties[buildId] = [];
            }

            foreach (var kvp in properties)
            {
                BuildProperties[buildId].Add(kvp.Key, kvp.Value);
            }
        }

        /// <summary>
        /// Adds or updates a build in the mock service for a specified buildId.
        /// </summary>
        public void AddBuild(int buildId, Microsoft.TeamFoundation.Build.WebApi.Build build)
        {
            m_adoBuilds[buildId] = build;
            AddBuildProperties(buildId, []); // Any known build has properties, even if empty
        }

        /// <summary>
        /// Adds or updates a buildProperties in the mock service for a specified property.
        /// </summary>
        public void AddBuildTriggerProperties(string triggerIdProperty, string triggerIdValue)
        {
            m_buildTriggerProperties[triggerIdProperty] = triggerIdValue;
        }

        /// <nodoc />
        public Task<string> GetPoolNameAsync()
        {
            return Task.FromResult(PoolName);
        }
    }
}