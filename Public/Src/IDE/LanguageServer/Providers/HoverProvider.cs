// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Ide.LanguageServer.Utilities;
using JetBrains.Annotations;
using LanguageServer;
using LanguageServer.Json;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using DScriptUtilities = TypeScript.Net.DScript.Utilities;
using TypeScript.Net.Reformatter;
using TypeScript.Net.Types;
using CancellationToken = System.Threading.CancellationToken;

namespace BuildXL.Ide.LanguageServer.Providers
{
    /// <summary>
    /// Provider for documentation/function signature peek functionality on mouse-hover.
    /// </summary>
    public sealed class HoverProvider : IdeProviderBase
    {
        /// <nodoc/>
        public HoverProvider(ProviderContext providerContext)
            : base(providerContext)
        { }

        /// <summary>
        /// Given a node and its symbol, and a string representation of a code snippet for the hover, constructs the language server Hover
        /// object with the documentation (trivia) collected for the symbol.
        /// </summary>
        /// <param name="symbol">The symbol for wich documentation will be collected.</param>
        /// <param name="node">The node, whose range will be used to indicate the location back through the languer server protocol</param>
        /// <param name="snippet">The code snippet that will be used to represent the node and the symbol to the user.</param>
        private static Result<Hover, ResponseError> CreateScriptHoverWithCodeSnippetAndDocumentation(ISymbol symbol, INode node, string snippet)
        {
            Contract.Requires(symbol != null);
            Contract.Requires(node != null);
            Contract.Requires(!string.IsNullOrEmpty(snippet));

            var documentation = DocumentationUtilities.GetDocumentationForSymbol(symbol);

            // The documentation strings are sent to the client as regular strings, wheras
            // the symbolAsString is a code snippet that is sent with language specifier to enable syntax highlighting.
            var codeSnippet = new List<StringOrObject<MarkedString>>
            {
                new StringOrObject<MarkedString>
                (
                    new MarkedString
                    {
                        Language = "DScript",
                        Value = snippet,
                    }
                ),
            };

            codeSnippet.AddRange(documentation.Select(d =>
            {
                return new StringOrObject<MarkedString>
                (
                    new MarkedString
                    {
                        Value = d
                    }
                );
            }));

            return Result<Hover, ResponseError>.Success(new Hover
            {
                Contents = codeSnippet.ToArray(),
                Range = node.ToRange(),
            });
        }

        /// <summary>
        /// Creates a hover response that represents the type related to the symbol and node specified.
        /// </summary>
        private Result<Hover, ResponseError> CreateHoverBasedOnTypeInformation(ISymbol symbol, INode node, string symbolAsString)
        {
            // If the node is an expression, then use the contextual type as it will contain
            // better type information.
            // For example:
            // Asking for the contextual type on a node like:
            // const myStringLiteral : MyStringLiteralType = "String Literal A"
            // will actually give you back the type "MyStringLiteralType" and hence hover will show
            // you all the options. The type checker is giving you the best type available given the
            // context.
            //
            // Whereas asking for type at location will give you "String Literal Type" and the only
            // string literal "String Literal A" for hover. The type checker is giving you the type
            // it can deduce solely based on the single node.
            var typeAsExpression = node.As<IExpression>();
            var typeAtLocation = typeAsExpression != null ? TypeChecker.GetContextualType(typeAsExpression) : TypeChecker.GetTypeAtLocation(node);

            // If GetContextualType does not return a valid type, getting the type at location is better than displaying
            // nothing
            if (typeAtLocation == null)
            {
                typeAtLocation = TypeChecker.GetTypeAtLocation(node);
            }

            var typeAsString = typeAtLocation != null ? CreateStringFromType(typeAtLocation) : string.Empty;

            if (string.IsNullOrEmpty(typeAsString) &&
                string.IsNullOrEmpty(symbolAsString))
            {
                return Result<Hover, ResponseError>.Success(null);
            }

            string finalTypeString;
            if (string.IsNullOrEmpty(typeAsString) || string.IsNullOrEmpty(symbolAsString))
            {
                finalTypeString = string.Format(
                    BuildXL.Ide.LanguageServer.Strings.HoverJustSymbolOrTypeFormat, 
                    string.IsNullOrEmpty(symbolAsString) ? typeAsString : symbolAsString);
            }
            else
            {
                finalTypeString = string.Format(BuildXL.Ide.LanguageServer.Strings.HoverSymbolAndStringFormat, symbolAsString, typeAsString);
            }

            return CreateScriptHoverWithCodeSnippetAndDocumentation(symbol, node, finalTypeString);
        }

