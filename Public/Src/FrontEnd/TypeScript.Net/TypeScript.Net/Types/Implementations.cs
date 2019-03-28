// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Threading;
using static BuildXL.Utilities.FormattableStringEx;

#pragma warning disable SA1649 // File name must match first type name

namespace TypeScript.Net.Types
{
    /// <nodoc/>
    public sealed class FileReference : IFileReference
    {
        /// <inheritdoc/>
        public int Pos { get; set; }

        /// <inheritdoc/>
        public int End { get; set; }

        /// <inheritdoc/>
        public string FileName { get; set; }
    }

    /// <nodoc/>
    [DebuggerDisplay("{ToString(), nq}")]
    public sealed class TextSpan : ITextSpan
    {
        /// <inheritdoc/>
        public int Start { get; set; }

        /// <inheritdoc/>
        public int Length { get; set; }

        /// <inheritdoc/>
        public override string ToString()
        {
            return I($"[{Start}, {Start + Length}]");
        }
    }

    /// <nodoc/>
    public sealed class TextChangeRange : ITextChangeRange
    {
        /// <inheritdoc/>
        public ITextSpan Span { get; set; }

        /// <inheritdoc/>
        public int NewLength { get; set; }
    }

    /// <summary>
    /// Factory for creating new instance of the <see cref="INode"/>.
    /// </summary>
    public static class NodeFactory
    {
        /// <nodoc/>
        public static T Create<T>(SyntaxKind kind, int pos, int end) where T : INode, new()
        {
            var node = FastActivator<T>.Create();
            node.Initialize(kind, pos, end);
            return node;
        }
    }

    /// <nodoc/>
    public sealed class CommentRange : ICommentRange
    {
        /// <inheritdoc/>
        public int Pos { get; set; }

        /// <inheritdoc/>
        public int End { get; set; }

        /// <inheritdoc/>
        public Optional<bool> HasTrailingNewLine { get; set; }

        /// <inheritdoc/>
        public SyntaxKind Kind { get; set; }
    }

    /// <nodoc/>
    public class Signature : ISignature
    {
        /// <inheritdoc/>
        public ISignatureDeclaration Declaration { get; set; } // Originating declaration

        /// <inheritdoc/>
        public IReadOnlyList<ITypeParameter> TypeParameters { get; set; } // Type parameters (undefined if non-generic)

        /// <inheritdoc/>
        public IReadOnlyList<ISymbol> Parameters { get; set; } // Parameters

        /// <inheritdoc/>
        public IType ResolvedReturnType { get; set; } // Resolved return type

        /// <inheritdoc/>
        public int MinArgumentCount { get; set; } // Number of non-optional parameters

        /// <inheritdoc/>
        public bool HasRestParameter { get; set; } // True if last parameter is rest parameter

        /// <inheritdoc/>
        public bool HasStringLiterals { get; set; } // True if specialized

        /// <inheritdoc/>
        public ISignature Target { get; set; } // Instantiation target

        /// <inheritdoc/>
        public ITypeMapper Mapper { get; set; } // Instantiation mapper

        /// <inheritdoc/>
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly", Justification = "Necessary functionality")]
        public List<ISignature> UnionSignatures { get; set; } // Underlying signatures of a union signature

        /// <inheritdoc/>
        public ISignature ErasedSignature { get; set; } // Erased version of signature (deferred)

        /// <inheritdoc/>
        public IObjectType IsolatedSignatureType { get; set; } // A manufactured type that just contains the signature for purposes of signature comparison

        /// <nodoc/>
        private Signature()
        {
        }

        /// <nodoc/>
        public static Signature Create(List<ISignature> signatures = null)
        {
            return new Signature()
            {
                UnionSignatures = signatures,
            };
        }
    }

    // TypeChecker types.
    internal class SymbolLinks : ISymbolLinks
    {
        private volatile ISymbol m_target;
        private volatile ISymbol m_directTarget;
        private volatile IType m_type;
        private volatile IType m_declaredType;
        private volatile IReadOnlyList<ITypeParameter> m_typeParameters;
        private volatile IType m_inferredClassType;
        private volatile Map<IType> m_instantiations;
        private volatile ITypeMapper m_mapper;

