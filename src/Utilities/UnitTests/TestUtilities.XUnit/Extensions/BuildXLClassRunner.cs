// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Test.BuildXL.TestUtilities.XUnit.Extensions
{
    /// <summary>
    /// Custom Xunit test class runner.
    /// </summary>
    internal sealed class BuildXLClassRunner : XunitTestClassRunner
    {
        private readonly Logger m_logger;

        /// <nodoc />
        public BuildXLClassRunner(
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
