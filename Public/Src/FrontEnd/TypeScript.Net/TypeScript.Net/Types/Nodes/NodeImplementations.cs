// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.Linq;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using JetBrains.Annotations;
using TypeScript.Net.Core;
using TypeScript.Net.Diagnostics;
using TypeScript.Net.Extensions;
using TypeScript.Net.Incrementality;
using TypeScript.Net.Reformatter;
using TypeScript.Net.Utilities;
using static BuildXL.Utilities.FormattableStringEx;

namespace TypeScript.Net.Types
{
#pragma warning disable SA1503 // Braces must not be omitted
#pragma warning disable SA1649 // File name must match first type name
#pragma warning disable SA1501 // Statement must not be on a single line
#pragma warning disable SA1307 // Accessible fields must begin with upper-case letter

    /// <summary>
    /// Extra (optional) state of the node.
    /// </summary>
    /// <remarks>
    /// <see cref="INode"/> interface declares a lot of properties that are optional in many cases.
    /// To reduce memory footprint, optional state is extracted into this class which would be instantiated
    /// only when necessary.
    ///
    /// Because different kinds of nodes could have different 'defaults' in terms of property usage,
    /// classes derived from the <see cref="NodeBase{T}"/> can override some properties with auto-property syntax
    /// which materialize it there (good example is a Symbol property that is optional for many nodes but almost always
    /// not-null for <see cref="PropertyAccessExpression"/>).
    /// </remarks>
    public class NodeExtraState
    {
        /// <nodoc/>
        public NodeFlags Flags { get; set; }

        /// <nodoc/>
        public NodeArray<IDecorator> Decorators { get; set; }

        /// <nodoc/>
        public ModifiersArray Modifiers { get; set; }

        /// <nodoc/>
        public ISymbolTable Locals { get; set; } // Locals associated with node (initialized by binding)

        /// <nodoc/>
        public INode NextContainer { get; set; } // Next container in declaration order (initialized by binding)

        /// <nodoc/>
        public ISymbol LocalSymbol { get; set; }

        /// <nodoc/>
        public ISymbol Symbol { get; set; }
    }

    /// <nodoc />
    public class NodeExtraStateWithResolvedSymbol : NodeExtraState
    {
        /// <nodoc/>
        public ISymbol ResolvedSymbol { get; set; }
    }

    /// <summary>
    /// Base type for all ast nodes.
    /// </summary>
    public abstract class NodeBase
    {
        private const int InvalidId = CoreUtilities.InvalidIdentifier;

        // Internal identifier that can be changed in a lock-free manner.
        internal int m_id = InvalidId;

        private int m_pos;
        private int m_end;

        /// <summary>
        /// Syntax kind field that derived type can decide to use for <see cref="SyntaxKind"/> property implementation.
        /// </summary>
        protected SyntaxKind m_kind; // byte
        private ParserContextFlags m_parserContextFlags; // byte
        private byte m_leadingTriviaLength;

        /// <summary>
        /// Unused field that can be used by derived type for any purposes.
        /// </summary>
        protected byte m_unused;

        /// <nodoc />
        public int Id => m_id;

        /// <nodoc />
        public int Pos
        {
            get { return m_pos; }
            set { m_pos = value; }
        }

        /// <nodoc />
        public int End
        {
            get { return m_end; }
            set { m_end = value; }
        }

        /// <nodoc />
        public ParserContextFlags ParserContextFlags
        {
            get { return m_parserContextFlags; }
            set { m_parserContextFlags = value; }
        }

        /// <nodoc />
        public byte LeadingTriviaLength
        {
            get { return m_leadingTriviaLength; }
            set { m_leadingTriviaLength = value; }
        }
    }

    /// <summary>
    /// Lightweight node that does not require an extra state.
    /// </summary>
    public abstract class NodeBaseLight : NodeBase, IVisitableNode, INode
    {
        /// <inheritdoc />
        public ISourceFile SourceFile
        {
            // Compute SourceFile by walking up the parents, instead of keeping the reference.
            get
            {
                return this.GetSourceFileSlow();
            }

            set
            {
                if (value != null && Parent == null)
                {
                    // The SourceFile property should be always available right after the parsing is done without the binding phase.
                    // This trick allows the lightweight nodes (like Identifier) to have a valid SourceFile property even when without binding.
                    Parent = value;
                }
            }
        }

        /// <inheritdoc/>
        public virtual SyntaxKind Kind
        {
            get => SyntaxKind;

            set
            {
                if (value != SyntaxKind)
                {
                    throw new InvalidOperationException(
                        I($"Can't set Kind to '{value}'. The only supported value is '{SyntaxKind}'"));
                }
            }
        }

        /// <summary>
        /// Syntax kind for this node.
        /// </summary>
        protected virtual SyntaxKind SyntaxKind
        {
            get { throw new NotImplementedException("Override this property or override Kind property."); }
        }

        /// <inheritdoc/>
        public NodeFlags Flags
        {
            get => NodeFlags.None;
            set
            {
                if (value != NodeFlags.None)
                {
                    throw new NotSupportedException("Flags setter is not supported");
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
                    throw new NotSupportedException("Decorators setter is not supported");
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
                    throw new NotSupportedException("Modifiers setter is not supported");
                }
            }
        }

        /// <inheritdoc/>
        public INode Parent { get; set; }

        /// <inheritdoc/>
        public virtual ISymbolTable Locals
        {
            get => null;
            set { }
        } // Symbol declared by node (initialized by binding)// Locals associated with node (initialized by binding)

        /// <inheritdoc/>
        public virtual ISymbol Symbol
        {
            get => null;
            set => throw new NotSupportedException("Symbol setter is not supported");
        } // Symbol declared by node (initialized by binding)

        /// <inheritdoc/>
        public virtual ISymbol ResolvedSymbol
        {
            get => null;
            set => throw new NotSupportedException("LocalSymbol setter is not supported");
        }

        /// <nodoc />
        public INode NextContainer
        {
            get => null;
            set => throw new NotSupportedException("NextContainer setter is not supported");
        } // Next container in declaration order (initialized by binding)

        /// <inheritdoc/>
        public ISymbol LocalSymbol
        {
            get => null;
            set => throw new NotSupportedException("LocalSymbol setter is not supported");
        } // Local symbol declared by node (initialized by binding only for exported nodes)

        /// <summary>
        /// Used for debugging only: Forces visual studio to list this field in the debugger tooltip.
        /// </summary>
        [Obsolete("Used for debugging only")]
        public string FormattedText => this.GetFormattedText();

