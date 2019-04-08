// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Mode for initializing file change tracker.
    /// </summary>
    public enum FileChangeTrackerInitializationMode
    {
        /// <summary>
        /// Tries to resume existing file change tracker or restart when failed.
        /// </summary>
        ResumeExisting,

        /// <summary>
        /// Forces to restart file change tracker and discard existing one.
        /// </summary>
        /// <remarks>
        /// Issues with file change tracker tend to be sticky. This mode can be considered
        /// as an escape hatch.
        /// </remarks>
        ForceRestart
    }
}