        // TODO: consider using enum with 3 states instead.
        private bool? m_referenced;
        private volatile IUnionOrIntersectionType m_containingType;
        private volatile ISymbolTable m_resolvedExports;
        private volatile bool m_exportsChecked;
        private bool? m_isNestedRedeclaration;
        private volatile IBindingElement m_bindingElement;
        private bool? m_exportsSomeValue;

        public ISymbol Target
        {
            get { return m_target; }
            set { m_target = value; }
        }

        public ISymbol DirectTarget
        {
            get { return m_directTarget; }
            set { m_directTarget = value; }
        }

        [SuppressMessage("Microsoft.Naming", "CA1721:PropertyNamesShouldNotMatchGetMethods", Justification = "Type nomenclature is necessary within a compiler.")]
        public IType Type
        {
            get { return m_type; }
            set { m_type = value; }
        }

        public IType DeclaredType
        {
            get { return m_declaredType; }
            set { m_declaredType = value; }
        }

        public IReadOnlyList<ITypeParameter> TypeParameters
        {
            get { return m_typeParameters; }
            set { m_typeParameters = value; }
        }

        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly", Justification = "Necessary functionality")]
        public IType InferredClassType
        {
            get { return m_inferredClassType; }
            set { m_inferredClassType = value; }
        }

        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly", Justification = "Necessary functionality")]
        public Map<IType> Instantiations
        {
            get { return m_instantiations; }
            set { m_instantiations = value; }
        }

        public ITypeMapper Mapper
        {
            get { return m_mapper; }
            set { m_mapper = value; }
        }

#pragma warning disable SA1501 // Statement must not be on a single line
        public bool? Referenced
        {
            get { lock (this) { return m_referenced; } }
            set { lock (this) { m_referenced = value; } }
        }

        public IUnionOrIntersectionType ContainingType
        {
            get { return m_containingType; }
            set { m_containingType = value; }
        }

        public ISymbolTable ResolvedExports
        {
            get { return m_resolvedExports; }
            set { m_resolvedExports = value; }
        }

        public bool ExportsChecked
        {
            get { return m_exportsChecked; }
            set { m_exportsChecked = value; }
        }

        public bool? IsNestedRedeclaration
        {
            get { lock (this) { return m_isNestedRedeclaration; } }
            set { lock (this) { m_isNestedRedeclaration = value; } }
        }

        public IBindingElement BindingElement
        {
            get { return m_bindingElement; }
            set { m_bindingElement = value; }
        }

        public bool? ExportsSomeValue
        {
            get { lock (this) { return m_exportsSomeValue; } }
            set { lock (this) { m_exportsSomeValue = value; } }
        }
#pragma warning restore SA1501 // Statement must not be on a single line
    }

    /// <summary>
    /// Special enum type that represents bool?.
    /// </summary>
    /// <remarks>
    /// This internal enum reduces memory footprint because it fits into a byte, and bool? will ocupy 4 or 8 bytes.
    /// </remarks>
    internal enum NullableBool : byte
    {
        Null,
        True,
        False,
    }

    internal static class NullableBoolExtensions
    {
        public static bool? AsBool(this NullableBool value)
        {
            switch (value)
            {
                case NullableBool.Null:
                    return null;
                case NullableBool.True:
                    return true;
                case NullableBool.False:
                    return false;
                default:
                    throw new ArgumentOutOfRangeException(nameof(value), value, null);
            }
        }

        public static NullableBool AsNullableBool(this bool? value)
        {
            if (value == null)
            {
                return NullableBool.Null;
            }

            return value == true ? NullableBool.True : NullableBool.False;
        }
    }

    internal class NodeLinks : INodeLinks
    {
        private Dictionary<int, bool> m_assignmentChecks;
        private volatile IType m_resolvedType;
        private volatile ISignature m_resolvedSignature;

        private volatile ISymbol m_resolvedSymbol;

        // This field order is very important.
        // The CLR will pack them in one 8 bytes chunk. Switching them around will increase memory footprint.
        private volatile int m_enumMemberValue = -1;
        private volatile NodeCheckFlags m_flags;
        private volatile NullableBool m_hasReportedStatementInAmbientContext;

        /// <nodoc />
        public object SyncRoot => this;

        /// <inheritdoc />
        public IType ResolvedType
        {
            get { return m_resolvedType; }
            set { m_resolvedType = value; }
        }

