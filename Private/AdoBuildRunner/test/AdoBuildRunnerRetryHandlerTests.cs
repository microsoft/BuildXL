// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.AdoBuildRunner;
using BuildXL.AdoBuildRunner.Utilities.Mocks;
using Microsoft.TeamFoundation.Build.WebApi;
using Xunit;

namespace Test.Tool.AdoBuildRunner
{
    public class AdoBuildRunnerRetryHandlerTests
    {
        private const int MaxRetryAttempts = 3;

        private readonly List<int> m_expectedBuildIds = new List<int> { 1234, 234, 456 };

        /// <summary>
        /// Api methods from the MockApiService which are to be tested.
        /// </summary>
        public enum ApiMethod
        {
            GetBuildAsync,
            GetBuildPropertiesAsync,
            UpdateBuildPropertiesAsync,
            GetBuildTriggerInfoAsync
        }

        /// <summary>
        /// Tests the AdoBuildRunnerRetryHandler's response to API failures across various methods.
        /// This test simulates API method calls with induced failures to verify if the AdoBuildRunnerRetryHandler
        /// properly attempts retries up to the maximum configured limit and ensures that it throws if all retry attempts fail.
        /// </summary>
        [Theory]
        [InlineData(ApiMethod.GetBuildAsync, false)]
        [InlineData(ApiMethod.GetBuildPropertiesAsync, false)]
        [InlineData(ApiMethod.UpdateBuildPropertiesAsync, false)]
        [InlineData(ApiMethod.GetBuildTriggerInfoAsync, true)]
        public async Task TestRetryForMockHttpApiMethods(ApiMethod apiMethod, bool mockAPIException)
        {
            var faultyBuildId = 000;
            var exceptionThrown = false;
            RetryHandlerHelper(mockAPIException: mockAPIException, out AdoBuildRunnerRetryHandler adoBuildRunnerRetryHandler, out MockLogger mockLogger, out MockAdoService mockAdoService);

            IAdoService service = mockAdoService;
            Task taskToBeAwaited = apiMethod switch
            {
                ApiMethod.GetBuildAsync => adoBuildRunnerRetryHandler.ExecuteAsync(() => service.GetBuildAsync(faultyBuildId), nameof(service.GetBuildAsync), mockLogger),
                ApiMethod.GetBuildPropertiesAsync => adoBuildRunnerRetryHandler.ExecuteAsync(() => service.GetBuildPropertiesAsync(faultyBuildId), nameof(service.GetBuildPropertiesAsync), mockLogger),
                ApiMethod.UpdateBuildPropertiesAsync => adoBuildRunnerRetryHandler.ExecuteAsync(() => service.UpdateBuildPropertiesAsync(new Microsoft.VisualStudio.Services.WebApi.PropertiesCollection(), faultyBuildId), nameof(service.UpdateBuildPropertiesAsync), mockLogger),
                ApiMethod.GetBuildTriggerInfoAsync => adoBuildRunnerRetryHandler.ExecuteAsync(() => service.GetBuildTriggerInfoAsync(), nameof(service.GetBuildTriggerInfoAsync), mockLogger),
                _ => throw new ArgumentException("Invalid API method")
            };

            try
            {
                await taskToBeAwaited;
            }
            catch (Exception ex)
            {
                // Expecting the RetryHandler to exhaust the maximum retry attempts and to throw an exception if the last attempt also fails.
                exceptionThrown = true;
                Assert.Equal(MaxRetryAttempts - 1, mockLogger.MessageCount("Retrying"));
                Assert.Contains($"Failed to execute {apiMethod} after {MaxRetryAttempts} attempts", ex.ToString());
            }

            Assert.True(exceptionThrown, "Expected exception was not thrown.");
        }

        /// <summary>
        /// Tests the AdoBuildRunnerRetryHandler's ability to ensure that the failing method is retried the expected number of times.
        /// This unit test also verifies that the RetryHandler throws the correct exception once the maximum number of retry attempts is reached.
        /// </summary>
        [Fact]
        public async Task TestRetryHandlerFailureAfterMaxAttempts()
        {
            int retryAttemptCount = 0;
            var retryHandler = new AdoBuildRunnerRetryHandler(MaxRetryAttempts);
            var mockLogger = new MockLogger();

            async Task<bool> failingTask()
            {
                retryAttemptCount++;
                await Task.Delay(10);
                throw new InvalidOperationException("Throw an exception for unit test");
            }

            var apiMethodName = nameof(failingTask);
            var exception = await Assert.ThrowsAsync<Exception>(() => retryHandler.ExecuteAsync(failingTask, apiMethodName, mockLogger));

            // Verify that the method was retried the expected number of times.
            Assert.Equal(MaxRetryAttempts, retryAttemptCount);

            // Verify that the AdoBuildRunnerRetryHandler generates the expected exception message and also logs the info message expected number of times. 
            Assert.Equal(MaxRetryAttempts - 1, mockLogger.MessageCount("Retrying"));
            Assert.Contains($"Failed to execute {apiMethodName} after {MaxRetryAttempts} attempts", exception.ToString());
        }

        /// <summary>
        /// Initializes mock API services and the AdoBuildRunnerRetryHandler for testing. 
        /// </summary>
        internal void RetryHandlerHelper(bool mockAPIException, out AdoBuildRunnerRetryHandler adoBuildRunnerRetryHandler, out MockLogger mockLogger, out MockAdoService mockAdoAPIService)
        {
            mockAdoAPIService = new MockAdoService(mockAPIException);
            foreach (var buildID in m_expectedBuildIds)
            {
                mockAdoAPIService.AddBuild(buildID, new Build());
                mockAdoAPIService.AddBuildProperties(buildID, new Microsoft.VisualStudio.Services.WebApi.PropertiesCollection());
                mockAdoAPIService.AddBuildTriggerProperties("DummyTriggerId", "1256");
            }

            adoBuildRunnerRetryHandler = new AdoBuildRunnerRetryHandler(MaxRetryAttempts);
            mockLogger = new MockLogger();
        }
    }
}