// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Defines defaults for <see cref="IScheduleConfiguration"/>
    /// </summary>
    public static class ScheduleConfigurationExtensions
    {
        /// <summary>
        /// <see cref="IScheduleConfiguration.UseHistoricalCpuUsageInfo"/>
        /// </summary>
        /// <remarks>
        /// Defaults to false.
        /// </remarks>
        public static bool UseHistoricalCpuUsageInfo(this IScheduleConfiguration scheduleConfiguration) => scheduleConfiguration.UseHistoricalCpuUsageInfo ?? false;

        /// <summary>
        /// <see cref="IScheduleConfiguration.MinimumTotalAvailableRamMb"/>
        /// </summary>
        /// <remarks>
        /// Defaults to 500
        /// </remarks>
        public static int MinimumTotalAvailableRamMb(this IScheduleConfiguration scheduleConfiguration) => scheduleConfiguration.MinimumTotalAvailableRamMb ?? 500;

        /// <summary>
        /// <see cref="IScheduleConfiguration.ManageMemoryMode"/>
        /// </summary>
        /// <remarks>
        /// Defaults to <see cref="ManageMemoryMode.CancellationRam"/>
        /// </remarks>
        public static ManageMemoryMode GetManageMemoryMode(this IScheduleConfiguration scheduleConfiguration) => scheduleConfiguration.ManageMemoryMode ?? ManageMemoryMode.CancellationRam;
    }
}
