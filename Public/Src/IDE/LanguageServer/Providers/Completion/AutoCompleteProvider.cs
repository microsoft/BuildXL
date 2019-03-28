// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;

using BuildXL.Utilities;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Ide.LanguageServer.Providers;
using BuildXL.Ide.LanguageServer.Utilities;
using LanguageServer;
using LanguageServer.Json;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using TypeScript.Net.Extensions;
using TypeScript.Net.Reformatter;
using TypeScript.Net.Types;
using CancellationToken = System.Threading.CancellationToken;
using ISymbol = TypeScript.Net.Types.ISymbol;

namespace BuildXL.Ide.LanguageServer.Completion
{
    /// <summary>
    /// Provider for code auto-completion functionality.
    /// </summary>
    public sealed class AutoCompleteProvider : IdeProviderBase
    {
        /// <summary>
        /// Contains an zero-length completion array with successful result
        /// </summary>
        private static readonly Result<ArrayOrObject<CompletionItem, CompletionList>, ResponseError> s_emptyResult = Result<ArrayOrObject<CompletionItem, CompletionList>, ResponseError>.Success(new CompletionItem[0]);

        /// <nodoc/>
        public AutoCompleteProvider(ProviderContext providerContext)
            : base(providerContext)
        {
            InitializeCompletionInformation();
        }

#pragma warning disable CA1822 // Member is static
        /// <nodoc/>
        public Result<CompletionItem, ResponseError> ResolveCompletionItem(CompletionItem item, CancellationToken token)
        {
            // TODO: support cancellation
            // https://github.com/Microsoft/language-server-protocol/blob/master/protocol.md#completionItem_resolve
            return Result<CompletionItem, ResponseError>.Success(item);
        }
#pragma warning restore CA1822

        /// <summary>
        /// Delegate that returns whether or not a completion handler should be run even
        /// after the syntax kind of the starting node, and its parents, has been verified.
        /// </summary>
        private delegate bool ShouldRunCompletionHandler(CompletionState completionState, INode completionStartNode);

        /// <summary>
        /// Delegate that returns the symbols for a given node.
        /// </summary>
        /// <remarks>
        /// A handler can either return an enumeration of symbols or an enumeration of completion items. When
        /// it returns an enumeration of sybmols, the completion items will be created automatically based
        /// on the symbol.
        /// </remarks>
        private delegate IEnumerable<ISymbol> SymbolCompletionHandler(CompletionState completionState, INode completionStartNode);

        /// <summary>
        /// Delegate that returns the completion itmes for a given node.
        /// </summary>
        /// <remarks>
        /// A handler can either return an enumeration of symbols or an enumeration of completion items.
        /// A handler typically returns completion items directly when there is no equivalent symbol (syntax tree symbol) for
        /// the completion item. For example, a string literal type has a string literal associated with it. There is no
        /// associated symbol for the string literal, so the handler must create the completion item directly.
        /// This also happens for import statements, as well as tagged template expressions used for file and directory paths.
        /// </remarks>
        private delegate IEnumerable<CompletionItem> CompletionItemCompletionHandler(CompletionState completionState, INode completionStartNode);

        /// <summary>
        /// Contains information that allows specific completion handlers to be implemented.
        /// </summary>
        /// <remarks>
        /// The concept here is that given a starting node kind, and an optional list of parent kinds,
        /// call a handler to retrieve the symbols for the node.
        ///
        /// This allows a specific implementation for a certain conditions to be implemented.
        /// </remarks>
        private struct CompletionInformation : IComparable<CompletionInformation>
        {
            /// <summary>
            /// The node kind representing the types of AST nodes the handler is interested in.
            /// </summary>
            public SyntaxKind StartingSyntaxKind;

            /// <summary>
            /// The parent chain of node kinds that must be satisfied before the handler is invoked.
            /// </summary>
            public List<SyntaxKind> ParentKinds;

            /// <summary>
            /// Gives a completion handler an option to opt out of being executed.
            /// </summary>
            /// <remarks>
            /// This can be used to handle cases where two parent chains are the same for different
            /// conditions.
            ///
            /// If this value is null, then the handler will be executed.
            /// </remarks>
            public ShouldRunCompletionHandler ShouldRunHandler;

