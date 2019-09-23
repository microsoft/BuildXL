// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text;
using System.Threading;
using BuildXL.Utilities;
using JetBrains.Annotations;
using TypeScript.Net.Diagnostics;
using TypeScript.Net.TypeChecking;
using NotNull = JetBrains.Annotations.NotNullAttribute;

#pragma warning disable SA1649 // File name must match first type name

namespace TypeScript.Net.Types
{
    /// <nodoc />
    public interface ITextSpan
    {
        /// <nodoc/>
        int Start { get; set; }

        /// <nodoc/>
        int Length { get; set; }
    }

    /// <nodoc />
    public interface ITextChangeRange
    {
        /// <nodoc/>
        ITextSpan Span { get; set; }

        /// <nodoc/>
        int NewLength { get; set; }
    }

    /// <summary>
    /// Simple custom collection that represents indexable set of nodes.
    /// </summary>
    public interface INodeArray<out T> : IReadOnlyTextRange
    {
        /// <summary>
        /// Underlying elements of the array.
        /// </summary>
        /// <remarks>
        /// The property is added for backward compatibilities and will be obsolete in the future.
        /// </remarks>
        IReadOnlyList<T> Elements { get; }

        /// <nodoc/>
        int Count { get; }

        /// <nodoc/>
        bool HasTrailingComma { get; set; }

        /// <nodoc/>
        T this[int index] { get; }

        /// <nodoc/>
        int Length { get; }

        /// <nodoc/>
        IEnumerator<T> GetEnumerator();
    }

    /// <summary>
    /// Special lightweight node array for variable declarations.
    /// </summary>
    public sealed class VariableDeclarationNodeArray : INodeArray<IVariableDeclaration>
    {
        private readonly IVariableDeclaration m_declaration;

        /// <inheritdoc />
        public IReadOnlyList<IVariableDeclaration> Elements
        {
            get { throw new NotSupportedException(); }
        }

        /// <inheritdoc />
        public int Pos { get; }

        /// <inheritdoc />
        public int End { get; }

        /// <inheritdoc />
        public int Count => Length;

        /// <inheritdoc />
        public bool HasTrailingComma
        {
            get { return false; }
            set { }
        }

        /// <nodoc />
        public VariableDeclarationNodeArray(IVariableDeclaration declaration, int pos, int end)
        {
            m_declaration = declaration;
            Pos = pos;
            End = end;
        }

        /// <nodoc />
        public static INodeArray<IVariableDeclaration> Create(INodeArray<IVariableDeclaration> declarations)
        {
            if (declarations.Count == 1)
            {
                return new VariableDeclarationNodeArray(declarations[0], declarations.Pos, declarations.End);
            }

            return declarations;
        }

        /// <inheritdoc />
        [SuppressMessage("Microsoft.Usage", "CA2201:DoNotRaiseReservedExceptionTypes", Justification = "Valid use case for IndexOutOfRangeException.")]
        public IVariableDeclaration this[int index]
        {
            get
            {
                if (index == 0)
                {
                    return m_declaration;
                }

                throw new IndexOutOfRangeException();
            }
        }

        /// <inheritdoc />
        public int Length => 1;

        /// <inheritdoc />
        IEnumerator<IVariableDeclaration> INodeArray<IVariableDeclaration>.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <nodoc/>
        public NodeArray.NodeEnumerator<IVariableDeclaration> GetEnumerator()
        {
            return new NodeArray.NodeEnumerator<IVariableDeclaration>(this);
        }
    }

    /// <summary>
    /// Special lightweight node array for template spans.
    /// </summary>
    /// <remarks>
    /// In vast majority of cases the each template has one or two elements.
    /// Creating a special node array for this case instead of using <see cref="NodeArray{T}"/>
    /// saves reasonable amount of memory for a large build.
    /// </remarks>
    public sealed class TemplateSpanNodeArray : INodeArray<ITemplateSpan>
    {
        private readonly ITemplateSpan m_firstElement;
        private readonly ITemplateSpan m_secondElement;

        /// <inheritdoc />
        public int Pos { get; }

        /// <inheritdoc />
        public int End { get; }

        /// <inheritdoc />
        public int Count => Length;

        /// <inheritdoc />
        public bool HasTrailingComma
        {
            get { return false; }
            set { }
        }

        /// <inheritdoc />
        public IReadOnlyList<ITemplateSpan> Elements
        {
            get { throw new NotSupportedException(); }
        }

        /// <nodoc />
        public TemplateSpanNodeArray([CanBeNull]ITemplateSpan firstElement, [CanBeNull]ITemplateSpan secondElement, int pos, int end)
        {
            m_firstElement = firstElement;
            m_secondElement = secondElement;
            Pos = pos;
            End = end;
        }

        /// <nodoc />
        public static INodeArray<ITemplateSpan> Create(int pos, int end)
        {
            return new TemplateSpanNodeArray(null, null, pos, end);
        }

        /// <nodoc />
        public static INodeArray<ITemplateSpan> Create(int pos, int end, ITemplateSpan firstElement)
        {
            return new TemplateSpanNodeArray(firstElement, null, pos, end);
        }

        /// <nodoc />
        public static INodeArray<ITemplateSpan> Create(int pos, int end, [NotNull]ITemplateSpan firstElement, [NotNull]ITemplateSpan secondElement)
        {
            return new TemplateSpanNodeArray(firstElement, secondElement, pos, end);
        }

        /// <nodoc />
        public static INodeArray<ITemplateSpan> Create(int pos, int end, List<ITemplateSpan> spans)
        {
            Contract.Requires(spans.Count > 2);

            return new NodeArray<ITemplateSpan>(spans) { Pos = pos, End = end };
        }

        /// <inheritdoc />
        [SuppressMessage("Microsoft.Usage", "CA2201:DoNotRaiseReservedExceptionTypes", Justification = "Valid use case for IndexOutOfRangeException.")]
        public ITemplateSpan this[int index]
        {
            get
            {
                if (index == 0)
                {
                    return m_firstElement;
                }

                if (index == 1)
                {
                    return m_secondElement;
                }

                throw new IndexOutOfRangeException();
            }
        }

        /// <inheritdoc />
        public int Length
        {
            get
            {
                int length = 0;
                if (m_firstElement != null)
                {
                    length++;
                }

                if (m_secondElement != null)
                {
                    length++;
                }

                return length;
            }
        }

        /// <inheritdoc />
        IEnumerator<ITemplateSpan> INodeArray<ITemplateSpan>.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <nodoc/>
        public NodeArray.NodeEnumerator<ITemplateSpan> GetEnumerator()
        {
            return new NodeArray.NodeEnumerator<ITemplateSpan>(this);
        }
    }

    /// <nodoc/>
    public static class NodeArray
    {
        /// <nodoc/>
        public static NodeArray<T> Create<T>(params T[] args)
        {
            return new NodeArray<T>(args);
        }

        /// <nodoc/>
        public static NodeArray<T> Empty<T>() => NodeArray<T>.Empty;

        /// <nodoc/>
        public static NodeArrayEnumerable<T> AsStructEnumerable<T>([CanBeNull]this INodeArray<T> @this)
        {
            return new NodeArrayEnumerable<T>(@this);
        }

        /// <nodoc/>
        public static bool Any<T>([CanBeNull]this INodeArray<T> @this, Func<T, bool> predicate)
        {
            foreach (var e in @this.AsStructEnumerable())
            {
                if (predicate(e))
                {
                    return true;
                }
            }

            return false;
        }

        /// <nodoc/>
        public static bool All<T>([CanBeNull]this INodeArray<T> @this, Func<T, bool> predicate)
        {
            foreach (var e in @this.AsStructEnumerable())
            {
                if (!predicate(e))
                {
                    return false;
                }
            }

            return true;
        }

        /// <nodoc />
        public readonly struct NodeArrayEnumerable<T>
        {
            private readonly INodeArray<T> m_array;

            /// <nodoc />
            public NodeArrayEnumerable(INodeArray<T> array)
            {
                m_array = array;
            }

            /// <nodoc />
            public NodeEnumerator<T> GetEnumerator()
            {
                return new NodeEnumerator<T>(m_array);
            }
        }

        /// <nodoc />
        public struct NodeEnumerator<T> : IEnumerator<T>
        {
            private readonly INodeArray<T> m_array;
            private int m_index;

            /// <nodoc />
            public NodeEnumerator([CanBeNull]INodeArray<T> array)
            {
                m_array = array;
                m_index = -1;
            }

            /// <inheritdoc />
            public void Reset()
            {
            }

            /// <inheritdoc />
            object IEnumerator.Current => Current;

            /// <inheritdoc />
            public T Current => m_array[m_index];

            /// <inheritdoc />
            public bool MoveNext()
            {
                if (m_index + 1 == (m_array?.Count ?? 0))
                {
                    return false;
                }

                m_index++;
                return true;
            }

            /// <inheritdoc />
            public void Dispose()
            {
            }
        }

        /// <summary>
        /// Allocation-free enumerator for LINQ-like Select function.
        /// </summary>
        public readonly struct NodeArraySelectorEnumerable<TSource, TResult>
        {
            private readonly INodeArray<TSource> m_array;
            private readonly Func<TSource, TResult> m_selector;

            /// <nodoc />
            public NodeArraySelectorEnumerable([CanBeNull]INodeArray<TSource> array, Func<TSource, TResult> selector)
            {
                m_array = array;
                m_selector = selector;
            }

            /// <nodoc />
            public int ArraySize => m_array?.Count ?? 0;

            /// <nodoc />
            public NodeSelectorEnumerator<TSource, TResult> GetEnumerator()
            {
                return new NodeSelectorEnumerator<TSource, TResult>(m_array, m_selector);
            }
        }

        /// <nodoc />
        public struct NodeSelectorEnumerator<TSource, TResult>
        {
            private readonly INodeArray<TSource> m_array;
            private readonly Func<TSource, TResult> m_selector;
            private int m_index;

            /// <nodoc />
            public NodeSelectorEnumerator([CanBeNull]INodeArray<TSource> array, Func<TSource, TResult> selector)
            {
                m_array = array;
                m_index = -1;
                m_selector = selector;
            }

            /// <nodoc />
            public TResult Current => m_selector(m_array[m_index]);

            /// <nodoc />
            public bool MoveNext()
            {
                if (m_index + 1 == (m_array?.Count ?? 0))
                {
                    return false;
                }

                m_index++;
                return true;
            }
        }
    }

    /// <summary>
    /// Concrete list-based implementation of the array of nodes.
    /// </summary>
    public class NodeArray<T> : INodeArray<T>
    {
        /// <nodoc/>
        private readonly List<T> m_nodes;

        /// <summary>
        /// Unsafe property exposes the underlying list to allow mutation in certain cases.
        /// </summary>
        [Obsolete("This is an unsafe API, do not use.")]
        public List<T> UnsafeMutableElementsForDynamicAccess => m_nodes;

        /// <summary>
        /// Returns underlying elements of the array in a readonly way.
        /// </summary>
        public IReadOnlyList<T> Elements => m_nodes;

        /// <nodoc/>
        public bool IsReadOnly { get; }

        /// <inheritdoc/>
        public int Pos { get; internal set; }

        /// <inheritdoc/>
        public int End { get; internal set; }

        /// <inheritdoc/>
        public bool HasTrailingComma { get; set; }

        /// <summary>
        /// Added for compatibility regarding TypeScript.Net.Syntax.
        /// The positions have the following layout for the examplary type argument list "&lt;number, string>":
        /// [PosIncludingStartToken]&lt;[Pos]number, string[End]>[EndIncludingEndToken]
        /// </summary>
        public int PosIncludingStartToken { get; set; }

        /// <nodoc/>
        public int EndIncludingEndToken { get; set; }

        /// <nodoc/>
        public NodeArray(bool isReadOnly = false)
        {
            IsReadOnly = isReadOnly;
            m_nodes = new List<T>();
        }

        /// <nodoc/>
        public NodeArray(int size)
        {
            IsReadOnly = false;
            m_nodes = new List<T>(size);
        }

        /// <nodoc/>
        public NodeArray(params T[] elements)
        {
            m_nodes = elements.Where(el => el != null).ToList();
        }

        /// <nodoc/>
        public NodeArray(T element)
        {
            m_nodes = element != null ? new List<T> { element } : new List<T>();
        }

