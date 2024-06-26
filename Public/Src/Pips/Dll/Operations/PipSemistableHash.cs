// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Core;
using static BuildXL.Utilities.Core.FormattableStringEx;

namespace BuildXL.Pips
{
    /// <summary>
    /// Pip semistable hash
    /// </summary>
    public readonly record struct PipSemitableHash(long Value)
    {
        /// <nodoc />
        public static implicit operator long(PipSemitableHash hash) => hash.Value;

        /// <nodoc />
        public static implicit operator PipSemitableHash(long value) => new(value);

        /// <nodoc />
        public static implicit operator PipSemitableHash(Pip pip) => new(pip.SemiStableHash);

        /// <summary>
        /// Format the semistable hash for display 
        /// </summary>
        /// <remarks>
        /// Keep in sync with <see cref="Pip.s_formattedSemiStableHashRegex"/> and <see cref="TryParseSemiStableHash"/>
        /// CODESYNC: Make sure to update 'GetStdInFilePath' in 'SandboxedProcessUnix.cs' when this logic changes!!!
        /// </remarks>
        public static string Format(long hash, bool includePrefix = true)
        {
            var prefix = includePrefix ? Pip.SemiStableHashPrefix : string.Empty;
            return I($"{prefix}{hash:X16}");
        }

        /// <summary>
        /// Gets the hex representation without <see cref="Pip.SemiStableHashPrefix"/>
        /// </summary>
        public string ToHex()
        {
            return Format(Value, includePrefix: false);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Format(Value, includePrefix: true);
        }

        /// <summary>
        /// Attempts to parse the given pip semistable hash
        /// </summary>
        public static bool TryParse(string formattedSemiStableHash, out PipSemitableHash hash)
        {
            var result = TryParseSemiStableHash(formattedSemiStableHash, out var hashValue);
            hash = new(hashValue);
            return result;
        }

        /// <summary>
        /// Inverse of <see cref="Format"/>
        /// </summary>
        public static bool TryParseSemiStableHash(string formattedSemiStableHash, out long hash)
        {
            if (!formattedSemiStableHash.StartsWith(Pip.SemiStableHashPrefix))
            {
                hash = -1;
                return false;
            }

            try
            {
                hash = Convert.ToInt64(formattedSemiStableHash.Substring(Pip.SemiStableHashPrefix.Length), 16);
                return true;
            }
            catch
            {
                hash = -1;
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
                return false;
#pragma warning restore ERP022 // Unobserved exception in generic exception handler
            }
        }
    }
}
