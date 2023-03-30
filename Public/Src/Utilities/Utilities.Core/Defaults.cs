// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities.Core
{
    /// <summary>
    /// Default values of various utilities.
    /// </summary>
    public static class Defaults
    {
        /// <summary>
        /// Indicates the default process timeout in minutes. This should be the minimum value. values overwriting should be no less than this
        /// </summary>
        public const int ProcessTimeoutInMinutes = 10;
    }
}
