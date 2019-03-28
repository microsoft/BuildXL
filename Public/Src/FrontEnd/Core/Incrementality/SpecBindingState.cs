// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using JetBrains.Annotations;
using BuildXL.FrontEnd.Sdk;
using TypeScript.Net.Incrementality;

namespace BuildXL.FrontEnd.Core.Incrementality
{
    /// <inheritdoc/>
    public sealed class SpecBindingState : ISpecBindingState, ISourceFileBindingState
    {
        private readonly AbsolutePath m_fullPath;

        /// <summary>
        /// Full path to the spec.
        /// </summary>
        public AbsolutePath GetAbsolutePath(PathTable pathTable)
        {
            return m_fullPath;
        }

        /// <summary>
        /// Set of files that the current file depends on.
        /// </summary>
        public RoaringBitSet FileDependencies { get; }

        /// <summary>
        /// Set files that depends on the current file.
        /// </summary>
        public RoaringBitSet FileDependents { get; }

        /// <summary>
        /// Fingerprint that conservatively contains all identifiers being used by this file or produced by it.
        /// </summary>
        public string ReferencedSymbolsFingerprint { get; }

        /// <summary>
        /// Fingerprint that contains only declarations from this file.
        /// </summary>
        public string DeclaredSymbolsBindingFingerprint { get; }

        /// <inheritdoc />
        public ISpecBindingSymbols BindingSymbols { get; }

        /// <inheritdoc />
        [JetBrains.Annotations.Pure]
        public ISpecBindingState WithBindingFingerprint(string referencedSymbolsFingerprint, string declaredSymbolsFingerprint)
        {
            return new SpecBindingState(m_fullPath, referencedSymbolsFingerprint, declaredSymbolsFingerprint, FileDependencies, FileDependents);
        }

        /// <nodoc />
        public SpecBindingState(
            AbsolutePath fullPath,
            [NotNull] string referencedSymbolsFingerprint,
            [NotNull] string declaredSymbolsFingerprint,
            [NotNull] RoaringBitSet fileDependencies,
            [NotNull] RoaringBitSet fileDependents)
        {
            Contract.Requires(fullPath.IsValid, "fullPath.IsValid");
            Contract.Requires(referencedSymbolsFingerprint != null, "referencedSymbolsFingerprint != null");
            Contract.Requires(declaredSymbolsFingerprint != null, "declaredSymbolsFingerprint != null");
            Contract.Requires(fileDependencies != null, "fileDependencies != null");
            Contract.Requires(fileDependencies != null, "fileDependencies != null");

            m_fullPath = fullPath;
            BindingSymbols = new SpecBindingSymbols(declaredSymbolsFingerprint, referencedSymbolsFingerprint);
            FileDependencies = fileDependencies;
            FileDependents = fileDependents;
        }
    }
}
