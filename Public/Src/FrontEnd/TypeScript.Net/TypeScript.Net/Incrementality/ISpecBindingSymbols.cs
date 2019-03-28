// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace TypeScript.Net.Incrementality
{
    /// <summary>
    /// Interaction fingerprint contains all declarations and identifiers declared and used by the spec.
    /// </summary>
    public interface ISpecBindingSymbols
    {
        /// <summary>
        /// SHA1 fingerprint for the symbols referenced by the source file.
        /// </summary>
        string ReferencedSymbolsFingerprint { get; }

        /// <summary>
        ///  SHA1 fingerprint for the symbols declared in the source file.
        /// </summary>
        string DeclaredSymbolsFingerprint { get; }
    }
}
