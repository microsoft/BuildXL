// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;

namespace BuildXL.Visualization.Models
{
    /// <summary>
    /// Model for Tools
    /// </summary>
    public sealed class ToolDetails : ToolReference
    {
        /// <summary>
        /// List of pips that execute the tool
        /// </summary>
        public IEnumerable<PipReference> ExecutingPips { get; set; }

        /// <summary>
        /// Average number of Outputs
        /// </summary>
        public double AverageOutputCount { get; set; }

        /// <summary>
        /// Average number of Inputs
        /// </summary>
        public double AverageInputCount { get; set; }

        /// <summary>
        /// Minimum time the tool was run
        /// </summary>
        public TimeSpan? MinimumRuntime { get; set; }

        /// <summary>
        /// Maximum time the tool was run
        /// </summary>
        public TimeSpan? MaximumRuntime { get; set; }

        /// <summary>
        /// Average runtime for the tool
        /// </summary>
        public TimeSpan? AverageRuntime { get; set; }
    }
}
