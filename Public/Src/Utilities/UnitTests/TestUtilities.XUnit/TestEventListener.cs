// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Utilities.Tracing;

namespace Test.BuildXL.TestUtilities.Xunit
{
    /// <inheritdoc/>
    public sealed class TestEventListener : TestEventListenerBase
    {
        /// <nodoc/>
        public TestEventListener(Events eventSource, string fullyQualifiedTestName, bool captureAllDiagnosticMessages = true, Action<string> logAction = null)
            : base(eventSource, fullyQualifiedTestName, captureAllDiagnosticMessages, logAction) { }

        /// <inheritdoc/>
        protected override void AssertTrue(bool condition, string format, params string[] args)
        {
            XAssert.IsTrue(condition, format, args);
        }
    }
}
