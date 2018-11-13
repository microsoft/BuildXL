// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Test.BuildXL.TestUtilities.XUnit.Extensions
{
    /// <summary>
    /// Custom Xunit assembly runner that runs all the tests for a given assembly.
    /// </summary>
    internal sealed class BuildXLAssemblyRunner : XunitTestAssemblyRunner
    {
        private readonly Logger m_logger;

        /// <nodoc />
        public BuildXLAssemblyRunner(
            Logger logger,
            ITestAssembly testAssembly,
            IEnumerable<IXunitTestCase> testCases,
            IMessageSink diagnosticMessageSink,
            IMessageSink executionMessageSink,
            ITestFrameworkExecutionOptions executionOptions)
            : base(testAssembly, testCases, diagnosticMessageSink, executionMessageSink, executionOptions)
        {
            m_logger = logger;
        }

        /// <inheritdoc />
        protected override Task<RunSummary> RunTestCollectionAsync(IMessageBus messageBus,
            ITestCollection testCollection, IEnumerable<IXunitTestCase> testCases,
            CancellationTokenSource cancellationTokenSource)
        {
            // All the logic resides in BuildXLDelegatingMessageBus.
            return new BuildXLCollectionRunner(
                    m_logger,
                    testCollection,
                    testCases,
                    DiagnosticMessageSink,
                    new BuildXLDelegatingMessageBus(m_logger, messageBus),
                    TestCaseOrderer,
                    new ExceptionAggregator(Aggregator),
                    cancellationTokenSource)
                .RunAsync();
        }
    }
}
