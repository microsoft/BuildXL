// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Workspaces;
using BuildXL.Ide.LanguageServer.Server.Utilities;
using LanguageServer;
using BuildXL.Utilities.Configuration;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using TypeScript.Net.Parsing;
using TypeScript.Net.Types;
using CancellationToken = System.Threading.CancellationToken;

namespace BuildXL.Ide.LanguageServer.Providers
{
    /// <summary>
    /// Provider that allows for CodeLens annotations functionality.
    /// </summary>
    public sealed class CodeLensProvider : IdeProviderBase
    {
        private ISemanticModel SemanticModel => Workspace.GetSemanticModel();

        /// <nodoc/>
        internal CodeLensProvider(ProviderContext providerContext)
            : base(providerContext)
        {
        }

        /// <summary>
        /// Called to provide a list of Code Lens annotations that apply to ranges within the specified document from the workspace
        /// </summary>
        public Result<CodeLens[], ResponseError> CodeLens(CodeLensParams @params, CancellationToken token)
        {
            // TODO: support cancellation
            var fileUri = new Uri(@params.TextDocument.Uri);

            var pathToFile = fileUri.ToAbsolutePath(PathTable);

            var codeLensObjs = new List<CodeLens>();

            // If it's not in the workspace (e.g. a configuration file), then we don't provide codelens
            if (Workspace.TryGetSourceFile(pathToFile, out var spec))
            {
                // Need to get module definition for a given source file to check the module kind.
                // If the module is V1 module or configuration file, then the result should be empty
                var module = Workspace.TryGetModuleBySpecFileName(spec.GetAbsolutePath(PathTable));

                if (module == null || module.Definition.ResolutionSemantics == NameResolutionSemantics.ExplicitProjectReferences ||
                    Workspace.IsPreludeOrConfigurationModule(module))
                {
                    // This specific module does not have any qualifiers.
                    return Result<CodeLens[], ResponseError>.Success(codeLensObjs.ToArray());
                }

                codeLensObjs.AddRange(GetQualifierTypeForModulesAndTopLevelDeclarations(spec));
            }

            return Result<CodeLens[], ResponseError>.Success(codeLensObjs.ToArray());
        }

        /// <summary>
        /// Create a list of Code Lens annotations a source file to represent the current qualifier
        /// </summary>
        private IEnumerable<CodeLens> GetQualifierTypeForModulesAndTopLevelDeclarations(ISourceFile file)
        {
            // We want to display CodeLens for at the top of the file, and for namespaces at any depth of the source file
            // Also, note that namespaces can be on the same line (e.g. namespace Microsoft.Sdk {...} is actually namespace Sdk inside of namespace Microsoft)
            // In this case we want to display the qualifier for namespace Sdk, since that's what the user cares about
            // So we will use a line-number key'ed dictionary to make sure that for any line in the file, we only add a single qualifier
            var result = new List<CodeLens>();
            var perLineCodeLenses = new Dictionary<int, CodeLens>();

            if (file.Statements.Count > 0)
            {
                var firstNode = file.Statements[0];
                var firstLine = firstNode.GetStartPosition().ToLineAndColumn().Line;

                if (GetQualifierAsCodeLens(firstNode, out var codeLens))
                {
                    perLineCodeLenses[firstLine] = codeLens;
                }
            }

            NodeWalker.ForEachChildRecursively(file, (node) =>
            {
                if (node.Kind == SyntaxKind.ModuleDeclaration)
                {
                    var lineAndColumn = node.GetStartPosition().ToLineAndColumn();
                    if (GetQualifierAsCodeLens(node, out var codeLens))
                    {
                        perLineCodeLenses[lineAndColumn.Line] = codeLens;
                    }
                }

                return (object)null;
            });

            result.AddRange(perLineCodeLenses.Values.Where(v => v != null));

            return result;
        }

        private bool GetQualifierAsCodeLens(INode node, out CodeLens codeLens)
        {
            codeLens = default(CodeLens);

            // If the current module is V1 the qualifier types would be unavailable
            var qualifierType = SemanticModel.GetCurrentQualifierType(node);
            if (qualifierType == null)
            {
                return false;
            }

            var qualifierDeclaration = SemanticModel.GetCurrentQualifierDeclaration(node);
            if (qualifierDeclaration == null)
            {
                return false;
            }

            var formattedQualifier = TypeChecker.TypeToString(qualifierType);

            var fileUri = qualifierDeclaration.GetSourceFile().ToUri().AbsoluteUri;
            var range = qualifierDeclaration.ToRange();

            codeLens = new CodeLens
            {
                Range = node.ToRange(),
                Command = new Command
                {
                    Title = FormattableStringEx.I($"Qualifier type: {formattedQualifier}"),
                    CommandIdentifier = "DScript.openDocument",
                    Arguments = new object[]
                    {
                        fileUri,
                        range,
                    },
                },
            };

            return true;
        }
    }
}
