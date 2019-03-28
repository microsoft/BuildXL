// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using JetBrains.Annotations;

namespace BuildXL.FrontEnd.Sdk
{
    /// <summary>
    /// Data used to perform filtering at Evaluation time.
    /// </summary>
    public interface IEvaluationFilter
    {
        /// <summary>
        /// Value names that can be resolved in a string form. If empty, all values must be resolved.
        /// </summary>
        [NotNull]
        IReadOnlyList<string> ValueNamesToResolveAsStrings { get; }

        /// <summary>
        /// Value definition roots to resolve in a string form. If empty, all definition sites must be resolved.
        /// </summary>
        [NotNull]
        IReadOnlyList<string> ValueDefinitionRootsToResolveAsStrings { get; }

        /// <summary>
        /// Module to resolve in a string form.  If empty, all modules must be resolved.
        /// </summary>
        [NotNull]
        IReadOnlyList<string> ModulesToResolveAsStrings { get; }

        /// <summary>
        /// Returns true if a current filter produces the graph that is a subset of the graph produced by the <paramref name="supersetCandidateFilter"/>.
        /// </summary>
        bool IsSubSetOf(IEvaluationFilter supersetCandidateFilter);

        /// <summary>
        /// Write the content of the filter to a writer.
        /// </summary>
        void Serialize(BinaryWriter writer);

        /// <summary>
        /// Returns a string representation of a filter.
        /// </summary>
        string ToDisplayString();

        /// <summary>
        /// Returns true if the evaluation filter can be used for partial evalaution.
        /// </summary>
        bool CanPerformPartialEvaluation { get; }

        /// <summary>
        /// Gets a deserialized filter that only has names, values, and modules as string.
        /// </summary>
        IEvaluationFilter GetDeserializedFilter();
    }

    /// <summary>
    /// Deserialized version of evaluation filter.
    /// </summary>
    /// <remarks>
    /// Filter comparison is happening before the symbol table is available. To work around this issue, the filter is serialized as text and compared using string comparison.
    /// </remarks>
    [DebuggerDisplay("{ToDisplayString(),nq}")]
    internal sealed class DeserializedEvaluationFilter : IEvaluationFilter
    {
        public DeserializedEvaluationFilter(IReadOnlyList<string> namesToResolve, IReadOnlyList<string> valueDefinitionRootsToResolve, IReadOnlyList<string> modulesToResolve)
        {
            ValueNamesToResolveAsStrings = namesToResolve;
            ValueDefinitionRootsToResolveAsStrings = valueDefinitionRootsToResolve;
            ModulesToResolveAsStrings = modulesToResolve;
        }

        /// <inheritdoc />
        public IReadOnlyList<string> ValueNamesToResolveAsStrings { get; }

        /// <inheritdoc />
        public IReadOnlyList<string> ValueDefinitionRootsToResolveAsStrings { get; }

        /// <inheritdoc />
        public IReadOnlyList<string> ModulesToResolveAsStrings { get; }

        /// <inheritdoc />
        public bool IsSubSetOf(IEvaluationFilter supersetCandidateFilter) => EvaluationFilter.IsSubSetOf(this, supersetCandidateFilter);

        /// <inheritdoc />
        public void Serialize(BinaryWriter writer) => EvaluationFilter.Serialize(this, writer);

        /// <inheritdoc />
        public string ToDisplayString()
        {
            return $"[{ModulesToResolveAsStrings.Count} module(s), {ValueDefinitionRootsToResolveAsStrings.Count} spec(s), {ValueNamesToResolveAsStrings.Count} value(s)]";
        }

        /// <inheritdoc />
        public IEvaluationFilter GetDeserializedFilter() => this;

        /// <inheritdoc />
        public bool CanPerformPartialEvaluation => ValueNamesToResolveAsStrings.Count > 0 || ValueDefinitionRootsToResolveAsStrings.Count > 0 || ModulesToResolveAsStrings.Count > 0;
    }

    /// <summary>
    /// Data used to perform filtering at Evaluation time.
    /// </summary>
    [DebuggerDisplay("{ToDisplayString(),nq}")]
    public sealed class EvaluationFilter : IEvaluationFilter
    {
        private readonly SymbolTable m_symbolTable;
        private readonly PathTable m_pathTable;

