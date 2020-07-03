// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Indicates a value whose provenance is tracked
    /// </summary>
    public partial interface ITrackedValue
    {
        /// <summary>
        /// Gets the location where the mount is defined
        /// </summary>
        LocationData Location { get; }
    }
}
