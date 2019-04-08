// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.FrontEnd.Script.Constants;
using JetBrains.Annotations;
using BuildXL.FrontEnd.Script.Expressions;
using TypeScript.Net.Types;
using TypeScript.Net.Utilities;
using static BuildXL.Utilities.FormattableStringEx;
using Expression = BuildXL.FrontEnd.Script.Expressions.Expression;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge
{
    /// <summary>
    /// Set of extension methods for <see cref="INode"/> required for ast conversion.
    /// </summary>
    public static class NodeExtensions
    {
        internal static UniversalLocation Location(this INode node, ISourceFile sourceFile, AbsolutePath path, PathTable pathTable)
        {
            var lineInfo = node.GetLineInfo(sourceFile);
            return new UniversalLocation(node, lineInfo, path, pathTable);
        }

        internal static LineInfo LineInfo(this INode node, ISourceFile sourceFile)
        {
            return node.GetLineInfo(sourceFile);
        }

        /// <nodoc />
        public static Location LocationForLogging(this INode node, ISourceFile sourceFile)
        {
            var lineInfo = LineInfo(node, sourceFile);
            var result = new Location()
            {
                File = sourceFile.FileName,
                Line = lineInfo.Line,
                Position = lineInfo.Position,
            };

            return result;
        }

        /// <summary>
        /// Returns true if specified <paramref name="identifier"/> starts with Captial.
        /// </summary>
        /// <remarks>
        /// Currently the difference in casing is critical for name resolution. But this solution is going to change soon.
        /// </remarks>
        internal static bool StartsWithUpperCase(this IIdentifier identifier)
        {
            Contract.Requires(identifier != null);
            Contract.Requires(!string.IsNullOrEmpty(identifier.Text));

            return char.IsUpper(identifier.Text[0]);
        }

        internal static bool StartsWithUpperCase(this string identifier)
        {
            Contract.Requires(!string.IsNullOrEmpty(identifier));

            return char.IsUpper(identifier[0]);
        }

        internal static bool IsInlineImport(this ICallExpression callExpression)
        {
            Contract.Requires(callExpression != null);

            var identifier = callExpression.Expression.As<IIdentifier>();
            var functionName = identifier?.Text;

            // Currently, BuildXL script support two types of inline require. One of them would be obsoleted!
            return functionName == Names.InlineImportFunction
                || functionName == Names.InlineImportFileFunction;
        }

        internal static IIdentifier GetConfigurationKeywordFromConfigurationDeclaration(this IStatement statement)
        {
            Contract.Requires(statement != null);
            Contract.Requires(statement.IsConfigurationDeclaration());

            var expressionStatement = (IExpressionStatement)statement;
            var callExpression = expressionStatement.Expression.Cast<ICallExpression>();
            var identifier = (IIdentifier)callExpression.Expression;

            return identifier;
        }

        /// <nodoc />
        internal static ICallExpression AsCallExpression(this IStatement statement)
        {
            Contract.Requires(statement != null);

            var expressionStatement = (IExpressionStatement)statement;
            return expressionStatement.Expression.Cast<ICallExpression>();
        }

        internal static IExpression GetExpression(this IExpressionStatement statement)
        {
            Contract.Requires(statement != null);
            return statement.Expression.As<IExpression>();
        }

        internal static bool IsPackageConfigurationDeclaration(this IStatement statement)
        {
            return TryGetPackageConfigurationDeclaration(statement, out bool dummy);
        }

        internal static bool IsLegacyPackageConfigurationDeclaration(this IStatement statement)
        {
            var result = TryGetPackageConfigurationDeclaration(statement, out bool isLegacy);
            Contract.Assert(result);

            return isLegacy;
        }

        /// <summary>
        /// Checks if this expression is a <see cref="ICallExpression"/> and if so returns
        /// the name of the called function; otherwise returns <code>null</code>.
        /// </summary>
        [CanBeNull]
        internal static string TryGetFunctionNameInCallExpression(this IStatement statement)
        {
            Contract.Requires(statement != null);

            // package statement is a special kind of statement declaration
            if (statement.Kind != TypeScript.Net.Types.SyntaxKind.ExpressionStatement)
            {
                return null;
            }

            var expressionStatement = (IExpressionStatement)statement;
            if (expressionStatement.Expression.Kind != TypeScript.Net.Types.SyntaxKind.CallExpression)
            {
                return null;
            }

            var callExpression = (ICallExpression)expressionStatement.GetExpression();

            var identifier = callExpression.Expression as IIdentifier;
            if (identifier == null)
            {
                return null;
            }

            return identifier.Text;
        }

        internal static bool TryGetPackageConfigurationDeclaration(this IStatement statement, out bool isLegacyKeyword)
        {
            isLegacyKeyword = false;

            var identifierText = statement.TryGetFunctionNameInCallExpression();
            if (identifierText == null)
            {
                return false;
            }

            var legacyPackageKeywordStr = Names.LegacyModuleConfigurationFunctionCall;
            isLegacyKeyword = identifierText == legacyPackageKeywordStr;
            return identifierText == legacyPackageKeywordStr || identifierText == Names.ModuleConfigurationFunctionCall;
        }

        internal static bool IsSpreadOperator(this Expression expression)
        {
            Contract.Requires(expression != null);

            var unaryExpression = expression as UnaryExpression;
            return unaryExpression?.OperatorKind == UnaryOperator.Spread;
        }

        internal static int AsNumber(this ILiteralExpression literalExpression, bool isNegative)
        {
            Contract.Requires(literalExpression != null);

            string text = isNegative ? I($"-{literalExpression.Text}") : literalExpression.Text;

            var result = LiteralConverter.TryConvertNumber(text);

            if (!result.IsValid)
            {
                Contract.Assert(false, I($"Conversion from literal expression to number should be successful. Text: {literalExpression.Text}"));
            }

            return result.Value;
        }
    }
}
