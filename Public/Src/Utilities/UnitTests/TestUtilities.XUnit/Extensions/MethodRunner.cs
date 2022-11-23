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
using Xunit;
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
        private readonly IMessageSink m_diagnosticMessageSink;
        private readonly object[] m_constructorArguments;

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
            m_diagnosticMessageSink = diagnosticMessageSink;
            m_constructorArguments = constructorArguments;
        }

        private static Task<T> RunInMtaThreadIfNeededAsync<T>(Func<Task<T>> func, bool runInMtaThread)
        {
            if (runInMtaThread && global::BuildXL.Utilities.OperatingSystemHelper.IsWindowsOS)
            {
                var tcs = new TaskCompletionSource<T>();
                var thread = new Thread(
                    () =>
                    {
                        Assert.Equal(Thread.CurrentThread.GetApartmentState(), ApartmentState.MTA);

                        try
                        {
                            var result = func().GetAwaiter().GetResult();
                            tcs.SetResult(result);
                        }
                        catch (Exception e)
                        {
                            tcs.SetException(e);
                        }
                    });
#pragma warning disable CA1416 // Suppressing the warning, because we're using 'OperatingSystemHelper' that is not known to the analyzer
                thread.SetApartmentState(ApartmentState.MTA);
#pragma warning restore CA1416
                thread.Start();
                return tcs.Task;
            }

            return func();
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

                    // You can disable the entire new set of runners if something acts weirdly.
                    bool useDemystifier = true;

                    RunSummary result;

                    if (useDemystifier)
                    {
                        var runner = DemystificationTestCaseRunner.Create(
                            demystify: true,
                            m_logger,
                            testCase,
                            testCase.DisplayName,
                            testCase.SkipReason,
                            m_constructorArguments,
                            m_diagnosticMessageSink,
                            MessageBus,
                            Aggregator,
                            CancellationTokenSource);

                        bool runInMtaThread = testCase.GetType().Name == "MtaTestCase";

                        result = await RunInMtaThreadIfNeededAsync(
                            func: () => runner.RunAsync().WithTimeoutAsync(TimeSpan.FromMinutes(5)),
                            runInMtaThread);
                    }
                    else
                    {
                        result = await base.RunTestCaseAsync(testCase).WithTimeoutAsync(TimeSpan.FromMinutes(5));
                    }

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
