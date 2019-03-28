// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using JetBrains.Annotations;
using BuildXL.FrontEnd.Sdk;
using TypeScript.Net.Incrementality;

namespace BuildXL.FrontEnd.Core.Incrementality
{
    /// <summary>
    /// Class containing helper methods responsible for serialization and deserialization of the <see cref="WorkspaceBindingSnapshot"/> and other data structures.
    /// </summary>
    public static class FrontEndSnapshotSerializer
    {
        /// <summary>
        /// Serializes the front end snapshot to the given writer.
        /// </summary>
        public static void SerializeWorkspaceBindingSnapshot([NotNull] IWorkspaceBindingSnapshot snapshot, [NotNull] BuildXLWriter writer, PathTable pathTable)
        {
            // File format:
            // 1. # specs
            // 2. spec info for each spec in the workspace
            //   * spec path
            //   * bit vector of spec dependencies
            //   * bit vector of spec dependents
            //   * binding fingerprint
            snapshot.MaterializeDependencies();

            // 1. # specs
            writer.WriteCompact(snapshot.SourcesCount);

            // 2. spec info
            foreach (var source in snapshot.Sources)
            {
                // There is no perf issues right now, but we can consider to compute all the fingerprints in parallel.
                SerializeSpecBindingState(source, writer, pathTable);
            }
        }

        /// <summary>
        /// Deserializes the state of all specs from the given reader.
        /// </summary>
        [NotNull]
        public static SpecBindingState[] DeserializeSpecStates([NotNull] BuildXLReader reader, PathTable pathTable, int? length = null)
        {
            if (length == null)
            {
                length = reader.ReadInt32Compact();
            }

            var result = new SpecBindingState[length.Value];

            for (int i = 0; i < length; i++)
            {
                var path = AbsolutePath.Create(pathTable, reader.ReadString());
                var dependencies = DeserializeBitVector(reader);
                var dependents = DeserializeBitVector(reader);
                var referencedSymbolsFingerprint = reader.ReadString();
                var declaredSymbolsFingerprint = reader.ReadString();
                result[i] = new SpecBindingState(path, referencedSymbolsFingerprint, declaredSymbolsFingerprint, dependencies, dependents);
            }

            return result;
        }

        /// <summary>
        /// Serializes the spec binding state to the given writer.
        /// </summary>
        public static void SerializeSpecBindingState([NotNull] ISourceFileBindingState sourceFile, [NotNull] BuildXLWriter writer, PathTable pathTable)
        {
            // 1. spec path
            // 2. bit vector of spec dependencies
            // 3. bit vector of spec dependents
            // 4. binding fingerprint
            writer.Write(sourceFile.GetAbsolutePath(pathTable).ToString(pathTable));

            SerializeBitSet(sourceFile.FileDependencies, writer);

            SerializeBitSet(sourceFile.FileDependents, writer);

            writer.Write(sourceFile.BindingSymbols?.ReferencedSymbolsFingerprint ?? string.Empty);
            writer.Write(sourceFile.BindingSymbols?.DeclaredSymbolsFingerprint ?? string.Empty);
        }

        /// <summary>
        /// Serializes the binding state to the given writer.
        /// </summary>
        public static void SerializeBindingSymbols([NotNull] SpecBindingSymbols bindingSymbols, [NotNull] BuildXLWriter writer)
        {
            SerializeSymbols(bindingSymbols.DeclaredSymbols, writer);
            SerializeSymbols(bindingSymbols.ReferencedSymbols, writer);
            writer.Write(bindingSymbols.DeclaredSymbolsFingerprint);
            writer.Write(bindingSymbols.ReferencedSymbolsFingerprint);
        }

        private static void SerializeSymbols(IReadOnlySet<InteractionSymbol> bindingSymbols, BuildXLWriter writer)
        {
            writer.WriteCompact(bindingSymbols?.Count ?? 0);

            if (bindingSymbols != null)
            {
                foreach (var symbol in bindingSymbols)
                {
                    writer.WriteCompact((int)symbol.Kind);
                    writer.Write(symbol.FullName);
                }
            }
        }

        /// <summary>
        /// Deserializes the spec fingerprint from the given reader.
        /// </summary>
        [NotNull]
        public static SpecBindingSymbols DeserializeBindingFingerprint([NotNull] BuildXLReader reader)
        {
            var declaredSymbols = ReadBindingSymbols(reader);
            var referencedSymbols = ReadBindingSymbols(reader);
            var declaredSymbolsFingerprint = reader.ReadString();
            var referencedSymbolsFingerprint = reader.ReadString();

            return new SpecBindingSymbols(declaredSymbols.ToReadOnlySet(), referencedSymbols.ToReadOnlySet(), declaredSymbolsFingerprint, referencedSymbolsFingerprint);
        }

        private static List<InteractionSymbol> ReadBindingSymbols(BuildXLReader reader)
        {
            int count = reader.ReadInt32Compact();
            var list = new List<InteractionSymbol>(count);

            for (int i = 0; i < count; i++)
            {
                int kind = reader.ReadInt32Compact();
                var fullName = reader.ReadString();
                list.Add(new InteractionSymbol((SymbolKind)kind, fullName));
            }

            return list;
        }

        /// <summary>
        /// Serializes the <see cref="RoaringBitSet"/> to the given writer.
        /// </summary>
        public static void SerializeBitSet([NotNull]RoaringBitSet bitArray, [NotNull]BuildXLWriter writer)
        {
            var set = bitArray.MaterializedSet;
            writer.WriteCompact(set.Count);
            foreach (var index in set)
            {
                writer.WriteCompact(index);
            }
        }

        /// <summary>
        /// Deserializes the <see cref="RoaringBitSet"/> from the given reader.
        /// </summary>
        [NotNull]
        public static RoaringBitSet DeserializeBitVector([NotNull]BuildXLReader reader)
        {
            var size = reader.ReadInt32Compact();
            var set = new HashSet<int>();
            for (int i = 0; i < size; i++)
            {
                set.Add(reader.ReadInt32Compact());
            }

            return RoaringBitSet.FromSet(set);
        }
    }
}
