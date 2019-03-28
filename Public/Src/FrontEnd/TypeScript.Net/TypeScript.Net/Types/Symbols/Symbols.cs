// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using TypeScript.Net.Core;
using TypeScript.Net.TypeChecking;

#pragma warning disable SA1649 // File name must match first type name

namespace TypeScript.Net.Types
{
    /// <nodoc/>
    public class Symbol : ISymbol
    {
        // Internal identifiers that assigned in a lock-free manner.
#pragma warning disable SA1307 // Accessible fields must begin with upper-case letter
        internal int m_id = InvalidId;
        internal int m_mergeId = InvalidId;
#pragma warning restore SA1307 // Accessible fields must begin with upper-case letter

        private SymbolExtraState m_extraState;

        private SymbolExtraState ExtraState => m_extraState ?? (m_extraState = new SymbolExtraState());

        /// <summary>
        /// A single declaration of the symbol.
        /// </summary>
        /// <remarks>
        /// In many cases symbol have just one declaration. In this case it will be stored in the following field but not in the list to
        /// reduce memory footprint.
        /// </remarks>
        private IDeclaration m_singleDeclaration;

        /// <nodoc />
        private List<IDeclaration> LazyDeclarations
        {
            get => m_extraState?.Declarations;
            set
            {
                if (value != null || m_extraState != null)
                {
                    ExtraState.Declarations = value;
                }
            }
        }

        /// <nodoc />
        protected IReadOnlyList<IDeclaration> ReadOnlyDeclarations
        {
            get => m_extraState?.ReadOnlyDeclarations;
            set
            {
                if (value != null || m_extraState != null)
                {
                    ExtraState.ReadOnlyDeclarations = value;
                }
            }
        }

        private void AddDeclaration(IDeclaration declaration)
        {
            // This method is called under the lock.
            if (m_singleDeclaration == null && LazyDeclarations == null && ReadOnlyDeclarations == null)
            {
                // Hot path: saving the declaration directly in the instance state.
                m_singleDeclaration = declaration;
            }
            else
            {
                var declarations = GetDeclarations();
                if (m_singleDeclaration != null)
                {
                    declarations.Add(m_singleDeclaration);
                    m_singleDeclaration = null;
                }

                declarations.Add(declaration);
            }
        }

        private List<IDeclaration> GetDeclarations()
        {
            // This method is called under the lock.
            if (LazyDeclarations == null)
            {
                if (ReadOnlyDeclarations != null)
                {
                    // In some cases a symbol was constructed with a list of existing definitions and that list
                    // needs to be extended by other definitions.
                    // In this case we need to copy all existing definitions into a mutable collection.
                    LazyDeclarations = ReadOnlyDeclarations.ToList();
                    ReadOnlyDeclarations = null;
                }
                else
                {
                    LazyDeclarations = new List<IDeclaration>(2);
                }
            }

            return LazyDeclarations;
        }

        /// <nodoc />
        public const int InvalidId = CoreUtilities.InvalidIdentifier;

        /// <inheritdoc/>
        public SymbolFlags Flags { get; internal set; } // Symbol flags

        /// <inheritdoc/>
        public void SetDeclaration(SymbolFlags symbolFlags, IDeclaration declaration)
        {
            var symbol = this;
            lock (symbol)
            {
                symbol.Flags |= symbolFlags;

                AddDeclaration(declaration);

                if ((symbolFlags & SymbolFlags.HasExports) != SymbolFlags.None && symbol.Exports == null)
                {
                    symbol.Exports = SymbolTable.Create();
                }

                if ((symbolFlags & SymbolFlags.HasMembers) != SymbolFlags.None && symbol.Members == null)
                {
                    symbol.Members = SymbolTable.Create();
                }

                if ((symbolFlags & SymbolFlags.Value) != SymbolFlags.None)
                {
                    var valueDeclaration = symbol.ValueDeclaration;

                    if (valueDeclaration == null ||
                        (valueDeclaration.Kind != declaration.Kind && valueDeclaration.Kind == SyntaxKind.ModuleDeclaration))
                    {
                        // other kinds of value declarations take precedence over modules
                        symbol.ValueDeclaration = declaration;
                    }
                }
            }
        }

