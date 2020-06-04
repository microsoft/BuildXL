// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Test.BuildXL.TestUtilities.XUnit.Extensions
{
    /// <summary>
    /// Custom Xunit test class runner.
    /// </summary>
    internal sealed class ClassRunner : XunitTestClassRunner
    {
        private readonly Logger m_logger;

        private readonly string m_skipTestClassSkipReason = null;

        /// <nodoc />
        public ClassRunner(
            Logger logger,
            ITestClass testClass,
            IReflectionTypeInfo typeInfo,
            IEnumerable<IXunitTestCase> testCases,
            IMessageSink diagnosticMessageSink,
            IMessageBus messageBus,
            ITestCaseOrderer testCaseOrderer,
            ExceptionAggregator aggregator,
            CancellationTokenSource cancellationTokenSource,
            IDictionary<Type, object> collectionFixtureMappings)
            : base(testClass, typeInfo, testCases, diagnosticMessageSink, messageBus, testCaseOrderer, aggregator,
                cancellationTokenSource, collectionFixtureMappings)
        {
            m_logger = logger;

            // Check if the test class has the attribute that specifies system requirements to run the tests
            var testClassAttributes = typeInfo.Type.GetCustomAttributes(typeof(TestClassIfSupportedAttribute), inherit: true);
            // If any of the attributes specify Skip, the test class is skipped
            m_skipTestClassSkipReason = testClassAttributes
                .Cast<TestClassIfSupportedAttribute>()
                .Select(attribute => attribute.Skip)
                .Where(skipReason => !string.IsNullOrEmpty(skipReason))
                .FirstOrDefault();
        }

        /// <inheritdoc />
        protected override Task<RunSummary> RunTestMethodAsync(
            ITestMethod testMethod,
            IReflectionMethodInfo method,
            IEnumerable<IXunitTestCase> testCases,
            object[] constructorArguments)
        {
            if (!string.IsNullOrEmpty(m_skipTestClassSkipReason))
            {
                var runSummary = new RunSummary();
                foreach (var testCase in testCases)
                {
                    MessageBus.QueueMessage(new TestSkipped(new XunitTest(testCase, testCase.DisplayName), m_skipTestClassSkipReason));
                    runSummary.Skipped++;
                    runSummary.Total++;
                }
                
                return Task.FromResult(runSummary);
            }

            return new BuildXLTestMethodRunner(
                           m_logger,
                           testMethod,
                           Class,
                           method,
                           testCases,
                           DiagnosticMessageSink,
                           MessageBus,
                           new ExceptionAggregator(Aggregator),
                           CancellationTokenSource,
                           constructorArguments).RunAsync();
        }
    }
}
