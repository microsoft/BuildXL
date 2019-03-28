// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;

namespace BuildXL.Visualization.Models
{
    /// <summary>
    /// Value Details
    /// </summary>
    public sealed class ValueDetails : ValueReference
    {
        /// <summary>
        /// Location
        /// </summary>
        public FileDetails OriginatingPath { get; set; }

        /// <summary>
        /// Location of value in file
        /// </summary>
        public Location OriginatingPosition { get; set; }

        /// <summary>
        /// List of all values that this value depends on
        /// </summary>
        public IEnumerable<ValueReference> Dependencies { get; set; }

        /// <summary>
        /// List of all values that depend on this value
        /// </summary>
        public IEnumerable<ValueReference> Dependents { get; set; }

        /// <summary>
        /// List of all pips that got created as a side effect of evaluating this value
        /// </summary>
        public IEnumerable<PipReference> Pips { get; set; }
    }
}
