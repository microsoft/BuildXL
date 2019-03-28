// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
