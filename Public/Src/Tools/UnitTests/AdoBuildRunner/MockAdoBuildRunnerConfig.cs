// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection.PortableExecutable;
using AdoBuildRunner;
using BuildXL.AdoBuildRunner;

namespace Test.Tool.AdoBuildRunner
{
    /// <summary>
    /// Mock implementation of IAdoBuildRunnerConfig for testing purpose.
    /// </summary>
    public class MockAdoBuildRunnerConfig : IAdoBuildRunnerConfiguration
    {
        /// <inheritdoc />
        public MachineRole PipelineRole { get; set; }

        /// <inheritdoc />
        public string InvocationKey { get; set; }

        /// <inheritdoc />
        public int MaximumWaitForWorkerSeconds { get; set; }

        /// <inheritdoc />
        public bool DisableEncryption { get; set; }

        /// <inheritdoc />
        public bool WorkerAlwaysSucceeds { get; set; }

        public ICacheConfigGenerationConfiguration CacheConfigGenerationConfiguration { get; set; }

        /// <nodoc />
        public MockAdoBuildRunnerConfig()
        {
            PipelineRole = MachineRole.Orchestrator;
            InvocationKey = "MockInvocationKey";
            WorkerAlwaysSucceeds = true;
            DisableEncryption = true;
            MaximumWaitForWorkerSeconds = int.MaxValue;
            CacheConfigGenerationConfiguration = new MockCacheConfigGeneration();
        }
    }
}