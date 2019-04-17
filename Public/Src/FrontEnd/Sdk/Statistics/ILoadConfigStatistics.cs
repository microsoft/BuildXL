// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
