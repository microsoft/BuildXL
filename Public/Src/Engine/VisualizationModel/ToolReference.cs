// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Visualization.Models
{
    /// <summary>
    /// Model class for a Tool reference
    /// </summary>
    public class ToolReference
    {
        /// <summary>
        /// Tool Id
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Tool Path
        /// </summary>
        public string Path { get; set; }

        /// <summary>
        /// The number of pips
        /// </summary>
        public int NumberOfPips { get; set; }
    }
}
