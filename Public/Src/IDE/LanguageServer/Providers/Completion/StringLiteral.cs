// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Ide.LanguageServer.Utilities;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using TypeScript.Net.Types;
using static TypeScript.Net.DScript.Utilities;

namespace BuildXL.Ide.LanguageServer.Completion
{
    internal static class StringLiteral
    {
        /// <summary>
        /// Handles the case of:
        /// const myVar : StringLiterlType = "{completion happens here}";
        /// </summary>
        /// <remarks>
        /// The type is based on the expected type of the initializer. So we ask the type checker for
        /// that type. We then expand all union types and then return the unique set
        /// of possibilities.
        /// </remarks>
        internal static IEnumerable<CompletionItem> CreateCompletionItemsFromVariableDeclaration(CompletionState completionState, INode completionNode)
        {
            var variableDeclaration = completionNode.Cast<IVariableDeclaration>();
            var type = completionState.TypeChecker.GetContextualType(variableDeclaration.Initializer);
            if (type != null)
            {
                var results = AddStringLiteralSymbolsFromType(type, completionState.TypeChecker);
                return CreateCompletionItemsFromStrings(results);
            }

            return null;
        }

        /// <summary>
        /// Handles the case of:
        /// const myVar : someInterfaceType  = "{ stringLiteralProperty: {completion happens here} }";
        /// </summary>
        /// <remarks>
        /// The type is based on the expected type of the initializer. So we ask the type checker for
        /// that type. We then expand all union types and then return the unique set
        /// of possibilities.
        /// </remarks>
        internal static IEnumerable<CompletionItem> CreateCompletionItemsFromPropertyAssignment(CompletionState completionState, INode completionNode)
        {
            var propertyAssignment = completionNode.Cast<IPropertyAssignment>();
            var type = completionState.TypeChecker.GetContextualType(propertyAssignment.Initializer);
            if (type != null)
            {
                var results = AddStringLiteralSymbolsFromType(type, completionState.TypeChecker);
                return CreateCompletionItemsFromStrings(results);
            }

            return null;
        }

        /// <summary>
        /// Handles the case of:
        /// function myFunction(args: "A" | "B" | "C");
        /// const myVar = myFunc({completion happens here});
        /// </summary>
        /// <remarks>
        /// The type is based on the expected type of the initializer. So we ask the type checker for
        /// that type. We then expand all union types and then return the unique set
        /// of possibilities.
        /// </remarks>
        internal static IEnumerable<CompletionItem> CreateCompletionFromCallExperssion(CompletionState completionState, INode completionNode)
        {
            var callExpression = completionNode.Cast<ICallExpression>();

            // The user can type a random function that looks like a call expresion:
            // const myVar == fooBar();
            // In which case we have nothing to resolve
            var callSymbol = completionState.TypeChecker.GetSymbolAtLocation(callExpression.Expression);
            if (callSymbol == null)
            {
                return null;
            }

            var typeOfExpression = completionState.TypeChecker.GetTypeAtLocation(callExpression.Expression);
            if (typeOfExpression == null)
            {
                return null;
            }

            var signature = completionState.TypeChecker.GetSignaturesOfType(typeOfExpression, SignatureKind.Call).FirstOrDefault();
            var parameterIndex = DScriptFunctionSignature.GetActiveParameterIndex(callExpression, completionState.StartingNode);

            var parameterCount = signature?.Parameters?.Count ?? 0;

            if (parameterIndex >= 0 && parameterIndex < parameterCount)
            {
                var parameter = signature.Parameters[parameterIndex];
                var type = completionState.TypeChecker.GetTypeAtLocation(parameter.GetFirstDeclarationOrDefault());
                if (type != null)
                {
                    var results = AddStringLiteralSymbolsFromType(type, completionState.TypeChecker);
                    return CreateCompletionItemsFromStrings(results);
                }
            }

            return null;
        }

        /// <summary>
        /// Handles the switch(stringLiteralType) { case "{completion happens here}" }
        /// </summary>
        internal static IEnumerable<CompletionItem> CreateCompletionFromCaseClause(CompletionState completionState, INode completionNode)
        {
            var caseClause = completionNode.Cast<ICaseClause>();

            // Walk up the parent chain until we find the switch statement as the expression of the
            // switch statement is the type we want to complete.
            // The type-script version of this does a "node.parent.parent.parent.parent" type of expression
            // which seems fragile if the AST parsing were changed in any way. So we will just walk the chain.
            var switchStatement = GetSwitchStatement(caseClause);
            var type = completionState.TypeChecker.GetTypeAtLocation(switchStatement.Cast<ISwitchStatement>().Expression);
            if (type != null)
            {
                var results = AddStringLiteralSymbolsFromType(type, completionState.TypeChecker);
                return CreateCompletionItemsFromStrings(results);
            }

            return null;
        }