        /// <nodoc/>
        public NodeArray(List<T> nodes)
        {
            m_nodes = nodes;
        }

        /// <inheritdoc/>
        IEnumerator<T> INodeArray<T>.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <summary>
        /// Non-allocating enumerator.
        /// </summary>
        public NodeArray.NodeEnumerator<T> GetEnumerator()
        {
            return new NodeArray.NodeEnumerator<T>(this);
        }

        /// <nodoc/>
        public int Length => m_nodes.Count;

        /// <inheritdoc/>
        public int Count => m_nodes.Count;

        /// <nodoc/>
        public void Add(T value)
        {
            ThrowIfReadonly();
            m_nodes.Add(value);
        }

        /// <nodoc/>
        public void Insert(int index, T item)
        {
            ThrowIfReadonly();
            m_nodes.Insert(index, item);
        }

        /// <nodoc/>
        public void Clear()
        {
            ThrowIfReadonly();
            m_nodes.Clear();
        }

        /// <nodoc/>
        public bool Contains(T item)
        {
            return m_nodes.Contains(item);
        }

        /// <nodoc/>
        public int IndexOf(T item)
        {
            return m_nodes.IndexOf(item);
        }

        /// <nodoc/>
        public void CopyTo(T[] array, int arrayIndex)
        {
            m_nodes.CopyTo(array, arrayIndex);
        }

        /// <nodoc/>
        public void CopyTo(int index, T[] array, int arrayIndex, int count)
        {
            m_nodes.CopyTo(index, array, arrayIndex, count);
        }

        /// <nodoc/>
        public bool Remove(T item)
        {
            ThrowIfReadonly();
            return m_nodes.Remove(item);
        }

        /// <nodoc/>
        public void Sort(Comparison<T> comparison)
        {
            ThrowIfReadonly();
            m_nodes.Sort(comparison);
        }

        /// <nodoc/>
        public T this[int index]
        {
            get { return m_nodes[index]; }
        }

        /// <nodoc/>
        public static readonly NodeArray<T> Empty = new NodeArray<T>(isReadOnly: true);

        private void ThrowIfReadonly()
        {
            if (IsReadOnly)
            {
                throw new InvalidOperationException("Can't mutate readonly NodeArray instance");
            }
        }
    }

    /// <summary>
    /// Special node array type that holds only set of <see cref="IModifier"/>.
    /// </summary>
    public abstract class ModifiersArray : INodeArray<IModifier>
    {
        /// <nodoc/>
        public static ModifiersArray Create(NodeFlags flags, int pos, int end, List<IModifier> modifiers)
        {
            ModifiersArray result = modifiers.Count == 1
                ? (ModifiersArray)new SingleModifiersNodeArray(pos, end, modifiers[0])
                : new ModifiersNodeArray(pos, end, modifiers);
            result.Flags = flags;

            return result;
        }

        /// <nodoc/>
        public static ModifiersArray Create(NodeFlags flags)
        {
            return new ModifiersNodeArray(flags);
        }

        /// <inheritdoc />
        public IReadOnlyList<IModifier> Elements
        {
            get { throw new NotSupportedException(); }
        }

        /// <nodoc/>
        public NodeFlags Flags { get; set; }

        /// <inheritdoc />
        public abstract int Pos { get; set; }

        /// <inheritdoc />
        public abstract int End { get; set; }

        /// <inheritdoc />
        public abstract int Count { get; }

        /// <summary>
        /// Add a modifier to the modifiers array.
        /// </summary>
        public abstract void Add(IModifier modifier);

        /// <inheritdoc />
        public bool HasTrailingComma
        {
            get { return false; }
            set { }
        }

        /// <inheritdoc />
        public abstract IModifier this[int index] { get; }

        /// <inheritdoc />
        public int Length => Count;

        /// <nodoc />
        public abstract ModifiersArrayEnumerator GetEnumerator();

        /// <inheritdoc />
        IEnumerator<IModifier> INodeArray<IModifier>.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <nodoc />
        public struct ModifiersArrayEnumerator : IEnumerator<IModifier>
        {
            private readonly IModifier m_modifier;
            private bool m_movedNext;
            private List<IModifier>.Enumerator m_enumerator;

            /// <nodoc />
            public ModifiersArrayEnumerator(IModifier modifier)
                : this()
            {
                m_modifier = modifier;
            }

            /// <nodoc />
            public ModifiersArrayEnumerator(List<IModifier>.Enumerator enumerator)
                : this()
            {
                m_enumerator = enumerator;
            }

            /// <nodoc />
            public void Dispose()
            {
            }

            /// <nodoc />
            public bool MoveNext()
            {
                if (m_modifier != null)
                {
                    if (m_movedNext)
                    {
                        return false;
                    }

                    m_movedNext = true;
                    return true;
                }

                return m_enumerator.MoveNext();
            }

            /// <nodoc />
            public void Reset()
            {
            }

            /// <nodoc />
            public IModifier Current => m_modifier ?? m_enumerator.Current;

            /// <nodoc />
            object IEnumerator.Current => Current;
        }
    }

    /// <summary>
    /// Modifiers array with arbitrary number of elements.
    /// </summary>
    public sealed class ModifiersNodeArray : ModifiersArray
    {
        private readonly List<IModifier> m_modifiers;

        /// <nodoc />
        public ModifiersNodeArray(int pos, int end, List<IModifier> modifiers)
        {
            Pos = pos;
            End = end;
            m_modifiers = modifiers;
        }

        /// <nodoc />
        public ModifiersNodeArray()
        {
            m_modifiers = new List<IModifier>();
        }

        /// <nodoc />
        public ModifiersNodeArray(NodeFlags flags)
            : this()
        {
            Flags = flags;
        }

        /// <inheritdoc />
        public override int Pos { get; set; }

        /// <inheritdoc />
        public override int End { get; set; }

        /// <inheritdoc />
        public override void Add(IModifier modifier) => m_modifiers.Add(modifier);

        /// <inheritdoc />
        public override int Count => m_modifiers.Count;

        /// <inheritdoc />
        public override IModifier this[int index] => m_modifiers[index];

        /// <inheritdoc />
        public override ModifiersArrayEnumerator GetEnumerator()
        {
            return new ModifiersArrayEnumerator(m_modifiers.GetEnumerator());
        }
    }

    /// <summary>
    /// Modifiers array with exactly one element.
    /// </summary>
    public sealed class SingleModifiersNodeArray : ModifiersArray
    {
        private readonly IModifier m_modifier;

        /// <nodoc />
        public SingleModifiersNodeArray(int pos, int end, [NotNull]IModifier modifier)
        {
            Pos = pos;
            End = end;
            m_modifier = modifier;
        }

        /// <inheritdoc />
        public override int Pos { get; set; }

        /// <inheritdoc />
        public override int End { get; set; }

        /// <inheritdoc />
        public override void Add(IModifier modifier)
        {
            throw new NotSupportedException("SingleModifiersNodeArray dows not support Add method.");
        }

        /// <inheritdoc />
        public override int Count => 1;

        /// <inheritdoc />
        public override IModifier this[int index]
        {
            get
            {
                if (index != 0)
                {
                    throw new ArgumentException("Index is out of range", nameof(index));
                }

                return m_modifier;
            }
        }

        /// <nodoc />
        public override ModifiersArrayEnumerator GetEnumerator()
        {
            return new ModifiersArrayEnumerator(m_modifier);
        }
    }

    /// <summary>
    /// Represents a text range in the source code;
    /// </summary>
    public interface ITextRange
    {
        /// <nodoc/>
        int Pos { get; set; }

        /// <nodoc/>
        int End { get; set; }
    }

    /// <summary>
    /// Represents a text range in the source code;
    /// </summary>
    public interface IReadOnlyTextRange
    {
        /// <nodoc/>
        int Pos { get; }

        /// <nodoc/>
        int End { get; }
    }

    /// <nodoc />
    public interface IAmdDependency
    {
        /// <nodoc/>
        string Path { get; set; }

        /// <nodoc/>
        string Name { get; }
    }

    /// <nodoc/>
    public abstract class ScriptReferenceHost
    {
        /// <nodoc/>
        public abstract ICompilerOptions GetCompilerOptions();

        /// <nodoc/>
        public abstract ISourceFile GetSourceFile(string fileName);

        /// <nodoc/>
        public abstract string GetCurrentDirectory();
    }

    /// <nodoc/>
    public abstract class ParseConfigHost
    {
        /// <nodoc/>
        public abstract string[] ReadDirectory(string rootDir, string extension, string[] exclude);
    }

    /// <nodoc />
    public delegate void WriteFileCallback(string fileName, string data, bool writeByteOrderMark, Action<string> onError/*?*/);

    /// <nodoc/>
    public abstract class CancellationToken
    {
        /// <nodoc/>
        public abstract bool IsCancellationRequested();

        /** @throws OperationCanceledException if isCancellationRequested is true */
        public abstract void ThrowIfCancellationRequested();
    }

    /// <nodoc />
    internal abstract class Program : ScriptReferenceHost
    {
        // For testing purposes only.
        /* @internal */
        public Optional<bool> StructureIsReused { get; set; }
        /**
         * Get a list of root file names that were passed to a 'createProgram'
         */
        public abstract string[] GetRootFileNames();

        /**
         * Get a list of files in the program
         */
        public abstract ISourceFile[] GetSourceFiles();

        /**
         * Emits the JavaScript and declaration files.  If targetSourceFile is not specified, then
         * the JavaScript and declaration files will be produced for all the files in this program.
         * If targetSourceFile is specified, then only the JavaScript and declaration for that
         * specific file will be generated.
         *
         * If writeFile is not specified then the writeFile callback from the compiler host will be
         * used for writing the JavaScript and declaration files.  Otherwise, the writeFile parameter
         * will be invoked when writing the JavaScript and declaration files.
         */
        public abstract IEmitResult Emit(Optional<ISourceFile> targetSourceFile, Optional<WriteFileCallback> writeFile, Optional<CancellationToken> cancellationToken);

        /// <nodoc/>
        public abstract Diagnostic[] GetOptionsDiagnostics(Optional<CancellationToken> cancellationToken);

        /// <nodoc/>
        public abstract Diagnostic[] GetGlobalDiagnostics(Optional<CancellationToken> cancellationToken);

        /// <nodoc/>
        public abstract Diagnostic[] GetSyntacticDiagnostics(Optional<ISourceFile> sourceFile, Optional<CancellationToken> cancellationToken);

        /// <nodoc/>
        public abstract Diagnostic[] GetSemanticDiagnostics(Optional<ISourceFile> sourceFile, Optional<CancellationToken> cancellationToken);

        /// <nodoc/>
        public abstract Diagnostic[] GetDeclarationDiagnostics(Optional<ISourceFile> sourceFile, Optional<CancellationToken> cancellationToken);

        /**
         * Gets a type checker that can be used to semantically analyze source fils in the program.
         */
        public abstract ITypeChecker GetTypeChecker();

        /* @internal */
        public abstract string GetCommonSourceDirectory();

        // For testing purposes only.  Should not be used by any other consumers (including the
        // language service).
        /* @internal */
        public abstract ITypeChecker GetDiagnosticsProducingTypeChecker();

        /* @internal */
        public abstract Dictionary<string, string> GetClassifiableNames();

        /* @internal */
        public abstract int GetNodeCount();

        /* @internal */
        public abstract int GetIdentifierCount();

        /* @internal */
        public abstract int GetSymbolCount();

        /* @internal */
        public abstract int GetTypeCount();

        /* @internal */
        internal abstract DiagnosticCollection GetFileProcessingDiagnostics();
    }

    /// <nodoc />
    public interface ISourceMapSpan
    {
        /// <summary>
        /// Line int in the .js file.
        /// </summary>
        int EmittedLine { get; set; }

        /// <summary>
        /// Column int in the .js file.
        /// </summary>
        int EmittedColumn { get; set; }

        /// <summary>
        /// Line int in the .ts file.
        /// </summary>
        int SourceLine { get; set; }

        /// <summary>
        /// Column int in the .ts file.
        /// </summary>
        int SourceColumn { get; set; }

        /// <summary>
        /// Optional name (index into names array) associated with this span.
        /// </summary>
        int? NameIndex { get; set; }

        /// <summary>
        /// .ts file (index into sources array) associated with this span
        /// </summary>
        int SourceIndex { get; set; }
    }