            /// <summary>
            /// The function to be invoked when the starting syntax kind and all parent kinds are met.
            /// The function recieves the overall completion state, and the node for which "kind" will be
            /// the last kind specified in the parent kind list, or if the parent kind list is not specified,
            /// will match the kind of the starting syntax kind.
            /// </summary>
            /// <remarks>
            /// The symbols returned will automatically have completion items created for them based on
            /// information returned in the symbol.
            /// This handler should not be secified if <see cref="CompletionItemCompletionHandler"/> is specified
            /// as the two handlers are mutually exclusive.
            /// </remarks>
            public SymbolCompletionHandler SymbolCompletionHandler;

            /// <summary>
            /// The function to be invoked when the starting syntax kind and all parent kinds are met.
            /// The function recieves the overall completion state, and the node for which "kind" will be
            /// the last kind specified in the parent kind list, or if the parent kind list is not specified,
            /// will match the kind of the starting syntax kind.
            /// </summary>
            /// <remarks>
            /// This handler should not be secified if <see cref="SymbolCompletionHandler"/> is specified
            /// as the two handlers are mutually exclusive.
            /// </remarks>
            public CompletionItemCompletionHandler CompletionItemCompletionHandler;

            #region IComparable

            // The comparer is responsible for sorting the list of completion handlers
            // first by the starting syntax kind, and then the longest parent chain.
            public int CompareTo(CompletionInformation other)
            {
                // First sort by the starting syntax kind.
                if (StartingSyntaxKind != other.StartingSyntaxKind)
                {
                    return (int)StartingSyntaxKind - (int)other.StartingSyntaxKind;
                }

                // If neither has a parent syntax kind list, then the objects are the same.
                if (ParentKinds == null && other.ParentKinds == null)
                {
                    return 0;
                }

                // If they both have a list, then sort by their lengths
                if (!ParentKinds.IsNullOrEmpty() && !other.ParentKinds.IsNullOrEmpty())
                {
                    // We want to make sure that if the "other" object has more
                    // parents than this one, that it preceeds this one in the list.
                    // Which means, return less than zero to indicate that "this"
                    // object preceeds the "other" object.
                    return other.ParentKinds.Count - ParentKinds.Count;
                }

                // If this object has a parent kind list, then ensure it preceeds the other in the list
                if (!ParentKinds.IsNullOrEmpty())
                {
                    return -1;
                }

                // This object does not have a parent kind list, so it must be greater than "other"
                Contract.Assert(!other.ParentKinds.IsNullOrEmpty());
                return 1;
            }

            #endregion
        }

        /// <summary>
        /// The list containing the completion information data structures that will match AST Node syntax kinds (and their parents)
        /// to a handler that knows how to create symbols for them.
        /// </summary>
        private readonly List<CompletionInformation> m_completionInformation = new List<CompletionInformation>();

        /// <nodoc/>
        private void AddSymbolCompletionHandlerInformation(SyntaxKind startingSyntaxKind, SymbolCompletionHandler completionHandler, ShouldRunCompletionHandler shouldRunHandler, params SyntaxKind[] parentKinds)
        {
            m_completionInformation.Add(new CompletionInformation
            {
                StartingSyntaxKind = startingSyntaxKind,
                ParentKinds = parentKinds.ToList(),
                SymbolCompletionHandler = completionHandler,
                ShouldRunHandler = shouldRunHandler,
            });
        }

        /// <nodoc/>
        private void AddSymbolCompletionHandlerInformation(SyntaxKind startingSyntaxKind, SymbolCompletionHandler completionHandler, params SyntaxKind[] parentKinds)
        {
            m_completionInformation.Add(new CompletionInformation
            {
                StartingSyntaxKind = startingSyntaxKind,
                ParentKinds = parentKinds.ToList(),
                SymbolCompletionHandler = completionHandler,
                ShouldRunHandler = (state, node) => true,
            });
        }

