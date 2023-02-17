// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Utilities.Core.Qualifier
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
