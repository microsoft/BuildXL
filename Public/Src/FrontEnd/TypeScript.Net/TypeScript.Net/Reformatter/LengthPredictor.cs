// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using TypeScript.Net.Extensions;
using TypeScript.Net.Types;

namespace TypeScript.Net.Reformatter
{
    /// <nodoc />
    public static class LengthPredictor
    {
        /// <summary>
        /// Computes if the given node still fits on a single line with the given remaining space.
        /// </summary>
        /// <remarks>
        /// When negative values are returned it implies it does not fit
        /// </remarks>
        public static int FitsOnOneLine(INode node, int remainingSpace)
        {
            int space = remainingSpace;

            space = TriviaFitOnOneLine(node, space);
            if (space < 0)
            {
                return space;
            }

            switch (node.Kind)
            {
                case SyntaxKind.NumericLiteral:
                    return FitsOnOneLine(node.Cast<ILiteralExpression>(), space);
                case SyntaxKind.StringLiteral:
                    return FitsOnOneLine(node.Cast<IStringLiteral>(), space);
                case SyntaxKind.TaggedTemplateExpression:
                    return FitsOnOneLine(node.Cast<ITaggedTemplateExpression>(), space);
                case SyntaxKind.TemplateExpression:
                    return FitsOnOneLine(node.Cast<ITemplateExpression>(), space);
                case SyntaxKind.ArrayLiteralExpression:
                    return FitsOnOneLine(node.Cast<IArrayLiteralExpression>(), space);
                case SyntaxKind.ObjectLiteralExpression:
                    return FitsOnOneLine(node.Cast<IObjectLiteralExpression>(), space);
                case SyntaxKind.SpreadElementExpression:
                    return FitsOnOneLine(node.Cast<ISpreadElementExpression>(), space);
                case SyntaxKind.Identifier:
                    return FitsOnOneLine(node.Cast<IIdentifier>(), space);
                case SyntaxKind.QualifiedName:
                    return FitsOnOneLine(node.Cast<IQualifiedName>(), space);
                case SyntaxKind.PropertyAccessExpression:
                    return FitsOnOneLine(node.Cast<IPropertyAccessExpression>(), space);
                case SyntaxKind.ParenthesizedExpression:
                    return FitsOnOneLine(node.Cast<IParenthesizedExpression>(), space);
                case SyntaxKind.PropertyAssignment:
                    return FitsOnOneLine(node.Cast<IPropertyAssignment>(), space);
                case SyntaxKind.Parameter:
                    return FitsOnOneLine(node.Cast<IParameterDeclaration>(), space);
                case SyntaxKind.ArrowFunction:
                    return FitsOnOneLine(node.Cast<IArrowFunction>(), space);
                case SyntaxKind.CallExpression:
                    return FitsOnOneLine(node.Cast<ICallExpression>(), space);
                case SyntaxKind.TypeReference:
                    return FitsOnOneLine(node.Cast<ITypeReferenceNode>(), space);
                case SyntaxKind.BinaryExpression:
                    return FitsOnOneLine(node.Cast<IBinaryExpression>(), space);
                case SyntaxKind.PrefixUnaryExpression:
                    return FitsOnOneLine(node.Cast<IPrefixUnaryExpression>(), space);
                case SyntaxKind.PostfixUnaryExpression:
                    return FitsOnOneLine(node.Cast<IPostfixUnaryExpression>(), space);
                case SyntaxKind.ExportSpecifier:
                    return FitsOnOneLine(node.Cast<IExportSpecifier>(), space);
                case SyntaxKind.ArrayType:
                    return FitsOnOneLine(node.Cast<IArrayTypeNode>(), space);
                case SyntaxKind.FunctionType:
                    return FitsOnOneLine(node.Cast<IFunctionOrConstructorTypeNode>(), space);
                case SyntaxKind.PropertySignature:
                    return FitsOnOneLine(node.Cast<IPropertySignature>(), space);
                case SyntaxKind.UnionType:
                case SyntaxKind.IntersectionType:
                    return FitsOnOneLine(node.Cast<IUnionOrIntersectionTypeNode>(), space);
                case SyntaxKind.StringLiteralType:
                    return FitsOnOneLine(node.Cast<IStringLiteralTypeNode>(), space);
                case SyntaxKind.TypeLiteral:
                    return FitsOnOneLine(node.Cast<ITypeLiteralNode>(), space);

                case SyntaxKind.StringKeyword:
                    return space - "string".Length;
                case SyntaxKind.NumberKeyword:
                    return space - "number".Length;
                case SyntaxKind.BooleanKeyword:
                    return space - "boolean".Length;
                case SyntaxKind.VoidKeyword:
                    return space - "void".Length;
                case SyntaxKind.AnyKeyword:
                    return space - "any".Length;
                case SyntaxKind.TrueKeyword:
                    return space - "true".Length;
                case SyntaxKind.FalseKeyword:
                    return space - "false".Length;

                default:
                    // Never fits on one line
                    return -1;
            }
        }

