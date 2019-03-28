// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BuildXL.Utilities.Collections;
using BuildXL.Ide.JsonRpc;
using BuildXL.Ide.LanguageServer.Server.Utilities;
using JetBrains.Annotations;
using LanguageServer;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using TypeScript.Net.Binding;
using TypeScript.Net.DScript;
using TypeScript.Net.Extensions;
using TypeScript.Net.Parsing;
using TypeScript.Net.Reformatter;
using TypeScript.Net.Scanning;
using TypeScript.Net.Types;
using CancellationToken = System.Threading.CancellationToken;
using ISymbol = TypeScript.Net.Types.ISymbol;
using DScriptUtilities = TypeScript.Net.DScript.Utilities;

namespace BuildXL.Ide.LanguageServer.Providers
{
    /// <summary>
    /// Provider for Find-all-references functionality.
    /// </summary>
    public sealed class FindReferencesProvider : IdeProviderBase
    {
        private readonly IProgressReporter m_progressReporter;
        private readonly int m_degreeOfParallelism;

        // TODO: see services.ts in DScript.typeScript repo, and look at nameTable field extension on SourceFile interface
        private readonly ConcurrentDictionary<ISourceFile, Dictionary<string, int>> m_nameTables = new ConcurrentDictionary<ISourceFile, Dictionary<string, int>>();

        /// <nodoc/>
        public FindReferencesProvider(ProviderContext providerContext, IProgressReporter progressReporter, int? findReferenceDegreeOfParallelism = null)
            : base(providerContext)
        {
            m_progressReporter = progressReporter;
            m_degreeOfParallelism = findReferenceDegreeOfParallelism ?? Environment.ProcessorCount;
        }

        /// <nodoc />
        public Result<Location[], ResponseError> GetReferencesAtPosition(TextDocumentPositionParams position) => GetReferencesAtPosition(position, CancellationToken.None);

        /// <nodoc />
        public Result<Location[], ResponseError> GetReferencesAtPosition(TextDocumentPositionParams position, CancellationToken cancellationToken)
        {
            Contract.Requires(position.Position != null);

            if (!TryFindSourceFile(position.TextDocument.Uri, out var sourceFile))
            {
                // The message that the file is not found is already logged.
                return Result<Location[], ResponseError>.Success(new Location[0]);
            }

            var lineAndColumn = position.Position.ToLineAndColumn();

            var sw = Stopwatch.StartNew();

            // Intentionally using all the sources. This will allow to use symbols in configuration and list files.
            var sources = Workspace.GetAllSourceFiles();
            
            var referencedSymbols = FindReferenceSymbols(
                sources,
                sourceFile,
                lineAndColumn,
                findInStrings: false,
                findInComments: false,
                cancellationToken: cancellationToken);

            var result = ConvertReferences(referencedSymbols);

            NotifyCompleteOrCancelled(sw.ElapsedMilliseconds, referencedSymbols.Sum(c => c.References.Count), sources.Length, cancellationToken);

            return result;

            void NotifyCompleteOrCancelled(long duration, int referencesCount, int sourcesCount, CancellationToken token)
            {
                if (token.IsCancellationRequested)
                {
                    m_progressReporter.ReportFindReferencesProgress(FindReferenceProgressParams.Cancelled((int) duration));
                }
                else
                {
                    m_progressReporter.ReportFindReferencesProgress(FindReferenceProgressParams.Create(referencesCount, (int)duration, sourcesCount, sourcesCount));
                }
            }
        }

        private IReadOnlyList<ReferencedSymbol> FindReferenceSymbols(
            IReadOnlyList<ISourceFile> sourceFiles,
            ISourceFile sourceFile,
            LineAndColumn lineAndColumn,
            bool findInStrings,
            bool findInComments,
            CancellationToken cancellationToken)
        {
            // Get the property node at position
            if (!DScriptNodeUtilities.TryGetNodeAtPosition(
                sourceFile,
                lineAndColumn,
                isNodeAcceptable: (n) => n.Kind.IsPropertyName() || n.IsStringLikeLiteral() || n.IsPathLikeLiteral(),
                nodeAtPosition: out var node))
            {
                // Thi is a normal situation, no need to log this.
                return null;
            }

            // String literals like 'FooBar' and string literal types like 'A'|'B' are handled separately.
            if (node.IsStringLikeLiteral())
            {
                return GetReferencesForStringLiteral(node.Cast<ILiteralLikeNode>(), sourceFiles, cancellationToken);
            }

            // Path-like things like p`foo.dsc` are handled differently as well
            if (node.IsPathLikeLiteral())
            {
                return GetReferencesForPathLikeLiterals(node.Cast<IPathLikeLiteralExpression>(), sourceFiles, cancellationToken);
            }

            if (node.Kind != SyntaxKind.Identifier &&

                // TODO: This should be enabled in a later release - currently breaks rename.
                // node.kind !== SyntaxKind.ThisKeyword &&
                // node.kind !== SyntaxKind.SuperKeyword &&
                !DScriptUtilities.IsLiteralNameOfPropertyDeclarationOrIndexAccess(node) &&
                !DScriptUtilities.IsNameOfExternalModuleImportOrDeclaration(node))
            {
                return null;
            }

            Debug.Assert(node.Kind == SyntaxKind.Identifier || node.Kind == SyntaxKind.NumericLiteral || node.Kind == SyntaxKind.StringLiteral, "Unexpected node kind");
            return GetReferencedSymbolsForNode(
                node,
                sourceFiles,
                findInStrings,
                findInComments,
                cancellationToken);
        }

        private IReadOnlyList<ReferencedSymbol> FindReferencesAndNotify(IReadOnlyList<ISourceFile> sourceFiles, Func<ISourceFile, List<ReferencedSymbol>> findReferencesSelector, CancellationToken cancellationToken)
        {
            var resultsPerFile = new IReadOnlyList<ReferencedSymbol>[sourceFiles.Count];
            using (var disposedNotifier = new ProgressNotifier(m_progressReporter, sourceFiles.Count, cancellationToken))
            {
                var notifier = disposedNotifier;
                Parallel.ForEach(
                    sourceFiles.Select((elem, idx) => Tuple.Create(elem, idx)).ToList(),
                    new ParallelOptions { MaxDegreeOfParallelism = m_degreeOfParallelism },
                    body: (tuple, loopState) =>
                          {
                              // Break the execution if requested.
                              if (cancellationToken.IsCancellationRequested)
                              {
                                  loopState.Stop();
                              }

                              // Getting the references for a current file.
                              var fileReferences = findReferencesSelector(tuple.Item1);
                              resultsPerFile[tuple.Item2] = fileReferences;

                              notifier.ProcessFileReferences(fileReferences);
                          });

                return cancellationToken.IsCancellationRequested
                    ? (IReadOnlyList<ReferencedSymbol>) CollectionUtilities.EmptyArray<ReferencedSymbol>()
                    : resultsPerFile.SelectMany(r => r ?? CollectionUtilities.EmptyArray<ReferencedSymbol>()).ToList();
            }
        }

