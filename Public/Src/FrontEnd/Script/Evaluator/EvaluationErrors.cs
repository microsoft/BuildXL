// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Ambients;
using BuildXL.FrontEnd.Script.Ambients.Exceptions;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Tracing;
using BuildXL.FrontEnd.Script.Util;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Qualifier;
using static BuildXL.Utilities.FormattableStringEx;
using static BuildXL.FrontEnd.Script.DisplayStringHelper;
using LineInfo = TypeScript.Net.Utilities.LineInfo;
using TypeScript.Net.Utilities;

namespace BuildXL.FrontEnd.Script.Evaluator
{
    /// <summary>
    /// Facade class for error notification during evaluation.
    /// </summary>
    /// <remarks>
    /// This class is just a simple facade over logging infrastructure and has no additional logic.
    /// It could be easily removed but it is still useful because it provides some higher level functions that
    /// direct logger.
    /// </remarks>
    public sealed class EvaluationErrors
    {
        private const int MaxKnownPackageList = 30;

        private ImmutableContextBase Context { get; }

        private LoggingContext LoggingContext => Context.LoggingContext;

        /// <nodoc />
        public EvaluationErrors(ImmutableContextBase context)
        {
            Contract.Requires(context != null);
            Context = context;
        }

        /// <nodoc/>
        public Logger Logger => Context.Logger;

        /// <nodoc />
        public void ReportArithmeticOverflow(
            ModuleLiteral env,
            Expression expression,
            LineInfo lineInfo)
        {
            Contract.Requires(env != null);
            var location = lineInfo.AsUniversalLocation(env, Context);

            Logger.ReportArithmeticOverflow(
                LoggingContext,
                location.AsLoggingLocation(),
                expression.ToDisplayString(Context));
        }

        /// <nodoc />
        public void ReportInvalidRadix(
            ModuleLiteral env,
            Expression expression,
            LineInfo lineInfo,
            int radix)
        {
            Contract.Requires(env != null);
            var location = lineInfo.AsUniversalLocation(env, Context);

            Logger.ReportInvalidRadix(
                LoggingContext,
                location.AsLoggingLocation(),
                expression.ToDisplayString(Context),
                radix);
        }

