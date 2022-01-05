// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Defines defaults for <see cref="ILoggingConfiguration"/>
    /// </summary>
    public static class LoggingConfigurationExtensions
    {
        /// <summary>
        /// <see cref="ILoggingConfiguration.ReplayWarnings"/>
        /// </summary>
        /// <remarks>
        /// Defaults to true.
        /// </remarks>
        public static bool ReplayWarnings(this ILoggingConfiguration loggingConfiguration) => loggingConfiguration.ReplayWarnings ?? true;
    }
}