        /// <inheritdoc/>
        public string Name { get; } // Name of symbol

        /// <inheritdoc/>
        public void Merge(ISymbol source, Checker checker)
        {
            lock (this)
            {
                var target = this;

                // Const enum modules are not supported.
                // if ((source.Flags & SymbolFlags.ValueModule) != SymbolFlags.None &&
                //        (target.Flags & SymbolFlags.ValueModule) != SymbolFlags.None &&
                //        target.ConstEnumOnlyModule.ValueOrDefault &&
                //        (!source.ConstEnumOnlyModule.ValueOrDefault))
                // {
                //    // reset flag when merging instantiated module into value module that has only const enums
                //    target.ConstEnumOnlyModule = false;
                // }
                target.Flags |= source.Flags;

                if (source.ValueDeclaration != null &&
                    (target.ValueDeclaration == null ||
                     (target.ValueDeclaration.Kind == SyntaxKind.ModuleDeclaration &&
                      source.ValueDeclaration.Kind != SyntaxKind.ModuleDeclaration)))
                {
                    // other kinds of value declarations take precedence over modules
                    target.ValueDeclaration = source.ValueDeclaration;
                }

                GetDeclarations().AddRange(source.DeclarationList);

                if (source.Members != null)
                {
                    checker.MergeSymbolTable(target.GetMembers(), source.Members);
                }

                if (source.Exports != null)
                {
                    checker.MergeSymbolTable(target.GetExports(), source.Exports);
                }
            }
        }

        /// <inheritdoc/>
        ReadOnlyList<IDeclaration> ISymbol.DeclarationList => m_singleDeclaration != null
            ? new ReadOnlyList<IDeclaration>(m_singleDeclaration)
            : new ReadOnlyList<IDeclaration>(ReadOnlyDeclarations ?? LazyDeclarations);

        /// <inheritdoc/>
        public IReadOnlyList<IDeclaration> Declarations => ((ISymbol)this).DeclarationList;

        // Declarations associated with this symbol

        /// <inheritdoc/>
        public IDeclaration ValueDeclaration { get; internal set; } // First value declaration of the symbol

        /// <inheritdoc/>
        public ISymbolTable Members { get; internal set; } // Class, interface or literal instance members

        /// <inheritdoc/>
        public ISymbolTable Exports { get; internal set; } // Module exports

        /// <inheritdoc/>
        public int Id => m_id; // Unique id (used to look up SymbolLinks)

        /// <inheritdoc/>
        public int MergeId => m_mergeId; // Merge id (used to look up merged symbol)

        /// <inheritdoc/>
        public ISymbol Parent { get; set; } // Parent symbol

        /// <inheritdoc/>
        public ISymbol ExportSymbol { get; set; } // Exported symbol associated with this symbol

        /// <nodoc/>
        protected Symbol(SymbolFlags flags, string name)
        {
            Flags = flags;
            Name = name;
        }

        /// <nodoc />
        protected Symbol(SymbolFlags flags, string name, ref SymbolData extraState)
        {
            Flags = flags;
            Name = name;

            LazyDeclarations = extraState.Declarations;
            ReadOnlyDeclarations = extraState.ReadOnlyDeclarations;
            Parent = extraState.Parent;
            ValueDeclaration = extraState.ValueDeclaration;
            Members = extraState.Members;
            Exports = extraState.Exports;
        }

        /// <nodoc/>
        public static Symbol Create(SymbolFlags flags, string name)
        {
            if ((flags & SymbolFlags.Transient) != SymbolFlags.None)
            {
                var extraState = default(SymbolData);
                return new TransientSymbol(flags, name, ref extraState);
            }

            return new Symbol(flags, name);
        }

        /// <nodoc />
        internal static Symbol Create(SymbolFlags flags, string name, ref SymbolData extraState)
        {
            if ((flags & SymbolFlags.Transient) != SymbolFlags.None)
            {
                return new TransientSymbol(flags, name, ref extraState);
            }

            return new Symbol(flags, name, ref extraState);
        }

        /// <summary>
        /// A regular symbol is never a DScript module. Create a <see cref="ExtendedSymbol"/> if that's the case.
        /// </summary>
        public virtual bool IsBuildXLModule => false;

