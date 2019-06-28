// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
//using BuildXL.Pips;
using BuildXL.Tracing;
using BuildXL.Utilities.Instrumentation.Common;
#if FEATURE_MICROSOFT_DIAGNOSTICS_TRACING
using Microsoft.Diagnostics.Tracing;
#else
using System.Diagnostics.Tracing;
#endif

#pragma warning disable 1591
#pragma warning disable CA1823 // Unused field
#pragma warning disable SA1600 // Element must be documented

namespace BuildXL.FrontEnd.Core.Tracing
{
    /// <summary>
    /// Logging
    /// </summary>
    [EventKeywordsType(typeof(Keywords))]
    [EventTasksType(typeof(Tasks))]
    public abstract partial class Logger : LoggerBase
    {
        private bool m_preserveLogEvents;

        private readonly ConcurrentQueue<Diagnostic> m_capturedDiagnostics = new ConcurrentQueue<Diagnostic>();

        // Internal logger will prevent public users from creating an instance of the logger
        internal Logger()
        {
        }

        /// <summary>
        /// Factory method that creates instances of the logger.
        /// </summary>
        /// <param name="preserveLogEvents">When specified all logged events would be stored in the internal data structure.</param>
        public static Logger CreateLogger(bool preserveLogEvents = false)
        {
            return new LoggerImpl { m_preserveLogEvents = preserveLogEvents };
        }

        /// <summary>
        /// Provides diagnostics captured by the logger.
        /// Would be non-empty only when preserveLogEvents flag was specified in the <see cref="Logger.CreateLogger" /> factory method.
        /// </summary>
        public IReadOnlyList<Diagnostic> CapturedDiagnostics => m_capturedDiagnostics.ToList();

        /// <inheritdoc />
        public override bool InspectMessageEnabled => m_preserveLogEvents;

        /// <inheritdoc />
        protected override void InspectMessage(int logEventId, EventLevel level, string message, Location? location = null)
        {
            m_capturedDiagnostics.Enqueue(new Diagnostic(logEventId, level, message, location));
        }

        [GeneratedEvent(
            (ushort)LogEventId.FrontEndLoadConfigPhaseStart,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Message = EventConstants.PhasePrefix + "Loading configuration",
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Start,
            Keywords = (int)(Keywords.UserMessage | Keywords.Progress | Keywords.Overwritable))]
        public abstract void FrontEndLoadConfigPhaseStart(LoggingContext context);

        [GeneratedEvent(
            (ushort)LogEventId.FrontEndLoadConfigPhaseComplete,
            EventGenerators = EventGenerators.LocalAndTelemetry,
            Message =
                EventConstants.PhasePrefix +
                "Done loading {statistics.FileCount} config files. Loaded in {statistics.ElapsedMilliseconds} ms. Parsed in {statistics.ElapsedMillisecondsParse} ms. Converted in {statistics.ElapsedMillisecondsConvertion} ms.",
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Stop,
            Keywords = (int)(Keywords.UserMessage | Keywords.Performance | Keywords.Progress))]
        public abstract void FrontEndLoadConfigPhaseComplete(LoggingContext context, LoadConfigurationStatistics statistics);

        [GeneratedEvent(
            (ushort)LogEventId.FrontEndInitializeResolversPhaseStart,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Message = EventConstants.PhasePrefix + "Initializing resolvers",
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Start,
            Keywords = (int)(Keywords.UserMessage | Keywords.Progress))]
        public abstract void FrontEndInitializeResolversPhaseStart(LoggingContext context);

        [GeneratedEvent(
            (ushort)LogEventId.FrontEndInitializeResolversPhaseComplete,
            EventGenerators = EventGenerators.LocalAndTelemetry,
            Message =
                EventConstants.PhasePrefix + "Done initializing {statistics.ResolverCount} resolvers in {statistics.ElapsedMilliseconds} ms.",
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Stop,
            Keywords = (int)(Keywords.UserMessage | Keywords.Performance | Keywords.Progress))]
        public abstract void FrontEndInitializeResolversPhaseComplete(LoggingContext context, InitializeResolversStatistics statistics);

        [GeneratedEvent(
            (ushort)LogEventId.FrontEndBuildWorkspacePhaseStart,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Message = EventConstants.PhasePrefix + "Building workspace...",
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Start,
            Keywords = (int)(Keywords.UserMessage | Keywords.Progress | Keywords.Overwritable))]
        public abstract void FrontEndBuildWorkspacePhaseStart(LoggingContext context);

        [GeneratedEvent(
            (ushort)LogEventId.FrontEndBuildWorkspacePhaseProgress,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Message = EventConstants.PhasePrefix + "Building workspace progress :: Parsing: {numParseDone}/{numParseTotal}",
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Start,
            Keywords = (int)(Keywords.UserMessage | Keywords.Progress | Keywords.Overwritable))]
        public abstract void FrontEndWorkspacePhaseProgress(LoggingContext context, int numParseDone, string numParseTotal);

        [GeneratedEvent(
            (ushort)LogEventId.FrontEndBuildWorkspacePhaseComplete,
            EventGenerators = EventGenerators.LocalAndTelemetry,
            Message =
                EventConstants.PhasePrefix + "Done building workspace in {statistics.ElapsedMilliseconds} ms. Projects: {statistics.ProjectCount}, modules: {statistics.ModuleCount}.",
            EventLevel = Level.Informational,
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Stop,
            Keywords = (int)(Keywords.UserMessage | Keywords.Overwritable))]
        public abstract void FrontEndBuildWorkspacePhaseComplete(LoggingContext context, WorkspaceStatistics statistics);

