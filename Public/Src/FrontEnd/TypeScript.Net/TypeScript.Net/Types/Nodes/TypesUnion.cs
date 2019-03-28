// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using JetBrains.Annotations;
using TypeScript.Net.Reformatter;
using static TypeScript.Net.Types.NodeUtilities;

#pragma warning disable SA1649 // File name must match first type name

namespace TypeScript.Net.Types
{
    /// <summary>
    /// Interface for union type that wraps two or more nodes together.
    /// </summary>
    /// <remarks>
    /// <see cref="IUnionNode"/> and <see cref="DelegatingUnionNode"/> are solely used to simplify TS to C# migration.
    /// </remarks>
    public interface IUnionNode
    {
        /// <summary>
        /// Return node that was instantiated for the union case.
        /// </summary>
        /// <remarks>
        /// For instance, for union type union1 = CallExpression | LambdaExpression
        /// Node will return first or second case based on the runtime instantitation.
        /// </remarks>
        INode Node { get; }
    }

    /// <summary>
    /// Equality comparer for INode types
    /// </summary>
    /// <remarks>
    /// This is needed because the INode instance can be a union type, so to
    /// determine pointer equality one would need to call node.ResolveUnionType() first.
    /// </remarks>
    public sealed class NodeComparer<T> : IEqualityComparer<T> where T : INode
    {
        /// <inheritdoc/>
        public bool Equals(T x, T y)
        {
            return x.ResolveUnionType() == y.ResolveUnionType();
        }

        /// <inheritdoc/>
        public int GetHashCode(T obj)
        {
            return obj.GetHashCode();
        }

        /// <summary>
        /// Global instance of the node comparer.
        /// </summary>
        public static readonly NodeComparer<T> Instance = new NodeComparer<T>();
    }

    /// <summary>
    /// Singleton that stores node comparer.
    /// </summary>
    public static class NodeComparer
    {
        /// <summary>
        /// Global instance of the node comparer.
        /// </summary>
        public static readonly NodeComparer<INode> Instance = NodeComparer<INode>.Instance;
    }

    /// <summary>
    /// Abstract base class for builiding union types that combines two or more <see cref="Node"/> types.
    /// </summary>
    [DebuggerDisplay("{ToString(), nq}")]
    public abstract class DelegatingUnionNode : INode, IUnionNode
    {
        private readonly INode m_node;

        /// <nodoc />
        protected DelegatingUnionNode(INode node)
        {
            Contract.Requires(node != null);
            m_node = node;
        }

        /// <inheritdoc />
        public ISourceFile SourceFile
        {
            get { return m_node.SourceFile; }
            set { m_node.SourceFile = value; }
        }

        /// <summary>
        /// Returns current node instance.
        /// </summary>
        [NotNull]
        public INode Node => m_node;

        /// <inheritdoc />
        public int Pos
        {
            get { return m_node.Pos; }
            set { m_node.Pos = value; }
        }

        /// <inheritdoc />
        public int End
        {
            get { return m_node.End; }
            set { m_node.End = value; }
        }

        /// <nodoc />
        public byte LeadingTriviaLength
        {
            get { return m_node.LeadingTriviaLength; }
            set { m_node.LeadingTriviaLength = value; }
        }

        /// <inheritdoc />
        public void Initialize(SyntaxKind kind, int pos, int end)
        {
            throw new NotSupportedException("Union types should not be initialized manually.");
        }

        /// <inheritdoc />
        public SyntaxKind Kind
        {
            get { return m_node.Kind; }
            set { m_node.Kind = value; }
        }

        /// <inheritdoc />
        public NodeFlags Flags
        {
            get { return m_node.Flags; }
            set { m_node.Flags = value; }
        }

        /// <inheritdoc />
        public ISymbol ResolvedSymbol
        {
            get { return m_node.ResolvedSymbol; }
            set { m_node.ResolvedSymbol = value; }
        }

        /// <inheritdoc />
        public ParserContextFlags ParserContextFlags
        {
            get { return m_node.ParserContextFlags; }
            set { m_node.ParserContextFlags = value; }
        }

        /// <inheritdoc />
        public NodeArray<IDecorator> Decorators
        {
            get { return m_node.Decorators; }
            set { m_node.Decorators = value; }
        }

        /// <inheritdoc />
        public ModifiersArray Modifiers
        {
            get { return m_node.Modifiers; }
            set { m_node.Modifiers = value; }
        }

        /// <inheritdoc />
        public int Id => m_node.Id;

        /// <inheritdoc />
        public INode Parent
        {
            get { return m_node.Parent; }
            set { m_node.Parent = value; }
        }

        /// <inheritdoc />
        // TODO:SQ: Memory growth - The 4 fields below grow the memory of each node by 4 pointers
        public ISymbol Symbol
        {
            get { return m_node.Symbol; }
            set { m_node.Symbol = value; }
        }

        /// <inheritdoc />
        public ISymbolTable Locals
        {
            get { return m_node.Locals; }
            set { m_node.Locals = value; }
        }

        /// <inheritdoc />
        public ISymbol LocalSymbol
        {
            get { return m_node.LocalSymbol; }
            set { m_node.LocalSymbol = value; }
        }

        /// <inheritdoc />
        public virtual string ToDisplayString()
        {
            return m_node.ToDisplayString();
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return m_node.ToString();
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            if (this == obj)
            {
                return true;
            }

            var o = obj as INode;
            if (o == null)
            {
                return false;
            }

            return Equals(this.ResolveUnionType(), o.ResolveUnionType());
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            var b = this.ResolveUnionType();
            if (b == null)
            {
                return 0;
            }

            return b.GetHashCode();
        }

        /// <inheritdoc />
        public INode GetActualNode()
        {
            return Node.GetActualNode();
        }

        /// <inheritdoc />
        public TNode TryCast<TNode>() where TNode : class, INode
        {
            return Node.TryCast<TNode>() ?? this as TNode;
        }
    }

    /// <summary>
    /// Union type that combines <see cref="IIdentifier"/> or <see cref="IQualifiedName"/> nodes.
    /// </summary>
    public sealed class EntityName : DelegatingUnionNode, IIdentifier, IQualifiedName
    {
        private IIdentifier Identifier => Node.As<IIdentifier>();

        private IQualifiedName QualifiedName => Node.As<IQualifiedName>();

        /// <nodoc/>
        public EntityName(IIdentifier identifier)
            : base(identifier)
        {
            Contract.Requires(identifier != null);
        }

        /// <nodoc/>
        public EntityName(IQualifiedName qualifiedName)
            : base(qualifiedName)
        {
            Contract.Requires(qualifiedName != null);
        }