        /// <summary>
        /// A regular symbol never has an original symbol associated with
        /// </summary>
        public virtual ISymbol OriginalSymbol => null;
    }

    /// <summary>
    /// Extra data associated to a symbol that is infrequent enough to not be part of the regular <see cref="Symbol"/>
    /// </summary>
    public class ExtendedSymbol : Symbol
    {
        /// <inheritdoc/>
        public override bool IsBuildXLModule { get; }

        /// <nodoc />
        protected ExtendedSymbol(SymbolFlags flags, string name, ref SymbolData extraState, bool isBuildXLModule)
            : base(flags, name, ref extraState)
        {
            IsBuildXLModule = isBuildXLModule;
        }

        /// <nodoc />
        internal static Symbol Create(SymbolFlags flags, string name, ref SymbolData extraState, bool isBuildXLModule)
        {
            return new ExtendedSymbol(flags, name, ref extraState, isBuildXLModule);
        }
    }

    /// <summary>
    /// Represents the public version of a symbol
    /// </summary>
    /// <remarks>
    /// Carries a pointer to the original symbol
    /// </remarks>
    public sealed class PublicSymbol : Symbol
    {
        /// <inheritdoc/>
        public override ISymbol OriginalSymbol{ get; }

        /// <nodoc />
        public PublicSymbol(SymbolFlags flags, string name, ref SymbolData extraState, ISymbol originalSymbol)
            : base(flags, name, ref extraState)
        {
            OriginalSymbol = originalSymbol;
        }

        /// <nodoc />
        internal static Symbol Create(SymbolFlags flags, string name, ref SymbolData extraState, ISymbol originalSymbol)
        {
            return new PublicSymbol(flags, name, ref extraState, originalSymbol);
        }
    }

    /// <nodoc/>
    public class TransientSymbol : Symbol, ITransientSymbol
    {
        /// <inheritdoc/>
        public IBindingElement BindingElement { get; }

        /// <inheritdoc/>
        public IUnionOrIntersectionType ContainingType { get; }

        /// <inheritdoc/>
        public IType DeclaredType { get; set; }

        /// <inheritdoc/>
        public bool ExportsChecked { get; set; }

        /// <inheritdoc/>
        public bool? ExportsSomeValue { get; set; }

        /// <inheritdoc/>
        public IType InferredClassType { get; set; }

        /// <inheritdoc/>
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly", Justification = "Necessary functionality")]
        public Map<IType> Instantiations { get; set; }

        /// <inheritdoc/>
        public bool? IsNestedRedeclaration { get; set; }

        /// <inheritdoc/>
        public ITypeMapper Mapper { get; }

        /// <inheritdoc/>
        public bool? Referenced { get; set; }

        /// <inheritdoc/>
        public ISymbolTable ResolvedExports { get; set; }

        /// <inheritdoc/>
        public ISymbol Target { get; set; }

        /// <inheritdoc/>
        public ISymbol DirectTarget { get; set; }

        /// <inheritdoc/>
        [SuppressMessage("Microsoft.Naming", "CA1721:PropertyNamesShouldNotMatchGetMethods", Justification = "Type nomenclature is necessary within a compiler.")]
        public IType Type { get; set; }

        /// <inheritdoc/>
        public IReadOnlyList<ITypeParameter> TypeParameters { get; set; }

        internal TransientSymbol(SymbolFlags flags, string name, ref SymbolData extraState)
            : base(flags, name, ref extraState)
        {
            BindingElement = extraState.BindingElement;
            ContainingType = extraState.ContainingType;

            Mapper = extraState.Mapper;
            Target = extraState.Target;
            Type = extraState.Type;
        }

        /// <nodoc/>
        internal static TransientSymbol Create(SymbolFlags flags, string name, SymbolData data)
        {
            return new TransientSymbol(flags, name, ref data);
        }
    }

    internal class SymbolExtraState
    {
        public IReadOnlyList<IDeclaration> ReadOnlyDeclarations { get; set; }

        public ISymbolTable Exports { get; set; }

        public ISymbolTable Members { get; set; }

#pragma warning disable CA2227 // Collection properties should be read only
        public List<IDeclaration> Declarations { get; set; }
#pragma warning restore CA2227 // Collection properties should be read only
    }
}
