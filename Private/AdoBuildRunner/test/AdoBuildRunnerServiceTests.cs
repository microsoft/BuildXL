// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using BuildXL.AdoBuildRunner;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.VisualStudio.Services.WebApi;
using Xunit;

namespace Test.Tool.AdoBuildRunner
{
    public class AdoBuildRunnerServiceTests : AdoBuildRunnerTestBase
    {
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
        [InlineData(true, false, 1)]
        [InlineData(false, false, 1)]
        [InlineData(true, false, 2)]
        [InlineData(false, false, 2)]
        public void GetInvocationKeyTest(bool isWorker, bool undefined, int attemptNumber)
        {
            var exceptionThrown = false;

            // Initialize the ADO Build Runner service and other mock services.
            var harness = CreateAgent(isWorker);
            harness.AdoEnvironment.JobAttemptNumber = attemptNumber;
            var specifiedKey = "testKey";
            harness.Config.InvocationKey = specifiedKey;
            harness.Initialize();
            var adoBuildRunnerService = harness.RunnerService;

            string chosenInvocationKey = null!;

            try
            {
                chosenInvocationKey = adoBuildRunnerService.GetInvocationKey();
            }
            catch (Exception ex)
            {
                // We expect an exception to be thrown if the env var is empty/null.
                exceptionThrown = true;
                Assert.Contains(ex.ToString(), "it is used to disambiguate between multiple builds running as part of the same pipeline");
            }

            if (undefined)
            {
                Assert.True(exceptionThrown);
            }
            else if (attemptNumber == 1)
            {
                Assert.Equal(specifiedKey, chosenInvocationKey);
                Assert.False(exceptionThrown);
            }
            else
            {
                // Since the job is rerun we expect the invocation key to be modified.
                Assert.NotEqual(specifiedKey, chosenInvocationKey);
                Assert.Contains("jobretry", chosenInvocationKey);
                Assert.False(exceptionThrown);
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
        [InlineData(false)]
        [InlineData(true)]
        public async Task TestPublishBuildInfo(bool encounterConflict)
        {
            var orchestratorBuildId = 12345;
            var exceptionThrown = false;

            // Initialize the ADO Build Runner service and other mock services.
            var harness = CreateWorker();
            harness.AdoEnvironment.BuildId = orchestratorBuildId;
            harness.Initialize();
            var adoBuildRunnerService = harness.RunnerService;

            // Add the orchestrator parsedOrchestratorBuildId and its properties to the mockServices.
            MockApiService.AddBuild(orchestratorBuildId, new Build());

            if (encounterConflict)
            {
                // Adding the invocation key as a property for the corresponding parsedOrchestratorBuildId to intentionally create a conflict for the second test case.
                // This creates the scenario where the invocation key has already been used, causing PublishBuildInfo method to throw an exception.
                MockApiService.AddBuildProperties(orchestratorBuildId, new PropertiesCollection()
                {
                    { harness.RunnerService.BuildContext.InvocationKey, "duplicateInvocationKey" }
                });
            }

            var buildInfo = CreateTestBuildInfo();
            try
            {
                // Attempt to publish build info using the ADO Build Runner service.
                // This should succeed for unique build IDs and fail for duplicate invocation keys.
                await adoBuildRunnerService.PublishBuildInfo(buildInfo);
            }
            catch (CoordinationException ex)
            {
                // Expect an exception for conflicting build IDs
                exceptionThrown = true;
                Assert.Contains("Identifiers (set through the environment variable", ex.ToString());
            }

            if (!encounterConflict)
            {
                // Verify that no exception was thrown.
                Assert.False(exceptionThrown);
                // Check if the build properties contain the serialized orchestrator data.
                var properties = await ((IAdoService)MockApiService).GetBuildPropertiesAsync(orchestratorBuildId);

                Assert.True(properties.ContainsValue(buildInfo.Serialize()));
            }
            else
            {
                Assert.True(exceptionThrown);
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
            var orchHarness = CreateOrchestrator();
            orchHarness.AdoEnvironment.LocalSourceVersion = "version1.0";
            orchHarness.AdoEnvironment.LocalSourceBranch = "currentBranch";
            orchHarness.Initialize();

            // Initialize the ADO Build Runner service and mock services using the helper method.
            var harness = CreateWorker();
            harness.AdoEnvironment.LocalSourceBranch = "unexpectedBranch";
            harness.AdoEnvironment.LocalSourceVersion = "version2.0";
            harness.AdoEnvironment.JobId = "10009";

            harness.Initialize();
            var adoBuildRunnerService = harness.RunnerService;

            // Add the orchestrator build to the mock ADO API service.
            // Create a mock orchestrator build with the specified source branch and version.
            var orchestratorBuild = CreateTestBuild(orchHarness.AdoEnvironment);
            MockApiService.AddBuild(TestOrchestratorId, orchestratorBuild);

            // Add the orchestrator's build ID to the mock ADO API services to indicate it is triggering the worker.
            MockApiService.AddBuildTriggerProperties(Constants.TriggeringAdoBuildIdParameter, TestOrchestratorId.ToString());

            // Add orchestrator's mock build properties for testing purpose.
            var buildInfo = CreateTestBuildInfo();
            var orchestratorProperties = new PropertiesCollection
            {
                { orchHarness.RunnerService.BuildContext.InvocationKey, buildInfo.Serialize() }
            };

            MockApiService.AddBuildProperties(TestOrchestratorId, orchestratorProperties);

            try
            {
                var result = await adoBuildRunnerService.WaitForBuildInfo();
            }
            catch (CoordinationException ex)
            {
                // Expect an exception to be thrown.
                exceptionThrown = true;
                Assert.Contains("Version control mismatch between the orchestrator build.", ex.ToString());
            }

            // Ensure that an exception was thrown.
            Assert.True(exceptionThrown);
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
            var orchHarness = CreateOrchestrator();
            orchHarness.AdoEnvironment.LocalSourceBranch = "currentBranch";
            orchHarness.AdoEnvironment.LocalSourceVersion = "version1.0";
            orchHarness.Initialize();

            // Initialize the ADO Build Runner service and mock services using the helper method.
            var harness = CreateWorker();
            harness.AdoEnvironment.LocalSourceBranch = orchHarness.AdoEnvironment.LocalSourceBranch;
            harness.AdoEnvironment.LocalSourceVersion = orchHarness.AdoEnvironment.LocalSourceVersion;
            harness.AdoEnvironment.JobId = jobId;
            harness.Initialize();
            var adoBuildRunnerService = harness.RunnerService;

            // Create a mock orchestrator build with the specified source branch and version.
            var orchestratorBuild = CreateTestBuild(orchHarness.AdoEnvironment);
            // Add the orchestrator build to the mock ADO API service.
            MockApiService.AddBuild(TestOrchestratorId, orchestratorBuild);

            // Add the orchestrator's build ID to the mock ADO API services to indicate it is triggering the worker.
            MockApiService.AddBuildTriggerProperties(Constants.TriggeringAdoBuildIdParameter, TestOrchestratorId.ToString());

            // Add orchestrator's mock build properties for testing purpose.
            // When testing for duplicate chosenInvocationKeys, we add the worker sentinel.
            if (!shouldSucceed)
            {
                MockApiService.AddBuildProperties(TestOrchestratorId, new()
                {
                    { orchHarness.RunnerService.BuildContext.InvocationKey + "__workerjobid", "1" }
                });
            }

            // We assume that the orchestrator has successfully published its address.
            // Hence we map the chosenInvocationKey with the below value.
            MockApiService.AddBuildProperties(TestOrchestratorId, new()
            {
                { orchHarness.RunnerService.BuildContext.InvocationKey, CreateTestBuildInfo().Serialize() }
            });

            try
            {
                var result = await adoBuildRunnerService.WaitForBuildInfo();
            }
            catch (Exception ex)
            {
                // Expect an exception to be thrown.
                exceptionThrown = true;
                if (!shouldSucceed)
                {
                    Assert.Contains("All workers participating in the build", ex.ToString());
                }
            }

            // Ensure that an exception was thrown if, expected.
            if (shouldSucceed)
            {
                Assert.False(exceptionThrown);
            }
            else
            {
                Assert.True(exceptionThrown);
            }
        }

        /// <summary>
        /// This test verifies the case where we have two workers and both the workers with the same JobId, SourceBranch and SourceVersion should be able to retrieve the buildInfo.
        /// </summary>
        [Fact]
        public async Task TestWaitForBuildInfoForMultipleWorkers()
        {
            // Orchestrator source branch and source version.
            var orchHarness = CreateOrchestrator();
            orchHarness.AdoEnvironment.LocalSourceBranch = "currentBranch";
            orchHarness.AdoEnvironment.LocalSourceVersion = "version1.0";
            orchHarness.Initialize();

            // Initialize the ADO Build Runner service and mock services using the helper method.
            var worker1 = CreateWorker(position: 1, totalWorkers: 2);
            var worker2 = CreateWorker(position: 2, totalWorkers: 2);

            var i = 1;
            foreach (var worker in new[] { worker1, worker2 })
            {
                worker.AdoEnvironment.LocalSourceBranch = orchHarness.AdoEnvironment.LocalSourceBranch;
                worker.AdoEnvironment.LocalSourceVersion = orchHarness.AdoEnvironment.LocalSourceVersion;
                worker.AdoEnvironment.JobPositionInPhase = i++;
                worker.AdoEnvironment.TotalJobsInPhase = 2;
                worker.AdoEnvironment.JobId = "10009";
                worker.Initialize();
            }

            // Add the orchestrator build to the mock ADO API service.
            // Create a mock orchestrator build with the specified source branch and version.
            var orchestratorBuild = CreateTestBuild(orchHarness.AdoEnvironment);
            MockApiService.AddBuild(TestOrchestratorId, orchestratorBuild);

            // Add the orchestrator's build ID to the mock ADO API services to indicate it is triggering the worker.
            MockApiService.AddBuildTriggerProperties(Constants.TriggeringAdoBuildIdParameter, TestOrchestratorId.ToString());

            // Add orchestrator's mock build properties for testing purpose.
            // When testing for duplicate chosenInvocationKey's, we add the chosenInvocationKey with different jobId.
            PropertiesCollection orchestratorProperties;
            // We assume that the orchestrator has successfully published its address.
            // Hence we map the chosenInvocationKey with the below value.
            var buildInfo = CreateTestBuildInfo();
            orchestratorProperties = new()
            {
                 { orchHarness.RunnerService.BuildContext.InvocationKey, buildInfo.Serialize() }
            };

            MockApiService.AddBuildProperties(TestOrchestratorId, orchestratorProperties);

            var worker2BuildInfo = await worker2.RunnerService.WaitForBuildInfo();
            var worker1BuildInfo = await worker1.RunnerService.WaitForBuildInfo();
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
            var harness = CreateAgent(!isOrchestrator);
            // Initialize the ADO Build Runner service and mock services using the helper method.
            harness.Config.InvocationKey = "invocationKey";
            harness.Initialize();

            // Initialize the expected arguments based on whether it's an orchestrator or a worker
            string[] expectedArgs;

            if (isOrchestrator)
            {
                expectedArgs = new string[]
                {
                            "/p:BuildXLWorkerAttachTimeoutMin=20",
                            $"/cacheMiss:{harness.RunnerService.BuildContext.InvocationKey}",
                            "/distributedBuildRole:orchestrator",
                            $"/distributedBuildServicePort:{Constants.MachineGrpcPort}",
                            $"/relatedActivityId:{TestRelatedSessionId}",
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
                            $"/cacheMiss:{harness.RunnerService.BuildContext.InvocationKey}",
                            $"/distributedBuildRole:worker",
                            $"/distributedBuildServicePort:{Constants.MachineGrpcPort}",
                            $"/distributedBuildOrchestratorLocation:{buildInfo.OrchestratorLocation}:{Constants.MachineGrpcPort}",
                            $"/relatedActivityId:{buildInfo.RelatedSessionId}",
                            "arg1",
                            "arg2",
                            "arg3"
                };
            }

            var adoBuildRunnerService = harness.RunnerService;
            var args = harness.BuildExecutor.ConstructArguments(buildInfo, TestDefaultArgs);
            AssertContains(args, expectedArgs);
        }
    }
}
