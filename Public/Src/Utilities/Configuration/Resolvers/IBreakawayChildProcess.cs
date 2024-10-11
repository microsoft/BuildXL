// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using BuildXL.Utilities.Core;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// A container for a breakaway child process.
    /// </summary>
    public interface IBreakawayChildProcess
    {
        /// <summary>
        /// The breakaway child process name
        /// </summary>
        PathAtom ProcessName { get; }

        /// <summary>
        /// Optionally, the substring that the command line arguments to <see cref="ProcessName"/> must contain for it to breakaway.
        /// </summary>
        string? RequiredArguments {  get; }

        /// <summary>
        /// Whether to ignore case when checking if the command line arguments contain the required arguments
        /// </summary>
        bool RequiredArgumentsIgnoreCase { get; }
    }
}
