// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Tracing;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;

#pragma warning disable 1591
#pragma warning disable SA1600 // Element must be documented

namespace BuildXL.FrontEnd.Script.Tracing
{
    /// <summary>
    /// Events triggered during the evaluation phase
    /// </summary>
    public abstract partial class Logger
    {
        [GeneratedEvent(
            (ushort)LogEventId.QualifierMustEvaluateToObjectLiteral,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message =
            EventConstants.LabeledProvenancePrefix + "Qualifier must evaluate to an object literal.{stackTrace}",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportQualifierMustEvaluateToObjectLiteral(LoggingContext context, Location location, string stackTrace);

        [GeneratedEvent(
            (ushort)LogEventId.QualifierValueMustEvaluateToString,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message =
            EventConstants.LabeledProvenancePrefix + "Qualifier value must evaluate to a string {error}.{stackTrace}",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportQualifierValueMustEvaluateToString(LoggingContext context, Location location, string error, string stackTrace);

        [GeneratedEvent(
            (ushort)LogEventId.ApplyAmbientNumberOfArgumentsLessThanMinArity,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message =
            EventConstants.LabeledProvenancePrefix +
            "The invocation of '{functorName}' requires at least {minArity} arguments, but it is performed with only {numOfArguments} arguments.{stackTrace}",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportApplyAmbientNumberOfArgumentsLessThanMinArity(
            LoggingContext context, Location location, string stackTrace, string functorName, int minArity, int numOfArguments);

        /// <summary>
        /// TODO: eventually we should move out from this one and start using <see cref="ReportQualifierCannotBeCoarcedToQualifierSpaceWithProvenance"/>
        /// </summary>
        [GeneratedEvent(
            (ushort)LogEventId.QualifierCannotBeCoarcedToQualifierSpace,
            EventGenerators = BuildXL.Tracing.EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Qualifier '{qualifierName}' cannot be coerced to '{qualifierSpace}'",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportQualifierCannotBeCoarcedToQualifierSpace(LoggingContext loggingContext, Location location, string qualifierName, string qualifierSpace);

        [GeneratedEvent(
            (ushort)LogEventId.QualifierCannotBeCoarcedToQualifierSpaceWithProvenance,
            EventGenerators = BuildXL.Tracing.EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Qualifier '{qualifierName}' cannot be coerced to '{qualifierSpace}' when referencing '{referencedLocation.File}({referencedLocation.Line},{referencedLocation.Position})'.",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportQualifierCannotBeCoarcedToQualifierSpaceWithProvenance(LoggingContext loggingContext, Location location, string qualifierName, string qualifierSpace, Location referencedLocation);

        [GeneratedEvent(
            (ushort)LogEventId.UnexpectedValueType,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message =
            EventConstants.LabeledProvenancePrefix +
            "Expecting '{currentExpression}' of type(s) '{expectedTypes}', but got '{actualValue}' of type '{actualType}'.{stackTrace}",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportUnexpectedValueType(
            LoggingContext context, Location location, string currentExpression, string expectedTypes, string actualValue, string actualType, string stackTrace);

        [GeneratedEvent(
            (ushort)LogEventId.UnexpectedValueTypeForName,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message =
            EventConstants.LabeledProvenancePrefix +
            "Expecting '{expectedValue}' of type(s) '{expectedTypes}', but got '{actualType}' with value '{actualValue}'.{stackTrace}",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportUnexpectedValueTypeForName(
            LoggingContext context, Location location, string expectedValue, string expectedTypes, string actualValue, string actualType, string stackTrace);

        [GeneratedEvent(
            (ushort)LogEventId.UnexpectedValueTypeOnConversion,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message =
            EventConstants.LabeledProvenancePrefix +
            "Expecting type(s) '{expectedTypes}' {receiver}, but got '{actualValue}' of type '{actualType}'.{stackTrace}",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportUnexpectedValueTypeOnConversion(
            LoggingContext context, Location location, string expectedTypes, string receiver, string actualValue, string actualType, string stackTrace);

        [GeneratedEvent(
            (ushort)LogEventId.ArrayIndexOufOfRange,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message =
            EventConstants.LabeledProvenancePrefix +
            "Index {index} is outside the bounds of the array '{arrayExpression}' (length = {length}).{stackTrace}",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportArrayIndexOufOfRange(
            LoggingContext context, Location location, int index, string arrayExpression, int length, string stackTrace);

        [GeneratedEvent(
            (ushort)LogEventId.StringIndexOufOfRange,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message =
            EventConstants.LabeledProvenancePrefix +
            "Index {index} is outside the bounds of the string '{expression}' (length = {length}).{stackTrace}",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportStringIndexOufOfRange(
            LoggingContext context, Location location, int index, string expression, int length, string stackTrace);

        [GeneratedEvent(
            (ushort)LogEventId.ArgumentIndexOutOfBound,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message =
            EventConstants.LabeledProvenancePrefix +
            "Function accesses argument at index {index}, but is invoked only with {numberOfArguments} argument(s).{stackTrace}",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportArgumentIndexOutOfBound(
            LoggingContext context, Location location, int index, int numberOfArguments, string stackTrace);

        [GeneratedEvent(
            (ushort)LogEventId.ResolveImportDuplicateBinding,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message =
                EventConstants.LabeledProvenancePrefix +
                "Duplicate binding '{name}' occurs on importing '{importedModule}' from '{importingModule}'.{stackTrace}",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportResolveImportDuplicateBinding(
            LoggingContext context, Location location, string name, string importedModule, string importingModule, string stackTrace);

        [GeneratedEvent(
            (ushort)LogEventId.FailResolveSelectorDueToUndefined,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message =
                EventConstants.LabeledProvenancePrefix + "Unable to get property '{propertyName}' since '{thisExpression}' evaluates to 'undefined'.{stackTrace}",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportFailResolveSelectorDueToUndefined(
            LoggingContext context, Location location, string propertyName, string thisExpression, string stackTrace);
        
        [GeneratedEvent(
            (ushort)LogEventId.FailResolveModuleSelector,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message =
                EventConstants.LabeledProvenancePrefix + "Selector '{selector}' of '{thisExpression}' cannot be resolved.{stackTrace}",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportFailResolveModuleSelector(
            LoggingContext context, Location location, string selector, string thisExpression, string stackTrace);

        [GeneratedEvent(
            (ushort)LogEventId.MissingNamespaceMember,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message =
                EventConstants.LabeledProvenancePrefix + "Missing field or member '{member}'{relatedMessage}.{stackTrace}",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportMissingNamespaceMember(
            LoggingContext context, Location location, string member, string relatedMessage, string stackTrace);

        [GeneratedEvent(
            (ushort)LogEventId.MissingInstanceMember,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message =
                EventConstants.LabeledProvenancePrefix + "Object of type '{type}' does not support property or method '{member}'.{stackTrace}",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportMissingInstanceMember(
            LoggingContext context, Location location, string member, string type, string stackTrace);

        [GeneratedEvent(
            (ushort)LogEventId.MissingNamespace,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message =
                EventConstants.LabeledProvenancePrefix + "Missing namespace '{namespaceName}'{relatedMessage}.{stackTrace}",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportMissingNamespace(
            LoggingContext context, Location location, string namespaceName, string relatedMessage, string stackTrace);

        [GeneratedEvent(
            (ushort)LogEventId.ContractAssert,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message =
                EventConstants.LabeledProvenancePrefix + "Assertion violation evaluating expression '{expression}'{additionalInformation}.{stackTrace}",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportContractAssert(
            LoggingContext context, Location location, string expression, string additionalInformation, string stackTrace);

        [GeneratedEvent(
            (ushort)LogEventId.ContractRequire,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message =
                EventConstants.LabeledProvenancePrefix + "Precondition violation evaluating expression '{expression}'{additionalInformation}.{stackTrace}",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportContractRequire(
            LoggingContext context, Location location, string expression, string additionalInformation, string stackTrace);

        [GeneratedEvent(
            (ushort)LogEventId.ContractFail,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "{message}\r\n{callstack}",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportContractFail(LoggingContext context, Location location, string message, string callstack);

        [GeneratedEvent(
            (ushort)LogEventId.ContractWarn,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "{message}",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportContractWarn(LoggingContext context, Location location, string message);

        [GeneratedEvent(
            (ushort)LogEventId.DirectoryOperationError,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message =
                EventConstants.LabeledProvenancePrefix + "Directory operation error for '{expression}'{additionalInformation}.{stackTrace}",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportDirectoryOperationError(
            LoggingContext context, Location location, string expression, string additionalInformation, string stackTrace);

        [GeneratedEvent(
            (ushort)LogEventId.DirectoryNotSupportedException,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message =
                EventConstants.LabeledProvenancePrefix + "Directory operation for '{expression}' is not supported for this SealedDirectory. At the moment this is only supported for Full and Partially sealed directories.{stackTrace}",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void DirectoryNotSupportedException(
            LoggingContext context, Location location, string expression, string stackTrace);


        [GeneratedEvent(
            (ushort)LogEventId.FileOperationError,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message =
                EventConstants.LabeledProvenancePrefix + "File operation error for '{expression}'{additionalInformation}.{stackTrace}",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportFileOperationError(
            LoggingContext context, Location location, string expression, string additionalInformation, string stackTrace);

        [GeneratedEvent(
            (ushort)LogEventId.InputValidationError,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message =
                EventConstants.LabeledProvenancePrefix + "Invalid input {errorMessage}{additionalInformation}.{stackTrace}",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportInputValidationError(
            LoggingContext context, Location location, string errorMessage, string additionalInformation, string stackTrace);

        [GeneratedEvent(
            (ushort)LogEventId.SpreadIsNotAppliedToArrayValue,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message =
                EventConstants.LabeledProvenancePrefix + "Spread operation on '{expression}' fails because the expression does not evaluate to an array value.{stackTrace}",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportSpreadIsNotAppliedToArrayValue(
            LoggingContext context, Location location, string expression, string stackTrace);

        [GeneratedEvent(
            (ushort)LogEventId.UnexpectedAmbientException,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message =
                EventConstants.LabeledProvenancePrefix + "Unexpected ambient exception: {fullExceptionMessage}.{stackTrace}",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportUnexpectedAmbientException(
            LoggingContext context, Location location, string fullExceptionMessage, string stackTrace);

        [GeneratedEvent(
            (ushort)LogEventId.DivideByZero,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message =
                EventConstants.LabeledProvenancePrefix + "Divide-by-zero error occurred evaluating an expression '{expression}'.{stackTrace}",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportDivideByZero(LoggingContext context, Location location, string expression, string stackTrace);

        [GeneratedEvent(
            (ushort)LogEventId.StackOverflow,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message =
                EventConstants.LabeledProvenancePrefix + "Stack overflow: current stack size is {stackSize}, but the threshold is {threshold}. Do you have runaway recursion?{stackTrace}",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportStackOverflow(
            LoggingContext context, Location location, int stackSize, int threshold, string stackTrace);

        [GeneratedEvent(
            (ushort)LogEventId.ForLoopOverflow,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message =
                EventConstants.LabeledProvenancePrefix + "For-loop overflow: current iteration maximum is {iterationThreshold}. Are you sure your loop is terminating?{stackTrace}",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportForLoopOverflow(
            LoggingContext context, Location location, int iterationThreshold, string stackTrace);

        [GeneratedEvent(
            (ushort)LogEventId.WhileLoopOverflow,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message =
                EventConstants.LabeledProvenancePrefix + "While-loop overflow: current iteration maximum is {iterationThreshold}. Are you sure your loop is terminating?{stackTrace}",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportWhileLoopOverflow(
            LoggingContext context, Location location, int iterationThreshold, string stackTrace);

        [GeneratedEvent(
            (ushort)LogEventId.InvalidPathAtom,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message =
                EventConstants.LabeledProvenancePrefix + "Invalid path atom {error}{additionalInformation}.{stackTrace}",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportInvalidPathAtom(
            LoggingContext context, Location location, string error, string additionalInformation, string stackTrace);

        [GeneratedEvent(
            (ushort)LogEventId.InvalidTypeFormat,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message =
                EventConstants.LabeledProvenancePrefix + "Invalid format of type '{targetType}' {error}{additionalInformation}.{stackTrace}",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportInvalidTypeFormat(
            LoggingContext context, Location location, string targetType, string error, string additionalInformation, string stackTrace);

        [GeneratedEvent(
            (ushort)LogEventId.JsonUnsuportedTypeForSerialization,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message =
                EventConstants.LabeledProvenancePrefix + "Encountered value of type '{encounteredType}'. This type is not supported to be serialized to Json.{additionalInformation}.{stackTrace}",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportJsonUnsuportedTypeForSerialization(
            LoggingContext context, Location location, string encounteredType, string additionalInformation, string stackTrace);

        [GeneratedEvent(
            (ushort)LogEventId.ReportJsonUnsuportedDynamicFieldsForSerialization,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message =
                EventConstants.LabeledProvenancePrefix + "Encountered value of type '{encounteredType}'. Dynamic json values are expected to be of type 'expectedType'.{additionalInformation}.{stackTrace}",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportJsonUnsuportedDynamicFieldsForSerialization(
            LoggingContext context, Location location, string encounteredType, string expectedType, string additionalInformation, string stackTrace);

        [GeneratedEvent(
            (ushort)LogEventId.ReportXmlUnsuportedTypeForSerialization,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message =
                EventConstants.LabeledProvenancePrefix + "Encountered value of type '{encounteredType}'. This type is not supported to be serialized to Xml.{additionalInformation}.{stackTrace}",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportXmlUnsuportedTypeForSerialization(
            LoggingContext context, Location location, string encounteredType, string additionalInformation, string stackTrace);

        [GeneratedEvent(
            (ushort)LogEventId.ReportUnsupportedTypeValueObjectException,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message =
                EventConstants.LabeledProvenancePrefix + "Encountered value of type '{encounteredType}'. This type is not supported to be used as a key nor value in the ValueCache.{additionalInformation}.{stackTrace}",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportUnsupportedTypeValueObjectException(
            LoggingContext context, Location location, string encounteredType, string additionalInformation, string stackTrace);

        [GeneratedEvent(
            (ushort)LogEventId.ReportXmlInvalidStructure,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message =
                EventConstants.LabeledProvenancePrefix + "Encountered node with unexpected value of type '{encounteredType}'. Expected a value of type '{expectedType}' for field '{fieldName}' of nodes with type '{nodeType}'.{additionalInformation}.{stackTrace}",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportXmlInvalidStructure(
            LoggingContext context, Location location, string encounteredType, string expectedType, string fieldName, string nodeType, string additionalInformation, string stackTrace);

        [GeneratedEvent(
            (ushort)LogEventId.ReportXmlReadError,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message =
                EventConstants.LabeledProvenancePrefix + "Error reading Xml file contents '{filePath}({line},{column})': {message}.{additionalInformation}.{stackTrace}",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportXmlReadError(
            LoggingContext context, Location location, string filePath, int line, int column, string message, string additionalInformation, string stackTrace);


        [GeneratedEvent(
            (ushort)LogEventId.KeyFormDllNotFound,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message =
                EventConstants.LabeledProvenancePrefix + "Specified KeyForm file: '{keyFormDllPath}' not found{additionalInformation}.{stackTrace}",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportKeyFormDllNotFound(
            LoggingContext context, Location location, string keyFormDllPath, string additionalInformation, string stackTrace);

        [GeneratedEvent(
            (ushort)LogEventId.KeyFormDllWrongFileName,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message =
                EventConstants.LabeledProvenancePrefix + "Specified KeyForm file: '{keyFormDllPath}' must have filename: '{keyFormDllName}' {additionalInformation}.{stackTrace}",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportKeyFormDllWrongFileName(
            LoggingContext context, Location location, string keyFormDllPath, string keyFormDllName, string additionalInformation, string stackTrace);

        [GeneratedEvent(
            (ushort)LogEventId.KeyFormDllLoad,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message =
                EventConstants.LabeledProvenancePrefix + "Specified KeyForm file: '{keyFormDllPath}' failed to load: {errorCode}: {errorMessage}{additionalInformation}.{stackTrace}",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportKeyFormDllLoad(
            LoggingContext context, Location location, string keyFormDllPath, int errorCode, string errorMessage, string additionalInformation, string stackTrace);

        [GeneratedEvent(
            (ushort)LogEventId.KeyFormDllLoadedWithDifferentDll,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message =
                EventConstants.LabeledProvenancePrefix + "Specified KeyForm file: '{keyFormDllPath}' cannot be loaded because KeyForm library is already loaded from : '{otherKeyFormDllPath}' {additionalInformation}.{stackTrace}",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportKeyFormDllLoadedWithDifferentDll(
            LoggingContext context, Location location, string keyFormDllPath, string otherKeyFormDllPath, string additionalInformation, string stackTrace);

        [GeneratedEvent(
            (ushort)LogEventId.ReportKeyFormNativeFailure,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message =
                EventConstants.LabeledProvenancePrefix + "Specified KeyForm file: '{keyFormDllPath}' failed to generate KeyForm with a failure of type {exceptionType}: '{exceptionMessage}' {additionalInformation}.{stackTrace}",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportKeyFormNativeFailure(
            LoggingContext context, Location location, string keyFormDllPath, string exceptionType, string exceptionMessage, string additionalInformation, string stackTrace);

        [GeneratedEvent(
            (ushort)LogEventId.UndefinedMapKey,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message =
                EventConstants.LabeledProvenancePrefix + "{message} {error}.{stackTrace}",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportUndefinedMapKey(
            LoggingContext context, Location location, string message, string error, string stackTrace);

        [GeneratedEvent(
            (ushort)LogEventId.InvalidKeyValueMap,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message =
                EventConstants.LabeledProvenancePrefix + "{message} {error}.{stackTrace}",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportInvalidKeyValueMap(
            LoggingContext context, Location location, string message, string error, string stackTrace);

        [GeneratedEvent(
            (ushort)LogEventId.UndefinedSetItem,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message =
                EventConstants.LabeledProvenancePrefix + "{message} {error}.{stackTrace}",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportUndefinedSetItem(
            LoggingContext context, Location location, string message, string error, string stackTrace);

        [GeneratedEvent(
            (ushort)LogEventId.GetMountNameNullOrEmpty,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message =
                EventConstants.LabeledProvenancePrefix + "The name passed to Context.getMount was null or empty.",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportGetMountNameNullOrEmpty(
            LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.GetMountNameNotFound,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message =
                EventConstants.LabeledProvenancePrefix + "Mount with name '{name}' was not found. Legal mounts are: '{mounts}'.",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportGetMountNameNotFound(
            LoggingContext context, Location location, string name, string mounts);

        [GeneratedEvent(
            (ushort)LogEventId.GetMountNameCaseMisMatch,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message =
                EventConstants.LabeledProvenancePrefix + "Mount with name '{name}' was not using the proper casing. You must refer to this mount using '{properName}'.",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportGetMountNameCaseMisMatch(
            LoggingContext context, Location location, string name, string properName);

        [GeneratedEvent(
            (int)LogEventId.InvalidFormatForStringToNumberConversion,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Input string '{expression}' is not convertible to number.",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportInvalidFormatForStringToNumberConversion(LoggingContext context, Location location, string expression);

        [GeneratedEvent(
            (int)LogEventId.ArgumentForPowerOperationShouldNotBeNegative,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Argument for exponentiation operator '**' should not be negative.",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportArgumentForPowerOperationShouldNotBeNegative(LoggingContext context, Location location);

        [GeneratedEvent(
            (int)LogEventId.FileNotFoundInStaticDirectory,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Could not find file '{fullPath}' in static directory.{stackTrace}",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportFileNotFoundInStaticDirectory(LoggingContext context, Location location, string fullPath, string stackTrace);

        [GeneratedEvent(
            (ushort)LogEventId.Cycle,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message =
                EventConstants.LabeledProvenancePrefix + "A cyclic evaluation dependency was detected between exported values.{stackTrace}",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportCycle(LoggingContext context, Location location, string stackTrace);

        [GeneratedEvent(
            (ushort)LogEventId.ArrayEvaluationStatistics,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Parser,
            Message = "  [DScript.{0}] array evaluation: {1} empty, {2} evaluations, {3} constructed as evaluated arrays.",
            Keywords = (int)Keywords.Performance | (int)Keywords.UserMessage)]
        public abstract void ArrayEvaluationStatistics(LoggingContext context, string name, long empty, long evaluations, long evaluatedArrays);

        [GeneratedEvent(
            (ushort)LogEventId.GlobStatistics,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Parser,
            Message = "  [DScript.{name}] glob: total time {totalGlobTimeInMs} ms.",
            Keywords = (int)Keywords.Performance | (int)Keywords.UserMessage)]
        public abstract void GlobStatistics(LoggingContext context, string name, long totalGlobTimeInMs);

        [GeneratedEvent(
            (ushort)LogEventId.MethodInvocationCountStatistics,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Parser,
            Message = "  [DScript.{name}] [{duration} ms] {methodName} was called {numberOfCalls} times.",
            Keywords = (int)Keywords.Performance | (int)Keywords.UserMessage)]
        public abstract void MethodInvocationCountStatistics(LoggingContext context, string name, string methodName, long numberOfCalls, string duration);

        [GeneratedEvent(
            (ushort)LogEventId.ContextStatistics,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Parser,
            Message = "  [DScript.{0}] contexts: {1} trees, {2} contexts.",
            Keywords = (int)Keywords.Performance | (int)Keywords.UserMessage)]
        public abstract void ContextStatistics(LoggingContext context, string name, long contextTrees, long contexts);

        [GeneratedEvent(
            (ushort)LogEventId.NoBuildLogicInProjects,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Interface, type, enum and function declarations are not allowed in project files ('{projectExtension}'). " +
                      "If this represents a build logic file, consider changing the extension to '{buildLogicExtension}'.",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportNoBuildLogicInProjects(LoggingContext context, Location location, string projectExtension, string buildLogicExtension);

        [GeneratedEvent(
            (ushort)LogEventId.NoExportedLambdasInProjects,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Exporting functions is not allowed in project files ('{projectExtension}'). " +
                      "If this represents a build logic file, consider changing the extension to '{buildLogicExtension}'.",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportNoExportedLambdasInProjects(LoggingContext context, Location location, string projectExtension, string buildLogicExtension);

        [GeneratedEvent(
            (ushort)LogEventId.TemplateInContextNotAvailable,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Captured template is not available in Context object. This functionality is only available in DScript V2.",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportTemplateInContextNotAvailable(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.InvalidPathOperation,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message =
                EventConstants.LabeledProvenancePrefix + "Invalid path operation {error}{additionalInformation}.{stackTrace}",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportInvalidPathOperation(
            LoggingContext context, Location location, string error, string additionalInformation, string stackTrace);
    }
}

#pragma warning restore SA1600 // Element must be documented
#pragma warning restore 1591