        /// <nodoc/>
        public static EntityName FromNode(INode node)
        {
            var identifier = node.As<IIdentifier>();
            if (identifier != null)
            {
                return new EntityName(identifier);
            }

            return new EntityName(node.Cast<IQualifiedName>());
        }

        /// <nodoc/>
        public string Text
        {
            get { return Identifier?.Text ?? QualifiedName.GetFormattedText(); }
        }

        /// <nodoc/>
        public IIdentifier AsIdentifier()
        {
            Contract.Ensures(Contract.Result<IIdentifier>() != null);
            return Identifier;
        }

        /// <nodoc/>
        public IQualifiedName AsQualifiedName()
        {
            Contract.Ensures(Contract.Result<IQualifiedName>() != null);
            return QualifiedName;
        }

        /// <inheritdoc/>
        string IHasText.Text
        {
            get { return Node.Cast<IHasText>().Text; }
            set { Node.Cast<IHasText>().Text = value; }
        }

        /// <inheritdoc/>
        SyntaxKind IIdentifier.OriginalKeywordKind
        {
            get { return Identifier.OriginalKeywordKind; }
            set { Identifier.OriginalKeywordKind = value; }
        }

        /// <inheritdoc/>
        EntityName IQualifiedName.Left
        {
            get { return QualifiedName.Left; }
            set { QualifiedName.Left = value; }
        }

        /// <inheritdoc/>
        public IIdentifier Right
        {
            get { return QualifiedName.Right; }
            set { QualifiedName.Right = value; }
        }
    }

    /// <summary>
    /// Union type that combines <see cref="IIdentifier"/>, <see cref="ILiteralExpression"/> and <see cref="IComputedPropertyName"/> nodes.
    /// </summary>
    public sealed class PropertyName : DelegatingUnionNode, IIdentifier, ILiteralExpression, IComputedPropertyName
    {
        private IIdentifier AsIdentifier => Node.As<IIdentifier>();

        private ILiteralExpression LiteralExpression => Node.As<ILiteralExpression>();

        private IComputedPropertyName ComputedPropertyName => Node.As<IComputedPropertyName>();

        /// <nodoc/>
        public PropertyName(IIdentifier identifier)
            : base(identifier)
        {
            Contract.Requires(identifier != null);
        }

        /// <nodoc/>
        public PropertyName(ILiteralExpression literalExpression)
            : base(literalExpression)
        {
            Contract.Requires(literalExpression != null);
        }

        /// <nodoc/>
        public PropertyName(IComputedPropertyName computedPropertyName)
            : base(computedPropertyName)
        {
            Contract.Requires(computedPropertyName != null);
        }

        /// <nodoc/>
        public static PropertyName Identifier(IIdentifier value) { return value != null ? new PropertyName(value) : null; }

        /// <nodoc/>
        public static PropertyName Identifier(ILiteralExpression value) { return value != null ? new PropertyName(value) : null; }

        /// <nodoc/>
        public static PropertyName Identifier(IComputedPropertyName value) { return value != null ? new PropertyName(value) : null; }

        /// <nodoc/>
        // Identifier
        string IHasText.Text
        {
            get { return Text; }
            set { Node.Cast<IHasText>().Text = value; }
        }

        /// <nodoc/>
        // LiteralExpression
        public bool IsUnterminated
        {
            get { return LiteralExpression.IsUnterminated; }
            set { LiteralExpression.IsUnterminated = value; }
        }

        /// <inheritdoc/>
        public bool HasExtendedUnicodeEscape
        {
            get { return LiteralExpression.HasExtendedUnicodeEscape; }
            set { LiteralExpression.HasExtendedUnicodeEscape = value; }
        }

        /// <inheritdoc/>
        public SyntaxKind OriginalKeywordKind
        {
            get { return AsIdentifier.OriginalKeywordKind; }
            set { AsIdentifier.OriginalKeywordKind = value; }
        }

        /// <nodoc/>
        public string Text
        {
            get => AsIdentifier?.Text ?? LiteralExpression?.Text;

            set
            {
                if (AsIdentifier != null)
                {
                    AsIdentifier.Text = value;
                }
                else
                {
                    LiteralExpression.Text = value;
                }
            }
        }

        /// <nodoc/>
        // ComputedPropertyName
        public IExpression Expression
        {
            get { return ComputedPropertyName.Expression; }
            set { ComputedPropertyName.Expression = value; }
        }
    }

    /// <summary>
    /// Union type that combines <see cref="TypeScript.Net.Types.PropertyName"/> and <see cref="IBindingPattern"/> nodes.
    /// </summary>
    public sealed class DeclarationName : DelegatingUnionNode, IIdentifier, ILiteralExpression, IComputedPropertyName, IBindingPattern
    {
        private PropertyName AsPropertyName => Node as PropertyName;

        private IBindingPattern AsBindingPattern => Node as IBindingPattern;

        private DeclarationName(PropertyName propertyName)
            : base(propertyName)
        {
            Contract.Requires(propertyName != null);
        }

        private DeclarationName(IBindingPattern bindingPattern)
            : base(bindingPattern)
        {
            Contract.Requires(bindingPattern != null);
        }

        /// <nodoc/>
        public static DeclarationName PropertyName(PropertyName value) { return value; }

        /// <nodoc/>
        public static DeclarationName PropertyName(IdentifierOrBindingPattern value)
        {
            var identifier = value.AsIdentifier();
            if (identifier != null)
            {
                return PropertyName(identifier);
            }

            return PropertyName(value.AsBindingPattern());
        }

        /// <nodoc/>
        public static DeclarationName PropertyName(IdentifierOrLiteralExpression value)
        {
            var identifier = value.AsIdentifier();
            if (identifier != null)
            {
                return PropertyName(identifier);
            }

            return PropertyName(value.AsLiteralExpression());
        }

        /// <nodoc/>
        public static DeclarationName PropertyName(IBindingPattern value) { return new DeclarationName(value); }

        /// <nodoc/>
        public static DeclarationName PropertyName(IIdentifier value) { return value != null ? new DeclarationName(new PropertyName(value)) : null; }

        /// <nodoc/>
        public static DeclarationName PropertyName(ILiteralExpression value) { return value != null ? new DeclarationName(new PropertyName(value)) : null; }

        /// <nodoc/>
        public static DeclarationName PropertyName(IComputedPropertyName value) { return value != null ? new DeclarationName(new PropertyName(value)) : null; }

        /// <nodoc/>
        public static implicit operator DeclarationName(PropertyName propertyName)
        {
            return propertyName != null ? new DeclarationName(propertyName) : null;
        }

