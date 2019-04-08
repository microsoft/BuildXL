// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// The verbosity level for logging
    /// </summary>
    public enum VerbosityLevel : byte
    {
        /// <summary>
        /// Nothing will be logged
        /// </summary>
        Off = 0,

        /// <summary>
        /// Error messages will be logged
        /// </summary>
        Error,

        /// <summary>
        /// Warning messages will be logged. Including Error.
        /// </summary>
        Warning,

        /// <summary>
        /// Informational messages will be logged. Including Error and Warning.
        /// </summary>
        Informational,

        /// <summary>
        /// Verbose messages will be logged. Including Error, Warning and Informational
        /// </summary>
        Verbose,
    }
}