        private static int TriviaFitOnOneLine(INode node, int remainingSpace)
        {
            int space = remainingSpace;

            // If the node has trivia with newlines it will not fit.
            var sourceFile = node.GetSourceFile();
            if (sourceFile == null || !sourceFile.PerNodeTrivia.TryGetValue(node.GetActualNode(), out Trivia trivia))
            {
                // No trivia, no need to fit on one line.
                return space;
            }

            if (trivia.LeadingComments != null)
            {
                foreach (var comment in trivia.LeadingComments)
                {
                    if (comment.IsSingleLine)
                    {
                        // Single line comments in the leading comments mean a newline
                        return -1;
                    }

                    if (comment.Content.IndexOfAny(new[] { '\n', '\r' }) >= 0)
                    {
                        // if multiline comment has a newline, it won't fit on one line
                        return -1;
                    }

                    space -= comment.Content.Length;
                }
            }

            if (trivia.TrailingComments != null)
            {
                bool seenSingleLineComment = false;
                foreach (var comment in trivia.TrailingComments)
                {
                    if (comment.IsSingleLine)
                    {
                        if (seenSingleLineComment)
                        {
                            // Second single line comment at the end, this won't fit on one line.
                            return -1;
                        }

                        seenSingleLineComment = true;
                    }

                    if (comment.Content.IndexOfAny(new[] { '\n', '\r' }) >= 0)
                    {
                        // if multiline comment has a newline, it won't fit on one line
                        return -1;
                    }

                    space -= comment.Content.Length;
                }
            }

            return space;
        }

        /// <nodoc />
        public static int FitsOnOneLine(INodeArray<INode> nodes, int separatorSize, int remainingSpace)
        {
            if (nodes.IsNullOrEmpty())
            {
                return remainingSpace;
            }

            if (nodes.Count > 5)
            {
                // Always print more than 5 nodes on separate lines.
                return -1;
            }

            int space = remainingSpace;
            foreach (var node in nodes.AsStructEnumerable())
            {
                if (space != remainingSpace)
                {
                    // Subtract space for separators on subsequent nodes
                    space -= separatorSize;
                }

                space = FitsOnOneLine(node, space);
                if (space < 0)
                {
                    // if it already doesn't fit, the rest won't fit either
                    return space;
                }
            }

            return space;
        }

        private static int FitsOnOneLine(ILiteralExpression expression, int remainingSpace)
        {
            return remainingSpace - expression.Text.Length;
        }

        private static int FitsOnOneLine(IStringLiteral expression, int remainingSpace)
        {
            if (expression.LiteralKind != LiteralExpressionKind.None)
            {
                remainingSpace -= 2;
            }

            return remainingSpace - expression.Text.Length;
        }

        private static int FitsOnOneLine(IArrayLiteralExpression expression, int remainingSpace)
        {
            var space = remainingSpace - 2;
            space = FitsOnOneLine(expression.Elements, 2, space);
            return space;
        }

        private static int FitsOnOneLine(ITemplateExpression expression, int remainingSpace)
        {
            // This is a recursion on the pretty printer which is not nice, but it is the simplest for now
            return remainingSpace - expression.GetFormattedText().Length;
        }

        private static int FitsOnOneLine(ITaggedTemplateExpression expression, int remainingSpace)
        {
            var space = FitsOnOneLine(expression.Tag, remainingSpace);
            if (space >= 0)
            {
                // This is a recursion on the pretty printer which is not nice, but it is the simplest for now
                space -= expression.GetFormattedText().Length;
            }

            return space;
        }

