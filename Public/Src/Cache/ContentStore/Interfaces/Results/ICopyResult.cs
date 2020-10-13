// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BuildXL.Cache.ContentStore.Interfaces.Results
{
    /// <nodoc />
    public interface ICopyResult
    {
        /// <nodoc />
        double? MinimumSpeedInMbPerSec { get; set; }

        /// <nodoc />
        long? Size { get; }
    }
}