        /// <summary>
        /// Returns all the references of a given <paramref name="literal"/>.
        /// </summary>
        /// <remarks>
        /// The function is ported (with LINQ modifications) from the typescript codebase.
        /// </remarks>
        private IReadOnlyList<ReferencedSymbol> GetReferencesForStringLiteral(ILiteralLikeNode literal, IReadOnlyList<ISourceFile> sourceFiles, CancellationToken cancellationToken)
        {
            return FindReferencesAndNotify(
                sourceFiles,
                sourceFile =>
                {
                    var possiblePositions = GetPossibleSymbolReferencePositions(sourceFile, literal.Text, 0, sourceFile.End);
                    return GetReferencesForStringLiteralInFile(sourceFile, literal.Text, possiblePositions, cancellationToken).ToList();
                },
                cancellationToken);
        }

        /// <summary>
        /// Returns all the references of a given <paramref name="literal"/>.
        /// </summary>
        /// <remarks>
        /// The function is very similar to <see cref="GetReferencesForStringLiteral"/> but instead of searching string literals it works with path-like literals.
        /// </remarks>
        private IReadOnlyList<ReferencedSymbol> GetReferencesForPathLikeLiterals(ILiteralLikeNode literal, IReadOnlyList<ISourceFile> sourceFiles, CancellationToken cancellationToken)
        {
            return FindReferencesAndNotify(
                sourceFiles, 
                sourceFile =>
                {
                    // Path-like literals are case insensitive.
                    var possiblePositions = GetPossibleSymbolReferencePositions(sourceFile, literal.Text, 0, sourceFile.End, stringComparison: StringComparison.OrdinalIgnoreCase);
                    return GetReferencesForPathLikeLiteralInFile(sourceFile, literal.Text, possiblePositions, cancellationToken).ToList();
                },
                cancellationToken);
        }

        private static IEnumerable<ReferencedSymbol> GetReferencesForStringLiteralInFile(ISourceFile sourceFile, string text, List<int> possiblePositions, CancellationToken token)
        {
            // Search though all the candidates to avoid exponential complexity.
            var touchingNodes = DScriptUtilities.GetTouchingWords(sourceFile, possiblePositions, token);

            if (token.IsCancellationRequested)
            {
                return Enumerable.Empty<ReferencedSymbol>();
            }

            return CreateReferences(touchingNodes, possiblePositions).ToList();

            IEnumerable<ReferencedSymbol> CreateReferences(IReadOnlyList<INode> nodes, IReadOnlyList<int> positions)
            {
                for (int i = 0; i < nodes.Count; i++)
                {
                    var node = nodes[i];
                    if (node?.IsStringLikeLiteral() == true && node.Cast<ILiteralLikeNode>().Text == text)
                    {
                        yield return CreateReference(sourceFile, text, positions[i]);
                    }
                }
            }
        }

        private static IEnumerable<ReferencedSymbol> GetReferencesForPathLikeLiteralInFile(ISourceFile sourceFile, string text, List<int> possiblePositions, CancellationToken token)
        {
            // Search though all the candidates to avoid exponential complexity.
            var touchingNodes = DScriptUtilities.GetTouchingPaths(sourceFile, possiblePositions, token);

            if (token.IsCancellationRequested)
            {
                return Enumerable.Empty<ReferencedSymbol>();
            }

            return CreateReferences(touchingNodes, possiblePositions).ToList();

            IEnumerable<ReferencedSymbol> CreateReferences(IReadOnlyList<INode> nodes, IReadOnlyList<int> positions)
            {
                for (int i = 0; i < nodes.Count; i++)
                {
                    var node = nodes[i];
                    if (node?.IsPathLikeLiteral() == true && string.Equals(node.Cast<IPathLikeLiteralExpression>().Text, text, StringComparison.OrdinalIgnoreCase))
                    {
                        yield return CreateReference(sourceFile, text, positions[i]);
                    }
                }
            }
        }

        private static ReferencedSymbol CreateReference(ISourceFile sourceFile, string text, int position)
        {
            return new ReferencedSymbol()
            {
                References = new List<ReferenceEntry>()
                {
                    new ReferenceEntry()
                    {
                        FileName = sourceFile.FileName,
                        SourceFile = sourceFile,
                        TextSpan = new TextSpan() { Start = position, Length = text.Length },
                        IsWriteAccess = false,
                    },
                }
            };
        }

        private IReadOnlyList<ReferencedSymbol> GetReferencedSymbolsForNode(
            INode node,
            IReadOnlyList<ISourceFile> sourceFiles,
            bool findInStrings,
            bool findInComments,
            CancellationToken cancellationToken)
        {
            // Trying to get references for labels, this keywords and similar rarely used entities.
            if (TryGetReferencedSymbolsForLabelsAndSpecialKeywords(node, sourceFiles, out var references))
            {
                return references;
            }

            // Common case: searching for a regular symbol.
            var symbol = TypeChecker.GetSymbolAtLocation(node) ?? node.Symbol;

            var declarations = symbol?.Declarations;

            // The symbol was an internal symbol and does not have a declaration e.g. undefined symbol
            if (declarations == null || declarations.Count == 0)
            {
                return null;
            }

            // Compute the meaning from the location and the symbol it references
            var searchMeaning = GetIntersectingMeaningFromDeclarations(
                GetMeaningFromLocation(node),
                declarations);

            // Get the text to search for.
            // Note: if this is an external module symbol, the name doesn't include quotes.
            var declaredName = StripQuotes(DScriptUtilities.GetDeclaredName(TypeChecker, symbol, node));

            // Try to get the smallest valid scope that we can limit our search to;
            // otherwise we'll need to search globally (i.e. include each file).
            var scope = GetSymbolScope(symbol);
            
            // Maps from a symbol ID to the ReferencedSymbol entry in 'result'.
            var symbolToIndex = new Dictionary<int, int>();

            if (scope != null)
            {
                return GetReferencesInNode(
                    scope,
                    symbol,
                    declaredName,
                    node,
                    searchMeaning,
                    findInStrings,
                    findInComments);
            }

            var internedName = GetInternedName(symbol, node, declarations);

            return FindReferencesAndNotify(
                sourceFiles,
                sf =>
                {
                    var nameTable = GetNameTable(sf);
                    if (nameTable.ContainsKey(internedName))
                    {
                        return GetReferencesInNode(
                            sf,
                            symbol,
                            declaredName,
                            node,
                            searchMeaning,
                            findInStrings,
                            findInComments);
                    }

                    return new List<ReferencedSymbol>();
                },
                cancellationToken);
        }