        /// <summary>
        /// Value names that can be resolved. If empty, all values must be resolved
        /// </summary>
        [NotNull]
        public IReadOnlyList<FullSymbol> ValueNamesToResolve { get; }

        IReadOnlyList<string> IEvaluationFilter.ValueNamesToResolveAsStrings => ValueNamesToResolve.Select(n => n.ToString(m_symbolTable)).ToList();

        /// <summary>
        /// Value definition roots to resolve. If empty, all definition sites must be resolved.
        /// </summary>
        [NotNull]
        public IReadOnlyList<AbsolutePath> ValueDefinitionRootsToResolve { get; }

        IReadOnlyList<string> IEvaluationFilter.ValueDefinitionRootsToResolveAsStrings => ValueDefinitionRootsToResolve.Select(r => r.ToString(m_pathTable)).ToList();

        /// <summary>
        /// Modules to resolve. If empty, all definition sites must be resolved.
        /// </summary>
        [NotNull]
        public IReadOnlyList<StringId> ModulesToResolve { get; }

        IReadOnlyList<string> IEvaluationFilter.ModulesToResolveAsStrings => ModulesToResolve.Select(m => m.ToString(m_pathTable.StringTable)).ToList();

        /// <summary>
        /// An empty filter that cannot be used for partial evaluation
        /// </summary>
        public static readonly EvaluationFilter Empty = new EvaluationFilter(null, null, CollectionUtilities.EmptyArray<FullSymbol>(), CollectionUtilities.EmptyArray<AbsolutePath>(), CollectionUtilities.EmptyArray<StringId>());

        /// <nodoc />
        public EvaluationFilter(
            SymbolTable symbolTable,
            PathTable pathTable,
            IReadOnlyList<FullSymbol> valueNamesToResolve,
            IReadOnlyList<AbsolutePath> valueDefinitionRootsToResolve,
            IReadOnlyList<StringId> modulesToResolver)
        {
            Contract.Requires(valueNamesToResolve != null);
            Contract.Requires(valueDefinitionRootsToResolve != null);
            Contract.Requires(modulesToResolver != null);

            // Path table should not be null if at least one filter is defined.
            if (valueDefinitionRootsToResolve.Count != 0 || modulesToResolver.Count != 0)
            {
                Contract.Assert(pathTable != null, "If value definition or module filters are specified, the path table should not be null.");
            }

            if (valueNamesToResolve.Count != 0)
            {
                Contract.Assert(symbolTable != null, "If value names to resolved are specified, the symbol table should not be null");
            }

            m_symbolTable = symbolTable;
            m_pathTable = pathTable;
            ValueNamesToResolve = valueNamesToResolve;
            ValueDefinitionRootsToResolve = valueDefinitionRootsToResolve;
            ModulesToResolve = modulesToResolver;
        }

        /// <summary>
        /// Serializes the object
        /// </summary>
        public void Serialize(BinaryWriter writer)
        {
            Serialize(this, writer);
        }

        internal static void Serialize(IEvaluationFilter filter, BinaryWriter writer)
        {
            Write(writer, filter.ValueNamesToResolveAsStrings, (w, e) => w.Write(e));
            Write(writer, filter.ValueDefinitionRootsToResolveAsStrings, (w, e) => w.Write(e));
            Write(writer, filter.ModulesToResolveAsStrings, (w, e) => w.Write(e));
        }

        /// <summary>
        /// Deserializes the object
        /// </summary>
        public static IEvaluationFilter Deserialize(BinaryReader reader)
        {
            var namesToResolve = ReadList(reader, r => r.ReadString());
            var valueDefinitionRootsToResolve = ReadList(reader, r => r.ReadString());
            var modulesToResolve = ReadList(reader, r => r.ReadString());
            return new DeserializedEvaluationFilter(namesToResolve, valueDefinitionRootsToResolve, modulesToResolve);
        }

        /// <inheritdoc />
        public IEvaluationFilter GetDeserializedFilter()
        {
            var thisFilter = this as IEvaluationFilter;
            return new DeserializedEvaluationFilter(
                thisFilter.ValueNamesToResolveAsStrings, 
                thisFilter.ValueDefinitionRootsToResolveAsStrings, 
                thisFilter.ModulesToResolveAsStrings);
        }

