// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using TypeScript.Net.Incrementality;

namespace BuildXL.FrontEnd.Sdk
{
    /// <summary>
    /// State of the source file required persisted across different BuildXL invocations.
    /// </summary>
    public interface ISpecBindingState : ISourceFileBindingState
    {
        /// <summary>
        /// Create new spec state with the given binding state.
        /// </summary>
        ISpecBindingState WithBindingFingerprint(string referencedSymbolsFingerprint, string declaredSymbolsFingerprint);
    }
}
