// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;
using TypeScript.Net.Parsing;
using TypeScript.Net.Reformatter;
using TypeScript.Net.Types;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Ide.LanguageServer.Utilities
{
    internal sealed class DScriptFunctionSignature
    {
        public string FunctionName { get; private set; }

        public IEnumerable<string> FormattedParameterNames { get; private set; }

        public string FormattedFullFunctionSignature { get; private set; }

        private const int DefaultArgIndex = 0;

        private DScriptFunctionSignature() { }

        /// <summary>
        /// Gets a function signature from the given symbol
        /// </summary>
        /// <param name="symbol">Function's symbol</param>
        /// <returns>The symbol broken down into a friendlier representation</returns>
        public static DScriptFunctionSignature FromSymbol(ISymbol symbol)
        {
            var declaration = symbol.GetFirstDeclarationOrDefault();

            if (declaration == null)
            {
                return null;
            }

            var functionLikeDeclaration = NodeUtilities.IsFunctionLike(declaration);

            if (functionLikeDeclaration != null)
            {
                return GetSymbolFromFunction(symbol.Name, functionLikeDeclaration.Parameters);
            }

            // check to see if this is a property that's really just a function reference - if so treat it like a function
            // instead of a property
            IPropertySignature propertySignature = declaration.As<IPropertySignature>();

            if (propertySignature == null)
            {
                return null;
            }

            IFunctionOrConstructorTypeNode functionTypeNode = propertySignature?.Type.As<IFunctionOrConstructorTypeNode>();

            if (functionTypeNode == null)
            {
                return null;
            }

            return GetSymbolFromFunction(symbol.Name, functionTypeNode.Parameters);
        }

        /// <summary>
        /// Gets a function signature from the given symbol
        /// </summary>
        /// <param name="functionName">The name of the function</param>
        /// <param name="signature">Signature of a function</param>
        /// <returns>The symbol broken down into a friendlier representation</returns>
        public static DScriptFunctionSignature FromSignature(string functionName, ISignature signature)
        {
            var formattedParameters = signature.Parameters
                .Select(symbol => I($"{symbol.GetFirstDeclarationOrDefault()?.GetFormattedText()}"));

            string returnTypeName = null;

            // TODO: Why doesn't this return true?
            // TODO: signature.ResolvedReturnType.Flags.HasFlag(TypeFlags.Intrinsic)?

            // Functions with no return types will have a null resolved return type
            var resolvedReturnType = signature.ResolvedReturnType;
            if (resolvedReturnType != null)
            {
                if (resolvedReturnType is IIntrinsicType intrinsicType)
                {
                    returnTypeName = intrinsicType.IntrinsicName;
                }
                else
                {
                    returnTypeName = resolvedReturnType.Symbol?.Name;
                }
            }

            returnTypeName = string.IsNullOrEmpty(returnTypeName) ? string.Empty : returnTypeName;

            var paramsString = string.Join(", ", formattedParameters);

            return new DScriptFunctionSignature()
            {
                FunctionName = functionName,
                FormattedFullFunctionSignature = I($"{functionName}({paramsString}) : {returnTypeName}"),
                FormattedParameterNames = formattedParameters,
            };
        }

        private static DScriptFunctionSignature GetSymbolFromFunction(string functionName, NodeArray<IParameterDeclaration> parameterDeclarations)
        {
            var elements = parameterDeclarations?.Elements;

            var paramsString = string.Empty;
            var formattedParameters = Enumerable.Empty<string>();

            if (elements != null)
            {
                formattedParameters = parameterDeclarations.Elements.Select(elem => elem.GetFormattedText()).ToArray();
                paramsString = string.Join(", ", formattedParameters);
            }

            return new DScriptFunctionSignature()
            {
                FunctionName = functionName,
                FormattedFullFunctionSignature = I($"{functionName}({paramsString})"),
                FormattedParameterNames = formattedParameters,
            };
        }

        /// <summary>
        /// Finds the parameter that either contains this node or _is_ this node.
        /// </summary>
        /// <param name="callExpression">The containing call expression for this node</param>
        /// <param name="nodeAtPosition">The current node in the editor</param>
        /// <returns>The index of the argument (or 0 if this didn't correspond to any argument)</returns>
        public static int GetActiveParameterIndex(ICallExpression callExpression, INode nodeAtPosition)
        {
            var arguments = callExpression?.Arguments?.Elements.ToArray();

            if (arguments == null)
            {
                return DefaultArgIndex;
            }

            // first check the top level arguments (much cheaper and also very likely)
            for (int i = 0; i < arguments.Length; ++i)
            {
                if (ReferenceEquals(arguments[i], nodeAtPosition))
                {
                    return i;
                }
            }

            // let's try the slightly more expensive check within subfields
            for (int i = 0; i < arguments.Length; ++i)
            {
                var result = NodeWalker.ForEachChildRecursively(arguments[i], node => ReferenceEquals(node, nodeAtPosition) ? node : null);

                if (result != null)
                {
                    return i;
                }
            }

            return DefaultArgIndex;
        }
    }
}
