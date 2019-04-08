// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
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
            // Tracing test methods invocation gives an ability to see what tests were running when 
            // the console runner crashed with stack overflow or similar unrecoverable error.
            m_logger.LogVerbose($"Starting '{testCase.DisplayName}'... ");

            try
            {
                // Unset any synchronization restrictions set by the caller (looking at you, xunit)
                SynchronizationContext.SetSynchronizationContext(null);
                
                return await base.RunTestCaseAsync(testCase).WithTimeoutAsync(TimeSpan.FromMinutes(5));
            }
            catch (TimeoutException)
            {
                // Tracing the test case that fails with timeout
                m_logger.LogVerbose($"Test '{testCase.DisplayName}' failed with timeout.");
                throw;
            }
            finally
            {
                m_logger.LogVerbose($"Finished '{testCase.DisplayName}'");
            }
        }
    }
}
