// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using LanguageServer;
using BuildXL.Utilities.Collections;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using DScriptUtilities = TypeScript.Net.DScript.Utilities;
using TypeScript.Net.Types;
using CancellationToken = System.Threading.CancellationToken;
using LanguageServerProtocolRange = Microsoft.VisualStudio.LanguageServer.Protocol.Range;

namespace BuildXL.Ide.LanguageServer.Providers
{
    /// <summary>
    /// Provider that allows for rename functionality.
    /// </summary>
    public sealed class RenameProvider : IdeProviderBase
    {
        private static readonly Result<WorkspaceEdit, ResponseError> s_emptyResult = Result<WorkspaceEdit, ResponseError>.Success(new WorkspaceEdit
        {
            Changes = new Dictionary<string, TextEdit[]>(),
        });

        private readonly FindReferencesProvider m_findReferencesProvider;

        private const int ReferenceThreshold = 1000;
        /// <nodoc />
        public RenameProvider(ProviderContext providerContext, FindReferencesProvider findReferencesProvider)
            : base(providerContext)
        {
            m_findReferencesProvider = findReferencesProvider;
        }

        /// <nodoc />
        public Result<WorkspaceEdit, ResponseError> GetWorkspaceEdits(RenameParams renameParameters, CancellationToken token)
        {
            if (!TryFindNode(renameParameters.ToTextDocumentPosition(), out var node))
            {
                return s_emptyResult;
            }

            // The logic for IsNodeEligibleForRenameand the checking for the import statement
            // was ported directly from the typescript version. Whe the import statement
            // isn't inside the IsNodeEligibleForRename function isn't clear. Perhaps
            // it is used elsewhere in TypeScript.

            // Do a pre-check to see if this node is even remotely eiligible for rename
            // This is purely based on syntax kind.
            if (!IsNodeEligibleForRename(node))
            {
                return s_emptyResult;
            }

            // Make sure we aren't trying to rename attempts to rename
            // the "default" keyword from the import statement.
            // TODO: This was ported from the type-script repositiory. I'm not sure I completely understand this.
            var symbol = TypeChecker.GetSymbolAtLocation(node);
            if (symbol != null &&
                symbol.Declarations?.Count > 0 &&
                node.Kind == SyntaxKind.Identifier &&
                node.Cast<IIdentifier>().OriginalKeywordKind == SyntaxKind.DefaultKeyword &&
                (symbol.Parent.Flags & SymbolFlags.Module) != SymbolFlags.Namespace)
            {
                return s_emptyResult;
            }

            // Call the find all references provider and get all the locations.
            var findReferencesResult = m_findReferencesProvider.GetReferencesAtPosition(renameParameters.ToTextDocumentPosition(), token);

            if (!findReferencesResult.IsSuccess)
            {
                return Result<WorkspaceEdit, ResponseError>.Error(findReferencesResult.ErrorValue);
            }

            // Next, we need to create a list per document URI (so a dictionary of Document URI to Ranges)
            // to create the workspace edits.
            var rangesByUri = new MultiValueDictionary<string, LanguageServerProtocolRange>();

            foreach (var location in findReferencesResult.SuccessValue)
            {
               rangesByUri.Add(location.Uri, location.Range);
            }

            // If the change is referenced in a lot of places that would cause performance issue 
            // as all the files being touched would be opened by the IDE
            int fileReferenceCount = rangesByUri.Count;
            if (fileReferenceCount > ReferenceThreshold)
            {
                string message = string.Format(BuildXL.Ide.LanguageServer.Strings.RenameWarningMessage, fileReferenceCount);

                var actions = new MessageActionItem[] {
                    new MessageActionItem(){ Title = BuildXL.Ide.LanguageServer.Strings.ContinueActionTitle },
                    new MessageActionItem(){ Title = BuildXL.Ide.LanguageServer.Strings.CancelActionTitle }
                };

                var response = RpcExtensions.ShowMessageRequestAsync(this.JsonRpc, MessageType.Warning, message, actions).GetAwaiter().GetResult();

                if (response == null || response.Title.Equals(BuildXL.Ide.LanguageServer.Strings.CancelActionTitle, System.StringComparison.OrdinalIgnoreCase))
                {
                    return Result<WorkspaceEdit, ResponseError>.Success(null);
                }
            }

            // Now, create the result which is a URI to an Array of text edits.
            var changes = new Dictionary<string, TextEdit[]>();
            foreach (var uriToRangePair in rangesByUri)
            {
                var edits = new List<TextEdit>();
                foreach (var locationRange in uriToRangePair.Value)
                {
                    edits.Add(new TextEdit
                    {
                        NewText = renameParameters.NewName,
                        Range = locationRange,
                    });
                }

                changes[uriToRangePair.Key] = edits.ToArray();
            }

            var result = new WorkspaceEdit
            {
                Changes = changes,
            };

            return Result<WorkspaceEdit, ResponseError>.Success(result);
        }

        private static bool IsNodeEligibleForRename(INode node)
        {
            switch (node.Kind)
            {
                case SyntaxKind.Identifier:
                case SyntaxKind.StringLiteral:
                case SyntaxKind.ThisKeyword:
                    return true;
                case SyntaxKind.NumericLiteral:
                    // TODO: Our version of IsLiteralNameOfPropertyDeclarationOrIndexAccess does not suppoert computed property or string literal type
                    // TODO: Whereas the typescript version does.
                    return DScriptUtilities.IsLiteralNameOfPropertyDeclarationOrIndexAccess(node);
                default:
                    return false;
            }
        }
    }

    internal static class RenameParamsExtensions
    {
        public static TextDocumentPositionParams ToTextDocumentPosition(this RenameParams renameParameters)
        {
            return new TextDocumentPositionParams
                   {
                       TextDocument = renameParameters.TextDocument,
                       Position = renameParameters.Position,
                   };
        }
    }
}
