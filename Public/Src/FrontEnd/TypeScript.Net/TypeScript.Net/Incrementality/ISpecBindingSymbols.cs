// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
