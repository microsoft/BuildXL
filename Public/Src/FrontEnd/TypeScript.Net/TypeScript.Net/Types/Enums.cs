// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

#pragma warning disable 1591 // disabling warning about missing API documentation; TODO: Remove this line and write documentation!

namespace TypeScript.Net.Types
{
    /// <summary>
    /// token > SyntaxKind.Identifer => token is a keyword.
    /// Also, If you add a new SyntaxKind be sure to keep the `Markers` section at the bottom in sync.
    /// </summary>
    public enum SyntaxKind : byte
    {
        Unknown,
        EndOfFileToken,
        SingleLineCommentTrivia,
        MultiLineCommentTrivia,
        NewLineTrivia,
        WhitespaceTrivia,

        // We detect and preserve #! on the first line
        ShebangTrivia,

        // We detect and provide better error recovery when we encounter a git merge marker.  This
        // allows us to edit files with git-conflict markers in them in a much more pleasant manner.
        ConflictMarkerTrivia,

        // Literals
        NumericLiteral,
        StringLiteral,
        RegularExpressionLiteral,
        NoSubstitutionTemplateLiteral,

        // Pseudo-literals
        TemplateHead,
        TemplateMiddle,
        TemplateTail,

        // Punctuation
        OpenBraceToken,
        CloseBraceToken,
        OpenParenToken,
        CloseParenToken,
        OpenBracketToken,
        CloseBracketToken,
        DotToken,
        DotDotDotToken,
        SemicolonToken,
        CommaToken,
        LessThanToken,
        LessThanSlashToken,
        GreaterThanToken,
        LessThanEqualsToken,
        GreaterThanEqualsToken,
        EqualsEqualsToken,
        ExclamationEqualsToken,
        EqualsEqualsEqualsToken,
        ExclamationEqualsEqualsToken,
        EqualsGreaterThanToken,
        PlusToken,
        MinusToken,
        AsteriskToken,
        AsteriskAsteriskToken,
        SlashToken,
        PercentToken,
        PlusPlusToken,
        MinusMinusToken,
        LessThanLessThanToken,
        GreaterThanGreaterThanToken,
        GreaterThanGreaterThanGreaterThanToken,
        AmpersandToken,
        BarToken,
        CaretToken,
        ExclamationToken,
        TildeToken,
        AmpersandAmpersandToken,
        BarBarToken,
        QuestionToken,
        ColonToken,
        AtToken,

        // Assignments
        EqualsToken,
        PlusEqualsToken,
        MinusEqualsToken,
        AsteriskEqualsToken,
        AsteriskAsteriskEqualsToken,
        SlashEqualsToken,
        PercentEqualsToken,
        LessThanLessThanEqualsToken,
        GreaterThanGreaterThanEqualsToken,
        GreaterThanGreaterThanGreaterThanEqualsToken,
        AmpersandEqualsToken,
        BarEqualsToken,
        CaretEqualsToken,

        // Identifiers
        Identifier,

        // Reserved words
        BreakKeyword,
        CaseKeyword,
        CatchKeyword,
        ClassKeyword,
        ConstKeyword,
        ContinueKeyword,
        DebuggerKeyword,
        DefaultKeyword,
        DeleteKeyword,
        DoKeyword,
        ElseKeyword,
        EnumKeyword,
        ExportKeyword,
        ExtendsKeyword,
        FalseKeyword,
        FinallyKeyword,
        ForKeyword,
        FunctionKeyword,
        IfKeyword,
        ImportKeyword,
        InKeyword,
        InstanceOfKeyword,
        NewKeyword,
        NullKeyword,
        ReturnKeyword,
        SuperKeyword,
        SwitchKeyword,
        ThisKeyword,
        ThrowKeyword,
        TrueKeyword,
        TryKeyword,
        TypeOfKeyword,
        VarKeyword,
        VoidKeyword,
        WhileKeyword,
        WithKeyword,

