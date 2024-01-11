// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using BuildXL.Interop;

namespace BuildXL.Utilities.Core
{
    /// <summary>
    /// Introspection and management specific to this running process.
    /// </summary>
    public static class CurrentProcess
    {
        /// <summary>
        /// Indicates if this process is elevated in the UAC sense (the primary token contains the local Administrators group).
        /// </summary>
        public static readonly bool IsElevated = Dispatch.IsElevated();

        /// <summary>
        /// Gets the command line for this process.
        /// </summary>
        /// <returns>The command line for this process</returns>
        public static string GetCommandLine()
        {
            return Environment.CommandLine;
        }

        /// <summary>
        /// Whether a sudo command can be issued without requiring user interaction
        /// </summary>
        /// <remarks>
        /// Only supported on a Unix-like OS
        /// </remarks>
        public static bool CanSudoNonInteractive()
        {
            if (OperatingSystemHelper.IsWindowsOS)
            {
                throw new InvalidOperationException("This function is only available in Unix-like OSs");
            }

            return BuildXL.Interop.Unix.Process.CanSudoNonInteractive();
        }
    }
}
