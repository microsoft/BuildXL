// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.IO;

#nullable enable

namespace BuildXL.Utilities.Core.Diagnostics
{
    /// <summary>
    /// Provides helper functions and extension methods for common exception handling.
    /// </summary>
    public static class ExceptionHandling
    {
        /// <summary>
        /// Notifies the environment about a fatal exception and terminates the process.
        /// </summary>
        /// <param name="exception">An exception that represents the error that caused the termination.
        /// This is typically the exception in a catch block.</param>
        /// <param name="message">A message that explains why the process was terminated,
        /// or null if no explanation is provided.</param>
        public static void OnFatalException(Exception exception, string? message = null)
        {
            Contract.RequiresNotNull(exception);

            if (Debugger.IsAttached)
            {
                Debugger.Break();
            }

            // Surface the exception text to stderr before failfasting. FailFast bypasses
            // AppDomain.UnhandledException, so without this hook the exception's Message
            // (which is often perfectly diagnostic — see the SandboxedProcessOutput ctor
            // invariant breach that motivated this hook) never reaches the host's logs.
            // Best-effort: failures here must not prevent FailFast from running.
            WriteFatalDiagnostic(exception, message, Console.Error);

            // Note that we don't use Environment.FailFast. It isn't trustworthy; instead we go straight to the kernel.
            ExceptionUtilities.FailFast(message, exception);
        }

        /// <summary>
        /// Writes a structured fatal-exception diagnostic block to the supplied <paramref name="writer"/>.
        /// Internal so that tests can drive this directly: <see cref="OnFatalException"/> itself
        /// terminates the process via <see cref="ExceptionUtilities.FailFast"/> and cannot be exercised
        /// by a test runner.
        /// </summary>
        /// <remarks>
        /// Wrapped in a defensive try/catch: this runs immediately before <see cref="ExceptionUtilities.FailFast"/>
        /// and we don't want an I/O failure (closed console, full disk, etc.) to mask the failfast.
        /// </remarks>
        internal static void WriteFatalDiagnostic(Exception exception, string? message, TextWriter writer)
        {
            try
            {
                writer.WriteLine();
                writer.WriteLine("=== Fatal exception ===");
                if (!string.IsNullOrEmpty(message))
                {
                    writer.WriteLine(message);
                }
                writer.WriteLine(exception.ToString());
                writer.WriteLine("=======================");
                writer.Flush();
            }
            catch
#pragma warning disable ERP022 // Unobserved exception in a generic exception handler
            {
                // Best-effort. FailFast must run regardless.
            }
#pragma warning restore ERP022
        }
    }
}