        /// <inheritdoc/>
        public void Initialize(SyntaxKind kind, int pos, int end)
        {
            Kind = kind;
            Pos = pos;
            End = end;
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

    /// <summary>
    /// Base <see cref="INode"/> implementation that uses extra state to trim down an instance size.
    /// </summary>
    [DebuggerDisplay("{ToString(), nq}")]
    public abstract partial class NodeBase<TExtraState> : NodeBase, IVisitableNode, INode where TExtraState : NodeExtraState, new()
    {
        /// <inheritdoc />
        public ISourceFile SourceFile { get; set; }

        /// <summary>
        /// Potentially unmaterialized extra state for a symbol.
        /// </summary>
        [CanBeNull]
        protected TExtraState m_extraState;

        /// <summary>
        /// Non-nullable extra state that would be created on demand.
        /// </summary>
        /// <remarks>
        /// Body uses <see cref="FastActivator{T}.Create"/> to avoid performance penalty from a regular <see cref="Activator.CreateInstance{T}"/>
        /// that would be used with <code>new T()</code> construct.
        /// </remarks>
        [JetBrains.Annotations.NotNull]
        protected TExtraState ExtraState => m_extraState ?? (m_extraState = FastActivator<TExtraState>.Create());

        /// <inheritdoc/>
        /// <remarks>
        /// Current type has two similar properties: Kind and SyntaxKind.
        /// The difference is subtle: first one is defined in <see cref="INode"/> interface and represents actual node kind.
        /// In vast majority of cases, node kind is known based on the node class.
        /// To avoid storing unnecessary field in a base type this property is imlpemented without backing field.
        ///
        /// In this case derived type has two options: it can override SyntaxKind property and just return one specific kind
        /// (in this case, any attempts to change the kind will fail). Or it can implement Kind property using auto-property
        /// syntax that will allow anyone to set it to any value.
        /// </remarks>
        public virtual SyntaxKind Kind
        {
            get => SyntaxKind;

            set
            {
                if (value != SyntaxKind)
                {
                    throw new InvalidOperationException(
                       I($"Can't set Kind to '{value}'. The only supported value is '{SyntaxKind}'"));
                }
            }
        }

        /// <summary>
        /// Syntax kind for this node.
        /// </summary>
        protected virtual SyntaxKind SyntaxKind
        {
            get { throw new NotImplementedException("Override this property or override Kind property."); }
        }

        /// <inheritdoc/>
        public virtual NodeFlags Flags
        {
            get { return m_extraState?.Flags ?? NodeFlags.None; }
            set { if (value != NodeFlags.None || m_extraState != null) ExtraState.Flags = value; }
        }

        /// <inheritdoc/>
        public NodeArray<IDecorator> Decorators
        {
            get { return m_extraState?.Decorators; }
            set { if (value != null || m_extraState != null) ExtraState.Decorators = value; }
        }

        /// <inheritdoc/>
        public virtual ModifiersArray Modifiers
        {
            get { return m_extraState?.Modifiers; }
            set { if (value != null || m_extraState != null) ExtraState.Modifiers = value; }
        }

        /// <inheritdoc/>
        public INode Parent { get; set; } // widely used!

        /// <inheritdoc/>
        public virtual ISymbolTable Locals
        {
            get { return m_extraState?.Locals; }
            set { if (value != null || m_extraState != null) ExtraState.Locals = value; }
        } // Symbol declared by node (initialized by binding)// Locals associated with node (initialized by binding)

        /// <inheritdoc/>
        public virtual ISymbol Symbol
        {
            get { return m_extraState?.Symbol; }
            set { if (value != null || m_extraState != null) ExtraState.Symbol = value; }
        } // Symbol declared by node (initialized by binding)

        /// <inheritdoc/>
        public virtual ISymbol ResolvedSymbol { get; set; } // Cached name resolution result

        /// <nodoc />
        public INode NextContainer
        {
            get { return m_extraState?.NextContainer; }
            set { ExtraState.NextContainer = value; }
        } // Next container in declaration order (initialized by binding)

        /// <inheritdoc/>
        public virtual ISymbol LocalSymbol
        {
            get { return m_extraState?.LocalSymbol; }
            set { ExtraState.LocalSymbol = value; }
        } // Local symbol declared by node (initialized by binding only for exported nodes)

        /// <summary>
        /// Used for debugging only: Forces visual studio to list this field in the debugger tooltip.
        /// </summary>
        [Obsolete("Used for debugging only")]
        public string FormattedText => this.GetFormattedText();

        /// <inheritdoc/>
        public void Initialize(SyntaxKind kind, int pos, int end)
        {
            Kind = kind;
            Pos = pos;
            End = end;
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
    }

    /// <nodoc/>
    [DebuggerDisplay("{ToString(), nq}")]
    public abstract partial class Node : NodeBase<NodeExtraState>
    {
    }

    /// <nodoc/>
    public abstract partial class TokenNodeBase : Node, ITokenNode
    { }

    /// <nodoc/>
    public sealed partial class DotTokenNode : TokenNodeBase
    {
        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.DotToken;
    }

    /// <nodoc/>
    public partial class TokenNode : TokenNodeBase, ITokenNode, IHasText
    {
        /// <inheritdoc/>
        string IHasText.Text { get; set; }

        /// <nodoc/>
        public override SyntaxKind Kind { get { return m_kind; } set { m_kind = value; } }
    }

    /// <nodoc/>
    public class EmptyDotTokenNode : TokenNodeBase, ITokenNode
    {
        /// <nodoc/>
        public override SyntaxKind Kind { get { return m_kind; } set { m_kind = value; } }

        /// <inheritdoc/>
        internal override void Accept(INodeVisitor visitor)
        {
            // Intentionally blank
        }

        /// <inheritdoc/>
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            throw new NotImplementedException();
        }
    }

    /// <nodoc/>
    public sealed partial class IfStatement : Node, IIfStatement, IExpressionStatement
    {
        /// <nodoc/>
        public IExpression Expression { get; set; }

        /// <inheritdoc/>
        public IStatement ThenStatement { get; set; }

        /// <inheritdoc/>
        public Optional<IStatement> ElseStatement { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.IfStatement;
    }

    /// <nodoc/>
    public sealed partial class DoStatement : Node, IDoStatement, IExpressionStatement
    {
        /// <inheritdoc/>
        public IStatement Statement { get; set; }

        /// <nodoc/>
        public IExpression Expression { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.DoStatement;
    }

    /// <nodoc/>
    public abstract partial class ClassLikeDeclarationBase : Node, IClassLikeDeclaration
    {
        /// <inheritdoc/>
        public IIdentifier Name { get; set; }

        /// <inheritdoc/>
        public NodeArray<ITypeParameterDeclaration> TypeParameters { get; set; }

        /// <inheritdoc/>
        public NodeArray<IHeritageClause> HeritageClauses { get; set; }

        /// <inheritdoc/>
        public NodeArray<IClassElement> Members { get; set; }

        private DeclarationName m_name;

        /// <inheritdoc/>
        DeclarationName IDeclaration.Name => m_name ?? (m_name = DeclarationName.PropertyName(Name));

        /// <nodoc/>
        public override string ToDisplayString()
        {
            return ReformatterHelper.FormatOnlyFunctionHeader(this);
        }
    }

    /// <nodoc/>
    public sealed partial class ClassDeclaration : ClassLikeDeclarationBase, IClassDeclaration
    {
        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.ClassDeclaration;
    }

    /// <nodoc/>
    public sealed partial class ClassExpression : ClassLikeDeclarationBase, IClassExpression
    {
        /// <nodoc />
        DeclarationName IDeclaration.Name => null;

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.ClassExpression;
    }

    /// <nodoc/>
    public sealed partial class LabeledStatement : Node, ILabeledStatement
    {
        /// <inheritdoc/>
        public IIdentifier Label { get; set; }

        /// <inheritdoc/>
        public IStatement Statement { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.LabeledStatement;
    }

    /// <nodoc/>
    public sealed partial class EmptyStatement : Node, IEmptyStatement
    {
        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.EmptyStatement;
    }

    /// <nodoc/>
    public sealed partial class BlankLineStatement : Node, IBlankLineStatement
    {
        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.BlankLineStatement;
    }

    /// <nodoc/>
    public sealed partial class SingleLineCommentExpression : Node, ISingleLineCommentExpression
    {
        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.SingleLineCommentTrivia;

        /// <summary>
        /// Text of the comment
        /// </summary>
        public string Text { get; set; }
    }

    /// <nodoc/>
    public sealed partial class MultiLineCommentExpression : Node, IMultiLineCommentExpression
    {
        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.MultiLineCommentTrivia;

        /// <summary>
        /// Text of the comment
        /// </summary>
        public string Text { get; set; }
    }

    /// <summary>
    /// Abstract class for nodes that wrap a comment expression
    /// </summary>
    public abstract class CommentExpressionWrapper : Node
    {
        private ICommentExpression m_commentExpression;

        /// <inhitdoc />
        public override SyntaxKind Kind { get { return m_kind; } set { m_kind = value; } }

        /// <nodoc />
        public ICommentExpression CommentExpression
        {
            get => m_commentExpression;

            set
            {
                if (value.Kind != Kind)
                {
                    throw new InvalidOperationException(
                       I($"Can't set Kind to '{value}'. The only supported value is '{Kind}'"));
                }

                m_commentExpression = value;
            }
        }
    }

    /// <nodoc/>
    public sealed partial class CommentStatement : CommentExpressionWrapper, ICommentStatement
    {
    }

    /// <nodoc/>
    public sealed partial class ExpressionStatement : Node, IExpressionStatement
    {
        /// <inheritdoc/>
        public IExpression Expression { get; set; }

        /// <nodoc/>
        public ExpressionStatement()
        {
        }

        /// <nodoc/>
        public ExpressionStatement(IExpression expression)
        {
            Kind = SyntaxKind.ExpressionStatement;
            Expression = expression;
        }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.ExpressionStatement;
    }

    /// <summary>
    /// Base type for different kind of function-like language constructs like function declaration, method declaration, constructor declaration etc.
    /// </summary>
    public abstract partial class FunctionLikeDeclarationBase : Node, IFunctionLikeDeclaration
    {
        /// <nodoc/>
        public IIdentifier Name { get; set; }

        /// <nodoc/>
        [CanBeNull]
        public IBlock Body { get; set; }

        /// <inheritdoc/>
        public NodeArray<ITypeParameterDeclaration> TypeParameters { get; set; }

        /// <inheritdoc/>
        public NodeArray<IParameterDeclaration> Parameters { get; set; }

        /// <inheritdoc/>
        [SuppressMessage("Microsoft.Naming", "CA1721:PropertyNamesShouldNotMatchGetMethods", Justification = "Type nomenclature is necessary within a compiler.")]
        public ITypeNode Type { get; set; }

        /// <inheritdoc/>
        public Optional<INode> AsteriskToken { get; set; }

        /// <inheritdoc/>
        public Optional<INode> QuestionToken { get; set; }

        /// <inheritdoc/>
        PropertyName ISignatureDeclaration.Name => PropertyName.Identifier(Name);

        /// <inheritdoc/>
        ConciseBody IFunctionLikeDeclaration.Body => ConciseBody.Block(Body);

        private DeclarationName m_name;

        /// <inheritdoc/>
        DeclarationName IDeclaration.Name => m_name ?? (m_name = DeclarationName.PropertyName(Name));

        /// <nodoc/>
        public sealed override string ToDisplayString()
        {
            return ReformatterHelper.FormatOnlyFunctionHeader(this);
        }
    }

    /// <nodoc/>
    public sealed partial class FunctionDeclaration : FunctionLikeDeclarationBase, IFunctionDeclaration
    {
        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.FunctionDeclaration;

        /// <inheritdoc/>
        public override NodeFlags Flags { get; set; }
    }

    /// <nodoc/>
    public sealed partial class ArrowFunction : NodeBase<NodeExtraState>, IArrowFunction, IFunctionLikeDeclaration
    {
        /// <inheritdoc/>
        public NodeArray<ITypeParameterDeclaration> TypeParameters { get; set; }

        /// <inheritdoc/>
        public NodeArray<IParameterDeclaration> Parameters { get; set; }

        /// <inheritdoc/>
        [SuppressMessage("Microsoft.Naming", "CA1721:PropertyNamesShouldNotMatchGetMethods", Justification = "Type nomenclature is necessary within a compiler.")]
        public ITypeNode Type { get; set; }

        /// <inheritdoc/>
        public PropertyName Name { get; set; }

        /// <inheritdoc/>
        public Optional<INode> AsteriskToken { get; set; }

        /// <inheritdoc/>
        public Optional<INode> QuestionToken { get; set; }

        /// <nodoc/>
        public ConciseBody Body { get; set; }

        /// <inheritdoc/>
        public ITokenNode EqualsGreaterThanToken { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.ArrowFunction;

        /// <nodoc/>
        public ArrowFunction()
        {
        }

        /// <nodoc/>
        public ArrowFunction(IParameterDeclaration[] parameters, params IStatement[] statements)
        {
            Parameters = new NodeArray<IParameterDeclaration>(parameters);
            Body = new ConciseBody(new Block(statements));
        }

        /// <nodoc/>
        public ArrowFunction(IParameterDeclaration[] parameters, IExpression expression)
        {
            Parameters = new NodeArray<IParameterDeclaration>(parameters);
            Body = new ConciseBody(expression);
        }

        /// <inheritdoc/>
        DeclarationName IDeclaration.Name => Name;

        /// <nodoc/>
        public override string ToDisplayString()
        {
            return ReformatterHelper.FormatOnlyFunctionHeader(this);
        }
    }

    /// <nodoc/>
    public sealed partial class FunctionExpression : NodeBase<NodeExtraState>, IFunctionExpression, IFunctionLikeDeclaration
    {
        /// <inheritdoc/>
        public NodeArray<ITypeParameterDeclaration> TypeParameters { get; set; }

        /// <inheritdoc/>
        public NodeArray<IParameterDeclaration> Parameters { get; set; }

        /// <inheritdoc/>
        [SuppressMessage("Microsoft.Naming", "CA1721:PropertyNamesShouldNotMatchGetMethods", Justification = "Type nomenclature is necessary within a compiler.")]
        public ITypeNode Type { get; set; }

        /// <inheritdoc/>
        public PropertyName Name { get; set; }

        /// <inheritdoc/>
        public Optional<INode> AsteriskToken { get; set; }

        /// <inheritdoc/>
        public Optional<INode> QuestionToken { get; set; }

        /// <inheritdoc/>
        public ConciseBody Body { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.FunctionExpression;

        /// <nodoc />
        DeclarationName IDeclaration.Name => Name;

        /// <nodoc />
        IIdentifier IFunctionExpression.Name => Name;

        /// <nodoc />
        public override string ToDisplayString()
        {
            return ReformatterHelper.FormatOnlyFunctionHeader(this);
        }
    }

    /// <nodoc/>
    public sealed partial class ClassElement : Node, IClassElement
    {
        /// <nodoc />
        DeclarationName IDeclaration.Name => null;

        /// <nodoc />
        public override SyntaxKind Kind { get { return m_kind; } set { m_kind = value; } }
    }

    /// <nodoc/>
    public sealed partial class SemicolonClassElement : Node, ISemicolonClassElement
    {
        /// <nodoc />
        DeclarationName IDeclaration.Name => null;

        /// <nodoc />
        public override SyntaxKind Kind { get { return m_kind; } set { m_kind = value; } }
    }

    /// <nodoc/>
    public sealed partial class ConstructorDeclaration : FunctionLikeDeclarationBase, IConstructorDeclaration
    {
        /// <nodoc />
        public override SyntaxKind Kind { get { return m_kind; } set { m_kind = value; } }
    }

    /// <nodoc/>
    public sealed partial class BreakOrContinueStatement : Node, IBreakOrContinueStatement
    {
        /// <nodoc />
        public IIdentifier Label { get; set; }

        /// <nodoc />
        public override SyntaxKind Kind
        {
            get { return m_kind; }
            set { m_kind = value; }
        }

        /// <nodoc/>
        public static BreakOrContinueStatement Break()
        {
            return new BreakOrContinueStatement()
            {
                Kind = SyntaxKind.BreakStatement,
            };
        }

        /// <nodoc/>
        public static BreakOrContinueStatement Continue()
        {
            return new BreakOrContinueStatement()
            {
                Kind = SyntaxKind.ContinueStatement,
            };
        }
    }

    /// <nodoc/>
    public sealed partial class ReturnStatement : Node, IReturnStatement, IExpressionStatement
    {
        /// <inheritdoc/>
        public IExpression Expression { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.ReturnStatement;

        /// <nodoc/>
        public ReturnStatement()
        {
        }

        /// <nodoc/>
        public ReturnStatement(IExpression expression)
        {
            Expression = expression;
        }
    }

    /// <nodoc/>
    public sealed partial class WithStatement : Node, IWithStatement, IExpressionStatement
    {
        /// <nodoc />
        public IExpression Expression { get; set; }

        /// <inheritdoc/>
        public IStatement Statement { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.WithStatement;
    }

    /// <nodoc/>
    public sealed partial class WhileStatement : Node, IWhileStatement, IExpressionStatement
    {
        /// <inheritdoc/>
        public IStatement Statement { get; set; }

        /// <nodoc />
        public IExpression Expression { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.WhileStatement;
    }

    /// <nodoc/>
    public sealed partial class ExportDeclaration : Node, IExportDeclaration
    {
        /// <inheritdoc/>
        public INamedExports ExportClause { get; set; }

        /// <inheritdoc/>
        public IExpression ModuleSpecifier { get; set; }

        /// <nodoc/>
        // required now, as there is a asterisk node in the syntax tree
        public int AsteriskPos { get; set; }

        /// <nodoc/>
        public int AsteriskEnd { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.ExportDeclaration;

        /// <nodoc/>
        public ExportDeclaration()
        {
        }

        /// <nodoc/>
        public ExportDeclaration(string module)
        {
            ModuleSpecifier = new LiteralExpression(module);
        }

        /// <inheritdoc />
        DeclarationName IDeclaration.Name => null;

        /// <inheritdoc />
        IIdentifier IDeclarationStatement.Name => null;
    }

    /// <nodoc/>
    public sealed partial class ExportAssignment : Node, IExportAssignment
    {
        /// <inheritdoc/>
        public Optional<bool> IsExportEquals { get; set; }

        /// <inheritdoc/>
        public IExpression Expression { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.ExportAssignment;

        /// <inheritdoc />
        DeclarationName IDeclaration.Name => null;

        /// <inheritdoc />
        IIdentifier IDeclarationStatement.Name => null;
    }

    /// <nodoc/>
    public sealed partial class ImportEqualsDeclaration : Node, IImportEqualsDeclaration
    {
        /// <inheritdoc/>
        public EntityNameOrExternalModuleReference ModuleReference { get; set; }

        /// <nodoc/>
        public IIdentifier Name { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.ImportEqualsDeclaration;

        private DeclarationName m_name;

        /// <inheritdoc />
        DeclarationName IDeclaration.Name => m_name ?? (m_name = DeclarationName.PropertyName(Name));
    }

    /// <nodoc/>
    public sealed partial class ImportDeclaration : Node, IImportDeclaration
    {
        /// <inheritdoc/>
        public IImportClause ImportClause { get; set; }

        /// <inheritdoc/>
        public IExpression ModuleSpecifier { get; set; }

        /// <inheritdoc/>
        public bool IsLikeImport { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.ImportDeclaration;

        /// <nodoc/>
        public ImportDeclaration()
        {
        }

        /// <summary>
        /// Generates `import * as alias from module
        /// </summary>
        public ImportDeclaration(string alias, string module)
        {
            ImportClause = new ImportClause(alias);
            ModuleSpecifier = new LiteralExpression(module);
        }

        /// <summary>
        /// Generates `import {n1,n2} from module
        /// </summary>
        public ImportDeclaration(string[] names, string module)
        {
            ImportClause = new ImportClause(names);
            ModuleSpecifier = new LiteralExpression(module);
        }
    }

    /// <nodoc/>
    public sealed partial class ImportClause : Node, IImportClause
    {
        /// <inheritdoc/>
        public NamespaceImportOrNamedImports NamedBindings { get; set; }

        /// <nodoc/>
        public IIdentifier Name { get; set; }

        /// <nodoc/>
        public bool IsImport { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.ImportClause;

        /// <nodoc/>
        public ImportClause()
        {
        }

        /// <summary>
        /// Generates `* as name`
        /// </summary>
        public ImportClause(string name)
        {
            NamedBindings = new NamespaceImportOrNamedImports(new NamespaceImport(name));
        }


        /// <summary>
        /// Generates `{n1, n2}`
        /// </summary>
        public ImportClause(params string[] names)
        {
            NamedBindings = new NamespaceImportOrNamedImports(new NamedImports(names));
        }

        private DeclarationName m_name;

        /// <inheritdoc />
        DeclarationName IDeclaration.Name => m_name ?? (m_name = DeclarationName.PropertyName(Name));
    }

    /// <nodoc/>
    public sealed partial class NamespaceImport : Node, INamespaceImport
    {
        /// <inheritdoc/>
        public IIdentifier Name { get; set; }

        /// <nodoc/>
        public bool IsImport { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.NamespaceImport;

        /// <nodoc/>
        public NamespaceImport()
        {
        }

        /// <nodoc/>
        public NamespaceImport(string name)
        {
            Name = new Identifier(name);
        }

        private DeclarationName m_name;

        /// <inheritdoc />
        DeclarationName IDeclaration.Name => m_name ?? (m_name = DeclarationName.PropertyName(Name));
    }

    /// <nodoc/>
    public sealed partial class ExternalModuleReference : Node, IExternalModuleReference
    {
        /// <inheritdoc/>
        public IExpression Expression { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.ExternalModuleReference;
    }

    /// <nodoc/>
    public sealed partial class QualifiedName : Node, IQualifiedName
    {
        /// <inheritdoc/>
        public EntityName Left { get; set; }

        /// <inheritdoc/>
        public IIdentifier Right { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.QualifiedName;

        /// <nodoc/>
        public QualifiedName()
        {
        }

        /// <nodoc/>
        public QualifiedName(EntityName left, string right)
        {
            Left = left;
            Right = new Identifier(right);
        }
    }

    /// <summary>
    /// Identifier node (like 'a' in 'const a = 42';).
    /// </summary>
    /// <remarks>
    /// This node is the most widely used ast node in the system.
    /// To reduce the memory footprint, this node is derived from <see cref="NodeBase"/> but not from <see cref="Node"/> or from <see cref="NodeBase{T}"/>.
    /// </remarks>
    public sealed partial class Identifier : NodeBaseLight, IIdentifier, IVisitableNode
    {
        private ISymbol m_resolvedSymbol;
        private string m_text;

        /// <nodoc />
        public static Identifier CreateUndefined() => new Identifier("undefined");

        /// <nodoc/>
        public Identifier()
        {
        }

        /// <nodoc/>
        public Identifier(string text, SyntaxKind originalKeywordKind = SyntaxKind.Identifier)
        {
            Text = text;
            OriginalKeywordKind = originalKeywordKind;
        }

        /// <inheritdoc />
        public string Text
        {
            get { return m_text; }
            set { m_text = value; }
        }

        /// <inheritdoc/>
        public override ISymbol ResolvedSymbol { get { return m_resolvedSymbol; } set { m_resolvedSymbol = value; } }

        /// <inheritdoc/>
        public SyntaxKind OriginalKeywordKind
        {
            get { return (SyntaxKind)m_unused; }
            set { m_unused = (byte)value; }
        }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.Identifier;
    }

    /// <nodoc/>
    public sealed partial class EnumDeclaration : Node, IEnumDeclaration
    {
        /// <inheritdoc/>
        public NodeArray<IEnumMember> Members { get; set; }

        /// <nodoc />
        public IIdentifier Name { get; set; }

        private DeclarationName m_name;

        /// <inheritdoc/>
        DeclarationName IDeclaration.Name => m_name ?? (m_name = DeclarationName.PropertyName(Name));

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.EnumDeclaration;

        /// <nodoc/>
        public override NodeFlags Flags { get; set; }
    }

    /// <nodoc/>
    public sealed partial class EnumMember : Node, IEnumMember, IVariableLikeDeclaration
    {
        /// <nodoc />
        public DeclarationName Name { get; set; }

        /// <inheritdoc/>
        public Optional<IExpression> Initializer { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.EnumMember;

        /// <inheritdoc/>
        PropertyName IVariableLikeDeclaration.PropertyName => (PropertyName)Name;

        /// <inheritdoc/>
        Optional<INode> IVariableLikeDeclaration.DotDotDotToken => default(Optional<INode>);

        /// <inheritdoc/>
        ITypeNode IVariableLikeDeclaration.Type => null;

        /// <inheritdoc/>
        IExpression IVariableLikeDeclaration.Initializer => Initializer.ValueOrDefault;

        /// <inheritdoc/>
        Optional<INode> IVariableLikeDeclaration.QuestionToken => default(Optional<INode>);
    }

    /// <nodoc/>
    public sealed partial class TypeAliasDeclaration : Node, ITypeAliasDeclaration
    {
        /// <inheritdoc/>
        public NodeArray<ITypeParameterDeclaration> TypeParameters { get; set; }

        /// <inheritdoc/>
        [SuppressMessage("Microsoft.Naming", "CA1721:PropertyNamesShouldNotMatchGetMethods", Justification = "Type nomenclature is necessary within a compiler.")]
        public ITypeNode Type { get; set; }

        /// <nodoc/>
        public IIdentifier Name { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.TypeAliasDeclaration;

        private DeclarationName m_name;

        /// <inheritdoc/>
        DeclarationName IDeclaration.Name => m_name ?? (m_name = DeclarationName.PropertyName(Name));
    }

    /// <nodoc/>
    public sealed partial class Modifier : Node, IModifier
    {
        /// <nodoc/>
        public override SyntaxKind Kind { get { return m_kind; } set { m_kind = value; } }

        /// <nodoc/>
        public override NodeFlags Flags { get; set; }

        /// <nodoc/>
        public override ISymbol ResolvedSymbol
        {
            get => null;
            set { }
        }
    }

    /// <nodoc/>
    public sealed partial class Decorator : Node, IDecorator
    {
        /// <nodoc />
        public Decorator()
        {
        }

        /// <nodoc />
        public Decorator(ILeftHandSideExpression expression)
        {
            Expression = expression;
        }

        /// <inheritdoc/>
        public ILeftHandSideExpression Expression { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.Decorator;
    }

    /// <summary>
    /// Extra state for <see cref="CallExpression"/>.
    /// </summary>
    public class CallExpressionExtraState : NodeExtraState
    {
        /// <nodoc />
        public NodeArray<ITypeNode> TypeArguments { get; set; }

        /// <nodoc />
        public ISymbol ResolvedSymbol { get; set; }
    }

    /// <nodoc/>
    public sealed partial class CallExpression : NodeBase<CallExpressionExtraState>, ICallExpression
    {
        /// <inheritdoc/>
        public ILeftHandSideExpression Expression { get; set; }

        /// <inheritdoc/>
        public NodeArray<ITypeNode> TypeArguments
        {
            get { return m_extraState?.TypeArguments; }
            set { if (value != null || m_extraState != null) ExtraState.TypeArguments = value; }
        }

        /// <nodoc/>
        public override ISymbol ResolvedSymbol
        {
            get { return m_extraState?.ResolvedSymbol; }
            set { if (value != null || m_extraState != null) ExtraState.ResolvedSymbol = value; }
        }

        /// <inheritdoc/>
        public NodeArray<IExpression> Arguments { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.CallExpression;

        /// <nodoc/>
        public CallExpression()
        {
        }

        /// <nodoc/>
        public CallExpression(ILeftHandSideExpression expression, params IExpression[] arguments)
        {
            Expression = expression;
            Arguments = new NodeArray<IExpression>(arguments);
        }
    }

    /// <nodoc/>
    public sealed partial class NewExpression : NodeBase<NodeExtraState>, INewExpression
    {
        /// <inheritdoc/>
        public ILeftHandSideExpression Expression { get; set; }

        /// <inheritdoc/>
        public NodeArray<ITypeNode> TypeArguments { get; set; }

        /// <inheritdoc/>
        public NodeArray<IExpression> Arguments { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.NewExpression;
    }

    /// <nodoc />
    public class PropertyAccessExpressionExtraState : NodeExtraState
    {
    }

    /// <nodoc/>
    public sealed partial class PropertyAccessExpression : NodeBase<PropertyAccessExpressionExtraState>, IPropertyAccessExpression
    {
        /// <inheritdoc/>
        public ILeftHandSideExpression Expression { get; set; }

        // DotToken is never used in DScript and intentially left as a no-op.
        // PropertyAccessExpression is one of the most widely used types and keeping
        // increases memory footprint for a big build significantly (150Mb on big Windows builds).

        /// <inheritdoc/>
        public INode DotToken
        {
            get { return null; }
            set { }
        }

        /// <inheritdoc/>
        public IIdentifier Name { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.PropertyAccessExpression;

        /// <nodoc/>
        public PropertyAccessExpression()
        {
        }

        /// <nodoc/>
        public PropertyAccessExpression(ILeftHandSideExpression expression, string name)
        {
            Expression = expression;
            DotToken = new DotTokenNode();
            Name = new Identifier(name);
        }

        /// <nodoc/>
        public PropertyAccessExpression(string first, string second)
        {
            // This object becomes the last part.
            Kind = SyntaxKind.PropertyAccessExpression;
            DotToken = new DotTokenNode();
            Expression = new Identifier(first);
            Name = new Identifier(second);
        }

        /// <nodoc/>
        public PropertyAccessExpression(string first, string second, params string[] parts)
        {
            // This object becomes the last part.
            Kind = SyntaxKind.PropertyAccessExpression;
            DotToken = new DotTokenNode();

            // The first part
            ILeftHandSideExpression current = new Identifier(first);

            // if we have parts
            if (parts != null && parts.Length > 0)
            {
                current = new PropertyAccessExpression(current, second);

                // don't loop over the last one.
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    current = new PropertyAccessExpression(current, parts[i]);
                }

                Expression = current;
                Name = new Identifier(parts[parts.Length - 1]);
            }
            else
            {
                Expression = current;
                Name = new Identifier(second);
            }
        }

        /// <nodoc/>
        public PropertyAccessExpression(IReadOnlyList<string> parts)
        {
            Contract.Requires(parts != null);
            Contract.Requires(parts.Count >= 2);

            // This object becomes the last part.
            Kind = SyntaxKind.PropertyAccessExpression;
            DotToken = new DotTokenNode();

            // The first part
            ILeftHandSideExpression current = new Identifier(parts[0]);

            // Loop from the second part to the one to last part
            for (int i = 1; i < parts.Count - 1; i++)
            {
                current = new PropertyAccessExpression(current, parts[i]);
            }

            // This object becomes the last part.
            Expression = current;
            Name = new Identifier(parts[parts.Count - 1]);
        }

        /// <inheritdoc/>
        DeclarationName IDeclaration.Name => DeclarationName.PropertyName(Name);
    }

    /// <nodoc/>
    public sealed partial class PrimaryExpression : NodeBase<NodeExtraState>, IPrimaryExpression
    {
        /// <nodoc/>
        public PrimaryExpression()
        {
        }

        /// <nodoc/>
        public PrimaryExpression(bool value)
        {
            Kind = value ? SyntaxKind.TrueKeyword : SyntaxKind.FalseKeyword;
        }

        /// <nodoc/>
        public PrimaryExpression(SyntaxKind kind)
        {
            Kind = kind;
        }

        /// <nodoc/>
        public override SyntaxKind Kind { get { return m_kind; } set { m_kind = value; } }
    }

    /// <summary>
    /// Extra state for <see cref="ObjectLiteralExpression"/>.
    /// </summary>
    public class ObjectLiteralExtraState : NodeExtraState
    {
        /// <nodoc />
        public DeclarationName Name { get; set; }

        /// <nodoc/>
        public ISymbol ResolvedSymbol { get; set; }
    }

    /// <nodoc/>
    public sealed partial class ObjectLiteralExpression : NodeBase<ObjectLiteralExtraState>, IObjectLiteralExpression
    {
        /// <inheritdoc/>
        public DeclarationName Name
        {
            get { return m_extraState?.Name; }
            set { if (value != null || m_extraState != null) ExtraState.Name = value; }
        }

        /// <inheritdoc/>
        public NodeArray<IObjectLiteralElement> Properties { get; set; }

        /// <nodoc/>
        public override ISymbol Symbol { get; set; }

        /// <inheritdoc/>
        public override ISymbol ResolvedSymbol
        {
            get { return m_extraState?.ResolvedSymbol; }
            set { if (value != null || m_extraState != null) ExtraState.ResolvedSymbol = value; }
        }

        /// <remarks>
        /// Flags are almost always presented, so it is more efficient to 'materialize' it in the instance and not to allocate extra state field
        /// </remarks>
        public override NodeFlags Flags { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.ObjectLiteralExpression;

        /// <nodoc/>
        public ObjectLiteralExpression()
        {
        }

        /// <nodoc/>
        public ObjectLiteralExpression(params IObjectLiteralElement[] properties)
        {
            Properties = new NodeArray<IObjectLiteralElement>(properties);
        }

        /// <nodoc/>
        public ObjectLiteralExpression(List<IObjectLiteralElement> properties)
        {
            Properties = new NodeArray<IObjectLiteralElement>(properties);
        }
    }

    /// <nodoc/>
    public sealed partial class Expression : NodeBase<NodeExtraState>, IExpression
    {
        /// <inheritdoc/>
        public override SyntaxKind Kind { get { return m_kind; } set { m_kind = value; } }
    }

    /// <nodoc/>
    public sealed partial class ArrayLiteralExpression : NodeBase<NodeExtraState>, IArrayLiteralExpression
    {
        /// <inheritdoc />
        public override NodeFlags Flags { get; set; }

        /// <inheritdoc/>
        public NodeArray<IExpression> Elements { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.ArrayLiteralExpression;

        /// <nodoc/>
        public ArrayLiteralExpression()
        {
        }

        /// <nodoc/>
        public ArrayLiteralExpression(params IExpression[] elements)
        {
            Elements = new NodeArray<IExpression>(elements);
        }

        /// <nodoc/>
        public ArrayLiteralExpression(List<IExpression> elements)
        {
            Elements = new NodeArray<IExpression>(elements);
        }

        /// <nodoc/>
        public ArrayLiteralExpression(IEnumerable<IExpression> elements)
            : this(elements.ToList())
        {
        }
    }

    /// <nodoc/>
    public sealed partial class SpreadElementExpression : NodeBase<NodeExtraState>, ISpreadElementExpression
    {
        /// <inheritdoc/>
        public IExpression Expression { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.SpreadElementExpression;
    }

    /// <nodoc/>
    public sealed partial class ParenthesizedExpression : NodeBase<NodeExtraState>, IParenthesizedExpression
    {
        /// <inheritdoc/>
        public IExpression Expression { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.ParenthesizedExpression;

        /// <nodoc/>
        public ParenthesizedExpression()
        {
        }

        /// <nodoc/>
        public ParenthesizedExpression(IExpression expression)
        {
            Expression = expression;
        }
    }

    /// <nodoc/>
    public sealed partial class Statement : Node, IStatement, IHasText
    {
        /// <inheritdoc />
        string IHasText.Text { get; set; }

        /// <nodoc/>
        public override SyntaxKind Kind { get { return m_kind; } set { m_kind = value; } }

        /// <nodoc/>
        public override string ToDisplayString()
        {
            return "'missing node'";
        }
    }

    /// <nodoc/>
    public sealed partial class TryStatement : Node, ITryStatement
    {
        /// <inheritdoc/>
        public IBlock TryBlock { get; set; }

        /// <inheritdoc/>
        public ICatchClause CatchClause { get; set; }

        /// <inheritdoc/>
        public IBlock FinallyBlock { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.TryStatement;
    }

    /// <nodoc/>
    public sealed partial class CatchClause : Node, ICatchClause
    {
        /// <inheritdoc/>
        public IVariableDeclaration VariableDeclaration { get; set; }

        /// <inheritdoc/>
        public IBlock Block { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.CatchClause;
    }

    /// <nodoc/>
    public sealed partial class SwitchStatement : Node, ISwitchStatement, IExpressionStatement
    {
        /// <nodoc/>
        public IExpression Expression { get; set; }

        /// <inheritdoc/>
        public ICaseBlock CaseBlock { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.SwitchStatement;

        /// <nodoc/>
        public SwitchStatement()
        {
        }

        /// <nodoc/>
        public SwitchStatement(IExpression expression, params ICaseClause[] clauses)
            : this(expression, new List<ICaseClause>(clauses))
        {
        }

        /// <nodoc/>
        public SwitchStatement(IExpression expression, List<ICaseClause> clauses)
        {
            Expression = expression;

            var causesOrDefault = new List<CaseClauseOrDefaultClause>(clauses.Count);
            causesOrDefault.AddRange(clauses.Select(clause => new CaseClauseOrDefaultClause(clause)));
            CaseBlock = new CaseBlock(causesOrDefault);
        }

        /// <nodoc/>
        public SwitchStatement(IExpression expression, IDefaultClause defaultClause, params ICaseClause[] clauses)
            : this(expression, defaultClause, new List<ICaseClause>(clauses))
        {
        }

        /// <nodoc/>
        public SwitchStatement(IExpression expression, IDefaultClause defaultClause, List<ICaseClause> clauses)
        {
            Expression = expression;

            var causesOrDefault = new List<CaseClauseOrDefaultClause>(clauses.Count + 1);
            causesOrDefault.AddRange(clauses.Select(clause => new CaseClauseOrDefaultClause(clause)));
            causesOrDefault.Add(new CaseClauseOrDefaultClause(defaultClause));
            CaseBlock = new CaseBlock(causesOrDefault);
        }
    }

    /// <nodoc/>
    public sealed partial class CaseBlock : Node, ICaseBlock
    {
        /// <inheritdoc/>
        public NodeArray<CaseClauseOrDefaultClause> Clauses { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.CaseBlock;

        /// <nodoc/>
        public CaseBlock()
        {
        }

        /// <nodoc/>
        public CaseBlock(params CaseClauseOrDefaultClause[] clauses)
        {
            Clauses = new NodeArray<CaseClauseOrDefaultClause>(clauses);
        }

        /// <nodoc/>
        public CaseBlock(List<CaseClauseOrDefaultClause> clauses)
        {
            Clauses = new NodeArray<CaseClauseOrDefaultClause>(clauses);
        }
    }

    /// <nodoc/>
    public sealed partial class ThrowStatement : Node, IThrowStatement, IExpressionStatement
    {
        /// <nodoc/>
        public IExpression Expression { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.ThrowStatement;
    }

    /// <nodoc/>
    public sealed partial class VariableStatement : Node, IVariableStatement
    {
        /// <inheritdoc/>
        public IVariableDeclarationList DeclarationList { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.VariableStatement;

        /// <nodoc/>
        public VariableStatement()
        {
        }

        /// <nodoc/>
        public VariableStatement(string name, IExpression initializer, ITypeNode type = null, NodeFlags flags = NodeFlags.Const, NodeFlags declarationFlags = NodeFlags.None)
        {
            var declaration = new VariableDeclaration(name, initializer, type);
            declaration.Flags |= declarationFlags;

            DeclarationList = new VariableDeclarationList(
                flags,
                declaration);
        }

        /// <nodoc />
        public override string ToDisplayString()
        {
            return DeclarationList?.ToDisplayString() ?? string.Empty;
        }

        /// <nodoc />
        public override ModifiersArray Modifiers { get; set; }

        /// <nodoc />
        public override NodeFlags Flags { get; set; }
    }

    /// <nodoc/>
    public sealed partial class VariableDeclarationList : Node, IVariableDeclarationList
    {
        /// <inheritdoc/>
        public INodeArray<IVariableDeclaration> Declarations { get; set; }

        /// <nodoc/>
        public override NodeFlags Flags { get; set; } // Flags and Modifiers are likely to be used. Materializing them in as an instance properties to avoid extra state allocation

        /// <nodoc/>
        public override ModifiersArray Modifiers { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.VariableDeclarationList;

        /// <nodoc/>
        public VariableDeclarationList()
        {
        }

        /// <nodoc/>
        public VariableDeclarationList(params IVariableDeclaration[] declarations)
        {
            Declarations = new NodeArray<IVariableDeclaration>(declarations);
            Flags = NodeFlags.Const;
        }

        /// <nodoc/>
        public VariableDeclarationList(NodeFlags flags, params IVariableDeclaration[] declarations)
        {
            Declarations = new NodeArray<IVariableDeclaration>(declarations);
            Flags = flags;
        }
    }

    /// <summary>
    /// Ad-hoc wrapper in order to support comments inside object literal
    /// </summary>
    public sealed partial class CommentAsLiteralElement : CommentExpressionWrapper, IObjectLiteralElement
    {
        /// <inheritdoc />
        DeclarationName IDeclaration.Name { get; }

        /// <nodoc />
        public PropertyName Name
        {
            get => new PropertyName(new Identifier("__dummy"));
            set { throw new InvalidOperationException("Cannot set the property name of a comment literal"); }
        }
    }

    /// <summary>
    /// Ad-hoc wrapper in order to support comments inside object literal
    /// </summary>
    public sealed partial class CommentAsTypeElement : CommentExpressionWrapper, ITypeElement
    {
        /// <nodoc />
        public Optional<INode> QuestionToken { get; set; }

        /// <nodoc />
        public DeclarationName Name { get; }
    }

    /// <summary>
    /// Ad-hoc wrapper in order to support comments inside object literal
    /// </summary>
    public sealed partial class CommentAsEnumMember : CommentExpressionWrapper, IEnumMember
    {
        /// <nodoc />
        DeclarationName IDeclaration.Name { get; }

        /// <nodoc />
        public Optional<IExpression> Initializer { get; set; }

        /// <nodoc />
        DeclarationName IEnumMember.Name { get; set; }
    }

    /// <nodoc/>
    public sealed partial class VariableDeclaration : Node, IVariableDeclaration, IVariableLikeDeclaration
    {
        /// <inheritdoc/>
        public IdentifierOrBindingPattern Name { get; set; }

        /// <nodoc/>
        [SuppressMessage("Microsoft.Naming", "CA1721:PropertyNamesShouldNotMatchGetMethods", Justification = "Type nomenclature is necessary within a compiler.")]
        public ITypeNode Type { get; set; }

        /// <nodoc/>
        public IExpression Initializer { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.VariableDeclaration;

        /// <nodoc/>
        public VariableDeclaration()
        { }

        /// <nodoc/>
        public VariableDeclaration(string name, IExpression initializer, ITypeNode type = null)
        {
            Name = new IdentifierOrBindingPattern(new Identifier(name));
            Type = type;
            Initializer = initializer;
        }

        /// <inheritdoc/>
        DeclarationName IDeclaration.Name => DeclarationName.PropertyName(Name);

        // TODO: Both name and property name point to the same node. Both elements are listed by the NodeWalker.
        // PropertyName is definitely not implemented properly.

        /// <inheritdoc/>
        PropertyName IVariableLikeDeclaration.PropertyName => null;

        /// <inheritdoc/>
        Optional<INode> IVariableLikeDeclaration.DotDotDotToken => default(Optional<INode>);

        /// <inheritdoc/>
        Optional<INode> IVariableLikeDeclaration.QuestionToken => default(Optional<INode>);
    }

    /// <nodoc/>
    public sealed partial class Block : Node, IBlock
    {
        /// <inheritdoc/>
        public NodeArray<IStatement> Statements { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.Block;

        /// <nodoc/>
        public Block()
        {
        }

        /// <nodoc/>
        public Block(params IStatement[] statements)
        {
            Statements = new NodeArray<IStatement>(statements);
        }
    }

    /// <nodoc/>
    public sealed partial class SourceFile : Node, ISourceFile
    {
        /// <nodoc/>
        internal Dictionary<INode, int> LeadingTriviasMap;

        /// <summary>
        /// Path table for a source file.
        /// </summary>
        internal PathTable PathTable;

        // Using ConcurrentBitArray for both upstream and downstream depdencies for performance reasons.
        // Non-concurrent collection like HashSet introduces noticeable contention and
        // concurrent collection like ConcurrentDictionary introduces significant memory overhead (few gigs for large builds).
        // Bit Vector is lock free but requires additional state because each bit now represents an index in
        // the source file list.
        // This means that the full information about upstream and downstream dependencies now available via
        // the checker interface and all the rest is kept internal in the SourceFile.
        #region file/module dependencies. Fields, props and helpers

        // Index for the current file in the source file list of the checker..
        private int m_currentFileIndex;

        /// <summary>
        /// Initialize two maps for tracking file-2-file dependencies and the index of the current file in the file map.
        /// </summary>
        public void InitDependencyMaps(int currentFileIndex, int numberOfFiles)
        {
            Contract.Requires(currentFileIndex >= 0);
            Contract.Requires(numberOfFiles > currentFileIndex);

            m_currentFileIndex = currentFileIndex;

            FileDependencies = new RoaringBitSet(numberOfFiles);
            FileDependents = new RoaringBitSet(numberOfFiles);
        }

        /// <inheritdoc/>
        public ISpecBindingSymbols BindingSymbols { get; private set; }

        /// <inheritdoc/>
        public void ComputeBindingFingerprint(BuildXL.Utilities.SymbolTable symbolTable)
        {
            Contract.Assert(BindingSymbols == null, "Can't recompute binding symbols more than once.");
            Contract.Assert(State != SourceFileState.Parsed, "Can't compute binding symbols on the unbound source file.");

            BindingSymbols = SpecBindingSymbols.Create(this);
        }

        /// <summary>
        /// Sets the symbols. Used by tests only.
        /// </summary>
        public void SetBindingFingerprintByTest(SpecBindingSymbols symbols) => BindingSymbols = symbols;

        /// <summary>
        /// Sets the file dependencies bit array. Used by tests only.
        /// </summary>
        public void SetFileDependenciesByTest(ConcurrentBitArray value) => FileDependencies = RoaringBitSet.FromBitArray(value);

        /// <summary>
        /// Sets the file dependents bit array. Used by tests only.
        /// </summary>
        public void SetFileDependentsByTest(ConcurrentBitArray value) => FileDependents = RoaringBitSet.FromBitArray(value);

        /// <summary>
        /// Returns an index of a current file in the file map.
        /// </summary>
        public int CurrentFileIndex => m_currentFileIndex;

        /// <summary>
        /// Bit vector that represents files that the current file depend on.
        /// If the bit is set then the current file depends on file with a given index.
        /// </summary>
        public RoaringBitSet FileDependencies { get; private set; }

        /// <summary>
        /// Set of files that depend on the current file.
        /// If the bit is set then the current file depends on file with a given index.
        /// </summary>
        public RoaringBitSet FileDependents { get; private set; }

        /// <summary>
        /// Add an index of the file that depends on the current one.
        /// </summary>
        public bool AddDependentFile(int sourceFileIndex)
        {
            return FileDependents.Set(sourceFileIndex, true);
        }

        /// <summary>
        /// Add an index of the file that the current file depends on.
        /// </summary>
        public bool AddFileDependency(int sourceFileIndex)
        {
            return FileDependencies.Set(sourceFileIndex, true);
        }

        /// <summary>
        /// Set of modules that the current file depends on.
        /// </summary>
        internal HashSet<string> ModuleDependencies { get; } = new HashSet<string>();

        /// <summary>
        /// Add a module name that the current file depends on.
        /// </summary>
        internal bool AddModuleDependency(string moduleName)
        {
            lock (ModuleDependencies)
            {
                return ModuleDependencies.Add(moduleName);
            }
        }

        /// <summary>
        /// Records the given trivia with the given node
        /// </summary>
        public void RecordTrivia(INode node, Trivia trivia)
        {
            m_perNodeTrivias[node.GetActualNode()] = trivia;

            // Hack the SourceFile if not yet set.
            if (node.SourceFile == null)
            {
                node.SourceFile = this;
            }
        }

        /// <summary>
        /// Moves trivia from one node to another
        /// </summary>
        /// <remarks>
        /// This is used when nodes are created too early by the parser and are not the proper outer nodes.
        /// </remarks>
        public void MoveTriva(INode from, INode to)
        {
            var actualFrom = from.GetActualNode();
            var actualTo = to.GetActualNode();

            if (m_perNodeTrivias.TryGetValue(actualFrom, out var trivia))
            {
                m_perNodeTrivias[actualTo] = trivia;
                m_perNodeTrivias.Remove(actualFrom);
            }
        }

        /// <inheritdoc />
        public bool IsPublicFacade => m_serializedAst.content != null;

        /// <inheritdoc />
        public void SetSerializedAst(byte[] content, int contentLength)
        {
            m_serializedAst = (content, contentLength);
        }

        /// <inheritdoc />
        public (byte[] content, int contentLength) SerializedAst => m_serializedAst;

        #endregion file/module dependencies. Fields, props and helpers

        private ( byte[] content, int contentLength) m_serializedAst;

        private AbsolutePath m_absolutePath;
        private LineMap m_lineMap;

        private List<Diagnostic> m_parseDiagnostics;
        private static readonly IReadOnlyList<Diagnostic> s_emptyDiagnostics = new List<Diagnostic>();
        private List<Diagnostic> m_bindDiagnostics;
        private readonly Dictionary<INode, Trivia> m_perNodeTrivias = new Dictionary<INode, Trivia>();

        private readonly ITextSourceProvider m_textSourceProvider;
        private TextSource m_textSource;

        /// <inheritdoc/>
        public SourceFileState State { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.SourceFile;

        // TODO:SQ: Verify correctness

        /// <nodoc/>
        public DeclarationName Name { get; set; }

        /// <inheritdoc/>
        public NodeArray<IStatement> Statements { get; set; }

        /// <inheritdoc/>
        public INode EndOfFileToken { get; set; }

        /// <inheritdoc/>
        public string FileName { get; set; }

        /// <inheritdoc/>
        public Path Path { get; set; }

        /// <inheritdoc/>
        public AbsolutePath GetAbsolutePath(PathTable pathTable)
        {
            Contract.Assert(Path.AbsolutePath != null);

            if (!m_absolutePath.IsValid)
            {
                m_absolutePath = AbsolutePath.Create(pathTable, Path.AbsolutePath);
            }

            return m_absolutePath;
        }

        /// <inheritdoc/>
        public TextSource Text
        {
            get
            {
                if (m_textSource == null)
                {
                    lock (this)
                    {
                        if (m_textSource == null)
                        {
                            m_textSource = m_textSourceProvider?.ReadTextSource();
                        }
                    }
                }

                return m_textSource;
            }
        }

        /// <inheritdoc/>
        public IAmdDependency[] AmdDependencies { get; set; }

        /// <inheritdoc/>
        public string ModuleName { get; set; }

        /// <inheritdoc/>
        public IFileReference[] ReferencedFiles { get; set; }

        /// <inheritdoc/>
        public LanguageVariant LanguageVariant { get; set; }

        /// <inheritdoc/>
        public Optional<Map<string>> RenamedDependencies { get; set; }

        /// <inheritdoc/>
        public bool HasNoDefaultLib { get; set; }

        /// <inheritdoc/>
        public ScriptTarget LanguageVersion { get; set; }

        /// <inheritdoc/>
        public INode ExternalModuleIndicator { get; set; }

        /// <inheritdoc/>
        public INode CommonJsModuleIndicator { get; set; }

        /// <inheritdoc/>
        public int NodeCount { get; set; }

        /// <inheritdoc/>
        public int IdentifierCount { get; set; }

        /// <inheritdoc/>
        public int SymbolCount { get; set; }

        /// <inheritdoc/>
        public IReadOnlyList<Diagnostic> ParseDiagnostics
        {
            get { return m_parseDiagnostics ?? s_emptyDiagnostics; }
            internal set { m_parseDiagnostics = value.ToList(); }
        }

        /// <inheritdoc/>
        public List<Diagnostic> BindDiagnostics
        {
            get
            {
                return m_bindDiagnostics = m_bindDiagnostics ?? new List<Diagnostic>();
            }
        }

        /// <inheritdoc/>
        public IReadOnlyList<Diagnostic> ModuleResolutionDiagnostics { get; set; }

        /// <nodoc/>
        public void SetLineMap(int[] lines)
        {
            m_lineMap = new LineMap(lines, BackslashesAllowedInPathInterpolation);
        }

        /// <inheritdoc/>
        public LineMap LineMap => m_lineMap;

        /// <inheritdoc/>
        public Optional<Map<string>> ClassifiableNames { get; set; }

        /// <inheritdoc/>
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly", Justification = "Necessary functionality")]
        public Map<IResolvedModule> ResolvedModules { get; set; }

        /// <inheritdoc/>
        public ILiteralExpression[] Imports { get; set; }

        /// <inheritdoc/>
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly", Justification = "Necessary functionality")]
        public List<ILiteralExpression> LiteralLikeSpecifiers { get; set; }

        /// <inheritdoc/>
        public override ISymbol Symbol { get; set; }

        /// <inheritdoc/>
        public bool DeclaresRootQualifier { get; set; }

        /// <inheritdoc/>
        public bool DeclaresInjectedTopLevelWithQualifier { get; set; }

        /// <inheritdoc/>
        public string ToDisplayStringV2()
        {
            return this.GetFormattedText();
        }

        /// <inheritdoc/>
        public bool BackslashesAllowedInPathInterpolation { get; set; }

        /// <nodoc/>
        public SourceFile([JetBrains.Annotations.NotNull]ITextSourceProvider textSourceProvider)
        {
            Contract.Requires(textSourceProvider != null);
            m_textSourceProvider = textSourceProvider;
        }

        /// <nodoc/>
        public SourceFile(params IStatement[] statements)
        {
            Kind = SyntaxKind.SourceFile;
            Statements = new NodeArray<IStatement>(statements);
        }

        /// <summary>
        /// Creates an empty source file with the default options
        /// </summary>
        public static SourceFile Create(string fileName)
        {
            // code from createNode is inlined here so createNode won't have to deal with special case of creating source files
            // this is quite rare comparing to other nodes and createNode should be as fast as possible
            var sourceFile = new SourceFile().Construct(SyntaxKind.SourceFile, /*pos*/ 0, /* end */ 0);
            sourceFile.Path = Path.Absolute(fileName);

            sourceFile.LanguageVersion = ScriptTarget.Es2015;

            // Original line from TS port:
            //   sourceFile.FileName = Core.NormalizePath(fileName);
            // Removed since we want to preserve OS-dependent separators and, besides some testing-related invocations, the path is already normalized since it comes
            // from a path table
            sourceFile.FileName = fileName;
            sourceFile.Flags = Path.FileExtensionIs(sourceFile.FileName, ".d.ts") ? NodeFlags.DeclarationFile : NodeFlags.None;
            sourceFile.LanguageVariant = LanguageVariant.Standard;

            sourceFile.LiteralLikeSpecifiers = new List<ILiteralExpression>();
            sourceFile.ResolvedModules = new Map<IResolvedModule>();

            sourceFile.SourceFile = sourceFile;
            sourceFile.BackslashesAllowedInPathInterpolation = true;

            return sourceFile;
        }

        /// <inheritdoc/>
        public bool IsDeclarationFile
        {
            get { throw new NotImplementedException(); }
            set { throw new NotImplementedException(); }
        }

        /// <inheritdoc/>
        public bool Equals(ISourceFile other)
        {
            // TODO:SQ: Figure out how to determine equality!
            return this == other;
        }

        /// <inheritdoc/>
        public IReadOnlyDictionary<INode, Trivia> PerNodeTrivia => m_perNodeTrivias;

        /// <summary>
        /// Adds a parse diagnostic to the source file.
        /// </summary>
        /// <remarks>
        /// This is intentionally not exposed in ISourceFile.
        /// </remarks>
        public void AddParseDiagnostic(Diagnostic diagnostic)
        {
            if (m_parseDiagnostics == null)
            {
                m_parseDiagnostics = new List<Diagnostic>();
            }

            m_parseDiagnostics.Add(diagnostic);
        }
    }

    /// <nodoc/>
    public sealed partial class ComputedPropertyName : Node, IComputedPropertyName
    {
        /// <inheritdoc/>
        public IExpression Expression { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.ComputedPropertyName;
    }

    /// <summary>
    /// Type that holds extra information occasionally required by <see cref="LiteralExpression"/>.
    /// </summary>
    public class LiteralExpressionExtraState : NodeExtraState
    {
        /// <nodoc />
        public bool IsUnterminated { get; set; }

        /// <nodoc />
        public bool HasExtendedUnicodeEscape { get; set; }

        /// <nodoc />
        public LiteralExpressionKind LiteralKind { get; set; }

        /// <nodoc />
        public ISymbol ResolvedSymbol { get; set; }
    }

    /// <nodoc/>
    public sealed partial class LiteralExpression : NodeBase<LiteralExpressionExtraState>, ILiteralExpression, IStringLiteral, IIdentifier
    {
        /// <inheritdoc/>
        public string Text { get; set; }

        /// <inheritdoc/>
        SyntaxKind IIdentifier.OriginalKeywordKind { get { return SyntaxKind.Unknown; } set { throw new NotSupportedException(); } }

        /// <nodoc/>
        public override SyntaxKind Kind { get { return m_kind; } set { m_kind = value; } } // Kind could be different for this node.

        /// <inheritdoc/>
        public bool IsUnterminated
        {
            get { return m_extraState?.IsUnterminated ?? false; }
            set { ExtraState.IsUnterminated = value; }
        }

        /// <inheritdoc/>
        public bool HasExtendedUnicodeEscape
        {
            get { return m_extraState?.HasExtendedUnicodeEscape ?? false; }
            set { ExtraState.HasExtendedUnicodeEscape = value; }
        }

        /// <nodoc/>
        public override ISymbol ResolvedSymbol
        {
            get { return m_extraState?.ResolvedSymbol; }
            set { if (value != null || m_extraState != null) ExtraState.ResolvedSymbol = value; }
        }

        /// <inheritdoc/>
        public LiteralExpressionKind LiteralKind { get; set; }

        /// <nodoc/>
        public LiteralExpression()
        {
        }

        /// <nodoc/>
        public LiteralExpression(string text, LiteralExpressionKind literalKind = LiteralExpressionKind.DoubleQuote)
        {
            Text = text;
            LiteralKind = literalKind;
            Kind = SyntaxKind.StringLiteral;
        }

        /// <nodoc/>
        public LiteralExpression(int number)
        {
            Text = number.ToString(CultureInfo.InvariantCulture);
            LiteralKind = LiteralExpressionKind.None;
            Kind = SyntaxKind.NumericLiteral;
        }

        /// <nodoc/>
        public static IReadOnlyDictionary<LiteralExpressionKind, char> LiteralExpressionToCharMap => s_literalExpressionToChar;

        private static readonly Dictionary<LiteralExpressionKind, char> s_literalExpressionToChar = new Dictionary<LiteralExpressionKind, char>
        {
            [LiteralExpressionKind.BackTick] = '`',
            [LiteralExpressionKind.DoubleQuote] = '\"',
            [LiteralExpressionKind.SingleQuote] = '\'',
        };
    }

    /// <nodoc/>
    public sealed partial class ConditionalExpression : NodeBase<NodeExtraState>, IConditionalExpression
    {
        /// <inheritdoc/>
        public IExpression Condition { get; set; }

        /// <inheritdoc/>
        public INode QuestionToken { get; set; }

        /// <inheritdoc/>
        public IExpression WhenTrue { get; set; }

        /// <inheritdoc/>
        public INode ColonToken { get; set; }

        /// <inheritdoc/>
        public IExpression WhenFalse { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.ConditionalExpression;
    }

    /// <nodoc/>
    public sealed partial class SwitchExpression : NodeBase<NodeExtraState>, ISwitchExpression
    {
        /// <inheritdoc />
        public IExpression Expression { get; set; }

        /// <inheritdoc />
        public NodeArray<ISwitchExpressionClause> Clauses { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.SwitchExpression;
    }

    /// <nodoc/>
    public sealed partial class SwitchExpressionClause : NodeBase<NodeExtraState>, ISwitchExpressionClause
    {
        /// <summary>
        /// This indicates the clause is the default case. as in: `default: 10`.
        /// This means the Match expression will be null.
        /// </summary>
        public bool IsDefaultFallthrough { get; set; }

        /// <inheritdoc />
        public IExpression Match { get; set; }

        /// <inheritdoc />
        public IExpression Expression { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.SwitchExpressionClause;
    }

    /// <nodoc/>
    public sealed partial class BinaryExpression : NodeBase<NodeExtraState>, IBinaryExpression
    {
        /// <inheritdoc/>
        public DeclarationName Name { get; set; }

        /// <inheritdoc/>
        public IExpression Left { get; set; }

        /// <inheritdoc/>
        public INode OperatorToken { get; set; }

        /// <inheritdoc/>
        public IExpression Right { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.BinaryExpression;

        /// <nodoc/>
        public BinaryExpression()
        {
        }

        /// <nodoc/>
        public BinaryExpression(IExpression left, SyntaxKind operatorToken, IExpression right)
        {
            Left = left;
            OperatorToken = new TokenNode()
            {
                Kind = operatorToken,
            };
            Right = right;
        }
    }

    /// <nodoc/>
    public sealed partial class ParameterDeclaration : Node, IParameterDeclaration, IVariableLikeDeclaration
    {
        /// <inheritdoc/>
        public Optional<INode> DotDotDotToken { get; set; }

        /// <inheritdoc/>
        public IdentifierOrBindingPattern Name { get; set; }

        /// <inheritdoc/>
        public Optional<INode> QuestionToken { get; set; }

        /// <nodoc/>
        [SuppressMessage("Microsoft.Naming", "CA1721:PropertyNamesShouldNotMatchGetMethods", Justification = "Type nomenclature is necessary within a compiler.")]
        public ITypeNode Type { get; set; }

        /// <nodoc/>
        public IExpression Initializer { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.Parameter;

        /// <inheritdoc/>
        DeclarationName IDeclaration.Name => DeclarationName.PropertyName(Name);

        /// <inheritdoc/>
        PropertyName IVariableLikeDeclaration.PropertyName => null; // PropertyName.Identifier(Name.As<IIdentifier>());

        /// <inheritdoc/>
        Optional<INode> IVariableLikeDeclaration.DotDotDotToken => DotDotDotToken;

        /// <inheritdoc/>
        Optional<INode> IVariableLikeDeclaration.QuestionToken => QuestionToken;

        /// <inheritdoc/>
        public bool Equals(IParameterDeclaration other)
        {
            if (other != null)
            {
                // TODO:SQ: How do you decide that 2 parameter declarations are equal (without doing deep comparison)?
                if (Name.GetText().Equals(other.Name.GetText()))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <nodoc/>
    public sealed partial class AsExpression : NodeBase<NodeExtraState>, IAsExpression
    {
        /// <inheritdoc/>
        public IExpression Expression { get; set; }

        /// <inheritdoc/>
        [SuppressMessage("Microsoft.Naming", "CA1721:PropertyNamesShouldNotMatchGetMethods", Justification = "Type nomenclature is necessary within a compiler.")]
        public ITypeNode Type { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.AsExpression;
    }

    /// <nodoc/>
    public sealed partial class PrefixUnaryExpression : NodeBase<NodeExtraState>, IPrefixUnaryExpression
    {
        /// <inheritdoc/>
        public SyntaxKind Operator { get; set; }

        /// <inheritdoc/>
        public IUnaryExpression Operand { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.PrefixUnaryExpression;
    }

    /// <nodoc/>
    public sealed partial class PostfixUnaryExpression : NodeBase<NodeExtraState>, IPostfixUnaryExpression
    {
        /// <inheritdoc/>
        public ILeftHandSideExpression Operand { get; set; }

        /// <inheritdoc/>
        public SyntaxKind Operator { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.PostfixUnaryExpression;
    }

    /// <nodoc/>
    public sealed partial class DeleteExpression : NodeBase<NodeExtraState>, IDeleteExpression
    {
        /// <inheritdoc/>
        public IUnaryExpression Expression { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.DeleteExpression;
    }

    /// <nodoc/>
    public sealed partial class TypeOfExpression : NodeBase<NodeExtraState>, ITypeOfExpression
    {
        /// <inheritdoc/>
        public IUnaryExpression Expression { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.TypeOfExpression;
    }

    /// <nodoc/>
    public sealed partial class VoidExpression : NodeBase<NodeExtraState>, IVoidExpression
    {
        /// <inheritdoc/>
        public IUnaryExpression Expression { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.VoidExpression;
    }

    /// <nodoc/>
    public sealed partial class TypeAssertion : NodeBase<NodeExtraState>, ITypeAssertion
    {
        /// <inheritdoc/>
        [SuppressMessage("Microsoft.Naming", "CA1721:PropertyNamesShouldNotMatchGetMethods", Justification = "Type nomenclature is necessary within a compiler.")]
        public ITypeNode Type { get; set; }

        /// <inheritdoc/>
        public IUnaryExpression Expression { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.TypeAssertionExpression;
    }

    /// <nodoc/>
    public sealed partial class YieldExpression : NodeBase<NodeExtraState>, IYieldExpression
    {
        /// <inheritdoc/>
        public INode AsteriskToken { get; set; }

        /// <inheritdoc/>
        public IExpression Expression { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.YieldExpression;
    }

    /// <nodoc/>
    public sealed partial class ModuleDeclaration : Node, IModuleDeclaration
    {
        /// <inheritdoc/>
        public ModuleBody Body { get; set; }

        /// <inheritdoc/>
        public IdentifierOrLiteralExpression Name { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.ModuleDeclaration;

        /// <nodoc/>
        public ModuleDeclaration()
        {
        }

        /// <nodoc/>
        public ModuleDeclaration(string name, params IStatement[] statements)
        {
            Name = new IdentifierOrLiteralExpression(new Identifier(name));
            Body = new ModuleBody(new ModuleBlock(statements));
            Flags = NodeFlags.Export | NodeFlags.Namespace;
        }

        /// <nodoc/>
        public ModuleDeclaration(string[] dottedNames, params IStatement[] statements)
        {
            Contract.Requires(dottedNames != null);
            Contract.Requires(dottedNames.Length > 0);

            var currentModuleDecl = new ModuleDeclaration(dottedNames[dottedNames.Length - 1], statements);

            for (int i = dottedNames.Length - 2; i >= 0; i--)
            {
                currentModuleDecl = new ModuleDeclaration()
                {
                    Kind = SyntaxKind.ModuleDeclaration,
                    Name = new IdentifierOrLiteralExpression(new Identifier(dottedNames[i])),
                    Body = new ModuleBody(currentModuleDecl),
                    Flags = NodeFlags.Export | NodeFlags.Namespace,
                };
            }

            // Clone the last one into this class.
            Kind = currentModuleDecl.Kind;
            Name = currentModuleDecl.Name;
            Body = currentModuleDecl.Body;
            Flags = currentModuleDecl.Flags;
        }

        private DeclarationName m_name;

        /// <inheritdoc/>
        DeclarationName IDeclaration.Name => m_name ?? (m_name = DeclarationName.PropertyName(Name));

        /// <inheritdoc/>
        IIdentifier IDeclarationStatement.Name => Name;

        /// <nodoc/>
        public override ISymbolTable Locals { get; set; }

        /// <nodoc/>
        public override NodeFlags Flags { get; set; }

        /// <nodoc/>
        public override ISymbol Symbol { get; set; }

        /// <nodoc/>
        public override ISymbol LocalSymbol { get; set; }
    }

    /// <nodoc/>
    public sealed partial class ModuleBlock : Node, IModuleBlock, IBlock
    {
        /// <inheritdoc/>
        public NodeArray<IStatement> Statements { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.ModuleBlock;

        /// <nodoc/>
        public ModuleBlock()
        {
        }

        /// <nodoc/>
        public ModuleBlock(params IStatement[] statements)
        {
            Statements = new NodeArray<IStatement>(statements);
        }
    }

    /// <nodoc/>
    public sealed partial class FunctionOrConstructorTypeNode : Node, IFunctionOrConstructorTypeNode
    {
        /// <inheritdoc/>
        public NodeArray<ITypeParameterDeclaration> TypeParameters { get; set; }

        /// <inheritdoc/>
        public NodeArray<IParameterDeclaration> Parameters { get; set; }

        /// <inheritdoc/>
        [SuppressMessage("Microsoft.Naming", "CA1721:PropertyNamesShouldNotMatchGetMethods", Justification = "Type nomenclature is necessary within a compiler.")]
        public ITypeNode Type { get; set; }

        /// <inheritdoc/>
        public PropertyName Name { get; set; }

        /// <inheritdoc/>
        public Optional<INode> AsteriskToken { get; }

        /// <inheritdoc/>
        public Optional<INode> QuestionToken { get; }

        /// <inheritdoc/>
        public ConciseBody Body { get; }

        /// <nodoc/>
        public override SyntaxKind Kind { get { return m_kind; } set { m_kind = value; } }

        /// <inheritdoc/>
        DeclarationName IDeclaration.Name => Name;

        /// <inheritdoc/>
        public bool Equals(ITypeNode other)
        {
            throw PlaceHolder.NotImplemented();
        }
    }

    /// <nodoc/>
    public sealed partial class CaseClause : Node, ICaseClause, IExpressionStatement
    {
        /// <nodoc/>
        public IExpression Expression { get; set; }

        /// <inheritdoc/>
        public NodeArray<IStatement> Statements { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.CaseClause;

        /// <nodoc/>
        public CaseClause()
        {
        }

        /// <nodoc/>
        public CaseClause(IExpression expression, params IStatement[] statements)
        {
            Expression = expression;
            Statements = new NodeArray<IStatement>(statements);
        }

        /// <nodoc/>
        public CaseClause(IExpression expression, List<IStatement> statements)
        {
            Expression = expression;
            Statements = new NodeArray<IStatement>(statements);
        }
    }

    /// <nodoc/>
    public sealed partial class ArrayTypeNode : Node, IArrayTypeNode
    {
        /// <inheritdoc/>
        public ITypeNode ElementType { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.ArrayType;

        /// <inheritdoc/>
        public bool Equals(ITypeNode other)
        {
            throw new NotImplementedException();
        }
    }

    /// <nodoc/>
    public sealed partial class DefaultClause : Node, IDefaultClause
    {
        /// <inheritdoc/>
        public NodeArray<IStatement> Statements { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.DefaultClause;

        /// <nodoc/>
        public DefaultClause()
        {
        }

        /// <nodoc/>
        public DefaultClause(params IStatement[] statements)
        {
            Statements = new NodeArray<IStatement>(statements);
        }

        /// <nodoc/>
        public DefaultClause(List<IStatement> statements)
        {
            Statements = new NodeArray<IStatement>(statements);
        }
    }

    /// <nodoc/>
    public sealed partial class UnionOrIntersectionTypeNode : Node, IUnionOrIntersectionTypeNode, IUnionTypeNode, IIntersectionTypeNode
    {
        /// <inheritdoc/>
        public NodeArray<ITypeNode> Types { get; set; }

        /// <nodoc/>
        public override SyntaxKind Kind { get { return m_kind; } set { m_kind = value; } }

        /// <inheritdoc/>
        public bool Equals(ITypeNode other)
        {
            throw new NotImplementedException();
        }
    }

    /// <nodoc/>
    public sealed partial class TypeNode : Node, ITypeNode
    {
        /// <nodoc/>
        public override SyntaxKind Kind { get { return m_kind; } set { m_kind = value; } }

        /// <inheritdoc/>
        public bool Equals(ITypeNode other)
        {
            throw new NotImplementedException();
        }
    }

    /// <nodoc/>
    public sealed partial class TypeReferenceNode : Node, ITypeReferenceNode
    {
        /// <inheritdoc/>
        public EntityName TypeName { get; set; }

        /// <inheritdoc/>
        public NodeArray<ITypeNode> TypeArguments { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.TypeReference;

        /// <nodoc/>
        public TypeReferenceNode()
        {
        }

        /// <nodoc/>
        public TypeReferenceNode(string name)
        {
            TypeName = new EntityName(new Identifier(name));
        }

        /// <nodoc/>
        public TypeReferenceNode(string name, params string[] names)
        {
            TypeName = new EntityName(new Identifier(name));
            for (int i = 0; i < names.Length; i++)
            {
                TypeName = new EntityName(new QualifiedName(TypeName, names[i]));
            }
        }

        /// <inheritdoc/>
        public bool Equals(ITypeNode other)
        {
            throw new NotImplementedException();
        }
    }

    /// <nodoc/>
    public sealed partial class TypePredicateNode : Node, ITypePredicateNode
    {
        /// <inheritdoc/>
        public IdentifierOrThisTypeUnionNode ParameterName { get; set; }

        /// <inheritdoc/>
        [SuppressMessage("Microsoft.Naming", "CA1721:PropertyNamesShouldNotMatchGetMethods", Justification = "Type nomenclature is necessary within a compiler.")]
        public ITypeNode Type { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.TypePredicate;

        /// <inheritdoc/>
        public bool Equals(ITypeNode other)
        {
            throw new NotImplementedException();
        }
    }

    /// <nodoc/>
    public sealed partial class TypeParameterDeclaration : Node, ITypeParameterDeclaration
    {
        /// <inheritdoc/>
        public ITypeNode Constraint { get; set; }

        /// <inheritdoc/>
        public IExpression Expression { get; set; }

        /// <inheritdoc/>
        public IIdentifier Name { get; set; }

        private DeclarationName m_name;

        /// <inheritdoc/>
        DeclarationName IDeclaration.Name => m_name ?? (m_name = DeclarationName.PropertyName(Name));

        /// <nodoc/>
        public override SyntaxKind Kind { get { return m_kind; } set { m_kind = value; } }
    }

    /// <nodoc/>
    public sealed partial class ThisTypeNode : Node, IThisTypeNode
    {
        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.ThisType;

        /// <inheritdoc/>
        public bool Equals(ITypeNode other)
        {
            throw new NotImplementedException();
        }
    }

    /// <nodoc/>
    public sealed partial class TypeQueryNode : Node, ITypeQueryNode
    {
        /// <inheritdoc/>
        public EntityName ExprName { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.TypeQuery;

        /// <inheritdoc/>
        public bool Equals(ITypeNode other)
        {
            throw new NotImplementedException();
        }
    }

    /// <nodoc/>
    public sealed partial class TypeLiteralNode : Node, ITypeLiteralNode
    {
        /// <nodoc />
        public TypeLiteralNode()
        {
        }

        /// <nodoc />
        public TypeLiteralNode(params ITypeElement[] members)
        {
            Members = new NodeArray<ITypeElement>(members);
        }

        /// <inheritdoc/>
        public DeclarationName Name { get; set; }

        /// <inheritdoc/>
        public NodeArray<ITypeElement> Members { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.TypeLiteral;

        /// <inheritdoc/>
        public bool Equals(ITypeNode other)
        {
            throw new NotImplementedException();
        }
    }

    /// <nodoc/>
    public sealed partial class TupleTypeNode : Node, ITupleTypeNode
    {
        /// <inheritdoc/>
        public NodeArray<ITypeNode> ElementTypes { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.TupleType;

        /// <inheritdoc/>
        public bool Equals(ITypeNode other)
        {
            throw new NotImplementedException();
        }
    }

    /// <nodoc/>
    public sealed partial class ParenthesizedTypeNode : Node, IParenthesizedTypeNode
    {
        /// <inheritdoc/>
        [SuppressMessage("Microsoft.Naming", "CA1721:PropertyNamesShouldNotMatchGetMethods", Justification = "Type nomenclature is necessary within a compiler.")]
        public ITypeNode Type { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.ParenthesizedType;

        /// <inheritdoc/>
        public bool Equals(ITypeNode other)
        {
            throw new NotImplementedException();
        }
    }

    /// <nodoc/>
    public sealed partial class InterfaceDeclaration : Node, IInterfaceDeclaration
    {
        /// <inheritdoc/>
        [CanBeNull]
        public NodeArray<ITypeParameterDeclaration> TypeParameters { get; set; }

        /// <inheritdoc/>
        [JetBrains.Annotations.NotNull]
        public NodeArray<IHeritageClause> HeritageClauses { get; set; }

        /// <inheritdoc/>
        [JetBrains.Annotations.NotNull]
        public NodeArray<ITypeElement> Members { get; set; }

        /// <nodoc/>
        public override NodeFlags Flags { get; set; }

        /// <inheritdoc/>
        public IIdentifier Name { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.InterfaceDeclaration;

        private DeclarationName m_name;

        /// <inheritdoc/>
        DeclarationName IDeclaration.Name => m_name ?? (m_name = DeclarationName.PropertyName(Name));

        /// <inheritdoc/>
        IIdentifier IDeclarationStatement.Name => Name;

        /// <nodoc/>
        public override string ToDisplayString()
        {
            return ReformatterHelper.FormatOnlyFunctionHeader(this);
        }
    }

    /// <nodoc/>
    public sealed partial class HeritageClause : Node, IHeritageClause
    {
        /// <inheritdoc/>
        public SyntaxKind Token { get; set; }

        /// <inheritdoc/>
        public NodeArray<IExpressionWithTypeArguments> Types { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.HeritageClause;
    }

    /// <nodoc/>
    public sealed partial class ExpressionWithTypeArguments : Node, IExpressionWithTypeArguments
    {
        /// <inheritdoc/>
        public ILeftHandSideExpression Expression { get; set; }

        /// <inheritdoc/>
        public NodeArray<ITypeNode> TypeArguments { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.ExpressionWithTypeArguments;

        /// <inheritdoc/>
        public bool Equals(ITypeNode other)
        {
            throw new NotImplementedException();
        }
    }

    /// <nodoc/>
    public sealed partial class CallSignatureDeclarationOrConstructSignatureDeclaration : Node, ICallSignatureDeclarationOrConstructSignatureDeclaration
    {
        /// <nodoc/>
        public Optional<INode> QuestionToken { get; set; }

        /// <inheritdoc/>
        public NodeArray<ITypeParameterDeclaration> TypeParameters { get; set; }

        /// <inheritdoc/>
        public NodeArray<IParameterDeclaration> Parameters { get; set; }

        /// <inheritdoc/>
        [SuppressMessage("Microsoft.Naming", "CA1721:PropertyNamesShouldNotMatchGetMethods", Justification = "Type nomenclature is necessary within a compiler.")]
        public ITypeNode Type { get; set; }

        /// <inheritdoc/>
        public PropertyName Name { get; set; }

        /// <inheritdoc/>
        public Optional<INode> AsteriskToken { get; }

        /// <inheritdoc/>
        public ConciseBody Body { get; }

        /// <nodoc/>
        public override SyntaxKind Kind
        {
            get { return m_kind; }
            set { m_kind = value; }
        }

        /// <inheritdoc/>
        DeclarationName IDeclaration.Name => Name;
    }

    /// <nodoc/>
    public sealed partial class IndexSignatureDeclaration : Node, IIndexSignatureDeclaration
    {
        /// <nodoc/>
        public Optional<INode> QuestionToken { get; set; }

        /// <inheritdoc/>
        public NodeArray<ITypeParameterDeclaration> TypeParameters { get; set; }

        /// <inheritdoc/>
        public NodeArray<IParameterDeclaration> Parameters { get; set; }

        /// <inheritdoc/>
        [SuppressMessage("Microsoft.Naming", "CA1721:PropertyNamesShouldNotMatchGetMethods", Justification = "Type nomenclature is necessary within a compiler.")]
        public ITypeNode Type { get; set; }

        /// <inheritdoc/>
        public PropertyName Name { get; set; }

        /// <inheritdoc/>
        public Optional<INode> AsteriskToken { get; }

        /// <inheritdoc/>
        public ConciseBody Body { get; }

        /// <nodoc/>
        public override SyntaxKind Kind { get { return m_kind; } set { m_kind = value; } }

        /// <inheritdoc/>
        DeclarationName IDeclaration.Name => Name;
    }

    /// <nodoc/>
    public sealed partial class PropertySignature : Node, IPropertySignature, IVariableLikeDeclaration, IPropertyDeclaration
    {
        /// <nodoc />
        public PropertySignature()
        {
        }

        /// <nodoc />
        public PropertySignature(string name, ITypeNode type, IExpression initializer = null)
        {
            Name = new PropertyName(new Identifier(name));
            Type = type;
            Initializer = initializer;
        }

        /// <nodoc />
        [SuppressMessage("Microsoft.Naming", "CA1721:PropertyNamesShouldNotMatchGetMethods", Justification = "Type nomenclature is necessary within a compiler.")]
        public ITypeNode Type { get; set; }

        /// <nodoc />
        public IExpression Initializer { get; set; }

        /// <nodoc />
        public PropertyName Name { get; set; }

        /// <nodoc />
        public Optional<INode> QuestionToken { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.PropertySignature;

        /// <inheritdoc/>
        DeclarationName IDeclaration.Name => DeclarationName.PropertyName(Name);

        /// <inheritdoc/>
        PropertyName IVariableLikeDeclaration.PropertyName => null;

        /// <inheritdoc/>
        Optional<INode> IVariableLikeDeclaration.DotDotDotToken => default(Optional<INode>);

        /// <inheritdoc/>
        ITypeNode IVariableLikeDeclaration.Type => Type;
    }

    /// <nodoc/>
    public sealed partial class PropertyDeclaration : Node, IPropertyDeclaration, IVariableLikeDeclaration
    {
        /// <inheritdoc/>
        [SuppressMessage("Microsoft.Naming", "CA1721:PropertyNamesShouldNotMatchGetMethods", Justification = "Type nomenclature is necessary within a compiler.")]
        public ITypeNode Type { get; set; }

        /// <inheritdoc/>
        public IExpression Initializer { get; set; }

        /// <inheritdoc/>
        public PropertyName Name { get; set; }

        /// <inheritdoc/>
        public Optional<INode> QuestionToken { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.PropertyDeclaration;

        /// <inheritdoc/>
        DeclarationName IDeclaration.Name => DeclarationName.PropertyName(Name);

        /// <inheritdoc/>
        PropertyName IVariableLikeDeclaration.PropertyName => null;

        /// <inheritdoc/>
        Optional<INode> IVariableLikeDeclaration.DotDotDotToken => default(Optional<INode>);
    }

    /// <nodoc/>
    // HINT: added IMethodDeclaration to avoid failure in runtime at CheckSourceElement function
    public sealed partial class MethodSignature : Node, IMethodSignature, IFunctionLikeDeclaration, IMethodDeclaration
    {
        /// <inheritdoc/>
        public PropertyName Name { get; set; }

        /// <inheritdoc/>
        public Optional<INode> QuestionToken { get; set; }

        /// <inheritdoc/>
        public NodeArray<ITypeParameterDeclaration> TypeParameters { get; set; }

        /// <inheritdoc/>
        public NodeArray<IParameterDeclaration> Parameters { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.MethodSignature;

        /// <inheritdoc/>
        [SuppressMessage("Microsoft.Naming", "CA1721:PropertyNamesShouldNotMatchGetMethods", Justification = "Type nomenclature is necessary within a compiler.")]
        public ITypeNode Type { get; set; }

        /// <inheritdoc/>
        DeclarationName IDeclaration.Name => DeclarationName.PropertyName(Name);

        /// <inheritdoc/>
        PropertyName ISignatureDeclaration.Name => Name;

        /// <inheritdoc/>
        Optional<INode> IFunctionLikeDeclaration.AsteriskToken => default(Optional<INode>);

        /// <inheritdoc/>
        ConciseBody IFunctionLikeDeclaration.Body => null;
    }

    /// <nodoc/>
    public sealed partial class AccessorDeclaration : Node, IAccessorDeclaration, IFunctionLikeDeclaration
    {
        /// <nodoc/>
        public IBlock Body { get; set; }

        /// <inheritdoc/>
        public PropertyName Name { get; set; }

        /// <inheritdoc/>
        public NodeArray<ITypeParameterDeclaration> TypeParameters { get; set; }

        /// <inheritdoc/>
        public NodeArray<IParameterDeclaration> Parameters { get; set; }

        /// <inheritdoc/>
        [SuppressMessage("Microsoft.Naming", "CA1721:PropertyNamesShouldNotMatchGetMethods", Justification = "Type nomenclature is necessary within a compiler.")]
        public ITypeNode Type { get; set; }

        /// <inheritdoc/>
        public override SyntaxKind Kind
        {
            get { return m_kind; }
            set { m_kind = value; }
        }

        /// <inheritdoc/>
        Optional<INode> IFunctionLikeDeclaration.AsteriskToken { get; }

        /// <inheritdoc/>
        Optional<INode> IFunctionLikeDeclaration.QuestionToken { get; }

        /// <inheritdoc/>
        DeclarationName IDeclaration.Name => DeclarationName.PropertyName(Name);

        /// <inheritdoc/>
        ConciseBody IFunctionLikeDeclaration.Body => ConciseBody.Block(Body);
    }

    /// <summary>
    /// Extra state for <see cref="PropertyAssignment"/> node.
    /// </summary>
    public class PropertyAssignmentExtraState : NodeExtraState
    {
        /// <nodoc />
        public INode QuestionToken { get; set; }

        /// <nodoc />
        public IExpression Initializer { get; set; }
    }

    /// <nodoc/>
    public sealed partial class PropertyAssignment : NodeBase<PropertyAssignmentExtraState>, IPropertyAssignment, IVariableLikeDeclaration
    {
        /// <inheritdoc/>
        public Optional<INode> QuestionToken
        {
            get { return m_extraState == null ? default(Optional<INode>) : Optional.Create(m_extraState.QuestionToken); }
            set { if (value.HasValue || m_extraState != null) ExtraState.QuestionToken = value.ValueOrDefault; }
        }

        /// <inheritdoc/>
        public IExpression Initializer { get; set; }

        /// <inheritdoc/>
        public PropertyName Name { get; set; }

        /// <inheritdoc/>
        public override ISymbol Symbol { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.PropertyAssignment;

        /// <nodoc/>
        public PropertyAssignment()
        {
        }

        /// <nodoc/>
        public PropertyAssignment(string name, IExpression initializer)
        {
            Initializer = initializer;
            Name = new PropertyName(new Identifier(name));
            Kind = SyntaxKind.PropertyAssignment;
        }

        private DeclarationName m_propertyName;

        /// <inheritdoc/>
        DeclarationName IDeclaration.Name => m_propertyName ?? (m_propertyName = DeclarationName.PropertyName(Name));

        /// <inheritdoc/>
        PropertyName IVariableLikeDeclaration.PropertyName => null;

        /// <inheritdoc/>
        Optional<INode> IVariableLikeDeclaration.DotDotDotToken => default(Optional<INode>);

        /// <inheritdoc/>
        ITypeNode IVariableLikeDeclaration.Type => null;
    }

    /// <nodoc/>
    public sealed partial class ShorthandPropertyAssignment : Node, IShorthandPropertyAssignment, IVariableLikeDeclaration, IPropertyAssignment
    {
        /// <inheritdoc/>
        public Optional<INode> QuestionToken { get; set; }

        /// <inheritdoc/>
        public IExpression Initializer
        {
            get { return ObjectAssignmentInitializer; }
            set { ObjectAssignmentInitializer = value; }
        }

        /// <inheritdoc/>
        public Optional<INode> EqualsToken { get; set; }

        /// <inheritdoc/>
        public IExpression ObjectAssignmentInitializer { get; set; }

        /// <inheritdoc/>
        public PropertyName Name { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.ShorthandPropertyAssignment;

        /// <inheritdoc/>
        DeclarationName IDeclaration.Name => Name;

        /// <inheritdoc/>
        PropertyName IVariableLikeDeclaration.PropertyName => null;

        /// <inheritdoc/>
        Optional<INode> IVariableLikeDeclaration.DotDotDotToken => default(Optional<INode>);

        /// <inheritdoc/>
        ITypeNode IVariableLikeDeclaration.Type => null;

        /// <inheritdoc/>
        IExpression IVariableLikeDeclaration.Initializer => ObjectAssignmentInitializer;
    }

    /// <nodoc/>
    public sealed partial class MethodDeclaration : FunctionLikeDeclarationBase, IMethodDeclaration
    {
        /// <nodoc/>
        public new PropertyName Name { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.MethodDeclaration;

        /// <inheritdoc/>
        ConciseBody IFunctionLikeDeclaration.Body => ConciseBody.Block(Body);
    }

    /// <nodoc/>
    public partial class TaggedTemplateExpression : NodeBaseLight, ITaggedTemplateExpression
    {
        /// <nodoc/>
        public ILeftHandSideExpression Tag { get; set; }

        /// <inheritdoc/>
        public LiteralExpressionOrTemplateExpression Template
        {
            get
            {
                var literal = TemplateExpression.As<ILiteralExpression>();
                if (literal != null)
                {
                    return new LiteralExpressionOrTemplateExpression(literal);
                }

                return new LiteralExpressionOrTemplateExpression(TemplateExpression.Cast<ITemplateExpression>());
            }

            set => TemplateExpression = (IPrimaryExpression)value.Node;
        }

        /// <inheritdoc/>
        public IPrimaryExpression TemplateExpression { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.TaggedTemplateExpression;

        /// <nodoc/>
        public TaggedTemplateExpression()
        {
        }

        /// <nodoc/>
        public TaggedTemplateExpression(string tag, string template)
        {
            Tag = new Identifier(tag);
            TemplateExpression = new LiteralExpressionOrTemplateExpression(
                new LiteralExpression(template)
                {
                    Kind = SyntaxKind.FirstTemplateToken,
                    LiteralKind = LiteralExpressionKind.BackTick,
                });
        }

        /// <nodoc/>
        public TaggedTemplateExpression(string tag, IExpression first, string second)
        {
            Tag = new Identifier(tag);
            TemplateExpression = new LiteralExpressionOrTemplateExpression(
                new TemplateExpression(
                    new TemplateSpan(first, second, true)));
        }
    }

    /// <nodoc/>
    public sealed partial class TemplateExpression : NodeBaseLight, ITemplateExpression
    {
        /// <inheritdoc/>
        public ITemplateLiteralFragment Head { get; set; }

        /// <inheritdoc/>
        public INodeArray<ITemplateSpan> TemplateSpans { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.TemplateExpression;

        /// <nodoc/>
        public TemplateExpression()
        {
        }

        /// <nodoc/>
        public TemplateExpression(params ITemplateSpan[] spans)
            : this(string.Empty, spans)
        {
        }

        /// <nodoc/>
        public TemplateExpression(string head, params ITemplateSpan[] spans)
        {
            Kind = SyntaxKind.TemplateExpression;
            Head = new TemplateLiteralFragment
            {
                Kind = SyntaxKind.TemplateHead,
                Text = head,
            };
            TemplateSpans = new NodeArray<ITemplateSpan>(spans);
        }
    }

    /// <nodoc/>
    public sealed partial class TemplateSpan : NodeBaseLight, ITemplateSpan
    {
        /// <inheritdoc/>
        public IExpression Expression { get; set; }

        /// <inheritdoc/>
        public ITemplateLiteralFragment Literal { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.TemplateSpan;

        /// <nodoc/>
        public TemplateSpan()
        {
        }

        /// <nodoc/>
        public TemplateSpan(IExpression expression, string literal, bool isLast)
        {
            Expression = expression;
            Literal = new TemplateLiteralFragment()
            {
                Text = literal,
                Kind = isLast ? SyntaxKind.LastTemplateToken : SyntaxKind.TemplateMiddle,
            };
        }
    }

    /// <nodoc/>
    public sealed partial class TemplateLiteralFragment : NodeBaseLight, ITemplateLiteralFragment
    {
        /// <inheritdoc/>
        public string Text { get; set; }

        // The following two fields are using one unused byte from the base class to avoid increasing the size of this type by 8 bytes.

        /// <inheritdoc />
        public bool IsUnterminated
        {
            get { return (m_unused & 1) == 1; }
            set { if (value) { m_unused |= 1; } else { m_unused &= unchecked((byte)(~1)); } }
        }

        /// <inheritdoc />
        public bool HasExtendedUnicodeEscape
        {
            get { return (m_unused & 2) != 0; }
            set { if (value) { m_unused |= 2; } else { m_unused &= unchecked((byte)(~2)); } }
        }

        /// <inheritdoc />
        public override SyntaxKind Kind { get { return m_kind; } set { m_kind = value; } }
    }

    /// <nodoc/>
    public sealed partial class ForInStatement : Node, IForInStatement
    {
        /// <inheritdoc/>
        public IStatement Statement { get; set; }

        /// <inheritdoc/>
        public VariableDeclarationListOrExpression Initializer { get; set; }

        /// <inheritdoc/>
        public IExpression Expression { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.ForInStatement;
    }

    /// <nodoc/>
    public sealed partial class ForStatement : Node, IForStatement
    {
        /// <inheritdoc/>
        public IStatement Statement { get; set; }

        /// <inheritdoc/>
        public VariableDeclarationListOrExpression Initializer { get; set; }

        /// <inheritdoc/>
        public IExpression Condition { get; set; }

        /// <inheritdoc/>
        public IExpression Incrementor { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.ForStatement;
    }

    /// <nodoc/>
    public sealed partial class ForOfStatement : Node, IForOfStatement
    {
        /// <inheritdoc/>
        public IStatement Statement { get; set; }

        /// <inheritdoc/>
        public VariableDeclarationListOrExpression Initializer { get; set; }

        /// <inheritdoc/>
        public IExpression Expression { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.ForOfStatement;
    }

    /// <nodoc/>
    public sealed partial class ElementAccessExpression : NodeBase<NodeExtraState>, IElementAccessExpression
    {
        /// <inheritdoc/>
        public ILeftHandSideExpression Expression { get; set; }

        /// <inheritdoc/>
        public IExpression ArgumentExpression { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.ElementAccessExpression;
    }

    /// <nodoc/>
    public sealed partial class BindingPattern : Node, IBindingPattern
    {
        /// <inheritdoc/>
        public NodeArray<IBindingElement> Elements { get; set; }

        /// <inheritdoc/>
        public override SyntaxKind Kind
        {
            get { return m_kind; }
            set { m_kind = value; }
        }
    }

    /// <nodoc/>
    public sealed partial class BindingElement : Node, IBindingElement, IVariableLikeDeclaration
    {
        /// <inheritdoc/>
        public PropertyName PropertyName { get; set; }

        /// <inheritdoc/>
        public Optional<INode> DotDotDotToken { get; set; }

        /// <inheritdoc/>
        public IdentifierOrBindingPattern Name { get; set; }

        /// <inheritdoc/>
        public override SyntaxKind Kind
        {
            get { return m_kind; }
            set { m_kind = value; }
        }

        /// <inheritdoc/>
        public IExpression Initializer { get; set; }

        /// <inheritdoc/>
        DeclarationName IDeclaration.Name => DeclarationName.PropertyName(Name);

        /// <inheritdoc/>
        Optional<INode> IVariableLikeDeclaration.QuestionToken => default(Optional<INode>);

        /// <inheritdoc/>
        ITypeNode IVariableLikeDeclaration.Type => null;
    }

    /// <nodoc/>
    public sealed partial class StringLiteralTypeNode : NodeBase<LiteralExpressionExtraState>, IStringLiteralTypeNode
    {
        /// <nodoc />
        public StringLiteralTypeNode()
        {
        }

        /// <nodoc />
        public StringLiteralTypeNode(string text)
        {
            Text = text;
        }

        /// <inheritdoc/>
        public string Text { get; set; }

        /// <inheritdoc/>
        public bool IsUnterminated
        {
            get { return m_extraState?.IsUnterminated ?? false; }
            set { ExtraState.IsUnterminated = value; }
        }

        /// <inheritdoc/>
        public bool HasExtendedUnicodeEscape
        {
            get { return m_extraState?.HasExtendedUnicodeEscape ?? false; }
            set { ExtraState.HasExtendedUnicodeEscape = value; }
        }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.StringLiteralType;

        /// <inheritdoc/>
        public bool Equals(ITypeNode other)
        {
            throw new NotImplementedException();
        }

        /// <nodoc />
        public LiteralExpressionKind LiteralKind { get; set; } = LiteralExpressionKind.DoubleQuote;
    }

    /// <nodoc/>
    public sealed partial class NamedImports : Node, INamedImports
    {
        /// <inheritdoc/>
        public NodeArray<IImportSpecifier> Elements { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.NamedImports;

        /// <nodoc />
        public NamedImports()
        {
        }

        /// <nodoc />
        public NamedImports(params string[] names)
        {
            Contract.Assert(names != null);
            var importSpecifiers = new IImportSpecifier[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                importSpecifiers[i] = new ImportSpecifier(names[i]);
            }

            Elements = NodeArray.Create(importSpecifiers);
        }
    }

    /// <nodoc/>
    public sealed partial class NamedExports : Node, INamedExports
    {
        /// <inheritdoc/>
        public NodeArray<IExportSpecifier> Elements { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.NamedExports;
    }

    /// <nodoc/>
    public sealed partial class ImportSpecifier : Node, IImportSpecifier
    {
        /// <inheritdoc/>
        public IIdentifier PropertyName { get; set; }

        /// <inheritdoc/>
        public IIdentifier Name { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.ImportSpecifier;

        private DeclarationName m_name;

        /// <inheritdoc/>
        DeclarationName IDeclaration.Name => m_name ?? (m_name = DeclarationName.PropertyName(Name));

        /// <nodoc />
        public ImportSpecifier()
        {
        }

        /// <nodoc />
        public ImportSpecifier(string name)
        {
            Name = new Identifier(name);
        }

        /// <nodoc />
        public ImportSpecifier(string propertyName, string name)
        {
            PropertyName = new Identifier(propertyName);
            Name = new Identifier(name);
        }
    }

    /// <nodoc/>
    public sealed partial class ExportSpecifier : Node, IExportSpecifier
    {
        /// <inheritdoc/>
        public IIdentifier PropertyName { get; set; }

        /// <inheritdoc/>
        public IIdentifier Name { get; set; }

        /// <inheritdoc/>
        protected override SyntaxKind SyntaxKind => SyntaxKind.ExportSpecifier;

        private DeclarationName m_name;

        /// <inheritdoc/>
        DeclarationName IDeclaration.Name => m_name ?? (m_name = DeclarationName.PropertyName(Name));
    }
}
#pragma warning restore SA1503 // Braces must not be omitted
#pragma warning restore SA1501 // Statement must not be on a single line
