// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Object pools for scheduler types.
    /// </summary>
    public static class SchedulerPools
    {
        /// <summary>
        /// Pool for pathset fingerprint map.
        /// </summary>
        public static readonly ObjectPool<Dictionary<ContentHash, Tuple<BoxRef<ProcessStrongFingerprintComputationData>, ObservedInputProcessingResult, ObservedPathSet>>> HashFingerprintDataMapPool =
            new ObjectPool<Dictionary<ContentHash, Tuple<BoxRef<ProcessStrongFingerprintComputationData>, ObservedInputProcessingResult, ObservedPathSet>>>(
                () => new Dictionary<ContentHash, Tuple<BoxRef<ProcessStrongFingerprintComputationData>, ObservedInputProcessingResult, ObservedPathSet>>(),
                c => c.Clear());

        /// <summary>
        /// Pool for strong fingerprint computation data list.
        /// </summary>
        public static readonly ObjectPool<List<BoxRef<ProcessStrongFingerprintComputationData>>> StrongFingerprintDataListPool =
            new ObjectPool<List<BoxRef<ProcessStrongFingerprintComputationData>>>(
                () => new List<BoxRef<ProcessStrongFingerprintComputationData>>(),
                c => c.Clear());
    }
}
