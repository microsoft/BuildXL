// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Reflection;
using AdoBuildRunner;

namespace Test.Tool.AdoBuildRunner
{
    /// <summary>
    /// Mock implementation of IAdoBuildRunnerConfig for testing purpose.
    /// </summary>
    public class MockAdoBuildRunnerConfig : IAdoBuildRunnerConfig
    {
        /// <inheritdoc />
        public string AccessToken { get; set; }

        /// <inheritdoc />
        public string AdoBuildRunnerPipelineRole { get; set; }

        /// <inheritdoc />
        public string AdoBuildRunnerInvocationKey { get; set; }

        /// <inheritdoc />
        public int MaximumWaitForWorkerSeconds { get; set; }

        /// <inheritdoc />
        public bool DisableEncryption { get; set; }

        /// <inheritdoc />
        public bool WorkerAlwaysSucceeds { get; set; }

        /// <nodoc />
        public MockAdoBuildRunnerConfig()
        {
            AccessToken = "MockAccessToken";
            AdoBuildRunnerPipelineRole = "Orchestrator";
            AdoBuildRunnerInvocationKey = "MockInvocationKey";
            WorkerAlwaysSucceeds = true;
            DisableEncryption = true;
            MaximumWaitForWorkerSeconds = int.MaxValue;
        }
    }
}