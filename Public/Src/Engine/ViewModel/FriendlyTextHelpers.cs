﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BuildXL.ViewModel
{
    /// <summary>
    /// Helper to print view model objects in a user friendly way.
    /// </summary>
    public static class FriendlyTextHelpers
    {
        /// <summary>
        /// Prints a timespan duration in human friendly form
        /// </summary>
        public static string MakeFriendly(this TimeSpan duration)
        {
            if (duration.TotalHours >= 3)
            {
                // over 3 hours, no need for seconds
                return $"{duration.Hours + duration.Days * 24}h {duration.Minutes}m";
            }

            if (duration.TotalHours >= 1)
            {
                return $"{duration.Hours + duration.Days * 24}h {duration.Minutes}m {duration.Seconds}s";
            }

            if (duration.TotalMinutes >= 5)
            {
                // over 5 minutes, no need for milliseconds
                return $"{duration.Minutes}m {duration.Seconds}s";
            }

            if (duration.TotalMinutes >= 1)
            {
                return $"{duration.Minutes}m {duration.Seconds}s";
            }

            if (duration.TotalSeconds >= 1)
            {
                return $"{duration.Seconds}s";
            }

            return $"{duration.Milliseconds}ms";
        }
    }
}
