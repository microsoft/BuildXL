// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Workspaces;
using BuildXL.FrontEnd.Core.Tracing;
using BuildXL.FrontEnd.Sdk;

namespace BuildXL.FrontEnd.Core
{
    /// <summary>
    /// Statistics object for a configuration processing.
    /// </summary>
    public class LoadConfigStatistics : ILoadConfigStatistics
    {
        /// <inheritdoc />
        public Counter FileCountCounter { get; } = new Counter();

        /// <inheritdoc />
        public int FileCount => FileCountCounter.Count;

        /// <inheritdoc />
        public Counter TotalDuration { get; } = new Counter();

        /// <inheritdoc />
        public int TotalDurationMs => (int) TotalDuration.AggregateDuration.TotalMilliseconds;

        /// <inheritdoc />
        public Counter ParseDuration { get; } = new Counter();

        /// <inheritdoc />
        public int ParseDurationMs => (int) ParseDuration.AggregateDuration.TotalMilliseconds;

        /// <inheritdoc />
        public Counter ConversionDuration { get; } = new Counter();

        /// <inheritdoc />
        public int ConversionDurationMs => (int) ConversionDuration.AggregateDuration.TotalMilliseconds;
    }

    /// <nodoc />
    public static class LoadConfigStatisticsExtensions
    {
        /// <nodoc />
        public static LoadConfigurationStatistics ToLoggingStatistics(this ILoadConfigStatistics statistics)
        {
            return new LoadConfigurationStatistics
                   {
                       ElapsedMilliseconds = statistics.TotalDurationMs,
                       ElapsedMillisecondsConvertion = statistics.ConversionDurationMs,
                       ElapsedMillisecondsParse = statistics.ParseDurationMs,
                       FileCount = statistics.FileCount,
                   };
        }
    }
}