        /// <inheritdoc />
        // This field is never used in DScript
        public IType ResolvedAwaitedType
        {
            get { return null; }
            set { }
        }

        /// <inheritdoc />
        public ISignature ResolvedSignature
        {
            get { return m_resolvedSignature; }
            set { m_resolvedSignature = value; }
        }

        /// <inheritdoc />
        public ISymbol ResolvedSymbolForIncrementalMode
        {
            get { return m_resolvedSymbol; }
            set { m_resolvedSymbol = value; }
        }

        /// <inheritdoc />
        public NodeCheckFlags Flags
        {
            get { return m_flags; }
            set { m_flags = value; }
        }

        /// <inheritdoc />
        public int EnumMemberValue
        {
            get { return m_enumMemberValue; }
            set { m_enumMemberValue = value; }
        }

        /// <inheritdoc />
        public bool? HasReportedStatementInAmbientContext
        {
            get { return m_hasReportedStatementInAmbientContext.AsBool(); }
            set { m_hasReportedStatementInAmbientContext = value.AsNullableBool(); }
        }

        /// <inheritdoc />
        // This field is nver used (except for assigning) in DScript.
        // TODO: consider removing it.
        public bool? IsVisible
        {
            get { return null; }
            set { }
        }

        /// <inheritdoc />
        public Dictionary<int, bool> AssignmentChecks
        {
            get
            {
                LazyInitializer.EnsureInitialized(ref m_assignmentChecks, () => new Dictionary<int, bool>());
                return m_assignmentChecks;
            }
        }
    }

    internal class SymbolVisibilityResult : ISymbolVisibilityResult
    {
        public SymbolAccessibility Accessibility { get; set; }

        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly", Justification = "Necessary functionality")]
        public List<AnyImportSyntax> AliasesToMakeVisible { get; set; }

        public INode ErrorNode { get; set; }

        public string ErrorSymbolName { get; set; }
    }

    internal class SymbolAccessibilityResult : SymbolVisibilityResult, ISymbolAccessiblityResult
    {
        public string ErrorModuleName { get; set; }
    }

    /// <nodoc/>
    public class CompilerOptions : ICompilerOptions
    {
        /// <summary>
        /// Empty compiler options.
        /// </summary>
        public static ICompilerOptions Empty { get; } = new CompilerOptions();

        /// <inheritdoc/>
        public Optional<bool> AllowNonTsExtensions { get; set; }

        /// <inheritdoc/>
        public Optional<string> Charset { get; set; }

        /// <inheritdoc/>
        public Optional<bool> Declaration { get; set; }

        /// <inheritdoc/>
        public Optional<bool> Diagnostics { get; set; }

        /// <inheritdoc/>
        public Optional<bool> EmitBom { get; set; }

        /// <inheritdoc/>
        public Optional<bool> Help { get; set; }

        /// <inheritdoc/>
        public Optional<bool> Init { get; set; }

        /// <inheritdoc/>
        public Optional<bool> InlineSourceMap { get; set; }

        /// <inheritdoc/>
        public Optional<bool> InlineSources { get; set; }

        /// <inheritdoc/>
        public Optional<bool> ListFiles { get; set; }

        /// <inheritdoc/>
        public Optional<string> Locale { get; set; }

        /// <inheritdoc/>
        public Optional<string> MapRoot { get; set; }

        /// <inheritdoc/>
        public Optional<ModuleKind> Module { get; set; }

        /// <inheritdoc/>
        public Optional<NewLineKind> NewLine { get; set; }

        /// <inheritdoc/>
        public Optional<bool> NoEmit { get; set; }

        /// <inheritdoc/>
        public Optional<bool> NoEmitHelpers { get; set; }

        /// <inheritdoc/>
        public Optional<bool> NoEmitOnError { get; set; }

        /// <inheritdoc/>
        public bool NoErrorTruncation { get; set; }

        /// <inheritdoc/>
        public Optional<bool> NoImplicitAny { get; set; }

        /// <inheritdoc/>
        public Optional<bool> NoLib { get; set; }

        /// <inheritdoc/>
        public Optional<bool> NoResolve { get; set; }

        /// <inheritdoc/>
        public Optional<string> Out { get; set; }

        /// <inheritdoc/>
        public Optional<string> OutFile { get; set; }

        /// <inheritdoc/>
        public Optional<string> OutDir { get; set; }