        private static int FitsOnOneLine(IObjectLiteralExpression expression, int remainingSpace)
        {
            if (expression.Properties == null)
            {
                return remainingSpace - 2;
            }

            if (expression.Properties.Length > 2)
            {
                // Objects of more than 3 members get their own line
                return -1;
            }

            int space = remainingSpace;
            foreach (var objectElement in expression.Properties)
            {
                if (objectElement.Kind != SyntaxKind.PropertyAssignment)
                {
                    // Only properties would fit on one line
                    return -1;
                }

                if (space != remainingSpace)
                {
                    space -= 2; // Separator
                }

                var property = objectElement as IPropertyAssignment;

                if (property == null)
                {
                    // anything but a property will cause wrapping.
                    return -1;
                }

                space -= property.Name.Text.Length;
                space -= 2; // separator
                space = FitsOnOneLine(property.Initializer, space);
            }

            space -= 2; // brackets

            return space;
        }

        private static int FitsOnOneLine(ISpreadElementExpression expression, int remainingSpace)
        {
            return FitsOnOneLine(expression.Expression, remainingSpace - 3);
        }

        private static int FitsOnOneLine(EntityName expression, int remainingSpace)
        {
            if (expression.Kind == SyntaxKind.QualifiedName)
            {
                return FitsOnOneLine(expression.Cast<IQualifiedName>(), remainingSpace);
            }

            return FitsOnOneLine(expression.Cast<IIdentifier>(), remainingSpace);
        }

        private static int FitsOnOneLine(IIdentifier expression, int remainingSpace)
        {
            return remainingSpace - expression.Text.Length;
        }

        private static int FitsOnOneLine(IQualifiedName expression, int remainingSpace)
        {
            var space = remainingSpace;
            space = FitsOnOneLine(expression.Left, space);
            space -= 1; // dot

            if (space > 0)
            {
                space = FitsOnOneLine(expression.Right, space);
            }

            return space;
        }

        private static int FitsOnOneLine(IPropertyAccessExpression expression, int remainingSpace)
        {
            var space = FitsOnOneLine(expression.Expression, remainingSpace);
            return space - expression.Name.Text.Length;
        }

        private static int FitsOnOneLine(IParenthesizedExpression expression, int remainingSpace)
        {
            return FitsOnOneLine(expression.Expression, remainingSpace) - 2;
        }

        private static int FitsOnOneLine(IPropertyAssignment expression, int remainingSpace)
        {
            var space = remainingSpace;
            space -= expression.Name.Text.Length;
            space -= 2;
            return FitsOnOneLine(expression.Initializer, space);
        }

        private static int FitsOnOneLine(IParameterDeclaration expression, int remainingSpace)
        {
            var space = remainingSpace;
            space -= expression.Name.GetText().Length;
            if (space >= 0 && expression.Type != null)
            {
                space -= 2;
                space = FitsOnOneLine(expression.Type, space);
            }

            if (space >= 0 && expression.Initializer != null)
            {
                space -= 2;
                space = FitsOnOneLine(expression.Initializer, space);
            }

            return space;
        }

        private static int FitsOnOneLine(IFunctionLikeDeclaration expression, int remainingSpace)
        {
            var space = remainingSpace;

            if (expression.TypeParameters != null)
            {
                space -= 2;
                space = FitsOnOneLine(expression.TypeParameters, 2, space);
            }

            if (space > 0 && expression.Parameters != null)
            {
                space -= 3; // The colon and its spaces.
                space = FitsOnOneLine(expression.Parameters, 2, space);
            }

            if (space > 0 && expression.Type != null)
            {
                space -= 3; // The colon and its spaces.
                space = FitsOnOneLine(expression.Type, space);
            }

            space -= 4; // The arrow
            if (space > 0 && expression.Body != null)
            {
                space = FitsOnOneLine(expression.Body, space);
            }

            return space;
        }

        private static int FitsOnOneLine(IArrowFunction expression, int remainingSpace)
        {
            return FitsOnOneLine((IFunctionLikeDeclaration)expression, remainingSpace);
        }

