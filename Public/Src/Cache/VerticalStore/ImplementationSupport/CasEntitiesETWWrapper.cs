// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Tracing;
using BuildXL.Cache.Interfaces;

#pragma warning disable SA1649 // File name must match first type name

namespace BuildXL.Cache.ImplementationSupport
{
    /// <summary>
    /// Wrapper struct to enable logging file hashes from a CasEntries struct.
    /// </summary>
    /// <remarks>
    /// Since ETW doesn't log enumerable classes or enumerator fields, something is needed
    /// to extract the actual file hashes.
    /// </remarks>
    [EventData]
    [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes", Justification = "Struct is never compared, but passed to ETW to let it unwrap.")]
    public struct CasEntriesETWWrapper
    {
        /// <nodoc/>
        [EventField]
        public CasEntries CasEntries { get; set; }

        /// <nodoc/>
        [EventField]
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays", Justification = "Copy of internal data is returned for ETW tracing only.")]
        public CasHash[] FileHashes
        {
            get
            {
                if (CasEntries != null)
                {
                    return new List<CasHash>(CasEntries).ToArray();
                }
                else
                {
                    return new CasHash[0];
                }
            }
        }
    }

    /// <summary>
    /// Class to provide extensions to convert CasEntries to CasETWWrapper
    /// </summary>
    /// <remarks>
    /// Not all classes / structs are objects we can reliably attribute for ETW logging as
    /// the possible set of them is unbounded and unpredictable, so we'll use a shim to enable late
    /// bound translation.
    /// </remarks>
    public static class CasWTWWrapperExtensions
    {
        /// <summary>
        /// Formats a CasEntries struct into an object ETW can understand.
        /// </summary>
        /// <param name="casEntries">Instance to format.</param>
        /// <returns>Object ETW can log properly</returns>
        public static CasEntriesETWWrapper ToETWFormat(in this CasEntries casEntries)
        {
            return new CasEntriesETWWrapper() { CasEntries = casEntries };
        }
    }
}
