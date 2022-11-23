// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using System.Threading;
using System;
using System.Diagnostics;
using Xunit.Abstractions;
using Xunit.Sdk;
using System.Linq;

namespace Test.BuildXL.TestUtilities.XUnit.Extensions
{
    /// <summary>
    /// Helper class that cleans up the stack traces reported when the xunit test fails.
    /// </summary>
    /// <remarks>
    /// This process consists of two main parts:
    /// 1. "Normal" exception demystification that removes clutter and prettifies the names
    /// 2. Removes fluent assertion stack frames from the output.
    /// </remarks>
    internal static class XUnitExceptionDemystifier
    {
        private static Exception[] UnwrapAggregateExceptionIfNeeded(Exception exception)
        {
            if (exception is null)
            {
                return Array.Empty<Exception>();
            }

            return exception is AggregateException ae ? ae.InnerExceptions.ToArray() : new[] { exception };
        }

        /// <summary>
        /// Updates the exceptions in <paramref name="aggregator"/> with the demystified version of them.
        /// </summary>
        public static void Demystify(this ExceptionAggregator aggregator, Logger logger)
        {
            if (aggregator.HasExceptions)
            {
                // Its possible that we have old exceptions here!
                var exceptions = UnwrapAggregateExceptionIfNeeded(aggregator.ToException());

                // It seems that this logic messes up with the parametrized unit tests!
                if (exceptions.All(e => e is XunitException))
                {
                    aggregator.Clear();
                    foreach (var e in exceptions)
                    {
                        aggregator.Add(Demystify(e, logger));
                    }
                }
            }
        }

        private static Exception Demystify(Exception e, Logger logger)
        {
            return e.Demystify(
                rethrowException: false,
                stackFrameFilter: frame =>
                {
                    // Uncomment logger.Verbose if you want to see which stack frames were filtered out.

                    var method = frame.GetMethod();
                    var declaringTypeName = method?.DeclaringType?.FullName;
                    if (declaringTypeName is null)
                    {
                        return false;
                    }

                    if (declaringTypeName.StartsWith("FluentAssertions"))
                    {
                        // logger.LogVerbose($"Filtering: {declaringTypeName}.{method.Name}");
                        return true;
                    }

                    // For 'async' methods with this approach we need to also filter out a few stack frames
                    // from the xunit itself, like these:
                    // async Task<decimal> Xunit.Sdk.TestInvoker<TTestCase>.InvokeTestMethod
                    // at async Task Xunit.Sdk.ExecutionTimer.AggregateAsync(Func<Task> asyncAction)

                    if (declaringTypeName.StartsWith("Xunit.Sdk"))
                    {
                        // logger.LogVerbose($"Filtering: {declaringTypeName}.{method.Name}");
                        return true;
                    }

                    return false;
                });
        }
    }
    internal sealed class DemystificationTestRunner : XunitTestRunner
    {
        private readonly Logger m_logger;

        /// <nodoc />
        public DemystificationTestRunner(Logger logger, ITest test, IMessageBus messageBus, Type testClass, object[] constructorArguments,
            MethodInfo testMethod, object[] testMethodArguments, string skipReason,
            IReadOnlyList<BeforeAfterTestAttribute> beforeAfterAttributes, ExceptionAggregator aggregator,
            CancellationTokenSource cancellationTokenSource)
            : base(test, messageBus, testClass, constructorArguments, testMethod, testMethodArguments, skipReason,
                beforeAfterAttributes, aggregator, cancellationTokenSource)
        {
            m_logger = logger;
        }

        /// <inheritdoc />
        protected override Task<decimal> InvokeTestMethodAsync(ExceptionAggregator aggregator)
        {
            return new DemystificationTestInvoker(m_logger, Test, MessageBus, TestClass, ConstructorArguments, TestMethod, TestMethodArguments, BeforeAfterAttributes, aggregator, CancellationTokenSource).RunAndDemystify();
        }
    }

    internal sealed class DemystificationTestInvoker : XunitTestInvoker
    {
        private readonly Logger m_logger;

        public DemystificationTestInvoker(Logger logger, ITest test, IMessageBus messageBus, Type testClass, object[] constructorArguments,
            MethodInfo testMethod, object[] testMethodArguments,
            IReadOnlyList<BeforeAfterTestAttribute> beforeAfterAttributes, ExceptionAggregator aggregator,
            CancellationTokenSource cancellationTokenSource)
            : base(test, messageBus, testClass, constructorArguments, testMethod, testMethodArguments, beforeAfterAttributes,
                aggregator, cancellationTokenSource)
        {
            m_logger = logger;
        }

        /// <summary>
        /// Runs a test and demystifies the exceptions if the test fails.
        /// </summary>
        public async Task<decimal> RunAndDemystify()
        {
            var r = await RunAsync();
            Aggregator.Demystify(m_logger);
            return r;
        }
    }

