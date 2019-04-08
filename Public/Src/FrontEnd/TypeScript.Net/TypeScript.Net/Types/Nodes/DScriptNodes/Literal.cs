// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Runtime.CompilerServices;
using BuildXL.Utilities;
using JetBrains.Annotations;
using TypeScript.Net.Api;
using TypeScript.Net.Core;
using TypeScript.Net.Reformatter;
using static BuildXL.Utilities.FormattableStringEx;

namespace TypeScript.Net.Types
{
#pragma warning disable SA1501 // Statement must not be on a single line
#pragma warning disable SA1600 // Elements must be documented

    /// <summary>
    /// Base class for all pre-processed memory efficient string-based literals expressions.
    /// </summary>
    public abstract class Literal : IHasText, IVisitableNode
    {
        // This class has explicit backing fields to ease instance size computation.
        private INode m_parent;

        private int m_pos;
        private int m_end;

        private SyntaxKind m_kind; // byte
        private byte m_leadingTriviaLength;
        private byte m_literalData;

        /// <nodoc />
        protected byte m_reservedByte;

        /// <nodoc />
        protected int m_reservedInt;

        /// <nodoc />
        public abstract int Id { get; }

        /// <nodoc />
        public int Pos { get { return m_pos; } set { m_pos = value; } }

        /// <nodoc />
        public int End { get { return m_end; } set { m_end = value; } }

        /// <inheritdoc/>
        public INode Parent { get { return m_parent; } set { m_parent = value; } }

        /// <inheritdoc/>
        public abstract string Text { get; set; }

        /// <summary>
        /// Path table that was used to create a current node.
        /// </summary>
        [NotNull]
        public PathTable PathTable => GetSourceFilePathTable(this);

        /// <nodoc />
        public virtual ParserContextFlags ParserContextFlags
        {
            get { return ParserContextFlags.None; }
            set { }
        }

        /// <nodoc />
        public byte LeadingTriviaLength
        {
            get { return m_leadingTriviaLength; }
            set { m_leadingTriviaLength = value; }
        }

        /// <inheritdoc />
        public virtual ISourceFile SourceFile
        {
            get => this.GetSourceFileSlow();
            set { }
        }

        /// <inheritdoc/>
        public SyntaxKind Kind
        {
            get { return m_kind; }
            set { m_kind = value; }
        }

        /// <nodoc />
        public bool IsUnterminated
        {
            get { return (m_literalData & 1) == 1; }
            set { if (value) { m_literalData |= 1; } else { m_literalData &= unchecked((byte)(~1)); } }
        }

        /// <nodoc />
        public bool HasExtendedUnicodeEscape
        {
            get { return (m_literalData & 2) != 0; }
            set { if (value) { m_literalData |= 2; } else { m_literalData &= unchecked((byte)(~2)); } }
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

        /// <inheritdoc/>
        public virtual ISymbol ResolvedSymbol
        {
            get => null;
            set => throw NotSupported();
        }

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
        public virtual string ToDisplayString()
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

        /// <summary>
        /// Helper method that throws <see cref="NotSupportedException"/>.
        /// </summary>
        internal static Exception NotSupported([CallerMemberName]string propertyName = null)
        {
            throw new NotSupportedException(I($"'{propertyName}' property is not supported by BuildXL literals for optimization purposes"));
        }

        /// <summary>
        /// Helper method that returns a path table associated with a current file.
        /// </summary>
        [NotNull]
        internal static PathTable GetSourceFilePathTable(INode node)
        {
            var sourceFile = (SourceFile)node.GetSourceFile();
            Contract.Assert(sourceFile != null, "Source file should not be null.");

            Contract.Assert(sourceFile.PathTable != null, "Source file should have a valid PathTable.");

            return sourceFile.PathTable;
        }

        /// <nodoc/>
        internal abstract void Accept(INodeVisitor visitor);

        /// <nodoc/>
        internal abstract TResult Accept<TResult>(INodeVisitor<TResult> visitor);

        /// <inheritdoc/>
        void IVisitableNode.Accept(INodeVisitor visitor)
        {
            Accept(visitor);
        }

        /// <inheritdoc/>
        TResult IVisitableNode.Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return Accept<TResult>(visitor);
        }
    }
}