        /// <nodoc />
        [SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
        public void ReportApplyAmbientNumberOfArgumentsLessThanMinArity(
            ModuleLiteral env,
            Expression functor,
            int minArity,
            int numOfArguments,
            LineInfo lineInfo = default(LineInfo))
        {
            Contract.Requires(env != null);
            Contract.Requires(functor != null);
            var location = lineInfo.AsUniversalLocation(env, Context);

            Logger.ReportApplyAmbientNumberOfArgumentsLessThanMinArity(
                LoggingContext,
                location.AsLoggingLocation(),
                Context.GetStackTraceAsErrorMessage(location),
                functor.ToDisplayString(Context),
                minArity,
                numOfArguments);
        }

        /// <nodoc />
        [SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
        public void ReportUnexpectedValueType(ModuleLiteral env, Expression expression, EvaluationResult value, params Type[] expectedTypes)
        {
            ReportUnexpectedValueType(env, expression, value, UnionTypeToString(Context, expectedTypes));
        }

        /// <nodoc />
        [SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
        public void ReportUnexpectedValueType(ModuleLiteral env, Expression expression, EvaluationResult value, string expectedTypes)
        {
            Contract.Requires(env != null);
            Contract.Requires(expression != null);
            Contract.Requires(expectedTypes != null);

            var location = expression.Location.AsUniversalLocation(env, Context);

            Logger.ReportUnexpectedValueType(
                LoggingContext,
                location.AsLoggingLocation(),
                expression.ToDisplayString(Context),
                expectedTypes,
                ValueToString(value, Context),
                ValueTypeToString(value, Context),
                Context.GetStackTraceAsErrorMessage(location));
        }

        /// <nodoc />
        public void ReportUnexpectedValueTypeForName(
            ModuleLiteral env,
            SymbolAtom name,
            string expectedTypes,
            EvaluationResult value,
            LineInfo lineInfo = default(LineInfo))
        {
            Contract.Requires(env != null);
            Contract.Requires(name.IsValid);
            Contract.Requires(expectedTypes != null);

            var location = lineInfo.AsUniversalLocation(env, Context);

            Logger.ReportUnexpectedValueTypeForName(
                LoggingContext,
                location.AsLoggingLocation(),
                name.ToString(Context.FrontEndContext.SymbolTable),
                expectedTypes,
                ValueToString(value, Context),
                ValueTypeToString(value, Context),
                Context.GetStackTraceAsErrorMessage(location));
        }

        /// <nodoc />
        public void ReportUnexpectedValueTypeOnConversion(
            ModuleLiteral env,
            ConvertException exception,
            LineInfo lineInfo)
        {
            Contract.Requires(env != null);
            Contract.Requires(exception != null);

            var location = lineInfo.AsUniversalLocation(env, Context);

            Logger.ReportUnexpectedValueTypeOnConversion(
                LoggingContext,
                location.AsLoggingLocation(),
                exception.ExpectedTypesToString(Context),
                exception.ErrorContext.ErrorReceiverAsString(Context),
                ValueToString(exception.Value, Context),
                ValueTypeToString(exception.Value, Context),
                Context.GetStackTraceAsErrorMessage(location));
        }

        /// <nodoc />
        public void ReportArrayIndexOufOfRange(
            ModuleLiteral env,
            IndexExpression expression,
            int index,
            int arrayLength)
        {
            Contract.Requires(env != null);
            Contract.Requires(expression != null);

            var location = expression.Location.AsUniversalLocation(env, Context);

            Logger.ReportArrayIndexOufOfRange(
                LoggingContext,
                location.AsLoggingLocation(),
                index,
                expression.ThisExpression.ToDisplayString(Context),
                arrayLength,
                Context.GetStackTraceAsErrorMessage(location));
        }

        /// <nodoc />
        public void ReportStringIndexOufOfRange(ModuleLiteral env, int index, string target, LineInfo lineInfo)
        {
            Contract.Requires(env != null);

            var location = lineInfo.AsUniversalLocation(env, Context);

            Logger.ReportStringIndexOufOfRange(
                LoggingContext,
                location.AsLoggingLocation(),
                index,
                target,
                target.Length,
                Context.GetStackTraceAsErrorMessage(location));
        }

        /// <nodoc />
        public void ReportArgumentIndexOutOfBound(ModuleLiteral env, int index, int numberOfArguments, LineInfo lineInfo)
        {
            Contract.Requires(env != null);

            var location = lineInfo.AsUniversalLocation(env, Context);

            Logger.ReportArgumentIndexOutOfBound(
                LoggingContext,
                location.AsLoggingLocation(),
                index,
                numberOfArguments,
                Context.GetStackTraceAsErrorMessage(location));
        }

        /// <nodoc />
        public void ReportMissingProperty(ModuleLiteral env, SymbolAtom selector, object receiver, LineInfo lineInfo)
        {
            Contract.Requires(env != null);
            Contract.Requires(receiver != null);

            var locationForLogging = lineInfo.AsUniversalLocation(env, Context);
            Logger.ReportMissingInstanceMember(
                LoggingContext,
                locationForLogging.AsLoggingLocation(),
                selector.ToDisplayString(Context),
                receiver.GetType().ToDisplayString(Context),
                Context.GetStackTraceAsString(locationForLogging));
        }

        /// <nodoc />
        [SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
        public void ReportFailResolveSelectorDueToUndefined(ModuleLiteral env, SelectorExpression expression)
        {
            Contract.Requires(env != null);
            Contract.Requires(expression != null);

            var location = expression.Location.AsUniversalLocation(env, Context);
            Logger.ReportFailResolveSelectorDueToUndefined(
                LoggingContext,
                location.AsLoggingLocation(),
                expression.Selector.ToDisplayString(Context),
                expression.ThisExpression.ToDisplayString(Context),
                Context.GetStackTraceAsErrorMessage(location));
        }

        /// <nodoc />
        [SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
        public void ReportFailResolveSelectorDueToUndefined(ModuleLiteral env, Expression thisExpression, SymbolAtom name, LineInfo lineInfo)
        {
            Contract.Requires(env != null);
            Contract.Requires(name.IsValid);

            var location = lineInfo.AsUniversalLocation(env, Context);
            Logger.ReportFailResolveSelectorDueToUndefined(
                LoggingContext,
                location.AsLoggingLocation(),
                name.ToDisplayString(Context),
                thisExpression.ToDisplayString(Context),
                Context.GetStackTraceAsErrorMessage(location));
        }

        /// <nodoc />
        public void ReportFailResolveModuleSelector(ModuleLiteral env, ModuleSelectorExpression expression)
        {
            Contract.Requires(env != null);
            Contract.Requires(expression != null);

            var location = expression.Location.AsUniversalLocation(env, Context);
            Logger.ReportFailResolveModuleSelector(
                LoggingContext,
                location.AsLoggingLocation(),
                expression.Selector.ToDisplayString(Context),
                expression.ThisExpression.ToDisplayString(Context),
                Context.GetStackTraceAsErrorMessage(location));
        }

        /// <nodoc />
        public void ReportMissingMember(ModuleLiteral env, SymbolAtom name, ModuleLiteral relatedEnv, LineInfo lineInfo)
        {
            Contract.Requires(env != null);
            Contract.Requires(name.IsValid);

            var relatedLocation = relatedEnv.Location.AsLoggingLocation(relatedEnv, Context).ToDisplayString();
            var relatedMessage = env == relatedEnv ? string.Empty : I($", related location '{relatedLocation}'");

            var location = lineInfo.AsUniversalLocation(env, Context);

            Logger.ReportMissingNamespaceMember(
                LoggingContext,
                location.AsLoggingLocation(),
                name.ToDisplayString(Context),
                relatedMessage,
                Context.GetStackTraceAsErrorMessage(location));
        }

        /// <nodoc />
        public void ReportMissingNamespace(ModuleLiteral env, FullSymbol name, ModuleLiteral relatedEnv, LineInfo lineInfo)
        {
            Contract.Requires(env != null);
            Contract.Requires(name.IsValid);
            Contract.Requires(relatedEnv != null);
            var relatedLocation = relatedEnv.Location.AsLoggingLocation(relatedEnv, Context).ToDisplayString();
            var relatedMessage = env == relatedEnv ? string.Empty : I($", related location '{relatedLocation}'");

            var location = lineInfo.AsUniversalLocation(env, Context);

            Logger.ReportMissingNamespace(
                LoggingContext,
                location.AsLoggingLocation(),
                name.ToDisplayString(Context),
                relatedMessage,
                Context.GetStackTraceAsErrorMessage(location));
        }

        /// <nodoc />
        [SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
        public void ReportContractAssert(ModuleLiteral env, Expression expression, string message, LineInfo lineInfo)
        {
            Contract.Requires(env != null);
            Contract.Requires(expression != null);

            string additionalInformation = string.IsNullOrEmpty(message)
                ? string.Empty
                : I($" : {message}");

            var location = lineInfo.AsUniversalLocation(env, Context);

            Logger.ReportContractAssert(
                LoggingContext,
                location.AsLoggingLocation(),
                expression.ToDisplayString(Context),
                additionalInformation,
                Context.GetStackTraceAsErrorMessage(location));
        }

        /// <nodoc />
        [SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
        public void ReportContractRequire(ModuleLiteral env, Expression expression, string message, LineInfo lineInfo)
        {
            Contract.Requires(env != null);
            Contract.Requires(expression != null);

            string additionalInformation = string.IsNullOrEmpty(message)
                ? string.Empty
                : I($" : {message}");

            var location = lineInfo.AsUniversalLocation(env, Context);

            Logger.ReportContractRequire(
                LoggingContext,
                location.AsLoggingLocation(),
                expression.ToDisplayString(Context),
                additionalInformation,
                Context.GetStackTraceAsErrorMessage(location));
        }

        /// <nodoc />
        public void ReportContractFail(ModuleLiteral env, string message, LineInfo lineInfo, string callStack)
        {
            var location = lineInfo.AsUniversalLocation(env, Context);

            Logger.ReportContractFail(
                LoggingContext,
                location.AsLoggingLocation(),
                message,
                callStack);
        }

        /// <nodoc />
        [SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
        public void ReportDirectoryOperationError(
            ModuleLiteral env,
            Expression expression,
            string errorInformation,
            LineInfo lineInfo)
        {
            Contract.Requires(env != null);
            Contract.Requires(expression != null);

            var location = lineInfo.AsUniversalLocation(env, Context);

            Logger.ReportDirectoryOperationError(
                LoggingContext,
                location.AsLoggingLocation(),
                expression.ToDisplayString(Context),
                errorInformation,
                Context.GetStackTraceAsErrorMessage(location));
        }

        /// <nodoc />
        [SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
        public void DirectoryNotSupportedException(
            ModuleLiteral env,
            Expression expression,
            LineInfo lineInfo)
        {
            Contract.Requires(env != null);
            Contract.Requires(expression != null);

            var location = lineInfo.AsUniversalLocation(env, Context);

            Logger.DirectoryNotSupportedException(
                LoggingContext,
                location.AsLoggingLocation(),
                expression.ToDisplayString(Context),
                Context.GetStackTraceAsErrorMessage(location));
        }

        /// <nodoc />
        [SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
        public void ReportFileOperationError(
            ModuleLiteral env,
            Expression expression,
            string errorInformation,
            LineInfo lineInfo)
        {
            Contract.Requires(env != null);
            Contract.Requires(expression != null);

            var location = lineInfo.AsUniversalLocation(env, Context);

            Logger.ReportFileOperationError(
                LoggingContext,
                location.AsLoggingLocation(),
                expression.ToDisplayString(Context),
                errorInformation,
                Context.GetStackTraceAsErrorMessage(location));
        }

        /// <nodoc />
        [SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
        public void ReportInputValidationException(ModuleLiteral env, InputValidationException inputValidationException, LineInfo lineInfo)
        {
            var location = lineInfo.AsUniversalLocation(env, Context);
            Logger.ReportInputValidationError(
                LoggingContext,
                location.AsLoggingLocation(),
                inputValidationException.ErrorContext.ToErrorString(Context),
                !string.IsNullOrEmpty(inputValidationException.Message) ? ": " + inputValidationException.Message : string.Empty,
                Context.GetStackTraceAsErrorMessage(location));
        }

        /// <nodoc />
        [SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
        public void ReportSpreadIsNotAppliedToArrayValue(ModuleLiteral env, Expression expression, LineInfo lineInfo)
        {
            Contract.Requires(env != null);
            Contract.Requires(expression != null);

            var location = lineInfo.AsUniversalLocation(env, Context);

            Logger.ReportSpreadIsNotAppliedToArrayValue(
                LoggingContext,
                location.AsLoggingLocation(),
                expression.ToDisplayString(Context),
                Context.GetStackTraceAsErrorMessage(location));
        }

        /// <nodoc />
        public void ReportUnexpectedAmbientException(ModuleLiteral env, Exception exception, LineInfo lineInfo)
        {
            Contract.Requires(env != null);
            Contract.Requires(exception != null);

            var location = lineInfo.AsUniversalLocation(env, Context);

            // TODO: print nested messages as well!!
            Logger.ReportUnexpectedAmbientException(
                LoggingContext,
                location.AsLoggingLocation(),
                exception.Message,
                Context.GetStackTraceAsErrorMessage(location));
        }

        /// <nodoc />
        [SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
        public void ReportDivideByZeroException(ModuleLiteral env, Expression expression, LineInfo lineInfo)
        {
            Contract.Requires(env != null);
            Contract.Requires(expression != null);

            var location = lineInfo.AsUniversalLocation(env, Context);
            Logger.ReportDivideByZero(
                LoggingContext,
                location.AsLoggingLocation(),
                expression.ToDisplayString(Context),
                Context.GetStackTraceAsErrorMessage(location));
        }

        /// <nodoc />
        public void ReportStackOverflow(ModuleLiteral env, LineInfo lineInfo)
        {
            Contract.Requires(env != null);

            var location = lineInfo.AsUniversalLocation(env, Context);
            Logger.ReportStackOverflow(
                LoggingContext,
                location.AsLoggingLocation(),
                Context.CallStackSize,
                ImmutableContextBase.CallStackThreshold,
                Context.GetStackTraceAsErrorMessage(location));
        }

        /// <nodoc />
        public void ReportForLoopOverflow(ModuleLiteral env, LineInfo lineInfo, int loopThreshold)
        {
            Contract.Requires(env != null);

            var location = lineInfo.AsUniversalLocation(env, Context);
            Logger.ReportForLoopOverflow(
                LoggingContext,
                location.AsLoggingLocation(),
                loopThreshold,
                Context.GetStackTraceAsErrorMessage(location));
        }

        /// <nodoc />
        public void ReportWhileLoopOverflow(ModuleLiteral env, LineInfo lineInfo, int loopThreshold)
        {
            Contract.Requires(env != null);

            var location = lineInfo.AsUniversalLocation(env, Context);
            Logger.ReportWhileLoopOverflow(
                LoggingContext,
                location.AsLoggingLocation(),
                loopThreshold,
                Context.GetStackTraceAsErrorMessage(location));
        }

        /// <nodoc />
        public void ReportCycle(ModuleLiteral env, LineInfo lineInfo)
        {
            Contract.Requires(env != null);

            var location = lineInfo.AsUniversalLocation(env, Context);
            Logger.ReportCycle(LoggingContext, location.AsLoggingLocation(),
                Context.GetStackTraceAsErrorMessage(location));
        }

        /// <nodoc />
        [SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
        public void ReportInvalidPathAtom(ModuleLiteral env, in ErrorContext errorContext, string message, LineInfo lineInfo)
        {
            Contract.Requires(env != null);

            var location = lineInfo.AsUniversalLocation(env, Context);
            Logger.ReportInvalidPathAtom(
                LoggingContext,
                location.AsLoggingLocation(),
                errorContext.ToErrorString(Context),
                !string.IsNullOrEmpty(message) ? ": " + message : string.Empty,
                Context.GetStackTraceAsErrorMessage(location));
        }

        /// <nodoc />
        [SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
        public void ReportInvalidRelativePath(ModuleLiteral env, in ErrorContext errorContext, string message, LineInfo lineInfo)
        {
            Contract.Requires(env != null);

            var location = lineInfo.AsUniversalLocation(env, Context);
            Logger.ReportInvalidPathAtom(
                LoggingContext,
                location.AsLoggingLocation(),
                errorContext.ToErrorString(Context),
                !string.IsNullOrEmpty(message) ? ": " + message : string.Empty,
                Context.GetStackTraceAsErrorMessage(location));
        }

        /// <nodoc />
        public void ReportInvalidTypeFormat(ModuleLiteral env, InvalidFormatException invalidFormatException, LineInfo lineInfo)
        {
            Contract.Requires(env != null);
            Contract.Requires(invalidFormatException != null);

            var location = lineInfo.AsUniversalLocation(env, Context);
            Logger.ReportInvalidTypeFormat(
                LoggingContext,
                location.AsLoggingLocation(),
                TypeToString(invalidFormatException.TargetType, Context),
                invalidFormatException.ErrorContext.ToErrorString(Context),
                !string.IsNullOrEmpty(invalidFormatException.Message) ? ": " + invalidFormatException.Message : string.Empty,
                Context.GetStackTraceAsErrorMessage(location));
        }

        /// <nodoc />
        public void ReportInvalidPathOperation(ModuleLiteral env, InvalidPathOperationException invalidPathOperationException, LineInfo lineInfo)
        {
            Contract.Requires(env != null);
            Contract.Requires(invalidPathOperationException != null);

            var location = lineInfo.AsUniversalLocation(env, Context);
            Logger.ReportInvalidPathOperation(
                LoggingContext,
                location.AsLoggingLocation(),
                invalidPathOperationException.ErrorContext.ToErrorString(Context),
                !string.IsNullOrEmpty(invalidPathOperationException.Message) ? ": " + invalidPathOperationException.Message : string.Empty,
                Context.GetStackTraceAsErrorMessage(location));
        }

        /// <nodoc />
        public void ReportJsonUnsuportedTypeForSerialization(ModuleLiteral env, JsonUnsuportedTypeForSerializationException exception, LineInfo lineInfo)
        {
            Contract.Requires(env != null);
            Contract.Requires(exception != null);

            var location = lineInfo.AsUniversalLocation(env, Context);
            Logger.ReportJsonUnsuportedTypeForSerialization(
                LoggingContext,
                location.AsLoggingLocation(),
                exception.EncounteredType,
                exception.ErrorContext.ToErrorString(Context),
                Context.GetStackTraceAsErrorMessage(location));
        }

        /// <nodoc />
        public void ReportJsonUnsuportedDynamicFieldsForSerialization(ModuleLiteral env, JsonUnsuportedDynamicFieldsForSerializationException exception, LineInfo lineInfo)
        {
            Contract.Requires(env != null);
            Contract.Requires(exception != null);

            var location = lineInfo.AsUniversalLocation(env, Context);
            Logger.ReportJsonUnsuportedDynamicFieldsForSerialization(
                LoggingContext,
                location.AsLoggingLocation(),
                exception.EncounteredType,
                exception.ExpectedType,
                exception.ErrorContext.ToErrorString(Context),
                Context.GetStackTraceAsErrorMessage(location));
        }

        /// <nodoc />
        public void ReportUnsupportedTypeValueObjectException(ModuleLiteral env, UnsupportedTypeValueObjectException exception, LineInfo lineInfo)
        {
            Contract.Requires(env != null);
            Contract.Requires(exception != null);

            var location = lineInfo.AsUniversalLocation(env, Context);
            Logger.ReportUnsupportedTypeValueObjectException(
                LoggingContext,
                location.AsLoggingLocation(),
                exception.EncounteredType,
                exception.ErrorContext.ToErrorString(Context),
                Context.GetStackTraceAsErrorMessage(location));
        }

        /// <nodoc />
        public void ReportXmlUnsuportedTypeForSerialization(ModuleLiteral env, XmlUnsuportedTypeForSerializationException exception, LineInfo lineInfo)
        {
            Contract.Requires(env != null);
            Contract.Requires(exception != null);

            var location = lineInfo.AsUniversalLocation(env, Context);
            Logger.ReportXmlUnsuportedTypeForSerialization(
                LoggingContext,
                location.AsLoggingLocation(),
                exception.EncounteredType,
                exception.ErrorContext.ToErrorString(Context),
                Context.GetStackTraceAsErrorMessage(location));
        }

        /// <nodoc />
        public void ReportXmlInvalidStructure(ModuleLiteral env, XmlInvalidStructureException exception, LineInfo lineInfo)
        {
            Contract.Requires(env != null);
            Contract.Requires(exception != null);

            var location = lineInfo.AsUniversalLocation(env, Context);
            Logger.ReportXmlInvalidStructure(
                LoggingContext,
                location.AsLoggingLocation(),
                exception.EncounteredType,
                exception.ExpectedType,
                exception.FieldName,
                exception.NodeType,
                exception.ErrorContext.ToErrorString(Context),
                Context.GetStackTraceAsErrorMessage(location));
        }

        /// <nodoc />
        public void ReportXmlReadError(ModuleLiteral env, XmlReadException exception, LineInfo lineInfo)
        {
            Contract.Requires(env != null);
            Contract.Requires(exception != null);

            var location = lineInfo.AsUniversalLocation(env, Context);
            Logger.ReportXmlReadError(
                LoggingContext,
                location.AsLoggingLocation(),
                exception.FilePath,
                exception.Line,
                exception.Column,
                exception.XmlErrorMessage,
                exception.ErrorContext.ToErrorString(Context),
                Context.GetStackTraceAsErrorMessage(location));
        }
        

        /// <nodoc />
        public void ReportKeyFormDllNotFound(ModuleLiteral env, KeyFormDllNotFoundException exception, LineInfo lineInfo)
        {
            Contract.Requires(env != null);
            Contract.Requires(exception != null);

            var location = lineInfo.AsUniversalLocation(env, Context);
            Logger.ReportKeyFormDllNotFound(
                LoggingContext,
                location.AsLoggingLocation(),
                exception.KeyFormDllPath,
                exception.ErrorContext.ToErrorString(Context),
                Context.GetStackTraceAsErrorMessage(location));
        }

        /// <nodoc />
        public void ReportKeyFormDllWrongFileName(ModuleLiteral env, KeyFormDllWrongFileNameException exception, LineInfo lineInfo)
        {
            Contract.Requires(env != null);
            Contract.Requires(exception != null);

            var location = lineInfo.AsUniversalLocation(env, Context);
            Logger.ReportKeyFormDllWrongFileName(
                LoggingContext,
                location.AsLoggingLocation(),
                exception.KeyFormDllPath,
                exception.ExpectedKeyFormFileName,
                exception.ErrorContext.ToErrorString(Context),
                Context.GetStackTraceAsErrorMessage(location));
        }

        /// <nodoc />
        public void ReportKeyFormDllLoad(ModuleLiteral env, KeyFormDllLoadException exception, LineInfo lineInfo)
        {
            Contract.Requires(env != null);
            Contract.Requires(exception != null);

            var location = lineInfo.AsUniversalLocation(env, Context);
            Logger.ReportKeyFormDllLoad(
                LoggingContext,
                location.AsLoggingLocation(),
                exception.KeyFormDllPath,
                exception.LastError,
                exception.ErrorMessage,
                exception.ErrorContext.ToErrorString(Context),
                Context.GetStackTraceAsErrorMessage(location));
        }

        /// <nodoc />
        public void ReportKeyFormDllLoadedWithDifferentDll(ModuleLiteral env, KeyFormDllLoadedWithDifferentDllException exception, LineInfo lineInfo)
        {
            Contract.Requires(env != null);
            Contract.Requires(exception != null);

            var location = lineInfo.AsUniversalLocation(env, Context);
            Logger.ReportKeyFormDllLoadedWithDifferentDll(
                LoggingContext,
                location.AsLoggingLocation(),
                exception.KeyFormDllPath,
                exception.OtherKeyFormDllPath,
                exception.ErrorContext.ToErrorString(Context),
                Context.GetStackTraceAsErrorMessage(location));
        }

        /// <nodoc />
        public void ReportKeyFormNativeFailure(ModuleLiteral env, KeyFormNativeFailureException exception, LineInfo lineInfo)
        {
            Contract.Requires(env != null);
            Contract.Requires(exception != null);

            var location = lineInfo.AsUniversalLocation(env, Context);
            Logger.ReportKeyFormNativeFailure(
                LoggingContext,
                location.AsLoggingLocation(),
                exception.KeyFormDllPath,
                exception.Exception.GetType().Name,
                exception.Exception.Message,
                exception.ErrorContext.ToErrorString(Context),
                Context.GetStackTraceAsErrorMessage(location));
        }

        /// <nodoc />
        [SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
        public void ReportUndefinedMapKeyException(
            ModuleLiteral env,
            in ErrorContext errorContext,
            string message,
            LineInfo lineInfo)
        {
            Contract.Requires(env != null);

            var location = lineInfo.AsUniversalLocation(env, Context);
            Logger.ReportUndefinedMapKey(
                LoggingContext,
                location.AsLoggingLocation(),
                message,
                errorContext.ToErrorString(Context),
                Context.GetStackTraceAsErrorMessage(location));
        }

        /// <nodoc />
        [SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
        public void ReportInvalidKeyValueMap(ModuleLiteral env, in ErrorContext errorContext, string error, LineInfo lineInfo)
        {
            Contract.Requires(env != null);

            var location = lineInfo.AsUniversalLocation(env, Context);
            Logger.ReportInvalidKeyValueMap(
                LoggingContext,
                location.AsLoggingLocation(),
                error,
                errorContext.ToErrorString(Context),
                Context.GetStackTraceAsErrorMessage(location));
        }

        /// <nodoc />
        [SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
        public void ReportUndefinedSetItem(
            ModuleLiteral env,
            in ErrorContext errorContext,
            string error,
            LineInfo lineInfo)
        {
            Contract.Requires(env != null);

            var location = lineInfo.AsUniversalLocation(env, Context);
            Logger.ReportUndefinedSetItem(
                LoggingContext,
                location.AsLoggingLocation(),
                error,
                errorContext.ToErrorString(Context),
                Context.GetStackTraceAsErrorMessage(location));
        }

        /// <nodoc />
        [SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
        public void ReportFileNotFoundInStaticDirectory(ModuleLiteral env, string fullPath, LineInfo lineInfo)
        {
            Contract.Requires(env != null);
            Contract.Requires(!string.IsNullOrEmpty(fullPath));

            var location = lineInfo.AsUniversalLocation(env, Context);
            Logger.ReportFileNotFoundInStaticDirectory(
                LoggingContext,
                location.AsLoggingLocation(),
                fullPath,
                Context.GetStackTraceAsErrorMessage(location));
        }

        /// <nodoc />
        public void ReportQualifierCannotBeCoarcedToQualifierSpace(
            ModuleLiteral env,
            QualifierId qualifierId,
            QualifierSpaceId qualifierSpaceId,
            LineInfo lineInfo)
        {
            Contract.Requires(env != null);
            Contract.Requires(qualifierId.IsValid);
            Contract.Requires(qualifierSpaceId.IsValid);

            Contract.Assume(Context.FrontEndContext.QualifierTable.IsValidQualifierId(qualifierId));
            Contract.Assume(Context.FrontEndContext.QualifierTable.IsValidQualifierSpaceId(qualifierSpaceId));

            Logger.ReportQualifierCannotBeCoarcedToQualifierSpace(
                LoggingContext,
                lineInfo.AsUniversalLocation(env, Context).AsLoggingLocation(),
                qualifierId.ToDisplayString(Context),
                qualifierSpaceId.ToDisplayString(Context));
        }

        /// <nodoc />
        public void ReportQualifierCannotBeCoarcedToQualifierSpaceWithProvenance(
            QualifierId qualifierId,
            QualifierSpaceId qualifierSpaceId,
            Location referencedLocation,
            Location referencingLocation)
        {
            Contract.Requires(qualifierId.IsValid);
            Contract.Requires(qualifierSpaceId.IsValid);

            Contract.Assume(Context.FrontEndContext.QualifierTable.IsValidQualifierId(qualifierId));
            Contract.Assume(Context.FrontEndContext.QualifierTable.IsValidQualifierSpaceId(qualifierSpaceId));

            Logger.ReportQualifierCannotBeCoarcedToQualifierSpaceWithProvenance(
                LoggingContext,
                referencingLocation,
                qualifierId.ToDisplayString(Context),
                qualifierSpaceId.ToDisplayString(Context),
                referencedLocation);
        }

        /// <nodoc />
        [SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
        public void ReportGetMountNameNullOrEmpty(
            ModuleLiteral env,
            LineInfo lineInfo)
        {
            Contract.Requires(env != null);

            var location = lineInfo.AsUniversalLocation(env, Context);
            Logger.ReportGetMountNameNullOrEmpty(LoggingContext, location.AsLoggingLocation());
        }

        /// <nodoc />
        [SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
        public void ReportGetMountNameNotFound(
            ModuleLiteral env,
            string name,
            IEnumerable<string> names,
            LineInfo lineInfo)
        {
            Contract.Requires(env != null);
            Contract.Requires(!string.IsNullOrEmpty(name));
            Contract.Requires(names != null);

            var location = lineInfo.AsUniversalLocation(env, Context);
            Logger.ReportGetMountNameNotFound(LoggingContext, location.AsLoggingLocation(), name, string.Join(", ", names));
        }

        /// <nodoc />
        [SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
        public void ReportGetMountNameCaseMisMatch(
            ModuleLiteral env,
            string name,
            string properName,
            LineInfo lineInfo)
        {
            Contract.Requires(env != null);
            Contract.Requires(!string.IsNullOrEmpty(name));
            Contract.Requires(!string.IsNullOrEmpty(properName));

            var location = lineInfo.AsUniversalLocation(env, Context);
            Logger.ReportGetMountNameCaseMisMatch(LoggingContext, location.AsLoggingLocation(), name, properName);
        }

        /// <nodoc />
        [SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
        public void ReportTemplateNotAvailable(
            ModuleLiteral env,
            LineInfo lineInfo)
        {
            Contract.Requires(env != null);

            var location = lineInfo.AsUniversalLocation(env, Context);
            Logger.ReportTemplateInContextNotAvailable(LoggingContext, location.AsLoggingLocation());
        }
    }
}
