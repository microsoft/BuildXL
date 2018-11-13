// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Utilities.Qualifier
{
    /// <summary>
    /// Represents an event forwarded from a worker
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815")]
    public struct UnsupportedQualifierValue
    {
        /// <summary>
        /// The location of the error
        /// </summary>
        public Location Location;

        /// <summary>
        /// The key of the qualifier
        /// </summary>
        public string QualifierKey { get; set; }

        /// <summary>
        /// The invalid value passed.
        /// </summary>
        public string InvalidValue { get; set; }

        /// <summary>
        /// The set of legal values
        /// </summary>
        public string LegalValues { get; set; }
    }
}