    /// <nodoc />
    public interface ISourceMapData
    {
        /// <summary>
        /// Where the sourcemap file is written
        /// </summary>
        string SourceMapFilePath { get; set; }

        /// <summary>
        /// source map URL written in the .js file
        /// </summary>
        string JsSourceMappingUrl { get; set; }

        /// <summary>
        /// Source map's file field - .js file name
        /// </summary>
        string SourceMapFile { get; set; }

        /// <summary>
        /// Source map's sourceRoot field - location where the sources will be present if not ""
        /// </summary>
        string SourceMapSourceRoot { get; set; }

        /// <summary>
        /// Source map's sources field - list of sources that can be indexed in this source map
        /// </summary>
        string[] SourceMapSources { get; set; }

        /// <summary>
        /// /Source map's sourcesContent field - list of the sources' text to be embedded in the source map
        /// </summary>
        Optional<string[]> SourceMapSourcesContent { get; set; }

        /// <summary>
        /// Input source file (which one can use on program to get the file), 1:1 mapping with the sourceMapSources list
        /// </summary>
        string[] InputSourceFileNames { get; set; }

        /// <summary>
        /// Source map's names field - list of names that can be indexed in this source map
        /// </summary>
        Optional<string[]> SourceMapNames { get; set; }

        /// <summary>
        /// Source map's mapping field - encoded source map spans
        /// </summary>
        string SourceMapMappings { get; set; }

        /// <summary>
        /// /Raw source map spans that were encoded into the sourceMapMappings
        /// </summary>
        ISourceMapSpan[] SourceMapDecodedMappings { get; set; }
    }

    /// <summary>
    /// Return code used by getEmitOutput function to indicate status of the function
    /// </summary>
    public enum ExitStatus
    {
        /// <summary>
        /// Compiler ran successfully.  Either this was a simple do-nothing compilation (for example,
        /// when -version or -help was provided, or this was a normal compilation, no diagnostics
        /// were produced, and all outputs were generated successfully.
        /// </summary>
        Success = 0,

        /// <summary>
        /// Diagnostics were produced and because of them no code was generated.
        /// </summary>
        DiagnosticsPresentOutputsSkipped = 1,

        /// <summary>
        /// Diagnostics were produced and outputs were generated in spite of them.
        /// </summary>
        DiagnosticsPresentOutputsGenerated = 2,
    }

    /// <nodoc/>
    public interface IEmitResult
    {
        /// <nodoc/>
        bool EmitSkipped { get; set; }

        /// <nodoc/>
        Diagnostic[] Diagnostics { get; set; }

        /// <nodoc/>
        /* @internal */
        ISourceMapData[] SourceMaps { get; set; } // Array of sourceMapData if compiler emitted sourcemaps
    }

    /// <nodoc/>
    public abstract class TypeCheckerHost
    {
        /// <nodoc/>
        public abstract ICompilerOptions GetCompilerOptions();

        /// <nodoc/>
        public abstract ISourceFile[] GetSourceFiles();

        /// <nodoc/>
        public abstract ISourceFile GetSourceFile(string fileName);

        /// <summary>
        /// DScript-specific. If the filename is known by the host, returns its owning module
        /// </summary>
        public abstract bool TryGetOwningModule(string fileName, out ModuleName moduleName);

        /// <summary>
        /// DScript-specific. Returns whether the filename is owned by a module with implicit reference semantics.
        /// </summary>
        public bool IsOwnedByImplicitReferenceModule(string fileName)
        {
            ModuleName moduleName;
            return TryGetOwningModule(fileName, out moduleName) && moduleName.ProjectReferencesAreImplicit;
        }

        /// <summary>
        /// Whether fileName is part of the prelude.
        /// </summary>
        /// <remarks>
        /// Returns false if there is no prelude defined or if the file is not known to the host
        /// </remarks>
        public bool IsPartOfPreludeModule(string fileName)
        {
            Contract.Requires(!string.IsNullOrEmpty(fileName));

            ModuleName prelude;
            if (!TryGetPreludeModuleName(out prelude))
            {
                return false;
            }

            ModuleName owningModule;
            if (!TryGetOwningModule(fileName, out owningModule))
            {
                return false;
            }

            return prelude == owningModule;
        }

        /// <summary>
        /// DScript-specific. Returns if a prelude module has been identified. In that case <paramref name="preludeName"/> contains it
        /// </summary>
        public abstract bool TryGetPreludeModuleName(out ModuleName preludeName);

        /// <summary>
        /// DScript-specific. Returns true if a given file is part of the prelude.
        /// </summary>
        public virtual bool IsPreludeFile(ISourceFile sourceFile) => false;

        /// <summary>
        /// DScript-specific. Returns whether a prelude module is known to the host.
        /// </summary>
        public bool IsPreludeSpecified()
        {
            ModuleName prelude;
            return TryGetPreludeModuleName(out prelude);
        }

        /// <summary>
        /// Callback for Checker to call to report a completion of type checking a spec (along with the duration).
        /// </summary>
        public abstract void ReportSpecTypeCheckingCompleted(ISourceFile node, TimeSpan elapsed);
    }

    /// <nodoc/>
    public interface ITypeChecker
    {
        /// <summary>
        /// Returns a file name that corresponds to a module referenced by <paramref name="sourceFile"/>.
        /// </summary>
        [CanBeNull]
        string TryGetResolvedModulePath(ISourceFile sourceFile, string moduleName, HashSet<ISourceFile> filteredSpecs);

        /// <summary>
        /// Returns a set of files that depend on the current one.
        /// </summary>
        RoaringBitSet GetFileDependentsOf(ISourceFile sourceFile);

        /// <summary>
        /// Returns a set of files that the current file depend on.
        /// </summary>
        RoaringBitSet GetFileDependenciesOf(ISourceFile sourceFile);

        /// <summary>
        /// Returns a set of modules that the current file depends on.
        /// </summary>
        HashSet<string> GetModuleDependenciesOf(ISourceFile sourceFile);

        /// <summary>
        /// Returns whether a given declaration is defined as part of the prelude.
        /// </summary>
        bool IsPreludeDeclaration(IDeclaration declaration);

        /// <nodoc/>
        ISymbol ResolveEntryByName(INode currentNode, string name, SymbolFlags blockScopedVariable);

        /// <nodoc/>
        int GetCurrentNodeId();

        /// <nodoc/>
        int GetCurrentMergeId();

        /// <nodoc/>
        int GetCurrentSymbolId();

        /// <nodoc/>
        int GetNextTypeId();

        /// <nodoc/>
        int GetSymbolId(ISymbol symbol);

        /// <nodoc/>
        IType GetTypeOfSymbolAtLocation(ISymbol symbol, INode node);

        /// <nodoc/>
        IType GetDeclaredTypeOfSymbol(ISymbol symbol);

        /// <nodoc/>
        IReadOnlyList<ISymbol> GetPropertiesOfType(IType type);

        /// <nodoc/>
        ISymbol GetPropertyOfType(IType type, string propertyName);

        /// <nodoc/>
        IReadOnlyList<ISignature> GetSignaturesOfType(IType type, SignatureKind kind);

        /// <nodoc/>
        IType GetIndexTypeOfType(IType type, IndexKind kind);

        /// <nodoc/>
        IReadOnlyList<IType> GetBaseTypes(IInterfaceType type);

        /// <nodoc/>
        IType GetReturnTypeOfSignature(ISignature signature);

        /// <nodoc/>
        IReadOnlyList<ISymbol> GetSymbolsInScope(INode location, SymbolFlags meaning);

        /// <nodoc/>
        ISymbol GetSymbolAtLocation(INode node);

        /// <nodoc/>
        ISymbol GetShorthandAssignmentValueSymbol(INode location);

        /// <nodoc/>
        IType GetTypeAtLocation(INode node);

        /// <nodoc/>
        string TypeToString(IType type, INode enclosingDeclaration = null, TypeFormatFlags flags = TypeFormatFlags.None, IStringSymbolWriter symbolWriter = null);

        /// <nodoc/>
        string SymbolToString(ISymbol symbol, INode enclosingDeclaration = null, SymbolFlags meaning = SymbolFlags.None);

        /// <nodoc/>
        string GetFullyQualifiedName(ISymbol symbol);

        /// <nodoc/>
        IReadOnlyList<ISymbol> GetAugmentedPropertiesOfType(IType type);

        /// <nodoc/>
        List<ISymbol> GetRootSymbols(ISymbol symbol);

        /// <nodoc/>
        IType GetContextualType(IExpression node);

        /// <nodoc/>
        ISignature GetResolvedSignature(/*TODO: CallLikeExpression*/INode node);

        /// <nodoc/>
        ISignature GetSignatureFromDeclaration(ISignatureDeclaration declaration);

        /// <nodoc/>
        bool IsImplementationOfOverload(IFunctionLikeDeclaration node);

        /// <nodoc/>
        bool IsUndefinedSymbol(ISymbol symbol);

        /// <nodoc/>
        bool IsArgumentsSymbol(ISymbol symbol);

        /// <nodoc/>
        int? GetConstantValue(INode node /*EnumMember | PropertyAccessExpression | ElementAccessExpression node*/);

        /// <nodoc/>
        bool IsValidPropertyAccess(INode node, string propertyName);

        /// <nodoc/>
        ISymbol GetAliasedSymbol(ISymbol symbol, bool resolveAliasRecursively = true);

        /// <nodoc/>
        IReadOnlyList<ISymbol> GetExportsOfModuleAsArray(ISymbol moduleSymbol);

        /// <nodoc/>
        ISymbolTable GetExportsOfModule(ISymbol moduleSymbol);

        /// <nodoc/>
        bool IsOptionalParameter(IParameterDeclaration node);

        /// <summary>
        /// Should not be called directly.  Should only be accessed through the Program instance.
        /// </summary>
        /* @internal */
        List<Diagnostic> GetDiagnostics(ISourceFile sourceFile = null, CancellationToken cancellationToken = null);

        /// <nodoc/>
        /* @internal */
        List<Diagnostic> GetGlobalDiagnostics();

        /// <nodoc/>
        /* @internal */
        int GetNodeCount();

        /// <nodoc/>
        /* @internal */
        int GetIdentifierCount();

        /// <nodoc/>
        /* @internal */
        int GetSymbolCount();

        /// <nodoc/>
        /* @internal */
        int GetTypeCount();

        /// <nodoc/>
        bool IsTypeAssignableTo(IType source, IType target);

        /// <nodoc/>
        bool IsTypeIdenticalTo(IType source, IType target);

        /// <nodoc/>
        List<INode> CollectLinkedAliases(IIdentifier node);

        /// <nodoc/>
        ISymbolAccessiblityResult IsSymbolAccessible(ISymbol symbol, INode enclosingDeclaration, SymbolFlags meaning);
    }

    /// <nodoc/>
    public enum TypeFormatFlags
    {
        /// <summary>
        /// Empty flags.
        /// </summary>
        None = 0x00000000,

        /// <summary>
        /// Write Array&lt;T> instead T[]
        /// </summary>
        WriteArrayAsGenericType = 0x00000001,

        /// <summary>
        /// Write typeof instead of function type literal
        /// </summary>
        UseTypeOfFunction = 0x00000002,

        /// <summary>
        /// Don't truncate typeToString result
        /// </summary>
        NoTruncation = 0x00000004,

        /// <summary>
        /// Write arrow style signature
        /// </summary>
        WriteArrowStyleSignature = 0x00000008,

        /// <summary>
        /// Write symbol's own name instead of 'any' for any like types (eg. unknown, __resolving__ etc)
        /// </summary>
        WriteOwnNameForAnyLike = 0x00000010,

        /// <summary>
        /// Write the type arguments instead of type parameters of the signature
        /// </summary>
        WriteTypeArgumentsOfSignature = 0x00000020,

        /// <summary>
        /// Writing an array or union element type
        /// </summary>
        InElementType = 0x00000040,

        /// <summary>
        /// Write out the fully qualified type name (eg. Module.Type, instead of Type)
        /// </summary>
        UseFullyQualifiedType = 0x00000080,
    }

    /// <nodoc/>
    public enum SymbolFormatFlags
    {
        /// <summary>
        /// Empty flags.
        /// </summary>
        None = 0x00000000,

