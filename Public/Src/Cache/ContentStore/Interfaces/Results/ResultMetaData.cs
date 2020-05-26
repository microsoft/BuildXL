// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BuildXL.Cache.ContentStore.Interfaces.Results
{
    /// <summary>
    /// MetaData information that can be added to results
    /// </summary>
    public class ResultMetaData
    {
        /// <summary>
        /// Time taken to get a semaphore to execute operation for result
        /// </summary>
        public TimeSpan GateWaitTime { get; }

        /// <summary>
        /// Occupied count of a semaphore used to execute operation
        /// </summary>
        public int GateOccupiedCount { get; }

        /// <nodoc />
        public ResultMetaData(
            TimeSpan gateWaitTime,
            int gateOccupiedCount)
        {
            GateWaitTime = gateWaitTime;
            GateOccupiedCount = gateOccupiedCount;
        }
    }
}
