// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Utilities;
using JetBrains.Annotations;
using TypeScript.Net.Core;
using TypeScript.Net.Diagnostics;
using TypeScript.Net.DScript;
using TypeScript.Net.Extensions;
using TypeScript.Net.Scanning;
using TypeScript.Net.Types;
using static BuildXL.Utilities.FormattableStringEx;
using static TypeScript.Net.Core.CoreUtilities;

#pragma warning disable 1591 // disabling warning about missing API documentation; TODO: Remove this line and write documentation!

namespace TypeScript.Net.Parsing
{
    /// <summary>
    /// TypeScript parser migrated from the typescript codebase.
    /// </summary>
    public class Parser
    {
        private enum ParsingContext
        {
            SourceElements,            // Elements in source file
            BlockStatements,           // Statements in block
            SwitchClauses,             // Clauses in switch statement
            SwitchClauseStatements,    // Statements in switch clause
            TypeMembers,               // Members in interface or type literal
            ClassMembers,              // Members in class declaration
            EnumMembers,               // Members in enum declaration
            HeritageClauseElement,     // Elements in a heritage clause
            VariableDeclarations,      // Variable declarations in variable statement
            ObjectBindingElements,     // Binding elements in object binding list
            ArrayBindingElements,      // Binding elements in array binding list
            ArgumentExpressions,       // Expressions in argument list
            ObjectLiteralMembers,      // Members in object literal
            JsxAttributes,             // Attributes in jsx element
            JsxChildren,               // Things between opening and closing JSX tags
            ArrayLiteralMembers,       // Members in array literal
            Parameters,                // Parameters in parameter list
            TypeParameters,            // Type parameters in type parameter list
            TypeArguments,             // Type arguments in type argument list
            TupleElementTypes,         // Element types in tuple element type list
            HeritageClauses,           // Heritage clauses for a class or interface declaration.
            ImportOrExportSpecifiers,  // Named import clause's import specifier list
            JsDocFunctionParameters,
            JsDocTypeArguments,
            JsDocRecordMembers,
            JsDocTupleTypes,
            Count, // Number of parsing contexts
        }

        /// <summary>
        /// Cache for empty literals for tagged template expression.
        /// </summary>
        private ITemplateLiteralFragment m_emptyTemplateHeadLiteral;

        /// <summary>
        /// Cache for identifiers used in p``, d``, f`` etc literals.
        /// </summary>
        private readonly Dictionary<char, IIdentifier> m_identifiersCache = new Dictionary<char, IIdentifier>();

        private readonly ParserContextFlags m_disallowInAndDecoratorContext = ParserContextFlags.DisallowIn | ParserContextFlags.Decorator;

        // Not every file has diagnostics. Using lazy to avoid unnecessary allocations.
        private readonly Lazy<List<Diagnostic>> m_parseDiagnostics = new Lazy<List<Diagnostic>>(() => new List<Diagnostic>());

        /// <summary>
        /// Share a single scanner across all calls to parse a source file.  This helps speed things
        /// up by avoiding the cost of creating/compiling scanners over and over again.
        /// </summary>
        protected Scanner m_scanner;

        /// <summary>
        /// Flags that dictate what parsing context we're in.  For example:
        /// Whether or not we are in strict parsing mode.  All that changes in strict parsing mode is
        /// that some tokens that would be considered identifiers may be considered keywords.
        ///
        /// When adding more parser context flags, consider which is the more common case that the
        /// flag will be in.  This should be the 'false' state for that flag.  The reason for this is
        /// that we don't store data in our nodes unless the value is in the *non-default* state.  So,
        /// for example, more often than code 'allows-in' (or doesn't 'disallow-in').  We opt for
        /// 'disallow-in' set to 'false'.  Otherwise, if we had 'allowsIn' set to 'true', then almost
        /// all nodes would need extra state on them to store this info.
        ///
        /// Note:  'allowIn' and 'allowYield' track 1:1 with the [in] and [yield] concepts in the ES6
        /// grammar specification.
        ///
        /// An important thing about these context concepts.  By default they are effectively inherited
        /// while parsing through every grammar production.  i.e., if you don't change them, then when
        /// you parse a sub-production, it will have the same context values as the parent production.
        /// This is great most of the time.  After all, consider all the 'expression' grammar productions
        /// and how nearly all of them pass along the 'in' and 'yield' context values:
        ///
        /// EqualityExpression[In, Yield] :
        ///      RelationalExpression[?In, ?Yield]
        ///      EqualityExpression[?In, ?Yield] == RelationalExpression[?In, ?Yield]
        ///      EqualityExpression[?In, ?Yield] != RelationalExpression[?In, ?Yield]
        ///      EqualityExpression[?In, ?Yield] == RelationalExpression[?In, ?Yield]
        ///      EqualityExpression[?In, ?Yield] != RelationalExpression[?In, ?Yield]
        ///
        /// Where you have to be careful is then understanding what the points are in the grammar
        /// where the values are *not* passed along.  For example:
        ///
        /// SingleNameBinding[Yield,GeneratorParameter]
        ///      [+GeneratorParameter]BindingIdentifier[Yield] Initializer[In]opt
        ///      [~GeneratorParameter]BindingIdentifier[?Yield]Initializer[In, ?Yield]opt
        ///
        /// Here this is saying that if the GeneratorParameter context flag is set, that we should
        /// explicitly set the 'yield' context flag to false before calling into the BindingIdentifier
        /// and we should explicitly unset the 'yield' context flag before calling into the Initializer.
        /// production.  Conversely, if the GeneratorParameter context flag is not set, then we
        /// should leave the 'yield' context flag alone.
        ///
        /// Getting this all correct is tricky and requires careful reading of the grammar to
        /// understand when these values should be changed versus when they should be inherited.
        ///
        /// it Note should not be necessary to save/restore these flags during speculative/lookahead
        /// parsing.  These context flags are naturally stored and restored through normal recursive
        /// descent parsing and unwinding.
        /// </summary>
        private ParserContextFlags m_contextFlags;

        private int m_fakeScriptIdCounter;

        /// <nodoc />
        protected int m_identifierCount;

        private int m_nodeCount;

        /// <summary>
        /// Whether or not we've had a parse error since creating the last AST node.  If we have
        /// encountered an error, it will be stored on the next AST node we create.  Parse errors
        /// can be broken down into three categories:
        ///
        /// 1) An error that occurred during scanning.  For example, an unterminated literal, or a
        ///    character that was completely not understood.
        ///
        /// 2) A token was expected, but was not present.  This type of error is commonly produced
        ///    by the 'parseExpected' function.
        ///
        /// 3) A token was present that no parsing  was able to consume.  This type of error
        ///    only occurs in the 'abortParsingListOrMoveToNextToken'  when the parser
        ///    decides to skip the token.
        ///
        /// In all of these cases, we want to mark the next node as having had an error before it.
        /// With this mark, we can know in incremental settings if this node can be reused, or if
        /// we have to reparse it.  If we don't keep this information around, we may just reuse the
        /// node.  in that event we would then not produce the same errors as we did before, causing
        /// significant confusion problems.
        ///
        /// it Note is necessary that this value be saved/restored during speculative/lookahead
        /// parsing.  During lookahead parsing, we will often create a node.  That node will have
        /// this value attached, and then this value will be set back to 'false'.  If we decide to
        /// rewind, we must get back to the same value we had prior to the lookahead.
        ///
        /// any Note errors at the end of the file that do not precede a regular node, should get
        /// attached to the EOF token.
        /// </summary>
        private bool m_parseErrorBeforeNextFinishedNode;

        private ParsingContext m_parsingContext;

        /// <nodoc />
        protected SourceFile m_sourceFile;

        private bool m_isScriptFile;

        private TextSource m_sourceText;
        private ITextSourceProvider m_sourceTextProvider;
        private IncrementalParser.SyntaxCursor m_syntaxCursor;

        /// <summary>
        /// Current token.
        /// </summary>
        protected SyntaxKind m_token;

        // DScript-specific parsing options. May be null.
        private ParsingOptions m_parsingOptions;

        /// <nodoc />
        public Parser()
        {
        }

        private static readonly List<Diagnostic> s_emptyList = new List<Diagnostic>();

        /// <nodoc />
        public IReadOnlyList<Diagnostic> ParseDiagnostics => m_parseDiagnostics.IsValueCreated ? m_parseDiagnostics.Value : s_emptyList;

        /// <nodoc />
        public ISourceFile ParseSourceFile(string fileName, TextSource sourceText, ScriptTarget languageVersion, IncrementalParser.SyntaxCursor syntaxCursor,
            ParsingOptions parsingOptions, bool setParentNodes = false)
        {
            m_parsingOptions = parsingOptions;
            m_sourceTextProvider = m_sourceTextProvider ?? new TextSourceProvider(sourceText);

            /* DScript specific. The scanner can be set to not skip trivia based on configuration options.
             TODO: the parser is modified to preserve comments (only when skip trivia is off) and line breaks only for *some* key cases:
             - comments as statements
             - comments in enums, interfaces, array literals and object literals.
             - comments where expressions (IExpression) are expected

             New lines are preserved for statements.

             If skip trivia is off, a comment showing up in a non-supported place will fail parsing
             If skip trivia is on, there is no change in the parsing behavior.

             If this ever needs to be generalized, consider an approach were each (real) node can be attached with trivia information
            */

            bool allowBackslashesInPathInterpolation = m_parsingOptions?.AllowBackslashesInPathInterpolation ?? false;
            using (var wrapper = Utilities.Pools.TextBuilderPool.GetInstance())
            {
                m_scanner = new Scanner(
                    ScriptTarget.Latest,
                    preserveTrivia: m_parsingOptions?.PreserveTrivia ?? false,
                    allowBackslashesInPathInterpolation: allowBackslashesInPathInterpolation,
                    textBuilder: wrapper.Instance,
                    text: sourceText);

                // For DScript, 'isJavaScriptFile' is always false.
                var isJavaScriptFile = false;
                InitializeState(fileName, sourceText, languageVersion, isJavaScriptFile, syntaxCursor);

                var result = (SourceFile)ParseSourceFileWorker(fileName, languageVersion, setParentNodes, allowBackslashesInPathInterpolation);

                result.SetLineMap(m_scanner.LineMap.ToArray());
                return result;
            }
        }

        /// <nodoc/>
        public ISourceFile ParseSourceFileContent(TextSource sourceText)
        {
            Contract.Requires(sourceText != null);
            Contract.Ensures(Contract.Result<ISourceFile>() != null);

            return ParseSourceFileContent("FakeSourceFile.tsc", sourceText, new TextSourceProvider(sourceText), ParsingOptions.DefaultParsingOptions);
        }

        /// <nodoc/>
        public ISourceFile ParseSourceFileContent(
            string fileName,
            TextSource sourceText,
            ITextSourceProvider sourceTextProvider,
            ParsingOptions parsingOptions = null)
        {
            Contract.Requires(sourceText != null);
            Contract.Requires(sourceTextProvider != null);
            Contract.Ensures(Contract.Result<ISourceFile>() != null);

            m_sourceTextProvider = sourceTextProvider;

            return ParseSourceFile(fileName, sourceText, ScriptTarget.Es2015, syntaxCursor: null, setParentNodes: false,
                parsingOptions: parsingOptions);
        }

        /// <nodoc/>
        public ISourceFile ParseSourceFileContent(string fileName, TextSource sourceText, ParsingOptions parsingOptions = null)
        {
            return ParseSourceFileContent(fileName, sourceText, new TextSourceProvider(sourceText), parsingOptions);
        }

        /// <nodoc/>
        public ISourceFile ParseSourceFileWorker(string fileName, ScriptTarget languageVersion, bool setParentNodes, bool allowBackslashesInPathInterpolation)
        {
            m_sourceFile = CreateSourceFile(fileName, languageVersion, allowBackslashesInPathInterpolation);
            m_isScriptFile = m_sourceFile.IsScriptFile();
            m_scanner.SetSourceFile(m_sourceFile);

            // Prime the scanner.
            m_token = NextToken();
            ProcessReferenceComments();

            // DScript-specific. We look if there is any top-level statement that declares a
            // DScript qualifier value, and we reflect that in the source file.
            // This avoids traversing all top-level statements again, after parsing.
            m_sourceFile.Statements =
                ParseListAndFindFirstMatch(
                    this,
                    ParsingContext.SourceElements,
                    p => p.ParseStatement(),
                    (parser, n) => parser.IsQualifierDeclaration(n),
                    out INode qualifierDeclaration);

            m_sourceFile.DeclaresRootQualifier = qualifierDeclaration != null;
            m_sourceFile.DeclaresInjectedTopLevelWithQualifier = false;

            var lastStatement = m_sourceFile.Statements.LastOrDefault();
            m_scanner.AllowTrailingTriviaOnNode(lastStatement);
            m_scanner.AssociateTrailingCommentsWithLastTrivia();

            Contract.Assert(m_token == SyntaxKind.EndOfFileToken);
            m_sourceFile.EndOfFileToken = ParseTokenNode<TokenNode>();

            SetExternalModuleIndicator(m_sourceFile);

            m_sourceFile.NodeCount = m_nodeCount;
            m_sourceFile.IdentifierCount = m_identifierCount;
            m_sourceFile.ParseDiagnostics = ParseDiagnostics;

            if (setParentNodes)
            {
                FixupParentReferences(m_sourceFile);
            }

            // If this is a JavaScript file, proactively see if we can get JSDoc comments for
            // relevant nodes in the file.  We'll use these to provide typing information if they're
            // available.
            if (IsSourceFileJavaScript())
            {
                AddJsDocComments();
            }

            return m_sourceFile;
        }

        /// <nodoc/>
        public T LookAhead<T>(Parser parser, Func<Parser, T> callback)
        {
            return SpeculationHelper(parser, callback, isLookAhead: true);
        }

        /// <nodoc/>
        public PropertyName ParsePropertyName()
        {
            return ParsePropertyNameWorker(/*allowComputedPropertyNames*/ true);
        }

        /// <summary>
        /// In an ambient declaration, the grammar only allows integer literals as initializers.
        /// In a non-ambient declaration, the grammar allows uninitialized members only in a
        /// ConstantEnumMemberSection, which starts at the beginning of an enum declaration
        /// or any time an integer literal initializer is encountered.
        /// </summary>
        public virtual IEnumMember ParseEnumMember()
        {
            var node = CreateNode<EnumMember>(SyntaxKind.EnumMember);
            var decorators = ParseDecorators();
            node.Name = ParsePropertyName();
            node.Initializer = Optional.Create(AllowInAnd(this, p => p.ParseNonParameterInitializer()));
            node.Decorators = decorators;
            return FinishNode(node);
        }

        private void InitializeState(string fileName, TextSource sourceText, ScriptTarget languageVersion, bool isJavaScriptFile, IncrementalParser.SyntaxCursor syntaxCursor)
        {
            m_sourceText = sourceText;

            m_syntaxCursor = syntaxCursor;

            if (m_parseDiagnostics.IsValueCreated)
            {
                // Clean, only when diagnostics were allocated.
                m_parseDiagnostics.Value.Clear();
            }

            m_parsingContext = default(ParsingContext);
            m_identifierCount = 0;
            m_nodeCount = 0;

            m_contextFlags = ParserContextFlags.None;
            m_parseErrorBeforeNextFinishedNode = false;

            // Initialize and prime the scanner before parsing the source elements.
            m_scanner.SetText(m_sourceText);
            m_scanner.SetOnError(ScanError);
            m_scanner.SetScriptTarget(languageVersion);
            m_scanner.SetLanguageVariant(GetLanguageVariant(fileName));
        }

        private T ParseTokenNode<T>() where T : INode, new()
        {
            var node = CreateNode<T>(m_token);
            NextToken();
            return FinishNode(node);
        }

        /// <nodoc />
        protected T FinishNode<T>(T node, int? end = null) where T : INode
        {
            node.End = end ?? m_scanner.StartPos;

            if (m_contextFlags == ParserContextFlags.None)
            {
                node.ClearNonDScriptSpecificFlags();
            }

            // Keep track on the node if we encountered an error while parsing it.  If we did, then
            // we cannot reuse the node incrementally.  Once we've marked this node, clear out the
            // flag so that we don't mark any subsequent nodes.
            if (m_parseErrorBeforeNextFinishedNode)
            {
                m_parseErrorBeforeNextFinishedNode = false;
                node.ParserContextFlags |= ParserContextFlags.ThisNodeHasError;
            }

            if (m_parsingOptions?.FailOnMissingSemicolons == true &&
                MissingSemicolonAnalyzer.IsSemicolonMissingAfter(node, m_sourceText, out int position))
            {
                ITextSpan targetLocation = DiagnosticUtilities.GetSpanOfTokenAtPosition(m_sourceFile, m_sourceText, position);
                ParseErrorAtPosition(targetLocation.Start, targetLocation.Length, Errors.Semicolon_expected);
            }

            return node;
        }

        private T DisallowInAnd<T>(Parser parser, Func<Parser, T> func)
        {
            return DoInsideOfContext(parser, ParserContextFlags.DisallowIn, func);
        }

        private T DoInsideOfContext<T>(Parser parser, ParserContextFlags context, Func<Parser, T> func)
        {
            // contextFlagsToSet will contain only the context flags that
            // are not currently set that we need to temporarily enable.
            // We don't just blindly reset to the previous flags to ensure
            // that we do not mutate cached flags for the incremental
            // parser (ThisNodeHasError, ThisNodeOrAnySubNodesHasError, and
            // HasAggregatedChildData).
            var contextFlagsToSet = context & ~m_contextFlags;
            if (contextFlagsToSet != ParserContextFlags.None)
            {
                // set the requested context flags
                SetContextFlag(/*val*/ true, contextFlagsToSet);
                var result = func(parser);

                // reset the context flags we just set
                SetContextFlag(/*val*/ false, contextFlagsToSet);
                return result;
            }

            // no need to do anything special as we are already in all of the requested contexts
            return func(parser);
        }

        private IStatement ParseStatement()
        {
            switch (m_token)
            {
                case SyntaxKind.SemicolonToken:
                    return ParseEmptyStatement();
                case SyntaxKind.OpenBraceToken:
                    return ParseBlock(/*ignoreMissingOpenBrace*/ false);
                case SyntaxKind.VarKeyword:
                    return ParseVariableStatement(m_scanner.StartPos, GetLeadingTriviaLength(m_scanner.StartPos), /*decorators*/ null, /*modifiers*/ null);
                case SyntaxKind.LetKeyword:
                    if (IsLetDeclaration())
                    {
                        return ParseVariableStatement(m_scanner.StartPos, GetLeadingTriviaLength(m_scanner.StartPos), /*decorators*/ null, /*modifiers*/ null);
                    }

                    break;
                case SyntaxKind.FunctionKeyword:
                    return ParseFunctionDeclaration(m_scanner.StartPos, GetLeadingTriviaLength(m_scanner.StartPos), /*decorators*/ null, /*modifiers*/ null);
                case SyntaxKind.ClassKeyword:
                    return (IDeclarationStatement)ParseClassDeclaration(m_scanner.StartPos, GetLeadingTriviaLength(m_scanner.StartPos), /*decorators*/ null, /*modifiers*/ null);
                case SyntaxKind.IfKeyword:
                    return ParseIfStatement();
                case SyntaxKind.DoKeyword:
                    return ParseDoStatement();
                case SyntaxKind.WhileKeyword:
                    return ParseWhileStatement();
                case SyntaxKind.ForKeyword:
                    return ParseForOrForInOrForOfStatement();
                case SyntaxKind.ContinueKeyword:
                    return ParseBreakOrContinueStatement(SyntaxKind.ContinueStatement);
                case SyntaxKind.BreakKeyword:
                    return ParseBreakOrContinueStatement(SyntaxKind.BreakStatement);
                case SyntaxKind.ReturnKeyword:
                    return ParseReturnStatement();
                case SyntaxKind.WithKeyword:
                    return ParseWithStatement();
                case SyntaxKind.SwitchKeyword:
                    return ParseSwitchStatement();
                case SyntaxKind.ThrowKeyword:
                    return ParseThrowStatement();
                case SyntaxKind.TryKeyword:
                // Include 'catch' and 'finally' for error recovery.
                case SyntaxKind.CatchKeyword:
                case SyntaxKind.FinallyKeyword:
                    return ParseTryStatement();
                case SyntaxKind.DebuggerKeyword:
                    return ParseDebuggerStatement();
                case SyntaxKind.AtToken:
                    return ParseDeclaration();
                case SyntaxKind.AsyncKeyword:
                case SyntaxKind.InterfaceKeyword:
                case SyntaxKind.TypeKeyword:
                case SyntaxKind.ModuleKeyword:
                case SyntaxKind.NamespaceKeyword:
                case SyntaxKind.DeclareKeyword:
                case SyntaxKind.ConstKeyword:
                case SyntaxKind.EnumKeyword:
                case SyntaxKind.ExportKeyword:
                case SyntaxKind.ImportKeyword:
                case SyntaxKind.PrivateKeyword:
                case SyntaxKind.ProtectedKeyword:
                case SyntaxKind.PublicKeyword:
                case SyntaxKind.AbstractKeyword:
                case SyntaxKind.StaticKeyword:
                case SyntaxKind.ReadonlyKeyword:
                    if (IsStartOfDeclaration())
                    {
                        return ParseDeclaration();
                    }

                    break;
            }

            return ParseExpressionOrLabeledStatement();
        }

        // TODO: this is a hack!
        private static IIdentifier AsIdentifier(IExpression expression)
        {
            return expression.As<IIdentifier>();
        }

        // private NodeUnion<ExpressionStatement, LabeledStatement> parseExpressionOrLabeledStatement()
        private IStatement ParseExpressionOrLabeledStatement()
        {
            // Avoiding having to do the look-ahead for a labeled statement by just trying to parse
            // out an expression, seeing if it is identifier and then seeing if it is followed by
            // a colon.
            var fullStart = m_scanner.StartPos;
            var triviaLength = GetLeadingTriviaLength(fullStart);
            var expression = AllowInAnd(this, p => p.ParseExpression());

            if (expression.Kind == SyntaxKind.Identifier && ParseOptional(SyntaxKind.ColonToken))
            {
                var labeledStatement = CreateNode<LabeledStatement>(SyntaxKind.LabeledStatement, fullStart, triviaLength);

                labeledStatement.Label = AsIdentifier(expression);

                // labeledStatement.label = (Identifier)expression;
                labeledStatement.Statement = ParseStatement();
                return FinishNode(labeledStatement);
            }

            var expressionStatement = CreateNode<ExpressionStatement>(SyntaxKind.ExpressionStatement, fullStart, triviaLength);
            expressionStatement.Expression = expression;
            ParseSemicolon();
            return FinishNode(expressionStatement);
        }

        private IStatement ParseDeclaration()
        {
            var fullStart = GetNodePos();
            var triviaLength = GetLeadingTriviaLength(fullStart);

            var decorators = ParseDecorators();
            var modifiers = ParseModifiers();

            // DScript-specific. We look for @@public decorator and reflect it in the modifiers of node
            if (decorators.Any(decorator => decorator.IsScriptPublicDecorator()))
            {
                if (modifiers == null)
                {
                    modifiers = ModifiersArray.Create(NodeFlags.ScriptPublic);
                }

                modifiers.Flags |= NodeFlags.ScriptPublic;
            }

            switch (m_token)
            {
                case SyntaxKind.VarKeyword:
                case SyntaxKind.LetKeyword:
                case SyntaxKind.ConstKeyword:
                    return ParseVariableStatement(fullStart, triviaLength, decorators, modifiers);
                case SyntaxKind.FunctionKeyword:
                    return ParseFunctionDeclaration(fullStart, triviaLength, decorators, modifiers);
                case SyntaxKind.ClassKeyword:
                    return (IDeclarationStatement)ParseClassDeclaration(fullStart, triviaLength, decorators, modifiers);
                case SyntaxKind.InterfaceKeyword:
                    return ParseInterfaceDeclaration(fullStart, triviaLength, decorators, modifiers);
                case SyntaxKind.TypeKeyword:
                    return ParseTypeAliasDeclaration(fullStart, triviaLength, decorators, modifiers);
                case SyntaxKind.EnumKeyword:
                    return ParseEnumDeclaration(fullStart, triviaLength, decorators, modifiers);
                case SyntaxKind.ModuleKeyword:
                case SyntaxKind.NamespaceKeyword:
                    return ParseModuleDeclaration(fullStart, triviaLength, decorators, modifiers);
                case SyntaxKind.ImportKeyword:
                    return ParseImportDeclarationOrImportEqualsDeclaration(fullStart, triviaLength, decorators, modifiers);
                case SyntaxKind.ExportKeyword:
                    NextToken();
                    return m_token == SyntaxKind.DefaultKeyword || m_token == SyntaxKind.EqualsToken ?
                    (IStatement)ParseExportAssignment(fullStart, triviaLength, decorators, modifiers) :
                    (IStatement)ParseExportDeclaration(fullStart, triviaLength, decorators, modifiers);
            }

            if (decorators != null || modifiers != null)
            {
                // We reached this point because we encountered decorators and/or modifiers and assumed a declaration
                // would follow. For recovery and error reporting purposes, return an incomplete declaration.
                var node = CreateMissingNode<Statement>(SyntaxKind.MissingDeclaration, /*reportAtCurrentPosition*/ true, Errors.Declaration_expected);
                node.Pos = fullStart;
                node.Decorators = decorators;

                if (decorators != null && decorators.Length > 0)
                {
                    m_scanner.MoveTrivia(decorators[0], node);
                }

                SetModifiers(node, modifiers);
                return FinishNode(node);
            }

            return null;
        }

        /// <nodoc />
        protected virtual IExportDeclaration ParseExportDeclaration(int fullStart, int triviaLength, NodeArray<IDecorator> decorators, ModifiersArray modifiers)
        {
            var node = CreateNode<ExportDeclaration>(SyntaxKind.ExportDeclaration, fullStart, triviaLength);
            node.Decorators = decorators;

            if (decorators != null && decorators.Length > 0)
            {
                m_scanner.MoveTrivia(decorators[0], node);
            }

            SetModifiers(node, modifiers);

            node.AsteriskPos = m_scanner.StartPos;
            node.AsteriskEnd = m_scanner.TextPos;

            if (ParseOptional(SyntaxKind.AsteriskToken))
            {
                ParseExpected(SyntaxKind.FromKeyword);
                node.ModuleSpecifier = ParseModuleSpecifier();
            }
            else
            {
                node.ExportClause = ParseNamedImportsOrExports(SyntaxKind.NamedExports);

                // It is not uncommon to accidentally omit the 'from' keyword. Additionally, in editing scenarios,
                // the 'from' keyword can be parsed as a named export when the export clause is unterminated (i.e., `export { from "moduleName";`)
                // If we don't have a 'from' keyword, see if we have a string literal such that ASI won't take effect.
                if (m_token == SyntaxKind.FromKeyword || (m_token == SyntaxKind.StringLiteral && !m_scanner.HasPrecedingLineBreak))
                {
                    ParseExpected(SyntaxKind.FromKeyword);
                    node.ModuleSpecifier = ParseModuleSpecifier();
                }
            }

            // DScript-specific. Collect the export specifier if it is a literal expression.
            var literalSpecifier = node.ModuleSpecifier as ILiteralExpression;
            if (literalSpecifier != null)
            {
                m_sourceFile.LiteralLikeSpecifiers.Add(literalSpecifier);
            }

            ParseSemicolon();
            return FinishNode(node);
        }

        private IExportAssignment ParseExportAssignment(int fullStart, int triviaLength, NodeArray<IDecorator> decorators, ModifiersArray modifiers)
        {
            var node = CreateNode<ExportAssignment>(SyntaxKind.ExportAssignment, fullStart, triviaLength);
            node.Decorators = decorators;
            SetModifiers(node, modifiers);
            if (ParseOptional(SyntaxKind.EqualsToken))
            {
                node.IsExportEquals = true;
            }
            else
            {
                ParseExpected(SyntaxKind.DefaultKeyword);
            }

            node.Expression = ParseAssignmentExpressionOrHigher();
            ParseSemicolon();
            return FinishNode(node);
        }

        /// <nodoc />
        protected virtual IModuleDeclaration ParseModuleDeclaration(int fullStart, int triviaLength, NodeArray<IDecorator> decorators, ModifiersArray modifiers)
        {
            var flags = modifiers?.Flags ?? 0;
            if (ParseOptional(SyntaxKind.NamespaceKeyword))
            {
                flags |= NodeFlags.Namespace;
            }
            else
            {
                ParseExpected(SyntaxKind.ModuleKeyword);
                if (m_token == SyntaxKind.StringLiteral)
                {
                    return ParseAmbientExternalModuleDeclaration(fullStart, triviaLength, decorators, modifiers);
                }
            }

            return ParseModuleOrNamespaceDeclaration(fullStart, triviaLength, decorators, modifiers, flags);
        }