        [GeneratedEvent(
            (ushort)LogEventId.FrontEndEndEvaluateValues,
            EventGenerators = EventGenerators.LocalAndTelemetry,
            Message = EventConstants.PhasePrefix + "Done evaluating values in {statistics.ElapsedMilliseconds} ms.",
            EventLevel = Level.Informational,
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Stop,
            Keywords = (int)(Keywords.UserMessage | Keywords.Overwritable))]
        public abstract void FrontEndEvaluatePhaseComplete(LoggingContext context, EvaluateStatistics statistics);

        [GeneratedEvent(
            (ushort)LogEventId.FrontEndBuildWorkspacePhaseCanceled,
            EventGenerators = EventGenerators.LocalOnly,
            Message = EventConstants.PhasePrefix + "Building workspace canceled.",
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Stop,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void FrontEndBuildWorkspacePhaseCanceled(LoggingContext context);

        [GeneratedEvent(
            (ushort)LogEventId.FrontEndWorkspaceAnalysisPhaseStart,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Message = EventConstants.PhasePrefix + "Type checking workspace...",
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Start,
            Keywords = (int)(Keywords.UserMessage | Keywords.Progress | Keywords.Overwritable))]
        public abstract void FrontEndWorkspaceAnalysisPhaseStart(LoggingContext context);

        [GeneratedEvent(
            (ushort)LogEventId.FrontEndWorkspaceAnalysisPhaseProgress,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Message = EventConstants.PhasePrefix + "Analyzing workspace progress :: Type Checking {numTypeCheckDone}/{numTypeCheckTotal}",
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Start,
            Keywords = (int)(Keywords.UserMessage | Keywords.Progress | Keywords.Overwritable))]
        public abstract void FrontEndWorkspaceAnalysisPhaseProgress(LoggingContext context, int numTypeCheckDone, int numTypeCheckTotal);

        [GeneratedEvent(
            (ushort)LogEventId.FrontEndWorkspaceAnalysisPhaseComplete,
            EventGenerators = EventGenerators.LocalAndTelemetry,
            Message =
                EventConstants.PhasePrefix + "Done type checking workspace in {statistics.ElapsedMilliseconds} ms. Projects: {statistics.ProjectCount}, modules: {statistics.ModuleCount}.",
            EventLevel = Level.Informational,
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Stop,
            Keywords = (int)(Keywords.UserMessage | Keywords.Overwritable))]
        public abstract void FrontEndWorkspaceAnalysisPhaseComplete(LoggingContext context, WorkspaceStatistics statistics);

        [GeneratedEvent(
            (ushort)LogEventId.FrontEndWorkspaceAnalysisPhaseCanceled,
            EventGenerators = EventGenerators.LocalOnly,
            Message = EventConstants.PhasePrefix + "Analyzing workspace canceled.",
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Stop,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void FrontEndWorkspaceAnalysisPhaseCanceled(LoggingContext context);

        [GeneratedEvent(
            (ushort)LogEventId.FrontEndParsePhaseStart,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Message = EventConstants.PhasePrefix + "Creating evaluation model...",
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Start,
            Keywords = (int)(Keywords.UserMessage | Keywords.Progress | Keywords.Overwritable))]
        public abstract void FrontEndParsePhaseStart(LoggingContext context);

        [GeneratedEvent(
            (ushort)LogEventId.FrontEndParsePhaseComplete,
            EventGenerators = EventGenerators.LocalAndTelemetry,
            Message = EventConstants.PhasePrefix + "Done creating evaluation model. {statistics.FileCount} files in {statistics.ElapsedMilliseconds} ms.",
            EventLevel = Level.Informational,
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Stop,
            Keywords = (int)(Keywords.UserMessage | Keywords.Overwritable))]
        public abstract void FrontEndParsePhaseComplete(LoggingContext context, ParseStatistics statistics);

        [GeneratedEvent(
            (ushort)LogEventId.FrontEndConvertPhaseStart,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Message = EventConstants.PhasePrefix + "Creating evaluation model...",
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Start,
            Keywords = (int)(Keywords.UserMessage | Keywords.Progress | Keywords.Overwritable))]
        public abstract void FrontEndConvertPhaseStart(LoggingContext context);

        [GeneratedEvent(
            (ushort)LogEventId.FrontEndConvertPhaseComplete,
            EventGenerators = EventGenerators.LocalAndTelemetry,
            Message = EventConstants.PhasePrefix + "Done creating evaluation model in {statistics.ElapsedMilliseconds} ms. Projects: {statistics.FileCount}, modules: {statistics.ModuleCount}.",
            EventLevel = Level.Informational,
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Stop,
            Keywords = (int)(Keywords.UserMessage | Keywords.Overwritable))]
        public abstract void FrontEndConvertPhaseComplete(LoggingContext context, ParseStatistics statistics);

        [GeneratedEvent(
            (ushort)LogEventId.FrontEndConvertPhaseCanceled,
            EventGenerators = EventGenerators.LocalOnly,
            Message = EventConstants.PhasePrefix + "Conversion canceled.",
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Stop,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void FrontEndConvertPhaseCanceled(LoggingContext context);

        [GeneratedEvent(
            (ushort)LogEventId.FrontEndEvaluatePhaseCanceled,
            EventGenerators = EventGenerators.LocalOnly,
            Message = EventConstants.PhasePrefix + "Evaluation canceled.",
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Stop,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void FrontEndEvaluatePhaseCanceled(LoggingContext context);

        [GeneratedEvent(
            (ushort)LogEventId.FrontEndConvertPhaseProgress,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Message = EventConstants.PhasePrefix + "Conversion progress :: Specs: {numSpecsDone}/{numSpecsTotal}.",
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Start,
            Keywords = (int)(Keywords.UserMessage | Keywords.Progress | Keywords.Overwritable))]
        public abstract void FrontEndConvertPhaseProgress(LoggingContext context, int numSpecsDone, int numSpecsTotal);

        [GeneratedEvent(
            (ushort)LogEventId.FrontEndEvaluateValuesProgress,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Message = EventConstants.PhasePrefix + "Evaluation progress :: Modules: {numModulesDone}/{numModulesTotal} :: Specs: {numSpecsDone}/{numSpecsTotal} :: Remaining modules: {remaining}",
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Start,
            Keywords = (int)(Keywords.UserMessage | Keywords.Progress | Keywords.Overwritable))]
        public abstract void FrontEndEvaluatePhaseProgress(LoggingContext context, int numModulesDone, int numModulesTotal, int numSpecsDone, int numSpecsTotal, string remaining);

        [GeneratedEvent(
            (ushort)LogEventId.FrontEndEvaluateFragmentsProgress,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Message = Events.PhasePrefix + "Evaluation progress :: Fragments: {numFragmentsDone}/{numFragmentsTotal} :: Remaining fragments: {remaining}",
            EventTask = (ushort)Events.Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Start,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.Progress | Events.Keywords.Overwritable))]
        public abstract void FrontEndEvaluatePhaseFragmentProgress(LoggingContext context, int numFragmentsDone, int numFragmentsTotal, string remaining);

        [GeneratedEvent(
            (ushort)LogEventId.FrontEndStartEvaluateValues,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Message = EventConstants.PhasePrefix + "Evaluating values...",
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)EventOpcode.Start,
            Keywords = (int)(Keywords.UserMessage | Keywords.Progress | Keywords.Overwritable))]
        public abstract void FrontEndEvaluatePhaseStart(LoggingContext context);

        [GeneratedEvent(
            (ushort)LogEventId.ErrorNotFoundNamedQualifier,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message =
                "Qualifier '{notFoundNamedQualifier.RequestedNamedQualifier}' does not exist. Available named qualifiers are '{notFoundNamedQualifier.AvailableNamedQualifiers}'.",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ErrorNotFoundNamedQualifier(LoggingContext context, NotFoundNamedQualifier notFoundNamedQualifier);

        [GeneratedEvent(
            (ushort)LogEventId.ErrorNonExistenceNamedQualifier,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message =
                "Unable to build with qualifier '{nonExistenceNamedQualifier.RequestedNamedQualifier}' because no named qualifiers exist in the configuration.",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ErrorNonExistenceNamedQualifier(LoggingContext context, NonExistenceNamedQualifier nonExistenceNamedQualifier);

        [GeneratedEvent(
            (ushort)LogEventId.ErrorIllFormedQualfierExpresion,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message =
                "Unable to build with qualifier '{requestedQualifierExpression}', the part '{requestedQualifierPart}' is ill-formed. The format is either /q:qualifierName where qualifierName must be in the main BuildXL configuraiton file, or /q:name1=value1;name2=value2",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ErrorIllFormedQualfierExpresion(LoggingContext context, string requestedQualifierExpression, string requestedQualifierPart);

        [GeneratedEvent(
            (ushort)LogEventId.ErrorNoQualifierValues,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message =
                "Unable to build with qualifier '{requestedQualifierExpression}'. After combining all values, the qualifier has no values in it.",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ErrorNoQualifierValues(LoggingContext context, string requestedQualifierExpression);

        [GeneratedEvent(
            (ushort)LogEventId.ErrorNamedQualifierNoValues,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message =
                "Invalid configuration. Named qualifier '{name}' has no fields defined.",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ErrorNamedQualifierNoValues(LoggingContext context, string name);

        [GeneratedEvent(
            (ushort)LogEventId.ErrorNamedQualifierInvalidKey,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message =
                "Invalid configuration. Named qualifier '{name}' has an invalid key '{key}' with value '{value}'. The key must be a valid {ShortScriptName} identifier.",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ErrorNamedQualifierInvalidKey(LoggingContext context, string name, string key, string value);

        [GeneratedEvent(
            (ushort)LogEventId.ErrorNamedQualifierInvalidValue,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message =
                "Invalid configuration. Named qualifier '{name}' has an invalid value '{value}' for key '{key}'. The value may not contain ';' or '='.",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ErrorNamedQualifierInvalidValue(LoggingContext context, string name, string key, string value);

        [GeneratedEvent(
            (ushort)LogEventId.ErrorDefaultQualiferInvalidKey,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message =
                "Invalid configuration. The default qualifier has an invalid key '{key}' with value '{value}'. The key must be a valid {ShortScriptName} identifier.",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ErrorDefaultQualiferInvalidKey(LoggingContext context, string key, string value);

        [GeneratedEvent(
            (ushort)LogEventId.ErrorDefaultQualifierInvalidValue,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message =
                "Invalid configuration. The DefaultQualifier has an invalid value '{value}' for key '{key}'. The value may not contain ';' or '='.",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ErrorDefaultQualifierInvalidValue(LoggingContext context, string key, string value);

    [GeneratedEvent(
            (ushort)LogEventId.ErrorEmptyQualfierExpresion,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message =
                "Unable to build with qualifier '{requestedQualifierExpression}', it contains an empty key value pair. The format is either /q:qualifierName where qualifierName must be in the main BuildXL configuraiton file, or /q:name1=value1;name2=value2",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ErrorEmptyQualfierExpresion(LoggingContext context, string requestedQualifierExpression);

        [GeneratedEvent(
            (ushort)LogEventId.UnregisteredResolverKind,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = "Resolver kind '{frontEndKind}' is not registered; registered resolvers are {registeredResolvers}.",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void UnregisteredResolverKind(LoggingContext context, string frontEndKind, string registeredResolvers);

        [GeneratedEvent(
            (ushort)LogEventId.UnableToFindFrontEndToParse,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message =
                "Unable to find a front-end to parse '{path}'; the file may not be owned by any package or the specified resolvers in the configuration file cannot find the package owning the file.",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void UnableToFindFrontEndToParse(LoggingContext context, string path);

        [GeneratedEvent(
            (ushort)LogEventId.UnableToFindFrontEndToEvaluate,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message =
                "Unable to find a front-end to evaluate '{path}'; the file may not be owned by any package or the specified resolvers in the configuration file cannot find the package owning the file.",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void UnableToFindFrontEndToEvaluate(LoggingContext context, string path);

        [GeneratedEvent(
            (ushort)LogEventId.UnableToFindFrontEndToAnalyze,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message =
                "Unable to find a front-end to analyze '{path}'; the file may not be owned by any package or the specified resolvers in the configuration file cannot find the package owning the file.",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void UnableToFindFrontEndToAnalyze(LoggingContext context, string path);

        [GeneratedEvent(
            (ushort)LogEventId.FailedToConvertModuleToEvaluationModel,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message =
                "Unable to convert module '{moduleName}': {fullError}",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void FailedToConvertModuleToEvaluationModel(LoggingContext context, string moduleName, string fullError);

        [GeneratedEvent(
            (ushort)LogEventId.PrimaryConfigFileNotFound,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = "Primary configuration file 'config.dsc' is not found. The configuration file passed as the value for /c or /config options must be 'config.dsc'.",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void PrimaryConfigFileNotFound(LoggingContext context);

        [GeneratedEvent(
            (ushort)LogEventId.StartDownloadingTool,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Parser,
            EventOpcode = (byte)EventOpcode.Start,
            Message = "-- Downloading tool '{toolName}' from '{url}' to '{targetLocation}'")]
        internal abstract void StartDownloadingTool(LoggingContext loggingContext, string toolName, string url, string targetLocation);

        [GeneratedEvent(
            (ushort)LogEventId.EndDownloadingTool,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)(Keywords.UserMessage | Keywords.Performance),
            EventTask = (ushort)Tasks.Parser,
            EventOpcode = (byte)EventOpcode.Stop,
            Message = "-- Done downloading tool '{toolName}'.")]
        internal abstract void EndDownloadingTool(LoggingContext loggingContext, string toolName);

        [GeneratedEvent(
            (ushort)LogEventId.DownloadToolFailedToHashExisting,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Parser,
            Message =
                "Tool '{toolName}' will be downloaded again because there was a failure computing the hash of file '{targetFilePath}' to validate if we need to download again: {message}")]
        public abstract void DownloadToolFailedToHashExisting(LoggingContext loggingContext, string toolName, string targetFilePath, string message);

        [GeneratedEvent(
            (ushort)LogEventId.DownloadToolErrorInvalidUri,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (ushort)Tasks.Parser,
            Message = "Tool '{toolName}' failed to download because '{url}' is not a valid uri")]
        public abstract void DownloadToolErrorInvalidUri(LoggingContext loggingContext, string toolName, string url);

        [GeneratedEvent(
            (ushort)LogEventId.DownloadToolErrorCopyFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.InfrastructureError),
            EventTask = (ushort)Tasks.Parser,
            Message = "Tool '{toolName}' failed to download because an error occurred copying from '{url}' to '{targetFilePath}': {message}")]
        public abstract void DownloadToolErrorCopyFile(
            LoggingContext loggingContext,
            string toolName,
            string url,
            string targetFilePath,
            string message);

        [GeneratedEvent(
            (ushort)LogEventId.DownloadToolFailedDueToCacheError,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Parser,
            Message = "Tool '{toolName}' will be downloaded again because there was a failure initializing the cache: {message}")]
        public abstract void DownloadToolFailedDueToCacheError(LoggingContext loggingContext, string toolName, string message);

        [GeneratedEvent(
            (ushort)LogEventId.DownloadToolIsUpToDate,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Parser,
            Message = "Tool '{toolName}' is up to date and will not be downloaded because file '{targetFilePath}' has the expected hash '{hash}'.")]
        public abstract void DownloadToolIsUpToDate(LoggingContext loggingContext, string toolName, string targetFilePath, string hash);

        [GeneratedEvent(
            (ushort)LogEventId.DownloadToolIsRetrievedFromCache,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Parser,
            Message =
                "Tool '{toolName}' is restored from cache and will not be downloaded because file '{targetFilePath}' has the expected hash '{hash}'.")
        ]
        public abstract void DownloadToolIsRetrievedFromCache(LoggingContext loggingContext, string toolName, string targetFilePath, string hash);

        [GeneratedEvent(
            (ushort)LogEventId.DownloadToolErrorDownloading,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.InfrastructureError),
            EventTask = (ushort)Tasks.Parser,
            Message = "Tool '{toolName}' failed to download from url: '{url}' to target location '{targetFilePath}' with error: {message}")]
        public abstract void DownloadToolErrorDownloading(
            LoggingContext loggingContext,
            string toolName,
            string url,
            string targetFilePath,
            string message);

        [GeneratedEvent(
            (ushort)LogEventId.DownloadToolErrorFileNotDownloaded,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.InfrastructureError),
            EventTask = (ushort)Tasks.Parser,
            Message = "Tool '{toolName}' failed to download. No file was produced at target location '{targetFilePath}'")]
        public abstract void DownloadToolErrorFileNotDownloaded(LoggingContext loggingContext, string toolName, string targetFilePath);

        [GeneratedEvent(
            (ushort)LogEventId.DownloadToolErrorDownloadedToolWrongHash,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.InfrastructureError),
            EventTask = (ushort)Tasks.Parser,
            Message =
                "Tool '{toolName}' failed to validate. The configuration specified required hash '{expectedHash}', but the file '{targetFilePath}' downloaded from '{url}' has hash '{actualHash}'. For safety reasons we will not continue the build. You must update the config and/or validate the that the server providing the file is not compromised.")]
        public abstract void DownloadToolErrorDownloadedToolWrongHash(
            LoggingContext loggingContext,
            string toolName,
            string targetFilePath,
            string url,
            string expectedHash,
            string actualHash);

        [GeneratedEvent(
            (ushort)LogEventId.DownloadToolCannotCache,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Parser,
            Message = "Tool '{toolName}' failed to store file '{targetFilePath}' successfully in the cache with has '{contentHash}': {message}")]
        public abstract void DownloadToolCannotCache(
            LoggingContext loggingContext,
            string toolName,
            string targetFilePath,
            string url,
            string contentHash,
            string message);

        [GeneratedEvent(
            (ushort)LogEventId.DownloadToolWarnCouldntHashDownloadedFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Parser,
            Message = "Tool '{toolName}' will be downloaded again in a future because there was an error hasing file '{targetFilePath}': {message}")]
        public abstract void DownloadToolWarnCouldntHashDownloadedFile(
            LoggingContext loggingContext,
            string toolName,
            string targetFilePath,
            string message);

        [GeneratedEvent(
            (ushort)LogEventId.StartRetrievingPackage,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)(Keywords.Diagnostics | Keywords.Performance),
            EventTask = (ushort)Tasks.Parser,
            EventOpcode = (byte)EventOpcode.Start,
            Message = "-- Checking if package '{friendlyPackageName}' is cached or needs to be downloaded from '{targetLocation}'")]
        internal abstract void StartRetrievingPackage(LoggingContext loggingContext, string friendlyPackageName, string targetLocation);

        [GeneratedEvent(
            (ushort)LogEventId.EndRetrievingPackage,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)(Keywords.Diagnostics | Keywords.Performance),
            EventTask = (ushort)Tasks.Parser,
            EventOpcode = (byte)EventOpcode.Stop,
            Message = "-- Done retrieving package {friendlyPackageName}.")]
        internal abstract void EndRetrievingPackage(LoggingContext loggingContext, string friendlyPackageName);

        [GeneratedEvent(
            (ushort)LogEventId.DownloadPackageFailedDueToCacheError,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Parser,
            Message = "Package '{friendlyPackageName}' will be downloaded again because there was a failure initializing the cache: {message}")]
        public abstract void DownloadPackageFailedDueToCacheError(LoggingContext loggingContext, string friendlyPackageName, string message);

        [GeneratedEvent(
            (ushort)LogEventId.CanNotRestorePackagesDueToCacheError,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.InfrastructureError),
            EventTask = (ushort)Tasks.Parser,
            Message = "Can't restore NuGet packages because there was a failure initializing the cache: {message}")]
        public abstract void CanNotRestorePackagesDueToCacheError(LoggingContext loggingContext, string message);

        [GeneratedEvent(
            (ushort)LogEventId.DownloadPackageFailedDueToInvalidCacheContents,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Parser,
            Message = "Package '{friendlyPackageName}' will be downloaded again because there was an invalid entry in the cache.{additionalInfo}")]
        public abstract void DownloadPackageFailedDueToInvalidCacheContents(LoggingContext loggingContext, string friendlyPackageName, string additionalInfo = null);

        [GeneratedEvent(
            (ushort)LogEventId.DownloadPackageCannotCacheError,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Parser,
            Message =
                "Package '{friendlyPackageName}' failed to store file '{targetLocation}' successfully in the cache with hash '{contentHash}': {message}. This is an error when /forcePopulatePackageCache is enabled.")]
        public abstract void DownloadPackageCannotCacheError(
            LoggingContext loggingContext,
            string friendlyPackageName,
            string targetLocation,
            string contentHash,
            string message);

        [GeneratedEvent(
            (ushort)LogEventId.DownloadPackageCannotCacheWarning,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Parser,
            Message =
                "Package '{friendlyPackageName}' failed to store file '{targetLocation}' successfully in the cache with hash '{contentHash}': {message}")]
        public abstract void DownloadPackageCannotCacheWarning(
            LoggingContext loggingContext,
            string friendlyPackageName,
            string targetLocation,
            string contentHash,
            string message);

        [GeneratedEvent(
            (ushort)LogEventId.DownloadPackageCouldntHashPackageFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Parser,
            Message = "Package '{friendlyPackageName}' failed to be stored in the cache because file '{targetLocation}' couldn't be hashed: {message}")]
        public abstract void DownloadPackageCouldntHashPackageFile(
            LoggingContext loggingContext,
            string friendlyPackageName,
            string targetLocation,
            string message);

        [GeneratedEvent(
            (ushort)LogEventId.PackageRestoredFromCache,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Parser,
            Message = "Package {package} has been restored from the cache.")]
        public abstract void PackageRestoredFromCache(LoggingContext context, string package);

        [GeneratedEvent(
            (ushort)LogEventId.PackagePresumedUpToDate,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Parser,
            Message = "Package {package} was presumed up to date on disk because of fingerprints match.")]
        public abstract void PackagePresumedUpToDate(LoggingContext context, string package);

        [GeneratedEvent(
            (ushort)LogEventId.PackagePresumedUpToDateWithoutHashComparison,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Parser,
            Message = "Package {package} was presumed up to date on disk without comparing the fingerprints.")]
        public abstract void PackagePresumedUpToDateWithoutHashComparison(LoggingContext context, string package);

        [GeneratedEvent(
            (ushort)LogEventId.CanNotReusePackageHashFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Parser,
            Message = "Package hash file '{file}' can't be reused. {message}")]
        public abstract void CanNotReusePackageHashFile(LoggingContext context, string file, string message);

        [GeneratedEvent(
            (ushort)LogEventId.CanNotUpdatePackageHashFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Parser,
            Message = "Failed to update a package hash file '{file}'. {message}")]
        public abstract void CanNotUpdatePackageHashFile(LoggingContext context, string file, string message);

        [GeneratedEvent(
            (ushort)LogEventId.PackageNotFoundInCacheAndStartedDownloading,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Parser,
            Message = "Package nuget://{id}/{version} was not found in the cache and started downloading.")]
        public abstract void PackageNotFoundInCacheAndStartedDownloading(LoggingContext context, string id, string version);

        [GeneratedEvent(
            (ushort)LogEventId.PackageNotFoundInCacheAndDownloaded,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Parser,
            Message = "Package nuget://{id}/{version} finished downloading. \r\n{packageHash}\r\n{packageFingerprint}")]
        public abstract void PackageNotFoundInCacheAndDownloaded(LoggingContext context, string id, string version, string packageHash, string packageFingerprint);

        [GeneratedEvent(
            (ushort)LogEventId.PackageCacheMissInformation,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Parser,
            Message = "Package nuget://{id}/{version} was not the same as the previous version: \r\nOriginal\r\n{originalFingerprint}\r\nNew\r\n{newFingerprint}")]
        public abstract void PackageCacheMissInformation(LoggingContext context, string id, string version, string originalFingerprint, string newFingerprint);

        [GeneratedEvent(
            (ushort)LogEventId.CannotBuildWorkspace,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = "{message}",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void CannotBuildWorkspace(LoggingContext context, string message);

        [GeneratedEvent(
            (ushort)LogEventId.CheckerError,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            Message = EventConstants.LabeledProvenancePrefix + "{error}")]
        public abstract void CheckerError(LoggingContext context, Location location, string error);

        [GeneratedEvent(
            (ushort)LogEventId.CheckerGlobalError,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Keywords = (int)Keywords.UserMessage,
            Message = "{error}")]
        public abstract void CheckerGlobalError(LoggingContext context, string error);

        [GeneratedEvent(
            (ushort)LogEventId.CheckerWarning,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            EventTask = (ushort)Tasks.Parser,
            Keywords = (int)Keywords.UserMessage,
            Message = EventConstants.LabeledProvenancePrefix + "{message}")]
        public abstract void CheckerWarning(LoggingContext context, Location location, string message);

        [GeneratedEvent(
            (ushort)LogEventId.CheckerGlobalWarning,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            EventTask = (ushort)Tasks.Parser,
            Keywords = (int)Keywords.UserMessage,
            Message = "{message}")]
        public abstract void CheckerGlobalWarning(LoggingContext context, string message);

        /// <summary>
        /// Generic typescript syntax error. All TS parser errors are routed here
        /// </summary>
        [GeneratedEvent(
            (ushort)LogEventId.TypeScriptSyntaxError,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "{message}",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void SyntaxError(LoggingContext context, Location location, string message);

        [GeneratedEvent(
            (ushort)LogEventId.TypeScriptLocalBindingError,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "{message}",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void LocalBindingError(LoggingContext context, Location location, string message);

        [GeneratedEvent(
            (ushort)LogEventId.MaterializingFileToFileDepdencyMap,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.LogAlways,
            EventTask = (ushort)Tasks.HostApplication,
            Message = EventConstants.PhasePrefix + "Writing spec-to-spec dependency map to '{destination}'.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void MaterializingFileToFileDepdencyMap(LoggingContext context, string destination);

        [GeneratedEvent(
            (ushort)LogEventId.ErrorMaterializingFileToFileDepdencyMap,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.LogAlways,
            EventTask = (ushort)Tasks.HostApplication,
            Message = EventConstants.PhasePrefix + "Spec-to-spec dependency map could not be written. Error code {errorCode:X8}: {message}.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ErrorMaterializingFileToFileDepdencyMap(LoggingContext context, int errorCode, string message);

        [GeneratedEvent(
            (ushort)LogEventId.GraphPartiallyReloaded,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            EventTask = (ushort)Tasks.Parser,
            Message = "Pip graph partially reloaded in {elapsedMillis}ms: #affected/total specs = {numAffectedSpecs}/{numTotalSpecs}, #reloaded pips = {numReloaded} + {numAutoAdded}, #skipped pips = {numSkipped} + {numNotReloadable}.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void GraphPartiallyReloaded(LoggingContext context, int numAffectedSpecs, int numTotalSpecs, int elapsedMillis, int numReloaded, int numAutoAdded, int numSkipped, int numNotReloadable);

        [GeneratedEvent(
            (ushort)LogEventId.GraphPatchingDetails,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Parser,
            Message = "Graph patching details: {details}",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void GraphPatchingDetails(LoggingContext context, string details);

        [GeneratedEvent(
            (ushort)LogEventId.SaveFrontEndSnapshot,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.HostApplication,
            Message = "FrontEnd snapshot was saved to {path} in {duration}ms.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void SaveFrontEndSnapshot(LoggingContext context, string path, int duration);

        [GeneratedEvent(
            (ushort)LogEventId.LoadFrontEndSnapshot,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.HostApplication,
            Message = "Loaded the front end snapshot for {specCount} specs in {duration}ms.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void LoadFrontEndSnapshot(LoggingContext context, int specCount, int duration);

        [GeneratedEvent(
            (ushort)LogEventId.SaveFrontEndSnapshotError,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            EventTask = (ushort)Tasks.HostApplication,
            Message = "FrontEnd snapshot could not be written to {path}: {message}.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void SaveFrontEndSnapshotError(LoggingContext context, string path, string message);

        [GeneratedEvent(
            (ushort)LogEventId.FailToReuseFrontEndSnapshot,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.HostApplication,
            Message = "Failed to reuse front end cache: {reason}.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void FailToReuseFrontEndSnapshot(LoggingContext context, string reason);

        [GeneratedEvent(
            (ushort)LogEventId.FailedToFilterWorkspaceDefinition,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.HostApplication,
            Message = "Failed to filter workspace definition: {reason}.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void FailedToFilterWorkspaceDefinition(LoggingContext context, string reason);

        [GeneratedEvent(
            (ushort)LogEventId.TryingToReuseFrontEndSnapshot,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.HostApplication,
            Message = "Trying to reuse a front-end snapshot...",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void TryingToReuseFrontEndSnapshot(LoggingContext context);

        [GeneratedEvent(
            (ushort)LogEventId.BuildingFullWorkspace,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.HostApplication,
            Message = "Building the full workspace...",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void BuildingFullWorkspace(LoggingContext context);

        [GeneratedEvent(
            (ushort)LogEventId.WorkspaceDefinitionCreated,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.HostApplication,
            Message = "Built workspace definition with {moduleCount} modules, {specCount} specs and {configSpecCount} configuration specs in {duration}ms.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void WorkspaceDefinitionCreated(LoggingContext context, int moduleCount, int specCount, int configSpecCount, int duration);

        [GeneratedEvent(
            (ushort)LogEventId.WorkspaceDefinitionFiltered,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.HostApplication,
            Message = "Applied user-defined filter based on reused spec-2-spec information in {duration}ms. Remaining spec count: {filteredCount}. Original spec count: {originalCount}.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void WorkspaceDefinitionFiltered(LoggingContext context, int filteredCount, int originalCount, int duration);

        [GeneratedEvent(
            (ushort)LogEventId.WorkspaceDefinitionFilteredBasedOnModuleFilter,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.HostApplication,
            Message = "Applied user-defined module filter in {duration}ms. Filtered out {moduleCount} modules with {specCount} specs. Original module count: {originalCount}. Final module count: {filteredCount}.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void WorkspaceDefinitionFilteredBasedOnModuleFilter(LoggingContext context, int moduleCount, int specCount, int originalCount, int filteredCount, int duration);

        [GeneratedEvent(
            (ushort)LogEventId.WorkspaceFiltered,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.HostApplication,
            Message = "Applied user-defined filter based on spec-2-spec information in {duration}ms. Remaining spec count: {filteredCount}. Original spec count: {originalCount}.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void WorkspaceFiltered(LoggingContext context, int filteredCount, int originalCount, int duration);

        [GeneratedEvent(
            (ushort)LogEventId.CycleDetectionStatistics,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Parser,
            Message = "  cycle detection: {0} threads created, {1} chains added, {2} removed before processing, {3} abandoned while processing, {4} removed after processing.",
            Keywords = (int)Keywords.Performance | (int)Keywords.UserMessage)]
        public abstract void CycleDetectionStatistics(LoggingContext context, long threadsCreated, long chainsAdded, long chainsRemovedBeforeProcessing, long chainsAbandonedWhileProcessing, long chainsRemovedAfterProcessing);

        [GeneratedEvent(
            (ushort)LogEventId.SlowestScriptElements,
            EventGenerators = EventGenerators.LocalAndTelemetry,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Parser,
            Message = "Slowest {ShortScriptName} elements by phase:\r\n    Parse:{parse}\r\n    Bind:{bind}\r\n    Type check:{typeCheck}\r\n    AST Conversion:{astConversion}\r\n    Facade computation:{facadeComputation}\r\n    Compute Fingerprint:{computeFingerprint}\r\n    Evaluation:{evaluation}\r\n    Prelude Processing:{preludeProcessing}",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void SlowestScriptElements(LoggingContext context, string parse, string bind, string typeCheck, string astConversion, string facadeComputation, string computeFingerprint, string evaluation, string preludeProcessing);

        [GeneratedEvent(
            (ushort)LogEventId.LargestScriptFiles,
            EventGenerators = EventGenerators.LocalAndTelemetry,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Parser,
            Message = "Largest {ShortScriptName} files:\r\n    By #identifiers:{byIdentifierCount}\r\n    By #lines:{byLineCount}\r\n    By #chars:{byCharCount}\r\n    By #nodes:{byNodeCount}\r\n    By #symbols:{bySymbolCount}",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void LargestScriptFiles(LoggingContext context, string byIdentifierCount, string byLineCount, string byCharCount, string byNodeCount, string bySymbolCount);

        [GeneratedEvent(
            (ushort)LogEventId.ScriptFilesReloadedWithNoWarningsOrErrors,
            EventGenerators = EventGenerators.LocalAndTelemetry,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = "{reloadedSpecCount} spec(s) were reloaded during {ShortProductName} invocation but no error or warning was logged. This behavior could drasitcally compromise system's performance and should be fixed. Stack trace that triggered file reloading: \r\n{stackTrace}",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ScriptFilesReloadedWithNoWarningsOrErrors(LoggingContext context, int reloadedSpecCount, string stackTrace);

        [GeneratedEvent(
            (ushort)LogEventId.ReportDestructionCone,
            EventGenerators = EventGenerators.LocalAndTelemetry,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Parser,
            Message = "Destruction cone (changed/affected/required/all specs): {numChanged}/{numAffected}/{numRequired}/{numAll}",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportDestructionCone(LoggingContext context, int numChanged, int numAffected, int numRequired, int numAll);
    }

    /// <summary>
    /// Empty struct for functions that have no arguments
    /// </summary>
    public readonly struct EmptyStruct
    {
    }

    /// <nodoc />
    [SuppressMessage("Microsoft.Performance", "CA1815")]
    public struct LoadConfigurationStatistics : IHasEndTime
    {
        /// <nodoc />
        public int FileCount;

        /// <inheritdoc />
        public int ElapsedMilliseconds { get; set; }

        /// <nodoc />
        public int ElapsedMillisecondsParse { get; set; }

        /// <nodoc />
        public int ElapsedMillisecondsConvertion { get; set; }
    }

    /// <summary>
    /// Statistics about the parse phase
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815")]
    public struct ParseStatistics : IHasEndTime
    {
        /// <nodoc />
        public int FileCount;

        /// <nodoc />
        public int ModuleCount;

        /// <inheritdoc />
        public int ElapsedMilliseconds { get; set; }
    }

    /// <summary>
    /// Statistics about the workspace
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815")]
    public struct WorkspaceStatistics : IHasEndTime
    {
        /// <inheritdoc />
        public int ElapsedMilliseconds { get; set; }

        /// <nodoc />
        public int ProjectCount { get; set; }

        /// <nodoc />
        public int ModuleCount { get; set; }
    }

    /// <nodoc />
    [SuppressMessage("Microsoft.Performance", "CA1815")]
    public struct EvaluateStatistics : IHasEndTime
    {
        /// <inheritdoc />
        public int ElapsedMilliseconds { get; set; }
    }

    /// <nodoc />
    [SuppressMessage("Microsoft.Performance", "CA1815")]
    public struct InitializeResolversStatistics : IHasEndTime
    {
        /// <inheritdoc />
        public int ElapsedMilliseconds { get; set; }

        /// <nodoc />
        public long ResolverCount { get; set; }
    }

    /// <summary>
    /// Represents an event forwarded from a worker
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815")]
    public struct NotFoundNamedQualifier
    {
        /// <summary>
        /// Qualifier name.
        /// </summary>
        public string RequestedNamedQualifier { get; set; }

        /// <summary>
        /// Available named qualifiers.
        /// </summary>
        public string AvailableNamedQualifiers { get; set; }
    }

    /// <summary>
    /// Represents an event forwarded from a worker
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815")]
    public struct NonExistenceNamedQualifier
    {
        /// <nodoc />
        public string RequestedNamedQualifier { get; set; }
    }
}
#pragma warning restore CA1823 // Unused field
#pragma warning restore SA1600 // Element must be documented
