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
    /// Custom implementation of Xunit collection runner.
    /// </summary>
    internal sealed class CollectionRunner : XunitTestCollectionRunner
    {
        private readonly Logger m_logger;
        private readonly IMessageSink m_diagnosticMessageSink;

        /// <nodoc />
        public CollectionRunner(
            Logger logger,
            ITestCollection testCollection,
            IEnumerable<IXunitTestCase> testCases,
            IMessageSink diagnosticMessageSink,
            IMessageBus messageBus,
            ITestCaseOrderer testCaseOrderer,
            ExceptionAggregator aggregator,
            CancellationTokenSource cancellationTokenSource)
            : base(testCollection, testCases, diagnosticMessageSink, messageBus, testCaseOrderer, aggregator,
                cancellationTokenSource)
        {
            m_logger = logger;
            m_diagnosticMessageSink = diagnosticMessageSink;
        }

        /// <inheritdoc />
        protected override Task<RunSummary> RunTestClassAsync(ITestClass testClass, IReflectionTypeInfo typeInfo,
            IEnumerable<IXunitTestCase> testCases)
        {
            return new ClassRunner(
                m_logger,
                testClass,
                typeInfo,
                testCases,
                m_diagnosticMessageSink,
                MessageBus,
                TestCaseOrderer,
                new ExceptionAggregator(Aggregator),
                CancellationTokenSource,
                CollectionFixtureMappings).RunAsync();
        }
    }
}