        /// <nodoc/>
        public static implicit operator DeclarationName(Identifier name)
        {
            return name != null ? new DeclarationName(new PropertyName(name)) : null;
        }

        /// <nodoc/>
        public static explicit operator PropertyName(DeclarationName declarationName)
        {
            return declarationName.AsPropertyName;
        }

        /// <nodoc/>
        // Identifier
        string IHasText.Text
        {
            get { return Node.Cast<IHasText>().Text; }
            set { Node.Cast<IHasText>().Text = value; }
        }

        /// <nodoc/>
        // LiteralExpression
        public bool IsUnterminated
        {
            get { return AsPropertyName.IsUnterminated; }
            set { AsPropertyName.IsUnterminated = value; }
        }

        /// <inheritdoc/>
        public bool HasExtendedUnicodeEscape
        {
            get { return AsPropertyName.HasExtendedUnicodeEscape; }
            set { AsPropertyName.HasExtendedUnicodeEscape = value; }
        }

        /// <inheritdoc/>
        public SyntaxKind OriginalKeywordKind
        {
            get { return AsPropertyName.OriginalKeywordKind; }
            set { AsPropertyName.OriginalKeywordKind = value; }
        }

        /// <inheritdoc/>
        IExpression IComputedPropertyName.Expression
        {
            get { return AsPropertyName.Expression; }
            set { AsPropertyName.Expression = value; }
        }

        /// <nodoc/>
        // BindingPattern
        public NodeArray<IBindingElement> Elements
        {
            get { return AsBindingPattern.Elements; }
            set { AsBindingPattern.Elements = value; }
        }

        /// <summary>
        /// Returns name of the property.
        /// </summary>
        /// <returns>Null if name was not defined.</returns>
        public string Text => AsPropertyName?.Text ?? AsBindingPattern.GetName();
    }

    /// <summary>
    /// Union type that combines <see cref="EntityName"/> and <see cref="IExternalModuleReference"/> nodes.
    /// </summary>
    public sealed class EntityNameOrExternalModuleReference : DelegatingUnionNode, IExternalModuleReference
    {
        private readonly EntityName m_entityName;

        private IExternalModuleReference ExternalModuleReference => Node.As<IExternalModuleReference>();

        /// <nodoc/>
        public EntityNameOrExternalModuleReference(EntityName entityName)
            : base(entityName)
        {
            Contract.Requires(entityName != null);
            m_entityName = entityName;
        }

        /// <nodoc/>
        public EntityNameOrExternalModuleReference(IExternalModuleReference externalModuleReference)
            : base(externalModuleReference)
        {
            Contract.Requires(externalModuleReference != null);
        }

        /// <nodoc/>
        public static explicit operator EntityName(EntityNameOrExternalModuleReference value)
        {
            return value.m_entityName;
        }

        /// <nodoc/>
        public static implicit operator EntityNameOrExternalModuleReference(EntityName value)
        {
            return new EntityNameOrExternalModuleReference(value);
        }

        /// <inheritdoc/>
        public IExpression Expression
        {
            get { return ExternalModuleReference.Expression; }
            set { ExternalModuleReference.Expression = value; }
        }
    }

    /// <summary>
    /// Union type that combines <see cref="INamespaceImport"/> and <see cref="INamedImports"/> nodes.
    /// </summary>
    public sealed class NamespaceImportOrNamedImports : DelegatingUnionNode, INamespaceImport, INamedImports
    {
        private INamedImports NamedImports => Node.As<INamedImports>();

        /// <nodoc/>
        public NamespaceImportOrNamedImports(INamespaceImport namespaceImport)
            : base(namespaceImport)
        {
            Contract.Requires(namespaceImport != null);
        }

        /// <nodoc/>
        public NamespaceImportOrNamedImports(INamedImports namedImports)
            : base(namedImports)
        {
            Contract.Requires(namedImports != null);
        }

        /// <inheritdoc/>
        public NodeArray<IImportSpecifier> Elements
        {
            get { return NamedImports.Elements; }
            set { NamedImports.Elements = value; }
        }

        /// <inheritdoc/>
        public IIdentifier Name { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }

        /// <inheritdoc/>
        DeclarationName IDeclaration.Name => ThrowNotSupportedException();
    }

    /// <summary>
    /// Union type that combines <see cref="INamedImports"/> and <see cref="INamedExports"/> node types.
    /// </summary>
    public sealed class NamedImportsOrNamedExports : DelegatingUnionNode, INamedImports, INamedExports
    {
        private INamedImports NamedImports => Node.As<INamedImports>();

        private INamedExports NamedExports => Node.As<INamedExports>();

        /// <nodoc/>
        public NamedImportsOrNamedExports(INamedImports namedImports)
            : base(namedImports)
        {
            Contract.Requires(namedImports != null);
        }

        /// <nodoc/>
        public NamedImportsOrNamedExports(INamedExports namedExports)
            : base(namedExports)
        {
            Contract.Requires(namedExports != null);
        }

        /// <inheritdoc/>
        NodeArray<IImportSpecifier> INamedImports.Elements
        {
            get { return NamedImports.Elements; }
            set { NamedImports.Elements = value; }
        }

        /// <inheritdoc/>
        NodeArray<IExportSpecifier> INamedExports.Elements
        {
            get { return NamedExports.Elements; }
            set { NamedExports.Elements = value; }
        }
    }

    /// <summary>
    /// Union type that combines <see cref="IIdentifier"/> and <see cref="IBindingPattern"/> node types.
    /// </summary>
    public sealed class IdentifierOrBindingPattern : DelegatingUnionNode, IIdentifier, IBindingPattern
    {
        private IIdentifier Identifier => Node.As<IIdentifier>();

        private IBindingPattern BindingPattern => Node.As<IBindingPattern>();

        /// <nodoc/>
        public IdentifierOrBindingPattern(IIdentifier identifier)
            : base(identifier)
        {
            Contract.Requires(identifier != null);
        }

        /// <nodoc/>
        public IdentifierOrBindingPattern(IBindingPattern bindingPattern)
            : base(bindingPattern)
        {
            Contract.Requires(bindingPattern != null);
        }

        /// <inheritdoc/>
        string IHasText.Text
        {
            get { return Identifier.Text; }
            set { Identifier.Text = value; }
        }

        /// <inheritdoc/>
        public SyntaxKind OriginalKeywordKind
        {
            get { return Identifier.OriginalKeywordKind; }
            set { Identifier.OriginalKeywordKind = value; }
        }