        /// <summary>
        /// Write symbols's type argument if it is instantiated symbol
        /// <![CDATA[
        /// eg. class C<T> { p: T }   <-- Show p as C<T>.p here
        ///     var public C<int> a { get; set; }
        ///     var p = a.p;  <--- Here p is property of C<int> so show it as C<int>.p instead of just C.p
        /// ]]>
        /// </summary>
        WriteTypeParametersOrArguments = 0x00000001,

        /// <summary>
        /// Use only external alias information to get the symbol name in the given context
        /// eg.  module m { export class c { } } import x = m.c;
        /// When this flag is specified m.c will be used to refer to the class instead of alias symbol x
        /// </summary>
        UseOnlyExternalAliasing = 0x00000002,
    }

    /// <nodoc/>
    /* @internal */
    public enum SymbolAccessibility
    {
        /// <nodoc/>
        Accessible,

        /// <nodoc/>
        NotAccessible,

        /// <nodoc/>
        CannotBeNamed,
    }

    /// <nodoc/>
    public enum TypePredicateKind
    {
        /// <nodoc/>
        This,

        /// <nodoc/>
        Identifier,
    }

    /// <nodoc/>
    public interface ITypePredicate
    {
        /// <nodoc/>
        TypePredicateKind Kind { get; set; }

        /// <nodoc/>
        IType Type { get; set; }
    }

    // @kind (TypePredicateKind.This)

    /// <nodoc/>
    public interface IThisTypePredicate : ITypePredicate
    {
        // public any _thisTypePredicateBrand { get; set; }
    }

    // @kind (TypePredicateKind.Identifier)

    /// <nodoc/>
    public interface IIdentifierTypePredicate : ITypePredicate
    {
        /// <nodoc/>
        string ParameterName { get; set; }

        /// <nodoc/>
        int? ParameterIndex { get; set; }
    }

    /// <nodoc/>
    /* @internal */
    public interface ISymbolVisibilityResult
    {
        /// <nodoc/>
        SymbolAccessibility Accessibility { get; set; }

        /// <nodoc/>
        List<AnyImportSyntax> AliasesToMakeVisible { get; } // aliases that need to have this symbol visible

        /// <nodoc/>
        string ErrorSymbolName { get; set; } // Optional symbol name that results in error

        /// <nodoc/>
        INode ErrorNode { get; set; } // optional node that results in error
    }

    /// <nodoc/>
    /* @internal */
    public interface ISymbolAccessiblityResult : ISymbolVisibilityResult
    {
        /// <nodoc/>
        string ErrorModuleName { get; set; } // If the symbol is not visible from module, module's name
    }

    /// <summary>
    /// Indicates how to serialize the name for a TypeReferenceNode when emitting decorator metadata
    /// </summary>
    /* @internal */
    public enum TypeReferenceSerializationKind
    {
        /// <summary>
        /// The TypeReferenceNode could not be resolved. The type name
        /// should be emitted using a safe fallback.
        /// </summary>
        Unknown,

        /// <summary>
        /// The TypeReferenceNode resolves to a type with a constructor
        /// function that can be reached at runtime (e.g. a `class`
        /// declaration or a `var` declaration for the static side
        /// of a type, such as the global `Promise` type in lib.d.ts).
        /// </summary>
        TypeWithConstructSignatureAndValue,

        /// <summary>
        /// The TypeReferenceNode resolves to a Void-like type.
        /// </summary>
        VoidType,

        /// <summary>
        /// The TypeReferenceNode resolves to a Number-like type.
        /// </summary>
        NumberLikeType,

        /// <summary>
        /// The TypeReferenceNode resolves to a String-like type.
        /// </summary>
        StringLikeType,

        /// <summary>
        /// The TypeReferenceNode resolves to a Boolean-like type.
        /// </summary>
        BooleanType,

        /// <summary>
        /// The TypeReferenceNode resolves to an Array-like type.
        /// </summary>
        ArrayLikeType,

        /// <summary>
        /// The TypeReferenceNode resolves to the ESSymbol type.
        /// </summary>
        EsSymbolType,

        /// <summary>
        /// The TypeReferenceNode resolves to a Function type or a typ
        /// with call signatures.
        /// </summary>
        TypeWithCallSignature,

        /// <summary>
        /// The TypeReferenceNode resolves to any other type.
        /// </summary>
        ObjectType,
    }

    /// <summary>
    /// Flags attached to a symbol.
    /// </summary>
    [Flags]
    public enum SymbolFlags : uint
    {
        /// <summary>
        /// No flags.
        /// </summary>
        None = 0,

        /// <summary>
        /// Variable (var) or parameter
        /// </summary>
        FunctionScopedVariable = 0x00000001,

        /// <summary>
        /// A block-scoped variable (let or const)
        /// </summary>
        BlockScopedVariable = 0x00000002,

        /// <summary>
        /// Property or enum member
        /// </summary>
        Property = 0x00000004,

        /// <summary>
        /// Enum member
        /// </summary>
        EnumMember = 0x00000008,

        /// <summary>
        /// Function
        /// </summary>
        Function = 0x00000010,  // Function

        /// <summary>
        /// Class
        /// </summary>
        Class = 0x00000020,  // Class

        /// <summary>
        /// Ingterface
        /// </summary>
        Interface = 0x00000040,  // Interface

        /// <summary>
        /// Const enum
        /// </summary>
        ConstEnum = 0x00000080,  // Const enum

        /// <summary>
        /// Regular (non-const) enum
        /// </summary>
        RegularEnum = 0x00000100,  // Enum

        /// <summary>
        /// Instantiated module
        /// </summary>
        ValueModule = 0x00000200,  // Instantiated module

        /// <summary>
        /// Uninstantiated module
        /// </summary>
        NamespaceModule = 0x00000400,  // Uninstantiated module

        /// <summary>
        /// Type literal
        /// </summary>
        TypeLiteral = 0x00000800,  // Type Literal

        /// <summary>
        /// Object literal
        /// </summary>
        ObjectLiteral = 0x00001000,  // Object Literal

        /// <summary>
        /// Method
        /// </summary>
        Method = 0x00002000,  // Method

        /// <summary>
        /// Constructor
        /// </summary>
        Constructor = 0x00004000,  // Constructor

        /// <summary>
        /// Get accessor
        /// </summary>
        GetAccessor = 0x00008000,  // Get accessor

        /// <summary>
        /// Set accessor
        /// </summary>
        SetAccessor = 0x00010000,  // Set accessor

        /// <summary>
        /// Call, construct or index signature
        /// </summary>
        Signature = 0x00020000,  // Call, construct, or index signature

        /// <summary>
        /// Type parameter
        /// </summary>
        TypeParameter = 0x00040000,  // Type parameter

        /// <summary>
        /// Type alias
        /// </summary>
        TypeAlias = 0x00080000,  // Type alias

        /// <summary>
        /// Exported value marker (see comment in declareModuleMember in binder)
        /// </summary>
        ExportValue = 0x00100000,

        /// <summary>
        /// Exported type marker (see comment in declareModuleMember in binder)
        /// </summary>
        ExportType = 0x00200000,

        /// <summary>
        /// Exported namespace marker (see comment in declareModuleMember in binder)
        /// </summary>
        ExportNamespace = 0x00400000,

        /// <summary>
        /// An alias for another symbol (see comment in isAliasSymbolDeclaration in checker)
        /// </summary>
        Alias = 0x00800000,

        /// <summary>
        /// Instantiated symbol
        /// </summary>
        Instantiated = 0x01000000,

        /// <summary>
        /// Merged symbol (created during program binding)
        /// </summary>
        Merged = 0x02000000,

        /// <summary>
        /// Transient symbol (created during type check)
        /// </summary>
        Transient = 0x04000000,

        /// <summary>
        /// Prototype property (no source representation)
        /// </summary>
        Prototype = 0x08000000,

        /// <summary>
        /// Property in union or intersection type
        /// </summary>
        SyntheticProperty = 0x10000000,

        /// <summary>
        /// Optional property
        /// </summary>
        Optional = 0x20000000,

        /// <summary>
        /// Export * declaration
        /// </summary>
        ExportStar = 0x40000000,

        /// <summary>
        /// DScript-specific. @@public decoration
        /// </summary>
        ScriptPublic = 0x80000000,

        /// <nodoc />
        Enum = RegularEnum | ConstEnum,

        /// <nodoc />
        Variable = FunctionScopedVariable | BlockScopedVariable,

        /// <nodoc />
        Value = Variable | Property | EnumMember | Function | Class | Enum | ValueModule | Method | GetAccessor | SetAccessor,

        /// <nodoc />
        Type = Class | Interface | Enum | TypeLiteral | ObjectLiteral | TypeParameter | TypeAlias,

        /// <nodoc />
        Namespace = ValueModule | NamespaceModule,

        /// <nodoc />
        Module = ValueModule | NamespaceModule,

        /// <nodoc />
        Accessor = GetAccessor | SetAccessor,

        /// <summary>
        /// Variables can be redeclared, but can not redeclare a block-scoped declaration with the
        /// same name, or any other value that is not a variable, e.g. ValueModule or Class
        /// </summary>
        FunctionScopedVariableExcludes = Value & ~FunctionScopedVariable,

        /// <summary>
        /// Block-scoped declarations are not allowed to be re-declared
        /// they can not merge with anything in the value space
        /// </summary>
        BlockScopedVariableExcludes = Value,

        /// <nodoc />
        ParameterExcludes = Value,

        /// <nodoc />
        PropertyExcludes = Value,

        /// <nodoc />
        EnumMemberExcludes = Value,

        /// <nodoc />
        FunctionExcludes = Value & ~(Function | ValueModule),

        /// <summary>
        /// class-interface mergability done in checker.ts
        /// </summary>
        ClassExcludes = (Value | Type) & ~(ValueModule | Interface),

        /// <nodoc />
        InterfaceExcludes = Type & ~(Interface | Class),

        /// <summary>
        /// regular enums merge only with regular enums and modules
        /// </summary>
        RegularEnumExcludes = (Value | Type) & ~(RegularEnum | ValueModule),

        /// <summary>
        /// const enums merge only with const enums
        /// </summary>
        ConstEnumExcludes = (Value | Type) & ~ConstEnum,

        /// <nodoc />
        ValueModuleExcludes = Value & ~(Function | Class | RegularEnum | ValueModule),

        /// <nodoc />
        NamespaceModuleExcludes = 0,

        /// <nodoc />
        MethodExcludes = Value & ~Method,

        /// <nodoc />
        GetAccessorExcludes = Value & ~SetAccessor,

        /// <nodoc />
        SetAccessorExcludes = Value & ~GetAccessor,

        /// <nodoc />
        TypeParameterExcludes = Type & ~TypeParameter,

        /// <nodoc />
        TypeAliasExcludes = Type,

        /// <nodoc />
        AliasExcludes = Alias,

        /// <nodoc />
        ModuleMember = Variable | Function | Class | Interface | Enum | Module | TypeAlias | Alias,

        /// <nodoc />
        ExportHasLocal = Function | Class | Enum | ValueModule,

        /// <nodoc />
        HasExports = Class | Enum | Module,

        /// <nodoc />
        HasMembers = Class | Interface | TypeLiteral | ObjectLiteral,

        /// <nodoc />
        BlockScoped = BlockScopedVariable | Class | Enum,

        /// <nodoc />
        PropertyOrAccessor = Property | Accessor,

        /* *** Commented out due to a Visual Studio bug, see SymbolFlagsHelper
        /// <nodoc />
        Export = ExportNamespace | ExportType | ExportValue,

        /// <summary>
        /// The set of things we consider semantically classifiable.  Used to speed up the LS during
        /// classification.
        /// </summary>
        /// @internal
        Classifiable = Class | Enum | TypeAlias | Interface | TypeParameter | Module,*/
    }

    /// <summary>
    /// This class was added due to a Visual Studio issue! Adding ScriptPublic to <see cref="SymbolFlags"/> makes the number
    /// of enum cases overflow an int. Switching the enum to have uint as the underlying type crashes VS!
    /// The problem seems to be caused by the computed members that are commented out above.
    /// Moving those members to this helper class avoids the issue :(
    /// </summary>
    public static class SymbolFlagsHelper
    {
        /// <nodoc />
        public static SymbolFlags Export()
        {
            return SymbolFlags.ExportNamespace | SymbolFlags.ExportType | SymbolFlags.ExportValue;
        }