        private IModuleDeclaration ParseModuleOrNamespaceDeclaration(
            int fullStart,
            int triviaLength,
            NodeArray<IDecorator> decorators,
            ModifiersArray modifiers,
            NodeFlags flags)
        {
            var node = CreateNode<ModuleDeclaration>(SyntaxKind.ModuleDeclaration, fullStart, triviaLength);

            // If we are parsing a dotted namespace name, we want to
            // propagate the 'Namespace' flag across the names if set.
            var namespaceFlag = flags & NodeFlags.Namespace;
            node.Decorators = decorators;

            if (decorators != null && decorators.Length > 0)
            {
                m_scanner.MoveTrivia(decorators[0], node);
            }

            SetModifiers(node, modifiers);
            node.Flags |= flags;
            node.Name = new IdentifierOrLiteralExpression(ParseIdentifier());
            if (ParseOptional(SyntaxKind.DotToken))
            {
                var moduleBodyFullPosition = GetNodePos();
                node.Body = new ModuleBody(
                    ParseModuleOrNamespaceDeclaration(
                        moduleBodyFullPosition,
                        GetLeadingTriviaLength(moduleBodyFullPosition),
                        /*decorators*/ null,
                        /*modifiers*/ null,
                        NodeFlags.Export | namespaceFlag));
            }
            else
            {
                var moduleBlock = ParseModuleBlock();

                // DScript-specific. withQualifier function is generated here if required
                if (m_parsingOptions?.GenerateWithQualifierFunctionForEveryNamespace == true)
                {
                    moduleBlock.AddWithQualifierFunction(node.Name.AsIdentifier(), m_sourceFile);
                }

                node.Body = new ModuleBody(moduleBlock);
            }

            // DScript-specific. All namespaces are public, regardless of their content
            if (m_parsingOptions?.NamespacesAreAutomaticallyExported == true && m_isScriptFile)
            {
                node.Flags |= NodeFlags.Export | NodeFlags.ScriptPublic;
            }

            return FinishNode(node);
        }

        private IModuleDeclaration ParseAmbientExternalModuleDeclaration(int fullStart, int triviaLength, NodeArray<IDecorator> decorators, ModifiersArray modifiers)
        {
            var node = CreateNode<ModuleDeclaration>(SyntaxKind.ModuleDeclaration, fullStart, triviaLength);
            node.Decorators = decorators;
            SetModifiers(node, modifiers);
            node.Name = new IdentifierOrLiteralExpression(ParseLiteralNode(/*internName*/ true));
            node.Body = new ModuleBody(ParseModuleBlock());
            return FinishNode(node);
        }

        private IModuleBlock ParseModuleBlock()
        {
            var node = CreateNode<ModuleBlock>(SyntaxKind.ModuleBlock);
            if (ParseExpected(SyntaxKind.OpenBraceToken))
            {
                node.Statements = ParseList(this, ParsingContext.BlockStatements, p => p.ParseStatement());
                ParseExpected(SyntaxKind.CloseBraceToken);
            }
            else
            {
                node.Statements = CreateMissingList<IStatement>();
            }

            return FinishNode(node);
        }

        private IStatement ParseImportDeclarationOrImportEqualsDeclaration(int fullStart, int triviaLength, NodeArray<IDecorator> decorators, ModifiersArray modifiers)
        {
            ParseExpected(SyntaxKind.ImportKeyword);
            var afterImportPos = m_scanner.StartPos;
            var afterImportTriviaLength = GetLeadingTriviaLength(afterImportPos);

            IIdentifier identifier = null;
            if (IsIdentifier())
            {
                identifier = ParseIdentifier();
                if (m_token != SyntaxKind.CommaToken && m_token != SyntaxKind.FromKeyword)
                {
                    // ImportEquals declaration of type:
                    // import x = importFrom("mod"); or
                    // import x = M.x;
                    var importEqualsDeclaration = CreateNode<ImportEqualsDeclaration>(SyntaxKind.ImportEqualsDeclaration, fullStart, triviaLength);
                    importEqualsDeclaration.Decorators = decorators;
                    SetModifiers(importEqualsDeclaration, modifiers);
                    importEqualsDeclaration.Name = identifier;
                    ParseExpected(SyntaxKind.EqualsToken);
                    importEqualsDeclaration.ModuleReference = ParseModuleReference();
                    ParseSemicolon();
                    return FinishNode(importEqualsDeclaration);
                }
            }

            // Import statement
            var importDeclaration = CreateNode<ImportDeclaration>(SyntaxKind.ImportDeclaration, fullStart, triviaLength);
            importDeclaration.Decorators = decorators;
            SetModifiers(importDeclaration, modifiers);

            // ImportDeclaration:
            //  import ImportClause from ModuleSpecifier ;
            //  import ModuleSpecifier;
            if (identifier != null || // import id
                m_token == SyntaxKind.AsteriskToken || // import *
                m_token == SyntaxKind.OpenBraceToken)
            {
                bool isImport;

                // import {
                importDeclaration.ImportClause = ParseImportClause(identifier, afterImportPos, afterImportTriviaLength, out isImport);
                importDeclaration.IsLikeImport = isImport;
                ParseExpected(SyntaxKind.FromKeyword);
            }

            importDeclaration.ModuleSpecifier = ParseModuleSpecifier();

            // DScript-specific. Collect the import specifier if it is a literal expression.
            var literalSpecifier = importDeclaration.ModuleSpecifier as ILiteralExpression;
            if (literalSpecifier != null)
            {
                m_sourceFile.LiteralLikeSpecifiers.Add(literalSpecifier);
            }

            ParseSemicolon();
            return FinishNode(importDeclaration);
        }

        private IImportClause ParseImportClause(IIdentifier identifier, int fullStart, int triviaLength, out bool isImport)
        {
            isImport = false;

            // ImportClause:
            //  ImportedDefaultBinding
            //  NameSpaceImport
            //  NamedImports
            //  ImportedDefaultBinding, NameSpaceImport
            //  ImportedDefaultBinding, NamedImports
            var importClause = CreateNode<ImportClause>(SyntaxKind.ImportClause, fullStart, triviaLength);
            if (identifier != null)
            {
                // ImportedDefaultBinding:
                //  ImportedBinding
                importClause.Name = identifier;
            }

            // If there was no default import or if there is comma token after default import
            // parse namespace or named imports
            if (importClause.Name == null ||
                ParseOptional(SyntaxKind.CommaToken))
            {
                importClause.NamedBindings =
                    m_token == SyntaxKind.AsteriskToken
                    ? new NamespaceImportOrNamedImports(ParseNamespaceImport(out isImport))
                    : new NamespaceImportOrNamedImports(ParseNamedImportsOrExports(SyntaxKind.NamedImports));
            }

            return FinishNode(importClause);
        }

        private INamespaceImport ParseNamespaceImport(out bool isImport)
        {
            // NameSpaceImport:
            //  * as ImportedBinding
            var namespaceImport = CreateNode<NamespaceImport>(SyntaxKind.NamespaceImport);

            isImport = false;
            ParseExpected(SyntaxKind.AsteriskToken);

            if (m_token != SyntaxKind.AsKeyword)
            {
                m_identifierCount++;
                m_fakeScriptIdCounter++;
                var node = CreateNode<Identifier>(SyntaxKind.Identifier);
                node.Text = InternIdentifier(I($"fake_id_{m_fakeScriptIdCounter}"));
                namespaceImport.Name = FinishNode(node);
                isImport = true;
            }
            else
            {
                ParseExpected(SyntaxKind.AsKeyword);
                namespaceImport.Name = ParseIdentifier();
            }

            namespaceImport.IsImport = isImport;

            // DS: more appropriate solution would be to extend NamespaceImport with additional hasAlias field, but this breaks emitter
            // and spreads DScript specific changes across different type definitions.
            // This approach keeps all the changes in ImportDeclaration type.
            return FinishNode(namespaceImport);
        }

        private NamedImportsOrNamedExports ParseNamedImportsOrExports(SyntaxKind kind)
        {
            NamedImportsOrNamedExports node;

            if (kind == SyntaxKind.NamedImports)
            {
                var imports = CreateNode<NamedImports>(kind);
                imports.Elements = ParseBracketedList(this, ParsingContext.ImportOrExportSpecifiers, p => p.ParseImportSpecifier(), SyntaxKind.OpenBraceToken, SyntaxKind.CloseBraceToken);

                node = new NamedImportsOrNamedExports(imports);
            }
            else
            {
                var exports = CreateNode<NamedExports>(kind);
                exports.Elements = ParseBracketedList(this, ParsingContext.ImportOrExportSpecifiers, p => p.ParseExportSpecifier(), SyntaxKind.OpenBraceToken, SyntaxKind.CloseBraceToken);

                node = new NamedImportsOrNamedExports(exports);
            }

            //// NamedImports:
            ////  { }
            ////  { ImportsList }
            ////  { ImportsList, }

            // ImportsList:
            //  ImportSpecifier
            //  ImportsList, ImportSpecifier
            // Solution from typescript compiler:
            // node.elements = parseBracketedList(ParsingContext.ImportOrExportSpecifiers,
            //    kind == SyntaxKind.NamedImports ? parseImportSpecifier : parseExportSpecifier,
            // SyntaxKind.OpenBraceToken, SyntaxKind.CloseBraceToken);
            return FinishNode(node);
        }

        private IImportSpecifier ParseImportSpecifier()
        {
            return (IImportSpecifier)ParseImportOrExportSpecifier(CreateNode<ImportSpecifier>(SyntaxKind.ImportSpecifier));
        }

        private IExportSpecifier ParseExportSpecifier()
        {
            return (IExportSpecifier)ParseImportOrExportSpecifier(CreateNode<ExportSpecifier>(SyntaxKind.ExportSpecifier));
        }

        private IImportOrExportSpecifier ParseImportOrExportSpecifier(IImportOrExportSpecifier node)
        {
            // ImportSpecifier:
            //   BindingIdentifier
            //   IdentifierName as BindingIdentifier
            // ExportSpecifier:
            //   IdentifierName
            //   IdentifierName as IdentifierName
            var checkIdentifierIsKeyword = m_token.IsKeyword() && !IsIdentifier();
            var checkIdentifierStart = m_scanner.TokenPos;
            var checkIdentifierEnd = m_scanner.TextPos;
            var identifierName = ParseIdentifierName();
            if (m_token == SyntaxKind.AsKeyword)
            {
                node.PropertyName = identifierName;
                ParseExpected(SyntaxKind.AsKeyword);
                checkIdentifierIsKeyword = m_token.IsKeyword() && !IsIdentifier();
                checkIdentifierStart = m_scanner.TokenPos;
                checkIdentifierEnd = m_scanner.TextPos;
                node.Name = ParseIdentifierName();
            }
            else
            {
                node.Name = identifierName;
            }

            if (node.Kind == SyntaxKind.ImportSpecifier && checkIdentifierIsKeyword)
            {
                // Report error identifier expected
                ParseErrorAtPosition(checkIdentifierStart, checkIdentifierEnd - checkIdentifierStart, Errors.Identifier_expected);
            }

            return FinishNode(node);
        }

        private EntityNameOrExternalModuleReference ParseModuleReference()
        {
            return IsExternalModuleReference()
                ? new EntityNameOrExternalModuleReference(ParseExternalModuleReference())
                : new EntityNameOrExternalModuleReference(ParseEntityName(/*allowReservedWords*/ false));
        }

        private IExternalModuleReference ParseExternalModuleReference()
        {
            var node = CreateNode<ExternalModuleReference>(SyntaxKind.ExternalModuleReference);
            ParseExpected(SyntaxKind.RequireKeyword);
            ParseExpected(SyntaxKind.OpenParenToken);
            node.Expression = ParseModuleSpecifier();
            ParseExpected(SyntaxKind.CloseParenToken);
            return FinishNode(node);
        }

        // The allowReservedWords parameter controls whether reserved words are permitted after the first dot
        private EntityName ParseEntityName(bool allowReservedWords, IDiagnosticMessage diagnosticMessage = null)
        {
            // throw PlaceHolder.NotImplemented();
            EntityName entity = new EntityName(ParseIdentifier(diagnosticMessage));
            while (ParseOptional(SyntaxKind.DotToken))
            {
                INode entityNode = entity;
                var node = CreateNode<QualifiedName>(SyntaxKind.QualifiedName, entityNode.Pos, entity.GetLeadingTriviaLength(m_sourceFile));
                node.Left = entity;
                node.Right = ParseRightSideOfDot(allowReservedWords);
                entity = new EntityName(FinishNode(node));
            }

            return entity;
        }

        private IIdentifier ParseRightSideOfDot(bool allowIdentifierNames)
        {
            // Technically a keyword is valid here as all identifiers and keywords are identifier names.
            // However, often we'll encounter this in error situations when the identifier or keyword
            // is actually starting another valid construct.
            //
            // So, we check for the following specific case:
            //
            //      name.
            //      identifierOrKeyword identifierNameOrKeyword
            //
            // Note: the newlines are important here.  For example, if that above code
            // were rewritten into:
            //
            //      Name.identifierOrKeyword
            //      identifierNameOrKeyword
            //
            // Then we would consider it valid.  That's because ASI would take effect and
            // the code would be implicitly: "Name.identifierOrKeyword; identifierNameOrKeyword".
            // In the first case though, ASI will not take effect because there is not a
            // line terminator after the identifier or keyword.
            if (m_scanner.HasPrecedingLineBreak && m_token.IsIdentifierOrKeyword())
            {
                var matchesPattern = LookAhead(this, p => p.NextTokenIsIdentifierOrKeywordOnSameLine());

                if (matchesPattern)
                {
                    // Report that we need an identifier.  However, report it right after the dot,
                    // and not on the next token.  This is because the next token might actually
                    // be an identifier and the error would be quite confusing.
                    return CreateMissingNode<Identifier>(SyntaxKind.Identifier, /*reportAtCurrentPosition*/ true, Errors.Identifier_expected);
                }
            }

            return allowIdentifierNames ? ParseIdentifierName() : ParseIdentifier();
        }

        private bool NextTokenIsIdentifierOrKeywordOnSameLine()
        {
            NextToken();
            return m_token.IsIdentifierOrKeyword() && !m_scanner.HasPrecedingLineBreak;
        }

        private IExpression ParseModuleSpecifier()
        {
            if (m_token == SyntaxKind.StringLiteral)
            {
                var result = ParseLiteralNode();
                InternIdentifier(result.Text);
                return result;
            }
            else
            {
                // We allow arbitrary expressions here, even though the grammar only allows string
                // literals.  We check to ensure that it is only a string literal later in the grammar
                // check pass.
                return ParseExpression();
            }
        }

        private bool IsExternalModuleReference()
        {
            return m_token == SyntaxKind.RequireKeyword && LookAhead(this, p => p.NextTokenIsOpenParen());
        }

        private bool NextTokenIsOpenParen()
        {
            return NextToken() == SyntaxKind.OpenParenToken;
        }

        /// <nodoc />
        protected virtual IEnumDeclaration ParseEnumDeclaration(int fullStart, int triviaLength, NodeArray<IDecorator> decorators, ModifiersArray modifiers)
        {
            var node = CreateNode<EnumDeclaration>(SyntaxKind.EnumDeclaration, fullStart, triviaLength);
            node.Decorators = decorators;

            if (decorators != null && decorators.Length > 0)
            {
                m_scanner.MoveTrivia(decorators[0], node);
            }

            SetModifiers(node, modifiers);
            ParseExpected(SyntaxKind.EnumKeyword);
            node.Name = ParseIdentifier();
            if (ParseExpected(SyntaxKind.OpenBraceToken))
            {
                node.Members = ParseDelimitedList(this, ParsingContext.EnumMembers, p => p.ParseEnumMember());
                ParseExpected(SyntaxKind.CloseBraceToken);
            }
            else
            {
                node.Members = CreateMissingList<IEnumMember>();
            }

            return FinishNode(node);
        }

        /*
         * Parses a comma-delimited list of elements.
         * DScript-specific: It allows for comments after and before list elements.
         * */
        private NodeArray<T> ParseDelimitedList<T>(Parser parser, ParsingContext kind, Func<Parser, T> parseElement, bool considerSemicolonAsDelimeter = false) where T : INode
        {
            Contract.Ensures(Contract.Result<NodeArray<T>>() != null);

            var saveParsingContext = m_parsingContext;
            m_parsingContext |= (ParsingContext)(1 << (int)kind);
            var result = new NodeArray<T>();
            result.Pos = GetNodePos();

            var commaStart = -1; // Meaning the previous token was not a comma
            while (true)
            {
                if (IsListElement(kind, /*inErrorRecovery*/ false))
                {
                    var element = ParseListElement(parser, kind, parseElement);
                    result.Add(element);
                    m_scanner.AllowTrailingTriviaOnNode(element);

                    commaStart = m_scanner.TokenPos;

                    if (ParseOptional(SyntaxKind.CommaToken))
                    {
                        continue;
                    }

                    commaStart = -1; // Back to the state where the last token was not a comma
                    if (IsListTerminator(kind))
                    {
                        break;
                    }

                    // We didn't get a comma, and the list wasn't terminated, explicitly parse
                    // out a comma so we give a good error message.
                    ParseExpected(SyntaxKind.CommaToken);

                    // If the token was a semicolon, and the caller allows that, then skip it and
                    // continue.  This ensures we get back on track and don't result in tons of
                    // parse errors.  For example, this can happen when people do things like use
                    // a semicolon to delimit object literal members.   Note: we'll have already
                    // reported an error when we called parseExpected above.
                    if (considerSemicolonAsDelimeter && m_token == SyntaxKind.SemicolonToken && !m_scanner.HasPrecedingLineBreak)
                    {
                        NextToken();
                    }

                    continue;
                }

                if (IsListTerminator(kind))
                {
                    break;
                }

                if (AbortParsingListOrMoveToNextToken(kind))
                {
                    break;
                }
            }

            // Recording the trailing comma is deliberately done after the previous
            // loop, and not just if we see a list terminator. This is because the list
            // may have ended incorrectly, but it is still important to know if there
            // was a trailing comma.
            // Check if the last token was a comma.
            if (commaStart >= 0)
            {
                // Always preserve a trailing comma by marking it on the NodeArray
                result.HasTrailingComma = true;
            }

            result.End = GetNodeEnd();
            m_parsingContext = saveParsingContext;
            return result;
        }

        /// <nodoc />
        protected virtual IInterfaceDeclaration ParseInterfaceDeclaration(int fullStart, int triviaLength, NodeArray<IDecorator> decorators, ModifiersArray modifiers)
        {
            var node = CreateNode<InterfaceDeclaration>(SyntaxKind.InterfaceDeclaration, fullStart, triviaLength);
            node.Decorators = decorators;

            if (decorators != null && decorators.Length > 0)
            {
                m_scanner.MoveTrivia(decorators[0], node);
            }

            SetModifiers(node, modifiers);
            ParseExpected(SyntaxKind.InterfaceKeyword);
            node.Name = ParseIdentifier();
            node.TypeParameters = ParseTypeParameters();
            node.HeritageClauses = ParseHeritageClauses(/*isClassHeritageClause*/ false);
            node.Members = ParseObjectTypeMembers();
            return FinishNode(node);
        }

        private NodeArray<IHeritageClause> ParseHeritageClauses(bool isClassHeritageClause)
        {
            Contract.Ensures(Contract.Result<NodeArray<IHeritageClause>>() != null);

            // ClassTail[Yield,Await] : (Modified) See 14.5
            //      ClassHeritage[?Yield,?Await]opt { ClassBody[?Yield,?Await]opt }
            if (IsHeritageClause())
            {
                return ParseList(this, ParsingContext.HeritageClauses, p => p.ParseHeritageClause());
            }

            return NodeArray<IHeritageClause>.Empty;
        }

        private IHeritageClause ParseHeritageClause()
        {
            if (m_token == SyntaxKind.ExtendsKeyword || m_token == SyntaxKind.ImplementsKeyword)
            {
                var node = CreateNode<HeritageClause>(SyntaxKind.HeritageClause);
                node.Token = m_token;
                NextToken();
                node.Types = ParseDelimitedList(this, ParsingContext.HeritageClauseElement, p => p.ParseExpressionWithTypeArguments());
                return FinishNode(node);
            }

            return null;
        }

        private IExpressionWithTypeArguments ParseExpressionWithTypeArguments()
        {
            var node = CreateNode<ExpressionWithTypeArguments>(SyntaxKind.ExpressionWithTypeArguments);
            node.Expression = ParseLeftHandSideExpressionOrHigher();
            if (m_token == SyntaxKind.LessThanToken)
            {
                node.TypeArguments = ParseBracketedList(this, ParsingContext.TypeArguments, p => p.ParseType(), SyntaxKind.LessThanToken, SyntaxKind.GreaterThanToken);
            }

            return FinishNode(node);
        }

        /// <nodoc />
        protected virtual ITypeAliasDeclaration ParseTypeAliasDeclaration(int fullStart, int triviaLength, NodeArray<IDecorator> decorators, ModifiersArray modifiers)
        {
            var node = CreateNode<TypeAliasDeclaration>(SyntaxKind.TypeAliasDeclaration, fullStart, triviaLength);
            node.Decorators = decorators;

            if (decorators != null && decorators.Length > 0)
            {
                m_scanner.MoveTrivia(decorators[0], node);
            }

            SetModifiers(node, modifiers);
            ParseExpected(SyntaxKind.TypeKeyword);
            node.Name = ParseIdentifier();
            node.TypeParameters = ParseTypeParameters();
            ParseExpected(SyntaxKind.EqualsToken);
            node.Type = ParseType();
            ParseSemicolon();
            return FinishNode(node);
        }

        /*
         * There are situations in which a modifier like 'const' will appear unexpectedly, such as on a class member.
         * In those situations, if we are entirely sure that 'const' is not valid on its own (such as when ASI takes effect
         * and turns it into a standalone declaration), then it is better to parse it and report an error later.
         *
         * In such situations, 'permitInvalidConstAsModifier' should be set to true.
         */

        private ModifiersArray ParseModifiers(bool permitInvalidConstAsModifier = false)
        {
            NodeFlags flags = 0;
            int startPosition = -1;
            List<IModifier> modifiers = null;
            while (true)
            {
                var modifierStart = m_scanner.StartPos;
                var modifierTriviaLength = GetLeadingTriviaLength(modifierStart);
                var modifierKind = m_token;

                if (m_token == SyntaxKind.ConstKeyword && permitInvalidConstAsModifier)
                {
                    // We need to ensure that any subsequent modifiers appear on the same line
                    // so that when 'const' is a standalone declaration, we don't issue an error.
                    if (!TryParse(this, p => p.NextTokenIsOnSameLineAndCanFollowModifier()))
                    {
                        break;
                    }
                }
                else
                {
                    if (!ParseAnyContextualModifier())
                    {
                        break;
                    }
                }

                if (modifiers == null)
                {
                    modifiers = new List<IModifier>();
                    startPosition = modifierStart;
                }

                flags |= modifierKind.ModifierToFlag();

                // Trivia should never be associated with modifiers, but with the higher order constructs.
                modifiers.Add(FinishNode(CreateNode<Modifier>(modifierKind, modifierStart, modifierTriviaLength, associateTrivia: false)));
            }

            if (modifiers != null)
            {
                var end = m_scanner.StartPos;
                return ModifiersArray.Create(flags, startPosition, end, modifiers);
            }

            return null;
        }

        private bool ParseAnyContextualModifier()
        {
            return m_token.IsModifierKind() && TryParse(this, p => p.NextTokenCanFollowModifier());
        }

        private bool NextTokenCanFollowModifier()
        {
            if (m_token == SyntaxKind.ConstKeyword)
            {
                // 'const' is only a modifier if followed by 'enum'.
                return NextToken() == SyntaxKind.EnumKeyword;
            }

            if (m_token == SyntaxKind.ExportKeyword)
            {
                NextToken();
                if (m_token == SyntaxKind.DefaultKeyword)
                {
                    return LookAhead(this, p => p.NextTokenIsClassOrFunction());
                }

                return m_token != SyntaxKind.AsteriskToken && m_token != SyntaxKind.OpenBraceToken && CanFollowModifier();
            }

            if (m_token == SyntaxKind.DefaultKeyword)
            {
                return NextTokenIsClassOrFunction();
            }

            if (m_token == SyntaxKind.StaticKeyword)
            {
                NextToken();
                return CanFollowModifier();
            }

            return NextTokenIsOnSameLineAndCanFollowModifier();
        }

        private bool NextTokenIsClassOrFunction()
        {
            NextToken();
            return m_token == SyntaxKind.ClassKeyword || m_token == SyntaxKind.FunctionKeyword;
        }

        private bool NextTokenIsOnSameLineAndCanFollowModifier()
        {
            NextToken();
            if (m_scanner.HasPrecedingLineBreak)
            {
                return false;
            }

            return CanFollowModifier();
        }

        private bool CanFollowModifier()
        {
            return m_token == SyntaxKind.OpenBracketToken
                || m_token == SyntaxKind.OpenBraceToken
                || m_token == SyntaxKind.AsteriskToken
                || IsLiteralPropertyName();
        }

        private bool IsLiteralPropertyName()
        {
            return m_token.IsIdentifierOrKeyword() ||
                m_token == SyntaxKind.StringLiteral ||
                m_token == SyntaxKind.NumericLiteral;
        }

        private T DoInDecoratorContext<T>(Parser parser, Func<Parser, T> func)
        {
            return DoInsideOfContext(parser, ParserContextFlags.Decorator, func);
        }

        private NodeArray<IDecorator> ParseDecorators()
        {
            NodeArray<IDecorator> decorators = null;
            while (true)
            {
                var decoratorStart = GetNodePos();
                var decoratorTriviaLength = GetLeadingTriviaLength(decoratorStart);
                if (!ParseOptional(SyntaxKind.AtToken))
                {
                    break;
                }

                if (decorators == null)
                {
                    decorators = new NodeArray<IDecorator>();
                    decorators.Pos = decoratorStart;
                }

                var decorator = CreateNode<Decorator>(SyntaxKind.Decorator, decoratorStart, decoratorTriviaLength);
                decorator.Expression = DoInDecoratorContext(this, p => p.ParseLeftHandSideExpressionOrHigher());
                decorators.Add(FinishNode(decorator));
            }

            if (decorators != null)
            {
                decorators.End = GetNodeEnd();
            }

            return decorators;
        }

        private ILeftHandSideExpression ParseLeftHandSideExpressionOrHigher()
        {
            // Original Ecma:
            // LeftHandSideExpression: See 11.2
            //      NewExpression
            //      CallExpression
            //
            // Our simplification:
            //
            // LeftHandSideExpression: See 11.2
            //      MemberExpression
            //      CallExpression
            //
            // See comment in parseMemberExpressionOrHigher on how we replaced NewExpression with
            // MemberExpression to make our lives easier.
            //
            // to best understand the below code, it's important to see how CallExpression expands
            // out into its own productions:
            //
            // CallExpression:
            //      MemberExpression Arguments
            //      CallExpression Arguments
            //      CallExpression[Expression]
            //      CallExpression.IdentifierName
            //      super   (   ArgumentListopt   )
            //      super.IdentifierName
            //
            // Because of the recursion in these calls, we need to bottom out first.  There are two
            // bottom out states we can run into.  Either we see 'super' which must start either of
            // the last two CallExpression productions.  Or we have a MemberExpression which either
            // completes the LeftHandSideExpression, or starts the beginning of the first four
            // CallExpression productions.
            var expression = m_token == SyntaxKind.SuperKeyword
                ? ParseSuperExpression()
                : ParseMemberExpressionOrHigher();

            // Now, we *may* be complete.  However, we might have consumed the start of a
            // CallExpression.  As such, we need to consume the rest of it here to be complete.
            return ParseCallExpressionRest(expression);
        }

        private ILeftHandSideExpression ParseCallExpressionRest(ILeftHandSideExpression expression)
        {
            while (true)
            {
                // DScript-specific. Collects the specifier in case the call expression is an importFrom
                if (expression.IsImportFrom())
                {
                    m_sourceFile.LiteralLikeSpecifiers.Add(expression.GetSpecifierInImportFrom());
                }

                if (m_parsingOptions?.CollectImportFile == true && expression.IsImportFile())
                {
                    var fileTemplateLiteral = expression.GetSpecifierInImportFile();
                    if (fileTemplateLiteral != null)
                    {
                        m_sourceFile.LiteralLikeSpecifiers.Add(fileTemplateLiteral);
                    }
                }

                expression = ParseMemberExpressionRest(expression);
                if (m_token == SyntaxKind.LessThanToken)
                {
                    // See if this is the start of a generic invocation.  If so, consume it and
                    // keep checking for postfix expressions.  Otherwise, it's just a '<' that's
                    // part of an arithmetic expression.  Break out so we consume it higher in the
                    // stack.
                    var typeArguments = TryParse(this, p => p.ParseTypeArgumentsInExpression());
                    if (typeArguments == null)
                    {
                        return expression;
                    }

                    var callExpr = CreateNode<CallExpression>(SyntaxKind.CallExpression, expression.Pos, expression.GetLeadingTriviaLength(m_sourceFile));
                    callExpr.Expression = expression;
                    callExpr.TypeArguments = typeArguments;
                    callExpr.Arguments = ParseArgumentList();
                    expression = FinishNode(callExpr);
                    continue;
                }
                else if (m_token == SyntaxKind.OpenParenToken)
                {
                    var callExpr = CreateNode<CallExpression>(SyntaxKind.CallExpression, expression.Pos, expression.GetLeadingTriviaLength(m_sourceFile));
                    callExpr.Expression = expression;
                    callExpr.Arguments = ParseArgumentList();
                    expression = FinishNode(callExpr);
                    continue;
                }

                return expression;
            }
        }

        private NodeArray<IExpression> ParseArgumentList()
        {
            var posIncludingStartToken = m_scanner.TokenPos;
            ParseExpected(SyntaxKind.OpenParenToken);
            var result = ParseDelimitedList(this, ParsingContext.ArgumentExpressions, p => p.ParseArgumentExpression());
            ParseExpected(SyntaxKind.CloseParenToken);
            result.PosIncludingStartToken = posIncludingStartToken;
            result.EndIncludingEndToken = m_scanner.TokenPos;
            return result;
        }

