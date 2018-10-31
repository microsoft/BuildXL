// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Utilities.Instrumentation.Common
{
    /// <summary>
    /// Severity of the logging event
    /// </summary>
    public enum LogEventLevel
    {
        /// <nodoc/>
        None = 0,
        /// <nodoc/>
        Error = 1,
        /// <nodoc/>
        Warning = 2,
        /// <nodoc/>
        Informational = 3,
        /// <nodoc/>
        Verbose = 4
    }
}