        private static bool TryGetReferencedSymbolsForLabelsAndSpecialKeywords(INode node, IReadOnlyList<ISourceFile> sourceFiles, out List<ReferencedSymbol> referencedSymbols)
        {
            // Labels
            if (DScriptUtilities.IsLabelName(node))
            {
                if (DScriptUtilities.IsJumpStatementTarget(node))
                {
                    var labelDefinition = DScriptUtilities.GetTargetLabel(node.Parent.Cast<IBreakOrContinueStatement>(), node.Cast<IIdentifier>().Text);

                    // if we have a label definition, look within its statement for references, if not, then
                    // the label is undefined and we have no results..
                    referencedSymbols = labelDefinition != null ?
                        GetLabelReferencesInNode(labelDefinition.Parent, labelDefinition) :
                        null;
                    return true;
                }

                // it is a label definition and not a target, search within the parent labeledStatement
                referencedSymbols = GetLabelReferencesInNode(node.Parent, node.Cast<IIdentifier>());
                return true;
            }

            if (node.Kind == SyntaxKind.ThisKeyword || node.Kind == SyntaxKind.ThisType)
            {
                referencedSymbols = GetReferencesForThisKeyword(node, sourceFiles);
                return true;
            }

            if (node.Kind == SyntaxKind.SuperKeyword)
            {
                // TODO: saqadri - port if necessary
                // return GetReferencesForSuperKeyword(node);
                throw new NotImplementedException();
            }

            referencedSymbols = null;
            return false;
        }