        private IExpression ParseArgumentExpression()
        {
            return DoOutsideOfContext(this, m_disallowInAndDecoratorContext, p => p.ParseArgumentOrArrayLiteralElement());
        }

        private NodeArray<ITypeNode> ParseTypeArgumentsInExpression()
        {
            if (!ParseOptional(SyntaxKind.LessThanToken))
            {
                return null;
            }

            NodeArray<ITypeNode> typeArguments = ParseDelimitedList(this, ParsingContext.TypeArguments, p => p.ParseType());
            if (!ParseExpected(SyntaxKind.GreaterThanToken))
            {
                // If it doesn't have the closing >  then it's definitely not an type argument list.
                return null;
            }

            // If we have a '<', then only parse this as a argument list if the type arguments
            // are complete and we have an open parenthesis.  if we don't, rewind and return nothing.
            return typeArguments != null && CanFollowTypeArgumentsInExpression()
                ? typeArguments
                : null;
        }

        private bool CanFollowTypeArgumentsInExpression()
        {
            switch (m_token)
            {
                case SyntaxKind.OpenParenToken: // foo<x>(
                // this case are the only case where this token can legally follow a type argument
                // list.  So we definitely want to treat this as a type arg list.
                case SyntaxKind.DotToken: // foo<x>.
                case SyntaxKind.CloseParenToken: // foo<x>)
                case SyntaxKind.CloseBracketToken: // foo<x>]
                case SyntaxKind.ColonToken: // foo<x>:
                case SyntaxKind.SemicolonToken: // foo<x>;
                case SyntaxKind.QuestionToken: // foo<x>?
                case SyntaxKind.EqualsEqualsToken: // foo<x> ==
                case SyntaxKind.EqualsEqualsEqualsToken: // foo<x> ===
                case SyntaxKind.ExclamationEqualsToken: // foo<x> !=
                case SyntaxKind.ExclamationEqualsEqualsToken: // foo<x> !==
                case SyntaxKind.AmpersandAmpersandToken: // foo<x> &&
                case SyntaxKind.BarBarToken: // foo<x> ||
                case SyntaxKind.CaretToken: // foo<x> ^
                case SyntaxKind.AmpersandToken: // foo<x> &
                case SyntaxKind.BarToken: // foo<x> |
                case SyntaxKind.CloseBraceToken: // foo<x> }
                case SyntaxKind.EndOfFileToken: // foo<x>
                    // these cases can't legally follow a type arg list.  However, they're not legal
                    // expressions either.  The user is probably in the middle of a generic type. So
                    // treat it as such.
                    return true;

                case SyntaxKind.CommaToken: // foo<x>,
                case SyntaxKind.OpenBraceToken: // foo<x> {
                                                // We don't want to treat these as type arguments.  Otherwise we'll parse this
                                                // as an invocation expression.  Instead, we want to parse out the expression
                                                // in isolation from the type arguments.
                default:
                    // Anything else treat as an expression.
                    return false;
            }
        }

        // MemberExpression parseMemberExpressionOrHigher()
        private ILeftHandSideExpression ParseMemberExpressionOrHigher()
        {
            // to Note make our lives simpler, we decompose the the NewExpression productions and
            // place ObjectCreationExpression and FunctionExpression into PrimaryExpression.
            // like so:
            //
            //   PrimaryExpression : See 11.1
            //      this
            //      Identifier
            //      Literal
            //      ArrayLiteral
            //      ObjectLiteral
            //      (Expression)
            //      FunctionExpression
            //      new MemberExpression Arguments?
            //
            //   MemberExpression : See 11.2
            //      PrimaryExpression
            //      MemberExpression[Expression]
            //      MemberExpression.IdentifierName
            //
            //   CallExpression : See 11.2
            //      MemberExpression
            //      CallExpression Arguments
            //      CallExpression[Expression]
            //      CallExpression.IdentifierName
            //
            // Technically this is ambiguous.  i.e., CallExpression defines:
            //
            //   CallExpression:
            //      CallExpression Arguments
            //
            // If you see: "new Foo()"
            //
            // Then that could be treated as a single ObjectCreationExpression, or it could be
            // treated as the invocation of "new Foo".  We disambiguate that in code (to match
            // the original grammar) by making sure that if we see an ObjectCreationExpression
            // we always consume arguments if they are there. So we treat "new Foo()" as an
            // object creation only, and not at all as an invocation)  Another way to think
            // about this is that for every "new" that we see, we will consume an argument list if
            // it is there as part of the *associated* object creation node.  Any additional
            // argument lists we see, will become invocation expressions.
            //
            // Because there are no other places in the grammar now that refer to FunctionExpression
            // or ObjectCreationExpression, it is safe to push down into the PrimaryExpression
            // production.
            //
            // Because CallExpression and MemberExpression are left recursive, we need to bottom out
            // of the recursion immediately.  So we parse out a primary expression to start with.
            var expression = ParsePrimaryExpression();
            return ParseMemberExpressionRest(expression);
        }

        private ILeftHandSideExpression ParseMemberExpressionRest(ILeftHandSideExpression expression)
        {
            while (true)
            {
                if (ParseOptional(SyntaxKind.DotToken))
                {
                    var propertyAccess = CreateNode<PropertyAccessExpression>(SyntaxKind.PropertyAccessExpression, expression.Pos, expression.GetLeadingTriviaLength(m_sourceFile));
                    propertyAccess.Expression = expression;

                    // DScript specific:
                    // Not parsing or saving dot tokens, because they consume memory and adds no value.
                    // propertyAccess.DotToken = dotToken;
                    propertyAccess.Name = ParseRightSideOfDot(/*allowIdentifierNames*/ true);
                    expression = FinishNode(propertyAccess);
                    continue;
                }

                // when in the [Decorator] context, we do not parse ElementAccess as it could be part of a ComputedPropertyName
                if (!InDecoratorContext() && ParseOptional(SyntaxKind.OpenBracketToken))
                {
                    var indexedAccess = CreateNode<ElementAccessExpression>(SyntaxKind.ElementAccessExpression, expression.Pos, expression.GetLeadingTriviaLength(m_sourceFile));
                    indexedAccess.Expression = expression;

                    // It's not uncommon for a user to write: "new Type[]".
                    // Check for that common pattern and report a better error message.
                    if (m_token != SyntaxKind.CloseBracketToken)
                    {
                        indexedAccess.ArgumentExpression = AllowInAnd(this, p => p.ParseExpression());
                        if (indexedAccess.ArgumentExpression.Kind == SyntaxKind.StringLiteral || indexedAccess.ArgumentExpression.Kind == SyntaxKind.NumericLiteral)
                        {
                            var literal = indexedAccess.ArgumentExpression.Cast<ILiteralExpression>();
                            literal.Text = InternIdentifier(literal.Text);
                        }
                    }

                    ParseExpected(SyntaxKind.CloseBracketToken);
                    expression = FinishNode(indexedAccess);
                    continue;
                }

                if (m_token == SyntaxKind.NoSubstitutionTemplateLiteral || m_token == SyntaxKind.TemplateHead)
                {
                    var tagExpression = CreateNode<TaggedTemplateExpression>(SyntaxKind.TaggedTemplateExpression, expression.Pos, expression.GetLeadingTriviaLength(m_sourceFile));
                    tagExpression.Tag = expression;

                    // DScript-specific. We retrieve the factory name to be able to do DScript-specific scanning
                    // on path-like templates
                    var factoryName = expression.Kind == SyntaxKind.Identifier ? expression.Cast<IIdentifier>().Text : null;

                    // Backlashes are allowed when it is a path-like literal and the associated configuration flag does not disallow it
                    var backslashesAreAllowed = m_parsingOptions?.AllowBackslashesInPathInterpolation == true &&
                                                              Scanner.IsPathLikeInterpolationFactory(factoryName);

                    var template = m_token == SyntaxKind.NoSubstitutionTemplateLiteral
                        ? (IPrimaryExpression)ParseLiteralNodeFactory(factoryName)
                        : ParseTemplateExpression(backslashesAreAllowed);

                    // Need to patch a location, because expression can be cached.
                    tagExpression.Pos = template.Pos - 1;
                    tagExpression.SetLeadingTriviaLength(Scanner.SkipOverTrivia(m_sourceText, m_sourceFile.BackslashesAllowedInPathInterpolation, tagExpression.Pos) - tagExpression.Pos, m_sourceFile);
                    tagExpression.TemplateExpression = template;

                    expression = FinishNode(tagExpression);
                    continue;
                }

                return expression;
            }
        }

        /// <nodoc />
        protected virtual ILiteralExpression ParseLiteralNodeFactory([CanBeNull]string factoryName)
        {
            return ParseLiteralNode();
        }

        private ITemplateExpression ParseTemplateExpression(bool backslashesAreAllowed)
        {
            var template = CreateNode<TemplateExpression>(SyntaxKind.TemplateExpression);

            template.Head = ParseTemplateLiteralFragmentHead();
            Contract.Assert(template.Head.Kind == SyntaxKind.TemplateHead, "Template head has wrong token kind");

            var pos = GetNodePos();

            // Special casing template strings with 1 and 2 taggs to avoid redundant allocations.
            var firstSpan = ParseTemplateSpan(backslashesAreAllowed);

            if (firstSpan.Literal.Kind != SyntaxKind.TemplateMiddle)
            {
                // Template string with one element
                template.TemplateSpans = TemplateSpanNodeArray.Create(pos, GetNodeEnd(), firstSpan);
            }
            else
            {
                var secondSpan = ParseTemplateSpan(backslashesAreAllowed);
                if (secondSpan.Literal.Kind != SyntaxKind.TemplateMiddle)
                {
                    // Template string with 2 elements.
                    template.TemplateSpans = TemplateSpanNodeArray.Create(pos, GetNodeEnd(), firstSpan, secondSpan);
                }
                else
                {
                    // The least common case: more than 2 elements.
                    var spans = new List<ITemplateSpan> { firstSpan, secondSpan };

                    do
                    {
                        spans.Add(ParseTemplateSpan(backslashesAreAllowed));
                    }
                    while (spans.LastOrDefault()?.Literal.Kind == SyntaxKind.TemplateMiddle);

                    template.TemplateSpans = TemplateSpanNodeArray.Create(pos, GetNodePos(), spans);
                }
            }

            return FinishNode(template);
        }

        private ITemplateSpan ParseTemplateSpan(bool backslashesAreAllowed)
        {
            var span = CreateNode<TemplateSpan>(SyntaxKind.TemplateSpan);
            span.Expression = AllowInAnd(this, p => p.ParseExpression());

            ITemplateLiteralFragment literal = null;

            if (m_token == SyntaxKind.CloseBraceToken)
            {
                ReScanTemplateToken(backslashesAreAllowed);
                literal = ParseTemplateLiteralFragment();
            }
            else
            {
                literal = ParseExpectedToken<TemplateLiteralFragment>(SyntaxKind.TemplateTail, /*reportAtCurrentPosition*/ false, Errors.Token_expected, Scanner.TokenToString(SyntaxKind.CloseBraceToken));
            }

            span.Literal = literal;
            return FinishNode(span);
        }

        private SyntaxKind ReScanTemplateToken(bool backslashesAreAllowed)
        {
            return m_token = m_scanner.RescanTemplateToken(backslashesAreAllowed);
        }

        private bool InDecoratorContext()
        {
            return InContext(ParserContextFlags.Decorator);
        }

        private IPrimaryExpression ParsePrimaryExpression()
        {
            switch (m_token)
            {
                case SyntaxKind.NumericLiteral:
                case SyntaxKind.StringLiteral:
                case SyntaxKind.NoSubstitutionTemplateLiteral:
                    return ParseLiteralNode();
                case SyntaxKind.ThisKeyword:
                case SyntaxKind.SuperKeyword:
                case SyntaxKind.NullKeyword:
                case SyntaxKind.TrueKeyword:
                case SyntaxKind.FalseKeyword:
                    return ParseTokenNode<PrimaryExpression>();
                case SyntaxKind.OpenParenToken:
                    return ParseParenthesizedExpression();
                case SyntaxKind.OpenBracketToken:
                    return ParseArrayLiteralExpression();
                case SyntaxKind.OpenBraceToken:
                    return ParseObjectLiteralExpression();
                case SyntaxKind.AsyncKeyword:
                    // Async arrow functions are parsed earlier in parseAssignmentExpressionOrHigher.
                    // If we encounter `async [no LineTerminator here] function` then this is an async
                    // function; otherwise, its an identifier.
                    if (!LookAhead(this, p => p.NextTokenIsFunctionKeywordOnSameLine()))
                    {
                        break;
                    }

                    return ParseFunctionExpression();
                case SyntaxKind.ClassKeyword:
                    return ParseClassExpression();
                case SyntaxKind.FunctionKeyword:
                    return ParseFunctionExpression();
                case SyntaxKind.NewKeyword:
                    return ParseNewExpression();
                case SyntaxKind.SlashToken:
                case SyntaxKind.SlashEqualsToken:
                    if (ReScanSlashToken() == SyntaxKind.RegularExpressionLiteral)
                    {
                        return ParseLiteralNode();
                    }

                    break;
                case SyntaxKind.TemplateHead:
                    // This is the case of string interpolation (with no factories), so it is not a path-like interpolation
                    return ParseTemplateExpression(backslashesAreAllowed: false);
            }

            return ParseIdentifier(Errors.Expression_expected);
        }

        private INewExpression ParseNewExpression()
        {
            var node = CreateNode<NewExpression>(SyntaxKind.NewExpression);
            ParseExpected(SyntaxKind.NewKeyword);
            node.Expression = ParseMemberExpressionOrHigher();
            node.TypeArguments = TryParse(this, p => p.ParseTypeArgumentsInExpression());
            if (node.TypeArguments != null || m_token == SyntaxKind.OpenParenToken)
            {
                node.Arguments = ParseArgumentList();
            }

            return FinishNode(node);
        }

        private SyntaxKind ReScanSlashToken()
        {
            return m_token = m_scanner.RescanSlashToken();
        }

        private IFunctionExpression ParseFunctionExpression()
        {
            // GeneratorExpression:
            //      function* BindingIdentifier [Yield][opt](FormalParameters[Yield]){ GeneratorBody }
            //
            // FunctionExpression:
            //       BindingIdentifier[opt](FormalParameters){ FunctionBody }
            var saveDecoratorContext = InDecoratorContext();
            if (saveDecoratorContext)
            {
                SetDecoratorContext(/*val*/ false);
            }

            var node = CreateNode<FunctionExpression>(SyntaxKind.FunctionExpression);
            SetModifiers(node, ParseModifiers());
            ParseExpected(SyntaxKind.FunctionKeyword);
            node.AsteriskToken = ParseOptionalToken<TokenNode>(SyntaxKind.AsteriskToken);

            var isGenerator = node.AsteriskToken.HasValue;
            var isAsync = (node.Flags & NodeFlags.Async) != NodeFlags.None;

            var identifier =
                isGenerator && isAsync ? DoInYieldAndAwaitContext(this, p => p.ParseOptionalIdentifier()) :
                    isGenerator ? DoInYieldContext(this, p => p.ParseOptionalIdentifier()) :
                        isAsync ? DoInAwaitContext(this, p => p.ParseOptionalIdentifier()) :
                            ParseOptionalIdentifier();

            if (identifier != null)
            {
                node.Name = new PropertyName(identifier);
            }

            FillSignature(SyntaxKind.ColonToken, /*yieldContext*/ isGenerator, /*awaitContext*/ isAsync, /*requireCompleteParameterList*/ false, node);
            node.Body = new ConciseBody(ParseFunctionBlock(/*allowYield*/ isGenerator, /*allowAwait*/ isAsync, /*ignoreMissingOpenBrace*/ false));

            if (saveDecoratorContext)
            {
                SetDecoratorContext(/*val*/ true);
            }

            return FinishNode(node);
        }

        private T DoInYieldAndAwaitContext<T>(Parser parser, Func<Parser, T> func)
        {
            return DoInsideOfContext(parser, ParserContextFlags.Yield | ParserContextFlags.Await, func);
        }

        private T DoInYieldContext<T>(Parser parser, Func<Parser, T> func)
        {
            return DoInsideOfContext(parser, ParserContextFlags.Yield, func);
        }

        private bool NextTokenIsFunctionKeywordOnSameLine()
        {
            NextToken();
            return m_token == SyntaxKind.FunctionKeyword && !m_scanner.HasPrecedingLineBreak;
        }

        private IObjectLiteralExpression ParseObjectLiteralExpression()
        {
            var node = CreateNode<ObjectLiteralExpression>(SyntaxKind.ObjectLiteralExpression);
            ParseExpected(SyntaxKind.OpenBraceToken);
            if (m_scanner.HasPrecedingLineBreak)
            {
                node.Flags |= NodeFlags.MultiLine;
            }

            node.Properties = ParseDelimitedList(this, ParsingContext.ObjectLiteralMembers, p => p.ParseObjectLiteralElement(), /*considerSemicolonAsDelimeter*/ true);
            ParseExpected(SyntaxKind.CloseBraceToken);
            return FinishNode(node);
        }

        private IAccessorDeclaration TryParseAccessorDeclaration(int fullStart, int triviaLength, NodeArray<IDecorator> decorators, ModifiersArray modifiers)
        {
            if (ParseContextualModifier(SyntaxKind.GetKeyword))
            {
                return ParseAccessorDeclaration(SyntaxKind.GetAccessor, triviaLength, fullStart, decorators, modifiers);
            }
            else if (ParseContextualModifier(SyntaxKind.SetKeyword))
            {
                return ParseAccessorDeclaration(SyntaxKind.SetAccessor, triviaLength, fullStart, decorators, modifiers);
            }

            return null;
        }

        private IAccessorDeclaration ParseAccessorDeclaration(SyntaxKind kind, int fullStart, int triviaLength, NodeArray<IDecorator> decorators, ModifiersArray modifiers)
        {
            var node = CreateNode<AccessorDeclaration>(kind, fullStart, triviaLength);
            node.Decorators = decorators;
            SetModifiers(node, modifiers);
            node.Name = ParsePropertyName();
            FillSignature(SyntaxKind.ColonToken, /*yieldContext*/ false, /*awaitContext*/ false, /*requireCompleteParameterList*/ false, node);
            var block = ParseFunctionBlockOrSemicolon(/*isGenerator*/ false, /*isAsync*/ false);
            node.Body = (block == null) ? null : new ConciseBody(block);
            return FinishNode(node);
        }

        private bool ParseContextualModifier(SyntaxKind t)
        {
            return m_token == t && TryParse(this, p => p.NextTokenCanFollowModifier());
        }

        private IObjectLiteralElement ParseObjectLiteralElement()
        {
            var fullStart = m_scanner.StartPos;
            var triviaLength = GetLeadingTriviaLength(fullStart);
            var decorators = ParseDecorators();
            var modifiers = ParseModifiers();

            var accessor = TryParseAccessorDeclaration(fullStart, triviaLength, decorators, modifiers);
            if (accessor != null)
            {
                return accessor;
            }

            var asteriskToken = ParseOptionalToken<TokenNode>(SyntaxKind.AsteriskToken);
            var tokenIsIdentifier = IsIdentifier();
            var propertyName = ParsePropertyName();

            // Disallowing of optional property assignments happens in the grammar checker.
            var questionToken = ParseOptionalToken<TokenNode>(SyntaxKind.QuestionToken);
            if (asteriskToken != null || m_token == SyntaxKind.OpenParenToken || m_token == SyntaxKind.LessThanToken)
            {
                return ParseMethodDeclaration(fullStart, triviaLength, decorators, modifiers, asteriskToken, propertyName, questionToken);
            }

            // check if it is short-hand property assignment or normal property assignment
            // if NOTE token is EqualsToken it is interpreted as CoverInitializedName production
            // CoverInitializedName[Yield] :
            //     IdentifierReference[?Yield] Initializer[In, ?Yield]
            // this is necessary because ObjectLiteral productions are also used to cover grammar for ObjectAssignmentPattern
            var isShorthandPropertyAssignment =
                tokenIsIdentifier && (m_token == SyntaxKind.CommaToken || m_token == SyntaxKind.CloseBraceToken || m_token == SyntaxKind.EqualsToken);

            if (isShorthandPropertyAssignment)
            {
                var shorthandDeclaration = CreateNode<ShorthandPropertyAssignment>(SyntaxKind.ShorthandPropertyAssignment, fullStart, triviaLength);
                m_scanner.MoveTrivia(propertyName, shorthandDeclaration);
                shorthandDeclaration.Name = propertyName;
                shorthandDeclaration.QuestionToken = questionToken;
                var equalsToken = ParseOptionalToken<TokenNode>(SyntaxKind.EqualsToken);
                if (equalsToken != null)
                {
                    shorthandDeclaration.EqualsToken = equalsToken;
                    shorthandDeclaration.ObjectAssignmentInitializer = AllowInAnd(this, p => p.ParseAssignmentExpressionOrHigher());
                }

                return FinishNode(shorthandDeclaration);
            }
            else
            {
                var propertyAssignment = CreateNode<PropertyAssignment>(SyntaxKind.PropertyAssignment, fullStart, triviaLength);
                m_scanner.MoveTrivia(propertyName, propertyAssignment);
                propertyAssignment.Modifiers = modifiers;
                propertyAssignment.Name = propertyName;
                propertyAssignment.QuestionToken = questionToken;
                ParseExpected(SyntaxKind.ColonToken);
                propertyAssignment.Initializer = AllowInAnd(this, p => p.ParseAssignmentExpressionOrHigher());
                return FinishNode(propertyAssignment);
            }
        }

        private IMethodDeclaration ParseMethodDeclaration(int fullStart, int triviaLength, NodeArray<IDecorator> decorators, ModifiersArray modifiers, INode asteriskToken, PropertyName name, INode questionToken, IDiagnosticMessage diagnosticMessage = null)
        {
            var method = CreateNode<MethodDeclaration>(SyntaxKind.MethodDeclaration, fullStart, triviaLength);
            m_scanner.MoveTrivia(name, method);
            method.Decorators = decorators;
            SetModifiers(method, modifiers);

            // TODO: why Option.Create is needed in those cases! Implicit conversion should work just fine
            method.AsteriskToken = Optional.Create(asteriskToken);
            method.Name = name;
            method.QuestionToken = Optional.Create(questionToken);
            var isGenerator = asteriskToken != null;
            var isAsync = (method.Flags & NodeFlags.Async) != NodeFlags.None;
            FillSignature(SyntaxKind.ColonToken, /*yieldContext*/ isGenerator, /*awaitContext*/ isAsync, /*requireCompleteParameterList*/ false, method);
            method.Body = ParseFunctionBlockOrSemicolon(isGenerator, isAsync, diagnosticMessage);
            return FinishNode(method);
        }

        private IArrayLiteralExpression ParseArrayLiteralExpression()
        {
            var node = CreateNode<ArrayLiteralExpression>(SyntaxKind.ArrayLiteralExpression);
            ParseExpected(SyntaxKind.OpenBracketToken);
            if (m_scanner.HasPrecedingLineBreak)
            {
                node.Flags |= NodeFlags.MultiLine;
            }

            node.Elements = ParseDelimitedList(this, ParsingContext.ArrayLiteralMembers, p => p.ParseArgumentOrArrayLiteralElement());
            ParseExpected(SyntaxKind.CloseBracketToken);
            return FinishNode(node);
        }

        private IExpression ParseArgumentOrArrayLiteralElement()
        {
            return m_token == SyntaxKind.DotDotDotToken ? ParseSpreadElement() :
                m_token == SyntaxKind.CommaToken ? CreateNode<Expression>(SyntaxKind.OmittedExpression) :
                    ParseAssignmentExpressionOrHigher();
        }

        private IExpression ParseSpreadElement()
        {
            var node = CreateNode<SpreadElementExpression>(SyntaxKind.SpreadElementExpression);
            ParseExpected(SyntaxKind.DotDotDotToken);
            node.Expression = ParseAssignmentExpressionOrHigher();
            return FinishNode(node);
        }

        private IParenthesizedExpression ParseParenthesizedExpression()
        {
            var node = CreateNode<ParenthesizedExpression>(SyntaxKind.ParenthesizedExpression);
            ParseExpected(SyntaxKind.OpenParenToken);
            node.Expression = AllowInAnd(this, p => p.ParseExpression());
            ParseExpected(SyntaxKind.CloseParenToken);
            return FinishNode(node);
        }

        private IMemberExpression ParseSuperExpression()
        {
            var expression = ParseTokenNode<PrimaryExpression>();
            if (m_token == SyntaxKind.OpenParenToken || m_token == SyntaxKind.DotToken || m_token == SyntaxKind.OpenBracketToken)
            {
                return expression;
            }

            // If we have seen "super" it must be followed by '(' or '.'.
            // If it wasn't then just try to parse out a '.' and report an error.
            var node = CreateNode<PropertyAccessExpression>(SyntaxKind.PropertyAccessExpression, expression.Pos, expression.GetLeadingTriviaLength(m_sourceFile));
            node.Expression = expression;
            node.DotToken = ParseExpectedToken(SyntaxKind.DotToken, /*reportAtCurrentPosition*/ false,
                Errors.Super_must_be_followed_by_an_argument_list_or_member_access);
            node.Name = ParseRightSideOfDot(/*allowIdentifierNames*/ true);
            return FinishNode(node);
        }

        private IStatement ParseDebuggerStatement()
        {
            var node = CreateNode<Statement>(SyntaxKind.DebuggerStatement);
            ParseExpected(SyntaxKind.DebuggerKeyword);
            ParseSemicolon();
            return FinishNode(node);
        }

        private IThrowStatement ParseThrowStatement()
        {
            // ThrowStatement[Yield] :
            //      throw [no LineTerminator here]Expression[In, ?Yield];

            // Because of automatic semicolon insertion, we need to report error if this
            // throw could be terminated with a semicolon.  Note: we can't call 'parseExpression'
            // directly as that might consume an expression on the following line.
            // We just return 'undefined' in that case.  The actual error will be reported in the
            // grammar walker.
            var node = CreateNode<ThrowStatement>(SyntaxKind.ThrowStatement);
            ParseExpected(SyntaxKind.ThrowKeyword);
            node.Expression = m_scanner.HasPrecedingLineBreak ? null : AllowInAnd(this, p => p.ParseExpression());
            ParseSemicolon();
            return FinishNode(node);
        }

        // TODO: Review for error recovery
        private ITryStatement ParseTryStatement()
        {
            var node = CreateNode<TryStatement>(SyntaxKind.TryStatement);

            ParseExpected(SyntaxKind.TryKeyword);
            node.TryBlock = ParseBlock(/*ignoreMissingOpenBrace*/ false);
            node.CatchClause = m_token == SyntaxKind.CatchKeyword ? ParseCatchClause() : null;

            // If we don't have a catch clause, then we must have a finally clause.  Try to parse
            // one out no matter what.
            if (node.CatchClause != null || m_token == SyntaxKind.FinallyKeyword)
            {
                ParseExpected(SyntaxKind.FinallyKeyword);
                node.FinallyBlock = ParseBlock(/*ignoreMissingOpenBrace*/ false);
            }

            return FinishNode(node);
        }

        private ICatchClause ParseCatchClause()
        {
            var result = CreateNode<CatchClause>(SyntaxKind.CatchClause);
            ParseExpected(SyntaxKind.CatchKeyword);
            if (ParseExpected(SyntaxKind.OpenParenToken))
            {
                result.VariableDeclaration = ParseVariableDeclaration();
            }

            ParseExpected(SyntaxKind.CloseParenToken);
            result.Block = ParseBlock(/*ignoreMissingOpenBrace*/ false);
            return FinishNode(result);
        }

        private IBindingElement ParseObjectBindingElement()
        {
            var node = CreateNode<BindingElement>(SyntaxKind.BindingElement);
            var tokenIsIdentifier = IsIdentifier();
            var propertyName = ParsePropertyName();
            if (tokenIsIdentifier && m_token != SyntaxKind.ColonToken)
            {
                node.Name = new IdentifierOrBindingPattern(propertyName);
            }
            else
            {
                ParseExpected(SyntaxKind.ColonToken);
                node.PropertyName = propertyName;
                node.Name = ParseIdentifierOrPattern();
            }

            node.Initializer = ParseBindingElementInitializer(/*inParameter*/ false);
            return FinishNode(node);
        }

        private IBindingPattern ParseObjectBindingPattern()
        {
            var node = CreateNode<BindingPattern>(SyntaxKind.ObjectBindingPattern);
            ParseExpected(SyntaxKind.OpenBraceToken);
            node.Elements = ParseDelimitedList(this, ParsingContext.ObjectBindingElements, p => p.ParseObjectBindingElement());
            ParseExpected(SyntaxKind.CloseBraceToken);
            return FinishNode(node);
        }

        private IBindingPattern ParseArrayBindingPattern()
        {
            var node = CreateNode<BindingPattern>(SyntaxKind.ArrayBindingPattern);
            ParseExpected(SyntaxKind.OpenBracketToken);
            node.Elements = ParseDelimitedList(this, ParsingContext.ArrayBindingElements, p => p.ParseArrayBindingElement());
            ParseExpected(SyntaxKind.CloseBracketToken);
            return FinishNode(node);
        }

        private IBindingElement ParseArrayBindingElement()
        {
            if (m_token == SyntaxKind.CommaToken)
            {
                return CreateNode<BindingElement>(SyntaxKind.OmittedExpression);
            }

            var node = CreateNode<BindingElement>(SyntaxKind.BindingElement);
            node.DotDotDotToken = ParseOptionalToken<TokenNode>(SyntaxKind.DotDotDotToken);
            node.Name = ParseIdentifierOrPattern();
            node.Initializer = ParseBindingElementInitializer(/*inParameter*/ false);
            return FinishNode(node);
        }