        /// <nodoc />
        public static SymbolFlags Classifiable()
        {
            return SymbolFlags.Class | SymbolFlags.Enum | SymbolFlags.TypeAlias | SymbolFlags.Interface | SymbolFlags.TypeParameter | SymbolFlags.Module;
        }
    }

    /// <summary>
    /// Interface for a symbol.
    /// </summary>
    public interface ISymbol
    {
        /// <summary>
        /// Symbol flags
        /// </summary>
        SymbolFlags Flags { get; }

        /// <summary>
        /// Sets a given flags.
        /// </summary>
        /// <remarks>
        /// Originally this method was defined in the binder, but
        /// this prevented from controlling symbol's mutation.
        /// This method not only changes the flags property but also
        /// mutates some other properties based on a <paramref name="symbolFlags"/> value.
        /// </remarks>
        void SetDeclaration(SymbolFlags symbolFlags, [NotNull] IDeclaration declaration);

        /// <summary>
        /// Name of symbol
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Merge source symbol into the current one.
        /// </summary>
        /// <remarks>
        /// Originally this function was in the checker, but for thread-safety purposes it become
        /// a member function of a symbol.
        /// </remarks>
        void Merge(ISymbol source, Checker checker);

        /// <summary>
        /// Declarations associated with this symbol
        /// </summary>
        /// ThreadSafe: no mutation after construction.
        ReadOnlyList<IDeclaration> DeclarationList { get; }

        /// <summary>
        /// Declarations associated with this symbol.
        /// </summary>
        /// <remarks>Property is obsolete: use <see cref="DeclarationList"/>.</remarks>
        IReadOnlyList<IDeclaration> Declarations { get; }

        /// <summary>
        /// First value declaration of the symbol
        /// </summary>
        [CanBeNull]
        IDeclaration ValueDeclaration { get; }

        /// <summary>
        /// Class, interface or literal instance members
        /// </summary>
        [CanBeNull]
        ISymbolTable Members { get; }

        /// <summary>
        /// Module exports
        /// </summary>
        [CanBeNull]
        ISymbolTable Exports { get; }

        /// <summary>
        /// Unique id (used to look up SymbolLinks)
        /// </summary>
        /// <remarks>Internal, Threadsafe</remarks>
        int Id { get; }

        /// <summary>
        /// Merge id (used to look up merged symbol)
        /// </summary>
        /// <remarks>Internal, Threadsafe</remarks>
        int MergeId { get; }

        /// <summary>
        /// /Parent symbol
        /// </summary>
        /// <remarks>Internal, Threadsafe: changed only by the binder via ISymbol interface.</remarks>
        ISymbol Parent { get; set; }

        /// <summary>
        /// Exported symbol associated with this symbol
        /// </summary>
        /// <remarks>Internal, Threadsafe: changed only by the binder.</remarks>
        ISymbol ExportSymbol { get; set; }

        /// <summary>
        /// Whether this symbol represents a DScript (V2) module.
        /// </summary>
        /// <remarks>
        /// This flag could be part of SymbolFlags, but that enum has reached its uint limit and this flag is true
        /// in relatively few cases compared to the overall set of symbols. So keeping this separate
        /// so implementations can decide how to represent this and not blow up the enum size for all symbols.
        /// </remarks>
        bool IsBuildXLModule { get; }

        /// <summary>
        /// If this symbol is the cloned public version of a symbol, points to its original symbol. Null otherwise.
        /// </summary>
        ISymbol OriginalSymbol { get; }
    }

    /// <nodoc/>
    /// <remarks>Internal, Threadsafe (with potential double computation of some properties).</remarks>
    public interface ISymbolLinks
    {
        /// <summary>
        /// Recursively resolved (non-alias) target of an alias
        /// </summary>
        ISymbol Target { get; set; }

        /// <summary>
        /// Direct target (could be alias or non-alias) of an alias
        /// </summary>
        ISymbol DirectTarget { get; set; }

        /// <summary>
        /// Type of value symbol
        /// </summary>
        /// <remarks>
        /// Threadsafe
        /// </remarks>
        IType Type { get; set; }

        /// <summary>
        /// Type of class, interface, enum, type alias, or type parameter
        /// </summary>
        /// <remarks>
        /// Threadsafe
        /// </remarks>
        IType DeclaredType { get; set; }

        /// <summary>
        /// Type parameters of type alias (undefined if non-generic)
        /// </summary>
        /// <remarks>
        /// Threadsafe
        /// </remarks>
        IReadOnlyList<ITypeParameter> TypeParameters { get; set; }

        /// <summary>
        /// Type of an inferred ES5 class
        /// </summary>
        /// <remarks>
        /// Threadsafe
        /// </remarks>
        IType InferredClassType { get; set; }

        /// <summary>
        /// Instantiations of generic type alias (undefined if non-generic)
        /// </summary>
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly", Justification = "Necessary functionality")]
        Map<IType> Instantiations { get; set; }

        /// <summary>
        /// Type mapper for instantiation alias
        /// </summary>
        ITypeMapper Mapper { get; }

        /// <summary>
        /// True if alias symbol has been referenced as a value
        /// </summary>
        bool? Referenced { get; set; }

        /// <summary>
        /// Containing union or intersection type for synthetic property
        /// </summary>
        IUnionOrIntersectionType ContainingType { get; }

        /// <summary>
        /// Resolved exports of module
        /// </summary>
        ISymbolTable ResolvedExports { get; set; }

        /// <summary>
        /// True if exports of external module have been checked
        /// </summary>
        bool ExportsChecked { get; set; }

        /// <summary>
        /// True if symbol is block scoped redeclaration
        /// </summary>
        bool? IsNestedRedeclaration { get; set; }

        /// <summary>
        /// Binding element associated with property symbol
        /// </summary>
        IBindingElement BindingElement { get; }

        /// <summary>
        /// true if module exports some value (not just types)
        /// </summary>
        bool? ExportsSomeValue { get; set; }
    }

    /// <summary>
    /// Extra state for a symbol that allows to make symbols at least partially immutable after construction.
    /// </summary>
    public struct SymbolData
    {
        /// <nodoc />
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly", Justification = "Necessary functionality")]
        public List<IDeclaration> Declarations { get; set; }

        /// <nodoc />
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly", Justification = "Necessary functionality")]
        public IReadOnlyList<IDeclaration> ReadOnlyDeclarations { get; set; }

        /// <nodoc />
        public IBindingElement BindingElement { get; set; }

        /// <nodoc />
        public IUnionOrIntersectionType ContainingType { get; set; }

        /// <nodoc />
        public ITypeMapper Mapper { get; set; }

        /// <nodoc />
        public ISymbol Target { get; set; }

        /// <nodoc />
        [SuppressMessage("Microsoft.Naming", "CA1721:PropertyNamesShouldNotMatchGetMethods", Justification = "Type nomenclature is necessary within a compiler.")]
        public IType Type { get; set; }

        /// <nodoc />
        public ISymbol Parent { get; set; }

        /// <nodoc />
        public IDeclaration ValueDeclaration { get; set; }

        /// <nodoc />
        public bool ConstEnumOnlyModule { get; set; }

        /// <nodoc />
        public ISymbolTable Members { get; set; }

        /// <nodoc />
        public ISymbolTable Exports { get; set; }
    }

    /// <nodoc/>
    /* @internal */
    public interface ITransientSymbol : ISymbol, ISymbolLinks { }

    /// <nodoc/>
    /* @internal */
    [Flags]
    public enum NodeCheckFlags : ushort
    {
        /// <summary>
        /// No flags
        /// </summary>
        None = 0x00000000,

        /// <summary>
        /// Node has been type checked
        /// </summary>
        TypeChecked = 0x00000001,

        /// <summary>
        /// Lexical 'this' reference
        /// </summary>
        LexicalThis = 0x00000002,

        /// <summary>
        /// Lexical 'this' used in body
        /// </summary>
        CaptureThis = 0x00000004,

        /// <summary>
        /// Emit __extends
        /// </summary>
        EmitExtends = 0x00000008,

        /// <summary>
        /// Emit __decorate
        /// </summary>
        EmitDecorate = 0x00000010,

        /// <summary>
        /// Emit __param helper for decorators
        /// </summary>
        EmitParam = 0x00000020,

        /// <summary>
        /// Emit __awaiter
        /// </summary>
        EmitAwaiter = 0x00000040,

        /// <summary>
        /// Emit __generator
        /// </summary>
        EmitGenerator = 0x00000080,

        /// <summary>
        /// Instance 'super' reference
        /// </summary>
        SuperInstance = 0x00000100,

        /// <summary>
        /// Static 'super' reference
        /// </summary>
        SuperStatic = 0x00000200,

        /// <summary>
        /// Contextual types have been assigned
        /// </summary>
        ContextChecked = 0x00000400,

        /// <nodoc/>
        LexicalArguments = 0x00000800,

        /// <summary>
        /// Lexical 'arguments' used in body (for async functions)
        /// </summary>
        CaptureArguments = 0x00001000,

        /// <summary>
        /// Values for enum members have been computed, and any errors have been reported for them.
        /// </summary>
        EnumValuesComputed = 0x00002000,

        /// <nodoc/>
        BlockScopedBindingInLoop = 0x00004000,

        /// <summary>
        /// Instantiated lexical module declaration is merged with a previous class declaration.
        /// </summary>
        LexicalModuleMergesWithClass = 0x00008000,

        /// <summary>
        /// Loop that contains block scoped variable captured in closure
        /// </summary>
        /// <remarks>
        /// This is a dirty hack to fit into ushort boundaries.
        /// EmitAwaiter is never used in DScript so we're reusing the same bit to avoid switching to int.
        /// </remarks>
        LoopWithBlockScopedBindingCapturedInFunction = EmitAwaiter,
    }

    /// <nodoc/>
    /* @internal */
    public interface INodeLinks
    {
        /// <summary>
        /// Cached type of type node
        /// </summary>
        IType ResolvedType { get; set; }

        /// <summary>
        /// Cached awaited type of type node
        /// </summary>
        IType ResolvedAwaitedType { get; set; }

        /// <summary>
        /// Cached signature of signature node or call expression
        /// </summary>
        ISignature ResolvedSignature { get; set; }

        /// <summary>
        /// Cached name resolution result
        /// </summary>
        /// <remarks>
        /// This node is used only by the language service.
        /// See more detailed comment at <see cref="INode.ResolvedSymbol"/>
        /// </remarks>
        ISymbol ResolvedSymbolForIncrementalMode { get; set; }

        /// <summary>
        /// Set of flags specific to Node
        /// </summary>
        NodeCheckFlags Flags { get; set; }

        /// <summary>
        /// Constant value of enum member.
        /// -1 if the value is invalid.
        /// </summary>
        int EnumMemberValue { get; set; }

        // TODO: this field is never used and should be removed.

        /// <summary>
        /// Is this node visible
        /// </summary>
        bool? IsVisible { get; set; }

        /// <summary>
        /// Cache of assignment checks
        /// </summary>
        // TODO: in TS implementation this is a Map<bool>, but its keys are numeric,
        //       so in the port we can be more precise and make it a Dictionary<int, bool>
        Dictionary<int, bool> AssignmentChecks { get; }

        /// <summary>
        /// Cache bool if we report statements in ambient context
        /// </summary>
        // TODO: this field is never used and should be removed.
        bool? HasReportedStatementInAmbientContext { get; set; }
    }

    /// <nodoc/>
    [Flags]
    public enum TypeFlags
    {
        /// <nodoc/>
        // HINT: Added in TypeScript.Net migration to allow comparing against 0
        None = 0x00000000,

        /// <nodoc/>
        Any = 0x00000001,

        /// <nodoc/>
        String = 0x00000002,

        /// <nodoc/>
        Number = 0x00000004,

        /// <nodoc/>
        Boolean = 0x00000008,

        /// <nodoc/>
        Void = 0x00000010,

        /// <nodoc/>
        Undefined = 0x00000020,

        /// <nodoc/>
        Null = 0x00000040,

        /// <nodoc/>
        Enum = 0x00000080,  // Enum type

        /// <nodoc/>
        StringLiteral = 0x00000100,  // String literal type

        /// <nodoc/>
        TypeParameter = 0x00000200,  // Type parameter

        /// <nodoc/>
        Class = 0x00000400,  // Class

        /// <nodoc/>
        Interface = 0x00000800,  // Interface

        /// <nodoc/>
        Reference = 0x00001000,  // Generic type reference

