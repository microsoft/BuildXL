// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.FrontEnd.Workspaces;

namespace BuildXL.FrontEnd.Sdk
{
    /// <summary>
    /// Statistics interface for a configuration processing.
    /// </summary>
    public interface ILoadConfigStatistics : IConfigurationStatistics
    {
        /// <nodoc />
        Counter FileCountCounter { get; }

        /// <nodoc />
        int FileCount { get; }

        /// <nodoc />
        Counter TotalDuration { get; }

        /// <nodoc />
        int TotalDurationMs { get; }

        /// <nodoc />
        Counter ParseDuration { get; }

        /// <nodoc />
        int ParseDurationMs { get; }

        /// <nodoc />
        Counter ConversionDuration { get; }

        /// <nodoc />
        int ConversionDurationMs { get; }
    }
}