        /// <inheritdoc/>
        public Optional<bool> PreserveConstEnums { get; set; }

        /// <inheritdoc/>
        /* @internal */
        public Optional<DiagnosticStyle> Pretty { get; set; }

        /// <inheritdoc/>
        public Optional<string> Project { get; set; }

        /// <inheritdoc/>
        public Optional<bool> RemoveComments { get; set; }

        /// <inheritdoc/>
        public Optional<string> RootDir { get; set; }

        /// <inheritdoc/>
        public Optional<bool> SourceMap { get; set; }

        /// <inheritdoc/>
        public Optional<string> SourceRoot { get; set; }

        /// <inheritdoc/>
        public Optional<bool> SuppressExcessPropertyErrors { get; set; }

        /// <inheritdoc/>
        public Optional<bool> SuppressImplicitAnyIndexErrors { get; set; }

        /// <inheritdoc/>
        public Optional<ScriptTarget> Target { get; set; }

        /// <inheritdoc/>
        public Optional<bool> Version { get; set; }

        /// <inheritdoc/>
        public Optional<bool> Watch { get; set; }

        /// <inheritdoc/>
        public Optional<bool> IsolatedModules { get; set; }

        /// <inheritdoc/>
        public Optional<bool> ExperimentalDecorators { get; set; }

        /// <inheritdoc/>
        public Optional<bool> EmitDecoratorMetadata { get; set; }

        /// <inheritdoc/>
        public Optional<ModuleResolutionKind> ModuleResolution { get; set; }

        /// <inheritdoc/>
        public Optional<bool> AllowUnusedLabels { get; set; }

        /// <inheritdoc/>
        public Optional<bool> AllowUnreachableCode { get; set; }

        /// <inheritdoc/>
        public Optional<bool> NoImplicitReturns { get; set; }

        /// <inheritdoc/>
        public Optional<bool> NoFallthroughCasesInSwitch { get; set; }

        /// <inheritdoc/>
        public Optional<bool> ForceConsistentCasingInFileNames { get; set; }

        /// <inheritdoc/>
        public Optional<bool> AllowSyntheticDefaultImports { get; set; }

        /// <inheritdoc/>
        public Optional<bool> AllowJs { get; set; }

        /// <nodoc/>
        /* @internal */
        public Optional<bool> StripInternal { get; set; }

        /// <summary>
        /// Skip checking lib.d.ts to help speed up tests.
        /// </summary>
        /* @internal */
        public Optional<bool> SkipDefaultLibCheck { get; set; }

        // [option: string]: string | int | bool;
    }

    /// <nodoc/>
    public sealed class InferenceContext : IInferenceContext
    {
        /// <inheritdoc/>
        public Optional<int> FailedTypeParameterIndex { get; set; }

        /// <inheritdoc/>
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly", Justification = "Necessary functionality")]
        public List<ITypeInferences> Inferences { get; set; }

        /// <inheritdoc/>
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly", Justification = "Necessary functionality")]
        public List<IType> InferredTypes { get; set; }

        /// <inheritdoc/>
        public bool InferUnionTypes { get; set; }

        /// <inheritdoc/>
        public ITypeMapper Mapper { get; set; }

        /// <inheritdoc/>
        public IReadOnlyList<ITypeParameter> TypeParameters { get; set; }
    }

    /// <nodoc/>
    public sealed class TypeInferences : ITypeInferences
    {
        /// <inheritdoc/>
        public bool IsFixed { get; set; }

        /// <inheritdoc/>
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly", Justification = "Necessary functionality")]
        public List<IType> Primary { get; set; }

        /// <inheritdoc/>
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly", Justification = "Necessary functionality")]
        public List<IType> Secondary { get; set; }
    }

    /// <nodoc/>
    public sealed class ResolvedModule : IResolvedModule
    {
        /// <nodoc/>
        public ResolvedModule(string resolvedFileName, bool isExternaLibraryImport)
        {
            Contract.Requires(!string.IsNullOrEmpty(resolvedFileName));

            ResolvedFileName = resolvedFileName;
            IsExternalLibraryImport = isExternaLibraryImport;
        }

        /// <inheritdoc/>
        public string ResolvedFileName { get; }

        /// <inheritdoc/>
        public bool IsExternalLibraryImport { get; }
    }
}
