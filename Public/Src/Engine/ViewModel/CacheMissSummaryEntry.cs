// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.ViewModel
{
    /// <summary>
    /// Cache miss details
    /// </summary>
    public class CacheMissSummaryEntry
    {
        /// <nodoc />
        public string PipDescription { get; set; }

        /// <nodoc />
        public string Reason { get; set; }

        /// <nodoc />
        public bool FromCacheLookup { get; set; }
    }
}