        /// <nodoc/>
        private void AddCompletionItemCompletionHandlerInformation(SyntaxKind startingSyntaxKind, CompletionItemCompletionHandler completionHandler, ShouldRunCompletionHandler shouldRunHandler, params SyntaxKind[] parentKinds)
        {
            m_completionInformation.Add(new CompletionInformation
            {
                StartingSyntaxKind = startingSyntaxKind,
                ParentKinds = parentKinds.ToList(),
                CompletionItemCompletionHandler = completionHandler,
                ShouldRunHandler = shouldRunHandler,
            });
        }

        /// <nodoc/>
        private void AddCompletionItemCompletionHandlerInformation(SyntaxKind startingSyntaxKind, CompletionItemCompletionHandler completionHandler, params SyntaxKind[] parentKinds)
        {
            m_completionInformation.Add(new CompletionInformation
            {
                StartingSyntaxKind = startingSyntaxKind,
                ParentKinds = parentKinds.ToList(),
                CompletionItemCompletionHandler = completionHandler,
                ShouldRunHandler = (state, node) => true,
            });
        }
        
        /// <summary>
        /// Initializes the <see cref="m_completionInformation"/> list.
        /// </summary>
        private void InitializeCompletionInformation()
        {
            // Handle the case for:
            // import * as bar from "{completion happens here}"
            AddCompletionItemCompletionHandlerInformation(
                SyntaxKind.StringLiteral,
                ImportStatement.GetCompletionsForImportStatement,
                ImportStatement.ShouldCreateCompletionItemsForImportStatements);

            // Handle the case for:
            // const myFile = f`{completion happens here}`;
            AddCompletionItemCompletionHandlerInformation(
                SyntaxKind.FirstTemplateToken,
                TaggedTemplatedExpression.TryCreateFileCompletionItemsForTaggedTemplatedExpression,
                TaggedTemplatedExpression.ShouldCreateFileCompletionItemsForTaggedTemplatedExpression,
                SyntaxKind.TaggedTemplateExpression);

            // Add handler for this case:
            //
            // interface MyType {
            //    requiredProperty: boolean
            // };
            //
            // const foo = < MyType >{
            //    <Ctrl+Space>
            // };
            //
            // The node that completion begins on is the actual object literal
            // expression. This is enough for the type checker to look up
            // contextual type information.
            AddSymbolCompletionHandlerInformation(
                SyntaxKind.ObjectLiteralExpression,
                ObjectLiteralExpressions.CreateSymbolsFromObjectLiteralExpression);

            // TODO: This condition was in the code previous to the
            // TODO: data drive refactoring. We need to figure out
            // TODO: if this case is even legitimate.
            AddSymbolCompletionHandlerInformation(
                SyntaxKind.PropertyAccessExpression,
                ObjectLiteralExpressions.CreateSymbolsFromObjectLiteralExpression,
                SyntaxKind.ShorthandPropertyAssignment, SyntaxKind.ObjectLiteralExpression);

            // TODO: We need to introduce the ability to map the starting
            // TODO: point from the beginning of completion.
            // TODO: Identifiers (SyntaxKind.Identifier) are really just
            // TODO: considered "words" from the type-checker perspective.
            // TODO: So when you ask the type-checker for "contextual type"
            // TODO: information, it cannot do so because that "word" has not
            // TODO: "context". However, the preceeding "node" or token typically
            // TODO: does. You cannot simply back up one node (go to its parent if you will)
            // TODO: because it still does not contain enough context.
            // TODO:
            // TODO: For example:
            // TODO: interface MyType { property: boolean }
            // TODO: const a : MyType = { <Type a 'p'> }
            // TODO: When you type the character 'p', the identifier is 'p', the parent
            // TODO: is a short hand property assignment. Neither node has enough
            // TODO: context for the type checker to perform its work since it simply
            // TODO: doesn't know what 'p' is meant to be.
            // TODO: However, the object literal expression does as it is a node
            // TODO: belonging to a strongly typed variable declaration.

            // Add handler for this case:
            //
            // interface MyType {
            //    requiredProperty: boolean
            // };
            //
            // const foo = < MyType >{
            //    r
            // };
            //
            // Where the identifier is "r", the parent is a short hand
            // property assignment, inside an object literal.
            // The type checker needs to object literal to acquire contextual
            // type information.
            AddSymbolCompletionHandlerInformation(
                SyntaxKind.Identifier,
                ObjectLiteralExpressions.CreateSymbolsFromObjectLiteralExpression,
                SyntaxKind.ShorthandPropertyAssignment, SyntaxKind.ObjectLiteralExpression);

            // Add handler for this case:
            //
            // interface MyFunctionArguments {
            //    requiredProperty: boolean,
            //    anotherProperty: boolean
            // };
            //
            // function myFunction(args:MyFunctionArguments){
            // };
            //
            // const foo = myFunction({
            //    a
            //    requiredProperty:true
            // });
            //
            // Where the identifier is "a" (which is being typed), the parent is a  property assignment, inside an object literal.
            // The type checker needs to object literal to acquire contextual
            // type information.
            AddSymbolCompletionHandlerInformation(
                SyntaxKind.Identifier,
                ObjectLiteralExpressions.CreateSymbolsFromObjectLiteralExpression,
                SyntaxKind.PropertyAssignment, SyntaxKind.ObjectLiteralExpression);

            // Add handler for this case:
            //
            // interface MyType {
            //    requiredProperty: boolean
            // };
            //
            // const foo = < MyType >{
            //    requiredProperty: true
            // };
            //
            // const bar = foo.<Completion occurs here>
            //
            // Where the identifier is the "." and its parent
            // is the the "foo" node which the TypeChecker can
            // acquire contextual type information.
            AddSymbolCompletionHandlerInformation(
                SyntaxKind.Identifier,
                PropertyAccessExpression.CreateSymbolsFromPropertyAccessExpression,
                SyntaxKind.PropertyAccessExpression);

            // Add a handler for this case:
            //
            // function MyFunction(args: MyType) {
            //    let myArgsProperty = args.
            //    <someOtherCode>
            //
            // What happens in this case is the AST actually creates
            // a property access expression where the "left hand expression"
            // is "args" and the "name" (the stuff to the right of the dot) is <someOtherCode>.
            // If you actually inspect the "formatted text" it looks like
            //    let myArgsProperty = args.<someOtherCode>
            // All the type-checker needs to operate on is the "left hand expression"
            // since "args" has already been resolved to a block scoped variable
            // that it can resolve the type for. The fact that the "name" portion currently doesn't
            // make sense is actually not relavent.
            AddSymbolCompletionHandlerInformation(
                SyntaxKind.PropertyAccessExpression,
                PropertyAccessExpression.CreateSymbolsFromPropertyAccessExpression);

            // Handles the case of:
            // const myVar : StringLiterlType = "{completion happens here}";
            AddCompletionItemCompletionHandlerInformation(
                SyntaxKind.StringLiteral,
                StringLiteral.CreateCompletionItemsFromVariableDeclaration,
                SyntaxKind.VariableDeclaration);

            // Handles the case of:
            // const myVar : someInterfaceType  = { stringLiteralProperty: "{completion happens here}" };
            AddCompletionItemCompletionHandlerInformation(
                SyntaxKind.StringLiteral,
                StringLiteral.CreateCompletionItemsFromPropertyAssignment,
                SyntaxKind.PropertyAssignment);

            // Handles the case of:
            // const foo = (a == b) ? "{Completion happens here}" : "{Completion happens here}"
            AddCompletionItemCompletionHandlerInformation(
                SyntaxKind.StringLiteral,
                StringLiteral.CreateCompletionItemsFromConditionalExpression,
                SyntaxKind.ConditionalExpression);

            // Handles the case of:
            // return ""
            AddCompletionItemCompletionHandlerInformation(
                SyntaxKind.StringLiteral,
                StringLiteral.CreateCompletionItemsFromReturnStatement,
                SyntaxKind.ReturnStatement);

            // Handles the case of:
            // function myFunction(args: "A" | "B" | "C");
            // const myVar = myFunc({completion happens here});
            AddCompletionItemCompletionHandlerInformation(
                SyntaxKind.StringLiteral,
                StringLiteral.CreateCompletionFromCallExperssion,
                SyntaxKind.CallExpression);

            // Handles the switch(stringLiteralType) { case "{completion happens here}" }
            AddCompletionItemCompletionHandlerInformation(
                SyntaxKind.StringLiteral,
                StringLiteral.CreateCompletionFromCaseClause,
                SyntaxKind.CaseClause);

            // Handles the if (stringLiteralType === "completion happens here").
            AddCompletionItemCompletionHandlerInformation(
                SyntaxKind.StringLiteral,
                StringLiteral.CreateCompletionFromBinaryExpression,
                StringLiteral.ShouldRunCreateCompletionFromBinaryExpression,
                SyntaxKind.BinaryExpression);

            // Handles the cases where completion occurs after a qualified name
            // such as referencing a type, interface, or variable that has been
            // imported from another module.
            AddSymbolCompletionHandlerInformation(
                SyntaxKind.Identifier,
                QualifiedName.CreateCompletionItemsFromQualifiedName,
                SyntaxKind.QualifiedName);

            // The completion handler list must be kept sorted so that the
            // handler can be located correctly for the longest parent kind chain.
            m_completionInformation.Sort();
        }