        /// <nodoc/>
        Tuple = 0x00002000,  // Tuple

        /// <nodoc/>
        Union = 0x00004000,  // Union (T | U)

        /// <nodoc/>
        Intersection = 0x00008000,  // Intersection (T & U)

        /// <nodoc/>
        Anonymous = 0x00010000,  // Anonymous

        /// <nodoc/>
        Instantiated = 0x00020000,  // Instantiated anonymous type

        /// <nodoc/>
        /* @internal */
        FromSignature = 0x00040000,  // Created for signature assignment check

        /// <nodoc/>
        ObjectLiteral = 0x00080000,  // Originates in an object literal

        /// <nodoc/>
        /* @internal */
        FreshObjectLiteral = 0x00100000,  // Fresh object literal type

        /// <nodoc/>
        /* @internal */
        ContainsUndefinedOrNull = 0x00200000,  // Type is or contains Undefined or Null type

        /// <nodoc/>
        /* @internal */
        ContainsObjectLiteral = 0x00400000,  // Type is or contains object literal type

        /// <nodoc/>
        /* @internal */
        ContainsAnyFunctionType = 0x00800000,  // Type is or contains object literal type

        /// <nodoc/>
        EsSymbol = 0x01000000,  // Type of symbol primitive introduced in ES6

        /// <nodoc/>
        ThisType = 0x02000000,  // This type

        /// <nodoc/>
        ObjectLiteralPatternWithComputedProperties = 0x04000000,  // Object literal type implied by binding pattern has computed properties

        /// <nodoc/>
        PredicateType = 0x08000000,  // Predicate types are also Boolean types, but should not be considered Intrinsics - there's no way to capture this with flags

        /// <nodoc/>
        /* @internal */
        Intrinsic = Any | String | Number | Boolean | EsSymbol | Void | Undefined | Null,

        /// <nodoc/>
        /* @internal */
        Primitive = String | Number | Boolean | EsSymbol | Void | Undefined | Null | StringLiteral | Enum,

        /// <nodoc/>
        StringLike = String | StringLiteral,

        /// <nodoc/>
        NumberLike = Number | Enum,

        /// <nodoc/>
        ObjectType = Class | Interface | Reference | Tuple | Anonymous,

        /// <nodoc/>
        UnionOrIntersection = Union | Intersection,

        /// <nodoc/>
        StructuredType = ObjectType | Union | Intersection,

        /// <nodoc/>
        /* @internal */
        RequiresWidening = ContainsUndefinedOrNull | ContainsObjectLiteral | PredicateType,

        /// <nodoc/>
        /* @internal */
        PropagatingFlags = ContainsUndefinedOrNull | ContainsObjectLiteral | ContainsAnyFunctionType,
    }

    /// <summary>
    /// Properties common to all types
    /// </summary>
    public interface IType
    {
        /// <summary>
        /// Flags
        /// </summary>
        TypeFlags Flags { get; set; }

        /// <summary>
        /// Unique ID
        /// </summary>
        int Id { get; }

        /// <summary>
        /// Symbol associated with type (if any)
        /// </summary>
        [CanBeNull]
        ISymbol Symbol { get; }

        /// <summary>
        /// Destructuring pattern represented by type (if any)
        /// </summary>
        [CanBeNull]
        DestructuringPattern Pattern { get; set; }

        /// <summary>
        /// HINT: To model ObjectAllocator and getTypeConstructor() behavior,
        /// IType interface should have Initialize method that would be used to initialize
        /// initial state instead.
        /// </summary>
        void Initialize(ITypeChecker checker, TypeFlags flags, ISymbol symbol);

        /// <nodoc/>
        void Initialize(int id, TypeFlags flags, ISymbol symbol);
    }

    /// <summary>
    /// Intrinsic types (TypeFlags.Intrinsic)
    /// </summary>
    public interface IIntrinsicType : IType
    {
        /// <nodoc/>
        string IntrinsicName { get; set; } // Name of intrinsic type
    }

    /// <summary>
    /// Predicate types (TypeFlags.Predicate)
    /// </summary>
    public interface IPredicateType : IType
    {
        /// <summary>
        /// Type predicate
        /// </summary>
        // TODO: ThisTypePredicate | IdentifierTypePredicate
        ITypePredicate Predicate { get; set; }
    }

    /// <summary>
    /// String literal types (TypeFlags.StringLiteral)
    /// </summary>
    public interface IStringLiteralType : IType
    {
        /// <nodoc/>
        string Text { get; set; } // Text of string literal
    }

    /// <summary>
    /// Object types (TypeFlags.ObjectType)
    /// </summary>
    public interface IObjectType : IType
    {
        /// <summary>
        /// Resolve object type.
        /// </summary>
        [NotNull]
        IResolvedType Resolve([NotNull]ResolvedTypeData resolvedTypeData);
    }

    /// <summary>
    /// Additional state that is needed for resolved types.
    /// </summary>
    /// <remarks>
    /// This auxilary data structure helps to make type resolution atomic.
    /// </remarks>
    public sealed class ResolvedTypeData
    {
        /// <nodoc/>
        public ISymbolTable Members { get; set; }

        /// <nodoc/>
        public IReadOnlyList<ISymbol> Properties { get; set; }

        /// <nodoc/>
        public IReadOnlyList<ISignature> CallSignatures { get; set; }

        /// <nodoc/>
        public IReadOnlyList<ISignature> ConstructSignatures { get; set; }

        /// <nodoc/>
        public IType StringIndexType { get; set; }

        /// <nodoc/>
        public IType NumberIndexType { get; set; }

        /// <nodoc/>
        public IType IsolatedSignatureType { get; set; }
    }

    /// <nodoc/>
    public sealed class InterfaceDeclaredMembersData
    {
        /// <nodoc/>
        public IReadOnlyList<ISymbol> DeclaredProperties { get; set; }

        /// <nodoc/>
        public IReadOnlyList<ISignature> DeclaredCallSignatures { get; set; }

        /// <nodoc/>
        public IReadOnlyList<ISignature> DeclaredConstructSignatures { get; set; }

        /// <nodoc/>
        public IType DeclaredStringIndexType { get; set; }

        /// <nodoc/>
        public IType DeclaredNumberIndexType { get; set; }
    }

    /// <summary>
    /// Class and interface types (TypeFlags.Class and TypeFlags.Interface)
    /// </summary>
    public interface IInterfaceType : IObjectType
    {
        /// <nodoc />
        IInterfaceTypeWithDeclaredMembers ResolveDeclaredMembers(InterfaceDeclaredMembersData data);

        /// <summary>
        /// Type parameters (undefined if non-generic)
        /// </summary>
        IReadOnlyList<ITypeParameter> TypeParameters { get; }

        /// <summary>
        /// Outer type parameters (undefined if none)
        /// </summary>
        IReadOnlyList<ITypeParameter> OuterTypeParameters { get; }

        /// <summary>
        /// Local type parameters (undefined if none)
        /// </summary>
        IReadOnlyList<ITypeParameter> LocalTypeParameters { get; }

        /// <summary>
        /// The "this" type (undefined if none)
        /// </summary>
        ITypeParameter ThisType { get; }

        /// <summary>
        /// Resolved base constructor type of class
        /// </summary>
        /// <remarks>Internal</remarks>
        IType ResolvedBaseConstructorType { get; set; }

        /// <summary>
        /// Resolved base types
        /// </summary>
        /// <remarks>Internal</remarks>
        IReadOnlyList</*TODO: IObjectType*/IType> ResolvedBaseTypes { get; set; }
    }

    /// <nodoc/>
    public interface IInterfaceTypeWithDeclaredMembers : IInterfaceType
    {
        /// <summary>
        /// Declared members
        /// </summary>
        IReadOnlyList<ISymbol> DeclaredProperties { get; }

        /// <summary>
        /// Declared call signatures
        /// </summary>
        IReadOnlyList<ISignature> DeclaredCallSignatures { get; }

        /// <summary>
        /// Declared construct signatures
        /// </summary>
        IReadOnlyList<ISignature> DeclaredConstructSignatures { get; }

        /// <summary>
        /// Declared string index type
        /// </summary>
        IType DeclaredStringIndexType { get; }

        /// <summary>
        /// Declared numeric index type
        /// </summary>
        IType DeclaredNumberIndexType { get; }
    }

    /// <summary>
    /// Type references (TypeFlags.Reference). When a class or interface has type parameters or
    /// a "this" type, references to the class or interface are made using type references. The
    /// typeArguments property specififes the types to substitute for the type parameters of the
    /// class or interface and optionally includes an extra element that specifies the type to
    /// substitute for "this" in the resulting instantiation. When no extra argument is present,
    /// the type reference itself is substituted for "this". The typeArguments property is undefined
    /// if the class or interface has no type parameters and the reference isn't specifying an
    /// explicit "this" argument.
    /// </summary>
    public interface ITypeReference : IObjectType
    {
        /// <summary>
        /// Type reference target
        /// </summary>
        IGenericType Target { get; }

        /// <summary>
        /// Type reference type arguments (undefined if none)
        /// </summary>
        IReadOnlyList<IType> TypeArguments { get; }
    }

    /// <summary>
    /// Generic class and interface types
    /// </summary>
    public interface IGenericType : IInterfaceType, ITypeReference
    {
        /// <summary>
        /// Generic instantiation cache
        /// </summary>
        /* @internal */
        Map<ITypeReference> Instantiations { get; }

        /// <nodoc/>
        ITypeReference TypeReference { get; }
    }

    /// <nodoc/>
    public interface ITupleType : IObjectType // TODO: Should this be IType? (IObjectType is essentially just an alias for IType)
    {
        /// <nodoc/>
        IReadOnlyList<IType> ElementTypes { get; set; } // Element types
    }

    /// <nodoc/>
    public interface IUnionOrIntersectionType : IType
    {
        /// <summary>
        /// Constituent types
        /// </summary>
        ///
        List<IType> Types { get; }

        /// <summary>
        /// Reduced union type (all subtypes removed)
        /// </summary>
        /* @internal */
        IType ReducedType { get; set; }

        /// <summary>
        /// Cache of resolved properties
        /// </summary>
        /* @internal */
        ISymbolTable ResolvedProperties { get; set; }
    }

    /// <nodoc/>
    public interface IUnionType : IUnionOrIntersectionType { }

    /// <nodoc/>
    public interface IIntersectionType : IUnionOrIntersectionType { }

    /// <summary>
    /// An instantiated anonymous type has a target and a mapper
    /// </summary>
    /* @internal */
    public interface IAnonymousType : IObjectType // TODO: Should this be IType? (IObjectType is essentially just an alias for IType)
    {
        /// <summary>
        /// Instantiation target
        /// </summary>
        IAnonymousType Target { get; set; }

        /// <summary>
        /// Instantiation mapper
        /// </summary>
        ITypeMapper Mapper { get; set; }
    }

    /// <summary>
    /// Resolved object, union, or intersection type
    /// </summary>
    /* @internal */
    public interface IResolvedType : IObjectType, IUnionOrIntersectionType // TODO: Should this be IType? (IObjectType is essentially just an alias for IType)
    {
        /// <summary>
        /// Properties by name
        /// </summary>
        ISymbolTable Members { get; }

        /// <summary>
        /// Properties
        /// </summary>
        IReadOnlyList<ISymbol> Properties { get; }

        /// <summary>
        /// Call signatures of type
        /// </summary>
        [NotNull]
        IReadOnlyList<ISignature> CallSignatures { get; }

        /// <summary>
        /// Construct signatures of type
        /// </summary>
        [NotNull]
        IReadOnlyList<ISignature> ConstructSignatures { get; }

        /// <summary>
        /// String index type
        /// </summary>
        IType StringIndexType { get; }

        /// <summary>
        /// Numeric index type
        /// </summary>
        IType NumberIndexType { get; }
    }

    /// <summary>
    /// Object literals are initially marked fresh. Freshness disappears following an assignment,
    /// before a type assertion, or when when an object literal's type is widened. The regular
    /// version of a fresh type is identical except for the TypeFlags.FreshObjectLiteral flag.
    /// </summary>
    /* @internal */
    public interface IFreshObjectLiteralType : IResolvedType
    {
        /// <summary>
        /// Regular version of fresh type
        /// </summary>
        IResolvedType RegularType { get; set; }
    }

