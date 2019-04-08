// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Utilities;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Test.BuildXL.TestUtilities.XUnit.Extensions
{
    /// <summary>
    /// Custom Xunit assembly runner that runs all the tests for a given assembly.
    /// </summary>
    internal sealed class AssemblyRunner : XunitTestAssemblyRunner
    {
        private readonly Logger m_logger;

        /// <nodoc />
        public AssemblyRunner(
            Logger logger,
            ITestAssembly testAssembly,
            IEnumerable<IXunitTestCase> testCases,
            IMessageSink diagnosticMessageSink,
            IMessageSink executionMessageSink,
            ITestFrameworkExecutionOptions executionOptions)
            : base(testAssembly, testCases, diagnosticMessageSink, executionMessageSink, executionOptions)
        {
            m_logger = logger;
            global::BuildXL.Storage.ContentHashingUtilities.SetDefaultHashType();
        }

        /// <inheritdoc />
        protected override Task<RunSummary> RunTestCollectionAsync(IMessageBus messageBus,
            ITestCollection testCollection, IEnumerable<IXunitTestCase> testCases,
            CancellationTokenSource cancellationTokenSource)
        {
            testCases = FilterTestCases(testCases);

            // All the logic resides in DelegatingMessageBus.
            return new CollectionRunner(
                    m_logger,
                    testCollection,
                    testCases,
                    DiagnosticMessageSink,
                    new DelegatingMessageBus(m_logger, messageBus),
                    TestCaseOrderer,
                    new ExceptionAggregator(Aggregator),
                    cancellationTokenSource)
                .RunAsync();
        }

        private IEnumerable<IXunitTestCase> FilterTestCases(IEnumerable<IXunitTestCase> testCases)
        {
            int? testFilterHashIndex = ParseEnvironmentInt32("[UnitTest]TestFilterHashIndex");
            int? testFilterHashCount = ParseEnvironmentInt32("[UnitTest]TestFilterHashCount");

            if (testFilterHashIndex == null || testFilterHashCount == null)
            {
                return testCases;
            }

            var result = testCases.Where(tc => (Math.Abs(HashCodeHelper.GetOrdinalIgnoreCaseHashCode(tc.Method.Name)) % testFilterHashCount.Value) == testFilterHashIndex.Value).ToList();
            return result;
        }

        private static int? ParseEnvironmentInt32(string key)
        {
            var value = Environment.GetEnvironmentVariable(key);
            if (value == null)
            {
                return null;
            }

            return int.Parse(value);
        }
    }
}
