// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
