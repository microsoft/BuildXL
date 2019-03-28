// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using BuildXL.Engine.Visualization;

namespace BuildXL.Visualization.Models
{
    /// <summary>
    /// Model for Pips
    /// </summary>
    public static class EngineModel
    {
        /// <summary>
        /// The visualization information
        /// </summary>
        [SuppressMessage("Microsoft.Usage", "CA2211")]
        public static IVisualizationInformation VisualizationInformation;
    }
}