        private IdentifierOrBindingPattern ParseIdentifierOrPattern()
        {
            if (m_token == SyntaxKind.OpenBracketToken)
            {
                return new IdentifierOrBindingPattern(ParseArrayBindingPattern());
            }

            if (m_token == SyntaxKind.OpenBraceToken)
            {
                return new IdentifierOrBindingPattern(ParseObjectBindingPattern());
            }

            return new IdentifierOrBindingPattern(ParseIdentifier());
        }

        private ITypeNode ParseTypeWorker()
        {
            if (IsStartOfFunctionType())
            {
                return ParseFunctionOrConstructorType(SyntaxKind.FunctionType);
            }

            if (m_token == SyntaxKind.NewKeyword)
            {
                return ParseFunctionOrConstructorType(SyntaxKind.ConstructorType);
            }

            return ParseUnionTypeOrHigher();
        }

        private ITypeNode ParseUnionTypeOrHigher()
        {
            return ParseUnionOrIntersectionType(SyntaxKind.UnionType, p => p.ParseIntersectionTypeOrHigher(), SyntaxKind.BarToken);
        }

        private ITypeNode ParseUnionOrIntersectionType(SyntaxKind kind, Func<Parser, ITypeNode> parseConstituentType, SyntaxKind @operator)
        {
            var type = parseConstituentType(this);
            if (m_token == @operator)
            {
                var types = new NodeArray<ITypeNode>(type);
                types.Pos = type.Pos;
                while (ParseOptional(@operator))
                {
                    types.Add(parseConstituentType(this));
                }

                types.End = GetNodeEnd();
                var node = CreateNode<UnionOrIntersectionTypeNode>(kind, type.Pos, type.GetLeadingTriviaLength(m_sourceFile));
                node.Types = types;
                type = FinishNode(node);
            }

            return type;
        }

        private ITypeNode ParseIntersectionTypeOrHigher()
        {
            return ParseUnionOrIntersectionType(SyntaxKind.IntersectionType, p => p.ParseArrayTypeOrHigher(), SyntaxKind.AmpersandToken);
        }

        private ITypeNode ParseArrayTypeOrHigher()
        {
            // DScript-specific: type literals support decorators.
            var decorators = ParseDecorators();

            var type = ParseNonArrayType();
            while (!m_scanner.HasPrecedingLineBreak && ParseOptional(SyntaxKind.OpenBracketToken))
            {
                ParseExpected(SyntaxKind.CloseBracketToken);
                var node = CreateNode<ArrayTypeNode>(SyntaxKind.ArrayType, type.Pos, type.GetLeadingTriviaLength(m_sourceFile));
                node.ElementType = type;
                type = FinishNode(node);
            }

            type.Decorators = decorators;
            return type;
        }

        private ITypeNode ParseNonArrayType()
        {
            switch (m_token)
            {
                case SyntaxKind.AnyKeyword:
                case SyntaxKind.StringKeyword:
                case SyntaxKind.NumberKeyword:
                case SyntaxKind.BooleanKeyword:
                case SyntaxKind.SymbolKeyword:
                    // If these are followed by a dot, then parse these out as a dotted type reference instead.
                    var node = TryParse(this, p => p.ParseKeywordAndNoDot());
                    return node ?? ParseTypeReferenceOrTypePredicate();
                case SyntaxKind.StringLiteral:
                    return ParseStringLiteralTypeNode();
                case SyntaxKind.VoidKeyword:
                    return ParseTokenNode<TypeNode>();
                case SyntaxKind.ThisKeyword:
                    {
                        var thisKeyword = ParseThisTypeNode();
                        if (m_token == SyntaxKind.IsKeyword && !m_scanner.HasPrecedingLineBreak)
                        {
                            return ParseTypePredicate(new IdentifierOrThisTypeUnionNode(thisKeyword));
                        }
                        else
                        {
                            return thisKeyword;
                        }
                    }

                case SyntaxKind.TypeOfKeyword:
                    return ParseTypeQuery();
                case SyntaxKind.OpenBraceToken:
                    return ParseTypeLiteral();
                case SyntaxKind.OpenBracketToken:
                    return ParseTupleType();
                case SyntaxKind.OpenParenToken:
                    return ParseParenthesizedType();
                default:
                    return ParseTypeReferenceOrTypePredicate();
            }
        }

        private ITypeQueryNode ParseTypeQuery()
        {
            var node = CreateNode<TypeQueryNode>(SyntaxKind.TypeQuery);
            ParseExpected(SyntaxKind.TypeOfKeyword);
            node.ExprName = ParseEntityName(/*allowReservedWords*/ true);
            return FinishNode(node);
        }

        private ITypeLiteralNode ParseTypeLiteral()
        {
            var node = CreateNode<TypeLiteralNode>(SyntaxKind.TypeLiteral);
            node.Members = ParseObjectTypeMembers();
            return FinishNode(node);
        }

        private NodeArray<ITypeElement> ParseObjectTypeMembers()
        {
            NodeArray<ITypeElement> members = null;
            if (ParseExpected(SyntaxKind.OpenBraceToken))
            {
                members = ParseList(this, ParsingContext.TypeMembers, p => p.ParseTypeMember());
                ParseExpected(SyntaxKind.CloseBraceToken);
            }
            else
            {
                members = CreateMissingList<ITypeElement>();
            }

            return members;
        }

        private ITypeElement ParseTypeMember()
        {
            // Note, parseTypeMember was reimplemented base on recent changes in the typescript parser
            if (m_token == SyntaxKind.OpenParenToken || m_token == SyntaxKind.LessThanToken)
            {
                return ParseSignatureMember(SyntaxKind.CallSignature);
            }

            if (m_token == SyntaxKind.NewKeyword && LookAhead(this, p => p.IsStartOfConstructSignature()))
            {
                return ParseSignatureMember(SyntaxKind.ConstructSignature);
            }

            var fullStart = GetNodePos();
            var triviaLength = GetLeadingTriviaLength(fullStart);
            var modifiers = ParseModifiers();
            if (IsIndexSignature())
            {
                var decorators = ParseDecorators();
                return ParseIndexSignatureDeclaration(fullStart, triviaLength, /*decorators*/ decorators, modifiers);
            }

            return ParsePropertyOrMethodSignature(fullStart, triviaLength, modifiers);
        }

        private bool IsStartOfConstructSignature()
        {
            NextToken();
            return m_token == SyntaxKind.OpenParenToken || m_token == SyntaxKind.LessThanToken;
        }

        private IIndexSignatureDeclaration ParseIndexSignatureDeclaration(int fullStart, int triviaLength, NodeArray<IDecorator> decorators, ModifiersArray modifiers)
        {
            var node = CreateNode<IndexSignatureDeclaration>(SyntaxKind.IndexSignature, fullStart, triviaLength);
            node.Decorators = decorators;
            SetModifiers(node, modifiers);
            node.Parameters = ParseBracketedList(this, ParsingContext.Parameters, p => p.ParseParameter(), SyntaxKind.OpenBracketToken, SyntaxKind.CloseBracketToken);
            node.Type = ParseTypeAnnotation();
            ParseTypeMemberSemicolon();
            return FinishNode(node);
        }

        private PropertySignatureOrMethodSignature ParsePropertyOrMethodSignature(int fullStart, int triviaLength, ModifiersArray modifiers)
        {
            var decorators = ParseDecorators();
            var name = ParsePropertyName();
            var questionToken = ParseOptionalToken<TokenNode>(SyntaxKind.QuestionToken);

            if (m_token == SyntaxKind.OpenParenToken || m_token == SyntaxKind.LessThanToken)
            {
                var method = CreateNode<MethodSignature>(SyntaxKind.MethodSignature, fullStart, triviaLength);
                m_scanner.MoveTrivia(name, method);
                SetModifiers(method, modifiers);
                method.Name = name;
                method.Decorators = decorators;
                method.QuestionToken = questionToken;

                // Method signatues don't exist in expression contexts.  So they have neither
                // [Yield] nor [Await]
                FillSignature(SyntaxKind.ColonToken, /*yieldContext*/ false, /*awaitContext*/ false, /*requireCompleteParameterList*/ false, method);
                ParseTypeMemberSemicolon();
                return new PropertySignatureOrMethodSignature(FinishNode(method));
            }

            var property = CreateNode<PropertySignature>(SyntaxKind.PropertySignature, fullStart, triviaLength);
            m_scanner.MoveTrivia(name, property);
            SetModifiers(property, modifiers);
            property.Name = name;
            property.QuestionToken = questionToken;
            property.Type = ParseTypeAnnotation();
            property.Decorators = decorators;

            if (m_token == SyntaxKind.EqualsToken)
            {
                // Although type literal properties cannot not have initializers, we attempt
                // to parse an initializer so we can report in the checker that an interface
                // property or type literal property cannot have an initializer.
                property.Initializer = ParseNonParameterInitializer();
            }

            ParseTypeMemberSemicolon();
            return new PropertySignatureOrMethodSignature(FinishNode(property));
        }

        private IParameterDeclaration ParseParameter()
        {
            var node = CreateNode<ParameterDeclaration>(SyntaxKind.Parameter);
            node.Decorators = ParseDecorators();
            SetModifiers(node, ParseModifiers());
            node.DotDotDotToken = ParseOptionalToken<TokenNode>(SyntaxKind.DotDotDotToken);

            // FormalParameter [Yield,Await]:
            //      BindingElement[?Yield,?Await]
            node.Name = ParseIdentifierOrPattern();

            if (Types.NodeUtilities.GetFullWidth(node.Name) == 0 && node.Flags == NodeFlags.None && m_token.IsModifierKind())
            {
                // in cases like
                // 'use strict'
                //  foo(static)
                // isParameter('static') == true, because of isModifier('static')
                // however 'static' is not a legal identifier in a strict mode.
                // so result of this  will be ParameterDeclaration (flags = 0, Name = missing, type = null, initializer = null)
                // and current token will not change => parsing of the enclosing parameter list will last till the end of time (or OOM)
                // to avoid this we'll advance cursor to the next token.
                NextToken();
            }

            node.QuestionToken = ParseOptionalToken<TokenNode>(SyntaxKind.QuestionToken);
            node.Type = ParseParameterType();
            node.Initializer = ParseBindingElementInitializer(/*inParameter*/ true);

            // Do not check for initializers in an ambient context for parameters. This is not
            // a grammar error because the grammar allows arbitrary call signatures in
            // an ambient context.
            // It is actually not necessary for this to be an error at all. The reason is that
            // function/varructor implementations are syntactically disallowed in ambient
            // contexts. In addition, parameter initializers are semantically disallowed in
            // overload signatures. So parameter initializers are transitively disallowed in
            // ambient contexts.
            return FinishNode(node);
        }

        private IExpression ParseBindingElementInitializer(bool inParameter)
        {
            return inParameter ? ParseParameterInitializer() : ParseNonParameterInitializer();
        }

        private IExpression ParseParameterInitializer()
        {
            return ParseInitializer(/*inParameter*/ true);
        }

        private ITypeNode ParseParameterType()
        {
            if (ParseOptional(SyntaxKind.ColonToken))
            {
                return ParseType();
            }

            return null;
        }

        private ICallSignatureDeclarationOrConstructSignatureDeclaration ParseSignatureMember(SyntaxKind kind)
        {
            var node = CreateNode<CallSignatureDeclarationOrConstructSignatureDeclaration>(kind);
            if (kind == SyntaxKind.ConstructSignature)
            {
                ParseExpected(SyntaxKind.NewKeyword);
            }

            FillSignature(SyntaxKind.ColonToken, /*yieldContext*/ false, /*awaitContext*/ false, /*requireCompleteParameterList*/ false, node);
            ParseTypeMemberSemicolon();
            return FinishNode(node);
        }

        private void ParseTypeMemberSemicolon()
        {
            // We allow type members to be separated by commas or (possibly ASI) semicolons.
            // First check if it was a comma.  If so, we're done with the member.
            if (ParseOptional(SyntaxKind.CommaToken))
            {
                return;
            }

            // Didn't have a comma.  We must have a (possible ASI) semicolon.
            ParseSemicolon();
        }

        private ITupleTypeNode ParseTupleType()
        {
            var node = CreateNode<TupleTypeNode>(SyntaxKind.TupleType);
            node.ElementTypes = ParseBracketedList(this, ParsingContext.TupleElementTypes, p => p.ParseType(), SyntaxKind.OpenBracketToken, SyntaxKind.CloseBracketToken);
            return FinishNode(node);
        }

        private IParenthesizedTypeNode ParseParenthesizedType()
        {
            var node = CreateNode<ParenthesizedTypeNode>(SyntaxKind.ParenthesizedType);
            ParseExpected(SyntaxKind.OpenParenToken);
            node.Type = ParseType();
            ParseExpected(SyntaxKind.CloseParenToken);
            return FinishNode(node);
        }

        private IThisTypeNode ParseThisTypeNode()
        {
            var node = CreateNode<ThisTypeNode>(SyntaxKind.ThisType) as IThisTypeNode;
            NextToken();
            return FinishNode(node);
        }

        private ITypeNode ParseKeywordAndNoDot()
        {
            var node = ParseTokenNode<TypeNode>();
            return m_token == SyntaxKind.DotToken ? null : node;
        }

        private TypeReferenceUnionNodeOrTypePredicateUnionNode ParseTypeReferenceOrTypePredicate()
        {
            var typeName = ParseEntityName(/*allowReservedWords*/ false, Errors.Type_expected);
            if (typeName.Kind == SyntaxKind.Identifier && m_token == SyntaxKind.IsKeyword && !m_scanner.HasPrecedingLineBreak)
            {
                return new TypeReferenceUnionNodeOrTypePredicateUnionNode(ParseTypePredicate(new IdentifierOrThisTypeUnionNode(typeName.AsIdentifier())));
            }

            var node = CreateNode<TypeReferenceNode>(SyntaxKind.TypeReference, typeName.Pos, typeName.GetLeadingTriviaLength(m_sourceFile));
            node.TypeName = typeName;
            if (!m_scanner.HasPrecedingLineBreak && m_token == SyntaxKind.LessThanToken)
            {
                node.TypeArguments = ParseBracketedList<ITypeNode>(
                    this,
                    ParsingContext.TypeArguments,
                    p => p.ParseType(),
                    SyntaxKind.LessThanToken,
                    SyntaxKind.GreaterThanToken);
            }
            else
            {
                // This will ensures that TypeArguments property is not null.
                node.TypeArguments = null;
            }

            return new TypeReferenceUnionNodeOrTypePredicateUnionNode(FinishNode(node));
        }

        private bool IsStartOfFunctionType()
        {
            if (m_token == SyntaxKind.LessThanToken)
            {
                return true;
            }

            return m_token == SyntaxKind.OpenParenToken && LookAhead(this, p => p.IsUnambiguouslyStartOfFunctionType());
        }

        private ITypePredicateNode ParseTypePredicate(IdentifierOrThisTypeUnionNode lhs)
        {
            NextToken();
            var node = CreateNode<TypePredicateNode>(SyntaxKind.TypePredicate, lhs.Pos, lhs.GetLeadingTriviaLength(m_sourceFile));
            node.ParameterName = lhs;
            node.Type = ParseType();
            return FinishNode(node);
        }

        private bool IsUnambiguouslyStartOfFunctionType()
        {
            NextToken();
            if (m_token == SyntaxKind.CloseParenToken || m_token == SyntaxKind.DotDotDotToken)
            {
                // ( )
                // ( ...
                return true;
            }

            if (IsIdentifier() || m_token.IsModifierKind())
            {
                NextToken();
                if (m_token == SyntaxKind.ColonToken || m_token == SyntaxKind.CommaToken ||
                    m_token == SyntaxKind.QuestionToken || m_token == SyntaxKind.EqualsToken ||
                    IsIdentifier() || m_token.IsModifierKind())
                {
                    // ( id :
                    // ( id ,
                    // ( id ?
                    // ( id =
                    // ( modifier id
                    return true;
                }

                if (m_token == SyntaxKind.CloseParenToken)
                {
                    NextToken();
                    if (m_token == SyntaxKind.EqualsGreaterThanToken)
                    {
                        // ( id ) =>
                        return true;
                    }
                }
            }

            return false;
        }

        private IFunctionOrConstructorTypeNode ParseFunctionOrConstructorType(SyntaxKind kind)
        {
            var node = CreateNode<FunctionOrConstructorTypeNode>(kind);
            if (kind == SyntaxKind.ConstructorType)
            {
                ParseExpected(SyntaxKind.NewKeyword);
            }

            FillSignature(SyntaxKind.EqualsGreaterThanToken, /*yieldContext*/ false, /*awaitContext*/ false, /*requireCompleteParameterList*/ false, node);
            return FinishNode(node);
        }

        private ITypeNode ParseType()
        {
            // The rules about 'yield' only apply to actual code/expression contexts.  They don't
            // apply to 'type' contexts.  So we disable these parameters here before moving on.
            return DoOutsideOfContext(this, ParserContextFlags.TypeExcludesFlags, p => p.ParseTypeWorker());
        }

        private ITypeNode ParseTypeAnnotation()
        {
            return ParseOptional(SyntaxKind.ColonToken) ? ParseType() : null;
        }

        private static bool IsInOrOfKeyword(SyntaxKind t)
        {
            return t == SyntaxKind.InKeyword || t == SyntaxKind.OfKeyword;
        }

        private IVariableDeclaration ParseVariableDeclaration()
        {
            var node = CreateNode<VariableDeclaration>(SyntaxKind.VariableDeclaration);
            node.Name = ParseIdentifierOrPattern();
            node.Type = ParseTypeAnnotation();
            if (!IsInOrOfKeyword(m_token))
            {
                node.Initializer = ParseInitializer(/*inParameter*/ false);
            }

            return FinishNode(node);
        }

        private CaseClauseOrDefaultClause ParseCaseOrDefaultClause()
        {
            return m_token == SyntaxKind.CaseKeyword
                ? new CaseClauseOrDefaultClause(ParseCaseClause())
                : new CaseClauseOrDefaultClause(ParseDefaultClause());
        }

        private ICaseClause ParseCaseClause()
        {
            var node = CreateNode<CaseClause>(SyntaxKind.CaseClause);
            ParseExpected(SyntaxKind.CaseKeyword);
            node.Expression = AllowInAnd(this, p => p.ParseExpression());
            ParseExpected(SyntaxKind.ColonToken);
            node.Statements = ParseList(this, ParsingContext.SwitchClauseStatements, p => p.ParseStatement());
            return FinishNode(node);
        }

        private IDefaultClause ParseDefaultClause()
        {
            var node = CreateNode<DefaultClause>(SyntaxKind.DefaultClause);
            ParseExpected(SyntaxKind.DefaultKeyword);
            ParseExpected(SyntaxKind.ColonToken);
            node.Statements = ParseList(this, ParsingContext.SwitchClauseStatements, p => p.ParseStatement());
            return FinishNode(node);
        }

        private ISwitchStatement ParseSwitchStatement()
        {
            var node = CreateNode<SwitchStatement>(SyntaxKind.SwitchStatement);
            ParseExpected(SyntaxKind.SwitchKeyword);
            ParseExpected(SyntaxKind.OpenParenToken);
            node.Expression = AllowInAnd(this, p => p.ParseExpression());
            ParseExpected(SyntaxKind.CloseParenToken);
            var caseBlock = CreateNode<CaseBlock>(SyntaxKind.CaseBlock, m_scanner.StartPos, GetLeadingTriviaLength(m_scanner.StartPos));
            ParseExpected(SyntaxKind.OpenBraceToken);
            caseBlock.Clauses = ParseList(this, ParsingContext.SwitchClauses, p => p.ParseCaseOrDefaultClause());
            ParseExpected(SyntaxKind.CloseBraceToken);
            node.CaseBlock = FinishNode(caseBlock);
            return FinishNode(node);
        }

        private IBreakOrContinueStatement ParseBreakOrContinueStatement(SyntaxKind kind)
        {
            var node = CreateNode<BreakOrContinueStatement>(kind);

            ParseExpected(kind == SyntaxKind.BreakStatement ? SyntaxKind.BreakKeyword : SyntaxKind.ContinueKeyword);
            if (!CanParseSemicolon())
            {
                node.Label = ParseIdentifier();
            }

            ParseSemicolon();
            return FinishNode(node);
        }

        private IReturnStatement ParseReturnStatement()
        {
            var node = CreateNode<ReturnStatement>(SyntaxKind.ReturnStatement);

            ParseExpected(SyntaxKind.ReturnKeyword);
            if (!CanParseSemicolon())
            {
                node.Expression = AllowInAnd(this, p => p.ParseExpression());
            }

            ParseSemicolon();
            return FinishNode(node);
        }

        private IWithStatement ParseWithStatement()
        {
            var node = CreateNode<WithStatement>(SyntaxKind.WithStatement);
            ParseExpected(SyntaxKind.WithKeyword);
            ParseExpected(SyntaxKind.OpenParenToken);
            node.Expression = AllowInAnd(this, p => p.ParseExpression());
            ParseExpected(SyntaxKind.CloseParenToken);
            node.Statement = ParseStatement();
            return FinishNode(node);
        }

        private IWhileStatement ParseWhileStatement()
        {
            var node = CreateNode<WhileStatement>(SyntaxKind.WhileStatement);
            ParseExpected(SyntaxKind.WhileKeyword);
            ParseExpected(SyntaxKind.OpenParenToken);
            node.Expression = AllowInAnd(this, p => p.ParseExpression());
            ParseExpected(SyntaxKind.CloseParenToken);
            node.Statement = ParseStatement();
            return FinishNode(node);
        }

        private IStatement ParseForOrForInOrForOfStatement()
        {
            var pos = GetNodePos();
            var triviaLength = GetLeadingTriviaLength(pos);
            ParseExpected(SyntaxKind.ForKeyword);
            ParseExpected(SyntaxKind.OpenParenToken);
            VariableDeclarationListOrExpression initializer = null;
            if (m_token != SyntaxKind.SemicolonToken)
            {
                if (m_token == SyntaxKind.VarKeyword || m_token == SyntaxKind.LetKeyword || m_token == SyntaxKind.ConstKeyword)
                {
                    initializer = new VariableDeclarationListOrExpression(ParseVariableDeclarationList(/*inForStatementInitializer*/ true));
                }
                else
                {
                    initializer = new VariableDeclarationListOrExpression(DisallowInAnd(this, p => p.ParseExpression()));
                }
            }

            IIterationStatement forOrForInOrForOfStatement = null;
            if (ParseOptional(SyntaxKind.InKeyword))
            {
                var forInStatement = CreateNode<ForInStatement>(SyntaxKind.ForInStatement, pos, triviaLength);
                forInStatement.Initializer = initializer;
                forInStatement.Expression = AllowInAnd(this, p => p.ParseExpression());
                ParseExpected(SyntaxKind.CloseParenToken);
                forOrForInOrForOfStatement = forInStatement;
            }
            else if (ParseOptional(SyntaxKind.OfKeyword))
            {
                var forOfStatement = CreateNode<ForOfStatement>(SyntaxKind.ForOfStatement, pos, triviaLength);
                forOfStatement.Initializer = initializer;
                forOfStatement.Expression = AllowInAnd(this, p => p.ParseAssignmentExpressionOrHigher());
                ParseExpected(SyntaxKind.CloseParenToken);
                forOrForInOrForOfStatement = forOfStatement;
            }
            else
            {
                var forStatement = CreateNode<ForStatement>(SyntaxKind.ForStatement, pos, triviaLength);
                forStatement.Initializer = initializer;
                ParseExpected(SyntaxKind.SemicolonToken);
                if (m_token != SyntaxKind.SemicolonToken && m_token != SyntaxKind.CloseParenToken)
                {
                    forStatement.Condition = AllowInAnd(this, p => p.ParseExpression());
                }

                ParseExpected(SyntaxKind.SemicolonToken);
                if (m_token != SyntaxKind.CloseParenToken)
                {
                    forStatement.Incrementor = AllowInAnd(this, p => p.ParseExpression());
                }

                ParseExpected(SyntaxKind.CloseParenToken);
                forOrForInOrForOfStatement = forStatement;
            }

            forOrForInOrForOfStatement.Statement = ParseStatement();

            return FinishNode(forOrForInOrForOfStatement);
        }

        private IDoStatement ParseDoStatement()
        {
            var node = CreateNode<DoStatement>(SyntaxKind.DoStatement);
            ParseExpected(SyntaxKind.DoKeyword);
            node.Statement = ParseStatement();
            ParseExpected(SyntaxKind.WhileKeyword);
            ParseExpected(SyntaxKind.OpenParenToken);
            node.Expression = AllowInAnd(this, p => p.ParseExpression());
            ParseExpected(SyntaxKind.CloseParenToken);

            // From: https://mail.mozilla.org/pipermail/es-discuss/2011-August/016188.html
            // 157 min --- All allen at wirfs-brock.com CONF --- "do{;}while(false)false" prohibited in
            // spec but allowed in consensus reality. Approved -- this is the de-facto standard whereby
            //  do;while(0)x will have a semicolon inserted before x.
            ParseOptional(SyntaxKind.SemicolonToken);
            return FinishNode(node);
        }

        private IfStatement ParseIfStatement()
        {
            var node = CreateNode<IfStatement>(SyntaxKind.IfStatement);
            ParseExpected(SyntaxKind.IfKeyword);
            ParseExpected(SyntaxKind.OpenParenToken);
            node.Expression = AllowInAnd(this, p => p.ParseExpression());
            ParseExpected(SyntaxKind.CloseParenToken);
            node.ThenStatement = ParseStatement();
            node.ElseStatement = Optional.Create(ParseOptional(SyntaxKind.ElseKeyword) ? ParseStatement() : null);
            return FinishNode(node);
        }

        private IClassDeclaration ParseClassDeclaration(int fullStart, int triviaLength, NodeArray<IDecorator> decorators, ModifiersArray modifiers)
        {
            return (IClassDeclaration)ParseClassDeclarationOrExpression(fullStart, triviaLength, decorators, modifiers, SyntaxKind.ClassDeclaration);
        }

        /// <nodoc />
        protected virtual IFunctionDeclaration ParseFunctionDeclaration(int fullStart, int triviaLength, NodeArray<IDecorator> decorators, ModifiersArray modifiers)
        {
            var node = CreateNode<FunctionDeclaration>(SyntaxKind.FunctionDeclaration, fullStart, triviaLength);
            node.Decorators = decorators;

            if (decorators != null && decorators.Length > 0)
            {
                m_scanner.MoveTrivia(decorators[0], node);
            }

            SetModifiers(node, modifiers);
            ParseExpected(SyntaxKind.FunctionKeyword);

            // TODO: what type of generic needs to be used in parseOptionalToken???
            node.AsteriskToken = ParseOptionalToken<TokenNode>(SyntaxKind.AsteriskToken);
            node.Name = (node.Flags & NodeFlags.Default) != NodeFlags.None ? ParseOptionalIdentifier() : ParseIdentifier();
            var isGenerator = node.AsteriskToken == true;
            var isAsync = (node.Flags & NodeFlags.Async) != NodeFlags.None;
            FillSignature(SyntaxKind.ColonToken, /*yieldContext*/ isGenerator, /*awaitContext*/ isAsync, /*requireCompleteParameterList*/ false, node);

            // TODO: I've added cast to FunctionBody
            node.Body = (IBlock)ParseFunctionBlockOrSemicolon(isGenerator, isAsync, Errors.Or_expected);
            return FinishNode(node);
        }

        /// <summary>
        /// Factory method that creates <see cref="ClassDeclaration"/> or <see cref="ClassExpression"/>.
        /// This helper function is required in .NET implementation because of nominal types in C#.
        /// (In typescript it's not needed because instance is compatible to any interface if it has compatible set of properties).
        /// </summary>
        private IClassLikeDeclaration CreateClassDeclarationOrExpression(SyntaxKind kind, int fullStart, int triviaLength)
        {
            if (kind == SyntaxKind.ClassDeclaration)
            {
                return CreateNode<ClassDeclaration>(kind, fullStart, triviaLength);
            }

            return CreateNode<ClassExpression>(kind, fullStart, triviaLength);
        }

        private IClassLikeDeclaration ParseClassDeclarationOrExpression(int fullStart, int triviaLength, NodeArray<IDecorator> decorators, ModifiersArray modifiers, SyntaxKind kind)
        {
            IClassLikeDeclaration node = CreateClassDeclarationOrExpression(kind, fullStart, triviaLength);
            node.Decorators = decorators;
            SetModifiers(node, modifiers);
            ParseExpected(SyntaxKind.ClassKeyword);
            node.Name = ParseNameOfClassDeclarationOrExpression();
            node.TypeParameters = ParseTypeParameters();
            node.HeritageClauses = ParseHeritageClauses(/*isClassHeritageClause*/ true);

            if (ParseExpected(SyntaxKind.OpenBraceToken))
            {
                // ClassTail[Yield,Await] : (Modified) See 14.5
                //      ClassHeritage[?Yield,?Await]opt { ClassBody[?Yield,?Await]opt }
                node.Members = ParseClassMembers();
                ParseExpected(SyntaxKind.CloseBraceToken);
            }
            else
            {
                node.Members = CreateMissingList<IClassElement>();
            }

            return FinishNode(node);
        }

        private IIdentifier ParseNameOfClassDeclarationOrExpression()
        {
            // implements is a future reserved word so
            // 'class implements' might mean either
            // - class expression with omitted name, 'implements' starts heritage clause
            // - class with name 'implements'
            // 'isImplementsClause' helps to disambiguate between these two cases
            return IsIdentifier() && !IsImplementsClause()
                ? ParseIdentifier()
                : null;
        }