        // Strict mode reserved words
        ImplementsKeyword,
        InterfaceKeyword,
        LetKeyword,
        PackageKeyword,
        PrivateKeyword,
        ProtectedKeyword,
        PublicKeyword,
        StaticKeyword,
        YieldKeyword,

        // Contextual keywords
        AbstractKeyword,
        AsKeyword,
        AnyKeyword,
        AsyncKeyword,
        AwaitKeyword,
        BooleanKeyword,
        ConstructorKeyword,
        DeclareKeyword,
        GetKeyword,
        IsKeyword,
        ModuleKeyword,
        NamespaceKeyword,
        ReadonlyKeyword,
        RequireKeyword,
        NumberKeyword,
        SetKeyword,
        StringKeyword,
        SymbolKeyword,
        TypeKeyword,
        FromKeyword,
        OfKeyword, // LastKeyword and LastToken

        // Parse tree nodes

        // Names
        QualifiedName,
        ComputedPropertyName,

        // Signature elements
        TypeParameter,
        Parameter,
        Decorator,

        // TypeMember
        PropertySignature,
        PropertyDeclaration,
        MethodSignature,
        MethodDeclaration,
        Constructor,
        GetAccessor,
        SetAccessor,
        CallSignature,
        ConstructSignature,
        IndexSignature,

        // Type
        TypePredicate,
        TypeReference,
        FunctionType,
        ConstructorType,
        TypeQuery,
        TypeLiteral,
        ArrayType,
        TupleType,
        UnionType,
        IntersectionType,
        ParenthesizedType,
        ThisType,
        StringLiteralType,

        // Binding patterns
        ObjectBindingPattern,
        ArrayBindingPattern,
        BindingElement,

        // Expression
        ArrayLiteralExpression,
        ObjectLiteralExpression,
        PropertyAccessExpression,
        ElementAccessExpression,
        CallExpression,
        NewExpression,
        TaggedTemplateExpression,
        TypeAssertionExpression,
        ParenthesizedExpression,
        FunctionExpression,
        ArrowFunction,
        DeleteExpression,
        TypeOfExpression,
        VoidExpression,
        AwaitExpression,
        PrefixUnaryExpression,
        PostfixUnaryExpression,
        BinaryExpression,
        ConditionalExpression,
        TemplateExpression,
        YieldExpression,
        SpreadElementExpression,
        ClassExpression,
        OmittedExpression,
        ExpressionWithTypeArguments,
        AsExpression,

        // Misc
        TemplateSpan,
        SemicolonClassElement,

        // Element
        Block,
        VariableStatement,
        EmptyStatement,
        BlankLineStatement,
        ExpressionStatement,
        IfStatement,
        DoStatement,
        WhileStatement,
        ForStatement,
        ForInStatement,
        ForOfStatement,
        ContinueStatement,
        BreakStatement,
        ReturnStatement,
        WithStatement,
        SwitchStatement,
        LabeledStatement,
        ThrowStatement,
        TryStatement,
        DebuggerStatement,
        VariableDeclaration,
        VariableDeclarationList,
        FunctionDeclaration,
        ClassDeclaration,
        InterfaceDeclaration,
        TypeAliasDeclaration,
        EnumDeclaration,
        ModuleDeclaration,
        ModuleBlock,
        CaseBlock,
        ImportEqualsDeclaration,
        ImportDeclaration,
        ImportClause,
        NamespaceImport,
        NamedImports,
        ImportSpecifier,
        ExportAssignment,
        ExportDeclaration,
        NamedExports,
        ExportSpecifier,
        MissingDeclaration,

        // Module references
        ExternalModuleReference,

        // Clauses
        CaseClause,
        DefaultClause,
        HeritageClause,
        CatchClause,

        // Property assignments
        PropertyAssignment,
        ShorthandPropertyAssignment,

        // Enum
        EnumMember,

        // Top-level nodes
        SourceFile,

        // JSDoc nodes.
        JsDocTypeExpression,

        // The * type.
        JsDocAllType = JsDocTypeExpression,