    /// <summary>
    /// Just a place to cache element types of iterables and iterators
    /// </summary>
    /* @internal */
    public interface IIterableOrIteratorType : IObjectType, IUnionType // TODO: Should this be IType? (IObjectType is essentially just an alias for IType)
    {
        /// <nodoc/>
        IType IterableElementType { get; set; }

        /// <nodoc/>
        IType IteratorElementType { get; set; }

        /// <nodoc/>
        IUnionType UnionType { get; set; }
    }

    /// <summary>
    /// Type parameters (TypeFlags.TypeParameter)
    /// </summary>
    public interface ITypeParameter : IType
    {
        /// <nodoc/>
        IType Constraint { get; set; } // Constraint

        /// <summary>
        /// Instantiation target
        /// </summary>
        /* @internal */
        ITypeParameter Target { get; set; }

        /// <summary>
        /// Instantiation mapper
        /// </summary>
        /* @internal */
        ITypeMapper Mapper { get; set; }

        /// <nodoc/>
        /* @internal */
        IType ResolvedApparentType { get; set; }
    }

    /// <nodoc/>
    public enum SignatureKind
    {
        /// <nodoc/>
        Call,

        /// <nodoc/>
        Construct,
    }

    /// <nodoc/>
    public interface ISignature
    {
        /// <summary>
        /// Originating declaration
        /// </summary>
        ISignatureDeclaration Declaration { get; }

        /// <summary>
        /// Type parameters (undefined if non-generic)
        /// </summary>
        IReadOnlyList<ITypeParameter> TypeParameters { get; }

        /// <nodoc/>
        IReadOnlyList<ISymbol> Parameters { get; } // Parameters

        /// <nodoc/>
        IType ResolvedReturnType { get; set; } // Resolved return type

        /// <summary>
        /// Number of non-optional parameters
        /// </summary>
        int MinArgumentCount { get; }

        /// <summary>
        /// True if last parameter is rest parameter
        /// </summary>
        bool HasRestParameter { get; }

        /// <summary>
        /// True if specialized
        /// </summary>
        bool HasStringLiterals { get; }

        /// <summary>
        /// Instantiation target
        /// </summary>
        ISignature Target { get; }

        /// <summary>
        /// Instantiation mapper
        /// </summary>
        ITypeMapper Mapper { get; }

        /// <summary>
        /// Underlying signatures of a union signature
        /// </summary>
        List<ISignature> UnionSignatures { get; }

        /// <summary>
        /// Erased version of signature (deferred)
        /// </summary>
        ISignature ErasedSignature { get; set; }

        /// <summary>
        /// A manufactured type that just contains the signature for purposes of signature comparison
        /// </summary>
        IObjectType IsolatedSignatureType { get; set; }
    }

    /// <nodoc/>
    public enum IndexKind
    {
        /// <nodoc/>
        String,

        /// <nodoc/>
        Number,
    }

    /// <nodoc/>
    /* @internal */
    public interface ITypeMapper
    {
        /// <nodoc/>
        Func<ITypeParameter /*t*/, IType> Mapper { get; }

        // TODO: The TypeScript implementation defines Instantiations as Type[], but its usage
        //       allows inserting values at arbitrary indices in the list.
        //       A dictionary serves this purpose better.

            /// <nodoc/>
        Dictionary<int, IType> Instantiations { get; } // Cache of instantiations created using this type mapper.

        /// <nodoc/>
        IInferenceContext Context { get; set; } // The inference context this mapper was created from.

        // The identity mapper and regular instantiation mappers do not need it.
        // Only inference mappers have this set (in createInferenceMapper).
    }

    /// <nodoc/>
    public sealed class TypeMapper : ITypeMapper
    {
        private Dictionary<int, IType> m_lazyInstantiations;

        /// <inheritdoc/>
        public Func<ITypeParameter, IType> Mapper { get; }

        /// <inheritdoc/>
        public Dictionary<int, IType> Instantiations
        {
            get
            {
                LazyInitializer.EnsureInitialized(ref m_lazyInstantiations, () => new Dictionary<int, IType>());

                return m_lazyInstantiations;
            }
        }

        /// <inheritdoc/>
        public IInferenceContext Context { get; set; }

        /// <nodoc/>
        public TypeMapper(Func<ITypeParameter, IType> mapper)
        {
            Mapper = mapper;
        }

        /// <nodoc/>
        public static ITypeMapper Create(Func<ITypeParameter, IType> mapper)
        {
            return new TypeMapper(mapper);
        }
    }

    /// <nodoc/>
    /* @internal */
    public interface ITypeInferences
    {
        /// <nodoc/>
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly", Justification = "Necessary functionality")]
        List<IType> Primary { get; set; } // Inferences made directly to a type parameter

        /// <nodoc/>
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly", Justification = "Necessary functionality")]
        List<IType> Secondary { get; set; } // Inferences made to a type parameter in a union type

        /// <nodoc/>
        bool IsFixed { get; set; } // Whether the type parameter is fixed, as defined in section 4.12.2 of the TypeScript spec

        // If a type parameter is fixed, no more inferences can be made for the type parameter
    }

    /// <nodoc/>
    /* @internal */
    public interface IInferenceContext
    {
        /// <nodoc/>
        IReadOnlyList<ITypeParameter> TypeParameters { get; set; } // Type parameters for which inferences are made

        /// <nodoc/>
        bool InferUnionTypes { get; set; } // Infer union types for disjoint candidates (otherwise undefinedType)

        /// <nodoc/>
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly", Justification = "Necessary functionality")]
        List<ITypeInferences> Inferences { get; } // Inferences made for each type parameter

        /// <nodoc/>
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly", Justification = "Necessary functionality")]
        List<IType> InferredTypes { get; } // Inferred type for each type parameter

        /// <nodoc/>
        ITypeMapper Mapper { get; set; } // Type mapper for this inference context

        /// <nodoc/>
        Optional<int> FailedTypeParameterIndex { get; set; } // Index of type parameter for which inference failed

        // It is optional because in contextual signature instantiation, nothing fails
    }

    /// <nodoc/>
    /* @internal */
    public enum SpecialPropertyAssignmentKind
    {
        /// <nodoc/>
        None,

        /// <summary>
        /// exports.Name = expr
        /// </summary>
        ExportsProperty,

        /// <summary>
        /// module.exports = expr
        /// </summary>
        ModuleExports,

        /// <summary>
        /// className.prototype.Name = expr
        /// </summary>
        PrototypeProperty,

        /// <summary>
        /// this.Name = expr
        /// </summary>
        ThisProperty,
    }

    /// <nodoc/>
    public enum ModuleResolutionKind
    {
        /// <nodoc/>
        Classic = 1,

        /// <nodoc/>
        NodeJs = 2,
    }

    /// <nodoc/>
    public interface ICompilerOptions
    {
        /// <nodoc/>
        Optional<bool> AllowNonTsExtensions { get; }

        /// <nodoc/>
        Optional<string> Charset { get; }

        /// <nodoc/>
        Optional<bool> Declaration { get; }

        /// <nodoc/>
        Optional<bool> Diagnostics { get; }

        /// <nodoc/>
        Optional<bool> EmitBom { get; }

        /// <nodoc/>
        Optional<bool> Help { get; }

        /// <nodoc/>
        Optional<bool> Init { get; }

        /// <nodoc/>
        Optional<bool> InlineSourceMap { get; }

        /// <nodoc/>
        Optional<bool> InlineSources { get; }

        /// <nodoc/>
        Optional<bool> ListFiles { get; }

        /// <nodoc/>
        Optional<string> Locale { get; }

        /// <nodoc/>
        Optional<string> MapRoot { get; }

        /// <nodoc/>
        Optional<ModuleKind> Module { get; }

        /// <nodoc/>
        Optional<NewLineKind> NewLine { get; }

        /// <nodoc/>
        Optional<bool> NoEmit { get; }

        /// <nodoc/>
        Optional<bool> NoEmitHelpers { get; }

        /// <nodoc/>
        Optional<bool> NoEmitOnError { get; }

        /// <nodoc/>
        bool NoErrorTruncation { get; }

        /// <nodoc/>
        Optional<bool> NoImplicitAny { get; }

        /// <nodoc/>
        Optional<bool> NoLib { get; }

        /// <nodoc/>
        Optional<bool> NoResolve { get; }

        /// <nodoc/>
        Optional<string> Out { get; }

        /// <nodoc/>
        Optional<string> OutFile { get; }

        /// <nodoc/>
        Optional<string> OutDir { get; }

        /// <nodoc/>
        Optional<bool> PreserveConstEnums { get; }

        /// <nodoc/>
        /* @internal */
        Optional<DiagnosticStyle> Pretty { get; }

        /// <nodoc/>
        Optional<string> Project { get; }

        /// <nodoc/>
        Optional<bool> RemoveComments { get; }

        /// <nodoc/>
        Optional<string> RootDir { get; }

        /// <nodoc/>
        Optional<bool> SourceMap { get; }

        /// <nodoc/>
        Optional<string> SourceRoot { get; }

        /// <nodoc/>
        Optional<bool> SuppressExcessPropertyErrors { get; }

        /// <nodoc/>
        Optional<bool> SuppressImplicitAnyIndexErrors { get; }

        /// <nodoc/>
        Optional<ScriptTarget> Target { get; }

        /// <nodoc/>
        Optional<bool> Version { get; }

        /// <nodoc/>
        Optional<bool> Watch { get; }

        /// <nodoc/>
        Optional<bool> IsolatedModules { get; }

        /// <nodoc/>
        Optional<bool> ExperimentalDecorators { get; }

        /// <nodoc/>
        Optional<bool> EmitDecoratorMetadata { get; }

        /// <nodoc/>
        Optional<ModuleResolutionKind> ModuleResolution { get; }

        /// <nodoc/>
        Optional<bool> AllowUnusedLabels { get; }

        /// <nodoc/>
        Optional<bool> AllowUnreachableCode { get; }

        /// <nodoc/>
        Optional<bool> NoImplicitReturns { get; }

        /// <nodoc/>
        Optional<bool> NoFallthroughCasesInSwitch { get; }

        /// <nodoc/>
        Optional<bool> ForceConsistentCasingInFileNames { get; }

        /// <nodoc/>
        Optional<bool> AllowSyntheticDefaultImports { get; }

        /// <nodoc/>
        Optional<bool> AllowJs { get; }

        /// <nodoc/>
        /* @internal */
        Optional<bool> StripInternal { get; }

        /// <nodoc/>
        // Skip checking lib.d.ts to help speed up tests.
        /* @internal */
        Optional<bool> SkipDefaultLibCheck { get; }

        // [option: string]: string | int | bool;
    }

    /// <nodoc/>
    public enum ModuleKind
    {
        /// <nodoc/>
        None = 0,

        /// <nodoc/>
        CommonJs = 1,

        /// <nodoc/>
        Amd = 2,

        /// <nodoc/>
        Umd = 3,

        /// <nodoc/>
        System = 4,

        /// <nodoc/>
        Es6 = 5,

        /// <nodoc/>
        Es2015 = Es6,
    }

    /// <nodoc/>
    public enum NewLineKind
    {
        /// <nodoc/>
        CarriageReturnLineFeed = 0,

        /// <nodoc/>
        LineFeed = 1,
    }

    /// <nodoc/>
    public readonly struct LineAndColumn : IEquatable<LineAndColumn>
    {
        /// <nodoc/>
        public int Line { get; }

        /// <summary>
        /// This value denotes the character position in line and is different from the 'column' because of tab characters.
        /// </summary>
        public int Character { get; }

        /// <nodoc/>
        public LineAndColumn(int line, int character)
        {
            Line = line;
            Character = character;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(
                Line.GetHashCode(),
                Character.GetHashCode());
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return (obj is LineAndColumn) && Equals((LineAndColumn)obj);
        }

        /// <inheritdoc/>
        public bool Equals(LineAndColumn other)
        {
            return Line == other.Line && Character == other.Character;
        }

        /// <nodoc/>
        public static bool operator ==(LineAndColumn x, LineAndColumn y)
        {
            return x.Equals(y);
        }

        /// <nodoc/>
        public static bool operator !=(LineAndColumn x, LineAndColumn y)
        {
            return !x.Equals(y);
        }

        /// <nodoc/>
        public static bool operator <(LineAndColumn x, LineAndColumn y)
        {
            return
                (x.Line < y.Line) ||
                (x.Line == y.Line && x.Character < y.Character);
        }

        /// <nodoc/>
        public static bool operator <=(LineAndColumn x, LineAndColumn y)
        {
            return (x < y) || (x == y);
        }

        /// <nodoc/>
        public static bool operator >(LineAndColumn x, LineAndColumn y)
        {
            return
                (x.Line > y.Line) ||
                (x.Line == y.Line && x.Character > y.Character);
        }

        /// <nodoc/>
        public static bool operator >=(LineAndColumn x, LineAndColumn y)
        {
            return (x > y) || (x == y);
        }
    }

