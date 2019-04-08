// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Storage.ChangeTracking
{
    /// <summary>
    /// Specifies behavior for tracking paths which are already actively tracked.
    /// </summary>
    public enum TrackingUpdateMode
    {
        /// <summary>
        /// Existing tracking is preserved. Changes to the originally-tracked file will cause invalidations.
        /// </summary>
        Preserve,

        /// <summary>
        /// Existing tracking is superseded. If possible, changes to the originally-tracked file will be ignored (as if this
        /// new tracked file was the first tracked at that path).
        /// </summary>
        Supersede,
    }
}
