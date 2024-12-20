// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using BuildXL.AdoBuildRunner;
using BuildXL.AdoBuildRunner.Vsts;

namespace Test.Tool.AdoBuildRunner
{
    /// <summary>
    /// Mock implementation of IAdoBuildRunnerConfig for testing purpose.
    /// </summary>
    public class MockAdoBuildRunnerConfig : IAdoBuildRunnerConfiguration
    {
        /// <inheritdoc />
        public string EngineLocation { get; set; }

        /// <inheritdoc />
        public global::BuildXL.AdoBuildRunner.AgentRole? AgentRole { get; set; }

        /// <inheritdoc />
        public string InvocationKey { get; set; }

        /// <inheritdoc />
        public int MaximumWaitForWorkerSeconds { get; set; }

        /// <inheritdoc />
        public bool DisableEncryption { get; set; }

        /// <inheritdoc />
        public bool WorkerAlwaysSucceeds { get; set; }

        public ICacheConfigGenerationConfiguration CacheConfigGenerationConfiguration { get; set; }


        public void PopulateFromEnvVars(IAdoEnvironment adoEnvironment, ILogger logger)
        {

        }

        /// <nodoc />
        public MockAdoBuildRunnerConfig()
        {
            EngineLocation = Path.GetTempPath();
            AgentRole = global::BuildXL.AdoBuildRunner.AgentRole.Orchestrator;
            InvocationKey = "MockInvocationKey";
            WorkerAlwaysSucceeds = true;
            DisableEncryption = true;
            MaximumWaitForWorkerSeconds = int.MaxValue;
            CacheConfigGenerationConfiguration = new MockCacheConfigGeneration();
        }
    }
}