    /// <nodoc/>
    public enum ScriptTarget
    {
        /// <nodoc/>
        Es3 = 0,

        /// <nodoc/>
        Es5 = 1,

        /// <nodoc/>
        Es6 = 2,

        /// <nodoc/>
        Es2015 = Es6,

        /// <nodoc/>
        Latest = Es6,
    }

    /// <nodoc/>
    public enum LanguageVariant
    {
        /// <nodoc/>
        Standard,

        /// <nodoc/>
        Jsx,
    }

    /// <nodoc/>
    /* @internal */
    public enum DiagnosticStyle
    {
        /// <nodoc/>
        Simple,

        /// <nodoc/>
        Pretty,
    }

    /// <nodoc/>
    public interface IParsedCommandLine
    {
        /// <nodoc/>
        ICompilerOptions Options { get; set; }

        /// <nodoc/>
        string[] FileNames { get; set; }

        /// <nodoc/>
        Diagnostic[] Errors { get; set; }
    }

    /// <nodoc/>
    /* @internal */
    public interface ICommandLineOptionBase
    {
        /// <nodoc/>
        string Name { get; }

        /// <nodoc/>
        Union<string, int, bool, Map<int>> Type { get; set; }

        /// <nodoc/>
        // type: "string" | "int" | "bool" | Map<int>;    // a value of a primitive type, or an object literal mapping named values to actual values
        Optional<bool> IsFilePath { get; set; } // True if option value is a path or path

        /// <nodoc/>
        Optional<string> ShortName { get; set; } // A short mnemonic for convenience - for instance, 'h' can be used in place of 'help'

        /// <nodoc/>
        Optional<IDiagnosticMessage> Description { get; set; } // The message describing what the command line switch does

        /// <nodoc/>
        Optional<IDiagnosticMessage> ParamType { get; set; } // The name to be used for a non-bool option's parameter

        /// <nodoc/>
        Optional<bool> Experimental { get; set; }
    }

    /// <nodoc/>
    /* @internal */
    public interface ICommandLineOptionOfPrimitiveType : ICommandLineOptionBase
    {
        /// <nodoc/>
        // TODO: type: "string" | "int" | "bool";
        new Union<string, int, bool> Type { get; set; }
    }

    /// <nodoc/>
    /* @internal */
    public interface ICommandLineOptionOfCustomType : ICommandLineOptionBase
    {
        /// <nodoc/>
        new Map<int> Type { get; } // an object literal mapping named values to actual values

        /// <nodoc/>
        IDiagnosticMessage Error { get; set; } // The error given when the argument does not fit a customized 'type'
    }

    internal class CommandLineOption : Union<ICommandLineOptionOfCustomType, ICommandLineOptionOfPrimitiveType> { }

    /// <nodoc/>
    public abstract class ModuleResolutionHost
    {
        /// <nodoc/>
        public abstract bool FileExists(string fileName);

        /// <summary>
        /// readFile function is used to read arbitrary text files on disk, i.e., when resolution procedure needs the content of 'package.json'
        /// to determine location of bundled typings for node module
        /// </summary>
        public abstract string ReadFile(string fileName);
    }

    /// <nodoc/>
    public interface IResolvedModule
    {
        /// <nodoc/>
        string ResolvedFileName { get; }

        /// <summary>
        /// Denotes if 'resolvedFileName' is isExternalLibraryImport and thus should be proper external module:
        /// - be a .d.ts file
        /// - use top level imports\exports
        /// - don't use tripleslash references
        /// </summary>
        bool IsExternalLibraryImport { get; }
    }

    /// <nodoc/>
    public interface IResolvedModuleWithFailedLookupLocations
    {
        /// <nodoc/>
        [CanBeNull]
        IResolvedModule ResolvedModule { get; }

        /// <nodoc/>
        string[] FailedLookupLocations { get; }
    }

    /// <nodoc/>
    public abstract class CompilerHost : ModuleResolutionHost
    {
        /// <nodoc/>
        public WriteFileCallback WriteFile { get; set; }

        /// <nodoc/>
        public abstract ISourceFile GetSourceFile(string fileName, ScriptTarget languageVersion, Action<string> onError);

        /// <nodoc/>
        public virtual CancellationToken GetCancellationToken() { return default(CancellationToken); }

        /// <nodoc/>
        public abstract string GetDefaultLibFileName(ICompilerOptions options);

        /// <nodoc/>
        public abstract string GetCurrentDirectory();

        /// <nodoc/>
        public abstract string GetCanonicalFileName(string fileName);

        /// <nodoc/>
        public abstract bool UseCaseSensitiveFileNames();

        /// <nodoc/>
        public abstract string GetNewLine();

        /// <summary>
        /// CompilerHost must either implement resolveModuleNames (in case if it wants to be completely in charge of
        /// module name resolution) or provide implementation for methods from ModuleResolutionHost (in this case compiler
        /// will appply built-in module resolution logic and use members of ModuleResolutionHost to ask host specific questions).
        /// If resolveModuleNames is implemented then implementation for members from ModuleResolutionHost can be just
        /// 'throw new Error("NotImplemented")'
        /// </summary>
        public virtual IResolvedModule[] ResolveModuleNames(string[] moduleNames, string containingFile)
        {
            return new IResolvedModule[] { };
        }
    }

    /// <nodoc/>
    internal interface IDiagnosticCollection
    {
        /// <summary>
        /// Adds a diagnostic to this diagnostic collection.
        /// </summary>
        void Add(Diagnostic diagnostic);

        /// <summary>
        /// Gets all the diagnostics that aren't associated with a file.
        /// </summary>
        List<Diagnostic> GetGlobalDiagnostics();

        /// <summary>
        /// If path is provided, gets all the diagnostics associated with that file name.
        /// Otherwise, returns all the diagnostics (global and file associated) in this colletion.
        /// </summary>
        List<Diagnostic> GetDiagnostics(string fileName = null);

        /// <summary>
        /// Gets a count of how many times this collection has been modified.  This value changes
        /// each time 'add' is called (regardless of whether or not an equivalent diagnostic was
        /// already in the collection).  As such, it can be used as a simple way to tell if any
        /// operation caused diagnostics to be returned by storing and comparing the return value
        /// of this method before/after the operation is performed.
        /// </summary>
        int GetModificationCount();
    }

    /// <nodoc/>
    internal interface ISymbolDisplayBuilder
    {
        /// <nodoc/>
        void BuildTypeDisplay(IType inputType, ISymbolWriter writer, INode enclosingDeclaration, TypeFormatFlags globalFlags);

        /// <nodoc/>
        void BuildSymbolDisplay(ISymbol symbol, ISymbolWriter writer, INode enclosingDeclaration, SymbolFlags meaning, SymbolFormatFlags flags = SymbolFormatFlags.None);

        /// <nodoc/>
        void BuildSignatureDisplay(ISignature signatures, ISymbolWriter writer, INode enclosingDeclaration, TypeFormatFlags flags, SignatureKind kind);

        /// <nodoc/>
        void BuildParameterDisplay(ISymbol parameter, ISymbolWriter writer, INode enclosingDeclaration, TypeFormatFlags flags);

        /// <nodoc/>
        void BuildTypeParameterDisplay(ITypeParameter tp, ISymbolWriter writer, INode enclosingDeclaration, TypeFormatFlags flags);

        /// <nodoc/>
        void BuildTypeParameterDisplayFromSymbol(ISymbol symbol, ISymbolWriter writer, INode enclosingDeclaration, TypeFormatFlags flags);

        /// <nodoc/>
        void BuildDisplayForParametersAndDelimiters(List<ISymbol> parameters, ISymbolWriter writer, INode enclosingDeclaration, TypeFormatFlags flags);

        /// <nodoc/>
        void BuildDisplayForTypeParametersAndDelimiters(List<ITypeParameter> typeParameters, ISymbolWriter writer, INode enclosingDeclaration, TypeFormatFlags flags);

        /// <nodoc/>
        void BuildReturnTypeDisplay(ISignature signature, ISymbolWriter writer, INode enclosingDeclaration, TypeFormatFlags flags);
    }

    /// <nodoc/>
    public interface ISymbolWriter
    {
        /// <nodoc/>
        void WriteKeyword(string text);

        /// <nodoc/>
        void WriteOperator(string text);

        /// <nodoc/>
        void WritePunctuation(string text);

        /// <nodoc/>
        void WriteSpace(string text);

        /// <nodoc/>
        void WriteStringLiteral(string text);

        /// <nodoc/>
        void WriteParameter(string text);

        /// <nodoc/>
        void WriteSymbol(string text, ISymbol symbol);

        /// <nodoc/>
        void WriteLine();

        /// <nodoc/>
        void IncreaseIndent();

        /// <nodoc/>
        void DecreaseIndent();

        /// <nodoc/>
        void Clear();

        /// <summary>
        /// Called when the symbol writer encounters a symbol to write.  Currently only used by the
        /// declaration emitter to help determine if it should patch up the final declaration file
        /// with import statements it previously saw (but chose not to emit).
        /// </summary>
        void TrackSymbol(ISymbol symbol, INode enclosingDeclaration, SymbolFlags meaning);

        /// <nodoc/>
        void ReportInaccessibleThisError();

        /// <summary>
        /// Whether <see cref="ReportInaccessibleThisError"/> was called
        /// </summary>
        bool IsInaccessibleErrorReported();
    }

    /// <nodoc/>
    public interface IStringSymbolWriter : ISymbolWriter
    {
        /// <nodoc/>
        string String();
    }

    /// <nodoc/>
    public static class SymbolWriterPool
    {
        internal class SymbolWriter : IStringSymbolWriter
        {
            private readonly ITypeChecker m_checker;
            private readonly StringBuilder m_builder = new StringBuilder();
            private bool m_inaccessibleErrorReported;

            public SymbolWriter([CanBeNull]ITypeChecker checker)
            {
                m_checker = checker;
                m_inaccessibleErrorReported = false;
            }

            public void Clear()
            {
                m_builder.Clear();
            }

            public void DecreaseIndent()
            { }

            public void IncreaseIndent()
            { }

            public void ReportInaccessibleThisError()
            {
                m_inaccessibleErrorReported = true;
            }

            public bool IsInaccessibleErrorReported()
            {
                return m_inaccessibleErrorReported;
            }

            public string String()
            {
                return m_builder.ToString();
            }

            public void TrackSymbol(ISymbol symbol, INode enclosingDeclaration, SymbolFlags meaning)
            {
                if (m_checker == null)
                {
                    return;
                }

                var result = m_checker.IsSymbolAccessible(symbol, enclosingDeclaration, meaning);
                if (result.Accessibility != SymbolAccessibility.Accessible)
                {
                    m_inaccessibleErrorReported = true;
                }
            }

            public void WriteKeyword(string text)
            {
                m_builder.Append(text);
            }

            public void WriteLine()
            {
                // Completely ignore indentation for string writers.  And map newlines to
                // a single space.
                m_builder.Append(" ");
            }

            public void WriteOperator(string text)
            {
                m_builder.Append(text);
            }

            public void WriteParameter(string text)
            {
                m_builder.Append(text);
            }

            public void WritePunctuation(string text)
            {
                m_builder.Append(text);
            }

            public void WriteSpace(string text)
            {
                m_builder.Append(text);
            }

            public void WriteStringLiteral(string text)
            {
                m_builder.Append(text);
            }

            public void WriteSymbol(string text, ISymbol symbol)
            {
                m_builder.Append(text);
            }
        }

        /// <nodoc/>
        // TODO:ST: use actual pool!
        public static IStringSymbolWriter GetSingleLineStringWriter([CanBeNull]ITypeChecker checker = null)
        {
            return new SymbolWriter(checker);
        }

        /// <nodoc />
        public static void ReleaseStringWriter(IStringSymbolWriter writer)
        {
            // DO nothing for now!
        }
    }
}