        /// <inheritdoc/>
        NodeArray<IBindingElement> IBindingPattern.Elements
        {
            get { return BindingPattern.Elements; }
            set { BindingPattern.Elements = value; }
        }

        /// <nodoc/>
        public string GetText()
        {
            return Identifier?.Text ?? BindingPattern.GetFormattedText();
        }

        /// <nodoc/>
        public IIdentifier AsIdentifier() => Identifier;

        /// <nodoc/>
        public IBindingPattern AsBindingPattern() => BindingPattern;
    }

    /// <summary>
    /// Union type that combines <see cref="IVariableDeclarationList"/> and <see cref="IExpression"/> node types.
    /// </summary>
    public sealed class VariableDeclarationListOrExpression : DelegatingUnionNode, IVariableDeclarationList, IExpression
    {
        private IVariableDeclarationList VariableDeclarationList => Node.As<IVariableDeclarationList>();

        private IExpression Expression => Node.As<IExpression>();

        /// <nodoc/>
        public VariableDeclarationListOrExpression(IVariableDeclarationList variableDeclarationList)
            : base(variableDeclarationList)
        {
            Contract.Requires(variableDeclarationList != null);
        }

        /// <nodoc/>
        public VariableDeclarationListOrExpression(IExpression expression)
            : base(expression)
        {
            Contract.Requires(expression != null);
        }

        /// <inheritdoc/>
        INodeArray<IVariableDeclaration> IVariableDeclarationList.Declarations
        {
            get { return VariableDeclarationList.Declarations; }
            set { VariableDeclarationList.Declarations = VariableDeclarationNodeArray.Create(value); }
        }

        /// <nodoc/>
        public IVariableDeclarationList AsVariableDeclarationList()
        {
            return VariableDeclarationList;
        }

        /// <nodoc/>
        public IExpression AsExpression()
        {
            return Expression;
        }
    }

    /// <summary>
    /// Union type that combines <see cref="ICaseClause"/> and <see cref="IDefaultClause"/> node types.
    /// </summary>
    public sealed class CaseClauseOrDefaultClause : DelegatingUnionNode, ICaseClause, IDefaultClause
    {
        private ICaseClause CaseClause => Node.As<ICaseClause>();

        private IDefaultClause DefaultClause => Node.As<IDefaultClause>();

        /// <nodoc/>
        public CaseClauseOrDefaultClause(ICaseClause caseClause)
            : base(caseClause)
        {
            Contract.Requires(caseClause != null);
        }

        /// <nodoc/>
        public CaseClauseOrDefaultClause(IDefaultClause defaultClause)
            : base(defaultClause)
        {
            Contract.Requires(defaultClause != null);
        }

        /// <inheritdoc/>
        IExpression ICaseClause.Expression
        {
            get { return CaseClause.Expression; }
            set { CaseClause.Expression = value; }
        }

        /// <inheritdoc/>
        NodeArray<IStatement> IStatementsContainer.Statements
        {
            get => CaseClause?.Statements ?? DefaultClause.Statements;

            set
            {
                if (CaseClause != null)
                {
                    CaseClause.Statements = value;
                }
                else
                {
                    DefaultClause.Statements = value;
                }
            }
        }

        /// <nodoc/>
        public NodeArray<IStatement> Statements
        {
            get => CaseClause != null ? CaseClause.Statements : DefaultClause.Statements;

            set
            {
                if (CaseClause != null)
                {
                    CaseClause.Statements = value;
                }
                else
                {
                    DefaultClause.Statements = value;
                }
            }
        }

        /// <nodoc/>
        public ICaseClause AsCaseClause()
        {
            return CaseClause;
        }

        /// <nodoc/>
        public IDefaultClause AsDefaultClause()
        {
            return DefaultClause;
        }
    }

    /// <summary>
    /// Union type that combines <see cref="IIdentifier"/> and <see cref="IThisTypeNode"/> node types.
    /// </summary>
    public sealed class IdentifierOrThisTypeUnionNode : DelegatingUnionNode, IIdentifier, IThisTypeNode
    {
        private IIdentifier Identifier => Node.As<IIdentifier>();

        private IThisTypeNode ThisTypeNode => Node.As<IThisTypeNode>();

        /// <nodoc/>
        public IdentifierOrThisTypeUnionNode(IIdentifier identifier)
            : base(identifier)
        {
            Contract.Requires(identifier != null);
        }

        /// <nodoc/>
        public IdentifierOrThisTypeUnionNode(IThisTypeNode thisTypeNode)
            : base(thisTypeNode)
        {
            Contract.Requires(thisTypeNode != null);
        }

        /// <inheritdoc/>
        string IHasText.Text
        {
            get { return Identifier.Text; }
            set { Identifier.Text = value; }
        }

        /// <inheritdoc/>
        public SyntaxKind OriginalKeywordKind
        {
            get { return Identifier.OriginalKeywordKind; }
            set { Identifier.OriginalKeywordKind = value; }
        }

        /// <inheritdoc/>
        public bool Equals(ITypeNode other)
        {
            return ThisTypeNode.Equals(other);
        }
    }

    /// <nodoc/>
    public sealed class ConciseBody : DelegatingUnionNode, IBlock, IExpression
    {
        private IBlock FunctionBody => Node.As<IBlock>();

        private IExpression AsExpression => Node.As<IExpression>();

        /// <nodoc/>
        public ConciseBody(IBlock functionBody)
            : base(functionBody)
        {
        }

        /// <nodoc/>
        public ConciseBody(IExpression expression)
            : base(expression)
        {
        }

        /// <inheritdoc/>
        NodeArray<IStatement> IStatementsContainer.Statements
        {
            get { return FunctionBody.Statements; }
            set { FunctionBody.Statements = value; }
        }

        /// <nodoc/>
        public static ConciseBody Block(IBlock value) { return value != null ? new ConciseBody(value) : null; }

        /// <nodoc/>
        public static ConciseBody Block(IExpression value) { return value != null ? new ConciseBody(value) : null; }

        /// <nodoc/>
        public IBlock Block()
        {
            return FunctionBody;
        }

        /// <nodoc/>
        public IExpression Expression()
        {
            return AsExpression;
        }
    }

    /// <summary>
    /// Union type that combines <see cref="IUnaryExpression"/> and <see cref="IBinaryExpression"/> node types.
    /// </summary>
    public sealed class UnaryExpressionOrBinaryExpression : DelegatingUnionNode, IUnaryExpression, IBinaryExpression
    {
        private IIdentifier Identifier => Node.As<IIdentifier>();

        private IBinaryExpression BinaryExpression => Node.As<IBinaryExpression>();

        private IUnaryExpression UnaryExpression => Node.As<IUnaryExpression>();

