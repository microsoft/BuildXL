// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.Linq;
using BuildXL.Ide.LanguageServer.Utilities;
using BuildXL.Utilities;
using LanguageServer;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using TypeScript.Net.Parsing;
using TypeScript.Net.Reformatter;
using TypeScript.Net.Types;
using CancellationToken = System.Threading.CancellationToken;

namespace BuildXL.Ide.LanguageServer.Providers
{
    /// <summary>
    /// Provides signature assistance in the IDE
    /// </summary>
    public sealed class SignatureHelpProvider : IdeProviderBase
    {
        /// <nodoc />
        public SignatureHelpProvider(ProviderContext providerContext)
            : base(providerContext)
        {
        }

        /// <summary>
        /// Gets the containing call expression for a given node (which is probably an argument)
        /// </summary>
        /// <param name="nodeAtPosition">The node currently at cursor in the editor</param>
        /// <returns>The call expression</returns>
        private static ICallExpression GetContainingCallExpression(INode nodeAtPosition)
        {
            INode potentialCallExpression = nodeAtPosition;

            while (potentialCallExpression != null)
            {
                if (potentialCallExpression.Kind == SyntaxKind.CallExpression)
                {
                    return potentialCallExpression.Cast<ICallExpression>();
                }

                potentialCallExpression = potentialCallExpression.Parent;
            }

            return null;
        }

        /// <nodoc/>
        public Result<SignatureHelp, ResponseError> SignatureHelp(TextDocumentPositionParams positionParams, CancellationToken token)
        {
            // TODO: support cancellation
            if (!TryFindNode(positionParams, out var nodeAtPosition))
            {
                return Result<SignatureHelp, ResponseError>.Success(new SignatureHelp());
            }

            ICallExpression callExpression = GetContainingCallExpression(nodeAtPosition);

            if (callExpression == null)
            {
                Logger.LanguageServerNonCriticalInternalIssue(LoggingContext, FormattableStringEx.I($"Couldn't find call expression containing {nodeAtPosition.GetFormattedText()}"));
                return Result<SignatureHelp, ResponseError>.Success(new SignatureHelp());
            }

            var callSymbol = TypeChecker.GetSymbolAtLocation(callExpression.Expression);

            // If the user has typed a call expresion to a symbol (function) that doesn't exist (i.e. "foo.bar()")
            // Then just issue a debug writeline and a success instead of crashing.
            // There is going to be a red-line squiggle under it anyway.
            if (callSymbol == null)
            {
                Logger.LanguageServerNonCriticalInternalIssue(LoggingContext, FormattableStringEx.I($"Couldn't find symbol for call expression containing {nodeAtPosition.GetFormattedText()}"));
                return Result<SignatureHelp, ResponseError>.Success(new SignatureHelp());
            }

            var signature = TypeChecker.GetSignaturesOfType(TypeChecker.GetTypeAtLocation(callExpression.Expression), SignatureKind.Call).FirstOrDefault();
            if (signature == null)
            {
                Logger.LanguageServerNonCriticalInternalIssue(LoggingContext, FormattableStringEx.I($"Couldn't find call signature for call expression containing {nodeAtPosition.GetFormattedText()}"));
                return Result<SignatureHelp, ResponseError>.Success(new SignatureHelp());
            }

            var functionDeclaration = DScriptFunctionSignature.FromSignature(callSymbol.Name, signature);

            var parameterInformations = functionDeclaration.FormattedParameterNames.Select(formattedParameterName => new ParameterInformation()
            {
                Label = formattedParameterName,
            });

            int activeParameterIndex = DScriptFunctionSignature.GetActiveParameterIndex(callExpression, nodeAtPosition);

            var signatureHelp = new SignatureHelp()
            {
                Signatures = new SignatureInformation[]
                {
                    new SignatureInformation()
                    {
                        Label = functionDeclaration.FormattedFullFunctionSignature,
                        Parameters = parameterInformations.ToArray(),
                        Documentation = DocumentationUtilities.GetDocumentationForSymbolAsString(callSymbol),
                    },
                },
                ActiveParameter = activeParameterIndex,
                ActiveSignature = 0,
            };

            return Result<SignatureHelp, ResponseError>.Success(signatureHelp);
        }
    }
}
