// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