        /// <nodoc/>
        public UnaryExpressionOrBinaryExpression(IUnaryExpression unaryExpression)
            : base(unaryExpression)
        {
            Contract.Requires(unaryExpression != null);
        }

        /// <nodoc/>
        public UnaryExpressionOrBinaryExpression(IBinaryExpression binaryExpression)
            : base(binaryExpression)
        {
            Contract.Requires(binaryExpression != null);
        }

        /// <nodoc/>
        public IIdentifier AsIdentifier()
        {
            return Identifier;
        }

        /// <inheritdoc/>
        public DeclarationName Name => BinaryExpression.Name;

        /// <inheritdoc/>
        IExpression IBinaryExpression.Left
        {
            get { return BinaryExpression.Left; }
            set { BinaryExpression.Left = value; }
        }

        /// <inheritdoc/>
        INode IBinaryExpression.OperatorToken
        {
            get { return BinaryExpression.OperatorToken; }
            set { BinaryExpression.OperatorToken = value; }
        }

        /// <inheritdoc/>
        IExpression IBinaryExpression.Right
        {
            get { return BinaryExpression.Right; }
            set { BinaryExpression.Right = value; }
        }
    }

    /// <summary>
    /// Union type that combines <see cref="ILiteralExpression"/> and <see cref="ITemplateExpression"/> node types.
    /// </summary>
    public sealed class LiteralExpressionOrTemplateExpression : DelegatingUnionNode, ILiteralExpression, ITemplateExpression
    {
        private ILiteralExpression LiteralExpression => Node.As<ILiteralExpression>();

        private ITemplateExpression TemplateExpression => Node.As<ITemplateExpression>();

        /// <nodoc/>
        public LiteralExpressionOrTemplateExpression(ILiteralExpression literalExpression)
            : base(literalExpression)
        {
            Contract.Requires(literalExpression != null);
        }

        /// <nodoc/>
        public LiteralExpressionOrTemplateExpression(ITemplateExpression templateExpression)
            : base(templateExpression)
        {
            Contract.Requires(templateExpression != null);
        }

        /// <inheritdoc/>
        public string Text
        {
            get { return LiteralExpression.Text; }
            set { LiteralExpression.Text = value; }
        }

        /// <inheritdoc/>
        public bool IsUnterminated
        {
            get { return LiteralExpression.IsUnterminated; }
            set { LiteralExpression.IsUnterminated = value; }
        }

        /// <inheritdoc/>
        public bool HasExtendedUnicodeEscape
        {
            get { return LiteralExpression.HasExtendedUnicodeEscape; }
            set { LiteralExpression.HasExtendedUnicodeEscape = value; }
        }

        /// <inheritdoc/>
        ITemplateLiteralFragment ITemplateExpression.Head
        {
            get { return TemplateExpression.Head; }
            set { TemplateExpression.Head = value; }
        }

        /// <inheritdoc/>
        INodeArray<ITemplateSpan> ITemplateExpression.TemplateSpans
        {
            get { return TemplateExpression.TemplateSpans; }
            set { TemplateExpression.TemplateSpans = value; }
        }
    }

    /// <summary>
    /// Union type that combines <see cref="INewExpression"/>, <see cref="ITaggedTemplateExpression"/> and<see cref="IDecorator"/> node types.
    /// </summary>
    public class CallLikeExpression : DelegatingUnionNode, ICallExpression,
        INewExpression, ITaggedTemplateExpression, IDecorator
    {
        private INewExpression NewExpression => Node.As<INewExpression>();

        private ITaggedTemplateExpression TaggedTemplateExpression => Node.As<ITaggedTemplateExpression>();

        /// <nodoc/>
        public CallLikeExpression(ICallExpression callExpression)
            : base(callExpression)
        {
            Contract.Requires(callExpression != null);
        }

        /// <nodoc/>
        public CallLikeExpression(INewExpression newExpression)
            : base(newExpression)
        {
            Contract.Requires(newExpression != null);
        }

        /// <nodoc/>
        public CallLikeExpression(ITaggedTemplateExpression taggedTemplateExpression)
            : base(taggedTemplateExpression)
        {
            Contract.Requires(taggedTemplateExpression != null);
        }

        /// <nodoc/>
        public CallLikeExpression(IDecorator decorator)
            : base(decorator)
        {
            Contract.Requires(decorator != null);
        }

        /// <inheritdoc/>
        public ILeftHandSideExpression Expression
        {
            get { return NewExpression.Expression; }
            set { NewExpression.Expression = value; }
        }

        /// <inheritdoc/>
        public NodeArray<ITypeNode> TypeArguments
        {
            get { return NewExpression.TypeArguments; }
            set { NewExpression.TypeArguments = value; }
        }

        /// <inheritdoc/>
        public NodeArray<IExpression> Arguments
        {
            get { return NewExpression.Arguments; }
            set { NewExpression.Arguments = value; }
        }

        /// <inheritdoc/>
        public ILeftHandSideExpression Tag
        {
            get { return TaggedTemplateExpression.Tag; }
            set { TaggedTemplateExpression.Tag = value; }
        }

        /// <inheritdoc/>
        public LiteralExpressionOrTemplateExpression Template
        {
            get { return TaggedTemplateExpression.Template; }
            set { TaggedTemplateExpression.Template = value; }
        }

        /// <inheritdoc/>
        public IPrimaryExpression TemplateExpression
        {
            get { return TaggedTemplateExpression.TemplateExpression; }
            set { TaggedTemplateExpression.TemplateExpression = value; }
        }
    }

    /// <summary>
    /// Union type that combines <see cref="IIdentifier"/> and <see cref="ILiteralExpression"/> node types.
    /// </summary>
    public sealed class IdentifierOrLiteralExpression : DelegatingUnionNode, IIdentifier, ILiteralExpression
    {
        private IIdentifier Identifier => Node.As<IIdentifier>();

        private ILiteralExpression LiteralExpression => Node.As<ILiteralExpression>();

        /// <nodoc/>
        public IdentifierOrLiteralExpression(IIdentifier identifier)
            : base(identifier)
        {
            Contract.Requires(identifier != null);
        }

        /// <nodoc/>
        public IdentifierOrLiteralExpression(ILiteralExpression literalExpression)
            : base(literalExpression)
        {
            Contract.Requires(literalExpression != null);
        }

        /// <nodoc/>
        public string Text
        {
            get { return Identifier?.Text ?? LiteralExpression.Text; }
        }

        /// <inheritdoc/>
        string IHasText.Text
        {
            get { return Node.Cast<IHasText>().Text; }
            set { Node.Cast<IHasText>().Text = value; }
        }

