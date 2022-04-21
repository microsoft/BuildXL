// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.Utilities.Instrumentation.Common
{
    /// <nodoc/>
    public sealed class LimitingResourcePercentages
    {
        /// <nodoc/>
        public int GraphShape;

        /// <nodoc/>
        public int CPU;

        /// <nodoc/>
        public int Disk;

        /// <nodoc/>
        public int Memory;

        /// <nodoc/>
        public int ConcurrencyLimit;

        /// <nodoc/>
        public int ProjectedMemory;

        /// <nodoc/>
        public int Semaphore;

        /// <nodoc/>
        public int UnavailableSlots;

        /// <nodoc/>
        public int Other;

        /// <nodoc/>
        public void AddToStats(Dictionary<string, long> stats)
        {
            string prefix = $"ExecuteInfo.{nameof(LimitingResourcePercentages)}_";
            
            stats.Add($"{prefix}{nameof(GraphShape)}", GraphShape);
            stats.Add($"{prefix}{nameof(CPU)}", CPU);
            stats.Add($"{prefix}{nameof(Disk)}", Disk);
            stats.Add($"{prefix}{nameof(Memory)}", Memory);
            stats.Add($"{prefix}{nameof(ConcurrencyLimit)}", ConcurrencyLimit);
            stats.Add($"{prefix}{nameof(ProjectedMemory)}", ProjectedMemory);
            stats.Add($"{prefix}{nameof(Semaphore)}", Semaphore);
            stats.Add($"{prefix}{nameof(UnavailableSlots)}", UnavailableSlots);
            stats.Add($"{prefix}{nameof(Other)}", Other);
        }
    }
}