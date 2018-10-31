// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Test.BuildXL.TestUtilities.XUnit.Extensions
{
    /// <summary>
    /// A custom message bus that prints messages into a custom logger
    /// </summary>
    internal sealed class BuildXLDelegatingMessageBus : DelegatingMessageBus
    {
        private Logger Logger { get; }

        /// <nodoc />
        public BuildXLDelegatingMessageBus(Logger logger, IMessageBus innerMessageBus, Action<IMessageSinkMessage> callback = null)
            : base(innerMessageBus, callback)
        {
            Logger = logger;
        }

        /// <inheritdoc />
        public override bool QueueMessage(IMessageSinkMessage message)
        {
            if (message is global::Xunit.Sdk.TestFailed testFailed)
            {
                HandleTestFailed(testFailed);
            }

            // Should always call the base method to allow IDE to catch the test failure.
            return base.QueueMessage(message);
        }

        private void HandleTestFailed(ITestFailed testFailed)
        {
            // This method is effectively the same as DefaultRunnerReporterWithTypesMessageHandler.HandleTestFailed
            Logger.LogError($"    {Escape(testFailed.Test.DisplayName)} [FAIL]");

            lock (Logger.LockObject)
            {
                foreach (var messageLine in global::Xunit.Sdk.ExceptionUtility.CombineMessages(testFailed)
                    .Split(new[] { Environment.NewLine }, StringSplitOptions.None))
                {
                    Logger.LogImportantMessage($"      {messageLine}");
                }

                LogStackTrace(global::Xunit.Sdk.ExceptionUtility.CombineStackTraces(testFailed));
                LogOutput(testFailed.Output);
            }
        }

        private void LogOutput(string output)
        {
            if (string.IsNullOrEmpty(output))
            {
                return;
            }

            // ITestOutputHelper terminates everything with NewLine, but we really don't need that
            // extra blank line in our output.
            if (output.EndsWith(Environment.NewLine, StringComparison.Ordinal))
            {
                output = output.Substring(0, output.Length - Environment.NewLine.Length);
            }

            Logger.LogMessage("      Output:");

            foreach (var line in output.Split(new[] {Environment.NewLine}, StringSplitOptions.None))
            {
                Logger.LogImportantMessage($"        {line}");
            }
        }

        private void LogStackTrace(string stackTrace)
        {
            if (string.IsNullOrEmpty(stackTrace))
            {
                return;
            }

            Logger.LogMessage("      Stack Trace:");

            foreach (var stackFrame in stackTrace.Split(new[] {Environment.NewLine}, StringSplitOptions.None))
            {
                #if DISABLE_FEATURE_XUNIT_PRETTYSTACKTRACE 
                Logger.LogImportantMessage($"        {stackFrame}");
                #else
                Logger.LogImportantMessage($"        {StackFrameTransformer.TransformFrame(stackFrame, null)}");
                #endif
            }
        }

        [SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic")]
        private string Escape(string text) => text;
    }
}
