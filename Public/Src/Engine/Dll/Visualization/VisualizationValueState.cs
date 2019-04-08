// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.Engine.Visualization
{
    /// <summary>
    /// The state of a visualization value
    /// </summary>
    [Serializable]
    public enum VisualizationValueState : byte
    {
        /// <summary>
        /// Indicates the value is not available in this build.
        /// </summary>
        Disabled,

        /// <summary>
        /// Not yet computed, but might be available later
        /// </summary>
        NotYetAvailable,

        /// <summary>
        /// The value is availalbe for use.
        /// </summary>
        Available,
    }
}
