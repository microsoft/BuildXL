// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// The verbosity level for logging
    /// </summary>
    public enum OutputReportingMode : byte
    {
        /// <summary>
        /// Include truncated output when the tool exits with an error
        /// </summary>
        TruncatedOutputOnError,

        /// <summary>
        /// Always include full tool output
        /// </summary>
        FullOutputAlways,

        /// <summary>
        /// Include full tool output when the tool exits with an error
        /// </summary>
        FullOutputOnError,

        /// <summary>
        /// Include full tool output when the tool exits with a warning or error
        /// </summary>
        FullOutputOnWarningOrError,
    }
}
