// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.FrontEnd.Script.Tracing
{
    // disable warning regarding 'missing XML comments on public API'. We don't need docs for these values
#pragma warning disable 1591

    /// <summary>
    /// Defines event IDs corresponding to events in <see cref="Logger" />
    /// <remarks>
    /// We have three main sections for errors
    /// 1) Syntactic errors:
    ///    a) Errors that come from the ported typescript parser. Error code is computed as original error code + a base number
    ///    b) Errors that are found by mandatory lint rules
    ///    c) Errors that are found by policy lint rules
    /// 2) AstConverter errors. Semantics errors found during conversion (e.g. double declaration)
    /// 3) Evaluation phase errors
    ///
    /// Each section, and subsection, have its own range
    ///
    /// Assembly reserved range 9000 - 9899
    /// </remarks>
    /// </summary>
    public enum LogEventId : ushort
    {
        None = 0,

        // Moved from Core. Used only for legacy parser
        // 2934 FREE,

        ScriptDebugLog = 7500,
        ContractFail = 7501,
        ContractWarn = 7502,
        ContractRequire = 7503,
        ContractAssert = 7504,
        QualifierSpaceValueMustBeValidValue = 7505,
        QualifierSpaceValueMustBeValidKey = 7506,
        // 1. Syntactic errors

        // 1.a. All errors that come from the typescript parser start at 100001. They currently don't go over 10k, so leaving 50k as a buffer.
        TypeScriptSyntaxError = 9000,
        TypeScriptBindingError = 9001,
        FailReadFileContent = 9002,
        FailedToPersistPublicFacadeOrEvaluationAst = 9003,

        // 1.b. Mandatory lint rules
        OnlyExtendsClauseIsAllowedInHeritageClause = 9004,
        InterfacesOnlyExtendedByIdentifiers = 9005,
        NotSupportedNonConstEnums = 9006,
        NotSupportedDefaultArguments = 9007,
        NotSupportedSymbolKeyword = 9008,
        NotSupportedMethodDeclarationInEnumMember = 9009,
        NotSupportedInterpolation = 9010, // Unknown interpolation function like foo`${var`};
        NotSupportedReadonlyModifier = 9011,
        NotSupportedForInLoops = 9012,
        NotSupportedFloatingPoints = 9013, // Floating points like 1.2 are not supported in DScript.

        NotSupportedClassDeclaration = 9014, // classes are not supported in DScript
        NotSupportedClassExpression = 9015, // class expressions are not supported in DScript
        NotSupportedNewExpression = 9016, // new expressions are not supported in DScript
        MissingSemicolon = 9017,
        ForIncrementorMustBeAssignmentOrPostfixIncrementOrDecrement = 9018,
        UnusedFreeAvailable = 9019,
        VariableMustBeInitialized = 9020,
        InvalidForOfVarDeclarationInitializer = 9021,
        InvalidForVarDeclarationInitializer = 9022,
        // Reserved 9023,
        QualifierSpaceValueMustBeStringLiteral = 9024,
        QualifierSpacePropertyCannotBeInShorthand = 9025,
        QualifierSpacePossibleValuesMustBeNonEmptyArrayLiteral = 9026,
        // Reserved 9027,
        // Reserved 9028,
        // Reserved 9029,

        TypeScriptFeatureIsNotSupported = 9030,
        ImportStarIsObsolete = 9031,
        EvalIsNotAllowed = 9032,
        NullNotAllowed = 9033,
        VarDeclarationNotAllowed = 9034,
        LabelsAreNotAllowed = 9035,
        OnlyTypeAndFunctionDeclarationsAndConstBindingsAreAllowedTopLevel = 9036,

        // can't call function without assigning a result to the variable on the top level.
        FunctionApplicationsWithoutConstBindingAreNotAllowedTopLevel = 9037,

        // On namespace level only const declarations are allowed
        OnlyConstBindingOnNamespaceLevel = 9038,

        // Reserved = 9039,
        // Reserved = 9040,

        NotSupportedNonStrictEquality = 9041,
        NotSupportedModifiersOnImport = 9042,
        NotSupportedExportImport = 9043,
        ExportedDeclarationInsideANonExportedNamespace = 9044,
        ExportsAreNotAllowedInsideNamespaces = 9045,
        NotUsedEnumIsNotExported = 9046,

        // import x from './spec.dsc';
        DefaultImportsNotAllowed = 9047,

        // declare function foo(); is not allowed because we'll definitely get undefined dereference once we'll try to use it.
        NotSupportedCustomAmbientFunctions = 9048,

        QualifierDeclarationShouldBeAloneInTheStatement = 9049,
        QualifierDeclarationShouldBeConstExportAmbient = 9050,
        QualifierTypeShouldBePresent = 9051,
        QualifierLiteralMemberShouldBeAnIdentifier = 9052,
        QualifierLiteralTypeMemberShouldHaveStringLiteralType = 9053,
        QualifierTypeShouldBeAnInterfaceOrTypeLiteral = 9054,
        QualifierInterfaceTypeShouldBeOrInheritFromQualifier = 9055,
        QualifierOptionalMembersAreNotAllowed = 9056,
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1700:DoNotNameEnumValuesReserved")]
        QualifierTypeNameIsReserved = 9057,
        CurrentQualifierCannotBeAccessedWithQualifications = 9058,
        QualifierNameCanOnlyBeUsedInVariableDeclarations = 9059,
        QualifierDeclarationShouldBeTopLevel = 9060,
        ImportStarIsNotSupportedWithSemanticResolution = 9061,
        // Deprecated 9062,
        ProjectLikeImportOrExportNotAllowedInModuleWithImplicitSemantics = 9063,
        TemplateNameCanOnlyBeUsedInVariableDeclarations = 9064,
        TemplateDeclarationShouldBeAloneInTheStatement = 9065,
        TemplateDeclarationShouldBeConstExportAmbient = 9066,
        TemplateDeclarationShouldBeTopLevel = 9067,
        TemplateDeclarationShouldHaveInitializer = 9068,
        TemplateDeclarationShouldHaveAType = 9069,
        TemplateDeclarationShouldNotHaveAnyType = 9070,
        RootNamespaceIsAKeyword = 9071,
        TemplateDeclarationShouldBeTheFirstStatement = 9072,

        NoMutableDeclarationsAtTopLevel = 9073,
        NoMutableDeclarationsAtExposedFunctions = 9074,
        NamedImportInConfigOrPackage = 9075,
        NamedImportOfConfigPackageModule = 9076,
        ImportFileInSpec = 9077,
        ImportFromV2Package = 9078,
        ImportFromConfig = 9079,
        ImportFilePassedNonFile = 9080,
        ImportModuleSpecifierIsNotAStringLiteral = 9081,
        ImportFromNotPassedAStringLiteral = 9082,
        ImportFileNotPassedAFileLiteral = 9083,
        FileLikePackageName = 9084,
        NamedImportInConfigOrPackageLikePath = 9085,
        AmbientAccessInConfig = 9086,
        ModuleShouldNotImportItself = 9087,
        // Deprecated 9088,
        // Deprecated 9089,
        ReportLiteralOverflows = 9090,

        // 1.c. Policy lint rules (report methods in LogPolicySyntacticEvents.cs)
        GlobFunctionsAreNotAllowed = 9100,
        MissingTypeAnnotationOnTopLevelDeclaration = 9101,
        NotAllowedTypeAnyOnTopLevelDeclaration = 9102,
        MissingPolicies = 9103,
        FunctionShouldDeclareReturnType = 9104,
        AmbientTransformerIsDisallowed = 9105,

        // 2. AstConverter errors (report methods in LogAstConverterEvents.cs)
        // Deprecated 9200,
        DuplicateBinding = 9201,

        // Empty
        ConfigurationDeclarationIsOnlyInConfigurationFile = 9202,
        PackageConfigurationDeclarationIsOnlyInConfigurationFile = 9203,
        InvalidEnumMember = 9204,
        LocalFunctionsAreNotSupported = 9205,
        // Deprecated 9206,
        InvalidPathInterpolationExpression = 9207,
        LeftHandSideOfAssignmentMustBeLocalVariable = 9208,
        InvalidPathExpression = 9209,
        InvalidRelativePathExpression = 9210,
        InvalidPathAtomExpression = 9211,
        OnlyASingleConfigurationDeclarationInConfigurationFile = 9212,
        UnknownFunctionCallInConfigurationFile = 9213,
        InvalidConfigurationFileFormat = 9214,
        // Deprecated 9215
        // Free 9216
        ProjectPathIsInvalid = 9217, // Like import * as Foo from '.d.dh1```';
        // Deprecated 9218,
        ProjectPathComputationFailed = 9219, // When absolute path computation for the file fails
        // Deprecated 9220,
        // Deprecated 9221,
        InvalidPackageConfigurationFileFormat = 9223,
        UnknownFunctionCallInPackageConfigurationFile = 9224,
        AtLeastSinglePackageConfigurationDeclarationInPackageConfigurationFile = 9225,

        // Empty slot. There was another warning that was removed from the system.
        NameCannotBeFound = 9226,
        BlockScopedVariableUsedBeforeDeclaration = 9227,
        OuterVariableCapturingForMutationIsNotSupported = 9228, // DScript prohibits capturing of the enclosing locals for mutation!
        DivisionOperatorIsNotSupported = 9229,
        ExpressionExpected = 9230,

        LeftHandSideOfAssignmentExpressionCannotBeAConstant = 9231,
        TheOperandOfAnIncrementOrDecrementOperatorCannotBeAConstant = 9232,
        OperandOfIncrementOrDecrementOperatorMustBeLocalVariable = 9233,

        ConfigurationParsingFailed = 9234,
        PackageConfigurationParsingFailed = 9235,
        ModuleSpecifierContainsInvalidCharacters = 9236,
        ImportAliasIsNotReferencedAndWillBeRemoved = 9237,
        // Deprecated 9238,
        WarnForDeprecatedV1Modules = 9239,

        // 3. Evaluation phase errors
        SourceResolverFailEvaluateUnregisteredFileModule = 9300,
        SourceResolverConfigurationIsNotObjectLiteral = 9301,
        SourceResolverPackageFilesDoNotExist = 9302,
        // Deprecated 9303,
        SourceResolverRootDirForPackagesDoesNotExist = 9304,
        SourceResolverPackageFileNotWithinConfiguration = 9305,
        // Deprecated 9306,
        // Deprecated 9307,
        // Deprecated 9308,
        PackageDescriptorIsNotObjectLiteral = 9309,
        PackageDescriptorsIsNotArrayLiteral = 9310,
        PackageMainFileIsNotInTheSameDirectoryAsPackageConfiguration = 9311,
        // Deprecated 9312,
        FailAddingPackageDueToPackageOwningAllProjectsExists = 9313,
        FailAddingPackageBecauseItWantsToOwnAllProjectsButSomeAreOwnedByOtherPackages = 9314,
        FailAddingPackageBecauseItsProjectIsOwnedByAnotherPackage = 9315,
        ConversionException = 9316,
        UnexpectedResolverException = 9317,
        MissingField = 9318,
        MissingTypeChecker = 9319,
        UnableToEnumerateFilesOnCollectingPackagesAndProjects = 9320,
        UnableToEnumerateDirectoriesOnCollectingPackagesAndProjects = 9321,
        InvalidPackageNameDueToUsingConfigPackage = 9322,
        DebugDumpCallStack = 9323,

        ApplyAmbientNumberOfArgumentsLessThanMinArity = 9324,
        UnexpectedValueType = 9325,
        UnexpectedValueTypeForName = 9326,
        UnexpectedValueTypeOnConversion = 9327,
        ArrayIndexOufOfRange = 9328,
        StringIndexOufOfRange = 9329,
        ArgumentIndexOutOfBound = 9330,
        // Deprecated 9331,
        ResolveImportDuplicateBinding = 9332,
        ResolveImportDuplicateNamespaceBinding = 9333,
        // Deprecated 9334,
        // Deprecated 9335,
        ResolveImportFailPrependedName = 9336,
        // Deprecated 9337,
        FailResolveSelectorDueToUndefined = 9338,
        // Deprecated 9339,
        FailResolveModuleSelector = 9340,
        AmbiguousResolveSelectorDueToMultipleImports = 9341, // Not used any more
        MissingNamespaceMember = 9342,
        MissingInstanceMember = 9343,
        MissingNamespace = 9344,
        MemberIsObsolete = 9345, // Not used any more
        PropertyIsObsolete = 9346, // Not used any more
        FunctionIsObsolete = 9347, // Not used any more
        DirectoryOperationError = 9348,
        FileOperationError = 9349,
        SpreadIsNotAppliedToArrayValue = 9350,
        UnexpectedAmbientException = 9351,
        DivideByZero = 9352,
        StackOverflow = 9353,
        InvalidPathAtom = 9354,
        // Deprecated 9355,
        InvalidTypeFormat = 9356,
        InputValidationError = 9357,
        UndefinedMapKey = 9358,
        InvalidKeyValueMap = 9359,
        UndefinedSetItem = 9360,
        QualifierMustEvaluateToObjectLiteral = 9361,
        QualifierValueMustEvaluateToString = 9362,
        QualifierCannotBeCoarcedToQualifierSpace = 9363,
        QualifierCannotBeCoarcedToQualifierSpaceWithProvenance = 9364,
        GetMountNameNullOrEmpty = 9365,
        GetMountNameNotFound = 9366,
        GetMountNameCaseMisMatch = 9367,
        GetMountFailDueToUninitializedEngine = 9368,

        ThrowNotAllowed = 9369,
        // Deprecated 9370,

        // let x: number = 11111111111111111111111111111; or let x: number = 1 << 111111111;
        IntegralConstantIsTooLarge = 9371,

        // let x: number = 55555555 * 5555555;
        ArithmeticOverflow = 9372,

        // +"notanumber"
        InvalidFormatForStringToNumberConversion = 9373,

        // x ** -1 is forbidden because DScript doesn't have floating point mumbers.
        ArgumentForPowerOperationShouldNotBeNegative = 9374,

        // Deprecated 9375,
        // Deprecated 9376,

        FileNotFoundInStaticDirectory = 9377,

        Cycle = 9378,
        ForLoopOverflow = 9379,
        WhileLoopOverflow = 9380,
        // Deprecated 9381,

        ImplicitSemanticsDoesNotAdmitMainFile = 9382,
        // Deprecated 9383,

        KeyFormDllNotFound = 9384,
        KeyFormDllWrongFileName = 9385,
        KeyFormDllLoad = 9386,
        KeyFormDllLoadedWithDifferentDll = 9387,
        ReportKeyFormNativeFailure = 9388,

        // Deprecated 9389,
        NoBuildLogicInProjects = 9390,
        NoExportedLambdasInProjects = 9391,

        CannotUsePackagesAndModulesSimultaneously = 9392,
        EvaluationCancellationRequestedAfterFirstFailure = 9393,

        TemplateInContextNotAvailable = 9394,

        ExplicitSemanticsDoesNotAdmitAllowedModuleDependencies = 9395,
        ExplicitSemanticsDoesNotAdmitCyclicalFriendModules = 9396,
        CyclicalFriendModulesNotEnabledByPolicy = 9397,
        DuplicateAllowedModuleDependencies = 9398,
        DuplicateCyclicalFriendModules = 9399,

        // 4. Statistics
        ArrayEvaluationStatistics = 9400,
        /*Was ThunkEvaluationStatistics. Now: reserved.*/

        GlobStatistics = 9401,
        ContextStatistics = 9402,
        MethodInvocationCountStatistics = 9403,
        PropertyAccessOnValueWithTypeAny = 9404,
        InvalidRadix = 9405,
        InvalidPathOperation = 9406,
        EvaluationCanceled = 9407,
        JsonUnsuportedTypeForSerialization = 9408,
        ReportJsonUnsuportedDynamicFieldsForSerialization = 9409,
        ReportXmlInvalidStructure = 9410,
        ReportXmlReadError = 9411,
        ReportXmlUnsuportedTypeForSerialization = 9412,
        ReportUnsupportedTypeValueObjectException = 9413,
        DirectoryNotSupportedException = 9414,
        // Obsolete syntax rules (starting from 9500)

        // Don't go beyond 9899
    }
}
