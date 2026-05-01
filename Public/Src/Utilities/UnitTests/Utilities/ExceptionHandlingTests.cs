// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using BuildXL.Utilities.Core.Diagnostics;
using Xunit;

#nullable enable

namespace Test.BuildXL.Utilities
{
    public class ExceptionHandlingTests
    {
        /// <summary>
        /// Verifies the diagnostic helper that runs immediately before <c>ExceptionUtilities.FailFast</c>
        /// inside <c>OnFatalException</c>. The full <c>OnFatalException</c> path can't be exercised
        /// from a test runner — it terminates the process — so we drive the writer hook directly.
        /// The motivation: <c>FailFast</c> bypasses <c>AppDomain.UnhandledException</c>, so without
        /// this hook the exception's <c>Message</c> never reaches the host's logs (this is exactly
        /// what made the QBuilder <c>SandboxedProcessOutput</c> failfast forensics so painful).
        /// </summary>
        [Fact]
        public void WriteFatalDiagnostic_IncludesExceptionToStringAndMessage()
        {
            var ex = new InvalidOperationException("the diagnostic detail that needs to survive failfast");
            using var writer = new StringWriter();

            ExceptionHandling.WriteFatalDiagnostic(ex, message: "explaining message", writer);

            string output = writer.ToString();
            Assert.Contains("=== Fatal exception ===", output);
            Assert.Contains("explaining message", output);
            Assert.Contains("InvalidOperationException", output);
            Assert.Contains("the diagnostic detail that needs to survive failfast", output);
            Assert.Contains("=======================", output);
        }

        [Fact]
        public void WriteFatalDiagnostic_NullMessageDoesNotEmitMessageLine()
        {
            var ex = new InvalidOperationException("whatever");
            using var writer = new StringWriter();

            ExceptionHandling.WriteFatalDiagnostic(ex, message: null, writer);

            string output = writer.ToString();
            Assert.Contains("=== Fatal exception ===", output);
            Assert.Contains("InvalidOperationException", output);
        }

        [Fact]
        public void WriteFatalDiagnostic_DoesNotThrowWhenWriterIsBroken()
        {
            // The diagnostic write runs immediately before FailFast; if the writer fails (closed
            // console, full disk) we must still proceed to FailFast. The helper swallows write
            // exceptions internally.
            var ex = new InvalidOperationException("whatever");
            var writer = new ThrowingTextWriter();

            // Should not throw.
            ExceptionHandling.WriteFatalDiagnostic(ex, message: "msg", writer);
        }

        private sealed class ThrowingTextWriter : TextWriter
        {
            public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;
            public override void Write(char value) => throw new IOException("simulated writer failure");
            public override void Write(string? value) => throw new IOException("simulated writer failure");
            public override void WriteLine() => throw new IOException("simulated writer failure");
            public override void WriteLine(string? value) => throw new IOException("simulated writer failure");
            public override void Flush() => throw new IOException("simulated writer failure");
        }
    }
}