        // The ? type.
        JsDocUnknownType = JsDocTypeExpression,
        JsDocArrayType = JsDocTypeExpression,
        JsDocUnionType = JsDocTypeExpression,
        JsDocTupleType = JsDocTypeExpression,
        JsDocNullableType = JsDocTypeExpression,
        JsDocNonNullableType = JsDocTypeExpression,
        JsDocRecordType = JsDocTypeExpression,
        JsDocRecordMember = JsDocTypeExpression,
        JsDocTypeReference = JsDocTypeExpression,
        JsDocOptionalType = JsDocTypeExpression,
        JsDocFunctionType = JsDocTypeExpression,
        JsDocVariadicType = JsDocTypeExpression,
        JsDocConstructorType = JsDocTypeExpression,
        JsDocThisType = JsDocTypeExpression,
        JsDocComment = JsDocTypeExpression,
        JsDocTag = JsDocTypeExpression,
        JsDocParameterTag = JsDocTypeExpression,
        JsDocReturnTag = JsDocTypeExpression,
        JsDocTypeTag = JsDocTypeExpression,
        JsDocTemplateTag = JsDocTypeExpression,

        // Synthesized list
        SyntaxList,

        // Enum value count
        Count,

        // Markers
        FirstAssignment = EqualsToken,
        LastAssignment = CaretEqualsToken,
        FirstReservedWord = BreakKeyword,
        LastReservedWord = WithKeyword,
        FirstKeyword = BreakKeyword,
        LastKeyword = OfKeyword,
        FirstFutureReservedWord = ImplementsKeyword,
        LastFutureReservedWord = YieldKeyword,
        FirstTypeNode = TypePredicate,
        LastTypeNode = StringLiteralType,
        FirstPunctuation = OpenBraceToken,
        LastPunctuation = CaretEqualsToken,
        FirstToken = Unknown,
        LastToken = LastKeyword,
        FirstTriviaToken = SingleLineCommentTrivia,
        LastTriviaToken = ConflictMarkerTrivia,
        FirstLiteralToken = NumericLiteral,
        LastLiteralToken = NoSubstitutionTemplateLiteral,
        FirstTemplateToken = NoSubstitutionTemplateLiteral,
        LastTemplateToken = TemplateTail,
        FirstBinaryOperator = LessThanToken,
        LastBinaryOperator = CaretEqualsToken,
        FirstNode = QualifiedName,
        FirstNonCommentTriviaToken = NewLineTrivia,
        LastNonCommentTriviaToken = LastTriviaToken,
    }

    /// <summary>
    /// Flags for the node type.
    /// </summary>
    [Flags]
    public enum NodeFlags
    {
        None = 0,
        Export = 1 << 1,  // Declarations
        Ambient = 1 << 2,  // Declarations
        Public = 1 << 3,  // Property/Method
        Private = 1 << 4,  // Property/Method
        Protected = 1 << 5,  // Property/Method
        Static = 1 << 6,  // Property/Method

        Abstract = 1 << 7,  // Class/Method/ConstructSignature
        Async = 1 << 8,  // Property/Method/Function
        Default = 1 << 9,  // Function/Class (export default declaration)
        MultiLine = 1 << 10,  // Multi-line array or object literal
        Synthetic = 1 << 11,  // Synthetic node (for full fidelity)
        DeclarationFile = 1 << 12,  // Node is a .d.ts file
        Let = 1 << 13,  // Variable declaration
        Const = 1 << 14,  // Variable declaration
        OctalLiteral = 1 << 15,  // Octal numeric literal
        Namespace = 1 << 16,  // Namespace declaration
        ExportContext = 1 << 17,  // Export context (initialized by binding)
        ContainsThis = 1 << 18,  // Interface contains references to "this"
        HasImplicitReturn = 1 << 19,  // If function implicitly returns on one of codepaths (initialized by binding)
        HasExplicitReturn = 1 << 20,  // If function has explicit reachable return on one of codepaths (initialized by binding)
        Readonly = 1 << 21,
        ScriptPublic = 1 << 22,  // Declarations can be annotated with a @@public decorator to flag them as public to the module
        Modifier = Export | Ambient | Public | Private | Protected | Static | Abstract | Default | Async,
        AccessibilityModifier = Public | Private | Protected,
        BlockScoped = Let | Const,

