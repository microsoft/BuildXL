// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.FrontEnd.Script
{
    /// <summary>
    /// SyntaxKind enum for efficient AST type checking.
    /// </summary>
    public enum SyntaxKind : ushort
    {
        // Okay for this enum to not have a XmlDocComment for every value.
#pragma warning disable 1591

        None = 0,

        SourceFile,

        // Misc.
        EnumMember,
        Parameter,
        TypeParameter,

        // Expressions.
        ApplyExpression,
        ApplyExpressionWithTypeArguments,
        ArrayExpression,
        BinaryExpression,
        ImportExpression, // never used

        // Symbol reference expressions.
        FullNameBasedSymbolReference,
        ModuleReferenceExpression,
        NameBasedSymbolReference,
        LocalReferenceExpression,
        LocationBasedSymbolReference,
        QualifiedLocationBasedSymbolReference, // never used
        ModuleIdExpression,

        // Qualifier-related expressions
        WithQualifierExpression,
        QualifierReferenceExpression,
        CoerceQualifierTypeExpression,

        IndexExpression,
        IteExpression,
        LambdaExpression,
        ObjectExpression, // Obsolete and never used any more.
        SelectorExpression,
        ResolvedSelectorExpression,
        ModuleSelectorExpression,
        UnaryExpression,
        PropertyAssignment,
        AssignmentExpression,
        IncrementDecrementExpression,
        CastExpression,

        ImportAliasExpression,
        ModuleToObjectLiteral,

        // Literals.
        PathLiteral,
        FileLiteral,
        FileLiteralExpression,
        StringLiteralExpression,
        DirectoryLiteral,
        PathAtomLiteral,
        RelativePathLiteral,
        StringLiteral,
        BoolLiteral,
        NumberLiteral,
        UndefinedLiteral,
        ResolvedStringLiteral,

        // Declarations.
        EnumDeclaration,
        EnumMemberDeclaration,
        ExportAssignment, // Obsolete and never used.
        FunctionDeclaration,
        ConfigurationDeclaration,
        PackageDeclaration,
        QualifierSpaceDeclaration,
        ImportDeclaration,
        ExportDeclaration,
        ImportEqualsDeclaration, // Obsolete and never used any more.
        InterfaceDeclaration,
        ModuleDeclaration,
        VarDeclaration,
        NamespaceImport, // Obsolete and never used any more.
        NamespaceAsVarImport,
        ImportOrExportModuleSpecifier,
        ImportOrExportVarSpecifier,
        NamedImportsOrExports,
        ImportOrExportClause,
        TypeAliasDeclaration,

        // Statements.
        BlockStatement,
        BreakStatement,
        ContinueStatement,
        CaseClause,
        DefaultClause,
        ExpressionStatement,
        IfStatement,
        ReturnStatement,
        SwitchStatement,
        VarStatement,
        ForStatement,
        ForOfStatement,
        WhileStatement,

        // Types.
        ArrayType,
        FunctionType,
        NamedTypeReference,
        ObjectType,
        PredefinedType,
        TupleType,
        UnionType,
        TypeQuery,

        // Signatures.
        PropertySignature,
        CallSignature,

        // Values and internal expressions.
        FileModuleLiteral,
        ResolvedFileModuleLiteral,
        TypeOrNamespaceModuleLiteral,
        GlobalModuleLiteral,

        ObjectLiteral0,
        ObjectLiteralN,
        ObjectLiteralSlim,
        ObjectLiteralOverride, // Never used!
        ArrayLiteral,
        Closure,
        Function0,
        Function1,
        Function2,
        FunctionN,
        BoundFunction,
        ImportValue, // obsolete and never used any more.

        MergeModuleValue, // Obsolete and never used.

        ArrayLiteralWithSpreads,
        InterpolatedPaths,

#pragma warning restore 1591
    }
}
