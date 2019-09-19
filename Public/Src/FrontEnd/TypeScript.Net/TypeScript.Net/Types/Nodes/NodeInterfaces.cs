// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using TypeScript.Net.Core;
using TypeScript.Net.Diagnostics;
using TypeScript.Net.Extensions;
using TypeScript.Net.Incrementality;
using TypeScript.Net.Utilities;

#pragma warning disable SA1649 // File name must match first type name

namespace TypeScript.Net.Types
{
    /// <summary>
    /// Interface that represents node in the abstract syntax tree.
    /// </summary>
    [NodeInfo(SyntaxKinds = null, NodeType = NodeType.Abstract)]
    public interface INode : ITextRange
    {
        /// <nodoc />
        byte LeadingTriviaLength { get; set; }

        /// <summary>
        /// Source file that a current node belongs to.
        /// </summary>
        [CanBeNull]
        ISourceFile SourceFile { get; set; }

        /// <nodoc/>
        SyntaxKind Kind { get; set; }

        /// <nodoc/>
        NodeFlags Flags { get; set; }

        /// <summary>
        /// Specific context the parser was in when this node was created.  Normally undefined.
        /// Only set when the parser was in some interesting context (like async/yield).
        /// </summary>
        /* @internal */
        ParserContextFlags ParserContextFlags { get; set; }

        /// <summary>
        /// Array of decorators (in document order)
        /// </summary>
        NodeArray<IDecorator> Decorators { get; set; }

        /// <summary>
        /// Array of modifiers
        /// </summary>
        ModifiersArray Modifiers { get; set; }

        /// <summary>
        /// Unique id (used to look up NodeLinks)
        /// </summary>
        int Id { get; }

        /// <nodoc/>
        INode Parent { get; set; } // Parent node (initialized by binding)

        // Required for binder
        // TODO:SQ: Memory growth - The 4 fields below grow the memory of each node by 4 pointers
        //          Wrap them in a class (e.g. NodeBindingState)

        /// <nodoc/>
        ISymbol Symbol { get; set; } // Symbol declared by node (initialized by binding)

        /// <summary>
        /// Cached name resolution result
        /// </summary>
        /// <remarks>
        /// The checker has two options where to keep the result of a name resolution: on the node or in the external table (in <see cref="INodeLinks"/>).
        /// When the checker is used in a batch mode (regular BuildXL invocation) it is more efficient to keep this information in the node,
        /// because almost every node will have a symbol.
        /// This approach is more memory efficient, but it is not suitable for IDE scenarios when the checker is used in the language service.
        /// In the IDE mode, the checker should be able to throw away the symbol and reconstruct this once again. This is possible only
        /// if the node itself doesn't keep this information.
        /// So in the IDE mode, the checker will store the symbol in <see cref="INodeLinks"/> and in the batch mode it will keep it on the node.
        /// </remarks>
        [CanBeNull]
        ISymbol ResolvedSymbol { get; set; }

        /// <nodoc/>
        ISymbolTable Locals { get; set; } // Locals associated with node (initialized by binding)

        /// <nodoc/>
        ISymbol LocalSymbol { get; set; }

        /// <nodoc/>
        void Initialize(SyntaxKind kind, int pos, int end);

        /// <nodoc/>
        string ToDisplayString();

        /// <summary>
        /// Returns an actual type of a current node.
        /// </summary>
        INode GetActualNode();

        /// <summary>
        /// Converts a current node to a given type.
        /// </summary>
        /// <remarks>
        /// There is an intional distinction between node.As&lt;T&lt;() and node.TryCast&lt;T&gt;().
        /// The first works with nulls but another one - is an instance method and will throw.
        /// </remarks>
        TNode TryCast<TNode>() where TNode : class, INode;