        private static int FitsOnOneLine(ICallExpression expression, int remainingSpace)
        {
            var space = remainingSpace;

            space = FitsOnOneLine(expression.Expression, space);

            if (space > 0 && expression.TypeArguments != null)
            {
                space -= 2; // The pointy brackets.
                space = FitsOnOneLine(expression.TypeArguments, 2, space);
            }

            if (space > 0 && expression.Arguments != null)
            {
                space -= 2; // Parenthesis;
                space = FitsOnOneLine(expression.Arguments, 2, space);
            }

            return space;
        }

        private static int FitsOnOneLine(ITypeReferenceNode expression, int remainingSpace)
        {
            var space = remainingSpace;

            space = FitsOnOneLine(expression.TypeName, space);
            if (space > 0 && expression.TypeArguments != null)
            {
                space -= 2; // The pointy brackets.
                space = FitsOnOneLine(expression.TypeArguments, 2, space);
            }

            return space;
        }

        private static int FitsOnOneLine(IBinaryExpression expression, int remainingSpace)
        {
            var space = remainingSpace;

            space = FitsOnOneLine(expression.Left, space);
            space -= 2;

            if (space > 0)
            {
                space = FitsOnOneLine(expression.Right, space);
            }

            return space;
        }

        private static int FitsOnOneLine(IPrefixUnaryExpression expression, int remainingSpace)
        {
            var space = remainingSpace;

            space -= 1; // operator approximation
            space = FitsOnOneLine(expression.Operand, space);

            return space;
        }

        private static int FitsOnOneLine(IPostfixUnaryExpression expression, int remainingSpace)
        {
            var space = remainingSpace;

            space -= 1; // operator approximation
            space = FitsOnOneLine(expression.Operand, space);

            return space;
        }

        private static int FitsOnOneLine(IExportSpecifier expression, int remainingSpace)
        {
            var space = remainingSpace;

            space = FitsOnOneLine(expression.Name, space);

            if (space > 0 && expression.PropertyName != null)
            {
                space -= 4; // as keyword and spaces
                space = FitsOnOneLine(expression.PropertyName, space);
            }

            return space;
        }

        private static int FitsOnOneLine(IArrayTypeNode expression, int remainingSpace)
        {
            var space = remainingSpace;

            space -= 2; // []
            space = FitsOnOneLine(expression.ElementType, space);

            return space;
        }

        private static int FitsOnOneLine(IFunctionOrConstructorTypeNode expression, int remainingSpace)
        {
            return FitsOnOneLine((IFunctionLikeDeclaration)expression, remainingSpace);
        }

        private static int FitsOnOneLine(IPropertySignature expression, int remainingSpace)
        {
            var space = remainingSpace;

            if (expression.Name.Kind == SyntaxKind.Identifier)
            {
                space = FitsOnOneLine(expression.Name.Cast<IIdentifier>(), space);
            }
            else
            {
                return -1;
            }

            if (expression.QuestionToken.HasValue)
            {
                space -= 1; // question mark
            }

            if (space > 0 && expression.Type != null)
            {
                space -= 2; // colon space
                space = FitsOnOneLine(expression.Type, space);
            }

            if (space > 0 && expression.Initializer != null)
            {
                space -= 3; // space equals space
                space = FitsOnOneLine(expression.Initializer, space);
            }

            return space;
        }

        private static int FitsOnOneLine(IUnionOrIntersectionTypeNode expression, int remainingSpace)
        {
            var space = remainingSpace;

            for (int i = 0; i < expression.Types.Count; i++)
            {
                if (i > 0)
                {
                    space -= 3; // space operator space
                }

                space = FitsOnOneLine(expression.Types[i], space);

                if (space < 0)
                {
                    return space;
                }
            }

            return space;
        }

        private static int FitsOnOneLine(IStringLiteralTypeNode expression, int remainingSpace)
        {
            return remainingSpace - 2 - expression.Text.Length;
        }

        private static int FitsOnOneLine(ITypeLiteralNode expression, int remainingSpace)
        {
            var space = remainingSpace - 2; // for the curly brackets

            for (int i = 0; i < expression.Members.Count; i++)
            {
                if (i > 0)
                {
                    space -= 2; // comma space
                }

                space = FitsOnOneLine(expression.Members[i], space);

                if (space < 0)
                {
                    return space;
                }
            }

            return space;
        }
    }
}