        /// <inheritdoc/>
        public bool IsUnterminated
        {
            get { return LiteralExpression.IsUnterminated; }
            set { LiteralExpression.IsUnterminated = value; }
        }

        /// <inheritdoc/>
        public bool HasExtendedUnicodeEscape
        {
            get { return LiteralExpression.HasExtendedUnicodeEscape; }
            set { LiteralExpression.HasExtendedUnicodeEscape = value; }
        }

        /// <inheritdoc/>
        SyntaxKind IIdentifier.OriginalKeywordKind
        {
            get { return Identifier.OriginalKeywordKind; }
            set { Identifier.OriginalKeywordKind = value; }
        }

        /// <nodoc/>
        public IIdentifier AsIdentifier() => Identifier;

        /// <nodoc/>
        public ILiteralExpression AsLiteralExpression() => LiteralExpression;
    }

    /// <summary>
    /// Union type that combines <see cref="IModuleBlock"/> and <see cref="IModuleDeclaration"/> node types.
    /// </summary>
    public sealed class ModuleBody : DelegatingUnionNode, IModuleBlock, IModuleDeclaration
    {
        private IModuleBlock ModuleBlock => Node.As<IModuleBlock>();

        private IModuleDeclaration ModuleDeclaration => Node.As<IModuleDeclaration>();

        /// <nodoc/>
        public ModuleBody(IModuleBlock moduleBlock)
            : base(moduleBlock)
        {
            Contract.Requires(moduleBlock != null);
        }

        /// <nodoc/>
        public ModuleBody(IModuleDeclaration moduleDeclaration)
            : base(moduleDeclaration)
        {
            Contract.Requires(moduleDeclaration != null);
        }

        /// <nodoc/>
        public NodeArray<IStatement> Statements => ModuleBlock?.Statements ?? ModuleDeclaration.Body.Statements;

        /// <inheritdoc/>
        NodeArray<IStatement> IStatementsContainer.Statements
        {
            get { return ModuleBlock.Statements; }
            set { ModuleBlock.Statements = value; }
        }

        /// <inheritdoc/>
        DeclarationName IDeclaration.Name => ThrowNotSupportedException();

        /// <inheritdoc/>
        public ModuleBody Body
        {
            get { return ModuleDeclaration.Body; }
            set { ModuleDeclaration.Body = value; }
        }

        /// <inheritdoc/>
        IIdentifier IDeclarationStatement.Name => ModuleDeclaration.Name;

        /// <inheritdoc/>
        IdentifierOrLiteralExpression IModuleDeclaration.Name { get { return ModuleDeclaration.Name; } set { ModuleDeclaration.Name = value; } }

        /// <nodoc/>
        public IModuleBlock AsModuleBlock()
        {
            return ModuleBlock;
        }

        /// <nodoc/>
        public IModuleDeclaration AsModuleDeclaration()
        {
            return ModuleDeclaration;
        }
    }

    /// <summary>
    /// Union type that combines <see cref="ITypeAssertion"/> and <see cref="IAsExpression"/> node types.
    /// </summary>
    public sealed class TypeAssertionOrAsExpression : DelegatingUnionNode, ITypeAssertion, IAsExpression
    {
        private ITypeAssertion TypeAssertion => Node.As<ITypeAssertion>();

        private IAsExpression AsExpression => Node.As<IAsExpression>();

        /// <nodoc/>
        public TypeAssertionOrAsExpression(ITypeAssertion typeAssertion)
            : base(typeAssertion)
        {
            Contract.Requires(typeAssertion != null);
        }

        /// <nodoc/>
        public TypeAssertionOrAsExpression(IAsExpression asExpression)
            : base(asExpression)
        {
            Contract.Requires(asExpression != null);
        }

        /// <inheritdoc/>
        IExpression IAsExpression.Expression
        {
            get { return AsExpression.Expression; }
            set { AsExpression.Expression = value; }
        }

        /// <inheritdoc/>
        ITypeNode IAsExpression.Type
        {
            get { return AsExpression.Type; }
            set { AsExpression.Type = value; }
        }

        /// <inheritdoc/>
        ITypeNode ITypeAssertion.Type
        {
            get { return TypeAssertion.Type; }
            set { TypeAssertion.Type = value; }
        }

        /// <inheritdoc/>
        IUnaryExpression ITypeAssertion.Expression
        {
            get { return TypeAssertion.Expression; }
            set { TypeAssertion.Expression = value; }
        }
    }

    /// <summary>
    /// Union type that combines <see cref="IImportDeclaration"/> and <see cref="IImportEqualsDeclaration"/> node types.
    /// </summary>
    public sealed class AnyImportSyntax : DelegatingUnionNode, IImportDeclaration, IImportEqualsDeclaration
    {
        private IImportDeclaration ImportDeclaration => Node.As<IImportDeclaration>();

        private IImportEqualsDeclaration ImportEqualsDeclaration => Node.As<IImportEqualsDeclaration>();

        /// <nodoc/>
        public AnyImportSyntax(IImportDeclaration importDeclaration)
            : base(importDeclaration)
        {
            Contract.Requires(importDeclaration != null);
        }

        /// <nodoc/>
        public AnyImportSyntax(IImportEqualsDeclaration importEqualsDeclaration)
            : base(importEqualsDeclaration)
        {
            Contract.Requires(importEqualsDeclaration != null);
        }

        /// <inheritdoc/>
        IImportClause IImportDeclaration.ImportClause
        {
            get { return ImportDeclaration.ImportClause; }
            set { ImportDeclaration.ImportClause = value; }
        }

        /// <inheritdoc/>
        IExpression IImportDeclaration.ModuleSpecifier
        {
            get { return ImportDeclaration.ModuleSpecifier; }
            set { ImportDeclaration.ModuleSpecifier = value; }
        }

        /// <inheritdoc/>
        IIdentifier IImportEqualsDeclaration.Name { get { return ImportEqualsDeclaration.Name; } set { ImportEqualsDeclaration.Name = value; } }

        /// <inheritdoc/>
        DeclarationName IDeclaration.Name => ThrowNotSupportedException();

        /// <inheritdoc/>
        IIdentifier IDeclarationStatement.Name => ImportEqualsDeclaration.Name;

        /// <inheritdoc/>
        EntityNameOrExternalModuleReference IImportEqualsDeclaration.ModuleReference
        {
            get { return ImportEqualsDeclaration.ModuleReference; }
            set { ImportEqualsDeclaration.ModuleReference = value; }
        }