        /// <summary>
        /// Implements the "textDocument/hover" portion of the language server protocol.
        /// </summary>
        public Result<Hover, ResponseError> Hover(TextDocumentPositionParams positionParams, CancellationToken token)
        {
            // TODO: support cancellation
            Contract.Requires(positionParams.Position != null);

            if (!TryFindNode(positionParams, out var node))
            {
                return Result<Hover, ResponseError>.Success(null);
            }

            // Label names are things such as "break", "continue", "jump" for which
            // we do not need to provide any hover.
            if (DScriptUtilities.IsLabelName(node))
            {
                return Result<Hover, ResponseError>.Success(null);
            }

            // Attempt to get the type from the node
            var typeAtLocation = TypeChecker.GetTypeAtLocation(node);

            if ((typeAtLocation.Flags & TypeFlags.Any) != TypeFlags.None && node.Kind == SyntaxKind.Identifier)
            {
                // Now if we are likely to return any (unknown), and we are on an identifier
                // its parent is a type-reference then let's display that instead.
                // An example of this is "someTemplateVariable.merge<>"
                if (node.Parent?.Kind == SyntaxKind.TypeReference)
                {
                    node = node.Parent;
                }
                else if (node.Parent?.Kind == SyntaxKind.QualifiedName)
                {
                    // The same type of thing can happen with values referenced
                    // from an imported value (i.e. qualified name) such as
                    // import "A" from "SomeModule";
                    // let z = A.B.C.value;
                    // "value" is the identifier and "A.B.C" are the qualified names.
                    // As you walk up the chain, you will eventually find "A" which is
                    // a type-reference.

                    var nodeWalk = node.Parent;
                    while (nodeWalk != null && nodeWalk.Kind == SyntaxKind.QualifiedName)
                    {
                        nodeWalk = nodeWalk.Parent;
                    }

                    if (nodeWalk.Kind == SyntaxKind.TypeReference)
                    {
                        node = nodeWalk;
                    }
                }
            }

            // TODO: Not sure why GetSymbolAtLocation isn't enough
            var symbol = TypeChecker.GetSymbolAtLocation(node) ?? node.Symbol ?? node.ResolvedSymbol;

            // Find the first declaration. We will use it to special case a few things below.
            var firstDeclaration = symbol?.GetFirstDeclarationOrDefault();

            // Grab the symbol name from the first declaration.
            string symbolAsString = firstDeclaration?.Name?.Text;

            // Handle a few special cases before we default to showing the type.
            // Typically these special cases are where hover is performed on a declaration of
            // things such as an enum, import statement (and eventually others)
            if (firstDeclaration != null && symbolAsString != null)
            {
                // Special case import * from "" and import {} from "" and import {x as y} from ""
                // So we can show the user a hover that says "import {} from 'module' so they can easily
                // see where it is coming from.
                if (firstDeclaration.Kind == SyntaxKind.ImportSpecifier || firstDeclaration.Kind == SyntaxKind.NamespaceImport)
                {
                    IImportClause importClause;
                    if (firstDeclaration.Kind == SyntaxKind.ImportSpecifier)
                    {
                        var importSpecifier = firstDeclaration.Cast<IImportSpecifier>();
                        var namedImports = importSpecifier.Parent.Cast<INamedImports>();
                        importClause = namedImports.Parent.Cast<IImportClause>();
                    }
                    else
                    {
                        var namespaceImport = firstDeclaration.Cast<INamespaceImport>();
                        importClause = namespaceImport.Parent.Cast<IImportClause>();
                    }

                    var importDeclaration = importClause.Parent.Cast<IImportDeclaration>();

                    return CreateScriptHoverWithCodeSnippetAndDocumentation(
                        symbol, 
                        node, 
                        string.Format(BuildXL.Ide.LanguageServer.Strings.HoverImportFormat, symbolAsString, importDeclaration.ModuleSpecifier.ToDisplayString()));
                }

                // For interface declarations, just show a hover of "interface X"
                if (firstDeclaration.Kind == SyntaxKind.InterfaceDeclaration)
                {
                    return CreateScriptHoverWithCodeSnippetAndDocumentation(symbol, node, firstDeclaration.GetFormattedText());
                    // We have decided to show the formatted text of the declaration as it allows
                    // the user to browse the entire interface.
                    // If we decide we want a more conscise hover, we can uncomment the following
                    // code.
                    //var interfaceDeclaration = firstDeclaration.Cast<IInterfaceDeclaration>();
                    //return CreateScriptHoverWithCodeSnippetAndDocumentation(symbol, node, string.Format(Strings.HoverInterfaceFormat, interfaceDeclaration.Name.Text));
                }

                // For enum declarations, show a hover of "enum { x, y, z}"
                if (firstDeclaration.Kind == SyntaxKind.EnumDeclaration)
                {
                    return CreateScriptHoverWithCodeSnippetAndDocumentation(symbol, node, firstDeclaration.GetFormattedText());
                    // We have decided to show the formatted text of the declaration as it allows
                    // the user to browse the entire enumeration.
                    // If we decide we want a more conscise hover, we can uncomment the following
                    // code.
                    /*
                    var enumDeclaration = firstDeclaration.Cast<IEnumDeclaration>();

                    var memberStrings = new List<string>();
                    foreach (var member in enumDeclaration.Members)
                    {
                        memberStrings.Add(member.Cast<IEnumMember>().Name.Text);
                    }

                    var memberString = memberStrings.Aggregate((current, next) => string.Format(Strings.HoverEnumMemberFormat, current, next));

                    return CreateScriptHoverWithCodeSnippetAndDocumentation(symbol, node, string.Format(Strings.HoverEnumFormat, enumDeclaration.Name.Text, memberString));
                    */
                }


                // Handle things that are function-like (such as interface methods, functions, etc).
                var functionLike = firstDeclaration.As<IFunctionLikeDeclaration>();
                if (functionLike != null)
                {
                    return CreateScriptHoverWithCodeSnippetAndDocumentation(symbol, node, functionLike.ToDisplayString());
                }
            }

            // So if we can't find a suitable declaration for the node to display for hover,
            // Then we will display the type associated the symbol, which is super useful
            // for things like variable declarations, etc. (You'll notice that variable declarations are not
            // handled above, for exactly this purpose).
            if (node != null && symbol != null)
            {
                return CreateHoverBasedOnTypeInformation(symbol, node, symbolAsString);
            }

            return Result<Hover, ResponseError>.Success(null);
        }
        
