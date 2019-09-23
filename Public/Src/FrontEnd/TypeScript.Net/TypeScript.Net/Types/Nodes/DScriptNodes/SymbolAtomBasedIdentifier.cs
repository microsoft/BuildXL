// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities;
using JetBrains.Annotations;
using TypeScript.Net.Api;
using TypeScript.Net.Reformatter;
using static TypeScript.Net.Types.Literal;

namespace TypeScript.Net.Types
{
    /// <summary>
    /// Memory efficient implementation of <see cref="IIdentifier"/> interface.
    /// </summary>
    /// <remarks>
    /// Unlike other types in this folder, <see cref="SymbolAtomBasedIdentifier"/> have to inherit from <see cref="NodeBase"/>
    /// because instances of this type could have lazily created NodeId that is managed via <see cref="NodeBase"/> instance.
    /// </remarks>
    public sealed partial class SymbolAtomBasedIdentifier : NodeBase, IVisitableNode, IIdentifier
    {
        private ISymbol m_resolvedSymbol;
        private INode m_parent;

        // The CLR has an issue with struct alignment. Using SymbolAtom here will cause aligning issue and the following
        // field will be aligned on 8 byte but not 4 bytes boundaries.
        private int m_symbolAtomValue;

        /// <nodoc />
        [NotNull]
        public PathTable PathTable => GetSourceFilePathTable(this);

        /// <nodoc/>
        public SymbolAtom Name
        {
            get { return SymbolAtom.UnsafeCreateFrom(StringId.UnsafeCreateFrom(m_symbolAtomValue)); }
            internal set { m_symbolAtomValue = value.StringId.Value; }
        }

        /// <inheritdoc />
        public string Text
        {
            get => Name.IsValid ? Name.ToString(PathTable.StringTable) : null;
            set { throw NotSupported(); }
        }

        /// <inheritdoc/>
        public ISymbol ResolvedSymbol { get { return m_resolvedSymbol; } set { m_resolvedSymbol = value; } }

        /// <inheritdoc/>
        public SyntaxKind OriginalKeywordKind
        {
            get { return (SyntaxKind)m_unused; }
            set { m_unused = (byte)value; }
        }

        /// <inheritdoc/>
        public SyntaxKind Kind
        {
            get { return m_kind; }
            set { m_kind = value; }
        }

        /// <inheritdoc/>
        public NodeFlags Flags
        {
            get => NodeFlags.None;
            set
            {
                if (value != NodeFlags.None)
                {
                    throw NotSupported();
                }
            }
        }

        /// <inheritdoc/>
        public NodeArray<IDecorator> Decorators
        {
            get => null;
            set
            {
                if (!value.IsNullOrEmpty())
                {
                    throw NotSupported();
                }
            }
        }

        /// <inheritdoc/>
        public ModifiersArray Modifiers
        {
            get => null;
            set
            {
                if (value.IsNullOrEmpty())
                {
                    throw NotSupported();
                }
            }
        }

        /// <inheritdoc/>
        public INode Parent { get { return m_parent; } set { m_parent = value; } }

        /// <inheritdoc/>
        public ISourceFile SourceFile
        {
            get => this.GetSourceFileSlow();
            set { }
        }

        /// <inheritdoc/>
        public ISymbolTable Locals
        {
            get => null;
            set { throw NotSupported(); }
        } // Symbol declared by node (initialized by binding)// Locals associated with node (initialized by binding)

        /// <inheritdoc/>
        public ISymbol Symbol
        {
            get => null;
            set => throw NotSupported();
        } // Symbol declared by node (initialized by binding)

        /// <nodoc />
        public INode NextContainer
        {
            get => null;
            set => throw NotSupported();
        } // Next container in declaration order (initialized by binding)

        /// <inheritdoc/>
        public ISymbol LocalSymbol
        {
            get => null;
            set => throw NotSupported();
        } // Local symbol declared by node (initialized by binding only for exported nodes)

        /// <inheritdoc/>
        public void Initialize(SyntaxKind kind, int pos, int end)
        {
            Kind = kind;
            Pos = pos;
            End = end;
            Flags = NodeFlags.None;
        }

        /// <inheritdoc/>
        public sealed override string ToString()
        {
            return ToDisplayString();
        }

        /// <inheritdoc/>
        public string ToDisplayString()
        {
            return this.GetFormattedText();
        }

        /// <nodoc/>
        public object Clone() => MemberwiseClone();

        /// <inheritdoc/>
        public INode GetActualNode()
        {
            return this;
        }

        /// <inheritdoc/>
        public TNode TryCast<TNode>() where TNode : class, INode
        {
            return this as TNode;
        }

        /// <nodoc />
        public void Accept(INodeVisitor visitor)
        {
            visitor.VisitIdentifier(this);
        }

        /// <nododc />
        public TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitIdentifier(this);
        }
    }
}
