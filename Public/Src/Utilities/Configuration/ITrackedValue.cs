// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
