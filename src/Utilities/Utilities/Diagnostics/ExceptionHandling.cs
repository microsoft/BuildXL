// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Utilities.Diagnostics
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
        public static void OnFatalException(Exception exception, string message = null)
        {
            Contract.Requires(exception != null);

            if (Debugger.IsAttached)
            {
                Debugger.Break();
            }

            // Note that we don't use Environment.FailFast. It isn't trustworthy; instead we go straight to the kernel.
            ExceptionUtilities.FailFast(message, exception);
        }
    }
}