    internal static class DemystificationTestCaseRunner
    {
        public static XunitTestCaseRunner Create(
            bool demystify,
            Logger logger,
            IXunitTestCase testCase,
            string displayName,
            string skipReason,
            object[] constructorArguments,
            IMessageSink diagnosticMessageSink,
            IMessageBus messageBus,
            ExceptionAggregator aggregator,
            CancellationTokenSource cancellationTokenSource)
        {
            // We need to create correct test runner.
            // This logic is not perfect because its possible to have a test method without any arguments marked with 'Theory' attribute.
            // In this case we'll try to run this is 'Fact' even though the original test runner won't run it at all.
            // But this is better option compared to the others like checking the attributes (like Fact, Theory) because
            // there are custom attributes like 'FactIfSupported' that this place doesn't know about.
            bool isTheory = testCase.Method.GetParameters().Any();
            logger.LogVerbose($"Running {testCase.DisplayName} as {(isTheory ? "Theory" : "Fact")}.");
            
            if (isTheory)
            {
                // This is a parametrized test.
                return demystify
                    ? (XunitTheoryTestCaseRunner)new DemystificationTheoryTestCaseRunner(
                        logger,
                        testCase,
                        displayName,
                        skipReason,
                        constructorArguments,
                        diagnosticMessageSink,
                        messageBus,
                        aggregator,
                        cancellationTokenSource)
                    : new XunitTheoryTestCaseRunner(
                        testCase,
                        displayName,
                        skipReason,
                        constructorArguments,
                        diagnosticMessageSink,
                        messageBus,
                        aggregator,
                        cancellationTokenSource);
            }

            return demystify
                ? (XunitTestCaseRunner)new DemystificationFactTestCaseRunner(
                    logger,
                    testCase,
                    displayName,
                    skipReason,
                    constructorArguments,
                    testCase.TestMethodArguments,
                    messageBus,
                    aggregator,
                    cancellationTokenSource)
                : new XunitTestCaseRunner(
                    testCase,
                    displayName,
                    skipReason,
                    constructorArguments,
                    testCase.TestMethodArguments,
                    messageBus,
                    aggregator,
                    cancellationTokenSource);
        }
    }

    internal sealed class DemystificationTheoryTestCaseRunner : XunitTheoryTestCaseRunner
    {
        private readonly Logger m_logger;

        /// <nodoc />
        public DemystificationTheoryTestCaseRunner(Logger logger, IXunitTestCase testCase, string displayName, string skipReason, object[] constructorArguments, IMessageSink diagnosticMessageSink, IMessageBus messageBus, ExceptionAggregator aggregator, CancellationTokenSource cancellationTokenSource)
            : base(testCase, displayName, skipReason, constructorArguments, diagnosticMessageSink, messageBus, aggregator, cancellationTokenSource)
        {
            m_logger = logger;
        }

        /// <inheritdoc />
        protected override XunitTestRunner CreateTestRunner(ITest test, IMessageBus messageBus, Type testClass,
            object[] constructorArguments,
            MethodInfo testMethod, object[] testMethodArguments,
            string skipReason,
            IReadOnlyList<BeforeAfterTestAttribute> beforeAfterAttributes,
            ExceptionAggregator aggregator,
            CancellationTokenSource cancellationTokenSource)
            => new DemystificationTestRunner(m_logger, test, messageBus, testClass, constructorArguments, testMethod, testMethodArguments,
                skipReason, beforeAfterAttributes, new ExceptionAggregator(aggregator), cancellationTokenSource);
    }

    internal sealed class DemystificationFactTestCaseRunner : XunitTestCaseRunner
    {
        private readonly Logger m_logger;

        /// <nodoc />
        public DemystificationFactTestCaseRunner(Logger logger, IXunitTestCase testCase, string displayName, string skipReason, object[] constructorArguments, object[] testMethodArguments, IMessageBus messageBus, ExceptionAggregator aggregator, CancellationTokenSource cancellationTokenSource)
            : base(testCase, displayName, skipReason, constructorArguments, testMethodArguments, messageBus, aggregator, cancellationTokenSource)
        {
            m_logger = logger;
        }

        /// <inheritdoc />
        protected override XunitTestRunner CreateTestRunner(ITest test, IMessageBus messageBus, Type testClass,
            object[] constructorArguments,
            MethodInfo testMethod, object[] testMethodArguments,
            string skipReason,
            IReadOnlyList<BeforeAfterTestAttribute> beforeAfterAttributes,
            ExceptionAggregator aggregator,
            CancellationTokenSource cancellationTokenSource)
            => new DemystificationTestRunner(m_logger, test, messageBus, testClass, constructorArguments, testMethod, testMethodArguments,
                skipReason, beforeAfterAttributes, new ExceptionAggregator(aggregator), cancellationTokenSource);
    }
}