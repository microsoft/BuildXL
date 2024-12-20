// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.AdoBuildRunner;
using BuildXL.AdoBuildRunner.Utilities.Mocks;
using BuildXL.AdoBuildRunner.Utilties.Mocks;
using BuildXL.AdoBuildRunner.Vsts;
using Microsoft.TeamFoundation.Build.WebApi;

namespace Test.Tool.AdoBuildRunner
{
    public class AdoBuildRunnerTestBase : TestBase
    {
        protected static readonly string TestRelatedSessionId = "testId123";

        protected static readonly string TestOrchestratorLocation = "testLocation";
        protected static readonly string TestOrchestratorPool = "testPool";

        protected static readonly int TestOrchestratorId = 12345;

        protected static readonly int TestWorkerId = 789;

        protected static readonly string[] TestDefaultArgs = { "arg1", "arg2", "arg3" };


        protected class AgentHarness
        {
            /// <nodoc />
            public MockAdoEnvironment AdoEnvironment { get; set; }

            /// <nodoc />
            public MockAdoBuildRunnerConfig Config { get; set; }

            public AdoBuildRunnerService RunnerService
            {
                get
                {
                    Contract.Assert(m_isInitialized);
                    return m_runnerService!;
                }
            }
            private AdoBuildRunnerService? m_runnerService;

            public IBuildExecutor BuildExecutor
            {
                get
                {
                    Contract.Assert(m_isInitialized);
                    return m_buildExecutor!;
                }
            }
            private IBuildExecutor? m_buildExecutor;
            private readonly IAdoService m_adoService;
            private readonly bool m_isWorker;
            private bool m_isInitialized;

            /// <nodoc />
            public MockLogger MockLogger { get; private set; }

            /// <nodoc />
            public MockLauncher MockLauncher { get; private set; } = new();

            internal AgentHarness(IAdoService service, bool isWorker)
            {
                m_adoService = service;
                m_isWorker = isWorker;
                MockLogger = new MockLogger();
                Config = new MockAdoBuildRunnerConfig();
                Config.AgentRole = isWorker ? AgentRole.Worker : AgentRole.Orchestrator;
                AdoEnvironment = new MockAdoEnvironment();
            }

            internal void Initialize()
            {
                m_runnerService = new AdoBuildRunnerService(MockLogger, AdoEnvironment, m_adoService, Config);
                m_buildExecutor = m_isWorker ? new WorkerBuildExecutor(MockLauncher, m_runnerService, MockLogger) : new OrchestratorBuildExecutor(MockLauncher, m_runnerService, MockLogger);
                m_isInitialized = true;
            }
        }

        // The "API service" is shared by all agents created for the test, as 
        // if everyone is querying against the same ADO endpoints
        protected MockAdoService MockApiService { get; set; } = new();

        /// <nodoc />
        public AdoBuildRunnerTestBase()
        {
        }

        protected AgentHarness CreateAgent(bool isWorker)
        {
            return isWorker ? CreateWorker() : CreateOrchestrator();
        }

        protected AgentHarness CreateWorker(int? position = null, int? totalWorkers = null)
        {
            var harness = new AgentHarness(MockApiService, isWorker: true);
            position ??= 2; // By default make it a generic worker
            harness.AdoEnvironment.JobPositionInPhase = position.Value;
            harness.AdoEnvironment.TotalJobsInPhase = totalWorkers ?? Math.Max(position.Value, 2); // By default make it a part of a group of workers
            harness.Initialize();
            return harness;
        }

        protected AgentHarness CreateOrchestrator()
        {
            var harness = new AgentHarness(MockApiService, isWorker: false);
            harness.AdoEnvironment.JobPositionInPhase = 1;
            harness.AdoEnvironment.TotalJobsInPhase = 1;
            return harness;
        }

        /// <summary>
        /// Creates BuildInfo for testing purpose.
        /// </summary>
        protected BuildInfo CreateTestBuildInfo()
        {
            var buildInfo = new BuildInfo()
            {
                RelatedSessionId = TestRelatedSessionId,
                OrchestratorLocation = TestOrchestratorLocation,
                OrchestratorPool = TestOrchestratorPool,
            };

            return buildInfo;
        }

        /// <summary>
        /// Create Build for testing purpose
        /// </summary>
        protected Build CreateTestBuild(IAdoEnvironment adoEnvironment)
        {
            Build build = new Build();
            build.SourceBranch = adoEnvironment.LocalSourceBranch;
            build.SourceVersion = adoEnvironment.LocalSourceVersion;
            build.Id = adoEnvironment.BuildId;
            return build;
        }
    }
}