        /// <inheritdoc/>
        bool IImportDeclaration.IsLikeImport
        {
            get { return ImportDeclaration.IsLikeImport; }
            set { ImportDeclaration.IsLikeImport = value; }
        }
    }

    /// <nodoc/>
    public sealed class ExportAssignmentOrImportEqualsDeclaration : DelegatingUnionNode, IImportEqualsDeclaration, IExportAssignment
    {
        private IExportAssignment ExportDeclaration => Node.As<IExportAssignment>();

        private IImportEqualsDeclaration ImportEqualsDeclaration => Node.As<IImportEqualsDeclaration>();

        /// <nodoc/>
        public ExportAssignmentOrImportEqualsDeclaration(IExportAssignment exportDeclaration)
            : base(exportDeclaration)
        {
            Contract.Requires(exportDeclaration != null);
        }

        /// <nodoc/>
        public ExportAssignmentOrImportEqualsDeclaration(IImportEqualsDeclaration importEqualsDeclaration)
            : base(importEqualsDeclaration)
        {
            Contract.Requires(importEqualsDeclaration != null);
        }

        /// <inheritdoc/>
        IIdentifier IImportEqualsDeclaration.Name { get { return ImportEqualsDeclaration.Name; } set { ImportEqualsDeclaration.Name = value; } }

        /// <inheritdoc/>
        EntityNameOrExternalModuleReference IImportEqualsDeclaration.ModuleReference
        {
            get { return ImportEqualsDeclaration.ModuleReference; }
            set { ImportEqualsDeclaration.ModuleReference = value; }
        }

        /// <inheritdoc/>
        IIdentifier IDeclarationStatement.Name
        {
            get { return ExportDeclaration.Name; }
        }

        /// <inheritdoc/>
        DeclarationName IDeclaration.Name
        {
            get { return ((IDeclaration)ExportDeclaration).Name; }
        }

        /// <inheritdoc/>
        Optional<bool> IExportAssignment.IsExportEquals
        {
            get { return ExportDeclaration.IsExportEquals; }
            set { ExportDeclaration.IsExportEquals = value; }
        }

        /// <inheritdoc/>
        IExpression IExportAssignment.Expression
        {
            get { return ExportDeclaration.Expression; }
            set { ExportDeclaration.Expression = value; }
        }
    }

    /// <summary>
    /// Union type that combines <see cref="IBindingPattern"/>, <see cref="IObjectLiteralExpression"/> and <see cref="IArrayLiteralExpression"/> node types.
    /// </summary>
    public sealed class DestructuringPattern : DelegatingUnionNode, IBindingPattern, IObjectLiteralExpression, IArrayLiteralExpression
    {
        private IBindingPattern BindingPattern => Node.As<IBindingPattern>();

        private IObjectLiteralExpression ObjectLiteralExpression => Node.As<IObjectLiteralExpression>();

        private IArrayLiteralExpression ArrayLiteralExpression => Node.As<IArrayLiteralExpression>();

        /// <nodoc/>
        public DestructuringPattern(IBindingPattern bindingPattern)
            : base(bindingPattern)
        {
            Contract.Requires(bindingPattern != null);
        }

        /// <nodoc/>
        public DestructuringPattern(IObjectLiteralExpression objectLiteralExpression)
            : base(objectLiteralExpression)
        {
            Contract.Requires(objectLiteralExpression != null);
        }

        /// <nodoc/>
        public DestructuringPattern(IArrayLiteralExpression arrayLiteralExpression)
            : base(arrayLiteralExpression)
        {
            Contract.Requires(arrayLiteralExpression != null);
        }

        /// <inheritdoc/>
        NodeArray<IBindingElement> IBindingPattern.Elements
        {
            get { return BindingPattern.Elements; }
            set { BindingPattern.Elements = value; }
        }

        /// <inheritdoc/>
        public DeclarationName Name => ObjectLiteralExpression.Name;

        /// <inheritdoc/>
        NodeArray<IObjectLiteralElement> IObjectLiteralExpression.Properties
        {
            get { return ObjectLiteralExpression.Properties; }
            set { ObjectLiteralExpression.Properties = value; }
        }

        /// <inheritdoc/>
        NodeArray<IExpression> IArrayLiteralExpression.Elements
        {
            get { return ArrayLiteralExpression.Elements; }
            set { ArrayLiteralExpression.Elements = value; }
        }
    }

    /// <summary>
    /// Union type that combines <see cref="IThisTypePredicate"/> and <see cref="IIdentifierTypePredicate"/> node types.
    /// </summary>
    public sealed class ThisTypePredicateOrIdentifierTypePredicate : IThisTypePredicate, IIdentifierTypePredicate
    {
        private readonly IThisTypePredicate m_thisTypePredicate;
        private readonly IIdentifierTypePredicate m_identifierTypePredicate;

        /// <nodoc/>
        public ThisTypePredicateOrIdentifierTypePredicate(IThisTypePredicate thisTypePredicate)
        {
            Contract.Requires(thisTypePredicate != null);
            m_thisTypePredicate = thisTypePredicate;
        }

        /// <nodoc/>
        public ThisTypePredicateOrIdentifierTypePredicate(IIdentifierTypePredicate identifierTypePredicate)
        {
            Contract.Requires(identifierTypePredicate != null);
            m_identifierTypePredicate = identifierTypePredicate;
        }

        /// <inheritdoc/>
        public TypePredicateKind Kind
        {
            get { return m_thisTypePredicate.Kind; }
            set { m_thisTypePredicate.Kind = value; }
        }

        /// <inheritdoc/>
        IType ITypePredicate.Type
        {
            get { return m_thisTypePredicate.Type; }
            set { m_thisTypePredicate.Type = value; }
        }

        /// <inheritdoc/>
        public string ParameterName
        {
            get { return m_identifierTypePredicate.ParameterName; }
            set { m_identifierTypePredicate.ParameterName = value; }
        }

        /// <inheritdoc/>
        public int? ParameterIndex
        {
            get { return m_identifierTypePredicate.ParameterIndex; }
            set { m_identifierTypePredicate.ParameterIndex = value; }
        }
    }

    /// <summary>
    /// Union type that combines <see cref="IImportSpecifier"/> and <see cref="IExportSpecifier"/> node types.
    /// </summary>
    public sealed class ImportSpecifierOrExportSpecifier : DelegatingUnionNode, IImportSpecifier, IExportSpecifier
    {
        /// <nodoc/>
        public ImportSpecifierOrExportSpecifier(IImportSpecifier importSpecifier)
            : base(importSpecifier)
        {
            Contract.Requires(importSpecifier != null);
        }

