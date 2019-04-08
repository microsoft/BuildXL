// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Utilities;
using LanguageServer;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using TypeScript.Net.Types;
using CancellationToken = System.Threading.CancellationToken;

namespace BuildXL.Ide.LanguageServer.Providers
{
    /// <summary>
    /// Handles the ctrl-shift-o symbol search
    /// </summary>
    public sealed class SymbolProvider : IdeProviderBase
    {
        /// <nodoc/>
        public SymbolProvider(ProviderContext providerContext)
            : base(providerContext)
        {
        }

        /// <nodoc/>
        public Result<SymbolInformation[], ResponseError> DocumentSymbols(DocumentSymbolParams documentSymbolsParams, CancellationToken token)
        {
            // TODO: support cancellation
            if (!TryFindSourceFile(documentSymbolsParams.TextDocument.Uri, out var sourceFile))
            {
                return Result<SymbolInformation[], ResponseError>.Success(new SymbolInformation[0]);
            }

            var locals = sourceFile.Locals;

            var listOfSymbolInformation = GetSymbolInformationForLocalsDictionary(documentSymbolsParams, locals);

            return Result<SymbolInformation[], ResponseError>.Success(listOfSymbolInformation.ToArray());
        }

        private static List<SymbolInformation> GetSymbolInformationForLocalsDictionary(DocumentSymbolParams documentSymbolParams, ISymbolTable localSymbolTable)
        {
            var listOfSymbolInformation = new List<SymbolInformation>();

            foreach (var nameAndSymbol in localSymbolTable)
            {
                // if this is a namespace block let's dive into the namespace instead and return the top level declarations contained in it.
                var firstDeclaration = nameAndSymbol.Value.GetFirstDeclarationOrDefault();

                if (firstDeclaration == null)
                {
                    continue;
                }

                listOfSymbolInformation.AddRange(
                    GetSymbolInformationForNode(new Uri(documentSymbolParams.TextDocument.Uri), nameAndSymbol.Key, firstDeclaration));
            }

            return listOfSymbolInformation;
        }

        private static IEnumerable<SymbolInformation> GetSymbolInformationForNode(Uri uri, string name, IDeclaration declaration)
        {
            switch (declaration.Kind)
            {
                case SyntaxKind.ModuleDeclaration:
                    var moduleDeclaration = declaration.Cast<IModuleDeclaration>();
                    return GetModuleDeclarationChildren(moduleDeclaration, uri);
                case SyntaxKind.NamespaceImport:
                case SyntaxKind.ImportSpecifier:
                    return Enumerable.Empty<SymbolInformation>();
                default:
                    return new SymbolInformation[]
                    {
                        new SymbolInformation()
                        {
                            Name = name,
                            Kind = GetSymbolKind(declaration),
                            Location = new Location()
                            {
                                Uri = uri.ToString(),
                                Range = declaration.ToRange(),
                            },
                        },
                    };
            }
        }

        /// <nodoc/>
        public static IEnumerable<SymbolInformation> GetModuleDeclarationChildren(IModuleDeclaration moduleDeclaration, Uri uri)
        {
            // dive into the module, grab the full namespace name and list all the exported variables
            var namespacePrefix = moduleDeclaration.GetFullNameString() + ".";

            return moduleDeclaration.GetModuleBlock().GetChildNodes()
                .Where(childNode => childNode.IsTopLevelOrNamespaceLevelDeclaration())
                .Where(childNode => !childNode.IsInjectedForDScript())
                .Select(childNode => new SymbolInformation()
                {
                    Name = namespacePrefix + GetIdentifierForNode(childNode),
                    Kind = GetSymbolKind(childNode),
                    Location = new Location()
                    {
                        Uri = uri.ToString(),
                        Range = childNode.ToRange(),
                    },
                });
        }

        private static string GetIdentifierForNode(INode node)
        {
            var asDeclaration = node.As<IDeclaration>();

            if (asDeclaration != null)
            {
                return asDeclaration.Name.Text;
            }

            var asVariableStatement = node.As<IVariableStatement>();

            if (asVariableStatement != null)
            {
                return asVariableStatement.DeclarationList.Declarations[0].Name.GetText();
            }

            return FormattableStringEx.I($"Unknown({node.Kind})");
        }

        private static SymbolKind GetSymbolKind(INode node)
        {
            if (node != null)
            {
                switch (node.Kind)
                {
                    case SyntaxKind.InterfaceDeclaration:
                        return SymbolKind.Interface;
                    case SyntaxKind.MethodSignature:
                    case SyntaxKind.MethodDeclaration:
                    case SyntaxKind.FunctionDeclaration:
                        return SymbolKind.Function;
                    case SyntaxKind.TypeAliasDeclaration:
                    case SyntaxKind.TypeLiteral:
                        return SymbolKind.Interface;
                    case SyntaxKind.ObjectLiteralExpression:
                    case SyntaxKind.VariableDeclaration:
                        return SymbolKind.Variable;
                    case SyntaxKind.EnumDeclaration:
                        return SymbolKind.Enum;
                }
            }

            // for no reason at all, just picking a default that's not obnoxious
            return SymbolKind.Constant;
        }
    }
}
