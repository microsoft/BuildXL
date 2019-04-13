// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Workspaces;

#pragma warning disable SA1649 // File name must match first type name

namespace BuildXL.FrontEnd.Sdk
{
    /// <summary>
    /// Interface for capturing the statistics for nuget.
    /// </summary>
    public interface INugetStatistics
    {
        /// <summary>
        /// Duration and number of the full restore packages phase.
        /// </summary>
        Counter EndToEnd { get; }

        /// <summary>
        /// Number of failures.
        /// </summary>
        Counter Failures { get; }

        /// <summary>
        /// Number of files restored from the file system.
        /// </summary>
        Counter PackagesFromDisk { get; }

        /// <summary>
        /// Number of files restored from the cache.
        /// </summary>
        Counter PackagesFromCache { get; }

        /// <summary>
        /// Number of files restored from nuget.
        /// </summary>
        Counter PackagesFromNuget { get; }

        /// <summary>
        /// Duration and number of regenerated specs.
        /// </summary>
        Counter SpecGeneration { get; }
    }

    /// <summary>
    /// Set of extension methods for <see cref="INugetStatistics"/> interface.
    /// </summary>
    public static class NugetStatisticsExtensions
    {
        /// <summary>
        /// Returns the number of successfully restored packages.
        /// </summary>
        public static int AllSuccessfulPackages(this INugetStatistics statistics) =>
            statistics.PackagesFromDisk.Count + statistics.PackagesFromCache.Count + statistics.PackagesFromNuget.Count;
    }
}