        /// <summary>
        /// Helper to create an evaluation filter from a single spec file
        /// </summary>
        public static EvaluationFilter FromSingleSpecPath(SymbolTable symbolTable, PathTable pathTable, AbsolutePath specPath)
        {
            Contract.Requires(specPath.IsValid);

            return new EvaluationFilter(symbolTable, pathTable, CollectionUtilities.EmptyArray<FullSymbol>(), new AbsolutePath[] { specPath }, CollectionUtilities.EmptyArray<StringId>());
        }

        /// <inheritdoc />
        public bool CanPerformPartialEvaluation => ValueNamesToResolve.Count > 0 || ValueDefinitionRootsToResolve.Count > 0 || ModulesToResolve.Count > 0;

        /// <summary>
        /// True if a partial evaluation can be performed for DScript.
        /// </summary>
        public bool CanPerformPartialEvaluationScript(AbsolutePath primaryConfigFile)
        {
            if (ValueNamesToResolve.Count > 0)
            {
                return false;
            }

            if (ValueDefinitionRootsToResolve.Any(path => path == primaryConfigFile))
            {
                return false;
            }

            return ValueDefinitionRootsToResolve.Count > 0 || ModulesToResolve.Count > 0;
        }

        /// <summary>
        /// Checks if the evaluation filter has a single path or not.
        /// </summary>
        /// <remarks>
        /// If you don't care about the file, you can pass AbsolutePath.Invalid
        /// </remarks>
        public bool HasSinglePath(AbsolutePath path)
        {
            if (ValueNamesToResolve.Count != 0)
            {
                return false;
            }

            if (ModulesToResolve.Count != 0)
            {
                return false;
            }

            if (ValueDefinitionRootsToResolve.Count != 1)
            {
                return false;
            }

            return path == AbsolutePath.Invalid || path == ValueDefinitionRootsToResolve[0];
        }

        private static void Write<T>(BinaryWriter writer, IReadOnlyList<T> list, Action<BinaryWriter, T> write)
        {
            writer.Write(list.Count);
            foreach (var l in list.AsStructEnumerable())
            {
                write(writer, l);
            }
        }

        private static IReadOnlyList<T> ReadList<T>(BinaryReader reader, Func<BinaryReader, T> read)
        {
            int count = reader.ReadInt32();
            List<T> result = new List<T>(count);

            for (int i = 0; i < count; i++)
            {
                result.Add(read(reader));
            }

            return result;
        }

        /// <summary>
        /// Returns true if a current filter produces the graph that is a subset of the graph produced by the <paramref name="supersetCandidateFilter"/>.
        /// </summary>
        public bool IsSubSetOf(IEvaluationFilter supersetCandidateFilter)
        {
            IEvaluationFilter subset = this;
            return IsSubSetOf(subset, supersetCandidateFilter);
        }

        internal static bool IsSubSetOf(IEvaluationFilter subset, IEvaluationFilter supersetCandidateFilter)
        {
            return IsSubSetOf(subsetCandiate: subset.ModulesToResolveAsStrings, supersetCandidate: supersetCandidateFilter.ModulesToResolveAsStrings) &&
                   IsSubSetOf(subsetCandiate: subset.ValueNamesToResolveAsStrings, supersetCandidate: supersetCandidateFilter.ValueNamesToResolveAsStrings) &&
                   IsSubSetOf(subsetCandiate: subset.ValueDefinitionRootsToResolveAsStrings,
                       supersetCandidate: supersetCandidateFilter.ValueDefinitionRootsToResolveAsStrings);
        }

        private static bool IsSubSetOf<T>(IReadOnlyCollection<T> subsetCandiate, IReadOnlyCollection<T> supersetCandidate)
        {
            return supersetCandidate.Count == 0 || new HashSet<T>(subsetCandiate).IsSubsetOf(supersetCandidate);
        }

        /// <inheritdoc />
        public string ToDisplayString()
        {
            return $"[{ModulesToResolve.Count} module(s), {ValueDefinitionRootsToResolve.Count} spec(s), {ValueNamesToResolve.Count} value(s)]";
        }
    }
}