        ReachabilityCheckFlags = HasImplicitReturn | HasExplicitReturn,
    }

    /// <summary>
    /// Flags that affects parsing.
    /// </summary>
    [Flags]
    public enum ParserContextFlags : byte
    {
        /// <nodoc />
        None = 0,

        /// <summary>
        /// If this node was parsed in a context where 'in-expressions' are not allowed.
        /// </summary>
        DisallowIn = 1 << 0,

        /// <summary>
        /// If this node was parsed in the 'yield' context created when parsing a generator.
        /// </summary>
        Yield = 1 << 1,

        /// <summary>
        /// If this node was parsed as part of a decorator
        /// </summary>
        Decorator = 1 << 2,

        /// <summary>
        /// If this node was parsed in the 'await' context created when parsing an async function.
        /// </summary>
        Await = 1 << 3,

        /// <summary>
        /// If the parser encountered an error when parsing the code that created this node.  Note
        /// the parser only sets this directly on the node it creates right after encountering the
        /// error.
        /// </summary>
        ThisNodeHasError = 1 << 4,

        // The following flag was changed from JavaScriptFile to DScriptInjectedNode.
        // DScript parser is not suitable for parsing JavaScript files, so this
        // bit of information can be freely reused for different purposes.

        /// <summary>
        /// DScript-specific. Nodes flagged with this are injected after parsing
        /// </summary>
        DScriptInjectedNode = 1 << 5,

        /// <summary>
        /// Combined set of DScript specific flags.
        /// </summary>
        DScriptSpecificFlags = DScriptInjectedNode,

        /// <summary>
        /// Context flags set directly by the parser.
        /// </summary>
        ParserGeneratedFlags = DisallowIn | Decorator | ThisNodeHasError,

        /// <summary>
        /// Exclude these flags when parsing a Type
        /// </summary>
        TypeExcludesFlags = Yield | Await,

        // Context flags computed by aggregating child flags upwards.

        /// <summary>
        /// Used during incremental parsing to determine if this node or any of its children had an
        /// error.  Computed only once and then cached.
        /// </summary>
        ThisNodeOrAnySubNodesHasError = 1 << 6,

        /// <summary>
        /// Used to know if we've computed data from children and cached it in this node.
        /// </summary>
        HasAggregatedChildData = 1 << 7,
    }

    /// <nodoc />
    public enum JsxFlags : byte
    {
        /// <nodoc />
        None = 0,

        /// <summary>
        /// An element from a named property of the JSX.IntrinsicElements interface
        /// </summary>
        IntrinsicNamedElement = 1 << 0,

        /// <summary>
        /// An element inferred from the string index signature of the JSX.IntrinsicElements interface
        /// </summary>
        IntrinsicIndexedElement = 1 << 1,

        /// <summary>
        /// An element backed by a class, class-like, or function value
        /// </summary>
        ValueElement = 1 << 2,

        /// <summary>
        /// Element resolution failed
        /// </summary>
        UnknownElement = 1 << 4,

        /// <nodoc />
        IntrinsicElement = IntrinsicNamedElement | IntrinsicIndexedElement,
    }

    internal enum RelationComparisonResult : byte
    {
        Succeeded = 1, // Should be truthy
        Failed = 2,
        FailedAndReported = 3,
    }

    /// <summary>
    /// Denotes what kind of literal expression was used: single-quoted, double-quoted or back-ticked.
    /// </summary>
    public enum LiteralExpressionKind : byte
    {
        /// <nodoc/>
        None,

        /// <nodoc/>
        SingleQuote,

        /// <nodoc/>
        DoubleQuote,

        /// <nodoc/>
        BackTick,
    }
}
