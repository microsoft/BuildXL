// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Interop;

namespace BuildXL.Utilities
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
    }
}
