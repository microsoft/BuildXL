// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.FrontEnd.Script.Constants;
using TypeScript.Net.Extensions;
using TypeScript.Net.Types;

namespace TypeScript.Net.DScript
{
    /// <summary>
    /// Set of factory methods for constructing DScript language constructs.
    /// </summary>
    public static class SyntaxFactory
    {
        /// <summary>
        /// Creates a variable statement with path-like interpolation literal.
        /// </summary>
        public static IStatement PathLikeConstVariableDeclaration(string variableName, InterpolationKind kind, string literal, Visibility visibility = Visibility.None)
        {
            Contract.Requires(!string.IsNullOrEmpty(variableName));
            Contract.Requires(kind != InterpolationKind.Unknown);
            Contract.Requires(!string.IsNullOrEmpty(literal));

            return new VariableDeclarationBuilder()
                .Const()
                .Visibility(visibility)
                .Name(variableName)
                .Initializer(PathLikeLiteral(kind, literal))
                .Build();
        }

        /// <summary>
        /// Creates an import declaration statement.
        /// </summary>
        public static IStatement ImportDeclaration(string alias, string moduleName)
            => new ImportDeclaration(alias, moduleName);

        /// <summary>
        /// Creates an import declaration statement.
        /// </summary>
        public static IStatement ImportDeclaration(string[] names, string moduleName)
            => new ImportDeclaration(names, moduleName);

        /// <summary>
        /// Creates a path-like expression with a given kind and text.
        /// </summary>
        public static ITaggedTemplateExpression PathLikeLiteral(InterpolationKind kind, string literalText)
        {
            return new TaggedTemplateExpression(kind.GetIdentifierName(), literalText);
        }
        
        /// <summary>
        /// Creates a path-like expression with a given kind and text.
        /// </summary>
        public static ITaggedTemplateExpression PathLikeLiteral(InterpolationKind kind, IExpression firstExpression, string literalText)
        {
            return new TaggedTemplateExpression(kind.GetIdentifierName(), firstExpression, literalText);
        }

        /// <summary>
        /// Creates identifier with a given name.
        /// </summary>
        public static IIdentifier Identifier(string name)
        {
            return new Identifier(name);
        }

        /// <summary>
        /// Creates a property access expression.
        /// </summary>
        public static IPropertyAccessExpression PropertyAccess(string first, string second)
        {
            Contract.Requires(!string.IsNullOrEmpty(first));
            Contract.Requires(!string.IsNullOrEmpty(second));

            return new PropertyAccessExpression(first, second);
        }

        /// <summary>
        /// Creates a property access expression.
        /// </summary>
        public static IPropertyAccessExpression PropertyAccess(string first, string second, params string[] parts)
        {
            Contract.Requires(!string.IsNullOrEmpty(first));
            Contract.Requires(!string.IsNullOrEmpty(second));

            return new PropertyAccessExpression(first, second, parts);
        }

        /// <summary>
        /// Creates a property access expression.
        /// </summary>
        public static IPropertyAccessExpression PropertyAccess(ILeftHandSideExpression first, string second)
        {
            Contract.Requires(first != null);
            Contract.Requires(!string.IsNullOrEmpty(second));

            return new PropertyAccessExpression(first, second);
        }

        /// <summary>
        /// Creates an array literal with a given set of expressions.
        /// </summary>
        public static IArrayLiteralExpression Array(params IExpression[] elements)
        {
            return new ArrayLiteralExpression(elements);
        }

        /// <summary>
        /// Creates an array literal with a given set of expressions.
        /// </summary>
        public static IArrayLiteralExpression Array(List<IExpression> elements)
        {
            return new ArrayLiteralExpression(elements);
        }

        /// <summary>
        /// Creates a type assertion like 'x : YourType'.
        /// </summary>
        public static IExpression TypeAssertion(ITypeNode type, IUnaryExpression expression)
        {
            var result = new TypeAssertion();
            result.Type = type;
            result.Expression = expression;
            return result;
        }

        /// <summary>
        /// Creates <code>importFomr(moduleName)</code> expression.
        /// </summary>
        public static ICallExpression ImportFrom(string moduleName)
        {
            return new CallExpression(
                new Identifier(Names.InlineImportFunction),
                new LiteralExpression(moduleName));
        }

        /// <summary>
        /// Creates a union type with a <paramref name="propertyName"/> and a set of cases represented by the <paramref name="literalTypes"/>.
        /// </summary>
        public static ITypeLiteralNode UnionType(string propertyName, string[] literalTypes)
        {
            Contract.Requires(!string.IsNullOrEmpty(propertyName));
            Contract.Requires(!literalTypes.IsNullOrEmpty());

            var literals = literalTypes.Select(lt => new StringLiteralTypeNode(lt)).ToArray();
            return new TypeLiteralNode(
                new PropertySignature(
                    propertyName,
                    new UnionOrIntersectionTypeNode()
                    {
                        Types = new NodeArray<ITypeNode>(literals),
                        Kind = SyntaxKind.UnionType,
                    }));
        }

        /// <summary>
        /// Creates a qualifier declaration with a given <paramref name="qualifierType"/>.
        /// </summary>
        public static IVariableStatement Qualifier(ITypeLiteralNode qualifierType)
        {
            Contract.Requires(qualifierType != null);
            return new VariableStatement(
                "qualifier",
                null,
                qualifierType,
                NodeFlags.Export | NodeFlags.Const | NodeFlags.Ambient);
        }
    }
}
