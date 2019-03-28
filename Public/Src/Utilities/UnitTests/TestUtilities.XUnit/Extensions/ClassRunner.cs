// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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

        private readonly bool m_skipTestClass = false;

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
            m_skipTestClass = !(testClassAttributes
                .Cast<TestClassIfSupportedAttribute>()
                .All(attribute => string.IsNullOrEmpty(attribute.Skip)));
        }

        protected override async Task<RunSummary> RunTestMethodsAsync()
        {
            if (m_skipTestClass)
            {
                // Fake the test run summary
                var summary = new RunSummary();
                summary.Total = summary.Skipped = TestCases.Count();
                return summary;
            }
            else
            {
                return await base.RunTestMethodsAsync();
            }
        }

        /// <inheritdoc />
        protected override Task<RunSummary> RunTestMethodAsync(
            ITestMethod testMethod,
            IReflectionMethodInfo method,
            IEnumerable<IXunitTestCase> testCases,
            object[] constructorArguments)
            => new BuildXLTestMethodRunner(
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