        /// <summary>
        /// Creates a string representation of a type to display in hover.
        /// </summary>
        private static string CreateStringFromType([CanBeNull] IType type)
        {
            if (type == null)
            {
                return string.Empty;
            }

            var declaration = type?.Symbol?.Declarations?.FirstOrDefault();

            if (declaration != null)
            {
                // Handle a few function types uniquely, such as function declarations
                // and function types.
                // For those, just return the display string BuildXL already provides.
                if (declaration.Kind == SyntaxKind.FunctionDeclaration)
                {
                    return declaration.Cast<IFunctionDeclaration>().ToDisplayString();
                }

                if (declaration.Kind == SyntaxKind.FunctionType)
                {
                    // Not all "function types" can be cast to IFunctionTypeNode or IFunctionOrConstructorTypeNode.
                    // so we will use As to just attempt the cast.
                    var functionOrConstructorTypeNode = declaration.As<IFunctionOrConstructorTypeNode>();

                    if (functionOrConstructorTypeNode != null)
                    {
                        return functionOrConstructorTypeNode.ToDisplayString();
                    }
                }
            }

            // For a intrinsic type (bool, string, etc.) just return the name.
            if ((type.Flags & TypeFlags.Intrinsic) != TypeFlags.None)
            {
                return type.Cast<IIntrinsicType>().IntrinsicName;
            }

            // For a string literal, return the text for it.
            if ((type.Flags & TypeFlags.StringLiteral) != TypeFlags.None)
            {
                return string.Format(BuildXL.Ide.LanguageServer.Strings.StringLiteralHoverFormat, type.Cast<IStringLiteralType>().Text);
            }

            // For a union, return a string composed of all of its members.
            // (member | member)
            if ((type.Flags & TypeFlags.UnionOrIntersection) != TypeFlags.None)
            {
                var unionStrings = new List<string>();

                foreach (var member in type.Cast<IUnionOrIntersectionType>().Types)
                {
                    unionStrings.Add(CreateStringFromType(member));
                }

                return string.Format(
                    BuildXL.Ide.LanguageServer.Strings.HoverUnionFormat, 
                    unionStrings.Aggregate((currentAggregation, nextString) => 
                                               string.Format(BuildXL.Ide.LanguageServer.Strings.HoverInnerUnionFormat, currentAggregation, nextString)));
            }

            // For a tuple, similar to a union, return a string composed of all of its elements
            // [element, element]
            if ((type.Flags & TypeFlags.Tuple) != TypeFlags.None)
            {
                var tupleStrings = new List<string>();
                foreach (var member in type.Cast<ITupleType>().ElementTypes)
                {
                    tupleStrings.Add(CreateStringFromType(member));
                }

                return string.Format(
                    BuildXL.Ide.LanguageServer.Strings.HoverTupleTypeFormat, 
                    tupleStrings.Aggregate((currentAggregation, nextString) => 
                                               string.Format(BuildXL.Ide.LanguageServer.Strings.HoverInnerTupleTypeFormat, currentAggregation, nextString)));
            }

            // For an interface, just return the name.
            // We end up here if we are building a type string (say for a union)
            // that includes an interface in the union.
            // Otherwise we would have just returned a hover object for the interface
            // declaration above.
            if ((type.Flags & TypeFlags.Interface) != TypeFlags.None)
            {
                return type.Cast<IInterfaceType>().Symbol?.Name ?? string.Empty;
            }

            // For enums, just return the name of the enum.
            if ((type.Flags & TypeFlags.Enum) != TypeFlags.None)
            {
                return type.Symbol?.Name ?? string.Empty;
            }

            // Type reference refers to a type that has type arguments (such as Map, or Array)
            // So, for those we will create Symbol<Argument, Argument>
            if ((type.Flags & TypeFlags.Reference) != TypeFlags.None)
            {
                var typeAsString = declaration?.Name?.GetFormattedText() ?? string.Empty;
                var typeReference = type.Cast<ITypeReference>();
                if (typeReference.TypeArguments?.Count > 0)
                {
                    var typeArgumentStrings = typeReference.TypeArguments.Select(typeArgument => CreateStringFromType(typeArgument));

                    return string.Format(
                        BuildXL.Ide.LanguageServer.Strings.HoverTypeArgumentsTypeFormat, 
                        typeAsString, 
                        typeArgumentStrings.Aggregate((currentAggregation, nextString) => 
                                                          string.Format(BuildXL.Ide.LanguageServer.Strings.HoverTypeArgumentsInnerFormat, currentAggregation, nextString)));
                }
            }

            return string.Empty;
        }
   }
}