        [SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters")]
        private static List<ReferencedSymbol> GetLabelReferencesInNode(INode container, IIdentifier targetLabel)
        {
            /*var references = new List<ReferenceEntry>();
            var sourceFile = container.GetSourceFile();
            var labelName = targetLabel.Text;
            var possiblePositions = GetPossibleSymbolReferencePositions(
                sourceFile,
                labelName,
                container.GetStart(),
                container.GetEnd());*/

            // TODO: saqadri - Port
            // function getLabelReferencesInNode(container: Node, targetLabel: Identifier): ReferencedSymbol[] {
            //    const references: ReferenceEntry[] = [];
            //    const sourceFile = container.getSourceFile();
            //    const labelName = targetLabel.text;
            //    const possiblePositions = getPossibleSymbolReferencePositions(sourceFile, labelName, container.getStart(), container.getEnd());
            //    forEach(possiblePositions, position => {
            //        cancellationToken.throwIfCancellationRequested();

            // const node = getTouchingWord(sourceFile, position);
            //        if (!node || node.getWidth() !== labelName.length)
            //        {
            //            return;
            //        }

            // // Only pick labels that are either the target label, or have a target that is the target label
            //        if (node === targetLabel ||
            //            (isJumpStatementTarget(node) && getTargetLabel(node, labelName) === targetLabel))
            //        {
            //            references.push(getReferenceEntryFromNode(node));
            //        }
            //    });

            // const definition: DefinitionInfo = {
            //        containerKind: "",
            //        containerName: "",
            //        fileName: targetLabel.getSourceFile().fileName,
            //        kind: ScriptElementKind.label,
            //        name: labelName,
            //        textSpan: createTextSpanFromBounds(targetLabel.getStart(), targetLabel.getEnd())
            //    };

            // return [{ definition, references }];
            // }
            throw new NotImplementedException();
        }

        [NotNull]
        private static List<int> GetPossibleSymbolReferencePositions(
            ISourceFile sourceFile,
            string symbolName,
            int start,
            int end,
            StringComparison stringComparison = StringComparison.Ordinal)
        {
            var positions = new List<int>();

            // TODO: Cache symbol existence for files to save text search
            // Also, need to make this work for unicode escapes.

            // Be resilient in the face of a symbol with no name or zero length name
            if (string.IsNullOrEmpty(symbolName))
            {
                return positions;
            }

            var text = sourceFile.Text.Text;
            var sourceLength = text.Length;
            var symbolNameLength = symbolName.Length;

            var position = text.IndexOf(symbolName, start, stringComparison);
            while (position >= 0)
            {
                // TODO: saqadri - port
                // cancellationToken.throwIfCancellationRequested();

                // If we are past the end, stop looking
                if (position > end)
                {
                    break;
                }

                // We found a match.  Make sure it's not part of a larger word (i.e. the char
                // before and after it have to be a non-identifier char).
                var endPosition = position + symbolNameLength;

                if ((position == 0 || !Scanner.IsIdentifierPart(text.CharCodeAt(position - 1), ScriptTarget.Latest)) &&
                    (endPosition == sourceLength || !Scanner.IsIdentifierPart(text.CharCodeAt(endPosition), ScriptTarget.Latest)))
                {
                    // Found a real match. Keep searching.
                    positions.Add(position);
                }

                position = text.IndexOf(symbolName, position + symbolNameLength + 1, stringComparison);
            }

            return positions;
        }

        [SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters")]
        private static List<ReferencedSymbol> GetReferencesForThisKeyword(INode thisOrSuperKeyword, IEnumerable<ISourceFile> sourceFiles)
        {
            // TODO: saqadri - port
            // function getReferencesForThisKeyword(thisOrSuperKeyword: Node, sourceFiles: SourceFile[]): ReferencedSymbol[] {
            //    let searchSpaceNode = getThisContainer(thisOrSuperKeyword, /* includeArrowFunctions */ false);

            // // Whether 'this' occurs in a static context within a class.
            //    let staticFlag = NodeFlags.Static;

            // switch (searchSpaceNode.kind)
            //    {
            //        case SyntaxKind.MethodDeclaration:
            //        case SyntaxKind.MethodSignature:
            //            if (isObjectLiteralMethod(searchSpaceNode))
            //            {
            //                break;
            //            }
            //        // fall through
            //        case SyntaxKind.PropertyDeclaration:
            //        case SyntaxKind.PropertySignature:
            //        case SyntaxKind.Constructor:
            //        case SyntaxKind.GetAccessor:
            //        case SyntaxKind.SetAccessor:
            //            staticFlag &= searchSpaceNode.flags;
            //            searchSpaceNode = searchSpaceNode.parent; // re-assign to be the owning class
            //            break;
            //        case SyntaxKind.SourceFile:
            //            if (isExternalModule(< SourceFile > searchSpaceNode))
            //            {
            //                return undefined;
            //            }
            //        // Fall through
            //        case SyntaxKind.FunctionDeclaration:
            //        case SyntaxKind.FunctionExpression:
            //            break;
            //        // Computed properties in classes are not handled here because references to this are illegal,
            //        // so there is no point finding references to them.
            //        default:
            //            return undefined;
            //    }

            // const references: ReferenceEntry[] = [];

            // let possiblePositions: number[];
            //    if (searchSpaceNode.kind === SyntaxKind.SourceFile)
            //    {
            //        forEach(sourceFiles, sourceFile => {
            //            possiblePositions = getPossibleSymbolReferencePositions(sourceFile, "this", sourceFile.getStart(), sourceFile.getEnd());
            //            getThisReferencesInFile(sourceFile, sourceFile, possiblePositions, references);
            //        });
            //    }
            //    else
            //    {
            //        const sourceFile = searchSpaceNode.getSourceFile();
            //        possiblePositions = getPossibleSymbolReferencePositions(sourceFile, "this", searchSpaceNode.getStart(), searchSpaceNode.getEnd());
            //        getThisReferencesInFile(sourceFile, searchSpaceNode, possiblePositions, references);
            //    }

            // return [{
            //        definition:
            //        {
            //            containerKind: "",
            //            containerName: "",
            //            fileName: node.getSourceFile().fileName,
            //            kind: ScriptElementKind.variableElement,
            //            name: "this",
            //            textSpan: createTextSpanFromBounds(node.getStart(), node.getEnd())
            //        },
            //        references: references
            //    }];

            // function getThisReferencesInFile(sourceFile: SourceFile, searchSpaceNode: Node, possiblePositions: number[], result: ReferenceEntry[]): void {
            //        forEach(possiblePositions, position => {
            //            cancellationToken.throwIfCancellationRequested();

            // const node = getTouchingWord(sourceFile, position);
            //            if (!node || (node.kind !== SyntaxKind.ThisKeyword && node.kind !== SyntaxKind.ThisType))
            //            {
            //                return;
            //            }

            // const container = getThisContainer(node, /* includeArrowFunctions */ false);

            // switch (searchSpaceNode.kind)
            //            {
            //                case SyntaxKind.FunctionExpression:
            //                case SyntaxKind.FunctionDeclaration:
            //                    if (searchSpaceNode.symbol === container.symbol)
            //                    {
            //                        result.push(getReferenceEntryFromNode(node));
            //                    }
            //                    break;
            //                case SyntaxKind.MethodDeclaration:
            //                case SyntaxKind.MethodSignature:
            //                    if (isObjectLiteralMethod(searchSpaceNode) && searchSpaceNode.symbol === container.symbol)
            //                    {
            //                        result.push(getReferenceEntryFromNode(node));
            //                    }
            //                    break;
            //                case SyntaxKind.ClassExpression:
            //                case SyntaxKind.ClassDeclaration:
            //                    // Make sure the container belongs to the same class
            //                    // and has the appropriate static modifier from the original container.
            //                    if (container.parent && searchSpaceNode.symbol === container.parent.symbol && (container.flags & NodeFlags.Static) === staticFlag)
            //                    {
            //                        result.push(getReferenceEntryFromNode(node));
            //                    }
            //                    break;
            //                case SyntaxKind.SourceFile:
            //                    if (container.kind === SyntaxKind.SourceFile && !isExternalModule(< SourceFile > container))
            //                    {
            //                        result.push(getReferenceEntryFromNode(node));
            //                    }
            //                    break;
            //            }
            //        });
            //    }
            // }
            throw new NotImplementedException();
        }

        private static SemanticMeaning GetIntersectingMeaningFromDeclarations(SemanticMeaning meaning, IEnumerable<IDeclaration> declarations)
        {
            if (declarations == null)
            {
                return meaning;
            }

            SemanticMeaning lastIterationMeaning;
            do
            {
                // The result is order-sensitive, for instance if initialMeaning === Namespace, and declarations = [class, instantiated module]
                // we need to consider both as they initialMeaning intersects with the module in the namespace space, and the module
                // intersects with the class in the value space.
                // To achieve that we will keep iterating until the result stabilizes.

                // Remember the last meaning
                lastIterationMeaning = meaning;

                foreach (var declaration in declarations)
                {
                    var declarationMeaning = GetMeaningFromDeclaration(declaration);
                    if ((declarationMeaning & meaning) != SemanticMeaning.None)
                    {
                        meaning |= declarationMeaning;
                    }
                }
            }
            while (meaning != lastIterationMeaning);

            return meaning;
        }

        private static SemanticMeaning GetMeaningFromLocation(INode node)
        {
            if (node.Parent.Kind == SyntaxKind.ExportAssignment)
            {
                return SemanticMeaning.Value | SemanticMeaning.Type | SemanticMeaning.Namespace;
            }

            if (DScriptUtilities.IsInRightSideOfImport(node))
            {
                return GetMeaningFromRightHandSideOfImportEquals(node);
            }

            if (NodeUtilities.IsDeclarationName(node) != null)
            {
                return GetMeaningFromDeclaration(node.Parent);
            }

            if (DScriptUtilities.IsTypeReference(node))
            {
                return SemanticMeaning.Type;
            }

            if (DScriptUtilities.IsNamespaceReference(node))
            {
                return SemanticMeaning.Namespace;
            }

            return SemanticMeaning.Value;
        }

        private static SemanticMeaning GetMeaningFromRightHandSideOfImportEquals(INode node)
        {
            Debug.Assert(node.Kind == SyntaxKind.Identifier, "Expect identifier");

            // import a = |b|; // Namespace
            //     import a = |b.c|; // Value, type, namespace
            //     import a = |b.c|.d; // Namespace
            if (node.Parent?.Kind == SyntaxKind.QualifiedName &&
                node.Parent.Cast<IQualifiedName>().Right.ResolveUnionType() == node.ResolveUnionType() &&
                node.Parent.Parent?.Kind == SyntaxKind.ImportEqualsDeclaration)
            {
                return SemanticMeaning.Value | SemanticMeaning.Type | SemanticMeaning.Namespace;
            }

            return SemanticMeaning.Namespace;
        }

        private static SemanticMeaning GetMeaningFromDeclaration(INode node)
        {
            switch (node.Kind)
            {
                case SyntaxKind.Parameter:
                case SyntaxKind.VariableDeclaration:
                case SyntaxKind.BindingElement:
                case SyntaxKind.PropertyDeclaration:
                case SyntaxKind.PropertySignature:
                case SyntaxKind.PropertyAssignment:
                case SyntaxKind.ShorthandPropertyAssignment:
                case SyntaxKind.EnumMember:
                case SyntaxKind.MethodDeclaration:
                case SyntaxKind.MethodSignature:
                case SyntaxKind.Constructor:
                case SyntaxKind.GetAccessor:
                case SyntaxKind.SetAccessor:
                case SyntaxKind.FunctionDeclaration:
                case SyntaxKind.FunctionExpression:
                case SyntaxKind.ArrowFunction:
                case SyntaxKind.CatchClause:
                    return SemanticMeaning.Value;

                case SyntaxKind.TypeParameter:
                case SyntaxKind.InterfaceDeclaration:
                case SyntaxKind.TypeAliasDeclaration:
                case SyntaxKind.TypeLiteral:
                    return SemanticMeaning.Type;

                case SyntaxKind.ClassDeclaration:
                case SyntaxKind.EnumDeclaration:
                    return SemanticMeaning.Value | SemanticMeaning.Type;

                case SyntaxKind.ModuleDeclaration:
                    if (DScriptUtilities.IsAmbientModule(node))
                    {
                        return SemanticMeaning.Namespace | SemanticMeaning.Value;
                    }
                    else if (Binder.GetModuleInstanceState(node) == ModuleInstanceState.Instantiated)
                    {
                        return SemanticMeaning.Namespace | SemanticMeaning.Value;
                    }
                    else
                    {
                        return SemanticMeaning.Namespace;
                    }

                case SyntaxKind.NamedImports:
                case SyntaxKind.ImportSpecifier:
                case SyntaxKind.ImportEqualsDeclaration:
                case SyntaxKind.ImportDeclaration:
                case SyntaxKind.ExportAssignment:
                case SyntaxKind.ExportDeclaration:
                    return SemanticMeaning.Value | SemanticMeaning.Type | SemanticMeaning.Namespace;

                // An external module can be a Value
                case SyntaxKind.SourceFile:
                    return SemanticMeaning.Namespace | SemanticMeaning.Value;
            }

            return SemanticMeaning.Value | SemanticMeaning.Type | SemanticMeaning.Namespace;
        }

        private static INode GetSymbolScope(ISymbol symbol)
        {
            // If this is the symbol of a named function expression or named class expression,
            // then named references are limited to its own scope.
            var valueDeclaration = symbol.ValueDeclaration;

            if (valueDeclaration?.Kind == SyntaxKind.FunctionExpression || valueDeclaration?.Kind == SyntaxKind.ClassExpression)
            {
                return valueDeclaration;
            }

            // If this is private property or method, the scope is the containing class
            if ((symbol.Flags & (SymbolFlags.Property | SymbolFlags.Method)) != SymbolFlags.None)
            {
                IDeclaration privateDeclaration = null;
                foreach (var declaration in symbol.GetDeclarations())
                {
                    if ((declaration.Flags & NodeFlags.Private) != NodeFlags.None)
                    {
                        privateDeclaration = declaration;
                        break;
                    }
                }

                if (privateDeclaration != null)
                {
                    return NodeUtilities.GetAncestor(privateDeclaration, SyntaxKind.ClassDeclaration);
                }
            }

            // If the symbol is an import we would like to find it if we are looking for what it imports.
            // So consider it visible outside its declaration scope.
            if ((symbol.Flags & SymbolFlags.Alias) != SymbolFlags.None)
            {
                return null;
            }

            // If this symbol is visible from its parent container, e.g. exported, then bail out
            // if symbol correspond to the union property - bail out
            if (symbol.Parent != null ||
                (symbol.Flags & SymbolFlags.SyntheticProperty) != SymbolFlags.None)
            {
                return null;
            }

            INode scope = null;
            var declarations = symbol.GetDeclarations();

            foreach (var declaration in declarations)
            {
                var container = DScriptUtilities.GetContainerNode(declaration);
                if (container == null)
                {
                    return null;
                }

                if (scope != null && scope.ResolveUnionType() != container.ResolveUnionType())
                {
                    // Different declaration have different containers; bail out
                    return null;
                }

                if (container.Kind == SyntaxKind.SourceFile &&
                    !SourceFileExtensions.IsExternalModule(container.Cast<ISourceFile>()))
                {
                    // This is a global variable and not an external module, any declaration defined
                    // within this scope is visible outside the file
                    return null;
                }

                // The search scope is the container node
                scope = container;
            }

            return scope;
        }

        private List<ReferencedSymbol> GetReferencesInNode(
            INode container,
            ISymbol searchSymbol,
            string searchText,
            INode searchLocation,
            SemanticMeaning searchMeaning,
            bool findInStrings,
            bool findInComments)
        {
            Dictionary<int, int> symbolToIndex = new Dictionary<int, int>();
            var result = new List<ReferencedSymbol>();

            var sourceFile = container.GetSourceFile();

            var possiblePositions = GetPossibleSymbolReferencePositions(
                sourceFile,
                searchText,
                start: NodeUtilities.GetTokenPosOfNode(container, sourceFile),
                end: container.End);

            if (possiblePositions.Count != 0)
            {
                // Build the set of symbols to search for, initially it has only the current symbol
                var searchSymbols = PopulateSearchSymbolSet(searchSymbol, searchLocation);

                foreach (var position in possiblePositions)
                {
                    // Get the property node at position, and check if it is a valid reference position
                    if (!DScriptNodeUtilities.TryGetNodeAtPosition(
                        sourceFile,
                        position,
                        isNodeAcceptable: (n) => n.Kind.IsPropertyName(),
                        nodeAtPosition: out var referenceLocation) ||
                        !IsValidReferencePosition(referenceLocation, searchText))
                    {
                        // This wasn't the start of a token.  Check to see if it might be a
                        // match in a comment or string if that's what the caller is asking
                        // for.
                        if ((findInStrings && DScriptUtilities.IsInString(sourceFile, position)) ||
                            (findInComments && IsInNonReferenceComment(sourceFile, position)))
                        {
                            // In the case where we're looking inside comments/strings, we don't have
                            // an actual definition.  So just use 'undefined' here.  Features like
                            // 'Rename' won't care (as they ignore the definitions), and features like
                            // 'FindReferences' will just filter out these results.
                            result.Add(ReferencedSymbol.Create(sourceFile, position, searchText));
                        }

                        continue;
                    }

                    if ((GetMeaningFromLocation(referenceLocation) & searchMeaning) == SemanticMeaning.None)
                    {
                        continue;
                    }

                    var referenceSymbol = TypeChecker.GetSymbolAtLocation(referenceLocation) ?? referenceLocation.Symbol;
                    if (referenceSymbol != null)
                    {
                        var referenceSymbolDeclaration = referenceSymbol.ValueDeclaration;
                        var shorthandValueSymbol = TypeChecker.GetShorthandAssignmentValueSymbol(referenceSymbolDeclaration);
                        var relatedSymbol = GetRelatedSymbol(searchSymbols, referenceSymbol, referenceLocation);

                        if (relatedSymbol != null)
                        {
                            var referencedSymbol = GetReferencedSymbol(relatedSymbol, symbolToIndex, result);
                            referencedSymbol.References.Add(GetReferenceEntryFromNode(referenceLocation));
                        }
                        else if ((referenceSymbol.Flags & SymbolFlags.Transient) == SymbolFlags.None && searchSymbols.IndexOf(shorthandValueSymbol) >= 0)
                        {
                            /* Because in short-hand property assignment, an identifier which stored as name of the short-hand property assignment
                             * has two meaning : property name and property value. Therefore when we do findAllReference at the position where
                             * an identifier is declared, the language service should return the position of the variable declaration as well as
                             * the position in short-hand property assignment excluding property accessing. However, if we do findAllReference at the
                             * position of property accessing, the referenceEntry of such position will be handled in the first case.
                             */
                            var referencedSymbol = GetReferencedSymbol(shorthandValueSymbol, symbolToIndex, result);
                            referencedSymbol.References.Add(GetReferenceEntryFromNode(referenceSymbolDeclaration.Name));
                        }
                    }
                }
            }

            return result;
        }

        private ISymbol GetRelatedSymbol(List<ISymbol> searchSymbols, ISymbol referenceSymbol, INode referenceLocation)
        {
            if (searchSymbols.IndexOf(referenceSymbol) >= 0)
            {
                return referenceSymbol;
            }

            // If the reference symbol is an alias, check if what it is aliasing is one of the search
            // symbols.
            if (DScriptUtilities.IsImportSpecifierSymbol(referenceSymbol))
            {
                var aliasedSymbol = TypeChecker.GetAliasedSymbol(referenceSymbol);
                if (searchSymbols.IndexOf(aliasedSymbol) >= 0)
                {
                    return aliasedSymbol;
                }
            }

            // For export specifiers, it can be a local symbol, e.g.
            //     import {a} from "mod";
            //     export {a as somethingElse}
            // We want the local target of the export (i.e. the import symbol) and not the final target (i.e. "mod".a)
            if (referenceLocation.Parent?.Kind == SyntaxKind.ExportSpecifier)
            {
                // TODO: saqadri - port
                // var aliasedSymbol = m_typeChecker.GetExportSpecifierLocalTargetSymbol(< ExportSpecifier > referenceLocation.parent);
                // if (searchSymbols.indexOf(aliasedSymbol) >= 0)
                // {
                //    return aliasedSymbol;
                // }
            }

            // If the reference location is in an object literal, try to get the contextual type for the
            // object literal, lookup the property symbol in the contextual type, and use this symbol to
            // compare to our searchSymbol
            if (DScriptUtilities.IsNameOfPropertyAssignment(referenceLocation))
            {
                foreach (var contextualSymbol in DScriptUtilities.GetPropertySymbolsFromContextualType(referenceLocation, TypeChecker) ?? Enumerable.Empty<ISymbol>())
                {
                    foreach (var s in TypeChecker.GetRootSymbols(contextualSymbol))
                    {
                        if (searchSymbols.IndexOf(s) >= 0)
                        {
                            return s;
                        }
                    }
                }
            }

            // Unwrap symbols to get to the root (e.g. transient symbols as a result of widening)
            // Or a union property, use its underlying unioned symbols
            foreach (var rootSymbol in TypeChecker.GetRootSymbols(referenceSymbol) ?? Enumerable.Empty<ISymbol>())
            {
                // if it is in the list, then we are done
                if (searchSymbols.IndexOf(rootSymbol) >= 0)
                {
                    return rootSymbol;
                }

                // Finally, try all properties with the same name in any type the containing type extended or implemented, and
                // see if any is in the list
                if ((rootSymbol.Parent?.Flags & (SymbolFlags.Class | SymbolFlags.Interface)) != SymbolFlags.None)
                {
                    // TODO: saqadri - port
                    // var result = new List<ISymbol>();
                    // GetPropertySymbolsFromBaseTypes(rootSymbol.parent, rootSymbol.getName(), result, /*previousIterationSymbolsCache*/ {});
                    // return forEach(result, s => searchSymbols.indexOf(s) >= 0 ? s : undefined);
                }
            }

            return null;
        }

        private static ReferenceEntry GetReferenceEntryFromNode(INode node)
        {
            var sourceFile = node.GetSourceFile();
            Contract.Assert(sourceFile != null);

            var start = NodeUtilities.GetTokenPosOfNode(node, sourceFile);
            var end = node.End;

            if (node.Kind == SyntaxKind.StringLiteral)
            {
                start++;
                end--;
            }

            return new ReferenceEntry()
            {
                FileName = sourceFile.FileName,
                SourceFile = sourceFile,
                TextSpan = new TextSpan() { Start = start, Length = end - start },
                IsWriteAccess = IsWriteAccess(node),
            };
        }

        private static bool IsWriteAccess(INode node)
        {
            if (node.Kind == SyntaxKind.Identifier && NodeUtilities.IsDeclarationName(node) != null)
            {
                return true;
            }

            var parent = node.Parent;
            if (parent != null)
            {
                if (parent.Kind == SyntaxKind.PostfixUnaryExpression || parent.Kind == SyntaxKind.PrefixUnaryExpression)
                {
                    return true;
                }

                if (parent.Kind == SyntaxKind.BinaryExpression && parent.Cast<IBinaryExpression>().Left.ResolveUnionType() == node.ResolveUnionType())
                {
                    var @operator = parent.Cast<IBinaryExpression>().OperatorToken.Kind;
                    return @operator >= SyntaxKind.FirstAssignment && @operator <= SyntaxKind.LastAssignment;
                }
            }

            return false;
        }

        private ReferencedSymbol GetReferencedSymbol(ISymbol symbol, Dictionary<int, int> symbolToIndex, List<ReferencedSymbol> result)
        {
            var symbolId = TypeChecker.GetSymbolId(symbol);
            
            if (!symbolToIndex.TryGetValue(symbolId, out var index))
            {
                index = result.Count;
                symbolToIndex.Add(symbolId, index);
                result.Add(
                    new ReferencedSymbol()
                    {
                        Definition = GetDefinition(symbol),
                        References = new List<ReferenceEntry>(),
                    });
            }

            return result[index];
        }

        private static DefinitionInfo GetDefinition(ISymbol symbol)
        {
            // TODO: saqadri - Port fully
            // const info = getSymbolDisplayPartsDocumentationAndSymbolKind(symbol, node.getSourceFile(), getContainerNode(node), node);
            // const name = map(info.displayParts, p => p.text).join("");
            var declarations = symbol.Declarations;
            if (declarations.IsNullOrEmpty())
            {
                return default(DefinitionInfo);
            }

            return new DefinitionInfo()
            {
                ContainerKind = string.Empty,
                ContainerName = string.Empty,

                // TODO: saqadri - port
                // name
                // kind: info.symbolKind
                FileName = declarations[0].GetSourceFile().FileName,
                TextSpan = new TextSpan() { Start = NodeUtilities.GetTokenPosOfNode(declarations[0], declarations[0].GetSourceFile()), Length = 0 },
            };
        }

        private static bool IsInNonReferenceComment(ISourceFile sourceFile, int position)
        {
            return DScriptUtilities.IsInCommentHelper(sourceFile, position, (c) => IsNonReferenceComment(c, sourceFile));
        }

        private static readonly Regex s_tripleSlashDirectivePrefixRegex = new Regex(@"^\/\/\/\s *<");

        private static bool IsNonReferenceComment(ICommentRange commentRange, ISourceFile sourceFile)
        {
            var commentText = sourceFile.Text.Substring(commentRange.Pos, commentRange.End);
            return s_tripleSlashDirectivePrefixRegex.IsMatch(commentText);
        }

        private List<ISymbol> PopulateSearchSymbolSet(ISymbol symbol, INode location)
        {
            // The search set contains at least the current symbol
            var result = new List<ISymbol>() { symbol };

            // If the symbol is an alias, add what it aliases to the list
            if (DScriptUtilities.IsImportSpecifierSymbol(symbol))
            {
                result.Add(TypeChecker.GetAliasedSymbol(symbol));
            }

            // For export specifiers, the exported name can be referring to a local symbol, e.g.:
            //     import {a} from "mod";
            //     export {a as somethingElse}
            // We want the *local* declaration of 'a' as declared in the import,
            // *not* as declared within "mod" (or farther)
            if (location.Parent?.Kind == SyntaxKind.ExportSpecifier)
            {
                // TODO: saqadri - Port
                // result.Add(m_typeChecker.GetExportSpecifierLocalTargetSymbol(< ExportSpecifier > location.parent));
            }

            // If the location is in a context sensitive location (i.e. in an object literal) try
            // to get a contextual type for it, and add the property symbol from the contextual
            // type to the search set
            if (DScriptUtilities.IsNameOfPropertyAssignment(location))
            {
                foreach (var contextualSymbol in DScriptUtilities.GetPropertySymbolsFromContextualType(location, TypeChecker) ?? Enumerable.Empty<ISymbol>())
                {
                    result.AddRange(TypeChecker.GetRootSymbols(contextualSymbol));
                }

                /* Because in short-hand property assignment, location has two meaning : property name and as value of the property
                 * When we do findAllReference at the position of the short-hand property assignment, we would want to have references to position of
                 * property name and variable declaration of the identifier.
                 * Like in below example, when querying for all references for an identifier 'name', of the property assignment, the language service
                 * should show both 'name' in 'obj' and 'name' in variable declaration
                 *      const name = "Foo";
                 *      const obj = { name };
                 * In order to do that, we will populate the search set with the value symbol of the identifier as a value of the property assignment
                 * so that when matching with potential reference symbol, both symbols from property declaration and variable declaration
                 * will be included correctly.
                 */
                var shorthandValueSymbol = TypeChecker.GetShorthandAssignmentValueSymbol(location.Parent);
                if (shorthandValueSymbol != null)
                {
                    result.Add(shorthandValueSymbol);
                }
            }

            // If the symbol.valueDeclaration is a property parameter declaration,
            // we should include both parameter declaration symbol and property declaration symbol
            // Parameter Declaration symbol is only visible within function scope, so the symbol is stored in constructor.locals.
            // Property Declaration symbol is a member of the class, so the symbol is stored in its class Declaration.symbol.members
            if (symbol.ValueDeclaration?.Kind == SyntaxKind.Parameter &&
                NodeUtilities.IsParameterPropertyDeclaration(symbol.ValueDeclaration.Cast<IParameterDeclaration>()))
            {
                // TODO: saqadri - port
                // result.Add(m_typeChecker.GetSymbolsOfParameterPropertyDeclaration(< ParameterDeclaration > symbol.valueDeclaration, symbol.name));
            }

            // If this is a union property, add all the symbols from all its source symbols in all unioned types.
            // If the symbol is an instantiation from a another symbol (e.g. widened symbol) , add the root the list
            foreach (var rootSymbol in TypeChecker.GetRootSymbols(symbol) ?? Enumerable.Empty<ISymbol>())
            {
                if (rootSymbol != symbol)
                {
                    result.Add(rootSymbol);
                }

                // Add symbol of properties/methods of the same name in base classes and implemented interfaces definitions
                if (rootSymbol.Parent != null && (rootSymbol.Parent.Flags & (SymbolFlags.Class | SymbolFlags.Interface)) != SymbolFlags.None)
                {
                    // TODO: saqadri - port
                    // GetPropertySymbolsFromBaseTypes(
                    //    rootSymbol.Parent,
                    //    rootSymbol.GetName(),
                    //    result,
                    //    /*previousIterationSymbolsCache*/ { });
                }
            }

            return result;
        }

        private static bool IsValidReferencePosition(INode node, string searchSymbolName)
        {
            if (node == null)
            {
                return false;
            }

            var nodeWidth = node.End - NodeUtilities.GetTokenPosOfNode(node, node.GetSourceFile());

            // Compare the length so we filter out strict superstrings of the symbol we are looking for
            switch (node.Kind)
            {
                case SyntaxKind.Identifier:
                    return nodeWidth == searchSymbolName.Length;

                case SyntaxKind.StringLiteral:
                    if (DScriptUtilities.IsLiteralNameOfPropertyDeclarationOrIndexAccess(node) ||
                        DScriptUtilities.IsNameOfExternalModuleImportOrDeclaration(node))
                    {
                        // For string literals we have two additional chars for the quotes
                        return nodeWidth == searchSymbolName.Length + 2;
                    }

                    break;

                case SyntaxKind.NumericLiteral:
                    if (DScriptUtilities.IsLiteralNameOfPropertyDeclarationOrIndexAccess(node))
                    {
                        return nodeWidth == searchSymbolName.Length;
                    }

                    break;
            }

            return false;
        }

        [SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters")]
        private static string GetInternedName(ISymbol symbol, INode location, IEnumerable<IDeclaration> declarations)
        {
            // If this is an export or import specifier it could have been renamed using the 'as' syntax.
            // If so we want to search for whatever under the cursor.
            if (DScriptUtilities.IsImportOrExportSpecifierName(location))
            {
                return location.GetText();
            }

            // Try to get the local symbol if we're dealing with an 'export default'
            // since that symbol has the "true" name.
            var localExportDefaultSymbol = DScriptUtilities.GetLocalSymbolForExportDefault(symbol);
            symbol = localExportDefaultSymbol ?? symbol;

            return StripQuotes(symbol.Name);
        }

        private static string StripQuotes(string name)
        {
            var length = name.Length;
            if (length >= 2 &&
                name[0] == name[length - 1] &&
                (name[0] == '"' || name[0] == '\''))
            {
                return name.Substring(1, length - 1);
            }

            return name;
        }

        private Dictionary<string, int> GetNameTable(ISourceFile sourceFile)
        {
            return m_nameTables.GetOrAdd(sourceFile, sf => InitializeNameTable(sf));
        }

        private static Dictionary<string, int> InitializeNameTable(ISourceFile sourceFile)
        {
            var nameTable = new Dictionary<string, int>();
            Walk(sourceFile, nameTable);

            return nameTable;
        }

        private static void Walk(INode node, Dictionary<string, int> nameTable)
        {
            string name;

            switch (node.Kind)
            {
                case SyntaxKind.Identifier:
                    name = node.Cast<IIdentifier>().Text;
                    nameTable[name] = nameTable.ContainsKey(name) ? node.Pos : -1;
                    break;

                case SyntaxKind.StringLiteral:
                case SyntaxKind.NumericLiteral:
                    // We want to store any numbers/strings if they were a name that could be
                    // related to a declaration.  So, if we have 'import x = require("something")'
                    // then we want 'something' to be in the name table.  Similarly, if we have
                    // "a['propname']" then we want to store "propname" in the name table.
                    if (NodeUtilities.IsDeclarationName(node) != null ||
                        node.Parent.Kind == SyntaxKind.ExternalModuleReference ||
                        DScriptUtilities.IsArgumentOfElementAccessExpression(node))
                    {
                        name = node.Cast<ILiteralExpression>().Text;
                        nameTable[name] = nameTable.ContainsKey(name) ? node.Pos : -1;
                    }

                    break;

                default:
                    // TODO: verify correctness
                    NodeWalker.ForEachChild(node, (n) =>
                        {
                            Walk(n, nameTable);
                            return false;
                        });
                    break;
            }
        }

        private static Result<Location[], ResponseError> ConvertReferences(IReadOnlyList<ReferencedSymbol> referencedSymbols)
        {
            if (referencedSymbols == null || referencedSymbols.Count == 0)
            {
                return SilentError();
            }

            var referenceEntries = new List<ReferenceEntry>();

            foreach (var referencedSymbol in referencedSymbols)
            {
                referenceEntries.AddRange(referencedSymbol.References);
            }

            var locations = new List<Location>();

            foreach (var referenceEntry in referenceEntries)
            {
                locations.Add(new Location()
                {
                    Range = referenceEntry.TextSpan.ToRange(referenceEntry.SourceFile),
                    Uri = referenceEntry.SourceFile.ToUri().ToString(),
                });
            }

            return Success(locations.ToArray());
        }

        private static Result<Location[], ResponseError> Success(Location[] locations)
        {
            return Result<Location[], ResponseError>.Success(locations);
        }

        private static Result<Location[], ResponseError> SilentError()
        {
            return Result<Location[], ResponseError>.Success(new Location[] { });
        }
    }

    [Flags]
    internal enum SemanticMeaning
    {
        None = 0x0,
        Value = 0x1,
        Type = 0x2,
        Namespace = 0x4,
        All = Value | Type | Namespace,
    }

    internal struct ReferenceEntry
    {
        public ITextSpan TextSpan { get; set; }

        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        public string FileName { get; set; }

        public ISourceFile SourceFile { get; set; }

        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        public bool IsWriteAccess { get; set; }
    }

    internal struct DefinitionInfo
    {
        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        public string FileName { get; set; }

        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        public ITextSpan TextSpan { get; set; }

        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        public string Kind { get; set; }

        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        public string Name { get; set; }

        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        public string ContainerKind { get; set; }

        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        public string ContainerName { get; set; }
    }

    internal struct ReferencedSymbol
    {
        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        public DefinitionInfo Definition { get; set; }

        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
#pragma warning disable CA2227 // Collection properties should be read only
        public List<ReferenceEntry> References { get; set; }
#pragma warning restore CA2227 // Collection properties should be read only

        public static ReferencedSymbol Create(ISourceFile referencedFile, int position, string searchText)
        {
            return new ReferencedSymbol()
                   {
                       // TODO: Definition = null,
                       References = new List<ReferenceEntry>()
                                    {
                                        new ReferenceEntry()
                                        {
                                            FileName = referencedFile.FileName,
                                            SourceFile = referencedFile,
                                            TextSpan = new TextSpan() { Start = position, Length = searchText.Length },
                                            IsWriteAccess = false,
                                        },
                                    },
                   };
        }
    }
}
