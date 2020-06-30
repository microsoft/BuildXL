// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Indicate cache miss analysis enabled for online or offline
    /// This will affect if we preserve fingerprintstores in log dir
    /// </summary>
    [Flags]
    public enum FingerprintStoreAnalysisMode
    {
        /// <summary>
        ///     Only enable run time cache miss analysis.
        /// </summary>
        Online = 1,

        /// <summary>
        ///     No run time cache miss analysis, but make fingerprint store available for post mortem analysis.
        /// </summary>
        Offline = 2, 

        /// <summary>
        ///     Both run time and post mortem cache miss analysis.
        /// </summary>
        OnlineAndOffline = Online | Offline,
    }
}