        /// <nodoc/>
        public ImportSpecifierOrExportSpecifier(IExportSpecifier exportSpecifier)
            : base(exportSpecifier)
        {
            Contract.Requires(exportSpecifier != null);
        }

        /// <nodoc/>
        public IImportOrExportSpecifier Specifier => (IImportOrExportSpecifier)Node;

        /// <inheritdoc/>
        IIdentifier IImportOrExportSpecifier.PropertyName
        {
            get { return Specifier.PropertyName; }
            set { Specifier.PropertyName = value; }
        }

        /// <inheritdoc/>
        IIdentifier IImportOrExportSpecifier.Name
        {
            get { return Specifier.Name; }
            set { Specifier.Name = value; }
        }

        /// <inheritdoc/>
        DeclarationName IDeclaration.Name => ThrowNotSupportedException();
    }

    /// <summary>
    /// Union type that combines <see cref="ITypeReferenceNode"/> and <see cref="ITypePredicateNode"/> node types.
    /// </summary>
    public class TypeReferenceUnionNodeOrTypePredicateUnionNode : DelegatingUnionNode, ITypeReferenceNode, ITypePredicateNode
    {
        private ITypeReferenceNode TypeReferenceNode => Node.As<ITypeReferenceNode>();

        private ITypePredicateNode TypePredicateNode => Node.As<ITypePredicateNode>();

        /// <nodoc/>
        public TypeReferenceUnionNodeOrTypePredicateUnionNode(ITypeReferenceNode typeReferenceNode)
            : base(typeReferenceNode)
        {
            Contract.Requires(typeReferenceNode != null);
        }

        /// <nodoc/>
        public TypeReferenceUnionNodeOrTypePredicateUnionNode(ITypePredicateNode typePredicateNode)
            : base(typePredicateNode)
        {
            Contract.Requires(typePredicateNode != null);
        }

        /// <inheritdoc/>
        public bool Equals(ITypeNode other)
        {
            return TypeReferenceNode.Equals(other);
        }

        /// <inheritdoc/>
        EntityName ITypeReferenceNode.TypeName
        {
            get { return TypeReferenceNode.TypeName; }
            set { TypeReferenceNode.TypeName = value; }
        }

        /// <inheritdoc/>
        NodeArray<ITypeNode> ITypeReferenceNode.TypeArguments
        {
            get { return TypeReferenceNode.TypeArguments; }
            set { TypeReferenceNode.TypeArguments = value; }
        }

        /// <inheritdoc/>
        IdentifierOrThisTypeUnionNode ITypePredicateNode.ParameterName
        {
            get { return TypePredicateNode.ParameterName; }
            set { TypePredicateNode.ParameterName = value; }
        }

        /// <inheritdoc/>
        ITypeNode ITypePredicateNode.Type
        {
            get { return TypePredicateNode.Type; }
            set { TypePredicateNode.Type = value; }
        }
    }

    /// <summary>
    /// Union type that combines <see cref="IPropertySignature"/> and <see cref="IMethodSignature"/> node types.
    /// </summary>
    public class PropertySignatureOrMethodSignature : DelegatingUnionNode, IPropertySignature, IMethodSignature, IVariableLikeDeclaration
    {
        private IPropertySignature PropertySignature => Node.As<IPropertySignature>();

        private IMethodSignature MethodSignature => Node.As<IMethodSignature>();

        /// <nodoc/>
        public PropertySignatureOrMethodSignature(IPropertySignature propertySignature)
            : base(propertySignature)
        {
            Contract.Requires(propertySignature != null);
        }

        /// <nodoc/>
        public PropertySignatureOrMethodSignature(IMethodSignature methodSignature)
            : base(methodSignature)
        {
            Contract.Requires(methodSignature != null);
        }

        /// <inheritdoc/>
        DeclarationName IDeclaration.Name => ThrowNotSupportedException();

        /// <inheritdoc/>
        PropertyName IMethodSignature.Name => MethodSignature.Name;

        /// <inheritdoc/>
        public NodeArray<ITypeParameterDeclaration> TypeParameters
        {
            get { return MethodSignature.TypeParameters; }
            set { MethodSignature.TypeParameters = value; }
        }

        /// <inheritdoc/>
        public NodeArray<IParameterDeclaration> Parameters
        {
            get { return MethodSignature.Parameters; }
            set { MethodSignature.Parameters = value; }
        }

        /// <inheritdoc/>
        PropertyName ISignatureDeclaration.Name => MethodSignature.Name;

        /// <inheritdoc/>
        PropertyName IPropertySignature.Name
        {
            get { return PropertySignature.Name; }
            set { PropertySignature.Name = value; }
        }

        // TODO: What is the difference between PropertyName and Name?

        /// <inheritdoc/>
        PropertyName IVariableLikeDeclaration.PropertyName { get; }

        /// <inheritdoc/>
        [SuppressMessage("Microsoft.Naming", "CA1721:PropertyNamesShouldNotMatchGetMethods", Justification = "Type nomenclature is necessary within a compiler.")]
        public ITypeNode Type
        {
            get { return PropertySignature.Type; }
            set { PropertySignature.Type = value; }
        }

        /// <inheritdoc/>
        ITypeNode IPropertySignature.Type
        {
            get { return PropertySignature.Type; }
            set { PropertySignature.Type = value; }
        }

        /// <inheritdoc/>
        ITypeNode IVariableLikeDeclaration.Type
        {
            get { return PropertySignature.Type; }
        }

        /// <inheritdoc/>
        IExpression IPropertySignature.Initializer
        {
            get { return PropertySignature.Initializer; }
            set { PropertySignature.Initializer = value; }
        }

        /// <inheritdoc/>
        IExpression IVariableLikeDeclaration.Initializer
        {
            get { return PropertySignature.Initializer; }
        }

        /// <inheritdoc/>
        Optional<INode> ITypeElement.QuestionToken
        {
            get { return PropertySignature.QuestionToken; }
            set { PropertySignature.QuestionToken = value; }
        }

        /// <inheritdoc/>
        Optional<INode> IVariableLikeDeclaration.QuestionToken
        {
            get { return PropertySignature.QuestionToken; }
        }

        /// <inheritdoc/>
        Optional<INode> IFunctionLikeDeclaration.AsteriskToken { get; }

        /// <inheritdoc/>
        Optional<INode> IFunctionLikeDeclaration.QuestionToken { get; }

        /// <inheritdoc/>
        ConciseBody IFunctionLikeDeclaration.Body { get; }

        /// <inheritdoc/>
        Optional<INode> IVariableLikeDeclaration.DotDotDotToken { get; }
    }
}
