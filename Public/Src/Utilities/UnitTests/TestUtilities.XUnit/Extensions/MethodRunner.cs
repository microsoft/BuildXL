// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Utilities.Tasks;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Test.BuildXL.TestUtilities.XUnit.Extensions
{
    /// <summary>
    /// Custom implementation of the Xunit test runner.
    /// </summary>
    internal sealed class BuildXLTestMethodRunner : XunitTestMethodRunner
    {
        private readonly Logger m_logger;

        /// <nodoc />
        public BuildXLTestMethodRunner(
            Logger logger,
            ITestMethod testMethod,
            IReflectionTypeInfo @class,
            IReflectionMethodInfo method,
            IEnumerable<IXunitTestCase> testCases,
            IMessageSink diagnosticMessageSink,
            IMessageBus messageBus,
            ExceptionAggregator aggregator,
            CancellationTokenSource cancellationTokenSource,
            object[] constructorArguments)
            : base(testMethod, @class, method, testCases, diagnosticMessageSink, messageBus, aggregator,
                cancellationTokenSource, constructorArguments)
        {
            m_logger = logger;
        }

        /// <inheritdoc />
        protected override async Task<RunSummary> RunTestCaseAsync(IXunitTestCase testCase)
        {
            var sw = Stopwatch.StartNew();

            var contractViolations = new Dictionary<(string condition, string message), StackTrace>();

            // Tracing test methods invocation gives an ability to see what tests were running when 
            // the console runner crashed with stack overflow or similar unrecoverable error.

            // For now, only failing fast for the cache part, because there are a lot of cases when the contracts are violated for bxl part.
            bool failFastOnContractViolation = testCase.DisplayName.Contains("ContentStore");

            // Some tests modify console streams
            // Capture them so they can be restored
            var consoleOut = Console.Out;
            var consoleError = Console.Error;

            m_logger.LogVerbose($"Starting '{testCase.DisplayName}'... (FailFastOnContractViolation={failFastOnContractViolation}) ");

            string resultString = "FAIL";
            try
            {
                try
                {
                    // Capturing contract violations that happen during the test invocation.
                    // This logic is not 100% correct, because xUnit runs tests in parallel, so
                    // we can have more than one test failure because of one contract violation.
                    // But this logic is far better compared to the old one that was killing the app after
                    // some delay.
                    if (failFastOnContractViolation)
                    {
                        Contract.ContractFailed += failFastContractViolationsCheck;
                    }

                    // Unset any synchronization restrictions set by the caller (looking at you, xunit)
                    SynchronizationContext.SetSynchronizationContext(null);

                    var result = await base.RunTestCaseAsync(testCase).WithTimeoutAsync(TimeSpan.FromMinutes(5));
                    
                    if (result.Failed == 0)
                    {
                        resultString = "PASS";
                    }

                    if (contractViolations.Count != 0)
                    {
                        throwInvalidOperationOnContractViolation();
                    }

                    return result;
                }
                finally
                {
                    // Some tests modify console streams
                    // Restore them to captured values. We do this here since we want to restore prior to
                    // catching timeout exception so it gets logged properly.
                    Console.SetOut(consoleOut);
                    Console.SetError(consoleError);
                }
            }
            catch (TimeoutException)
            {
                // Tracing the test case that fails with timeout
                m_logger.LogVerbose($"Test '{testCase.DisplayName}' (Result={resultString}) failed with timeout.");
                throw;
            }
            finally
            {
                m_logger.LogVerbose($"Finished '{testCase.DisplayName}' (Result={resultString}) in {sw.ElapsedMilliseconds:n0}ms");
                if (failFastOnContractViolation)
                {
                    Contract.ContractFailed -= failFastContractViolationsCheck;
                }
            }

            void failFastContractViolationsCheck(object sender, ContractFailedEventArgs args)
            {
                contractViolations.Add((args.Condition, args.Message), new StackTrace());
            }

            void throwInvalidOperationOnContractViolation()
            {
                string error = string.Join(", ", contractViolations.Select(kvp => $"Condition: {kvp.Key.condition}, Message: {kvp.Key.message}, StackTrace: {kvp.Value}"));
                
                // The error can be quite long, so limit the message.
                var finalError = error.Substring(0, length: 5000);
                if (finalError.Length != error.Length)
                {
                    finalError += "...";
                }

                throw new InvalidOperationException($"Contract exception(s) occurred when running {testCase.DisplayName}. {finalError}");
            }
        }
    }
}