        private NodeArray<IClassElement> ParseClassMembers()
        {
            return ParseList(this, ParsingContext.ClassMembers, p => p.ParseClassElement());
        }

        private IClassElement ParseClassElement()
        {
            if (m_token == SyntaxKind.SemicolonToken)
            {
                var result = CreateNode<SemicolonClassElement>(SyntaxKind.SemicolonClassElement);
                NextToken();
                return FinishNode(result);
            }

            var fullStart = GetNodePos();
            var triviaLength = GetLeadingTriviaLength(fullStart);
            var decorators = ParseDecorators();
            var modifiers = ParseModifiers(/*permitInvalidConstAsModifier*/ true);

            var accessor = TryParseAccessorDeclaration(fullStart, triviaLength, decorators, modifiers);
            if (accessor != null)
            {
                return accessor;
            }

            if (m_token == SyntaxKind.ConstructorKeyword)
            {
                return ParseConstructorDeclaration(fullStart, triviaLength, decorators, modifiers);
            }

            if (IsIndexSignature())
            {
                return ParseIndexSignatureDeclaration(fullStart, triviaLength, decorators, modifiers);
            }

            // It is very important that we check this *after* checking indexers because
            // the [ token can start an index signature or a computed property name
            if (Utils.TokenIsIdentifierOrKeyword(m_token) ||
                m_token == SyntaxKind.StringLiteral ||
                m_token == SyntaxKind.NumericLiteral ||
                m_token == SyntaxKind.AsteriskToken ||
                m_token == SyntaxKind.OpenBracketToken)
            {
                return ParsePropertyOrMethodDeclaration(fullStart, triviaLength, decorators, modifiers);
            }

            if (decorators != null || modifiers != null)
            {
                // treat this as a property declaration with a missing name.
                var name = CreateMissingNode<Identifier>(SyntaxKind.Identifier, /*reportAtCurrentPosition*/ true, Errors.Declaration_expected);
                return ParsePropertyDeclaration(fullStart, triviaLength, decorators, modifiers, new PropertyName(name), /*questionToken*/ null);
            }

            // 'isClassMemberStart' should have hinted not to attempt parsing.
            Contract.Assert(false, "Should not have attempted to parse class member declaration.");
            throw new InvalidOperationException();
        }

        private IClassElement ParsePropertyOrMethodDeclaration(int fullStart, int triviaLength, NodeArray<IDecorator> decorators, ModifiersArray modifiers)
        {
            var asteriskToken = ParseOptionalToken<TokenNode>(SyntaxKind.AsteriskToken);
            var name = ParsePropertyName();

            // this Note is not legal as per the grammar.  But we allow it in the parser and
            // report an error in the grammar checker.
            var questionToken = ParseOptionalToken<TokenNode>(SyntaxKind.QuestionToken);
            if (asteriskToken != null || m_token == SyntaxKind.OpenParenToken || m_token == SyntaxKind.LessThanToken)
            {
                return ParseMethodDeclaration(fullStart, triviaLength, decorators, modifiers, asteriskToken, name, questionToken, Errors.Or_expected);
            }

            return ParsePropertyDeclaration(fullStart, triviaLength, decorators, modifiers, name, questionToken);
        }

        private IClassElement ParsePropertyDeclaration(int fullStart, int triviaLength, NodeArray<IDecorator> decorators, ModifiersArray modifiers, PropertyName name, Node questionToken)
        {
            var property = CreateNode<PropertyDeclaration>(SyntaxKind.PropertyDeclaration, fullStart, triviaLength);
            property.Decorators = decorators;
            SetModifiers(property, modifiers);
            property.Name = name;
            property.QuestionToken = questionToken;
            property.Type = ParseTypeAnnotation();

            // For instance properties specifically, since they are evaluated inside the varructor,
            // we do *not * want to parse yield expressions, so we specifically turn the yield context
            // off. The grammar would look something like this:
            //
            //    MemberVariableDeclaration[Yield]:
            //        AccessibilityModifier_opt   PropertyName   TypeAnnotation_opt   Initialiser_opt[In];
            //        AccessibilityModifier_opt  static_opt  PropertyName   TypeAnnotation_opt   Initialiser_opt[In, ?Yield];
            //
            // The checker may still error in the static case to explicitly disallow the yield expression.
            property.Initializer = modifiers != null && (modifiers.Flags & NodeFlags.Static) != NodeFlags.None
                ? AllowInAnd(this, p => p.ParseNonParameterInitializer())
                : DoOutsideOfContext(this, ParserContextFlags.Yield | ParserContextFlags.DisallowIn, p => p.ParseNonParameterInitializer());

            ParseSemicolon();
            return FinishNode(property);
        }

        private IConstructorDeclaration ParseConstructorDeclaration(int pos, int triviaLength, NodeArray<IDecorator> decorators, ModifiersArray modifiers)
        {
            var node = CreateNode<ConstructorDeclaration>(SyntaxKind.Constructor, pos, triviaLength);
            node.Decorators = decorators;
            SetModifiers(node, modifiers);
            ParseExpected(SyntaxKind.ConstructorKeyword);
            FillSignature(SyntaxKind.ColonToken, /*yieldContext*/ false, /*awaitContext*/ false, /*requireCompleteParameterList*/ false, node);
            node.Body = ParseFunctionBlockOrSemicolon(/*isGenerator*/ false, /*isAsync*/ false, Errors.Or_expected);
            return FinishNode(node);
        }

        private IClassExpression ParseClassExpression()
        {
            // <  >
            return (IClassExpression)ParseClassDeclarationOrExpression(
                fullStart: m_scanner.StartPos,
                triviaLength: GetLeadingTriviaLength(m_scanner.StartPos),
                decorators: null,
                modifiers: null,
                kind: SyntaxKind.ClassExpression);
        }

        private bool IsImplementsClause()
        {
            return m_token == SyntaxKind.ImplementsKeyword && LookAhead(this, p => p.NextTokenIsIdentifierOrKeyword());
        }

        private IBlock ParseFunctionBlockOrSemicolon(bool isGenerator, bool isAsync, IDiagnosticMessage diagnosticMessage = null)
        {
            if (m_token != SyntaxKind.OpenBraceToken && CanParseSemicolon())
            {
                ParseSemicolon();
                return null;
            }

            return ParseFunctionBlock(isGenerator, isAsync, /*ignoreMissingOpenBrace*/ false, diagnosticMessage);
        }

        private IBlock ParseFunctionBlock(bool allowYield, bool allowAwait, bool ignoreMissingOpenBrace, IDiagnosticMessage diagnosticMessage = null)
        {
            var savedYieldContext = InYieldContext();
            SetYieldContext(allowYield);

            var savedAwaitContext = InAwaitContext();
            SetAwaitContext(allowAwait);

            // We may be in a [Decorator] context when parsing a function expression or
            // arrow function. The body of the function is not in [Decorator] context.
            var saveDecoratorContext = InDecoratorContext();
            if (saveDecoratorContext)
            {
                SetDecoratorContext(/*val*/ false);
            }

            var block = ParseBlock(ignoreMissingOpenBrace, diagnosticMessage);

            if (saveDecoratorContext)
            {
                SetDecoratorContext(/*val*/ true);
            }

            SetYieldContext(savedYieldContext);
            SetAwaitContext(savedAwaitContext);

            return block;
        }

        private void SetAwaitContext(bool val)
        {
            SetContextFlag(val, ParserContextFlags.Await);
        }

        private void SetYieldContext(bool val)
        {
            SetContextFlag(val, ParserContextFlags.Yield);
        }

        private IIdentifier ParseOptionalIdentifier()
        {
            return IsIdentifier() ? ParseIdentifier() : null;
        }

        private IIdentifier ParseIdentifier(IDiagnosticMessage diagnosticMessage = null)
        {
            return CreateIdentifier(IsIdentifier(), diagnosticMessage);
        }

        private bool IsLetDeclaration()
        {
            // In ES6 'let' always starts a lexical declaration if followed by an identifier or {
            // or [.
            return LookAhead(this, p => p.NextTokenIsIdentifierOrStartOfDestructuring());
        }

        private bool NextTokenIsIdentifierOrStartOfDestructuring()
        {
            NextToken();
            return IsIdentifier() || m_token == SyntaxKind.OpenBraceToken || m_token == SyntaxKind.OpenBracketToken;
        }

        /// <nodoc />
        protected virtual IVariableStatement ParseVariableStatement(int fullStart, int triviaLength, NodeArray<IDecorator> decorators, ModifiersArray modifiers)
        {
            var node = CreateNode<VariableStatement>(SyntaxKind.VariableStatement, fullStart, triviaLength);
            node.Decorators = decorators;
            SetModifiers(node, modifiers);
            node.DeclarationList = ParseVariableDeclarationList(/*inForStatementInitializer*/ false);

            if (decorators != null && node.DeclarationList != null && decorators.Length > 0 && node.DeclarationList.Declarations.Count > 0)
            {
                m_scanner.MoveTrivia(decorators[0], node.DeclarationList.Declarations[0]);
            }

            ParseSemicolon();
            return FinishNode(node);
        }

        private bool ParseSemicolon()
        {
            if (CanParseSemicolon())
            {
                if (m_token == SyntaxKind.SemicolonToken)
                {
                    // consume the semicolon if it was explicitly provided.
                    NextToken();
                }

                return true;
            }
            else
            {
                return ParseExpected(SyntaxKind.SemicolonToken);
            }
        }

        private bool CanParseSemicolon()
        {
            // If there's a real semicolon, then we can always parse it out.
            if (m_token == SyntaxKind.SemicolonToken)
            {
                return true;
            }

            // We can parse out an optional semicolon in ASI cases in the following cases.
            return m_token == SyntaxKind.CloseBraceToken || m_token == SyntaxKind.EndOfFileToken || m_scanner.HasPrecedingLineBreak;
        }

        private IVariableDeclarationList ParseVariableDeclarationList(bool inForStatementInitializer)
        {
            var node = CreateNode<VariableDeclarationList>(SyntaxKind.VariableDeclarationList);

            switch (m_token)
            {
                case SyntaxKind.VarKeyword:
                    break;
                case SyntaxKind.LetKeyword:
                    node.Flags |= NodeFlags.Let;
                    break;
                case SyntaxKind.ConstKeyword:
                    node.Flags |= NodeFlags.Const;
                    break;
                default:
                    throw new InvalidOperationException(I($"Unsupported token kind {m_token}."));
            }

            NextToken();

            // The user may have written the following:
            //
            //    for (let of X) { }
            //
            // In this case, we want to parse an empty declaration list, and then parse 'of'
            // as a keyword. The reason this is not automatic is that 'of' is a valid identifier.
            // So we need to look ahead to determine if 'of' should be treated as a keyword in
            // this context.
            // The checker will then give an error that there is an empty declaration list.
            if (m_token == SyntaxKind.OfKeyword && LookAhead(this, p => p.CanFollowContextualOfKeyword()))
            {
                node.Declarations = CreateMissingList<IVariableDeclaration>();
            }
            else
            {
                var savedDisallowIn = InDisallowInContext();
                SetDisallowInContext(inForStatementInitializer);

                var declarations = ParseDelimitedList(this, ParsingContext.VariableDeclarations, p => p.ParseVariableDeclaration());
                node.Declarations = VariableDeclarationNodeArray.Create(declarations);
                SetDisallowInContext(savedDisallowIn);
            }

            return FinishNode(node);
        }

        private bool CanFollowContextualOfKeyword()
        {
            return NextTokenIsIdentifier() && NextToken() == SyntaxKind.CloseParenToken;
        }

        private bool NextTokenIsIdentifier()
        {
            NextToken();
            return IsIdentifier();
        }

        private void SetDisallowInContext(bool val)
        {
            SetContextFlag(val, ParserContextFlags.DisallowIn);
        }

        private IStatement ParseEmptyStatement()
        {
            var node = CreateNode<EmptyStatement>(SyntaxKind.EmptyStatement);
            ParseExpected(SyntaxKind.SemicolonToken);
            return FinishNode(node);
        }

        private IBlock ParseBlock(bool ignoreMissingOpenBrace, IDiagnosticMessage diagnosticMessage = null)
        {
            var node = CreateNode<Block>(SyntaxKind.Block);
            if (ParseExpected(SyntaxKind.OpenBraceToken, diagnosticMessage) || ignoreMissingOpenBrace)
            {
                node.Statements = ParseList<IStatement>(this, ParsingContext.BlockStatements, p => p.ParseStatement()); // new NodeArray<Statement>()
                ParseExpected(SyntaxKind.CloseBraceToken);
            }
            else
            {
                node.Statements = CreateMissingList<IStatement>();
            }

            return FinishNode(node);
        }

        private NodeArray<T> CreateMissingList<T>()
        {
            var pos = GetNodePos();
            var result = new NodeArray<T>();
            result.Pos = pos;
            result.End = pos;
            return result;
        }

        private static void SetExternalModuleIndicator(ISourceFile sourceFile)
        {
            // Every DScript file should be treated as an external module
            // (except prelude, but this covered separately).
            if (sourceFile.IsScriptFile())
            {
                sourceFile.ExternalModuleIndicator = sourceFile;
            }
            else
            {
                sourceFile.ExternalModuleIndicator = NodeArrayExtensions.ForEachUntil(
                    sourceFile.Statements,
                    node =>
                    {
                        return (node.Flags & NodeFlags.Export) != NodeFlags.None
                               || (node.Kind == SyntaxKind.ImportEqualsDeclaration &&
                                   node.Cast<IImportEqualsDeclaration>().ModuleReference.Kind == SyntaxKind.ExternalModuleReference)
                               || node.Kind == SyntaxKind.ImportDeclaration
                               || node.Kind == SyntaxKind.ExportAssignment
                               || node.Kind == SyntaxKind.ExportDeclaration
                            ? node
                            : (IStatement)null;
                    });
            }
        }

        private void SetParent(INode parent, INode node)
        {
            node.Parent = parent;

            NodeWalker.ForEachChild(
                node,
                (parser: this , node: node),
                (child, tuple) =>
                {
                    var @this = tuple.parser;
                    var localNode = tuple.node;
                    @this.SetParent(localNode, child);
                    return string.Empty;
                });
        }

        private void FixupParentReferences(ISourceFile sourceFile)
        {
            SetParent(null, sourceFile);
        }

        private static bool IsSourceFileJavaScript()
        {
            return false;

            // throw new NotImplementedException();
        }

        private static void AddJsDocComments()
        {
            throw new NotImplementedException();

            // if (m_addJsDocCommentsVisit == null)
            // {
            //    m_addJsDocCommentsVisit = node =>
            //    {
            //        // Add additional cases as necessary depending on how we see JSDoc comments used
            //        // in the wild.
            //        switch (node.Kind)
            //        {
            //            case SyntaxKind.VariableStatement:
            //            case SyntaxKind.FunctionDeclaration:
            //            case SyntaxKind.Parameter:
            //                AddJsDocComment(node);
            //                break;
            //        }

            // ForEachChild(node, n => m_addJsDocCommentsVisit);
            //        return true;
            //    };
            // }

            // ForEachChild(m_sourceFile, m_addJsDocCommentsVisit);
            // return;
        }

        // Parses a list of elements
        private NodeArray<T> ParseList<T>(Parser parser, ParsingContext kind, Func<Parser, T> parseElement)
            where T : INode
        {
            INode dummyMatch;
            return ParseListAndFindFirstMatch(parser, kind, parseElement, (_, t) => false, out dummyMatch);
        }

        // Parses a list of elements. DScript-specific: a condition can be passed to identify an element, and the first match (or null) is returned
        // This modification is added for performance reasons, so an extra traversal is avoided.
        private NodeArray<T> ParseListAndFindFirstMatch<T>(Parser parser, ParsingContext kind, Func<Parser, T> parseElement, Func<Parser, T, bool> condition, out INode firstMatch) where T : INode
        {
            Contract.Ensures(Contract.Result<NodeArray<T>>() != null);

            firstMatch = null;

            var saveParsingContext = m_parsingContext;

            // TODO: following expressions uses bitwise operators on non-flag enum!!
            m_parsingContext |= (ParsingContext)(1 << (int)kind);
            var result = new NodeArray<T>();
            result.Pos = GetNodePos();

            while (!IsListTerminator(kind))
            {
                if (IsListElement(kind, inErrorRecovery: false))
                {
                    var element = ParseListElement(parser, kind, parseElement);
                    result.Add(element);

                    m_scanner.AllowTrailingTriviaOnNode(element);

                    // Collects the first match, if any
                    if (condition(parser, element) && firstMatch == null)
                    {
                        firstMatch = element;
                    }

                    continue;
                }

                if (AbortParsingListOrMoveToNextToken(kind))
                {
                    break;
                }
            }

            result.End = GetNodeEnd();
            m_parsingContext = saveParsingContext;
            return result;
        }

        private T ParseListElement<T>(Parser parser, ParsingContext kind, Func<Parser, T> parseElement)
            where T : INode
        {
            var node = CurrentNode(m_parsingContext);
            if (node != null)
            {
                return (T)ConsumeNode(node);
            }

            return parseElement(parser);
        }

        private INode ConsumeNode(INode node)
        {
            // Move the scanner so it is after the node we just consumed.
            m_scanner.SetTextPos(node.End);
            NextToken();
            return node;
        }

        private INode CurrentNode(ParsingContext parsingContext)
        {
            // If there is an outstanding parse error that we've encountered, but not attached to
            // some node, then we cannot get a node from the old source tree.  This is because we
            // want to mark the next node we encounter as being unusable.
            //
            // This Note may be too conservative.  Perhaps we could reuse the node and set the bit
            // on it (or its leftmost child) as having the error.  For now though, being conservative
            // is nice and likely won't ever affect perf.
            if (m_parseErrorBeforeNextFinishedNode)
            {
                return null;
            }

            if (m_syntaxCursor == null)
            {
                // if we don't have a cursor, we could never return a node from the old tree.
                return null;
            }

            var node = m_syntaxCursor.CurrentNode(m_scanner.StartPos);

            // Can't reuse a missing node.
            if (Types.NodeUtilities.NodeIsMissing(node))
            {
                return null;
            }

            // Can't reuse a node that intersected the change range.
            if (node.IntersectsChange)
            {
                return null;
            }

            // Can't reuse a node that contains a parse error.  This is necessary so that we
            // produce the same set of errors again.
            if (Types.NodeUtilities.ContainsParseError(node))
            {
                return null;
            }

            // We can only reuse a node if it was parsed under the same strict mode that we're
            // currently in.  i.e., if we originally parsed a node in non-strict mode, but then
            // the user added 'using strict' at the top of the file, then we can't use that node
            // again as the presense of strict mode may cause us to parse the tokens in the file
            // differetly.
            //
            // we Note *can* reuse tokens when the strict mode changes.  That's because tokens
            // are unaffected by strict mode.  It's just the parser will decide what to do with it
            // differently depending on what mode it is in.
            //
            // This also applies to all our other context flags as well.
            var nodeContextFlags = node.ParserContextFlags & ParserContextFlags.ParserGeneratedFlags;
            if (nodeContextFlags != m_contextFlags)
            {
                return null;
            }

            // Ok, we have a node that looks like it could be reused.  Now verify that it is valid
            // in the currest list parsing context that we're currently at.
            if (!CanReuseNode(node, parsingContext))
            {
                return null;
            }

            return node;
        }

        private static bool CanReuseNode(INode node, ParsingContext parsingContext)
        {
            switch (parsingContext)
            {
                case ParsingContext.ClassMembers:
                    return IsReusableClassMember(node);

                case ParsingContext.SwitchClauses:
                    return IsReusableSwitchClause(node);

                case ParsingContext.SourceElements:
                case ParsingContext.BlockStatements:
                case ParsingContext.SwitchClauseStatements:
                    return IsReusableStatement(node);

                case ParsingContext.EnumMembers:
                    return IsReusableEnumMember(node);

                case ParsingContext.TypeMembers:
                    return IsReusableTypeMember(node);

                case ParsingContext.VariableDeclarations:
                    return IsReusableVariableDeclaration(node);

                case ParsingContext.Parameters:
                    return IsReusableParameter(node);

                // Any other lists we do not care about reusing nodes in.  But feel free to add if
                // you can do so safely.  Danger areas involve nodes that may involve speculative
                // parsing.  If speculative parsing is involved with the node, then the range the
                // parser reached while looking ahead might be in the edited range (see the example
                // in canReuseVariableDeclaratorNode for a good case of this).
                case ParsingContext.HeritageClauses:
                // This would probably be safe to reuse.  There is no speculative parsing with
                // heritage clauses.
                case ParsingContext.TypeParameters:
                // This would probably be safe to reuse.  There is no speculative parsing with
                // type parameters.  Note that that's because type *parameters* only occur in
                // unambiguous *type* contexts.  While type *arguments* occur in very ambiguous
                // *expression* contexts.
                case ParsingContext.TupleElementTypes:
                // This would probably be safe to reuse.  There is no speculative parsing with
                // tuple types.

                // Technically, type argument list types are probably safe to reuse.  While
                // speculative parsing is involved with them (since type argument lists are only
                // produced from speculative parsing a < as a type argument list), we only have
                // the types because speculative parsing succeeded.  Thus, the lookahead never
                // went past the end of the list and rewound.
                case ParsingContext.TypeArguments:

                // these Note are almost certainly not safe to ever reuse.  Expressions commonly
                // need a large amount of lookahead, and we should not reuse them as they may
                // have actually intersected the edit.
                case ParsingContext.ArgumentExpressions:

                // This is not safe to reuse for the same reason as the 'AssignmentExpression'
                // cases.  i.e., a property assignment may end with an expression, and thus might
                // have lookahead far beyond its old node.
                case ParsingContext.ObjectLiteralMembers:

                // This is probably not safe to reuse.  There can be speculative parsing with
                // type names in a heritage clause.  There can be generic names in the type
                // name list, and there can be left hand side expressions (which can have type
                // arguments.)
                case ParsingContext.HeritageClauseElement:

                // Perhaps safe to reuse, but it's unlikely we'd see more than a dozen attributes
                // on any given element. Same for children.
                case ParsingContext.JsxAttributes:
                case ParsingContext.JsxChildren:
                    return true;
            }

            return false;
        }

        private static bool IsReusableClassMember(INode node)
        {
            if (node != null)
            {
                switch (node.Kind)
                {
                    case SyntaxKind.Constructor:
                    case SyntaxKind.IndexSignature:
                    case SyntaxKind.GetAccessor:
                    case SyntaxKind.SetAccessor:
                    case SyntaxKind.PropertyDeclaration:
                    case SyntaxKind.SemicolonClassElement:
                        return true;
                    case SyntaxKind.MethodDeclaration:
                        // Method declarations are not necessarily reusable.  An object-literal
                        // may have a method calls "varructor(...)" and we must reparse that
                        // into an actual .ConstructorDeclaration.
                        var methodDeclaration = node.Cast<IMethodDeclaration>();
                        var nameIsConstructor = methodDeclaration.Name.Kind == SyntaxKind.Identifier &&
                            ((IIdentifier)methodDeclaration.Name).OriginalKeywordKind == SyntaxKind.ConstructorKeyword;

                        return !nameIsConstructor;
                }
            }

            return false;
        }

        private static bool IsReusableSwitchClause(INode node)
        {
            if (node != null)
            {
                switch (node.Kind)
                {
                    case SyntaxKind.CaseClause:
                    case SyntaxKind.DefaultClause:
                        return true;
                }
            }

            return false;
        }

        private static bool IsReusableStatement(INode node)
        {
            if (node != null)
            {
                switch (node.Kind)
                {
                    case SyntaxKind.FunctionDeclaration:
                    case SyntaxKind.VariableStatement:
                    case SyntaxKind.Block:
                    case SyntaxKind.IfStatement:
                    case SyntaxKind.ExpressionStatement:
                    case SyntaxKind.ThrowStatement:
                    case SyntaxKind.ReturnStatement:
                    case SyntaxKind.SwitchStatement:
                    case SyntaxKind.BreakStatement:
                    case SyntaxKind.ContinueStatement:
                    case SyntaxKind.ForInStatement:
                    case SyntaxKind.ForOfStatement:
                    case SyntaxKind.ForStatement:
                    case SyntaxKind.WhileStatement:
                    case SyntaxKind.WithStatement:
                    case SyntaxKind.EmptyStatement:
                    case SyntaxKind.TryStatement:
                    case SyntaxKind.LabeledStatement:
                    case SyntaxKind.DoStatement:
                    case SyntaxKind.DebuggerStatement:
                    case SyntaxKind.ImportDeclaration:
                    case SyntaxKind.ImportEqualsDeclaration:
                    case SyntaxKind.ExportDeclaration:
                    case SyntaxKind.ExportAssignment:
                    case SyntaxKind.ModuleDeclaration:
                    case SyntaxKind.ClassDeclaration:
                    case SyntaxKind.InterfaceDeclaration:
                    case SyntaxKind.EnumDeclaration:
                    case SyntaxKind.TypeAliasDeclaration:
                        return true;
                }
            }

            return false;
        }

        private static bool IsReusableEnumMember(INode node)
        {
            return node.Kind == SyntaxKind.EnumMember;
        }

        private static bool IsReusableTypeMember(INode node)
        {
            if (node != null)
            {
                switch (node.Kind)
                {
                    case SyntaxKind.ConstructSignature:
                    case SyntaxKind.MethodSignature:
                    case SyntaxKind.IndexSignature:
                    case SyntaxKind.PropertySignature:
                    case SyntaxKind.CallSignature:
                        return true;
                }
            }

            return false;
        }

        private static bool IsReusableVariableDeclaration(INode node)
        {
            if (node.Kind != SyntaxKind.VariableDeclaration)
            {
                return false;
            }

            // Very subtle incremental parsing bug.  Consider the following code:
            //
            //      var v = new List < A, B
            //
            // This is actually legal code.  It's a list of variable declarators "v = new List<A"
            // on one side and "B" on the other. If you then change that to:
            //
            //      var v = new List < A, B >()
            //
            // then we have a problem.  "v = new List<A" doesn't intersect the change range, so we
            // start reparsing at "B" and we completely fail to handle this properly.
            //
            // In order to prevent this, we do not allow a variable declarator to be reused if it
            // has an initializer.
            var variableDeclarator = node.Cast<IVariableDeclaration>();
            return variableDeclarator.Initializer == null;
        }

        private static bool IsReusableParameter(INode node)
        {
            if (node.Kind != SyntaxKind.Parameter)
            {
                return false;
            }

            // See the comment in isReusableVariableDeclaration for why we do this.
            var parameter = node.Cast<IParameterDeclaration>();
            return parameter.Initializer == null;
        }

        private int GetNodeEnd()
        {
            return m_scanner.StartPos;
        }

        private bool AbortParsingListOrMoveToNextToken(ParsingContext kind)
        {
            ParseErrorAtCurrentToken(ParsingContextErrors(kind));
            if (IsInSomeParsingContext())
            {
                return true;
            }

            NextToken();
            return false;
        }

