// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
        public int Other;
    }
}