// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Utilities.Tracing
{
    /// <summary>
    /// Describes the general configured state of a specific warning message.
    /// </summary>
    public enum WarningState
    {
        /// <summary>
        /// The specific message should be treated as a warning.
        /// </summary>
        AsWarning,

        /// <summary>
        /// The specific message should be treated as an error and lead to a failed operation.
        /// </summary>
        AsError,

        /// <summary>
        /// The specific message should be suppressed.
        /// </summary>
        Suppressed,
    }
}