        /// <summary>
        /// Determines whether the completion provider should call the <see cref="CreateCompletionFromBinaryExpression(CompletionState, INode)"/> handler.
        /// </summary>
        internal static bool ShouldRunCreateCompletionFromBinaryExpression(CompletionState copmletionState, INode completionNode)
        {
            var binaryExpression = completionNode.Cast<IBinaryExpression>();
            return IsEqualityOperator(binaryExpression.OperatorToken);
        }

        /// <summary>
        /// Handles the if (stringLiteralType === "completion happens here").
        /// </summary>
        /// <remarks>
        /// Actually this handles any types of binary experssions. For example !== is handled as well.
        /// </remarks>
        internal static IEnumerable<CompletionItem> CreateCompletionFromBinaryExpression(CompletionState completionState, INode completionNode)
        {
            var binaryExpression = completionNode.Cast<IBinaryExpression>();

            // We want the side of the expression that is opposite the string literal
            // So, check both sides of the expression and pick the one that isn't the string literal node (which is our starting node in this case)
            Contract.Assert(ReferenceEquals(binaryExpression.Left, completionState.StartingNode) || ReferenceEquals(binaryExpression.Right, completionState.StartingNode));
            var notTheStringLiteralNode = ReferenceEquals(binaryExpression.Left, completionState.StartingNode) ? binaryExpression.Right : binaryExpression.Left;
            if (notTheStringLiteralNode != null)
            {
                var type = completionState.TypeChecker.GetTypeAtLocation(notTheStringLiteralNode);
                if (type != null)
                {
                    var results = AddStringLiteralSymbolsFromType(type, completionState.TypeChecker);
                    return CreateCompletionItemsFromStrings(results);
                }
            }

            return null;
        }

        /// <summary>
        /// Handles the case of (x === y) ? "" : "" where the completion is a string literal.
        /// </summary>
        internal static IEnumerable<CompletionItem> CreateCompletionItemsFromConditionalExpression(CompletionState completionState, INode completionNode)
        {
            var conditionalExpression = completionNode.Cast<IConditionalExpression>();
            var type = completionState.TypeChecker.GetContextualType(conditionalExpression);
            if (type != null)
            {
                var results = AddStringLiteralSymbolsFromType(type, completionState.TypeChecker);
                return CreateCompletionItemsFromStrings(results);
            }

            return null;
        }

        /// <summary>
        /// Handles the case of (x === y) ? "" : "" where the completion is a string literal.
        /// </summary>
        internal static IEnumerable<CompletionItem> CreateCompletionItemsFromSwitchExpression(CompletionState completionState, INode completionNode)
        {
            var switchExpression = completionNode.Cast<ISwitchExpression>();
            var type = completionState.TypeChecker.GetContextualType(switchExpression);

            // $TODO:

            return null;
        }

        /// <summary>
        /// Handles the case of return "" where the completion is a string literal.
        /// </summary>
        internal static IEnumerable<CompletionItem> CreateCompletionItemsFromReturnStatement(CompletionState completionState, INode completionNode)
        {
            var returnStatement = completionNode.Cast<IReturnStatement>();
            if (returnStatement.Expression != null)
            {
                var type = completionState.TypeChecker.GetContextualType(returnStatement.Expression);
                if (type != null)
                {
                    var results = AddStringLiteralSymbolsFromType(type, completionState.TypeChecker);
                    return CreateCompletionItemsFromStrings(results);
                }
            }

            return null;
        }

        /// <summary>
        /// Creates a unique set of strings representing the string literal types present in the given type (and all unions if present)
        /// </summary>
        private static IEnumerable<string> AddStringLiteralSymbolsFromType(IType type, ITypeChecker typeChecker)
        {
            var completionStrings = GetAllTypesIfUnion(type, typeChecker).
                Where(t => ((t.Flags & TypeFlags.StringLiteral) != TypeFlags.None)).
                Select(t => t.Cast<IStringLiteralType>().Text).Distinct();

            return completionStrings;
        }

        private static IEnumerable<CompletionItem> CreateCompletionItemsFromStrings(IEnumerable<string> strings)
        {
            return strings.Select(stringLiteral => new CompletionItem
            {
                Label = stringLiteral,
                Kind = CompletionItemKind.Variable
            });
        }
    }
}
