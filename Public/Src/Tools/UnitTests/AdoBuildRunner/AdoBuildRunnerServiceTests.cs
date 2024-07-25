// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using BuildXL.AdoBuildRunner;
using BuildXL.AdoBuildRunner.Build;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.VisualStudio.Services.WebApi;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.Tool.AdoBuildRunner
{
    public class AdoBuildRunnerServiceTests : AdoBuildRunnerTestBase
    {
        private static readonly string s_testRelatedSessionId = "testId123";

        private static readonly string s_testOrchestratorLocation = "testLocation";

        private static readonly string s_invocationKey = "testKey";

        private static readonly string s_orchestratorId = "12345";

        private static readonly int s_workerId = 789;

        private static readonly string[] s_defaultArgs = { "arg1", "arg2", "arg3" };

        /// <summary>
        /// Tests that the correct invocation key is retrieved for the build.
        /// </summary>
        /// <remarks>
        /// Case 1: The invocation key should be obtained from the InvocationKey environment variable, the job is run only once.
        /// Case 2: If the job is rerun, the invocation key should be modified to avoid conflicts with the previous run.
        /// Case 3: If the environment variable value cannot be retrieved, an exception should be thrown and handled gracefully.
        /// </remarks>
        /// <param name="expectedInvocationKey">Represents the invocation key</param>
        /// <param name="jobAttemptVariableName"> Represents the job attempt number</param>
        [Theory]
        [InlineData("testKey1", 1)]
        [InlineData("testKey2", 2)]
        [InlineData(null, 1)]
        public void GetInvocationKeyTest(string expectedInvocationKey, int jobAttemptVariableName)
        {
            var exceptionThrown = false;

            // Initialize the ADO Build Runner service and other mock services.
            MockAdoEnvironment.JobAttemptNumber = jobAttemptVariableName;
            MockConfig.InvocationKey = expectedInvocationKey;
            var adoBuildRunnerService = CreateAdoBuildRunnerService();

            string invocationKey = null;

            try
            {
                invocationKey = adoBuildRunnerService.GetInvocationKey();
            }
            catch (Exception ex)
            {
                // We expect an exception to be thrown if the env var is empty/null.
                exceptionThrown = true;
                XAssert.Contains(ex.ToString(), "it is used to disambiguate between multiple builds running as part of the same pipeline");
            }

            if (expectedInvocationKey == "testKey1")
            {
                XAssert.AreEqual(expectedInvocationKey, invocationKey);
                XAssert.IsFalse(exceptionThrown);
            }
            else if (expectedInvocationKey == "testKey2")
            {
                // Since the job is rerun we expect the invocation key to be modified.
                XAssert.Contains(invocationKey, "jobretry");
                XAssert.IsFalse(exceptionThrown);
            }
            else if (string.IsNullOrEmpty(invocationKey))
            {
                XAssert.IsTrue(exceptionThrown);
            }
        }

        /// <summary>
        /// Test to verify that the orchestrator publishes BuildInfo correctly for unique build IDs and throws an exception for duplicate build IDs.
        /// </summary>
        /// <remarks>
        /// Case 1: When the build identifier is valid and unique, PublishBuildInfo should publish the orchestrators build properties without exceptions.
        /// Case 2: When the build identifier has been used before, an exception should be thrown to indicate a conflict.
        /// </remarks>
        [Theory]
        [InlineData(12345)]
        [InlineData(789)]
        public async Task TestPublishBuildInfo(int orchestratorBuildId)
        {
            var exceptionThrown = false;

            // Initialize the ADO Build Runner service and other mock services.
            MockAdoEnvironment.BuildId = orchestratorBuildId;
            var adoBuildRunnerService = CreateAdoBuildRunnerService();

            // Add the orchestrator parsedOrchestratorBuildId and its properties to the mockServices.
            MockAdoApiService.AddBuildId(orchestratorBuildId, new Build());

            PropertiesCollection buildProperties;
            if (orchestratorBuildId == 12345)
            {
                buildProperties = new Microsoft.VisualStudio.Services.WebApi.PropertiesCollection();
            }
            else
            {
                // Adding the invocationKey as a property for the corresponding parsedOrchestratorBuildId to intentionally create a conflict for the second test case.
                // This creates the scenario where the invocationKey has already been used, causing PublishBuildInfo method to throw an exception.
                buildProperties = new()
                {
                    {s_invocationKey, "duplicateInvocationKey" }
                };
            }
            MockAdoApiService.AddBuildProperties(orchestratorBuildId, buildProperties);

            try
            {
                // Attempt to publish build info using the ADO Build Runner service.
                // This should succeed for unique build IDs and fail for duplicate invocation keys.
                await adoBuildRunnerService.PublishBuildInfo(CreateTestBuildContext(orchestratorBuildId, s_invocationKey), CreateTestBuildInfo());
            }
            catch (Exception ex)
            {
                // Expect an exception for conflicting build IDs
                exceptionThrown = true;
                XAssert.Contains(ex.ToString(), "Identifiers (set through the environment variable");
            }

            if (orchestratorBuildId == 12345)
            {
                // Verify that no exception was thrown.
                XAssert.IsFalse(exceptionThrown);
                // Check if the build properties contain the serialized orchestrator data.
                var properties = await MockAdoApiService.GetBuildPropertiesAsync(orchestratorBuildId);
                XAssert.IsTrue(properties.ContainsValue($"{s_testRelatedSessionId};{s_testOrchestratorLocation}"));
            }
            else
            {
                XAssert.IsTrue(exceptionThrown);
            }
        }

        /// <summary>
        /// Test to verify that an error is thrown when the worker's source branch and source version do not match the orchestrator's source branch and source version.
        /// </summary>
        [Fact]
        public async Task TestSourceBranchAndVersionMismatch()
        {
            var exceptionThrown = false;

            // Orchestrator source branch and source version.
            var orchestratorSourceBranch = "currentBranch";
            var orchestratorSourceVersion = "version1.0";

            // Create a mock orchestrator build with the specified source branch and version.
            var orchestratorBuild = CreateTestBuild(orchestratorSourceBranch, orchestratorSourceVersion, s_orchestratorId);

            // Initialize the ADO Build Runner service and mock services using the helper method.
            MockAdoEnvironment.LocalSourceBranch = "unexpectedBranch";
            MockAdoEnvironment.LocalSourceVersion = "version2.0";
            MockAdoEnvironment.JobId = "10009";
            var adoBuildRunnerService = CreateAdoBuildRunnerService();

            // Create the build context for the worker.
            var buildContext = CreateTestBuildContext(s_workerId, s_invocationKey);

            // Add the orchestrator build to the mock ADO API service.
            MockAdoApiService.AddBuildId(int.Parse(s_orchestratorId), orchestratorBuild);

            // Add the orchestrator's build ID to the mock ADO API services to indicate it is triggering the worker.
            MockAdoApiService.AddBuildTriggerProperties(Constants.TriggeringAdoBuildIdParameter, s_orchestratorId);

            // Add orchestrator's mock build properties for testing purpose.
            var orchestratorProperties = new PropertiesCollection
            {
                { s_invocationKey, $"{s_testRelatedSessionId};{s_testOrchestratorLocation}" }
            };

            MockAdoApiService.AddBuildProperties(int.Parse(s_orchestratorId), orchestratorProperties);

            try
            {
                var result = await adoBuildRunnerService.WaitForBuildInfo(buildContext);
            }
            catch (Exception ex)
            {
                // Expect an exception to be thrown.
                exceptionThrown = true;
                XAssert.Contains(ex.ToString(), "Version control mismatch between the orchestrator build.");
            }

            // Ensure that an exception was thrown.
            XAssert.IsTrue(exceptionThrown);
        }

        /// <summary>
        /// This test verifies various use cases related to the worker waiting for orchestrators buildInfo and also verifies the worker's correctness scenarios.
        /// </summary>
        /// <remarks>
        /// Case 1: When the workers with the same invocation key have different job ids(indicates that pipeline specification is duplicating invocation keys), an error is thrown.
        /// Case 2: When the worker's source branch and source version match the orchestrator's and there are no duplicate invocation keys, a successful build is expected.
        /// </remarks>
        [Theory]
        [InlineData("10020", false)]
        [InlineData("10009", true)]
        public async Task TestWaitForBuildInfo(string jobId, bool shouldSucceed)
        {
            var exceptionThrown = false;

            // Orchestrator source branch and source version.
            var orchestratorSourceBranch = "currentBranch";
            var orchestratorSourceVersion = "version1.0";

            // Create a mock orchestrator build with the specified source branch and version.
            var orchestratorBuild = CreateTestBuild(orchestratorSourceBranch, orchestratorSourceVersion, s_orchestratorId);

            // Initialize the ADO Build Runner service and mock services using the helper method.
            MockAdoEnvironment.LocalSourceBranch = "currentBranch";
            MockAdoEnvironment.LocalSourceVersion = "version1.0";
            MockAdoEnvironment.JobId = jobId;
            var adoBuildRunnerService = CreateAdoBuildRunnerService();

            // Create the build context for the worker.
            var buildContext = CreateTestBuildContext(s_workerId, s_invocationKey);

            // Add the orchestrator build to the mock ADO API service.
            MockAdoApiService.AddBuildId(int.Parse((string)s_orchestratorId), orchestratorBuild);

            // Add the orchestrator's build ID to the mock ADO API services to indicate it is triggering the worker.
            MockAdoApiService.AddBuildTriggerProperties(Constants.TriggeringAdoBuildIdParameter, s_orchestratorId);

            // Add orchestrator's mock build properties for testing purpose.
            // When testing for duplicate invocationKey's, we add the invocationKey with different jobId.
            PropertiesCollection orchestratorProperties;
            if (!shouldSucceed)
            {
                orchestratorProperties = new()
                {
                    { s_invocationKey + "__workerjobid", "10009" }
                };
            }
            else
            {
                // We assume that the orchestrator has successfully published its address.
                // Hence we map the invocationKey with the below value.
                orchestratorProperties = new()
                {
                    { s_invocationKey, $"{s_testRelatedSessionId};{s_testOrchestratorLocation}" }
                };
            }
            MockAdoApiService.AddBuildProperties(int.Parse((string)s_orchestratorId), orchestratorProperties);

            try
            {
                var result = await adoBuildRunnerService.WaitForBuildInfo(buildContext);
            }
            catch (Exception ex)
            {
                // Expect an exception to be thrown.
                exceptionThrown = true;
                if (!shouldSucceed)
                {
                    XAssert.Contains(ex.ToString(), "All workers participating in the build");
                }
            }

            // Ensure that an exception was thrown if, expected.
            if (shouldSucceed)
            {
                XAssert.IsFalse(exceptionThrown);
            }
            else
            {
                XAssert.IsTrue(exceptionThrown);
            }
        }

        /// <summary>
        /// This test verifies the case where we have two workers and both the workers with the same JobId, SourceBranch and SourceVersion should be able to retrieve the buildInfo.
        /// </summary>
        [Fact]
        public async Task TestWaitForBuildInfoForMultipleWorkers()
        {
            var exceptionThrown = false;
            var buildId = 7890;
            // Orchestrator source branch and source version.
            var orchestratorSourceBranch = "currentBranch";
            var orchestratorSourceVersion = "version1.0";

            // Create a mock orchestrator build with the specified source branch and version.
            var orchestratorBuild = CreateTestBuild(orchestratorSourceBranch, orchestratorSourceVersion, s_orchestratorId);

            // Initialize the ADO Build Runner service and mock services using the helper method.
            MockAdoEnvironment.LocalSourceBranch = "currentBranch";
            MockAdoEnvironment.LocalSourceVersion = "version1.0";
            MockAdoEnvironment.TotalJobsInPhase = 2;
            MockAdoEnvironment.JobId = "10009";
            var adoBuildRunnerService = CreateAdoBuildRunnerService();

            // Add the orchestrator build to the mock ADO API service.
            MockAdoApiService.AddBuildId(int.Parse((string)s_orchestratorId), orchestratorBuild);

            // Add the orchestrator's build ID to the mock ADO API services to indicate it is triggering the worker.
            MockAdoApiService.AddBuildTriggerProperties(Constants.TriggeringAdoBuildIdParameter, s_orchestratorId);

            // Add orchestrator's mock build properties for testing purpose.
            // When testing for duplicate invocationKey's, we add the invocationKey with different jobId.
            PropertiesCollection orchestratorProperties;
            // We assume that the orchestrator has successfully published its address.
            // Hence we map the invocationKey with the below value.
            orchestratorProperties = new()
            {
                 { s_invocationKey, $"{s_testRelatedSessionId};{s_testOrchestratorLocation}" }
            };

            MockAdoApiService.AddBuildProperties(int.Parse((string)s_orchestratorId), orchestratorProperties);

            try
            {
                // Create the build context for the worker1 and worker 2
                MockAdoEnvironment.JobPositionInPhase = 2;
                var buildContext2 = CreateTestBuildContext(buildId, s_invocationKey);
                var worker2BuildInfo = await adoBuildRunnerService.WaitForBuildInfo(buildContext2);

                var buildContext1 = CreateTestBuildContext(buildId, s_invocationKey);
                MockAdoEnvironment.JobPositionInPhase = 1;
                var worker1BuildInfo = await adoBuildRunnerService.WaitForBuildInfo(buildContext1);
            }
            catch (Exception ex)
            {
                exceptionThrown = true;
                XAssert.Contains(ex.ToString(), "All workers participating in the build");
            }
            XAssert.IsFalse(exceptionThrown);
        }

        /// <summary>
        /// Test verifies the arguments constructed for Workers and Orchestrator.
        /// </summary>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void TestBuildArgsForOrchestratorAndWorker(bool isOrchestrator)
        {
            // Create build info for the test.
            var buildInfo = CreateTestBuildInfo();

            // Initialize the expected arguments based on whether it's an orchestrator or a worker
            string[] expectedArgs;

            if (isOrchestrator)
            {
                expectedArgs = new string[]
                {
                            "/p:BuildXLWorkerAttachTimeoutMin=20",
                            $"/cacheMiss:{s_invocationKey}",
                            "/distributedBuildRole:orchestrator",
                            $"/distributedBuildServicePort:{Constants.MachineGrpcPort}",
                            $"/relatedActivityId:{s_testRelatedSessionId}",
                            "arg1",
                            "arg2",
                            "arg3"
                };
            }
            else
            {
                expectedArgs = new string[]
                {
                            "/p:BuildXLWorkerAttachTimeoutMin=20",
                            $"/cacheMiss:{s_invocationKey}",
                            $"/distributedBuildRole:worker",
                            $"/distributedBuildServicePort:{Constants.MachineGrpcPort}",
                            $"/distributedBuildOrchestratorLocation:{buildInfo.OrchestratorLocation}:{Constants.MachineGrpcPort}",
                            $"/relatedActivityId:{buildInfo.RelatedSessionId}",
                            "arg1",
                            "arg2",
                            "arg3"
                };
            }

            // Initialize the ADO Build Runner service and mock services using the helper method.
            var adoBuildRunnerService = CreateAdoBuildRunnerService();
            MockConfig.InvocationKey = s_invocationKey;

            IBuildExecutor buildExecutor;
            // Create the build executor.
            if (isOrchestrator)
            {
                buildExecutor = new OrchestratorBuildExecutor(MockLogger, adoBuildRunnerService);
            }
            else
            {
                buildExecutor = new WorkerBuildExecutor(MockLogger, adoBuildRunnerService);
            }

            var buildContext = CreateTestBuildContext(123, s_invocationKey);

            if (isOrchestrator)
            {
                var orchestratorArgs = buildExecutor.ConstructArguments(buildContext, buildInfo, s_defaultArgs);
                XAssert.Contains(orchestratorArgs, expectedArgs);
            }
            else
            {
                var workerArgs = buildExecutor.ConstructArguments(buildContext, buildInfo, s_defaultArgs);
                XAssert.Contains(workerArgs, expectedArgs);
            }
        }

        /// <summary>
        /// Creates BuildContext for testing purpose.
        /// </summary>
        public BuildContext CreateTestBuildContext(int buildId, string invocationKey)
        {
            var buildContext = new BuildContext()
            {
                InvocationKey = invocationKey,
                StartTime = DateTime.UtcNow,
                BuildId = buildId,
                AgentMachineName = "testAgentMachine",
                AgentHostName = $"testAgentMachine.internal.cloudapp.net",
                SourcesDirectory = "testSourceDir",
                RepositoryUrl = "testUrl",
                ServerUrl = "testServerUri",
                TeamProjectId = "teamProjectId",
            };
            return buildContext;
        }

        /// <summary>
        /// Creates BuildInfo for testing purpose.
        /// </summary>
        public BuildInfo CreateTestBuildInfo()
        {
            var buildInfo = new BuildInfo()
            {
                RelatedSessionId = s_testRelatedSessionId,
                OrchestratorLocation = s_testOrchestratorLocation
            };

            return buildInfo;
        }

        /// <summary>
        /// Create Build for testing purpose
        /// </summary>
        public Build CreateTestBuild(string sourceBranch, string sourceVersion, string buildId)
        {
            Build build = new Build();
            build.SourceBranch = sourceBranch;
            build.SourceVersion = sourceVersion;
            build.Id = int.Parse((string)buildId);
            return build;
        }

    }
}
