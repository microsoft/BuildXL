// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using JetBrains.Annotations;
using TypeScript.Net.Types;
using NotNullAttribute = JetBrains.Annotations.NotNullAttribute;

namespace TypeScript.Net.Incrementality
{
    /// <summary>
    /// Interaction fingerprint contains all declarations and identifiers declared and used by the spec.
    /// </summary>
    /// <remarks>
    /// If a spec has the same set of public/internal/private members as well as the same set of locals/arguments/identifiers for all functions, the interaction between this file
    /// and any other files within the module hasn't changed.
    /// If all the source files in a build have the same interaction fingerprints then the entire spec-2-spec map can be reused.
    /// </remarks>
    public sealed class SpecBindingSymbols : ISpecBindingSymbols
    {
        /// <summary>
        /// Declarations and references for a given file.
        /// </summary>
        [CanBeNull]
        public IReadOnlySet<InteractionSymbol> Symbols { get; }

        /// <summary>
        /// Set of symbols declared in the file.
        /// </summary>
        [CanBeNull]
        public IReadOnlySet<InteractionSymbol> DeclaredSymbols { get; }

        /// <summary>
        /// Set of symbols referenced by the file.
        /// </summary>
        [CanBeNull]
        public IReadOnlySet<InteractionSymbol> ReferencedSymbols { get; }

        /// <inheritdoc />
        public string DeclaredSymbolsFingerprint { get; }

        /// <inheritdoc />
        public string ReferencedSymbolsFingerprint { get; }

        /// <nodoc />
        public SpecBindingSymbols([CanBeNull]IReadOnlySet<InteractionSymbol> declaredSymbols, [CanBeNull]IReadOnlySet<InteractionSymbol> referencedSymbols, [NotNull]string declaredSymbolsFingerpint, [NotNull]string referencedSymbolsFingerprint)
            : this(declaredSymbolsFingerpint, referencedSymbolsFingerprint)
        {
            if (declaredSymbols != null && referencedSymbols != null)
            {
                Symbols = new HashSet<InteractionSymbol>(declaredSymbols.Concat(referencedSymbols)).ToReadOnlySet();
            }

            DeclaredSymbols = declaredSymbols;
            ReferencedSymbols = referencedSymbols;
        }

        /// <nodoc />
        public SpecBindingSymbols([NotNull]string declaredSymbolsFingerpint, [NotNull]string referencedSymbolsFingerprint)
        {
            DeclaredSymbolsFingerprint = declaredSymbolsFingerpint;
            ReferencedSymbolsFingerprint = referencedSymbolsFingerprint;
        }

        /// <nodoc />
        public static SpecBindingSymbols Create([NotNull] ISourceFile sourceFile, bool keepSymbols = false)
        {
            Contract.Requires(sourceFile != null, "sourceFile should not be null");
            Contract.Requires(sourceFile.State == SourceFileState.Bound, "sourceFile should be bound in order to compute fingerprint");

            using (var referencedSymbols = Pools.MemoryStreamPool.GetInstance())
            using (var declaredSymbols = Pools.MemoryStreamPool.GetInstance())
            {
                var referencedSymbolsStream = referencedSymbols.Instance;
                var declaredSymbolsStream = declaredSymbols.Instance;

                var analyzer = new SpecBindingAnalyzer(
                    sourceFile,
                    BuildXLWriter.Create(referencedSymbolsStream),
                    BuildXLWriter.Create(declaredSymbolsStream),
                    keepSymbols);
                analyzer.ComputeFingerprint();

                string referencedSymbolsFingerprint = ComputeFingerprint(referencedSymbolsStream);
                string declaredSymbolsFingerprint = ComputeFingerprint(declaredSymbolsStream);
                return new SpecBindingSymbols(analyzer.DeclaredSymbols, analyzer.ReferencedSymbols, declaredSymbolsFingerprint, referencedSymbolsFingerprint);
            }
        }

        private static string ComputeFingerprint(MemoryStream stream)
            => ComputeFingerprint(stream.GetBuffer(), (int)stream.Position);

        [SuppressMessage("Microsoft.Cryptography", "CA5350:DoNotUseWeakCryptographicAlgorithms", Justification = "Encryption is not required to be secure here.")]
        private static string ComputeFingerprint(byte[] data, int size)
        {
            using (SHA1CryptoServiceProvider sha1 = new SHA1CryptoServiceProvider())
            {
                return Convert.ToBase64String(sha1.ComputeHash(data, 0, size));
            }
        }

        /// <summary>
        /// Serializes the binding state to the given writer.
        /// </summary>
        public static void SerializeAllBindingSymbols([NotNull] SpecBindingSymbols bindingSymbols, [NotNull] BuildXLWriter writer)
        {
            SerializeSymbols(bindingSymbols.Symbols, writer);
        }

        /// <summary>
        /// Serializes the binding state to the given writer.
        /// </summary>
        public static void SerializeDeclarationSymbols([NotNull] SpecBindingSymbols bindingSymbols, [NotNull] BuildXLWriter writer)
        {
            SerializeSymbols(bindingSymbols.DeclaredSymbols, writer);
        }

        private static void SerializeSymbols(IReadOnlySet<InteractionSymbol> bindingSymbols, BuildXLWriter writer)
        {
            writer.WriteCompact(bindingSymbols.Count);

            foreach (var symbol in bindingSymbols)
            {
                writer.WriteCompact((int)symbol.Kind);
                writer.Write(symbol.FullName);
            }
        }
    }
}
