// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Constants;
using BuildXL.Tracing;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;

#pragma warning disable 1591
#pragma warning disable SA1600 // Element must be documented

namespace BuildXL.FrontEnd.Script.Tracing
{
    /// <summary>
    /// Events triggered by AstConverter
    /// </summary>
    public abstract partial class Logger
    {
        [GeneratedEvent(
            (ushort)LogEventId.ConfigurationParsingFailed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Failed to parse configuration: {message}",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportConfigurationParsingFailed(LoggingContext context, Location location, string message);

        [GeneratedEvent(
            (ushort)LogEventId.PackageConfigurationParsingFailed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Failed to parse module configuration file: {message}",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportPackageConfigurationParsingFailed(LoggingContext context, Location location, string message);

        [GeneratedEvent(
            (ushort)LogEventId.InvalidPackageConfigurationFileFormat,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Module configuration file is invalid. {message}.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportInvalidPackageConfigurationFileFormat(LoggingContext context, Location location, string message);

        [GeneratedEvent(
            (ushort)LogEventId.UnknownFunctionCallInPackageConfigurationFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Unexpected statement in module configuration file. Only a single call to '" +
                Names.ModuleConfigurationFunctionCall + "' function is allowed.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportUnknownStatementInPackageConfigurationFile(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.AtLeastSinglePackageConfigurationDeclarationInPackageConfigurationFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "The module configuration file must contain at least a single call to '" +
                Names.ModuleConfigurationFunctionCall + "' function.",
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void ReportAtLeastSingleFunctionCallInPackageConfigurationFile(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.DuplicateBinding,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Duplicate binding for '{name}'.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportDuplicateBinding(LoggingContext context, Location location, string name);

        [GeneratedEvent(
            (ushort)LogEventId.ConfigurationDeclarationIsOnlyInConfigurationFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Configuration declaration '" + Names.ConfigurationFunctionCall +
                "' can only occur in the configuration file '" + Names.ConfigBc + "'.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportConfigurationDeclarationIsOnlyInConfigurationFile(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.PackageConfigurationDeclarationIsOnlyInConfigurationFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Module configuration declaration '" + Names.ModuleConfigurationFunctionCall +
                "' can only occur in a module configuration file.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportPackageConfigurationDeclarationIsOnlyInConfigurationFile(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.InvalidEnumMember,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "'{enumMember}' is invalid. A member of a constant enum must have an initializer that evaluates to a numeric value.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportInvalidEnumMember(LoggingContext context, Location location, string enumMember);

        [GeneratedEvent(
            (ushort)LogEventId.LocalFunctionsAreNotSupported,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Local functions are not supported in DScript. Use lambda expressions instead.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportLocalFunctionsAreNotSupported(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.InvalidPathInterpolationExpression,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "{message}",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportInvalidPathInterpolationExpression(LoggingContext context, Location location, string message);

        [GeneratedEvent(
            (ushort)LogEventId.LeftHandSideOfAssignmentMustBeLocalVariable,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Left-hand side of an assignment expression must be a local variable.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportLeftHandSideOfAssignmentMustBeLocalVariable(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.ImportAliasIsNotReferencedAndWillBeRemoved,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Imported file '{path}' is not referenced and will be removed.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportImportAliasIsNotReferencedAndWillBeRemoved(LoggingContext context, Location location, string path);

        [GeneratedEvent(
            (ushort)LogEventId.OperandOfIncrementOrDecrementOperatorMustBeLocalVariable,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "The operand of an increment or decrement operator must be a local variable.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void OperandOfIncrementOrDecrementOperatorMustBeLocalVariable(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.OnlyASingleConfigurationDeclarationInConfigurationFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "The configuration file '" + Names.ConfigBc +
                "' must contain a single call to 'configure' function.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportOnlyASingleFunctionCallInConfigurationFile(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.UnknownFunctionCallInConfigurationFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Unexpected statement in configuration file '" +
                        Names.ConfigBc +
                        "'. Only a single call to '" + Names.ConfigurationFunctionCall + "' function is allowed.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportUnknownStatementInConfigurationFile(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.InvalidConfigurationFileFormat,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Configuration file '" + Names.ConfigBc + "' is invalid. {message}",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportInvalidConfigurationFileFormat(LoggingContext context, Location location, string message);

        [GeneratedEvent(
            (ushort)LogEventId.InvalidPathExpression,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Path '{path}' is invalid.",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportInvalidPathExpression(LoggingContext context, Location location, string path);

        [GeneratedEvent(
            (ushort)LogEventId.InvalidRelativePathExpression,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Invalid relative path expression.",
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void ReportInvalidRelativePathExpression(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.InvalidPathAtomExpression,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Invalid path atom expression.",
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void ReportInvalidPathAtomExpression(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.ProjectPathIsInvalid,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Referenced project path '{path}' is invalid.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportProjectPathIsInvalid(LoggingContext context, Location location, string path);

        [GeneratedEvent(
            (ushort)LogEventId.ModuleSpecifierContainsInvalidCharacters,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Referenced project '{moduleName}' contains invalid characters. The following characters are not allowed: {invalidCharacters}.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportModuleSpecifierContainsInvalidCharacters(LoggingContext context, Location location, string moduleName, string invalidCharacters);

        [GeneratedEvent(
            (ushort)LogEventId.ProjectPathComputationFailed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Failed to compute absolute path for '{path}'.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportProjectPathComputationFailed(LoggingContext context, Location location, string path);

        [GeneratedEvent(
            (int)LogEventId.IntegralConstantIsTooLarge,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Integral constant '{expression}' is too large.",
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void ReportIntegralConstantIsTooLarge(LoggingContext context, Location location, string expression);

        [GeneratedEvent(
            (int)LogEventId.LeftHandSideOfAssignmentExpressionCannotBeAConstant,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Left-hand side of assignment expression '{expression}' cannot be a constant.",
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void ReportLeftHandSideOfAssignmentExpressionCannotBeAConstant(LoggingContext context, Location location, string expression);

        [GeneratedEvent(
            (int)LogEventId.TheOperandOfAnIncrementOrDecrementOperatorCannotBeAConstant,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "The operand of an increment or decrement operator cannot be a constant.",
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void ReportTheOperandOfAnIncrementOrDecrementOperatorCannotBeAConstant(LoggingContext context, Location location, string expression);

        [GeneratedEvent(
            (int)LogEventId.ArithmeticOverflow,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Arithmetic operation '{expression}' resulted in an overflow.",
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void ReportArithmeticOverflow(LoggingContext context, Location location, string expression);

        [GeneratedEvent(
            (int)LogEventId.InvalidRadix,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Invalid radix was specified for '{expression}'. Valid values are 2, 8, 10 or 16, but got {radix}",
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void ReportInvalidRadix(LoggingContext context, Location location, string expression, int radix);

        [GeneratedEvent(
            (int)LogEventId.NameCannotBeFound,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Name '{name}' cannot be found.",
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void ReportNameCannotBeFound(LoggingContext context, Location location, string name);

        [GeneratedEvent(
            (int)LogEventId.OuterVariableCapturingForMutationIsNotSupported,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Cannot capture enclosing variable '{name}' for mutation.",
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void ReportOuterVariableCapturingForMutationIsNotSupported(LoggingContext context, Location location, string name);

        [GeneratedEvent(
            (int)LogEventId.BlockScopedVariableUsedBeforeDeclaration,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Block scoped variable '{variableName}' used before its declaration.",
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void ReportBlockScopedVariableUsedBeforeDeclaration(LoggingContext context, Location location, string variableName);

        [GeneratedEvent(
            (int)LogEventId.WarnForDeprecatedV1Modules,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            EventTask = (ushort)Tasks.Parser,
            Message = @"Deprecated module configuration. Module '{moduleName}' declared in '{moduleFile}' is declared as an old Legacy DScript file (V1-Format). This format will soon be deprecated. You can make it a V2 module by setting the name resolution semantics.
    module({{
        name: '{moduleName}',
        nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences,
        // and the rest of your configuration.
    }});

If you need assistance in migrating to V2, feel free to reach out to the BuildXL team and we can help.
If you can't update and need this feature after July 2018 please reach out to the BuildXL team.",
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void WarnForDeprecatedV1Modules(LoggingContext context, string moduleName, string moduleFile);
    }
}
#pragma warning restore SA1600 // Element must be documented
#pragma warning restore 1591