        // True if positioned at element or terminator of the current list or any enclosing list
        private bool IsInSomeParsingContext()
        {
            for (var kind = 0; kind < (int)ParsingContext.Count; kind++)
            {
                if (((int)m_parsingContext & (1 << kind)) != 0)
                {
                    if (IsListElement((ParsingContext)kind, /*inErrorRecovery*/ true) || IsListTerminator((ParsingContext)kind))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static IDiagnosticMessage ParsingContextErrors(ParsingContext context)
        {
            switch (context)
            {
                case ParsingContext.SourceElements: return Errors.Declaration_or_statement_expected;
                case ParsingContext.BlockStatements: return Errors.Declaration_or_statement_expected;
                case ParsingContext.SwitchClauses: return Errors.Case_or_default_expected;
                case ParsingContext.SwitchClauseStatements: return Errors.Statement_expected;
                case ParsingContext.TypeMembers: return Errors.Property_or_signature_expected;
                case ParsingContext.ClassMembers: return Errors.Unexpected_token_A_constructor_method_accessor_or_property_was_expected;
                case ParsingContext.EnumMembers: return Errors.Enum_member_expected;
                case ParsingContext.HeritageClauseElement: return Errors.Expression_expected;
                case ParsingContext.VariableDeclarations: return Errors.Variable_declaration_expected;
                case ParsingContext.ObjectBindingElements: return Errors.Property_destructuring_pattern_expected;
                case ParsingContext.ArrayBindingElements: return Errors.Array_element_destructuring_pattern_expected;
                case ParsingContext.ArgumentExpressions: return Errors.Argument_expression_expected;
                case ParsingContext.ObjectLiteralMembers: return Errors.Property_assignment_expected;
                case ParsingContext.ArrayLiteralMembers: return Errors.Expression_or_comma_expected;
                case ParsingContext.Parameters: return Errors.Parameter_declaration_expected;
                case ParsingContext.TypeParameters: return Errors.Type_parameter_declaration_expected;
                case ParsingContext.TypeArguments: return Errors.Type_argument_expected;
                case ParsingContext.TupleElementTypes: return Errors.Type_expected;
                case ParsingContext.HeritageClauses: return Errors.Unexpected_token_expected;
                case ParsingContext.ImportOrExportSpecifiers: return Errors.Identifier_expected;
                case ParsingContext.JsxAttributes: return Errors.Identifier_expected;
                case ParsingContext.JsxChildren: return Errors.Identifier_expected;
                case ParsingContext.JsDocFunctionParameters: return Errors.Parameter_declaration_expected;
                case ParsingContext.JsDocTypeArguments: return Errors.Type_argument_expected;
                case ParsingContext.JsDocTupleTypes: return Errors.Type_expected;
                case ParsingContext.JsDocRecordMembers: return Errors.Property_assignment_expected;
            }

            return null;
        }

        private bool IsListElement(ParsingContext parsingContext, bool inErrorRecovery)
        {
            INode node = CurrentNode(parsingContext);
            if (node != null)
            {
                return true;
            }

            switch (parsingContext)
            {
                case ParsingContext.SourceElements:
                case ParsingContext.BlockStatements:
                case ParsingContext.SwitchClauseStatements:
                    // If we're in error recovery, then we don't want to treat ';' as an empty statement.
                    // The problem is that ';' can show up in far too many contexts, and if we see one
                    // and assume it's a statement, then we may bail out inappropriately from whatever
                    // we're parsing.  For example, if we have a semicolon in the middle of a class, then
                    // we really don't want to assume the class is over and we're on a statement in the
                    // outer module.  We just want to consume and move on.
                    return !(m_token == SyntaxKind.SemicolonToken && inErrorRecovery) && IsStartOfStatement();
                case ParsingContext.SwitchClauses:
                    return m_token == SyntaxKind.CaseKeyword || m_token == SyntaxKind.DefaultKeyword;
                case ParsingContext.TypeMembers:
                    // DScript-specific. We allow comments here as well
                    return LookAhead(this, p => p.IsTypeMemberStart());
                case ParsingContext.ClassMembers:
                    // We allow semicolons as class elements (as specified by ES6) as long as we're
                    // not in error recovery.  If we're in error recovery, we don't want an errant
                    // semicolon to be treated as a class member (since they're almost always used
                    // for statements.
                    return LookAhead(this, p => p.IsClassMemberStart()) || (m_token == SyntaxKind.SemicolonToken && !inErrorRecovery);
                case ParsingContext.EnumMembers:
                    // DS: in DScript enum members could be decorated with ambient decorators
                    if (m_token == SyntaxKind.AtToken)
                    {
                        return true;
                    }

                    // Include open bracket computed properties. This technically also lets in indexers,
                    // which would be a candidate for improved error reporting.
                    // DScript-specific. We allow comments here as well
                    return m_token == SyntaxKind.OpenBracketToken || IsLiteralPropertyName();
                case ParsingContext.ObjectLiteralMembers:
                    // DScript-specific. We allow comments here as well
                    return m_token == SyntaxKind.OpenBracketToken || m_token == SyntaxKind.AsteriskToken || IsLiteralPropertyName();
                case ParsingContext.ObjectBindingElements:
                    return m_token == SyntaxKind.OpenBracketToken || IsLiteralPropertyName();
                case ParsingContext.HeritageClauseElement:
                    // If we see { } then only consume it as an expression if it is followed by , or {
                    // That way we won't consume the body of a class in its heritage clause.
                    if (m_token == SyntaxKind.OpenBraceToken)
                    {
                        return LookAhead(this, p => p.IsValidHeritageClauseObjectLiteral());
                    }

                    if (!inErrorRecovery)
                    {
                        return IsStartOfLeftHandSideExpression() && !IsHeritageClauseExtendsOrImplementsKeyword();
                    }
                    else
                    {
                        // If we're in error recovery we tighten up what we're willing to match.
                        // That way we don't treat something like "this" as a valid heritage clause
                        // element during recovery.
                        return IsIdentifier() && !IsHeritageClauseExtendsOrImplementsKeyword();
                    }

                case ParsingContext.VariableDeclarations:
                    return IsIdentifierOrPattern();
                case ParsingContext.ArrayBindingElements:
                    return m_token == SyntaxKind.CommaToken || m_token == SyntaxKind.DotDotDotToken || IsIdentifierOrPattern();
                case ParsingContext.TypeParameters:
                    return IsIdentifier();
                case ParsingContext.ArgumentExpressions:
                case ParsingContext.ArrayLiteralMembers:
                    // DScript-specific. We allow comments here as well
                    return m_token == SyntaxKind.CommaToken || m_token == SyntaxKind.DotDotDotToken || IsStartOfExpression();
                case ParsingContext.Parameters:
                    return IsStartOfParameter();
                case ParsingContext.TypeArguments:
                case ParsingContext.TupleElementTypes:
                    return m_token == SyntaxKind.CommaToken || IsStartOfType();
                case ParsingContext.HeritageClauses:
                    return IsHeritageClause();
                case ParsingContext.ImportOrExportSpecifiers:
                    return m_token.IsIdentifierOrKeyword();
                case ParsingContext.JsxAttributes:
                    return m_token.IsIdentifierOrKeyword() || m_token == SyntaxKind.OpenBraceToken;
                case ParsingContext.JsxChildren:
                    return true;
                case ParsingContext.JsDocFunctionParameters:
                case ParsingContext.JsDocTypeArguments:
                case ParsingContext.JsDocTupleTypes:
                    // return JSDocParser.isJSDocType();
                    throw PlaceHolder.SkipThrow();
                case ParsingContext.JsDocRecordMembers:
                    // return isSimplePropertyName();
                    throw PlaceHolder.SkipThrow();
            }

            Contract.Assert(false, "Non-exhaustive case in 'isListElement'.");
            return false;
        }

        private bool IsHeritageClauseExtendsOrImplementsKeyword()
        {
            if (m_token == SyntaxKind.ImplementsKeyword ||
                m_token == SyntaxKind.ExtendsKeyword)
            {
                return LookAhead(this, p => p.NextTokenIsStartOfExpression());
            }

            return false;
        }

        private bool NextTokenIsStartOfExpression()
        {
            NextToken();
            return IsStartOfExpression();
        }

        private bool IsValidHeritageClauseObjectLiteral()
        {
            Contract.Assert(m_token == SyntaxKind.OpenBraceToken);
            if (NextToken() == SyntaxKind.CloseBraceToken)
            {
                // if we see  "extends {}" then only treat the {} as what we're extending (and not
                // the class Body) if we have:
                //
                //      extends {} {
                //      extends {},
                //      extends {} extends
                //      extends {} implements
                var next = NextToken();
                return next == SyntaxKind.CommaToken || next == SyntaxKind.OpenBraceToken || next == SyntaxKind.ExtendsKeyword ||
                       next == SyntaxKind.ImplementsKeyword;
            }

            return true;
        }

        private bool IsClassMemberStart()
        {
            SyntaxKind? idToken = null;

            if (m_token == SyntaxKind.AtToken)
            {
                return true;
            }

            // Eat up all modifiers, but hold on to the last one in case it is actually an identifier.
            while (m_token.IsModifierKind())
            {
                idToken = m_token;

                // If the idToken is a class modifier (protected, private, public, and static), it is
                // certain that we are starting to parse class member. This allows better error recovery
                // Example:
                //      public foo() ...     // true
                //      public @dec blah ... // true; we will then report an error later
                //      export public ...    // true; we will then report an error later
                if (IsClassMemberModifier(idToken.Value))
                {
                    return true;
                }

                NextToken();
            }

            if (m_token == SyntaxKind.AsteriskToken)
            {
                return true;
            }

            // Try to get the first property-like token following all modifiers.
            // This can either be an identifier or the 'get' or 'set' keywords.
            if (IsLiteralPropertyName())
            {
                idToken = m_token;
                NextToken();
            }

            // Index signatures and computed properties are class members; we can parse.
            if (m_token == SyntaxKind.OpenBracketToken)
            {
                return true;
            }

            // If we were able to get any potential identifier...
            if (idToken != null)
            {
                // If we have a non-keyword identifier, or if we have an accessor, then it's safe to parse.
                if (!idToken.Value.IsKeyword() || idToken == SyntaxKind.SetKeyword || idToken == SyntaxKind.GetKeyword)
                {
                    return true;
                }

                // If it *is* a keyword, but not an accessor, check a little farther along
                // to see if it should actually be parsed as a class member.
                switch (m_token)
                {
                    case SyntaxKind.OpenParenToken: // Method declaration
                    case SyntaxKind.LessThanToken: // Generic Method declaration
                    case SyntaxKind.ColonToken: // Type Annotation for declaration
                    case SyntaxKind.EqualsToken: // Initializer for declaration
                    case SyntaxKind.QuestionToken: // Not valid, but permitted so that it gets caught later on.
                        return true;
                    default:
                        // Covers
                        //  - Semicolons     (declaration termination)
                        //  - Closing braces (end-of-class, must be declaration)
                        //  - End-of-files   (not valid, but permitted so that it gets caught later on)
                        //  - Line-breaks    (enabling *automatic semicolon insertion*)
                        return CanParseSemicolon();
                }
            }

            return false;
        }

        private static bool IsClassMemberModifier(SyntaxKind idToken)
        {
            switch (idToken)
            {
                case SyntaxKind.PublicKeyword:
                case SyntaxKind.PrivateKeyword:
                case SyntaxKind.ProtectedKeyword:
                case SyntaxKind.StaticKeyword:
                case SyntaxKind.ReadonlyKeyword:
                    return true;
                default:
                    return false;
            }
        }

        private bool IsHeritageClause()
        {
            return m_token == SyntaxKind.ExtendsKeyword || m_token == SyntaxKind.ImplementsKeyword;
        }

        private bool IsStartOfType()
        {
            switch (m_token)
            {
                case SyntaxKind.AnyKeyword:
                case SyntaxKind.StringKeyword:
                case SyntaxKind.NumberKeyword:
                case SyntaxKind.BooleanKeyword:
                case SyntaxKind.SymbolKeyword:
                case SyntaxKind.VoidKeyword:
                case SyntaxKind.ThisKeyword:
                case SyntaxKind.TypeOfKeyword:
                case SyntaxKind.OpenBraceToken:
                case SyntaxKind.OpenBracketToken:
                case SyntaxKind.LessThanToken:
                case SyntaxKind.NewKeyword:
                case SyntaxKind.StringLiteral:
                    return true;
                case SyntaxKind.OpenParenToken:
                    // Only consider '(' the start of a type if followed by ')', '...', an identifier, a modifier,
                    // or something that starts a type. We don't want to consider things like '(1)' a type.
                    return LookAhead(this, p => p.IsStartOfParenthesizedOrFunctionType());
                default:
                    return IsIdentifier();
            }
        }

        private bool IsStartOfParenthesizedOrFunctionType()
        {
            NextToken();
            return m_token == SyntaxKind.CloseParenToken || IsStartOfParameter() || IsStartOfType();
        }

        private bool IsTypeMemberStart()
        {
            SyntaxKind? idToken = null;

            // DS: DScript supports annotations on interface members
            if (m_token == SyntaxKind.AtToken)
            {
                return true;
            }

            // Return true if we have the start of a signature member
            if (m_token == SyntaxKind.OpenParenToken || m_token == SyntaxKind.LessThanToken)
            {
                return true;
            }

            // Eat up all modifiers, but hold on to the last one in case it is actually an identifier
            while (m_token.IsModifierKind())
            {
                idToken = m_token;
                NextToken();
            }

            // Index signatures and computed property names are type members
            if (m_token == SyntaxKind.OpenBracketToken)
            {
                return true;
            }

            // Try to get the first property-like token following all modifiers
            if (IsLiteralPropertyName())
            {
                idToken = m_token;
                NextToken();
            }

            // If we were able to get any potential identifier, check that it is
            // the start of a member declaration
            if (idToken != null)
            {
                return m_token == SyntaxKind.OpenParenToken ||
                    m_token == SyntaxKind.LessThanToken ||
                    m_token == SyntaxKind.QuestionToken ||
                    m_token == SyntaxKind.ColonToken ||
                    CanParseSemicolon();
            }

            return false;
        }

        private bool IsStartOfIndexSignatureDeclaration()
        {
            while (m_token.IsModifierKind())
            {
                NextToken();
            }

            return IsIndexSignature();
        }

        private bool IsIndexSignature()
        {
            if (m_token != SyntaxKind.OpenBracketToken)
            {
                return false;
            }

            return LookAhead(this, p => p.IsUnambiguouslyIndexSignature());
        }

        private bool IsUnambiguouslyIndexSignature()
        {
            // The only allowed sequence is:
            //
            //   [id:
            //
            // However, for error recovery, we also check the following cases:
            //
            //   [...
            //   [id,
            //   [id?,
            //   [id?:
            //   [id?]
            //   [public id
            //   [private id
            //   [protected id
            //   []
            NextToken();
            if (m_token == SyntaxKind.DotDotDotToken || m_token == SyntaxKind.CloseBracketToken)
            {
                return true;
            }

            if (m_token.IsModifierKind())
            {
                NextToken();
                if (IsIdentifier())
                {
                    return true;
                }
            }
            else if (!IsIdentifier())
            {
                return false;
            }
            else
            {
                // Skip the identifier
                NextToken();
            }

            // A colon signifies a well formed indexer
            // A comma should be a badly formed indexer because comma expressions are not allowed
            // in computed properties.
            if (m_token == SyntaxKind.ColonToken || m_token == SyntaxKind.CommaToken)
            {
                return true;
            }

            // Question mark could be an indexer with an optional property,
            // or it could be a conditional expression in a computed property.
            if (m_token != SyntaxKind.QuestionToken)
            {
                return false;
            }

            // If any of the following tokens are after the question mark, it cannot
            // be a conditional expression, so treat it as an indexer.
            NextToken();
            return m_token == SyntaxKind.ColonToken || m_token == SyntaxKind.CommaToken || m_token == SyntaxKind.CloseBracketToken;
        }

        private bool IsTypeMemberWithLiteralPropertyName()
        {
            NextToken();
            return m_token == SyntaxKind.OpenParenToken ||
                m_token == SyntaxKind.LessThanToken ||
                m_token == SyntaxKind.QuestionToken ||
                m_token == SyntaxKind.ColonToken ||
                CanParseSemicolon();
        }

        private bool IsStartOfParameter()
        {
            return m_token == SyntaxKind.DotDotDotToken || IsIdentifierOrPattern() || m_token.IsModifierKind() || m_token == SyntaxKind.AtToken;
        }

        private bool IsIdentifierOrPattern()
        {
            return m_token == SyntaxKind.OpenBraceToken || m_token == SyntaxKind.OpenBracketToken || IsIdentifier();
        }

        private bool IsListTerminator(ParsingContext kind)
        {
            if (m_token == SyntaxKind.EndOfFileToken)
            {
                return true;
            }

            switch (kind)
            {
                case ParsingContext.BlockStatements:
                case ParsingContext.SwitchClauses:
                case ParsingContext.TypeMembers:
                case ParsingContext.ClassMembers:
                case ParsingContext.EnumMembers:
                case ParsingContext.ObjectLiteralMembers:
                case ParsingContext.ObjectBindingElements:
                case ParsingContext.ImportOrExportSpecifiers:
                    return m_token == SyntaxKind.CloseBraceToken;
                case ParsingContext.SwitchClauseStatements:
                    return m_token == SyntaxKind.CloseBraceToken || m_token == SyntaxKind.CaseKeyword || m_token == SyntaxKind.DefaultKeyword;
                case ParsingContext.HeritageClauseElement:
                    return m_token == SyntaxKind.OpenBraceToken || m_token == SyntaxKind.ExtendsKeyword || m_token == SyntaxKind.ImplementsKeyword;
                case ParsingContext.VariableDeclarations:
                    return IsVariableDeclaratorListTerminator();
                case ParsingContext.TypeParameters:
                    // Tokens other than '>' are here for better error recovery
                    return m_token == SyntaxKind.GreaterThanToken || m_token == SyntaxKind.OpenParenToken || m_token == SyntaxKind.OpenBraceToken || m_token == SyntaxKind.ExtendsKeyword || m_token == SyntaxKind.ImplementsKeyword;
                case ParsingContext.ArgumentExpressions:
                    // Tokens other than ')' are here for better error recovery
                    return m_token == SyntaxKind.CloseParenToken || m_token == SyntaxKind.SemicolonToken;
                case ParsingContext.ArrayLiteralMembers:
                case ParsingContext.TupleElementTypes:
                case ParsingContext.ArrayBindingElements:
                    return m_token == SyntaxKind.CloseBracketToken;
                case ParsingContext.Parameters:
                    // Tokens other than ')' and ']' (the latter for index signatures) are here for better error recovery
                    return m_token == SyntaxKind.CloseParenToken || m_token == SyntaxKind.CloseBracketToken /*|| token == SyntaxKind.OpenBraceToken*/;
                case ParsingContext.TypeArguments:
                    // Tokens other than '>' are here for better error recovery
                    return m_token == SyntaxKind.GreaterThanToken || m_token == SyntaxKind.OpenParenToken;
                case ParsingContext.HeritageClauses:
                    return m_token == SyntaxKind.OpenBraceToken || m_token == SyntaxKind.CloseBraceToken;
                case ParsingContext.JsxAttributes:
                    return m_token == SyntaxKind.GreaterThanToken || m_token == SyntaxKind.SlashToken;
                case ParsingContext.JsxChildren:
                    return m_token == SyntaxKind.LessThanToken && LookAhead(this, p => p.NextTokenIsSlash());
                case ParsingContext.JsDocFunctionParameters:
                    return m_token == SyntaxKind.CloseParenToken || m_token == SyntaxKind.ColonToken || m_token == SyntaxKind.CloseBraceToken;
                case ParsingContext.JsDocTypeArguments:
                    return m_token == SyntaxKind.GreaterThanToken || m_token == SyntaxKind.CloseBraceToken;
                case ParsingContext.JsDocTupleTypes:
                    return m_token == SyntaxKind.CloseBracketToken || m_token == SyntaxKind.CloseBraceToken;
                case ParsingContext.JsDocRecordMembers:
                    return m_token == SyntaxKind.CloseBraceToken;
            }

            return false;
        }

        private bool NextTokenIsSlash()
        {
            return NextToken() == SyntaxKind.SlashToken;
        }

        private bool IsVariableDeclaratorListTerminator()
        {
            // If we can consume a semicolon (either explicitly, or with ASI), then consider us done
            // with parsing the list of  variable declarators.
            if (CanParseSemicolon())
            {
                return true;
            }

            // in the case where we're parsing the variable declarator of a 'for-in' statement, we
            // are done if we see an 'in' keyword in front of us. Same with for-of
            if (IsInOrOfKeyword(m_token))
            {
                return true;
            }

            // ERROR RECOVERY TWEAK:
            // For better error recovery, if we see an '=>' then we just stop immediately.  We've got an
            // arrow  here and it's going to be very unlikely that we'll resynchronize and get
            // another variable declaration.
            if (m_token == SyntaxKind.EqualsGreaterThanToken)
            {
                return true;
            }

            // Keep trying to parse out variable declarators.
            return false;
        }

        private int GetNodePos()
        {
            return m_scanner.TokenPos;
        }

        private static void ProcessReferenceComments()
        {
            PlaceHolder.Skip();
        }

        /// <nodoc/>
        protected virtual SourceFile CreateSourceFile(string fileName, ScriptTarget languageVersion, bool allowBackslashesInPathInterpolation)
        {
            // code from createNode is inlined here so createNode won't have to deal with special case of creating source files
            // this is quite rare comparing to other nodes and createNode should be as fast as possible
            var sourceFile = new SourceFile(m_sourceTextProvider).Construct(SyntaxKind.SourceFile, /*pos*/ 0, /* end */ m_sourceText.Length);
            sourceFile.Path = Path.Absolute(fileName);

            m_nodeCount++;

            sourceFile.LanguageVersion = languageVersion;

            // Original line from TS port:
            //   sourceFile.FileName = Core.NormalizePath(fileName);
            // Removed since we want to preserve OS-dependent separators and, besides some testing-related invocations, the path is already normalized since it comes
            // from a path table
            sourceFile.FileName = fileName;
            sourceFile.Flags = Path.FileExtensionIs(sourceFile.FileName, ".d.ts") ? NodeFlags.DeclarationFile : NodeFlags.None;
            sourceFile.LanguageVariant = GetLanguageVariant(sourceFile.FileName);

            sourceFile.LiteralLikeSpecifiers = new List<ILiteralExpression>();
            sourceFile.ResolvedModules = new Map<IResolvedModule>();

            sourceFile.SourceFile = sourceFile;
            sourceFile.BackslashesAllowedInPathInterpolation = allowBackslashesInPathInterpolation;

            return sourceFile;
        }

        /// <nodoc />
        protected SyntaxKind NextToken()
        {
            // DScript-specific: We always ignore trivia that is not a single or multiline comment.
            // Observe that if the scanner is initialized to skip trivia, then this call is equivalent
            // to a plain Scan(), since in that case trivias are not reported
            // by the scanner
            m_token = m_scanner.ScanAndSkipOverNonCommentTrivia();

            return m_token;
        }

        private static LanguageVariant GetLanguageVariant(string fileName)
        {
            // .tsx and .jsx files are treated as jsx language variant.
            return Path.FileExtensionIs(fileName, ".tsx") || Path.FileExtensionIs(fileName, ".jsx") ? LanguageVariant.Jsx : LanguageVariant.Standard;
        }

        private void ScanError(IDiagnosticMessage message, int length)
        {
            var pos = m_scanner.TextPos;
            ParseErrorAtPosition(pos, length, message);
        }

        private void ParseErrorAtPosition(int start, int length, IDiagnosticMessage message, params object[] arg0)
        {
            // Don't report another error if it would just be at the same position as the last error.
            var lastError = ParseDiagnostics.LastOrDefault();
            if (lastError == null || start != lastError.Start)
            {
                m_parseDiagnostics.Value.Add(Diagnostic.CreateFileDiagnostic(m_sourceFile, start, length, message, arg0));
            }

            // Mark that we've encountered an error.  We'll set an appropriate bit on the next
            // node we finish so that it can't be reused incrementally.
            m_parseErrorBeforeNextFinishedNode = true;
        }

        private void ParseErrorAtPosition(int start, int length, IDiagnosticMessage message, object arg0)
        {
            // Don't report another error if it would just be at the same position as the last error.
            var lastError = ParseDiagnostics.LastOrDefault();
            if (lastError == null || start != lastError.Start)
            {
                m_parseDiagnostics.Value.Add(Diagnostic.CreateFileDiagnostic(m_sourceFile, start, length, message, arg0));
            }

            // Mark that we've encountered an error.  We'll set an appropriate bit on the next
            // node we finish so that it can't be reused incrementally.
            m_parseErrorBeforeNextFinishedNode = true;
        }

        private bool InContext(ParserContextFlags flags)
        {
            return (m_contextFlags & flags) != ParserContextFlags.None;
        }

        private bool InYieldContext()
        {
            return InContext(ParserContextFlags.Yield);
        }

        private bool InAwaitContext()
        {
            return InContext(ParserContextFlags.Await);
        }

        private bool InDisallowInContext()
        {
            return InContext(ParserContextFlags.DisallowIn);
        }

        // Invokes the provided callback.  If the callback returns something falsy, then it restores
        // the parser to the state it was in immediately prior to invoking the callback.  If the
        // callback returns something truthy, then the parser state is not rolled back.  The result
        // of invoking the callback is returned from this function.
        private T TryParse<T>(Parser parser, Func<Parser, T> callback)
        {
            return SpeculationHelper(parser, callback, /*isLookAhead*/ false);
        }

        private T SpeculationHelper<T>(Parser parser, Func<Parser, T> callback, bool isLookAhead)
        {
            // Keep track of the state we'll need to rollback to if lookahead fails (or if the
            // caller asked us to always reset our state).
            var saveToken = m_token;
            var saveParseDiagnosticsLength = ParseDiagnostics.Count;
            var saveParseErrorBeforeNextFinishedNode = m_parseErrorBeforeNextFinishedNode;

            // it Note is not actually necessary to save/restore the context flags here.  That's
            // because the saving/restoring of these flags happens naturally through the recursive
            // descent nature of our parser.  However, we still store this here just so we can
            // assert that that invariant holds.
            var saveContextFlags = m_contextFlags;

            // If we're only looking ahead, then tell the scanner to only lookahead as well.
            // Otherwise, if we're actually speculatively parsing, then tell the scanner to do the
            // same.
            var result = isLookAhead
                ? m_scanner.LookAhead((parser, callback), tpl => tpl.callback(tpl.parser))
                : m_scanner.TryScan((parser, callback), tpl => tpl.callback(tpl.parser));

            Contract.Assert(saveContextFlags == m_contextFlags);

            // If our callback returned something 'falsy' or we're just looking ahead,
            // then unconditionally restore us to where we were.
            if (isLookAhead || IsFalsy(result))
            {
                m_token = saveToken;
                if (saveParseDiagnosticsLength != ParseDiagnostics.Count)
                {
                    m_parseDiagnostics.Value.SetLength(saveParseDiagnosticsLength);
                }

                m_parseErrorBeforeNextFinishedNode = saveParseErrorBeforeNextFinishedNode;
            }

            return result;
        }

        // Ignore strict mode flag because we will report an error in type checker instead.
        private bool IsIdentifier()
        {
            if (m_token == SyntaxKind.Identifier)
            {
                return true;
            }

            // If we have a 'yield' keyword, and we're in the [yield] context, then 'yield' is
            // considered a keyword and is not an identifier.
            if (m_token == SyntaxKind.YieldKeyword && InYieldContext())
            {
                return false;
            }

            // If we have a 'await' keyword, and we're in the [Await] context, then 'await' is
            // considered a keyword and is not an identifier.
            if (m_token == SyntaxKind.AwaitKeyword && InAwaitContext())
            {
                return false;
            }

            return m_token > SyntaxKind.LastReservedWord;
        }

        private bool IsBinaryOperator()
        {
            if (InDisallowInContext() && m_token == SyntaxKind.InKeyword)
            {
                return false;
            }

            return GetBinaryOperatorPrecedence() > 0;
        }

        private int GetBinaryOperatorPrecedence()
        {
            switch (m_token)
            {
                case SyntaxKind.BarBarToken:
                    return 1;
                case SyntaxKind.AmpersandAmpersandToken:
                    return 2;
                case SyntaxKind.BarToken:
                    return 3;
                case SyntaxKind.CaretToken:
                    return 4;
                case SyntaxKind.AmpersandToken:
                    return 5;
                case SyntaxKind.EqualsEqualsToken:
                case SyntaxKind.ExclamationEqualsToken:
                case SyntaxKind.EqualsEqualsEqualsToken:
                case SyntaxKind.ExclamationEqualsEqualsToken:
                    return 6;
                case SyntaxKind.LessThanToken:
                case SyntaxKind.GreaterThanToken:
                case SyntaxKind.LessThanEqualsToken:
                case SyntaxKind.GreaterThanEqualsToken:
                case SyntaxKind.InstanceOfKeyword:
                case SyntaxKind.InKeyword:
                case SyntaxKind.AsKeyword:
                    return 7;
                case SyntaxKind.LessThanLessThanToken:
                case SyntaxKind.GreaterThanGreaterThanToken:
                case SyntaxKind.GreaterThanGreaterThanGreaterThanToken:
                    return 8;
                case SyntaxKind.PlusToken:
                case SyntaxKind.MinusToken:
                    return 9;
                case SyntaxKind.AsteriskToken:
                case SyntaxKind.SlashToken:
                case SyntaxKind.PercentToken:
                    return 10;
                case SyntaxKind.AsteriskAsteriskToken:
                    return 11;
            }

            // -1 is lower than all other precedences.  Returning it will cause binary expression
            // parsing to stop.
            return -1;
        }

        private bool IsDeclaration()
        {
            while (true)
            {
                switch (m_token)
                {
                    case SyntaxKind.VarKeyword:
                    case SyntaxKind.LetKeyword:
                    case SyntaxKind.ConstKeyword:
                    case SyntaxKind.FunctionKeyword:
                    case SyntaxKind.ClassKeyword:
                    case SyntaxKind.EnumKeyword:
                        return true;

                    // 'declare', 'module', 'namespace', 'interface'* and 'type' are all legal JavaScript identifiers;
                    // however, an identifier cannot be followed by another identifier on the same line. This is what we
                    // count on to parse out the respective declarations. For instance, we exploit this to say that
                    //
                    //    namespace n
                    //
                    // can be none other than the beginning of a namespace declaration, but need to respect that JavaScript sees
                    //
                    //    namespace
                    //    n
                    //
                    // as the identifier 'namespace' on one line followed by the identifier 'n' on another.
                    // We need to look one token ahead to see if it permissible to try parsing a declaration.
                    //
                    // *Note*: 'interface' is actually a strict mode reserved word. So while
                    //
                    //   "use strict"
                    //   interface
                    //   I
                    // { }
                    //
                    // could be legal, it would add complexity for very little gain.
                    case SyntaxKind.InterfaceKeyword:
                    case SyntaxKind.TypeKeyword:
                        return NextTokenIsIdentifierOnSameLine();
                    case SyntaxKind.ModuleKeyword:
                    case SyntaxKind.NamespaceKeyword:
                        return NextTokenIsIdentifierOrStringLiteralOnSameLine();
                    case SyntaxKind.AbstractKeyword:
                    case SyntaxKind.AsyncKeyword:
                    case SyntaxKind.DeclareKeyword:
                    case SyntaxKind.PrivateKeyword:
                    case SyntaxKind.ProtectedKeyword:
                    case SyntaxKind.PublicKeyword:
                    case SyntaxKind.ReadonlyKeyword:
                        NextToken();

                        // ASI takes effect for this modifier.
                        if (m_scanner.HasPrecedingLineBreak)
                        {
                            return false;
                        }

                        continue;

                    case SyntaxKind.ImportKeyword:
                        NextToken();
                        return m_token == SyntaxKind.StringLiteral || m_token == SyntaxKind.AsteriskToken ||
                            m_token == SyntaxKind.OpenBraceToken || m_token.IsIdentifierOrKeyword();
                    case SyntaxKind.ExportKeyword:
                        NextToken();
                        if (m_token == SyntaxKind.EqualsToken || m_token == SyntaxKind.AsteriskToken ||
                            m_token == SyntaxKind.OpenBraceToken || m_token == SyntaxKind.DefaultKeyword)
                        {
                            return true;
                        }

                        continue;

                    case SyntaxKind.StaticKeyword:
                        NextToken();
                        continue;
                    default:
                        return false;
                }
            }
        }

        private bool NextTokenIsIdentifierOrStringLiteralOnSameLine()
        {
            NextToken();
            return !m_scanner.HasPrecedingLineBreak && (IsIdentifier() || m_token == SyntaxKind.StringLiteral);
        }

        private bool IsStartOfDeclaration()
        {
            return LookAhead(this, p => p.IsDeclaration());
        }

        private bool IsStartOfLeftHandSideExpression()
        {
            switch (m_token)
            {
                case SyntaxKind.ThisKeyword:
                case SyntaxKind.SuperKeyword:
                case SyntaxKind.NullKeyword:
                case SyntaxKind.TrueKeyword:
                case SyntaxKind.FalseKeyword:
                case SyntaxKind.NumericLiteral:
                case SyntaxKind.StringLiteral:
                case SyntaxKind.NoSubstitutionTemplateLiteral:
                case SyntaxKind.TemplateHead:
                case SyntaxKind.OpenParenToken:
                case SyntaxKind.OpenBracketToken:
                case SyntaxKind.OpenBraceToken:
                case SyntaxKind.FunctionKeyword:
                case SyntaxKind.ClassKeyword:
                case SyntaxKind.NewKeyword:
                case SyntaxKind.SlashToken:
                case SyntaxKind.SlashEqualsToken:
                case SyntaxKind.Identifier:
                    return true;
                default:
                    return IsIdentifier();
            }
        }

        private bool IsStartOfExpression()
        {
            if (IsStartOfLeftHandSideExpression())
            {
                return true;
            }

            switch (m_token)
            {
                case SyntaxKind.PlusToken:
                case SyntaxKind.MinusToken:
                case SyntaxKind.TildeToken:
                case SyntaxKind.ExclamationToken:
                case SyntaxKind.DeleteKeyword:
                case SyntaxKind.TypeOfKeyword:
                case SyntaxKind.VoidKeyword:
                case SyntaxKind.PlusPlusToken:
                case SyntaxKind.MinusMinusToken:
                case SyntaxKind.LessThanToken:
                case SyntaxKind.AwaitKeyword:
                case SyntaxKind.YieldKeyword:
                    // Yield/await always starts an expression.  Either it is an identifier (in which case
                    // it is definitely an expression).  Or it's a keyword (either because we're in
                    // a generator or async function, or in strict mode (or both)) and it started a yield or await expression.
                    return true;
                default:
                    // Error tolerance.  If we see the start of some binary operator, we consider
                    // that the start of an expression.  That way we'll parse out a missing identifier,
                    // give a good message about an identifier being missing, and then consume the
                    // rest of the binary expression.
                    if (IsBinaryOperator())
                    {
                        return true;
                    }

                    return IsIdentifier();
            }
        }

        private bool IsStartOfStatement()
        {
            switch (m_token)
            {
                case SyntaxKind.AtToken:
                case SyntaxKind.SemicolonToken:
                case SyntaxKind.OpenBraceToken:
                case SyntaxKind.VarKeyword:
                case SyntaxKind.LetKeyword:
                case SyntaxKind.FunctionKeyword:
                case SyntaxKind.ClassKeyword:
                case SyntaxKind.EnumKeyword:
                case SyntaxKind.IfKeyword:
                case SyntaxKind.DoKeyword:
                case SyntaxKind.WhileKeyword:
                case SyntaxKind.ForKeyword:
                case SyntaxKind.ContinueKeyword:
                case SyntaxKind.BreakKeyword:
                case SyntaxKind.ReturnKeyword:
                case SyntaxKind.WithKeyword:
                case SyntaxKind.SwitchKeyword:
                case SyntaxKind.ThrowKeyword:
                case SyntaxKind.TryKeyword:
                case SyntaxKind.DebuggerKeyword:
                // 'catch' and 'finally' do not actually indicate that the code is part of a statement,
                // however, we say they are here so that we may gracefully parse them and error later.
                case SyntaxKind.CatchKeyword:
                case SyntaxKind.FinallyKeyword:
                    return true;

                case SyntaxKind.ConstKeyword:
                case SyntaxKind.ExportKeyword:
                case SyntaxKind.ImportKeyword:
                    return IsStartOfDeclaration();

                case SyntaxKind.AsyncKeyword:
                case SyntaxKind.DeclareKeyword:
                case SyntaxKind.InterfaceKeyword:
                case SyntaxKind.ModuleKeyword:
                case SyntaxKind.NamespaceKeyword:
                case SyntaxKind.TypeKeyword:
                    // When these don't start a declaration, they're an identifier in an expression statement
                    return true;

                case SyntaxKind.PublicKeyword:
                case SyntaxKind.PrivateKeyword:
                case SyntaxKind.ProtectedKeyword:
                case SyntaxKind.StaticKeyword:
                case SyntaxKind.ReadonlyKeyword:
                    // When these don't start a declaration, they may be the start of a class member if an identifier
                    // immediately follows. Otherwise they're an identifier in an expression statement.
                    return IsStartOfDeclaration() || !LookAhead(this, p => p.NextTokenIsIdentifierOrKeywordOnSameLine());

                default:
                    return IsStartOfExpression();
            }
        }

        private PropertyName ParsePropertyNameWorker(bool allowComputedPropertyNames)
        {
            if (m_token == SyntaxKind.StringLiteral || m_token == SyntaxKind.NumericLiteral)
            {
                return new PropertyName(ParseLiteralNode(/*internName*/true));
            }

            if (allowComputedPropertyNames && m_token == SyntaxKind.OpenBracketToken)
            {
                return new PropertyName(ParseComputedPropertyName());
            }

            return new PropertyName(ParseIdentifierName());
        }

        private IIdentifier ParseIdentifierName()
        {
            return CreateIdentifier(m_token.IsIdentifierOrKeyword());
        }

        // An identifier that starts with two underscores has an extra underscore character prepended to it to avoid issues
        // with magic property names like '__proto__'. The 'identifiers' object is used to share a single string instance for
        // each identifier in order to reduce memory consumption.
        private IIdentifier CreateIdentifier(bool isIdentifier, IDiagnosticMessage diagnosticMessage = null)
        {
            if (isIdentifier)
            {
                // If the next character is a backtick and the current text is a well-known identifier
                // then we can reuse an identifier instead of creating brand new one.
                // This is a major memory and perf optimization because path-like literals are verywhere.
                string identifierName = m_scanner.TokenText;
                if (Scanner.IsPathLikeInterpolationFactory(identifierName) && m_scanner.NextCharacter == CharacterCodes.Backtick)
                {
                    // Not using TryGetOrAdd to avoid closure allocation.
                    if (m_identifiersCache.TryGetValue(identifierName[0], out var result))
                    {
                        NextToken();
                        return result;
                    }

                    result = CreateIdentifierNode();
                    m_identifiersCache.Add(identifierName[0], result);
                    return result;
                }

                return CreateIdentifierNode();
            }

            return CreateMissingNode<Identifier>(SyntaxKind.Identifier, /*reportAtCurrentPosition*/ false, diagnosticMessage ?? Errors.Identifier_expected);
        }

        /// <nodoc />
        protected virtual IIdentifier CreateIdentifierNode()
        {
            m_identifierCount++;
            var node = CreateNode<Identifier>(SyntaxKind.Identifier);

            // Store original token kind if it is not just an Identifier so we can report appropriate error later in type checker
            if (m_token != SyntaxKind.Identifier)
            {
                node.OriginalKeywordKind = m_token;
            }

            node.Text = InternIdentifier(m_scanner.TokenValue);
            NextToken();
            return FinishNode(node);
        }

        private TNode CreateMissingNode<TNode>(SyntaxKind kind, bool reportAtCurrentPosition, IDiagnosticMessage diagnosticMessage = null, object arg0 = null) where TNode : IHasText, new()
        {
            if (reportAtCurrentPosition)
            {
                ParseErrorAtPosition(m_scanner.StartPos, 0, diagnosticMessage, arg0);
            }
            else
            {
                ParseErrorAtCurrentToken(diagnosticMessage, arg0);
            }

            TNode result = CreateNode<TNode>(kind, m_scanner.StartPos, GetLeadingTriviaLength(m_scanner.StartPos));
            var identifierResult = result.Cast<IHasText>();
            identifierResult.Text = string.Empty;
            return FinishNode(result);
        }

        internal TNode CreateNode<TNode>(SyntaxKind kind, int pos, int triviaLength)
            where TNode : INode, new()
        {
            return CreateNode<TNode>(kind, pos, triviaLength, true);
        }

        internal TNode CreateNode<TNode>(SyntaxKind kind)
            where TNode : INode, new()
        {
            return CreateNode<TNode>(kind, -1, -1, true);
        }

        internal TNode CreateNode<TNode>(SyntaxKind kind, int pos, int triviaLength, bool associateTrivia) where TNode : INode, new()
        {
            m_nodeCount++;
            if (pos < 0)
            {
                pos = m_scanner.StartPos;
            }

            if (triviaLength < 0)
            {
                triviaLength = GetLeadingTriviaLength(pos);
                Contract.Assert(triviaLength >= 0);
            }

            var node = NodeFactory.Create<TNode>(kind, pos, pos);

            // Now each node has a pointer to a source file.
            // This is perf requirement, because manual traversal is not cheap.
            node.SourceFile = m_sourceFile;

            node.SetLeadingTriviaLength(triviaLength, m_sourceFile);

            if (associateTrivia)
            {
                m_scanner.CollectAccumulatedTriviaAndReset(node);
            }

            return node;
        }

        private int GetLeadingTriviaLength(int pos)
        {
            return m_scanner.TokenPos - pos;
        }

        private IComputedPropertyName ParseComputedPropertyName()
        {
            // PropertyName [Yield]:
            //      LiteralPropertyName
            //      ComputedPropertyName[?Yield]
            var node = CreateNode<ComputedPropertyName>(SyntaxKind.ComputedPropertyName);
            ParseExpected(SyntaxKind.OpenBracketToken);

            // We parse any expression (including a comma expression). But the grammar
            // says that only an assignment expression is allowed, so the grammar checker
            // will error if it sees a comma expression.
            node.Expression = AllowInAnd(this, p => p.ParseExpression());

            ParseExpected(SyntaxKind.CloseBracketToken);
            return FinishNode(node);
        }

        private IExpression ParseExpression()
        {
            // Expression[in]:
            //      AssignmentExpression[in]
            //      Expression[in] , AssignmentExpression[in]

            // clear the decorator context when parsing Expression, as it should be unambiguous when parsing a decorator
            var saveDecoratorContext = InDecoratorContext();
            if (saveDecoratorContext)
            {
                SetDecoratorContext(/*val*/ false);
            }

            var expr = ParseAssignmentExpressionOrHigher();
            INode operatorToken = null;
            while ((operatorToken = ParseOptionalToken<TokenNode>(SyntaxKind.CommaToken)) != null)
            {
                expr = MakeBinaryExpression(expr, operatorToken, ParseAssignmentExpressionOrHigher());
            }

            if (saveDecoratorContext)
            {
                SetDecoratorContext(/*val*/ true);
            }

            return expr;
        }

        private void SetDecoratorContext(bool val)
        {
            SetContextFlag(val, ParserContextFlags.Decorator);
        }

        [NotNull]
        private static T AllowInAnd<T>(Parser parser, Func<Parser, T> func)
        {
            return parser.DoOutsideOfContext(parser, ParserContextFlags.DisallowIn, func);
        }

        private T DoOutsideOfContext<T>(Parser parser, ParserContextFlags context, Func<Parser, T> func)
        {
            // contextFlagsToClear will contain only the context flags that are
            // currently set that we need to temporarily clear
            // We don't just blindly reset to the previous flags to ensure
            // that we do not mutate cached flags for the incremental
            // parser (ThisNodeHasError, ThisNodeOrAnySubNodesHasError, and
            // HasAggregatedChildData).
            var contextFlagsToClear = context & m_contextFlags;
            if (contextFlagsToClear != ParserContextFlags.None)
            {
                // clear the requested context flags
                SetContextFlag(/*val*/ false, contextFlagsToClear);
                var result = func(parser);

                // restore the context flags we just cleared
                SetContextFlag(/*val*/ true, contextFlagsToClear);
                return result;
            }

            // no need to do anything special as we are not in any of the requested contexts
            return func(parser);
        }

        private void SetContextFlag(bool val, ParserContextFlags flag)
        {
            if (val)
            {
                m_contextFlags |= flag;
            }
            else
            {
                m_contextFlags &= ~flag;
            }
        }

        private bool ParseExpected(SyntaxKind kind, IDiagnosticMessage diagnosticMessage = null, bool shouldAdvance = true)
        {
            if (m_token == kind)
            {
                if (shouldAdvance)
                {
                    NextToken();
                }

                return true;
            }

            // Report specific message if provided with one.  Otherwise, report generic fallback message.
            if (diagnosticMessage != null)
            {
                ParseErrorAtCurrentToken(diagnosticMessage);
            }
            else
            {
                ParseErrorAtCurrentToken(Errors.Token_expected, Scanner.TokenToString(kind));
            }

            return false;
        }

        private void ParseErrorAtCurrentToken(IDiagnosticMessage message, object arg0 = null)
        {
            var start = m_scanner.TokenPos;
            var length = m_scanner.TextPos - start;

            ParseErrorAtPosition(start, length, message, arg0);
        }

        private IStringLiteralTypeNode ParseStringLiteralTypeNode()
        {
            var node = CreateNode<StringLiteralTypeNode>(SyntaxKind.StringLiteralType);
            node.LiteralKind = GetStringLiteralKind(SyntaxKind.StringLiteralType, m_scanner.CurrentCharacter);

            return (IStringLiteralTypeNode)ParseLiteralLikeNode(node, /*internName*/true);
        }

        private ILiteralExpression ParseLiteralNode(bool internName = false)
        {
            ILiteralLikeNode literalLikeNode = ParseLiteralLikeNode(m_token, internName);
            var result = (ILiteralExpression)literalLikeNode;
            return result;
        }

        /// <summary>
        /// Parses template literal fragment like "/literal" fragment in the string <code>"${expression}/literal"</code>.
        /// </summary>
        protected virtual ITemplateLiteralFragment ParseTemplateLiteralFragment()
        {
            var node = CreateNode<TemplateLiteralFragment>(m_token);
            return (ITemplateLiteralFragment)ParseLiteralLikeNode(node, /*internName*/ false);
        }

        /// <summary>
        /// Parses template literal head (i.e. the first string literal in the template expression before a fist <code>${...}</code>ю
        /// </summary>
        private ITemplateLiteralFragment ParseTemplateLiteralFragmentHead()
        {
            // In too many cases, the head of the template expression is empty.
            // Optimizing this saves hundreds of sousands of nodes in the Office/WDG builds.
            if (string.IsNullOrEmpty(m_scanner.TokenValue))
            {
                if (m_emptyTemplateHeadLiteral == null)
                {
                    m_emptyTemplateHeadLiteral = ParseTemplateLiteralFragment();
                }
                else
                {
                    // Still need to move the scanner forward.
                    NextToken();
                }

                return m_emptyTemplateHeadLiteral;
            }

            return ParseTemplateLiteralFragment();
        }

        private ILiteralLikeNode ParseLiteralLikeNode(ILiteralLikeNode node, bool internName)
        {
            var text = m_scanner.TokenValue;
            node.Text = internName ? InternIdentifier(text) : text;

            if (m_scanner.HasExtendedUnicodeEscape)
            {
                node.HasExtendedUnicodeEscape = true;
            }

            if (m_scanner.IsUnterminated)
            {
                node.IsUnterminated = true;
            }

            var tokenPos = m_scanner.TokenPos;
            NextToken();
            FinishNode(node);

            // Octal literals are not allowed in strict mode or ES5
            // Note that theoretically the following condition would hold true literals like 009,
            // which is not octal.But because of how the scanner separates the tokens, we would
            // never get a token like this. Instead, we would get 00 and 9 as two separate tokens.
            // We also do not need to check for negatives because any prefix operator would be part of a
            // parent unary expression.
            if (node.Kind == SyntaxKind.NumericLiteral
                && m_sourceText.CharCodeAt(tokenPos) == CharacterCodes._0
                && Scanner.IsOctalDigit(m_sourceText.CharCodeAt(tokenPos + 1)))
            {
                node.Flags |= NodeFlags.OctalLiteral;
            }

            return node;
        }

        private static LiteralExpressionKind GetStringLiteralKind(SyntaxKind kind, CharacterCodes characterCode)
        {
            // This is hacky solution to get back ticks around string literals like let x = `foo`;
            if (kind == SyntaxKind.FirstTemplateToken)
            {
                return LiteralExpressionKind.BackTick;
            }

            switch (characterCode)
            {
                case CharacterCodes.SingleQuote:
                    return LiteralExpressionKind.SingleQuote;
                case CharacterCodes.DoubleQuote:
                    return LiteralExpressionKind.DoubleQuote;

                default:
                    return LiteralExpressionKind.None;
            }
        }

        private ILiteralLikeNode ParseLiteralLikeNode(SyntaxKind kind, bool internName)
        {
            var node = CreateNode<LiteralExpression>(kind);
            node.LiteralKind = GetStringLiteralKind(kind, m_scanner.CurrentCharacter);

            var text = m_scanner.TokenValue;
            node.Text = internName ? InternIdentifier(text) : text;

            if (m_scanner.HasExtendedUnicodeEscape)
            {
                node.HasExtendedUnicodeEscape = true;
            }

            if (m_scanner.IsUnterminated)
            {
                node.IsUnterminated = true;
            }

            var tokenPos = m_scanner.TokenPos;
            NextToken();
            FinishNode(node);

            // Octal literals are not allowed in strict mode or ES5
            // Note that theoretically the following condition would hold true literals like 009,
            // which is not octal.But because of how the scanner separates the tokens, we would
            // never get a token like this. Instead, we would get 00 and 9 as two separate tokens.
            // We also do not need to check for negatives because any prefix operator would be part of a
            // parent unary expression.
            if (node.Kind == SyntaxKind.NumericLiteral
                && m_sourceText.CharCodeAt(tokenPos) == CharacterCodes._0
                && Scanner.IsOctalDigit(m_sourceText.CharCodeAt(tokenPos + 1)))
            {
                node.Flags |= NodeFlags.OctalLiteral;
            }

            return node;
        }

        private string InternIdentifier(string text)
        {
            // Currently, interning is no op, because an actual intening is happening in TextSource class.
            text = m_parsingOptions?.EscapeIdentifiers == true ? Utils.EscapeIdentifier(text) : text;
            return text;
        }

        private IExpression ParseNonParameterInitializer()
        {
            return ParseInitializer(/*inParameter*/ false);
        }

        private IExpression ParseInitializer(bool inParameter)
        {
            if (m_token != SyntaxKind.EqualsToken)
            {
                // It's not uncommon during typing for the user to miss writing the '=' token.  Check if
                // there is no newline after the last token and if we're on an expression.  If so, parse
                // this as an equals-value clause with a missing equals.
                // NOTE: There are two places where we allow equals-value clauses.  The first is in a
                // variable declarator.  The second is with a parameter.  For variable declarators
                // it's more likely that a { would be a allowed (as an object literal).  While this
                // is also allowed for parameters, the risk is that we consume the { as an object
                // literal when it really will be for the block following the parameter.
                if (m_scanner.HasPrecedingLineBreak || (inParameter && m_token == SyntaxKind.OpenBraceToken) || !IsStartOfExpression())
                {
                    // preceding line break, open brace in a parameter (likely a function body) or current token is not an expression -
                    // do not try to parse initializer
                    return null;
                }
            }

            // Initializer[In, Yield] :
            //     = AssignmentExpression[?In, ?Yield]
            ParseExpected(SyntaxKind.EqualsToken);
            return ParseAssignmentExpressionOrHigher();
        }

        private IExpression ParseAssignmentExpressionOrHigher()
        {
            // AssignmentExpression[in,yield]:
            //      1) ConditionalExpression[?in,?yield]
            //      2) LeftHandSideExpression = AssignmentExpression[?in,?yield]
            //      3) LeftHandSideExpression AssignmentOperator AssignmentExpression[?in,?yield]
            //      4) ArrowFunctionExpression[?in,?yield]
            //      5) [+Yield] YieldExpression[?In]
            //
            // Note: for ease of implementation we treat productions '2' and '3' as the same thing.
            // (i.e., they're both BinaryExpressions with an assignment operator in it).

            // First, do the simple check if we have a YieldExpression (production '5').
            if (IsYieldExpression())
            {
                return ParseYieldExpression();
            }

            // Then, check if we have an arrow function (production '4') that starts with a parenthesized
            // parameter list. If we do, we must *not* recurse for productions 1, 2 or 3. An ArrowFunction is
            // not a  LeftHandSideExpression, nor does it start a ConditionalExpression.  So we are done
            // with AssignmentExpression if we see one.
            var arrowExpression = TryParseParenthesizedArrowFunctionExpression();
            if (arrowExpression != null)
            {
                return arrowExpression;
            }

            // Now try to see if we're in production '1', '2' or '3'.  A conditional expression can
            // start with a LogicalOrExpression, while the assignment productions can only start with
            // LeftHandSideExpressions.
            //
            // So, first, we try to just parse out a BinaryExpression.  If we get something that is a
            // LeftHandSide or higher, then we can try to parse out the assignment expression part.
            // Otherwise, we try to parse out the conditional expression bit.  We want to allow any
            // binary expression here, so we pass in the 'lowest' precedence here so that it matches
            // and consumes anything.
            var expr = ParseBinaryExpressionOrHigher(/*precedence*/ 0);

            // To avoid a look-ahead, we did not handle the case of an arrow function with a single un-parenthesized
            // parameter ('x => ...') above. We handle it here by checking if the parsed expression was a single
            // identifier and the current token is an arrow.
            if (expr.Kind == SyntaxKind.Identifier && m_token == SyntaxKind.EqualsGreaterThanToken)
            {
                return ParseSimpleArrowFunctionExpression(expr.As<IIdentifier>());
            }

            // Now see if we might be in cases '2' or '3'.
            // If the expression was a LHS expression, and we have an assignment operator, then
            // we're in '2' or '3'. Consume the assignment and return.
            //
            // Note: we call reScanGreaterToken so that we get an appropriately merged token
            // for cases like > > =  becoming >>=
            if (Types.NodeUtilities.IsLeftHandSideExpression(expr) && ReScanGreaterToken().IsAssignmentOperator())
            {
                return MakeBinaryExpression(expr, ParseTokenNode<TokenNode>(), ParseAssignmentExpressionOrHigher());
            }

            // It wasn't an assignment or a lambda.  This is a conditional expression:
            return ParseConditionalExpressionRest(expr);
        }

        private IExpression ParseConditionalExpressionRest(IExpression leftOperand)
        {
            // Note: we are passed in an expression which was produced from parseBinaryExpressionOrHigher.
            var questionToken = ParseOptionalToken<TokenNode>(SyntaxKind.QuestionToken);
            if (questionToken == null)
            {
                return leftOperand;
            }

            // Note: we explicitly 'allowIn' in the whenTrue part of the condition expression, and
            // we do not that for the 'whenFalse' part.
            var node = CreateNode<ConditionalExpression>(SyntaxKind.ConditionalExpression, leftOperand.Pos, leftOperand.GetLeadingTriviaLength(m_sourceFile));
            node.Condition = leftOperand;
            node.QuestionToken = questionToken;
            node.WhenTrue = DoOutsideOfContext(this, m_disallowInAndDecoratorContext, p => p.ParseAssignmentExpressionOrHigher());
            node.ColonToken = ParseExpectedToken(SyntaxKind.ColonToken, /*reportAtCurrentPosition*/ false,
                Errors.Token_expected, Scanner.TokenToString(SyntaxKind.ColonToken));
            node.WhenFalse = ParseAssignmentExpressionOrHigher();
            return FinishNode(node);
        }

        private TokenNode ParseExpectedToken(SyntaxKind t, bool reportAtCurrentPosition, IDiagnosticMessage diagnosticMessage, object arg0 = null)
        {
            return
                ParseOptionalToken<TokenNode>(t) ??
                CreateMissingNode<TokenNode>(t, reportAtCurrentPosition, diagnosticMessage, arg0);
        }

        private T ParseExpectedToken<T>(SyntaxKind t, bool reportAtCurrentPosition, IDiagnosticMessage diagnosticMessage, object arg0 = null) where T : class, IHasText, new()
        {
            return
                ParseOptionalToken<T>(t) ??
                CreateMissingNode<T>(t, reportAtCurrentPosition, diagnosticMessage, arg0);
        }

        private IBinaryExpression MakeBinaryExpression(IExpression left, INode operatorToken, IExpression right)
        {
            var node = CreateNode<BinaryExpression>(SyntaxKind.BinaryExpression, left.Pos, left.GetLeadingTriviaLength(m_sourceFile));
            node.Left = left;
            node.OperatorToken = operatorToken;
            node.Right = right;
            return FinishNode(node);
        }

        private SyntaxKind ReScanGreaterToken()
        {
            return m_token = m_scanner.RescanGreaterToken();
        }

        private IExpression ParseSimpleArrowFunctionExpression(IIdentifier identifier)
        {
            Contract.Assert(m_token == SyntaxKind.EqualsGreaterThanToken, "parseSimpleArrowFunctionExpression should only have been called if we had a =>");

            var node = CreateNode<ArrowFunction>(SyntaxKind.ArrowFunction, identifier.Pos, identifier.GetLeadingTriviaLength(m_sourceFile));
            node.TypeParameters = null;
            var parameter = CreateNode<ParameterDeclaration>(SyntaxKind.Parameter, identifier.Pos, identifier.GetLeadingTriviaLength(m_sourceFile));
            parameter.Name = new IdentifierOrBindingPattern(identifier);
            FinishNode(parameter);

            node.Parameters = new NodeArray<IParameterDeclaration>();
            node.Parameters.Add(parameter);
            node.Parameters.PosIncludingStartToken = parameter.Pos;
            node.Parameters.Pos = parameter.Pos;
            node.Parameters.End = parameter.End;
            node.Parameters.EndIncludingEndToken = parameter.End;

            node.EqualsGreaterThanToken = ParseExpectedToken(SyntaxKind.EqualsGreaterThanToken, /*reportAtCurrentPosition*/ false, Errors.Token_expected, "=>");
            node.Body = ParseArrowFunctionExpressionBody(/*isAsync*/ false);

            return FinishNode(node);
        }

        private IExpression ParseBinaryExpressionOrHigher(int precedence)
        {
            var leftOperand = ParseUnaryExpressionOrHigher();
            return ParseBinaryExpressionRest(precedence, leftOperand);
        }

        /**
         * Parse ES7 unary expression and await expression
         *
         * ES7 UnaryExpression:parseSimpleArrowFunctionExpression
         * 1) SimpleUnaryExpression[?yield]
         * 2) IncrementExpression[?yield] ** UnaryExpression[?yield]
         */
        private IExpression ParseUnaryExpressionOrHigher()
        {
            if (IsAwaitExpression())
            {
                return ParseAwaitExpression();
            }

            if (IsIncrementExpression())
            {
                var incrementExpression = ParseIncrementExpression();
                return m_token == SyntaxKind.AsteriskAsteriskToken ?
                    (IExpression)ParseBinaryExpressionRest(GetBinaryOperatorPrecedence(), incrementExpression) :
                    incrementExpression;
            }

            var unaryOperator = m_token;
            var simpleUnaryExpression = ParseSimpleUnaryExpression();
            if (m_token == SyntaxKind.AsteriskAsteriskToken)
            {
                var start = Scanner.SkipTrivia(m_sourceText, simpleUnaryExpression.Pos);
                if (simpleUnaryExpression.Kind == SyntaxKind.TypeAssertionExpression)
                {
                    ParseErrorAtPosition(start, simpleUnaryExpression.End - start, Errors.A_type_assertion_expression_is_not_allowed_in_the_left_hand_side_of_an_exponentiation_expression_Consider_enclosing_the_expression_in_parentheses);
                }
                else
                {
                    ParseErrorAtPosition(start, simpleUnaryExpression.End - start, Errors.An_unary_expression_with_the_0_operator_is_not_allowed_in_the_left_hand_side_of_an_exponentiation_expression_Consider_enclosing_the_expression_in_parentheses, Scanner.TokenToString(unaryOperator));
                }
            }

            return simpleUnaryExpression;
        }

        private IExpression ParseBinaryExpressionRest(int precedence, IExpression leftOperand)
        {
            while (true)
            {
                // We either have a binary operator here, or we're finished.  We call
                // reScanGreaterToken so that we merge token sequences like > and = into >=
                ReScanGreaterToken();
                var newPrecedence = GetBinaryOperatorPrecedence();

                // Check the precedence to see if we should "take" this operator
                // - For left associative operator (all operator but **), consume the operator,
                //   recursively call the function below, and parse binaryExpression as a rightOperand
                //   of the caller if the new precendence of the operator is greater then or equal to the current precendence.
                //   For example:
                //      a - b - c;
                //            ^token; leftOperand = b. Return b to the caller as a rightOperand
                //      a * b - c
                //            ^token; leftOperand = b. Return b to the caller as a rightOperand
                //      a - b * c;
                //            ^token; leftOperand = b. Return b * c to the caller as a rightOperand
                // - For right associative operator (**), consume the operator, recursively call the function
                //   and parse binaryExpression as a rightOperand of the caller if the new precendence of
                //   the operator is strictly grater than the current precendence
                //   For example:
                //      a ** b ** c;
                //             ^^token; leftOperand = b. Return b ** c to the caller as a rightOperand
                //      a - b ** c;
                //            ^^token; leftOperand = b. Return b ** c to the caller as a rightOperand
                //      a ** b - c
                //             ^token; leftOperand = b. Return b to the caller as a rightOperand
                var consumeCurrentOperator = m_token == SyntaxKind.AsteriskAsteriskToken ?
                    newPrecedence >= precedence :
                    newPrecedence > precedence;

                if (!consumeCurrentOperator)
                {
                    break;
                }

                if (m_token == SyntaxKind.InKeyword && InDisallowInContext())
                {
                    break;
                }

                if (m_token == SyntaxKind.AsKeyword)
                {
                    // Make sure we *do* perform ASI for constructs like this:
                    //    var x = foo
                    //    as (Bar)
                    // This should be parsed as an initialized variable, followed
                    // by a function call to 'as' with the argument 'Bar'
                    if (m_scanner.HasPrecedingLineBreak)
                    {
                        break;
                    }
                    else
                    {
                        NextToken();
                        leftOperand = MakeAsExpression(leftOperand, ParseType());
                    }
                }
                else
                {
                    leftOperand = MakeBinaryExpression(leftOperand, ParseTokenNode<TokenNode>(), ParseBinaryExpressionOrHigher(newPrecedence));
                }
            }

            return leftOperand;
        }

        private IAsExpression MakeAsExpression(IExpression left, ITypeNode right)
        {
            var node = CreateNode<AsExpression>(SyntaxKind.AsExpression, left.Pos, left.GetLeadingTriviaLength(m_sourceFile));
            node.Expression = left;
            node.Type = right;
            return FinishNode(node);
        }

        /**
         * Parse ES7 IncrementExpression. IncrementExpression is used instead of ES6's PostFixExpression.
         *
         * ES7 IncrementExpression[yield]:
         * 1) LeftHandSideExpression[?yield]
         * 2) LeftHandSideExpression[?yield] [[no LineTerminator here]]++
         * 3) LeftHandSideExpression[?yield] [[no LineTerminator here]]--
         * 4) ++LeftHandSideExpression[?yield]
         * 5) --LeftHandSideExpression[?yield]
         * In TypeScript (2), (3) are parsed as PostfixUnaryExpression. (4), (5) are parsed as PrefixUnaryExpression
         */
        private IIncrementExpression ParseIncrementExpression()
        {
            if (m_token == SyntaxKind.PlusPlusToken || m_token == SyntaxKind.MinusMinusToken)
            {
                var node = CreateNode<PrefixUnaryExpression>(SyntaxKind.PrefixUnaryExpression);
                node.Operator = m_token;
                NextToken();
                node.Operand = ParseLeftHandSideExpressionOrHigher();
                return FinishNode(node);
            }
            else if (m_sourceFile.LanguageVariant == LanguageVariant.Jsx && m_token == SyntaxKind.LessThanToken &&
                     LookAhead(this, p => p.NextTokenIsIdentifierOrKeyword()))
            {
                // JSXElement is part of primaryExpression
                return ParseJsxElementOrSelfClosingElement();
            }

            var expression = ParseLeftHandSideExpressionOrHigher();

            Contract.Assert(Types.NodeUtilities.IsLeftHandSideExpression(expression));

            if ((m_token == SyntaxKind.PlusPlusToken || m_token == SyntaxKind.MinusMinusToken) && !m_scanner.HasPrecedingLineBreak)
            {
                var node = CreateNode<PostfixUnaryExpression>(SyntaxKind.PostfixUnaryExpression, expression.Pos, expression.GetLeadingTriviaLength(m_sourceFile));
                node.Operand = expression;
                node.Operator = m_token;
                NextToken();
                return FinishNode(node);
            }

            return expression;
        }

        private static IIncrementExpression ParseJsxElementOrSelfClosingElement()
        {
            throw PlaceHolder.NotImplemented();
        }

        private bool NextTokenIsIdentifierOrKeyword()
        {
            NextToken();
            return m_token.IsIdentifierOrKeyword();
        }

        /**
         * Check if the current token can possibly be an ES7 increment expression.
         *
         * ES7 IncrementExpression:
         * LeftHandSideExpression[?Yield]
         * LeftHandSideExpression[?Yield][no LineTerminator here]++
         * LeftHandSideExpression[?Yield][no LineTerminator here]--
         * ++LeftHandSideExpression[?Yield]
         * --LeftHandSideExpression[?Yield]
         */
        private bool IsIncrementExpression()
        {
            // This function is called inside parseUnaryExpression to decide
            // whether to call parseSimpleUnaryExpression or call parseIncrmentExpression directly
            switch (m_token)
            {
                case SyntaxKind.PlusToken:
                case SyntaxKind.MinusToken:
                case SyntaxKind.TildeToken:
                case SyntaxKind.ExclamationToken:
                case SyntaxKind.DeleteKeyword:
                case SyntaxKind.TypeOfKeyword:
                case SyntaxKind.VoidKeyword:
                    return false;
                case SyntaxKind.LessThanToken:
                    // If we are not in JSX context, we are parsing TypeAssertion which is an UnaryExpression
                    if (m_sourceFile.LanguageVariant != LanguageVariant.Jsx)
                    {
                        return false;
                    }

                    // We are in JSX context and the token is part of JSXElement.
                    // Fall through
                    return false;

                default:
                    return true;
            }
        }

        private bool IsAwaitExpression()
        {
            if (m_token == SyntaxKind.AwaitKeyword)
            {
                if (InAwaitContext())
                {
                    return true;
                }

                // here we are using similar heuristics as 'isYieldExpression'
                return LookAhead(this, p => p.NextTokenIsIdentifierOnSameLine());
            }

            return false;
        }

        private static IAwaitExpression ParseAwaitExpression()
        {
            throw PlaceHolder.NotImplemented();

            // var node = createNode<AwaitExpression>(SyntaxKind.AwaitExpression);
            // nextToken();
            // node.expression = parseSimpleUnaryExpression();
            // return finishNode(node);
        }

        /**
         * Parse ES7 simple-unary expression or higher:
         *
         * ES7 SimpleUnaryExpression:
         * 1) IncrementExpression[?yield]
         * 2) delete UnaryExpression[?yield]
         * 3) void UnaryExpression[?yield]
         * 4) typeof UnaryExpression[?yield]
         * 5) + UnaryExpression[?yield]
         * 6) - UnaryExpression[?yield]
         * 7) ~ UnaryExpression[?yield]
         * 8) ! UnaryExpression[?yield]
         */
        private IUnaryExpression ParseSimpleUnaryExpression()
        {
            switch (m_token)
            {
                case SyntaxKind.PlusToken:
                case SyntaxKind.MinusToken:
                case SyntaxKind.TildeToken:
                case SyntaxKind.ExclamationToken:
                    return ParsePrefixUnaryExpression();
                case SyntaxKind.DeleteKeyword:
                    return ParseDeleteExpression();
                case SyntaxKind.TypeOfKeyword:
                    return ParseTypeOfExpression();
                case SyntaxKind.VoidKeyword:
                    return ParseVoidExpression();
                case SyntaxKind.LessThanToken:
                    // This is modified UnaryExpression grammar in TypeScript
                    //  UnaryExpression (modified):
                    //      < type > UnaryExpression
                    return ParseTypeAssertion();
                default:
                    return ParseIncrementExpression();
            }
        }

        private IPrefixUnaryExpression ParsePrefixUnaryExpression()
        {
            var node = CreateNode<PrefixUnaryExpression>(SyntaxKind.PrefixUnaryExpression);
            node.Operator = m_token;
            NextToken();
            node.Operand = ParseSimpleUnaryExpression();

            return FinishNode(node);
        }

        private IDeleteExpression ParseDeleteExpression()
        {
            var node = CreateNode<DeleteExpression>(SyntaxKind.DeleteExpression);
            NextToken();
            node.Expression = ParseSimpleUnaryExpression();
            return FinishNode(node);
        }

        private ITypeOfExpression ParseTypeOfExpression()
        {
            var node = CreateNode<TypeOfExpression>(SyntaxKind.TypeOfExpression);
            NextToken();
            node.Expression = ParseSimpleUnaryExpression();
            return FinishNode(node);
        }

        private IVoidExpression ParseVoidExpression()
        {
            var node = CreateNode<VoidExpression>(SyntaxKind.VoidExpression);
            NextToken();
            node.Expression = ParseSimpleUnaryExpression();
            return FinishNode(node);
        }

        private ITypeAssertion ParseTypeAssertion()
        {
            var node = CreateNode<TypeAssertion>(SyntaxKind.TypeAssertionExpression);
            ParseExpected(SyntaxKind.LessThanToken);
            node.Type = ParseType();
            ParseExpected(SyntaxKind.GreaterThanToken);
            node.Expression = ParseSimpleUnaryExpression();
            return FinishNode(node);
        }

        private bool NextTokenIsIdentifierOnSameLine()
        {
            NextToken();
            return !m_scanner.HasPrecedingLineBreak && IsIdentifier();
        }

        // True        -> We definitely expect a parenthesized arrow function here.
        //  False       -> There *cannot* be a parenthesized arrow function here.
        //  Unknown     -> There *might* be a parenthesized arrow function here.
        //                 Speculatively look ahead to be sure, and rollback if not.
        private Tristate IsParenthesizedArrowFunctionExpression()
        {
            if (m_token == SyntaxKind.OpenParenToken || m_token == SyntaxKind.LessThanToken || m_token == SyntaxKind.AsyncKeyword)
            {
                return LookAhead(this, p => p.IsParenthesizedArrowFunctionExpressionWorker());
            }

            if (m_token == SyntaxKind.EqualsGreaterThanToken)
            {
                // ERROR RECOVERY TWEAK:
                // If we see a standalone => try to parse it as an arrow function expression as that's
                // likely what the user intended to write.
                return Tristate.True;
            }

            // Definitely not a parenthesized arrow function.
            return Tristate.False;
        }

        private Tristate IsParenthesizedArrowFunctionExpressionWorker()
        {
            if (m_token == SyntaxKind.AsyncKeyword)
            {
                NextToken();
                if (m_scanner.HasPrecedingLineBreak)
                {
                    return Tristate.False;
                }

                if (m_token != SyntaxKind.OpenParenToken && m_token != SyntaxKind.LessThanToken)
                {
                    return Tristate.False;
                }
            }

            var first = m_token;
            var second = NextToken();

            if (first == SyntaxKind.OpenParenToken)
            {
                if (second == SyntaxKind.CloseParenToken)
                {
                    // Simple cases: "() =>", "(): ", and  "() {".
                    // This is an arrow function with no parameters.
                    // The last one is not actually an arrow function,
                    // but this is probably what the user intended.
                    var third = NextToken();
                    switch (third)
                    {
                        case SyntaxKind.EqualsGreaterThanToken:
                        case SyntaxKind.ColonToken:
                        case SyntaxKind.OpenBraceToken:
                            return Tristate.True;
                        default:
                            return Tristate.False;
                    }
                }

                // If encounter "([" or "({", this could be the start of a binding pattern.
                // Examples:
                //      ([ x ]) => { }
                //      ({ x }) => { }
                //      ([ x ])
                //      ({ x })
                if (second == SyntaxKind.OpenBracketToken || second == SyntaxKind.OpenBraceToken)
                {
                    return Tristate.Unknown;
                }

                // Simple case: "(..."
                // This is an arrow  with a rest parameter.
                if (second == SyntaxKind.DotDotDotToken)
                {
                    return Tristate.True;
                }

                // If we had "(" followed by something that's not an identifier,
                // then this definitely doesn't look like a lambda.
                // we Note could be a little more lenient and allow
                // "(public" or "(private". These would not ever actually be allowed,
                // but we could provide a good error message instead of bailing out.
                if (!IsIdentifier())
                {
                    return Tristate.False;
                }

                // If we have something like "(a:", then we must have a
                // type-annotated parameter in an arrow  expression.
                if (NextToken() == SyntaxKind.ColonToken)
                {
                    return Tristate.True;
                }

                // This *could* be a parenthesized arrow function.
                // Return Unknown to var the caller know.
                return Tristate.Unknown;
            }
            else
            {
                Contract.Assert(first == SyntaxKind.LessThanToken);

                // If we have "<" not followed by an identifier,
                // then this definitely is not an arrow function.
                if (!IsIdentifier())
                {
                    return Tristate.False;
                }

                // JSX overrides
                if (m_sourceFile.LanguageVariant == LanguageVariant.Jsx)
                {
                    var isArrowFunctionInJsx = LookAhead(this, p =>
                    {
                        var third = p.NextToken();
                        if (third == SyntaxKind.ExtendsKeyword)
                        {
                            var fourth = p.NextToken();
                            switch (fourth)
                            {
                                case SyntaxKind.EqualsToken:
                                case SyntaxKind.GreaterThanToken:
                                    return false;
                                default:
                                    return true;
                            }
                        }
                        if (third == SyntaxKind.CommaToken)
                        {
                            return true;
                        }
                        return false;
                    });

                    if (isArrowFunctionInJsx)
                    {
                        return Tristate.True;
                    }

                    return Tristate.False;
                }

                // This *could* be a parenthesized arrow function.
                return Tristate.Unknown;
            }
        }

        private static void SetModifiers(INode node, ModifiersArray modifiers)
        {
            if (modifiers != null)
            {
                node.Flags |= modifiers.Flags;
                node.Modifiers = modifiers;
            }
        }

        private ModifiersArray ParseModifiersForArrowFunction()
        {
            NodeFlags flags = 0;
            ModifiersArray modifiers = null;
            if (m_token == SyntaxKind.AsyncKeyword)
            {
                var modifierStart = m_scanner.StartPos;
                var triviaLength = GetLeadingTriviaLength(modifierStart);
                var modifierKind = m_token;
                NextToken();
                modifiers = ModifiersArray.Create(NodeFlags.None);
                modifiers.Pos = modifierStart;
                flags |= modifierKind.ModifierToFlag();
                modifiers.Add(FinishNode(CreateNode<Modifier>(modifierKind, modifierStart, triviaLength)));
                modifiers.Flags = flags;
                modifiers.End = m_scanner.StartPos;
            }

            return modifiers;
        }

        private IArrowFunction ParseParenthesizedArrowFunctionExpressionHead(bool allowAmbiguity)
        {
            var node = CreateNode<ArrowFunction>(SyntaxKind.ArrowFunction);

            SetModifiers(node, ParseModifiersForArrowFunction());

            // was !!(node.flags & NodeFlags.Async)
            var isAsync = (node.Flags & NodeFlags.Async) != NodeFlags.None;

            // Arrow functions are never generators.
            //
            // If we're speculatively parsing a signature for a parenthesized arrow function, then
            // we have to have a complete parameter list.  Otherwise we might see something like
            // a => (b => c)
            // And think that "(b =>" was actually a parenthesized arrow function with a missing
            // close paren.
            FillSignature(SyntaxKind.ColonToken, /*yieldContext*/ false, /*awaitContext*/ isAsync, /*requireCompleteParameterList*/ !allowAmbiguity, node);

            // If we couldn't get parameters, we definitely could not parse out an arrow function.
            if (node.Parameters == null)
            {
                return null;
            }

            // Parsing a signature isn't enough.
            // Parenthesized arrow signatures often look like other valid expressions.
            // For instance:
            //  - "(x = 10)" is an assignment expression parsed as a signature with a default parameter value.
            //  - "(x,y)" is a comma expression parsed as a signature with two parameters.
            //  - "a ? (b): c" will have "(b):" parsed as a signature with a return type annotation.
            //
            // So we need just a bit of lookahead to ensure that it can only be a signature.
            if (!allowAmbiguity && m_token != SyntaxKind.EqualsGreaterThanToken && m_token != SyntaxKind.OpenBraceToken)
            {
                // Returning undefined here will cause our caller to rewind to where we started from.
                return null;
            }

            return node;
        }

        private void FillSignature(
            SyntaxKind returnToken,
            bool yieldContext,
            bool awaitContext,
            bool requireCompleteParameterList,
            ISignatureDeclaration signature)
        {
            var returnTokenRequired = returnToken == SyntaxKind.EqualsGreaterThanToken;
            signature.TypeParameters = ParseTypeParameters();
            signature.Parameters = ParseParameterList(yieldContext, awaitContext, requireCompleteParameterList);

            if (returnTokenRequired)
            {
                ParseExpected(returnToken);
                signature.Type = ParseType();
            }
            else if (ParseOptional(returnToken))
            {
                signature.Type = ParseType();
            }
        }

        private NodeArray<IParameterDeclaration> ParseParameterList(bool yieldContext, bool awaitContext, bool requireCompleteParameterList)
        {
            // FormalParameters [Yield,Await]: (modified)
            //      [empty]
            //      FormalParameterList[?Yield,Await]
            //
            // FormalParameter[Yield,Await]: (modified)
            //      BindingElement[?Yield,Await]
            //
            // BindingElement [Yield,Await]: (modified)
            //      SingleNameBinding[?Yield,?Await]
            //      BindingPattern[?Yield,?Await]Initializer [In, ?Yield,?Await] opt
            //
            // SingleNameBinding [Yield,Await]:
            //      BindingIdentifier[?Yield,?Await]Initializer [In, ?Yield,?Await] opt
            var posIncludingStartToken = m_scanner.TokenPos;

            if (ParseExpected(SyntaxKind.OpenParenToken))
            {
                var savedYieldContext = InYieldContext();
                var savedAwaitContext = InAwaitContext();

                SetYieldContext(yieldContext);
                SetAwaitContext(awaitContext);

                NodeArray<IParameterDeclaration> result = ParseDelimitedList(this, ParsingContext.Parameters, p => p.ParseParameter());

                SetYieldContext(savedYieldContext);
                SetAwaitContext(savedAwaitContext);

                if (!ParseExpected(SyntaxKind.CloseParenToken) && requireCompleteParameterList)
                {
                    // Caller insisted that we had to end with a )   We didn't.  So just return null.
                    return null;
                }

                result.PosIncludingStartToken = posIncludingStartToken;
                result.EndIncludingEndToken = m_scanner.TokenPos;

                return result;
            }

            // We didn't even have an open paren.  If the caller requires a complete parameter list,
            // we definitely can't provide that.  However, if they're ok with an incomplete one,
            // then just return an empty set of parameters.

            // THis is a marker of a failure
            return requireCompleteParameterList ? null : CreateMissingList<IParameterDeclaration>();
        }

        [CanBeNull]
        private NodeArray<ITypeParameterDeclaration> ParseTypeParameters()
        {
            // There is a sublte difference between empty array and null, even that violates common .NET idiom of not using nulls as a collections.
            // In this codebase theres is a lot of places that distinguishes empty type parameters from null.
            if (m_token == SyntaxKind.LessThanToken)
            {
                return ParseBracketedList(this, ParsingContext.TypeParameters, p => p.ParseTypeParameter(), SyntaxKind.LessThanToken, SyntaxKind.GreaterThanToken);
            }

            return null;
        }

        private ITypeParameterDeclaration ParseTypeParameter()
        {
            var node = CreateNode<TypeParameterDeclaration>(SyntaxKind.TypeParameter);
            node.Name = ParseIdentifier();
            if (ParseOptional(SyntaxKind.ExtendsKeyword))
            {
                // It's not uncommon for people to write improper varraints to a generic.  If the
                // user writes a varraint that is an expression and not an actual type, then parse
                // it out as an expression (so we can recover well), but report that a type is needed
                // instead.
                if (IsStartOfType() || !IsStartOfExpression())
                {
                    node.Constraint = ParseType();
                }
                else
                {
                    // It was not a type, and it looked like an expression.  Parse out an expression
                    // here so we recover well.  it Note is important that we call parseUnaryExpression
                    // and not parseExpression here.  If the user has:
                    //
                    //      <T extends "">
                    //
                    // We do *not* want to consume the  >  as we're consuming the expression for "".
                    node.Expression = ParseUnaryExpressionOrHigher();
                }
            }

            return FinishNode(node);
        }

        private NodeArray<T> ParseBracketedList<T>(Parser parser, ParsingContext kind, Func<Parser, T> parseElement, SyntaxKind open, SyntaxKind close) where T : INode
        {
            Contract.Ensures(Contract.Result<NodeArray<T>>() != null);

            var posIncludingStartToken = m_scanner.TokenPos;

            // throw PlaceHolder.NotImplemented();
            if (ParseExpected(open))
            {
                var result = ParseDelimitedList(parser, kind, parseElement);
                ParseExpected(close);
                result.PosIncludingStartToken = posIncludingStartToken;
                result.EndIncludingEndToken = m_scanner.TokenPos;
                return result;
            }

            return CreateMissingList<T>();
        }

        private IExpression TryParseParenthesizedArrowFunctionExpression()
        {
            var triState = IsParenthesizedArrowFunctionExpression();
            if (triState == Tristate.False)
            {
                // It's definitely not a parenthesized arrow function expression.
                return null;
            }

            // If we definitely have an arrow function, then we can just parse one, not requiring a
            // following => or { token. Otherwise, we *might* have an arrow function.  Try to parse
            // it out, but don't allow any ambiguity, and return 'undefined' if this could be an
            // expression instead.
            var arrowFunction = triState == Tristate.True
                ? ParseParenthesizedArrowFunctionExpressionHead(/*allowAmbiguity*/ true)
                : TryParse(this, p => p.ParsePossibleParenthesizedArrowFunctionExpressionHead());

            if (arrowFunction == null)
            {
                // Didn't appear to actually be a parenthesized arrow function.  Just bail out.
                return null;
            }

            var isAsync = (arrowFunction.Flags & NodeFlags.Async) != NodeFlags.None;

            // If we have an arrow, then try to parse the body. Even if not, try to parse if we
            // have an opening brace, just in case we're in an error state.
            var lastToken = m_token;
            arrowFunction.EqualsGreaterThanToken = ParseExpectedToken(SyntaxKind.EqualsGreaterThanToken, /*reportAtCurrentPosition*/false, Errors.Token_expected, "=>");
            arrowFunction.Body = lastToken == SyntaxKind.EqualsGreaterThanToken || lastToken == SyntaxKind.OpenBraceToken
                ? ParseArrowFunctionExpressionBody(isAsync)
                : new ConciseBody(ParseIdentifier());

            return FinishNode(arrowFunction);
        }

        private ConciseBody ParseArrowFunctionExpressionBody(bool isAsync)
        {
            if (m_token == SyntaxKind.OpenBraceToken)
            {
                return new ConciseBody(ParseFunctionBlock(/*allowYield*/ false, /*allowAwait*/ isAsync, /*ignoreMissingOpenBrace*/ false));
            }

            if (m_token != SyntaxKind.SemicolonToken &&
                m_token != SyntaxKind.FunctionKeyword &&
                m_token != SyntaxKind.ClassKeyword &&
                IsStartOfStatement() &&
                !IsStartOfExpressionStatement())
            {
                // Check if we got a plain statement (i.e., no expression-statements, no function/class expressions/declarations)
                //
                // Here we try to recover from a potential error situation in the case where the
                // user meant to supply a block. For example, if the user wrote:
                //
                //  a =>
                //      let v = 0;
                //  }
                //
                // they may be missing an open brace.  Check to see if that's the case so we can
                // try to recover better.  If we don't do this, then the next close curly we see may end
                // up preemptively closing the containing construct.
                //
                // Note: even when 'ignoreMissingOpenBrace' is passed as true, parseBody will still error.
                return new ConciseBody(ParseFunctionBlock(/*allowYield*/ false, /*allowAwait*/ isAsync, /*ignoreMissingOpenBrace*/ true));
            }

            return isAsync
                ? new ConciseBody(DoInAwaitContext(this, p => p.ParseAssignmentExpressionOrHigher()))
                : new ConciseBody(DoOutsideOfAwaitContext(this, p => p.ParseAssignmentExpressionOrHigher()));
        }

        private T DoInAwaitContext<T>(Parser parser, Func<Parser, T> func)
        {
            return DoInsideOfContext(parser, ParserContextFlags.Await, func);
        }

        private T DoOutsideOfAwaitContext<T>(Parser parser, Func<Parser, T> func)
        {
            return DoOutsideOfContext(parser, ParserContextFlags.Await, func);
        }

        private bool IsStartOfExpressionStatement()
        {
            // As per the grammar, none of '{' or 'function' or 'class' can start an expression statement.
            return m_token != SyntaxKind.OpenBraceToken &&
                m_token != SyntaxKind.FunctionKeyword &&
                m_token != SyntaxKind.ClassKeyword &&
                m_token != SyntaxKind.AtToken &&
                IsStartOfExpression();
        }

        private IArrowFunction ParsePossibleParenthesizedArrowFunctionExpressionHead()
        {
            return ParseParenthesizedArrowFunctionExpressionHead(/*allowAmbiguity*/ false);
        }

        private bool IsYieldExpression()
        {
            // TODO: Don't need them for now!
            // return false;
            if (m_token == SyntaxKind.YieldKeyword)
            {
                // If we have a 'yield' keyword, and htis is a context where yield expressions are
                // allowed, then definitely parse out a yield expression.
                if (InYieldContext())
                {
                    return true;
                }

                // We're in a context where 'yield expr' is not allowed.  However, if we can
                // definitely tell that the user was trying to parse a 'yield expr' and not
                // just a normal expr that start with a 'yield' identifier, then parse out
                // a 'yield expr'.  We can then report an error later that they are only
                // allowed in generator expressions.
                //
                // for example, if we see 'yield(foo)', then we'll have to treat that as an
                // invocation expression of something called 'yield'.  However, if we have
                // 'yield foo' then that is not legal as a normal expression, so we can
                // definitely recognize this as a yield expression.
                //
                // for now we just check if the next token is an identifier.  More heuristics
                // can be added here later as necessary.  We just need to make sure that we
                // don't accidently consume something legal.
                return LookAhead(this, p => p.NextTokenIsIdentifierOrKeywordOrNumberOnSameLine());
            }

            // NextTokenIsIdentifierOrKeywordOnSameLine
            // NextTokenIsIdentifierOrKeywordOrNumberOnSameLine
            return false;
        }

        private bool NextTokenIsIdentifierOrKeywordOrNumberOnSameLine()
        {
            NextToken();
            return (m_token.IsIdentifierOrKeyword() || m_token == SyntaxKind.NumericLiteral) && !m_scanner.HasPrecedingLineBreak;
        }

        private IYieldExpression ParseYieldExpression()
        {
            var node = CreateNode<YieldExpression>(SyntaxKind.YieldExpression);

            // YieldExpression[In] :
            //      yield
            //      yield [no LineTerminator here] [Lexical goal InputElementRegExp]AssignmentExpression[?In, Yield]
            //      yield [no LineTerminator here] * [Lexical goal InputElementRegExp]AssignmentExpression[?In, Yield]
            NextToken();

            if (!m_scanner.HasPrecedingLineBreak &&
                (m_token == SyntaxKind.AsteriskToken || IsStartOfExpression()))
            {
                node.AsteriskToken = ParseOptionalToken<TokenNode>(SyntaxKind.AsteriskToken);
                node.Expression = ParseAssignmentExpressionOrHigher();
                return FinishNode(node);
            }
            else
            {
                // if the next token is not on the same line as yield.  or we don't have an '*' or
                // the start of an expressin, then this is just a simple "yield" expression.
                return FinishNode(node);
            }
        }

        /// <summary>
        /// Returns true if a given statement has a 'qualifier' declaration.
        /// </summary>
        protected virtual bool IsQualifierDeclaration(IStatement statement)
        {
            return statement.IsQualifierDeclaration();
        }

        private bool ParseOptional(SyntaxKind t)
        {
            if (m_token == t)
            {
                NextToken();
                return true;
            }

            return false;
        }

        private TNode ParseOptionalToken<TNode>(SyntaxKind t) where TNode : INode, new()
        {
            if (m_token == t)
            {
                return ParseTokenNode<TNode>();
            }

            return default(TNode);
        }

        /// <nodoc/>
        public interface IIncrementalElement : ITextRange
        {
            /// <nodoc/>
            INode Parent { get; set; }

            /// <nodoc/>
            bool IntersectsChange { get; set; }

            /// <nodoc/>
            Types.Optional<int> Length { get; set; }

            /// <nodoc/>
            INode[] Children { get; set; }
        }

        /// <nodoc/>
        // TODO: Handle multiple inheritance
        public interface IIncrementalNode : INode, IIncrementalElement
        {
            /// <nodoc/>
            IIncrementalElement Element { get; set; }

            /// <nodoc/>
            bool HasBeenIncrementallyParsed { get; set; }
        }

        /// <nodoc/>
        public class IncrementalParser
        {
            /// <summary>
            /// Allows finding nodes in the source file at a certain position in an efficient manner.
            /// The implementation takes advantage of the calling pattern it knows the parser will
            /// make in order to optimize finding nodes as quickly as possible.
            /// </summary>
            public abstract class SyntaxCursor
            {
                /// <nodoc/>
                public abstract IIncrementalNode CurrentNode(int position);
            }
        }

        private enum Tristate
        {
            False,
            True,
            Unknown,
        }
    }
}
