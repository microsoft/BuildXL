// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Constants;
using BuildXL.Tracing;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;

#pragma warning disable 1591 // Missing Xml comment
#pragma warning disable CA1823 // Unused field
#pragma warning disable SA1600 // Element must be documented

namespace BuildXL.FrontEnd.Script.Tracing
{
    /// <summary>
    /// Syntactic errors found by lint rules
    /// </summary>
    public abstract partial class Logger
    {
        private const string QualifierDeclarationExample =
            @"For example, 'export declare const qualifier: {{platform: 'x64'; configuration: 'debug' | 'release'}};'";

        private const string TemplateDeclarationExample =
            @"For example, 'export declare const template : T = {{library: {{enableStaticAnalysis: true}}}};'";

        [GeneratedEvent(
            (ushort)LogEventId.FunctionApplicationsWithoutConstBindingAreNotAllowedTopLevel,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Function applications without const/let bindings are not allowed as top level statements. The exceptions are '" + Names.ConfigurationFunctionCall +
            "' and '" + Names.ModuleConfigurationFunctionCall + "' in configuration and module files.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportFunctionApplicationsWithoutConstLetBindingAreNotAllowedTopLevel(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.OnlyConstBindingOnNamespaceLevel,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Top level declarations are constant. Use const binding to express this behavior.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportOnlyConstBindingOnNamespaceLevel(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.NoMutableDeclarationsAtTopLevel,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Top level declarations should not be of mutable types. Mutable data structures are allowed only as an implementation detail for pure functions.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportNoMutableDeclarationsAtTopLevel(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.NoMutableDeclarationsAtExposedFunctions,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Public functions should not return mutable types.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportNoMutableDeclarationsAtExposedFunctions(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.OnlyTypeAndFunctionDeclarationsAndConstBindingsAreAllowedTopLevel,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Only type, function declarations and const binding are allowed as top level statements, but expression of type '{type}' was found.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportOnlyTypeAndFunctionDeclarationsAndConstBindingsAreAllowedTopLevel(LoggingContext context, Location location, string type);

        [GeneratedEvent(
            (ushort)LogEventId.LabelsAreNotAllowed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Labels are not allowed in DScript.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportLabelsAreNotAllowed(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.VarDeclarationNotAllowed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "'var' declarations are not allowed in DScript. Use 'const' or 'let' instead.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportVarDeclarationNotAllowed(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.ThrowNotAllowed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "'throw' is not allowed in DScript.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportThrowNotAllowed(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.NullNotAllowed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "'null' is not allowed in DScript.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportNullNotAllowed(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.EvalIsNotAllowed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "The function 'eval' is not available in DScript.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportEvalIsNotAllowed(LoggingContext context, Location location);

        /// <summary>
        /// Generic typescript syntax error. All TS parser errors are routed here
        /// </summary>
        [GeneratedEvent(
            (ushort)LogEventId.TypeScriptSyntaxError,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "{message}",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportTypeScriptSyntaxError(LoggingContext context, Location location, string message);

        /// <summary>
        /// Generic typescript binding error.
        /// </summary>
        [GeneratedEvent(
            (ushort)LogEventId.TypeScriptBindingError,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "{message}",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportTypeScriptBindingError(LoggingContext context, Location location, string message);

        /// <summary>
        /// This one should go away once we implement all constructs in the parser
        /// </summary>
        [GeneratedEvent(
            (ushort)LogEventId.TypeScriptFeatureIsNotSupported,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "{message}",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportTypeScriptFeatureIsNotSupported(LoggingContext context, Location location, string message);

        [GeneratedEvent(
            (ushort)LogEventId.FailReadFileContent,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Failed to read the content of '{fileToRead}': {details}",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportFailReadFileContent(LoggingContext context, Location location, string fileToRead, string details);

        [GeneratedEvent(
            (ushort)LogEventId.FailedToPersistPublicFacadeOrEvaluationAst,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Failed to serialize persist public facade or evaluation ast: {error}",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportFailedToPersistPublicFacadeOrEvaluationAst(LoggingContext context, Location location, string error);

        [GeneratedEvent(
            (ushort)LogEventId.ImportStarIsObsolete,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Syntax is obsolete. Use 'import * as ...' instead.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportImportStarIsObsolete(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.MemberIsObsolete,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Member '{member}' is obsolete.{message}",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportMemberIsObsolete(LoggingContext context, Location location, string member, string message);

        [GeneratedEvent(
            (ushort)LogEventId.ImportStarIsNotSupportedWithSemanticResolution,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "'import * as' syntax is not supported in DScript V2.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportImportStarIsNotSupportedWithSemanticResolution(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.NotSupportedReadonlyModifier,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Declarations are implicitly readonly in DScript. Please remove 'readonly' modifier from the declaration.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportNotSupportedReadonlyModifier(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.QualifierSpaceValueMustBeStringLiteral,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Qualifier type value must be a string literal.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportQualifierSpaceValueMustBeStringLiteral(LoggingContext context, Location location);
        
        [GeneratedEvent(
            (ushort)LogEventId.QualifierSpaceValueMustBeValidValue,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Qualifier type  must have a valid value. '{value}' is not. ';' and '=' are not allowed.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportQualifierSpaceValueMustBeValidValue(LoggingContext context, Location location, string value);

        [GeneratedEvent(
            (ushort)LogEventId.QualifierSpaceValueMustBeValidKey,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Qualifier type must have a valid qualifier key. '{key}' is not. ';' and '=' are not allowed.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportQualifierSpaceValueMustBeValidKey(LoggingContext context, Location location, string key);

        [GeneratedEvent(
            (ushort)LogEventId.QualifierSpacePropertyCannotBeInShorthand,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Qualifier type property cannot be a shorthand property assignment.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportQualifierSpacePropertyCannotBeInShorthand(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.QualifierSpacePossibleValuesMustBeNonEmptyArrayLiteral,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Qualifier type must be a non-empty array literal.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportQualifierSpacePossibleValuesMustBeNonEmptyArrayLiteral(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.NotSupportedInterpolation,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Interpolation function '{interpolationFunction} is not allowed.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportNotSupportedInterpolation(LoggingContext context, Location location, string interpolationFunction);

        [GeneratedEvent(
            (ushort)LogEventId.NotSupportedSymbolKeyword,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Symbol type is not allowed in DScript.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportNotSupportedSymbolKeyword(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.NotSupportedDefaultArguments,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Default arguments are not allowed in DScript.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportNotSupportedDefaultArguments(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.NotSupportedMethodDeclarationInEnumMember,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Method declaration '{member}' is not allowed in object literals.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportNotSupportedMethodDeclarationInEnumMember(LoggingContext context, Location location, string member);

        [GeneratedEvent(
            (ushort)LogEventId.NotSupportedClassDeclaration,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "class declarations are not allowed in DScript.",
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void ReportNotSupportedClassDeclaration(LoggingContext context, Location location);

        [GeneratedEvent(
            (int)LogEventId.NotSupportedClassExpression,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "class expressions are not allowed in DScript.",
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void ReportNotSupportedClassExpression(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.NotSupportedNewExpression,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "'new' expressions are not allowed in DScript.",
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void ReportNotSupportedNewExpression(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.ImportModuleSpecifierIsNotAStringLiteral,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "The 'from' clause of an import statement can only be a string literal, but '{moduleSpecifier}' was found.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportImportModuleSpecifierIsNotAStringLiteral(LoggingContext context, Location location, string moduleSpecifier);

        [GeneratedEvent(
            (ushort)LogEventId.ImportFromNotPassedAStringLiteral,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Method 'importFrom' can only be passed a string literal, but '{moduleSpecifier}' was found.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportImportFromNotPassedAStringLiteral(LoggingContext context, Location location, string moduleSpecifier);

        [GeneratedEvent(
            (ushort)LogEventId.ImportFileNotPassedAFileLiteral,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Method 'importFile' can only be passed a file literal, but '{moduleSpecifier}' was found.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportImportFileNotPassedAFileLiteral(LoggingContext context, Location location, string moduleSpecifier);

        [GeneratedEvent(
            (ushort)LogEventId.ForIncrementorMustBeAssignmentOrPostfixIncrementOrDecrement,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "For-incrementor expression must be an assignment expression of the form 'identifier = expression', 'identifier += expression', 'identifier -= expression', or a postfix increment 'identifer++' or postfix decrement 'identifier--'.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportForIncrementorMustBeAssignmentOrPostfixIncrementOrDecrement(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.VariableMustBeInitialized,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Variable declaration '{name}' must have an initializer.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportVariableMustBeInitialized(LoggingContext context, Location location, string name);

        [GeneratedEvent(
            (ushort)LogEventId.InvalidForVarDeclarationInitializer,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Variable declaration for for-intializer must be of the form 'let identifier = expression'.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportInvalidForVarDeclarationInitializer(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.InvalidForOfVarDeclarationInitializer,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Variable declaration for for-of-initializer must be of the form 'let identifier'.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportInvalidForOfVarDeclarationInitializer(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.NotSupportedForInLoops,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "for-in loops are not allowed in DScript. Use for-of loops instead.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportForInLoopsNotSupported(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.NotSupportedNonConstEnums,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Non-constant enums are not allowed. Add 'const' modifier to enum declaration.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportNotSupportedNonConstEnums(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.NotSupportedFloatingPoints,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Floating point numbers are not allowed in DScript.",
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void ReportNotSupportedFloatingPoints(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.ReportLiteralOverflows,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Literal '{literal}' is outside the range of 'number'. DScript does not support the numeric value 'Infinity'.",
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void ReportLiteralOverflows(LoggingContext context, Location location, string literal);

        [GeneratedEvent(
            (ushort)LogEventId.OnlyExtendsClauseIsAllowedInHeritageClause,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Keyword 'extends' is expected.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportOnlyExtendsClauseIsAllowedInHeritageClause(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.InterfacesOnlyExtendedByIdentifiers,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Unexpected expression '{expression}'. An interface can only be extended by an identifier with optional type parameters.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportInterfacesOnlyExtendedByIdentifiers(LoggingContext context, Location location, string expression);

        [GeneratedEvent(
            (ushort)LogEventId.NotSupportedNonStrictEquality,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Non-strict equality ('==') is not allowed. Use strict equality ('===') instead.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportNotSupportedNonStrictEquality(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.NotSupportedModifiersOnImport,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Modifier '{modifier}' is not allowed on an import declaration.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportNotSupportedModifiersOnImport(LoggingContext context, Location location, string modifier);

        [GeneratedEvent(
            (ushort)LogEventId.NotSupportedExportImport,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "'export import' is obsolete. Please convert this into two separate statements: an import and an export declaration.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportNotSupportedExportImport(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.ExportedDeclarationInsideANonExportedNamespace,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "A declaration is being exported, but its containing namespace is not. Maybe you forgot to export the namespace?",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportExportedDeclarationInsideANonExportedNamespace(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.ExportsAreNotAllowedInsideNamespaces,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Export declarations are not allowed in a namespace.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportExportsAreNotAllowedInsideNamespaces(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.DefaultImportsNotAllowed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Default import/export is not allowed in DScript. Use `import {{name}} from spec` syntax instead.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportDefaultImportsNotAllowed(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.NotSupportedCustomAmbientFunctions,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Custom ambient declarations are not allowed in DScript.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportNotSupportedCustomAmbientFunctions(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.DivisionOperatorIsNotSupported,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Division operator is not allowed. Maybe you tried to create a path? Use an explicit path operator in that case. E.g. p`a/b/c`.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportDivisionOperatorIsNotSupported(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.QualifierDeclarationShouldBeAloneInTheStatement,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Qualifier declaration should be the only one in the statement. " + QualifierDeclarationExample,
            Keywords = (int)Keywords.UserMessage)]
        public abstract void QualifierDeclarationShouldBeAloneInTheStatement(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.TemplateDeclarationShouldBeAloneInTheStatement,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Template declaration should be the only one in the statement. " + TemplateDeclarationExample,
            Keywords = (int)Keywords.UserMessage)]
        public abstract void TemplateDeclarationShouldBeAloneInTheStatement(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.QualifierDeclarationShouldBeConstExportAmbient,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Qualifier declaration does not contain all required modifiers. " + QualifierDeclarationExample,
            Keywords = (int)Keywords.UserMessage)]
        public abstract void QualifierDeclarationShouldBeConstExportAmbient(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.TemplateDeclarationShouldBeConstExportAmbient,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Template declaration does not contain all required modifiers. " + TemplateDeclarationExample,
            Keywords = (int)Keywords.UserMessage)]
        public abstract void TemplateDeclarationShouldBeConstExportAmbient(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.TemplateDeclarationShouldBeTheFirstStatement,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Template declaration should be the first statement in the block.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void TemplateDeclarationShouldBeTheFirstStatement(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.QualifierTypeShouldBePresent,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Qualifier declaration should include a type declaration. " + QualifierDeclarationExample,
            Keywords = (int)Keywords.UserMessage)]
        public abstract void QualifierTypeShouldBePresent(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.QualifierLiteralMemberShouldBeAnIdentifier,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Qualifier key should be an identifier. " + QualifierDeclarationExample,
            Keywords = (int)Keywords.UserMessage)]
        public abstract void QualifierLiteralMemberShouldBeAnIdentifier(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.QualifierLiteralTypeMemberShouldHaveStringLiteralType,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Qualifier type member should be a string literal, or a union of string literals. " + QualifierDeclarationExample,
            Keywords = (int)Keywords.UserMessage)]
        public abstract void QualifierLiteralTypeMemberShouldHaveStringLiteralType(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.QualifierTypeShouldBeAnInterfaceOrTypeLiteral,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Qualifier type should reference an interface or be an inline type literal. " + QualifierDeclarationExample,
            Keywords = (int)Keywords.UserMessage)]
        public abstract void QualifierTypeShouldBeAnInterfaceOrTypeLiteral(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.QualifierInterfaceTypeShouldBeOrInheritFromQualifier,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Qualifier type should reference '{wellKnownQualifierType}' type or reference an interface that directly inherits from it.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void QualifierInterfaceTypeShouldBeOrInheritFromQualifier(LoggingContext context, Location location, string wellKnownQualifierType);

        [GeneratedEvent(
            (ushort)LogEventId.QualifierOptionalMembersAreNotAllowed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Qualifier type members should not be optional.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void QualifierOptionalMembersAreNotAllowed(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.QualifierTypeNameIsReserved,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "The type name '{wellKnownQualifierType}' is reserved.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportQualifierTypeNameIsReserved(LoggingContext context, Location location, string wellKnownQualifierType);

        [GeneratedEvent(
            (ushort)LogEventId.CurrentQualifierCannotBeAccessedWithQualifications,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "The current qualifier can only be referenced as '{currentQualifier}'. Namespace qualifications are not allowed.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportCurrentQualifierCannotBeAccessedWithQualifications(LoggingContext context, Location location, string currentQualifier);

        [GeneratedEvent(
            (ushort)LogEventId.ExpressionExpected,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Expression expected.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportExpressionExpected(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.QualifierNameCanOnlyBeUsedInVariableDeclarations,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "'{currentQualifier}' is a reserved keyword that can only be used in variable declarations.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportQualifierNameCanOnlyBeUsedInVariableDeclarations(LoggingContext context, Location location, string currentQualifier);

        [GeneratedEvent(
            (ushort)LogEventId.TemplateNameCanOnlyBeUsedInVariableDeclarations,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "'{template}' is a reserved keyword that can only be used in variable declarations.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportTemplateNameCanOnlyBeUsedInVariableDeclarations(LoggingContext context, Location location, string template);

        [GeneratedEvent(
            (ushort)LogEventId.QualifierDeclarationShouldBeTopLevel,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Qualifier declarations can only occur directly under a namespace or at the top level of a file.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void QualifierDeclarationShouldBeTopLevel(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.TemplateDeclarationShouldBeTopLevel,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Template declarations can only occur directly under a namespace or at the top level of a file.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void TemplateDeclarationShouldBeTopLevel(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.ProjectLikeImportOrExportNotAllowedInModuleWithImplicitSemantics,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Path-based specifier '{specifier}' is not allowed in a spec that belongs to a module with implicit reference semantics. Make sure the value you want to reference is exported and reference it directly.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportProjectLikeImportOrExportNotAllowedInModuleWithImplicitSemantics(LoggingContext context, Location location, string specifier);

        [GeneratedEvent(
            (ushort)LogEventId.TemplateDeclarationShouldHaveInitializer,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "An initializer must be present in template declarations.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void TemplateDeclarationShouldHaveInitializer(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.TemplateDeclarationShouldHaveAType,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Template declaration should include a type declaration.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void TemplateDeclarationShouldHaveAType(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.TemplateDeclarationShouldNotHaveAnyType,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Template type cannot be 'any'.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void TemplateDeclarationShouldNotHaveAnyType(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.RootNamespaceIsAKeyword,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "'{rootNamespaceName}' is a reserved keyword.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportRootNamespaceIsAKeyword(LoggingContext context, Location location, string rootNamespaceName);

        [GeneratedEvent(
            (ushort)LogEventId.NamedImportInConfigOrPackage,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Found package name '{packageName}' in '{functionName}' call. Only relative paths (e.g. './Foo.dsc' or '/Bar.dsc') are allowed in '{functionName}' calls.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportNamedImportInConfigOrPackage(LoggingContext context, Location location, string functionName, string packageName);

        [GeneratedEvent(
            (ushort)LogEventId.NamedImportInConfigOrPackageLikePath,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Found package name '{packageName}' in '{functionName}' call, which resembles a file path. Only relative and rooted paths are allowed, which must start with './', '/', or a drive.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportNamedImportInConfigOrPackageLikePath(LoggingContext context, Location location, string functionName, string packageName);

        [GeneratedEvent(
            (ushort)LogEventId.NamedImportOfConfigPackageModule,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Found config, package, or module file '{packageName}' in '{functionName}' call. These file types cannot expose anything.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportNamedImportOfConfigPackageModule(LoggingContext context, Location location, string functionName, string packageName);

        [GeneratedEvent(
            (ushort)LogEventId.ImportFileInSpec,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Found 'importFile' call in a spec file. Use 'importFrom' instead.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportImportFileInSpec(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.ImportFromV2Package,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Found non-module name '{packageName}' in 'importFrom' call in a spec file. 'importFrom' may only be passed module names (e.g. 'MyModule', not './path/to/spec') in projects within implicit semantics modules.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportImportFromV2Package(LoggingContext context, Location location, string packageName);

        [GeneratedEvent(
            (ushort)LogEventId.AmbientAccessInConfig,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Ambient '{lhsName}.{rhsName}' cannot be used in a config file.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportAmbientAccessInConfig(LoggingContext context, Location location, string lhsName, string rhsName);

        [GeneratedEvent(
            (ushort)LogEventId.ModuleShouldNotImportItself,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Module '{moduleName}' should not import itself.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportModuleShouldNotImportItself(LoggingContext context, Location location, string moduleName);
    }
}