        /// <summary>
        /// Tries to find a completion handler given for the current AST node.
        /// </summary>
        /// <remarks>
        /// Note that in order for this function to work correctly, the <see cref="m_completionInformation"/> list must
        /// remain sorted by "starting syntax kind" and then by the "longest" parent kind list as
        /// the longest chains are checked first, then smaller chains, and so on, to find the best
        /// handler for the job.
        /// </remarks>
        private bool TryFindCompletionHandlerForNode(CompletionState completionState, out SymbolCompletionHandler symbolHandler, out CompletionItemCompletionHandler completionHandler, out INode completionNode)
        {
            symbolHandler = null;
            completionHandler = null;
            completionNode = null;

            foreach (var completionInfo in m_completionInformation)
            {
                if (completionInfo.StartingSyntaxKind == completionState.StartingNode.Kind)
                {
                    symbolHandler = completionInfo.SymbolCompletionHandler;
                    completionHandler = completionInfo.CompletionItemCompletionHandler;

                    completionNode = completionState.StartingNode;

                    // If we don't have any parent kinds, then we are done.
                    if (completionInfo.ParentKinds.IsNullOrEmpty())
                    {
                        return true;
                    }

                    // Walk up the parent kinds and make sure that our node has
                    // the correct type of parent.
                    foreach (var parentKind in completionInfo.ParentKinds)
                    {
                        completionNode = (completionNode.Parent?.Kind == parentKind) ? completionNode.Parent : null;

                        if (completionNode == null)
                        {
                            break;
                        }
                    }

                    // If at this point, we still have a completion node, then we are good to go as we have matched
                    // the longest parent kind list.
                    if (completionNode != null)
                    {
                        if (completionInfo.ShouldRunHandler == null || completionInfo.ShouldRunHandler(completionState, completionNode))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <nodoc/>
        public Result<ArrayOrObject<CompletionItem, CompletionList>, ResponseError> Completion(TextDocumentPositionParams positionParameters, CancellationToken cancellationToken)
        {
            // TODO: support cancellation
            // Node at position can be null if the entire file is encapsulated in a comment.
            if (!TryFindNode(positionParameters, out var nodeAtPoint))
            {
                // The message was logged.
                return s_emptyResult;
            }

            return GetCompletionItemsForNode(new CompletionState(nodeAtPoint, positionParameters, TypeChecker, Workspace, PathTable));
        }

        private Result<ArrayOrObject<CompletionItem, CompletionList>, ResponseError> GetCompletionItemsForNode(CompletionState completionState)
        {
            IEnumerable<ISymbol> autoCompleteSymbols = null;
            if (TryFindCompletionHandlerForNode(completionState, out var symbolHandler, out var completionHandler, out var completionNode))
            {
                Contract.Assert(symbolHandler != null || completionHandler != null);

                if (symbolHandler != null)
                {
                    autoCompleteSymbols = symbolHandler(completionState, completionNode);
                }
                else
                {
                    var handlerCompletionItems = completionHandler(completionState, completionNode);
                    if (handlerCompletionItems != null)
                    {
                        return Result<ArrayOrObject<CompletionItem, CompletionList>, ResponseError>.Success(handlerCompletionItems.ToArray());
                    }
                }
            }
            else
            {
                // find nodes in scope that match this name and use those as the completion items (we're probably just typing a word and
                // auto-complete fired)
                autoCompleteSymbols = TypeChecker.GetSymbolsInScope(completionState.StartingNode, SymbolFlags.BlockScoped | SymbolFlags.ModuleMember | SymbolFlags.Alias | SymbolFlags.Namespace | SymbolFlags.Type);
            }

            // TODO: Log telemetry when this occurs
            if (autoCompleteSymbols == null)
            {
                return s_emptyResult;
            }

            var completionItems = autoCompleteSymbols
                .Select(autoCompleteSymbol => GetCompletionItemForSymbol(autoCompleteSymbol))
                .ToArray();

            return Result<ArrayOrObject<CompletionItem, CompletionList>, ResponseError>.Success(completionItems);
        }

        private static CompletionItemKind CompletionItemKindFromSymbolFlags(SymbolFlags flags)
        {
            if ((flags &SymbolFlags.Property) != SymbolFlags.None || (flags &SymbolFlags.Accessor) != SymbolFlags.None)
            {
                return CompletionItemKind.Property;
            }

            if ((flags &SymbolFlags.RegularEnum) != SymbolFlags.None || (flags &SymbolFlags.ConstEnum) != SymbolFlags.None || (flags &SymbolFlags.EnumMember) != SymbolFlags.None)
            {
                return CompletionItemKind.Enum;
            }

            if ((flags &SymbolFlags.Function) != SymbolFlags.None)
            {
                return CompletionItemKind.Function;
            }

            if ((flags &SymbolFlags.Interface) != SymbolFlags.None)
            {
                return CompletionItemKind.Interface;
            }

            if ((flags &SymbolFlags.Class) != SymbolFlags.None)
            {
                return CompletionItemKind.Class;
            }

            if ((flags &SymbolFlags.Variable) != SymbolFlags.None)
            {
                return CompletionItemKind.Variable;
            }

            if ((flags &SymbolFlags.Method) != SymbolFlags.None)
            {
                return CompletionItemKind.Method;
            }

            if ((flags &SymbolFlags.Value) != SymbolFlags.None)
            {
                return CompletionItemKind.Value;
            }

            return CompletionItemKind.Text;
        }

        private CompletionItem GetCompletionItemForSymbol(ISymbol symbol)
        {
            var functionSignature = DScriptFunctionSignature.FromSymbol(symbol);

            if (functionSignature != null)
            {
                return new CompletionItem()
                {
                    Kind = CompletionItemKind.Function,
                    Label = functionSignature.FunctionName,
                    InsertText = functionSignature.FunctionName,
                    Detail = functionSignature.FormattedFullFunctionSignature,
                    Documentation = DocumentationUtilities.GetDocumentationForSymbolAsString(symbol),
                };
            }

            return new CompletionItem()
            {
                Label = symbol.Name,
                Kind = CompletionItemKindFromSymbolFlags(symbol.Flags),
                InsertText = symbol.Name,
                Detail = GetCompletionItemDetail(symbol),
                Documentation = DocumentationUtilities.GetDocumentationForSymbolAsString(symbol),
            };
        }

        private string GetCompletionItemDetail(ISymbol symbol)
        {
            // happens if the symbol isn't actually a type (for instance if we're handing out
            // symbols in scope)
            if (symbol.ValueDeclaration == null)
            {
                return string.Empty;
            }

            switch (symbol.ValueDeclaration.Kind)
            {
                case SyntaxKind.PropertyAccessExpression:
                case SyntaxKind.EnumDeclaration:
                case SyntaxKind.PropertySignature:
                    return symbol.ValueDeclaration.GetFormattedText();
                default:
                    return GetNameOfType(symbol.ValueDeclaration);
            }
        }

        private string GetNameOfType(INode node)
        {
            var type = Workspace.GetSemanticModel().GetTypeAtLocation(node);

            var name = type.Symbol?.Name;

            if (name != null)
            {
                return name;
            }

            var asIntrinsicType = type.As<IIntrinsicType>();

            if (asIntrinsicType != null)
            {
                return asIntrinsicType.IntrinsicName;
            }

            return string.Empty;
        }
    }
}