        // Local symbol declared by node (initialized by binding only for exported nodes)
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = null, NodeType = NodeType.Leaf)]
    public interface ITokenNode : INode
    {
    }

    /// <nodoc/>
    [NodeInfo(
        SyntaxKinds = new[]
    {
        SyntaxKind.AbstractKeyword, SyntaxKind.AsyncKeyword, SyntaxKind.ConstKeyword,
        SyntaxKind.DeclareKeyword, SyntaxKind.DefaultKeyword, SyntaxKind.ExportKeyword,
        SyntaxKind.PublicKeyword, SyntaxKind.PrivateKeyword, SyntaxKind.ProtectedKeyword, SyntaxKind.StaticKeyword,
    }, NodeType = NodeType.Leaf)]
    public interface IModifier : INode
    {
    }

    /// <summary>
    /// Marker interface for every nodes that has Text property.
    /// </summary>
    [NodeInfo(SyntaxKinds = null, NodeType = NodeType.Marker)]
    public interface IHasText : INode
    {
        /// <summary>
        /// Text of identifier (with escapes converted to characters)
        /// </summary>
        string Text { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.Identifier }, NodeType = NodeType.Leaf)]
    public interface IIdentifier : IPrimaryExpression, IHasText
    {
        /// <summary>
        /// Original syntaxKind which get set so that we can report an error later
        /// </summary>
        SyntaxKind OriginalKeywordKind { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.QualifiedName }, NodeType = NodeType.Leaf)]
    public interface IQualifiedName : INode
    {
        /// <summary>
        /// Must have same layout as PropertyAccess
        /// </summary>
        EntityName Left { get; set; }

        /// <nodoc/>
        IIdentifier Right { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = null, NodeType = NodeType.Marker)]
    public interface IDeclaration : INode
    {
        /// <nodoc/>
        DeclarationName Name { get; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = null, NodeType = NodeType.Marker)]
    public interface IDeclarationStatement : IDeclaration, IStatement
    {
        /// <nodoc/>
        new IIdentifier Name { get; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.ComputedPropertyName }, NodeType = NodeType.Leaf)]
    public interface IComputedPropertyName : INode
    {
        /// <nodoc/>
        IExpression Expression { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.Decorator }, NodeType = NodeType.Leaf)]
    public interface IDecorator : INode
    {
        /// <nodoc/>
        ILeftHandSideExpression Expression { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.TypeParameter }, NodeType = NodeType.Leaf)]
    public interface ITypeParameterDeclaration : IDeclaration
    {
        /// <nodoc/>
        new IIdentifier Name { get; set; }

        /// <nodoc/>
        ITypeNode Constraint { get; set; }

        /// <nodoc/>
        // For error recovery purposes.
        IExpression Expression { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = null, NodeType = NodeType.Marker)]
    public interface ISignatureDeclaration : IDeclaration
    {
        /// <nodoc/>
        new PropertyName Name { get; }

        /// <nodoc/>
        [CanBeNull]
        NodeArray<ITypeParameterDeclaration> TypeParameters { get; set; }

        /// <nodoc/>
        [JetBrains.Annotations.NotNull]
        NodeArray<IParameterDeclaration> Parameters { get; set; }

        /// <nodoc/>
        ITypeNode Type { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.CallSignature }, NodeType = NodeType.Leaf)]
    public interface ICallSignatureDeclaration : IFunctionLikeDeclaration, ITypeElement
    {
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.ConstructSignature }, NodeType = NodeType.Leaf)]
    public interface IConstructSignatureDeclaration : IFunctionLikeDeclaration, ITypeElement
    {
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.CallSignature, SyntaxKind.ConstructSignature }, NodeType = NodeType.Marker)]
    public interface ICallSignatureDeclarationOrConstructSignatureDeclaration : ICallSignatureDeclaration,
        IConstructSignatureDeclaration
    {
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = null, NodeType = NodeType.Leaf)]
    public interface IVariableDeclaration : IDeclaration
    {
        // HINT: TypeScript implementation redeclares parent here as IVariableDeclarationList.
        //       Seems unnecessary (and causes test crash in C#)
        // new IVariableDeclarationList Parent { get; }

        /// <summary>
        /// Declared variable name
        /// </summary>
        new IdentifierOrBindingPattern Name { get; set; }

        /// <summary>
        /// Optional type annotation
        /// </summary>
        [CanBeNull]
        ITypeNode Type { get; set; }

        /// <summary>
        /// Optional initializer
        /// </summary>
        [CanBeNull]
        IExpression Initializer { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.VariableDeclarationList }, NodeType = NodeType.Leaf)]
    public interface IVariableDeclarationList : INode
    {
        /// <nodoc/>
        INodeArray<IVariableDeclaration> Declarations { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.Parameter }, NodeType = NodeType.Leaf)]
    public interface IParameterDeclaration : IDeclaration, IEquatable<IParameterDeclaration>
    {
        /// <summary>
        /// Present on rest parameter
        /// </summary>
        Optional<INode> DotDotDotToken { get; set; }

        /// <summary>
        /// Declared parameter name
        /// </summary>
        new IdentifierOrBindingPattern Name { get; set; }

        /// <summary>
        /// Present on optional parameter
        /// </summary>
        Optional<INode> QuestionToken { get; set; }

        /// <summary>
        /// Optional type annotation
        /// </summary>
        [CanBeNull]
        ITypeNode Type { get; set; }

        /// <summary>
        /// Optional initializer
        /// </summary>
        [CanBeNull]
        IExpression Initializer { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.BindingElement }, NodeType = NodeType.Leaf)]
    public interface IBindingElement : IDeclaration
    {
        /// <nodoc/>
        PropertyName PropertyName { get; set; } // Binding property name (in object binding pattern)

        /// <nodoc/>
        Optional<INode> DotDotDotToken { get; set; } // Present on rest binding element

        /// <nodoc/>
        new IdentifierOrBindingPattern Name { get; set; } // Declared binding element name

        /// <nodoc/>
        IExpression Initializer { get; set; } // Optional initializer
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.PropertySignature }, NodeType = NodeType.Leaf)]
    public interface IPropertySignature : ITypeElement
    {
        /// <nodoc/>
        new PropertyName Name { get; set; } // Declared property name

        /// <nodoc/>
        ITypeNode Type { get; set; } // Optional type annotation

        /// <nodoc/>
        IExpression Initializer { get; set; } // Optional initializer
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.PropertyDeclaration }, NodeType = NodeType.Leaf)]
    public interface IPropertyDeclaration : IClassElement
    {
        /// <nodoc/>
        Optional<INode> QuestionToken { get; set; } // Present for use with reporting a grammar error

        /// <nodoc/>
        new PropertyName Name { get; }

        /// <nodoc/>
        ITypeNode Type { get; set; }

        /// <nodoc/>
        IExpression Initializer { get; set; } // Optional initializer
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = null, NodeType = NodeType.Marker)]
    public interface IObjectLiteralElement : IDeclaration
    {
        /// <nodoc/>
        new PropertyName Name { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.PropertyAssignment }, NodeType = NodeType.Leaf)]
    public interface IPropertyAssignment : IObjectLiteralElement
    {
        /// <nodoc/>
        Optional<INode> QuestionToken { get; set; }

        /// <nodoc/>
        IExpression Initializer { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.ShorthandPropertyAssignment }, NodeType = NodeType.Leaf)]
    public interface IShorthandPropertyAssignment : IObjectLiteralElement
    {
        /// <nodoc/>
        Optional<INode> QuestionToken { get; set; }

        /// <summary>
        /// used when ObjectLiteralExpression is used in ObjectAssignmentPattern
        /// it is grammar error to appear in actual object initializer
        /// </summary>
        Optional<INode> EqualsToken { get; set; }

        /// <nodoc/>
        IExpression ObjectAssignmentInitializer { get; set; }
    }

    /// <summary>
    /// This is interface is an artificial Role-interface that helps polimorphyc consumption of different node types.
    /// TypeScript type system is weak and it allows explicit casting instances to unrelated interfaces.
    /// In typescript you can easily cast VariableDeclaration to VariableLikeDeclaration, but the same cast would be invalid in C#.
    /// In order to allow TypeScript idioms in this code, every type that implements mentioned syntax kinds have to
    /// implement <see cref="IVariableLikeDeclaration"/> interface.
    /// </summary>
    [NodeInfo(
        SyntaxKinds = new[]
    {
        SyntaxKind.VariableDeclaration, SyntaxKind.Parameter, SyntaxKind.BindingElement, SyntaxKind.PropertyDeclaration,
        SyntaxKind.PropertyAssignment, SyntaxKind.ShorthandPropertyAssignment, SyntaxKind.EnumMember,
    }, NodeType = NodeType.Marker)]
    public interface IVariableLikeDeclaration : IDeclaration
    {
        /// <nodoc/>
        PropertyName PropertyName { get; }

        /// <nodoc/>
        Optional<INode> DotDotDotToken { get; }

        /// <nodoc/>
        Optional<INode> QuestionToken { get; }

        /// <nodoc/>
        ITypeNode Type { get; }

        /// <nodoc/>
        IExpression Initializer { get; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = null, NodeType = NodeType.Marker)]
    public interface IPropertyLikeDeclaration : IDeclaration
    {
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = null, NodeType = NodeType.Leaf)]
    public interface IBindingPattern : INode
    {
        /// <nodoc/>
        NodeArray<IBindingElement> Elements { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.ObjectBindingPattern }, NodeType = NodeType.Marker)]
    public interface IObjectBindingPattern : IBindingPattern
    {
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.ArrayBindingPattern }, NodeType = NodeType.Marker)]
    public interface IArrayBindingPattern : IBindingPattern
    {
    }

    /// <summary>
    /// Several node kinds share function-like features such as a signature,
    /// a name, and a body. These nodes should extend FunctionLikeDeclaration.
    /// Examples:
    /// - FunctionDeclaration
    /// - MethodDeclaration
    /// - AccessorDeclaration
    /// </summary>
    [NodeInfo(SyntaxKinds = null, NodeType = NodeType.Abstract)]
    public interface IFunctionLikeDeclaration : ISignatureDeclaration
    {
        /// <nodoc/>
        Optional<INode> AsteriskToken { get; }

        /// <nodoc/>
        Optional<INode> QuestionToken { get; }

        /// <nodoc/>
        ConciseBody Body { get; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.FunctionDeclaration }, NodeType = NodeType.Leaf)]
    public interface IFunctionDeclaration : IFunctionLikeDeclaration, IDeclarationStatement
    {
        /// <nodoc/>
        new IIdentifier Name { get; set; }

        /// <nodoc/>
        new IBlock Body { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.MethodSignature }, NodeType = NodeType.Leaf)]
    public interface IMethodSignature : IFunctionLikeDeclaration, ITypeElement
    {
        /// <nodoc/>
        new PropertyName Name { get; }
    }

    /// <summary>
    /// Note that a MethodDeclaration is considered both a ClassElement and an ObjectLiteralElement.
    /// Both the grammars for ClassDeclaration and ObjectLiteralExpression allow for MethodDeclarations
    /// as child elements, and so a MethodDeclaration satisfies both interfaces.  This avoids the
    /// alternative where we would need separate kinds/types for ClassMethodDeclaration and
    /// ObjectLiteralMethodDeclaration, which would look identical.
    ///
    /// Because of this, it may be necessary to determine what sort of MethodDeclaration you have
    /// at later stages of the compiler pipeline.  In that case, you can either check the parent kind
    /// of the method, or use helpers like isObjectLiteralMethodDeclaration
    /// </summary>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.MethodDeclaration }, NodeType = NodeType.Leaf)]
    public interface IMethodDeclaration : IFunctionLikeDeclaration, IClassElement, IObjectLiteralElement
    {
        /// <nodoc/>
        new PropertyName Name { get; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.Constructor }, NodeType = NodeType.Leaf)]
    public interface IConstructorDeclaration : IFunctionLikeDeclaration, IClassElement
    {
        // new PropertyName Name { get; set; } // TODO: HD should be uncommented but then the current implementation breaks
    }

    /// <summary>
    /// For when we encounter a semicolon in a class declaration.  ES6 allows these as class elements.
    /// </summary>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.SemicolonClassElement }, NodeType = NodeType.Leaf)]
    public interface ISemicolonClassElement : IClassElement
    {
    }

    /// <summary>
    /// See the comment on MethodDeclaration for the intuition behind AccessorDeclaration being a
    /// ClassElement and an ObjectLiteralElement.
    /// </summary>
    [NodeInfo(SyntaxKinds = null, NodeType = NodeType.Leaf)]
    public interface IAccessorDeclaration : IFunctionLikeDeclaration, IClassElement, IObjectLiteralElement
    {
        /// <nodoc/>
        new PropertyName Name { get; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.GetAccessor }, NodeType = NodeType.Marker)]
    public interface IGetAccessorDeclaration : IAccessorDeclaration
    {
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.SetAccessor }, NodeType = NodeType.Marker)]
    public interface ISetAccessorDeclaration : IAccessorDeclaration
    {
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.IndexSignature }, NodeType = NodeType.Leaf)]
    public interface IIndexSignatureDeclaration : IFunctionLikeDeclaration, IClassElement, ITypeElement
    {
    }

    /// <nodoc/>
    [NodeInfo(
        SyntaxKinds = new[]
    {
        SyntaxKind.AnyKeyword, SyntaxKind.NumberKeyword, SyntaxKind.BooleanKeyword,
        SyntaxKind.StringKeyword, SyntaxKind.SymbolKeyword, SyntaxKind.VoidKeyword,
    }, NodeType = NodeType.Leaf)]
    public interface ITypeNode : INode, IEquatable<ITypeNode>
    {
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.ThisType }, NodeType = NodeType.Leaf)]
    public interface IThisTypeNode : ITypeNode
    {
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = null, NodeType = NodeType.Marker)]
    public interface IFunctionOrConstructorTypeNode : ITypeNode, IFunctionLikeDeclaration
    {
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.FunctionType }, NodeType = NodeType.Leaf)]
    public interface IFunctionTypeNode : IFunctionLikeDeclaration, IFunctionOrConstructorTypeNode
    {
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.ConstructorType }, NodeType = NodeType.Leaf)]
    public interface IConstructorTypeNode : IFunctionLikeDeclaration, IFunctionOrConstructorTypeNode
    {
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.TypeReference }, NodeType = NodeType.Leaf)]
    public interface ITypeReferenceNode : ITypeNode
    {
        /// <nodoc/>
        EntityName TypeName { get; set; }

        /// <nodoc/>
        [CanBeNull]
        NodeArray<ITypeNode> TypeArguments { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.TypePredicate }, NodeType = NodeType.Leaf)]
    public interface ITypePredicateNode : ITypeNode
    {
        /// <nodoc/>
        IdentifierOrThisTypeUnionNode ParameterName { get; set; }

        /// <nodoc/>
        ITypeNode Type { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.TypeQuery }, NodeType = NodeType.Leaf)]
    public interface ITypeQueryNode : ITypeNode
    {
        /// <nodoc/>
        EntityName ExprName { get; set; }
    }

    /// <summary>
    /// A TypeLiteral is the declaration node for an anonymous symbol.
    /// </summary>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.TypeLiteral }, NodeType = NodeType.Leaf)]
    public interface ITypeLiteralNode : ITypeNode, IDeclaration
    {
        /// <nodoc/>
        NodeArray<ITypeElement> Members { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.ArrayType }, NodeType = NodeType.Leaf)]
    public interface IArrayTypeNode : ITypeNode
    {
        /// <nodoc/>
        ITypeNode ElementType { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.TupleType }, NodeType = NodeType.Leaf)]
    public interface ITupleTypeNode : ITypeNode
    {
        /// <nodoc/>
        NodeArray<ITypeNode> ElementTypes { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = null, NodeType = NodeType.Marker)]
    public interface IUnionOrIntersectionTypeNode : ITypeNode
    {
        /// <nodoc/>
        NodeArray<ITypeNode> Types { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.UnionType }, NodeType = NodeType.Leaf)]
    public interface IUnionTypeNode : IUnionOrIntersectionTypeNode
    {
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.IntersectionType }, NodeType = NodeType.Leaf)]
    public interface IIntersectionTypeNode : IUnionOrIntersectionTypeNode
    {
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.ParenthesizedType }, NodeType = NodeType.Leaf)]
    public interface IParenthesizedTypeNode : ITypeNode
    {
        /// <nodoc/>
        ITypeNode Type { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.StringLiteralType }, NodeType = NodeType.Leaf)]
    public interface IStringLiteralTypeNode : ILiteralLikeNode, ITypeNode
    {
        ///// <summary>
        ///// String literal kind.
        ///// </summary>
        ///// <remarks>
        ///// This is extension to the existing AST to preserve kind of a string literal, i.e., double quoted or single quoted.
        ///// </remarks>
        // LiteralExpressionKind LiteralKind { get; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.StringLiteral }, NodeType = NodeType.Marker)]
    public interface IStringLiteral : ILiteralExpression
    {
        /// <summary>
        /// String literal kind.
        /// </summary>
        /// <remarks>
        /// This is extension to the existing AST to preserve kind of a string literal, i.e., double quoted or single quoted.
        /// </remarks>
        LiteralExpressionKind LiteralKind { get; }
    }

    /// <summary>
    /// Note: 'brands' in our syntax nodes serve to give us a small amount of nominal typing.
    /// Consider 'Expression'.  Without the brand, 'Expression' is actually no different
    /// (structurally) than 'Node'.  Because of this you can pass any Node to a function that
    /// takes an Expression without any error.  By using the 'brands' we ensure that the type
    /// checker actually thinks you have something of the right type.  the Note brands are
    /// never actually given values.  At runtime they have zero cost.
    /// </summary>
    [NodeInfo(SyntaxKinds = null, NodeType = NodeType.Leaf)]
    public interface IExpression : INode
    {
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.OmittedExpression }, NodeType = NodeType.Marker)]
    public interface IOmittedExpression : IExpression
    {
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = null, NodeType = NodeType.Marker)]
    public interface IUnaryExpression : IExpression
    {
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = null, NodeType = NodeType.Marker)]
    public interface IIncrementExpression : IUnaryExpression
    {
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.PrefixUnaryExpression }, NodeType = NodeType.Leaf)]
    public interface IPrefixUnaryExpression : IIncrementExpression
    {
        /// <nodoc/>
        SyntaxKind Operator { get; set; }

        /// <nodoc/>
        IUnaryExpression Operand { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.PostfixUnaryExpression }, NodeType = NodeType.Leaf)]
    public interface IPostfixUnaryExpression : IIncrementExpression
    {
        /// <nodoc/>
        ILeftHandSideExpression Operand { get; set; }

        /// <nodoc/>
        SyntaxKind Operator { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = null, NodeType = NodeType.Marker)]
    public interface IPostfixExpression : IUnaryExpression
    {
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = null, NodeType = NodeType.Marker)]
    public interface ILeftHandSideExpression : IIncrementExpression
    {
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = null, NodeType = NodeType.Marker)]
    public interface IMemberExpression : ILeftHandSideExpression
    {
    }

    /// <nodoc/>
    [NodeInfo(
        SyntaxKinds =
        new[]
        { SyntaxKind.TrueKeyword, SyntaxKind.FalseKeyword, SyntaxKind.NullKeyword, SyntaxKind.ThisKeyword, SyntaxKind.SuperKeyword },
        NodeType = NodeType.Leaf)]
    public interface IPrimaryExpression : IMemberExpression
    {
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.DeleteExpression }, NodeType = NodeType.Leaf)]
    public interface IDeleteExpression : IUnaryExpression
    {
        /// <nodoc/>
        IUnaryExpression Expression { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.TypeOfExpression }, NodeType = NodeType.Leaf)]
    public interface ITypeOfExpression : IUnaryExpression
    {
        /// <nodoc/>
        IUnaryExpression Expression { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.VoidExpression }, NodeType = NodeType.Leaf)]
    public interface IVoidExpression : IUnaryExpression
    {
        /// <nodoc/>
        IUnaryExpression Expression { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.AwaitExpression }, NodeType = NodeType.Marker)]
    public interface IAwaitExpression : IUnaryExpression
    {
        /// <nodoc/>
        IUnaryExpression Expression { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.YieldExpression }, NodeType = NodeType.Leaf)]
    public interface IYieldExpression : IExpression
    {
        /// <nodoc/>
        INode AsteriskToken { get; set; }

        /// <nodoc/>
        IExpression Expression { get; set; }
    }

    /// <summary>
    /// Binary expressions can be declarations if they are 'exports.foo = bar' expressions in JS files
    /// </summary>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.BinaryExpression }, NodeType = NodeType.Leaf)]
    public interface IBinaryExpression : IExpression, IDeclaration
    {
        /// <nodoc/>
        IExpression Left { get; set; }

        /// <nodoc/>
        INode OperatorToken { get; set; }

        /// <nodoc/>
        IExpression Right { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.ConditionalExpression }, NodeType = NodeType.Leaf)]
    public interface IConditionalExpression : IExpression
    {
        /// <nodoc/>
        IExpression Condition { get; set; }

        /// <nodoc/>
        INode QuestionToken { get; set; }

        /// <nodoc/>
        IExpression WhenTrue { get; set; }

        /// <nodoc/>
        INode ColonToken { get; set; }

        /// <nodoc/>
        IExpression WhenFalse { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.SwitchExpression }, NodeType = NodeType.Leaf)]
    public interface ISwitchExpression : IExpression
    {
        /// <nodoc/>
        IExpression Expression { get; set; }

        /// <nodoc/>
        NodeArray<ISwitchExpressionClause> Clauses { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.SwitchExpressionClause }, NodeType = NodeType.Leaf)]
    public interface ISwitchExpressionClause : IExpression
    {
        /// <summary>
        /// This indicates the clause is the default case. as in: `default: 10`.
        /// This means the Match expression will be null.
        /// </summary>
        bool IsDefaultFallthrough { get; set; }

        /// <nodoc/>
        IExpression Match { get; set; }

        /// <nodoc/>
        IExpression Expression { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.FunctionExpression }, NodeType = NodeType.Leaf)]
    public interface IFunctionExpression : IPrimaryExpression, IFunctionLikeDeclaration
    {
        /// <nodoc/>
        new IIdentifier Name { get; }

        // new IBlock Body { get; set; }  // Required, whereas the member inherited from FunctionDeclaration is optional
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.ArrowFunction }, NodeType = NodeType.Leaf)]
    public interface IArrowFunction : IExpression, IFunctionLikeDeclaration
    {
        /// <nodoc/>
        ITokenNode EqualsGreaterThanToken { get; set; }

        /// <nodoc/>
        // Required, whereas the member inherited from FunctionDeclaration is optional
        new ConciseBody Body { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = null, NodeType = NodeType.Marker)]
    public interface ILiteralLikeNode : INode, IHasText
    {
        /// <nodoc/>
        bool IsUnterminated { get; set; }

        /// <nodoc/>
        bool HasExtendedUnicodeEscape { get; set; }
    }

    /// <summary>
    /// The text property of a LiteralExpression stores the interpreted value of the literal in text form. For a StringLiteral,
    /// or any literal of a template, this means quotes have been removed and escapes have been converted to actual characters.
    /// For a NumericLiteral, the stored value is the toString() representation of the int. For example 1, 1.00, and 1e0 are all stored as just "1".
    /// </summary>
    [NodeInfo(
        SyntaxKinds =
            new[] { SyntaxKind.NumericLiteral, SyntaxKind.RegularExpressionLiteral, SyntaxKind.NoSubstitutionTemplateLiteral },
        NodeType = NodeType.Leaf)]
    public interface ILiteralExpression : ILiteralLikeNode, IPrimaryExpression
    {
    }

    /// <nodoc/>
    [NodeInfo(
        SyntaxKinds = new[] { SyntaxKind.TemplateHead, SyntaxKind.TemplateMiddle, SyntaxKind.TemplateTail },
        NodeType = NodeType.Leaf)]
    public interface ITemplateLiteralFragment : ILiteralExpression
    {
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.TemplateExpression }, NodeType = NodeType.Leaf)]
    public interface ITemplateExpression : IPrimaryExpression
    {
        /// <nodoc/>
        ITemplateLiteralFragment Head { get; set; }

        /// <nodoc/>
        INodeArray<ITemplateSpan> TemplateSpans { get; set; }
    }

    /// <summary>
    /// Each of these corresponds to a substitution expression and a template literal, in that order.
    /// The template literal must have kind TemplateMiddleLiteral or TemplateTailLiteral.
    /// </summary>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.TemplateSpan }, NodeType = NodeType.Leaf)]
    public interface ITemplateSpan : INode
    {
        /// <nodoc/>
        IExpression Expression { get; set; }

        /// <nodoc/>
        ITemplateLiteralFragment Literal { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.ParenthesizedExpression }, NodeType = NodeType.Leaf)]
    public interface IParenthesizedExpression : IPrimaryExpression
    {
        /// <nodoc/>
        IExpression Expression { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.ArrayLiteralExpression }, NodeType = NodeType.Leaf)]
    public interface IArrayLiteralExpression : IPrimaryExpression
    {
        /// <nodoc/>
        [JetBrains.Annotations.NotNull]
        NodeArray<IExpression> Elements { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.SpreadElementExpression }, NodeType = NodeType.Leaf)]
    public interface ISpreadElementExpression : IExpression
    {
        /// <nodoc/>
        IExpression Expression { get; set; }
    }

    /// <summary>
    /// An ObjectLiteralExpression is the declaration node for an anonymous symbol.
    /// </summary>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.ObjectLiteralExpression }, NodeType = NodeType.Leaf)]
    public interface IObjectLiteralExpression : IPrimaryExpression, IDeclaration
    {
        /// <nodoc/>
        NodeArray<IObjectLiteralElement> Properties { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.PropertyAccessExpression }, NodeType = NodeType.Leaf)]
    public interface IPropertyAccessExpression : IMemberExpression, IDeclaration
    {
        /// <nodoc/>
        ILeftHandSideExpression Expression { get; set; }

        /// <nodoc/>
        INode DotToken { get; set; }

        /// <nodoc/>
        new IIdentifier Name { get; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.ElementAccessExpression }, NodeType = NodeType.Leaf)]
    public interface IElementAccessExpression : IMemberExpression
    {
        /// <nodoc/>
        ILeftHandSideExpression Expression { get; set; }

        /// <nodoc/>
        [CanBeNull]
        IExpression ArgumentExpression { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.CallExpression }, NodeType = NodeType.Leaf)]
    public interface ICallExpression : ILeftHandSideExpression
    {
        /// <nodoc/>
        ILeftHandSideExpression Expression { get; set; }

        /// <nodoc/>
        NodeArray<ITypeNode> TypeArguments { get; set; }

        /// <nodoc/>
        NodeArray<IExpression> Arguments { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.ExpressionWithTypeArguments }, NodeType = NodeType.Leaf)]
    public interface IExpressionWithTypeArguments : ITypeNode
    {
        /// <nodoc/>
        ILeftHandSideExpression Expression { get; set; }

        /// <nodoc/>
        NodeArray<ITypeNode> TypeArguments { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.NewExpression }, NodeType = NodeType.Leaf)]
    public interface INewExpression : ICallExpression, IPrimaryExpression
    {
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.TaggedTemplateExpression }, NodeType = NodeType.Leaf)]
    public interface ITaggedTemplateExpression : IMemberExpression
    {
        /// <nodoc/>
        ILeftHandSideExpression Tag { get; set; }

        /// <summary>
        /// Template expression of the tagged literal.
        /// This is an expression wrapped in backticks.
        /// </summary>
        /// <remarks>
        /// Obsolete. Left for backward compatibility reasons.
        /// </remarks>
        LiteralExpressionOrTemplateExpression Template { get; set; }

        /// <summary>
        /// Template expression of the tagged literal.
        /// </summary>
        /// <remarks>
        /// This could be only <see cref="ILiteralExpression"/> or <see cref="ITemplateExpression"/>, but the base type of the two is used to avoid redundant allocations.
        /// </remarks>
        IPrimaryExpression TemplateExpression { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.AsExpression }, NodeType = NodeType.Leaf)]
    public interface IAsExpression : IExpression
    {
        /// <nodoc/>
        IExpression Expression { get; set; }

        /// <nodoc/>
        ITypeNode Type { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.TypeAssertionExpression }, NodeType = NodeType.Leaf)]
    public interface ITypeAssertion : IUnaryExpression
    {
        /// <nodoc/>
        ITypeNode Type { get; set; }

        /// <nodoc/>
        IUnaryExpression Expression { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = null, NodeType = NodeType.Marker)]
    public interface IStatement : INode
    {
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.EmptyStatement }, NodeType = NodeType.Leaf)]
    public interface IEmptyStatement : IStatement
    {
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.SingleLineCommentTrivia }, NodeType = NodeType.Leaf)]
    public interface ISingleLineCommentExpression : ICommentExpression
    {
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.MultiLineCommentTrivia }, NodeType = NodeType.Leaf)]
    public interface IMultiLineCommentExpression : ICommentExpression
    {
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.MultiLineCommentTrivia, SyntaxKind.SingleLineCommentTrivia }, NodeType = NodeType.Leaf)]
    public interface ICommentExpression : IExpression, IHasText
    {
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = null, NodeType = NodeType.Marker)]
    public interface ICommentStatement : IStatement
    {
        /// <nodoc/>
        ICommentExpression CommentExpression { get; set; }
    }

    /// <nodoc/>
    // This interface is not originally part of TypeScript.Net, and is added just for pretty printing purposes
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.BlankLineStatement }, NodeType = NodeType.Leaf)]
    public interface IBlankLineStatement : IStatement
    {
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.MissingDeclaration }, NodeType = NodeType.Marker)]
    public interface IMissingDeclaration : IDeclarationStatement, IClassElement, IObjectLiteralElement, ITypeElement
    {
        /// <nodoc/>
        new IIdentifier Name { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.Block }, NodeType = NodeType.Leaf)]
    public interface IBlock : IStatement, IStatementsContainer
    {
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.VariableStatement }, NodeType = NodeType.Leaf)]
    public interface IVariableStatement : IStatement
    {
        /// <nodoc/>
        IVariableDeclarationList DeclarationList { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.ExpressionStatement }, NodeType = NodeType.Leaf)]
    public interface IExpressionStatement : IStatement
    {
        /// <nodoc/>
        IExpression Expression { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.IfStatement }, NodeType = NodeType.Leaf)]
    public interface IIfStatement : IStatement
    {
        /// <nodoc/>
        IExpression Expression { get; set; }

        /// <nodoc/>
        IStatement ThenStatement { get; set; }

        /// <nodoc/>
        Optional<IStatement> ElseStatement { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = null, NodeType = NodeType.Marker)]
    public interface IIterationStatement : IStatement
    {
        /// <nodoc/>
        IStatement Statement { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.DoStatement }, NodeType = NodeType.Leaf)]
    public interface IDoStatement : IIterationStatement
    {
        /// <nodoc/>
        IExpression Expression { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.WhileStatement }, NodeType = NodeType.Leaf)]
    public interface IWhileStatement : IIterationStatement
    {
        /// <nodoc/>
        IExpression Expression { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.ForStatement }, NodeType = NodeType.Leaf)]
    public interface IForStatement : IIterationStatement
    {
        /// <nodoc/>
        [CanBeNull]
        VariableDeclarationListOrExpression Initializer { get; set; }

        /// <nodoc/>
        [CanBeNull]
        IExpression Condition { get; set; }

        /// <nodoc/>
        [CanBeNull]
        IExpression Incrementor { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.ForInStatement }, NodeType = NodeType.Leaf)]
    public interface IForInStatement : IIterationStatement
    {
        /// <nodoc/>
        VariableDeclarationListOrExpression Initializer { get; set; }

        /// <nodoc/>
        IExpression Expression { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.ForOfStatement }, NodeType = NodeType.Leaf)]
    public interface IForOfStatement : IIterationStatement
    {
        /// <nodoc/>
        VariableDeclarationListOrExpression Initializer { get; set; }

        /// <nodoc/>
        IExpression Expression { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.BreakStatement }, NodeType = NodeType.Leaf)]
    public interface IBreakStatement : IBreakOrContinueStatement
    {
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.ContinueStatement }, NodeType = NodeType.Leaf)]
    public interface IContinueStatement : IBreakOrContinueStatement
    {
    }

    /// <summary>
    /// Artificial interface that simplifies migration from TypeScript code
    /// </summary>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.BreakStatement, SyntaxKind.ContinueStatement }, NodeType = NodeType.Marker)]
    public interface IBreakOrContinueStatement : IStatement
    {
        /// <nodoc/>
        [CanBeNull]
        IIdentifier Label { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.ReturnStatement }, NodeType = NodeType.Leaf)]
    public interface IReturnStatement : IExpressionStatement
    {
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.WithStatement }, NodeType = NodeType.Leaf)]
    public interface IWithStatement : IStatement
    {
        /// <nodoc/>
        IExpression Expression { get; set; }

        /// <nodoc/>
        IStatement Statement { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.SwitchStatement }, NodeType = NodeType.Leaf)]
    public interface ISwitchStatement : IStatement
    {
        /// <nodoc/>
        IExpression Expression { get; set; }

        /// <nodoc/>
        ICaseBlock CaseBlock { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.CaseBlock }, NodeType = NodeType.Leaf)]
    public interface ICaseBlock : INode
    {
        /// <nodoc/>
        NodeArray<CaseClauseOrDefaultClause> Clauses { get; set; }
    }

    /// <nodoc/>
    public static class CaseBlockExtensions
    {
        /// <nodoc/>
        public static IEnumerable<ICaseClause> GetCaseClauses(this ICaseBlock caseBlock)
        {
            return caseBlock.Clauses.Select(c => c.AsCaseClause()).Where(c => c != null);
        }

        /// <nodoc/>
        public static IDefaultClause GetDefaultClause(this ICaseBlock caseBlock)
        {
            return caseBlock.Clauses.Select(c => c.AsDefaultClause()).FirstOrDefault(c => c != null);
        }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.CaseClause }, NodeType = NodeType.Leaf)]
    public interface ICaseClause : INode, IStatementsContainer
    {
        /// <nodoc/>
        IExpression Expression { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.DefaultClause }, NodeType = NodeType.Leaf)]
    public interface IDefaultClause : INode, IStatementsContainer
    {
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.LabeledStatement }, NodeType = NodeType.Leaf)]
    public interface ILabeledStatement : IStatement
    {
        /// <nodoc/>
        IIdentifier Label { get; set; }

        /// <nodoc/>
        IStatement Statement { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.DebuggerStatement }, NodeType = NodeType.Leaf)]
    public interface IDebuggerStatement : IStatement
    {
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.ThrowStatement }, NodeType = NodeType.Leaf)]
    public interface IThrowStatement : IStatement
    {
        /// <nodoc/>
        IExpression Expression { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.TryStatement }, NodeType = NodeType.Leaf)]
    public interface ITryStatement : IStatement
    {
        /// <nodoc/>
        IBlock TryBlock { get; set; }

        /// <nodoc/>
        ICatchClause CatchClause { get; set; }

        /// <nodoc/>
        IBlock FinallyBlock { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.CatchClause }, NodeType = NodeType.Leaf)]
    public interface ICatchClause : INode
    {
        /// <nodoc/>
        IVariableDeclaration VariableDeclaration { get; set; }

        /// <nodoc/>
        IBlock Block { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = null, NodeType = NodeType.Abstract)]
    public interface IClassLikeDeclaration : IDeclaration
    {
        /// <nodoc/>
        new IIdentifier Name { get; set; } // TODO:readonly

        /// <nodoc/>
        NodeArray<ITypeParameterDeclaration> TypeParameters { get; set; }

        /// <nodoc/>
        NodeArray<IHeritageClause> HeritageClauses { get; set; }

        /// <nodoc/>
        NodeArray<IClassElement> Members { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.ClassDeclaration }, NodeType = NodeType.Leaf)]
    public interface IClassDeclaration : IClassLikeDeclaration, IDeclarationStatement
    {
        /// <nodoc/>
        new IIdentifier Name { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.ClassExpression }, NodeType = NodeType.Leaf)]
    public interface IClassExpression : IClassLikeDeclaration, IPrimaryExpression
    {
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = null, NodeType = NodeType.Leaf)]
    public interface IClassElement : IDeclaration
    {
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = null, NodeType = NodeType.Marker)]
    public interface ITypeElement : IDeclaration
    {
        /// <nodoc/>
        Optional<INode> QuestionToken { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.InterfaceDeclaration }, NodeType = NodeType.Leaf)]
    public interface IInterfaceDeclaration : IDeclarationStatement
    {
        /// <nodoc/>
        new IIdentifier Name { get; set; }

        /// <nodoc/>
        NodeArray<ITypeParameterDeclaration> TypeParameters { get; set; }

        /// <nodoc/>
        NodeArray<IHeritageClause> HeritageClauses { get; set; }

        /// <nodoc/>
        NodeArray<ITypeElement> Members { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.HeritageClause }, NodeType = NodeType.Leaf)]
    public interface IHeritageClause : INode
    {
        /// <nodoc/>
        SyntaxKind Token { get; set; }

        /// <nodoc/>
        NodeArray<IExpressionWithTypeArguments> Types { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.TypeAliasDeclaration }, NodeType = NodeType.Leaf)]
    public interface ITypeAliasDeclaration : IDeclarationStatement
    {
        /// <nodoc/>
        new IIdentifier Name { get; set; }

        /// <nodoc/>
        NodeArray<ITypeParameterDeclaration> TypeParameters { get; set; }

        /// <nodoc/>
        ITypeNode Type { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.EnumMember }, NodeType = NodeType.Leaf)]
    public interface IEnumMember : IDeclaration
    {
        /// <summary>
        /// This does include ComputedPropertyName, but the parser will give an error
        /// if it parses a ComputedPropertyName in an EnumMember
        /// </summary>
        new DeclarationName Name { get; set; }

        /// <nodoc/>
        Optional<IExpression> Initializer { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.EnumDeclaration }, NodeType = NodeType.Leaf)]
    public interface IEnumDeclaration : IDeclarationStatement
    {
        /// <nodoc/>
        new IIdentifier Name { get; set; }

        /// <nodoc/>
        NodeArray<IEnumMember> Members { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.ModuleDeclaration }, NodeType = NodeType.Leaf)]
    public interface IModuleDeclaration : IDeclarationStatement
    {
        /// <nodoc/>
        new IdentifierOrLiteralExpression Name { get; set; }

        /// <nodoc/>
        [JetBrains.Annotations.NotNull]
        ModuleBody Body { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.ModuleBlock }, NodeType = NodeType.Leaf)]
    public interface IModuleBlock : IStatement, IStatementsContainer
    {
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.ImportEqualsDeclaration }, NodeType = NodeType.Leaf)]
    public interface IImportEqualsDeclaration : IDeclarationStatement
    {
        /// <nodoc/>
        new IIdentifier Name { get; set; }

        /// <summary>
        /// 'EntityName' for an internal module reference, 'ExternalModuleReference' for an external
        /// module reference.
        /// </summary>
        EntityNameOrExternalModuleReference ModuleReference { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.ExternalModuleReference }, NodeType = NodeType.Leaf)]
    public interface IExternalModuleReference : INode
    {
        /// <nodoc/>
        IExpression Expression { get; set; }
    }

    /// <summary>
    /// In case of:
    /// import "mod"  => importClause = undefined, moduleSpecifier = "mod"
    /// In rest of the cases, module specifier is string literal corresponding to module
    /// ImportClause information is shown at its declaration below.
    /// For DScript following syntax is valid:
    ///   import * from 'mod' with {qualifierObjectLiteral}
    /// </summary>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.ImportDeclaration }, NodeType = NodeType.Leaf)]
    public interface IImportDeclaration : IStatement
    {
        /// <nodoc/>
        IImportClause ImportClause { get; set; }

        /// <nodoc/>
        IExpression ModuleSpecifier { get; set; }

        /// <summary>
        /// True for import * from like imports. In this case different name merging scheme should used.
        /// </summary>
        bool IsLikeImport { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.ImportClause }, NodeType = NodeType.Leaf)]
    public interface IImportClause : IDeclaration
    {
        /// <nodoc/>
        NamespaceImportOrNamedImports NamedBindings { get; set; }
    }

    /// <summary>
    /// In case of:
    /// import d from "mod" => Name = d, namedBinding = undefined
    /// import * as ns from "mod" => Name = undefined, NamespaceImport namedBinding = { ns Name }
    /// import d, * as ns from "mod" => Name = d,      NamespaceImport namedBinding = { ns Name }
    /// import { a, b as x } from "mod" => Name = undefined, NamedImports namedBinding = { elements: [{ a Name }, { x Name, b propertyName}]}
    /// import d, { a, b as x } from "mod" => Name = d, NamedImports namedBinding = { elements: [{ a Name }, { x Name, b propertyName}]}
    /// </summary>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.NamespaceImport }, NodeType = NodeType.Leaf)]
    public interface INamespaceImport : IDeclaration
    {
        /// <nodoc/>
        new IIdentifier Name { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.ExportDeclaration }, NodeType = NodeType.Leaf)]
    public interface IExportDeclaration : IDeclarationStatement
    {
        /// <nodoc/>
        INamedExports ExportClause { get; set; }

        /// <nodoc/>
        IExpression ModuleSpecifier { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.NamedImports }, NodeType = NodeType.Leaf)]
    public interface INamedImports : INode
    {
        /// <nodoc/>
        NodeArray<IImportSpecifier> Elements { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.NamedExports }, NodeType = NodeType.Leaf)]
    public interface INamedExports : INode
    {
        /// <nodoc/>
        NodeArray<IExportSpecifier> Elements { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = null, NodeType = NodeType.Marker)]
    public interface IImportOrExportSpecifier : IDeclaration
    {
        /// <summary>
        /// Name preceding "as" keyword (or undefined when "as" is absent)
        /// </summary>
        IIdentifier PropertyName { get; set; }

        /// <summary>
        /// Declared name
        /// </summary>
        new IIdentifier Name { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.ImportSpecifier }, NodeType = NodeType.Leaf)]
    public interface IImportSpecifier : IImportOrExportSpecifier
    {
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.ExportSpecifier }, NodeType = NodeType.Leaf)]
    public interface IExportSpecifier : IImportOrExportSpecifier
    {
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.ExportAssignment }, NodeType = NodeType.Leaf)]
    public interface IExportAssignment : IDeclarationStatement
    {
        /// <nodoc/>
        Optional<bool> IsExportEquals { get; set; }

        /// <nodoc/>
        IExpression Expression { get; set; }
    }

    /// <nodoc/>
    public interface IFileReference : ITextRange
    {
        /// <nodoc/>
        string FileName { get; set; }
    }

    /// <nodoc/>
    public interface ICommentRange : ITextRange
    {
        /// <nodoc/>
        Optional<bool> HasTrailingNewLine { get; set; }

        /// <nodoc/>
        SyntaxKind Kind { get; set; }
    }

    /// <summary>
    /// represents a top level: { type } expression in a JSDoc comment.
    /// </summary>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.JsDocTypeExpression }, NodeType = NodeType.Marker)]
    public interface IJsDocTypeExpression : INode
    {
        /// <nodoc/>
        IJsDocType Type { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = null, NodeType = NodeType.Marker)]
    public interface IJsDocType : ITypeNode
    {
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.JsDocAllType }, NodeType = NodeType.Marker)]
    public interface IJsDocAllType : IJsDocType
    {
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.JsDocUnknownType }, NodeType = NodeType.Marker)]
    public interface IJsDocUnknownType : IJsDocType
    {
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.JsDocArrayType }, NodeType = NodeType.Marker)]
    public interface IJsDocArrayType : IJsDocType
    {
        /// <nodoc/>
        IJsDocType ElementType { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.JsDocUnionType }, NodeType = NodeType.Marker)]
    public interface IJsDocUnionType : IJsDocType
    {
        /// <nodoc/>
        NodeArray<IJsDocType> Types { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.JsDocTupleType }, NodeType = NodeType.Marker)]
    public interface IJsDocTupleType : IJsDocType
    {
        /// <nodoc/>
        NodeArray<IJsDocType> Types { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.JsDocNonNullableType }, NodeType = NodeType.Marker)]
    public interface IJsDocNonNullableType : IJsDocType
    {
        /// <nodoc/>
        IJsDocType Type { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.JsDocNullableType }, NodeType = NodeType.Marker)]
    public interface IJsDocNullableType : IJsDocType
    {
        /// <nodoc/>
        IJsDocType Type { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.JsDocRecordType }, NodeType = NodeType.Marker)]
    public interface IJsDocRecordType : IJsDocType, ITypeLiteralNode
    {
        /// <nodoc/>
        new NodeArray<IJsDocRecordMember> Members { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.JsDocTypeReference }, NodeType = NodeType.Marker)]
    public interface IJsDocTypeReference : IJsDocType
    {
        /// <nodoc/>
        EntityName Name { get; }

        /// <nodoc/>
        NodeArray<IJsDocType> TypeArguments { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.JsDocOptionalType }, NodeType = NodeType.Marker)]
    public interface IJsDocOptionalType : IJsDocType
    {
        /// <nodoc/>
        IJsDocType Type { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.JsDocFunctionType }, NodeType = NodeType.Marker)]
    public interface IJsDocFunctionType : IJsDocType, ISignatureDeclaration
    {
        /// <nodoc/>
        new NodeArray<IParameterDeclaration> Parameters { get; set; }

        // new IJsDocType Type { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.JsDocVariadicType }, NodeType = NodeType.Marker)]
    public interface IJsDocVariadicType : IJsDocType
    {
        /// <nodoc/>
        IJsDocType Type { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.JsDocConstructorType }, NodeType = NodeType.Marker)]
    public interface IJsDocConstructorType : IJsDocType
    {
        /// <nodoc/>
        IJsDocType Type { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.JsDocThisType }, NodeType = NodeType.Marker)]
    public interface IJsDocThisType : IJsDocType
    {
        /// <nodoc/>
        IJsDocType Type { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.JsDocRecordMember }, NodeType = NodeType.Marker)]
    public interface IJsDocRecordMember : IPropertySignature
    {
        /// <nodoc/>
        new IdentifierOrLiteralExpression Name { get; }

        // new Optional<IJsDocType> Type { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.JsDocComment }, NodeType = NodeType.Marker)]
    public interface IJsDocComment : INode
    {
        /// <nodoc/>
        NodeArray<IJsDocTag> Tags { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.JsDocTag }, NodeType = NodeType.Marker)]
    public interface IJsDocTag : INode
    {
        /// <nodoc/>
        INode AtToken { get; set; }

        /// <nodoc/>
        IIdentifier TagName { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.JsDocTemplateTag }, NodeType = NodeType.Marker)]
    public interface IJsDocTemplateTag : IJsDocTag
    {
        /// <nodoc/>
        NodeArray<ITypeParameterDeclaration> TypeParameters { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.JsDocReturnTag }, NodeType = NodeType.Marker)]
    public interface IJsDocReturnTag : IJsDocTag
    {
        /// <nodoc/>
        IJsDocTypeExpression TypeExpression { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.JsDocTypeTag }, NodeType = NodeType.Marker)]
    public interface IJsDocTypeTag : IJsDocTag
    {
        /// <nodoc/>
        IJsDocTypeExpression TypeExpression { get; set; }
    }

    /// <nodoc/>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.JsDocParameterTag }, NodeType = NodeType.Marker)]
    public interface IJsDocParameterTag : IJsDocTag
    {
        /// <nodoc/>
        Optional<IIdentifier> PreParameterName { get; set; }

        /// <nodoc/>
        Optional<IJsDocTypeExpression> TypeExpression { get; set; }

        /// <nodoc/>
        Optional<IIdentifier> PostParameterName { get; set; }

        /// <nodoc/>
        bool IsBracketed { get; set; }
    }

    /// <nodoc/>
    public interface IStatementsContainer : INode
    {
        /// <nodoc/>
        NodeArray<IStatement> Statements { get; set; }
    }

    /// <summary>
    /// Source file undergo few transformations, like parsing, binding, module resolution and type checking.
    /// This enum represents a current state of a source file.
    /// </summary>
    public enum SourceFileState
    {
        /// <nodoc/>
        Parsed,

        /// <nodoc/>
        Bound,

        /// <nodoc/>
        Resolved,

        /// <nodoc/>
        TypeChecked,
    }

    /// <summary>
    /// Source files are declarations when they are external modules.
    /// </summary>
    [NodeInfo(SyntaxKinds = new[] { SyntaxKind.SourceFile }, NodeType = NodeType.Leaf)]
    public interface ISourceFile : ISourceFileBindingState, IDeclaration, IEquatable<ISourceFile>, IStatementsContainer
    {
        /// <summary>
        /// State of the current file.
        /// </summary>
        SourceFileState State { get; set; }

        /// <nodoc/>
        INode EndOfFileToken { get; set; }

        /// <nodoc/>
        string FileName { get; }

        /// <nodoc/>
        /* internal */
        Path Path { get; }

        /// <nodoc/>
        TextSource Text { get; }

        /// <nodoc/>
        IAmdDependency[] AmdDependencies { get; set; }

        /// <nodoc/>
        string ModuleName { get; set; }

        /// <nodoc/>
        IFileReference[] ReferencedFiles { get; set; }

        /// <nodoc/>
        LanguageVariant LanguageVariant { get; set; }

        /// <nodoc/>
        bool IsDeclarationFile { get; set; }

        /// <summary>
        /// this map is used by transpiler to supply alternative names for dependencies (i.e., in case of bundling)
        /// </summary>
        /* @internal */
        Optional<Map<string>> RenamedDependencies { get; set; }

        /// <summary>
        /// lib.d.ts should have a reference comment like
        /// <code>
        ///  /// <reference no-default-lib="true"/>
        /// </code>
        /// If any other file has this comment, it signals not to include lib.d.ts
        /// because this containing file is intended to act as a default library.
        /// </summary>
        bool HasNoDefaultLib { get; set; }

        /// <nodoc/>
        ScriptTarget LanguageVersion { get; set; }

        /// <summary>
        /// The first node that causes this file to be an external module
        /// </summary>
        /* @internal */
        INode ExternalModuleIndicator { get; set; }

        /// <summary>
        /// The first node that causes this file to be a CommonJS module
        /// </summary>
        /* @internal */
        INode CommonJsModuleIndicator { get; set; }

        /// <nodoc/>
        /* @internal */
        int NodeCount { get; set; }

        /// <nodoc/>
        /* @internal */
        int IdentifierCount { get; set; }

        /// <nodoc/>
        /* @internal */
        int SymbolCount { get; set; }

        /// <summary>
        /// File level diagnostics reported by the parser (includes diagnostics about /// references
        /// as well as code diagnostics).
        /// </summary>
        /* @internal */
        [JetBrains.Annotations.NotNull]
        IReadOnlyList<Diagnostic> ParseDiagnostics { get; }

        /// <summary>
        /// File level diagnostics reported by the binder.
        /// </summary>
        /* @internal */
        [JetBrains.Annotations.NotNull]
        List<Diagnostic> BindDiagnostics { get; }

        /// <nodoc/>
        IReadOnlyList<Diagnostic> ModuleResolutionDiagnostics { get; }

        /// <summary>
        /// Stores a line map for the file.
        /// This field should never be used directly to obtain line map, use getLineMap function instead.
        /// </summary>
        [JetBrains.Annotations.NotNull]
        LineMap LineMap { get; }

        /// <nodoc/>
        /* @internal */
        Optional<Map<string>> ClassifiableNames { get; set; }

        /// <summary>
        /// Stores a mapping 'external module reference text' -> 'resolved file name' | undefined
        /// It is used to resolve module names in the checker.
        /// Content of this fiels should never be used directly - use getResolvedModuleFileName/setResolvedModuleFileName functions instead
        /// </summary>
        /* @internal */
        Map<IResolvedModule> ResolvedModules { get; }

        /// <nodoc/>
        /* @internal */
        ILiteralExpression[] Imports { get; set; }

        /// <summary>
        /// This field is DScript specific. Only literal-like specifiers coming from import clauses, 'importFrom' or 'export' are collected here during parsing
        /// </summary>
        List<ILiteralExpression> LiteralLikeSpecifiers { get; }

        /// <summary>
        /// This field is DScript specific. Flags if the source file declares a DScript qualifier value at the root
        /// </summary>
        bool DeclaresRootQualifier { get; set; }

        /// <summary>
        /// This field is DScript specific. Flags if the source file already has an injected top-level withQualifier
        /// </summary>
        bool DeclaresInjectedTopLevelWithQualifier { get; set; }

        /// <summary>
        /// DScript-specific. Displays a source file with DScript V2 tweaks
        /// </summary>
        string ToDisplayStringV2();

        /// <summary>
        /// DScript-specific. Whether backslashes were allowed in path interpolation when constructing this ISourceFile
        /// </summary>
        /// <remarks>
        /// Useful for understanding how the file was parsed when the parser that was used is no longer around. From all
        /// the DScript-specific parsing options, this one actually affects subsequent parsings
        /// </remarks>
        bool BackslashesAllowedInPathInterpolation { get; set; }

        /// <summary>
        /// Optional map that is populated when parser is created with preserving trivia.
        /// </summary>
        [JetBrains.Annotations.NotNull]
        IReadOnlyDictionary<INode, Trivia> PerNodeTrivia { get; }

        /// <summary>
        /// Records triva for a given node. Should only be called when preserving trivia.
        /// </summary>
        void RecordTrivia(INode node, Trivia trivia);

        /// <summary>
        /// Moves trivia from one node to another
        /// </summary>
        /// <remarks>
        /// This is used when nodes are created too early by the parser and are not the proper outer nodes.
        /// </remarks>
        void MoveTriva(INode from, INode to);

        /// <summary>
        /// DScript-specific. Whether this source file is a public facade representation of a regular source file
        /// </summary>
        bool IsPublicFacade { get; }

        /// <summary>
        /// DScript-specific. Sets serialized AST blob associated with the current file.
        /// </summary>
        /// <remarks>
        /// If the source file has just a public surface of the file, then a serialized version of an ast should be associated with the file as well.
        /// </remarks>
        void SetSerializedAst(byte[] content, int contentLength);

        /// <summary>
        /// Gets a serialized ast associated with the current file.
        /// </summary>
        (byte[] content, int contentLength) SerializedAst { get; }

        /// <summary>
        /// Computes a binding symbols and keeps it in the file.
        /// </summary>
        void ComputeBindingFingerprint([JetBrains.Annotations.NotNull]BuildXL.Utilities.SymbolTable symbolTable);
    }
}
