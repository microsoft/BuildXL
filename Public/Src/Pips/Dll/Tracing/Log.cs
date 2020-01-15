// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using BuildXL.Tracing;
using BuildXL.Utilities.Instrumentation.Common;

#pragma warning disable 1591
#pragma warning disable CA1823 // Unused field

namespace BuildXL.Pips.Tracing
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

        internal Logger()
        {
        }

        /// <summary>
        /// Factory method that creates instances of the logger.
        /// </summary>
        /// <param name="preserveLogEvents">When specified all logged events would be stored in the internal data structure.</param>
        public static Logger CreateLogger(bool preserveLogEvents = false)
        {
            return new LoggerImpl() { m_preserveLogEvents = preserveLogEvents };
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

        /// <summary>
        /// Returns the logger instance
        /// </summary>
        public static Logger Log => m_log;

        [GeneratedEvent(
            (ushort)LogEventId.DeserializationStatsPipGraphFragment,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Engine,
            Message = "Deserialization stats of graph fragment '{fragmentDescription}': {stats}")]
        public abstract void DeserializationStatsPipGraphFragment(LoggingContext context, string fragmentDescription, string stats);

        [GeneratedEvent(
            (ushort)LogEventId.ExceptionOnDeserializingPipGraphFragment,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Engine,
            Message = "An exception occured during deserialization of pip graph fragment '{path}': {exceptionMessage}")]
        public abstract void ExceptionOnDeserializingPipGraphFragment(LoggingContext context, string path, string exceptionMessage);


        [GeneratedEvent(
            (ushort)LogEventId.FailedToAddFragmentPipToGraph,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (ushort)Tasks.Engine,
            Message = "[{pipDescription}] Unable to add the pip from fragment '{fragmentName}'.")]
        public abstract void FailedToAddFragmentPipToGraph(LoggingContext context, string fragmentName, string pipDescription);

        [GeneratedEvent(
            (ushort)LogEventId.ExceptionOnAddingFragmentPipToGraph,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Engine,
            Message = "[{pipDescription}] An exception occured when adding the pip from fragment '{fragmentName}': {exceptionMessage}")]
        public abstract void ExceptionOnAddingFragmentPipToGraph(LoggingContext context, string fragmentName, string pipDescription, string exceptionMessage);

        [GeneratedEvent(
            (int)LogEventId.PerformanceDataCacheTrace,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Storage,
            Message = "{message}")]
        public abstract void PerformanceDataCacheTrace(LoggingContext context, string message);
        
        [GeneratedEvent(
              (int)LogEventId.InvalidGraphSinceArtifactPathOverlapsTempPath,
              EventGenerators = EventGenerators.LocalOnly,
              EventLevel = Level.Error,
              Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
              EventTask = (int)Tasks.Scheduler,
              Message = "Invalid temp path declared. Pips cannot declare artifacts underneath declared temp directories.\r\n{artifactLocation.File}({artifactLocation.Line},{artifactLocation.Position}): [{artifactProducerPip}] declared '{artifactPath}' as an artifact path.\r\n{tempLocation.File}({tempLocation.Line},{tempLocation.Position}): [{tempProducerPip}] declared '{tempPath}' as a temp path.")]
        public abstract void InvalidGraphSinceArtifactPathOverlapsTempPath(
              LoggingContext context,
              Location tempLocation,
              string tempPath,
              string tempProducerPip,
              Location artifactLocation,
              string artifactPath,
              string artifactProducerPip);

        [GeneratedEvent(
           (int)LogEventId.InvalidGraphSinceOutputDirectoryCoincidesSealedDirectory,
           EventGenerators = EventGenerators.LocalOnly,
           EventLevel = Level.Error,
           Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
           EventTask = (int)Tasks.Scheduler,
           Message =
               EventConstants.ProvenancePrefix +
               "Invalid graph since '{outputDirectory}', produced by '{outputDirectoryProducerDescription}', coincides with the sealed directory '{sealedDirectory}', produced by '{sealedDirectoryProducerDescription}'.")]
        public abstract void ScheduleFailInvalidGraphSinceOutputDirectoryCoincidesSealedDirectory(
           LoggingContext context,
           string file,
           int line,
           int column,
           string outputDirectory,
           string outputDirectoryProducerDescription,
           string sealedDirectory,
           string sealedDirectoryProducerDescription);

        [GeneratedEvent(
           (int)LogEventId.InvalidGraphSinceOutputDirectoryContainsSealedDirectory,
           EventGenerators = EventGenerators.LocalOnly,
           EventLevel = Level.Error,
           Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
           EventTask = (int)Tasks.Scheduler,
           Message = EventConstants.ProvenancePrefix +
           "Invalid graph since '{outputDirectory}', produced by '{outputDirectoryProducerDescription}', contains the sealed directory '{sealedDirectory}', produced by '{sealedDirectoryProducerDescription}'.")]
        public abstract void ScheduleFailInvalidGraphSinceOutputDirectoryContainsSealedDirectory(
           LoggingContext context,
           string file,
           int line,
           int column,
           string outputDirectory,
           string outputDirectoryProducerDescription,
           string sealedDirectory,
           string sealedDirectoryProducerDescription);

        [GeneratedEvent(
            (int)LogEventId.InvalidGraphSinceSharedOpaqueDirectoryContainsExclusiveOpaqueDirectory,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Scheduler,
            Message =
                EventConstants.ProvenancePrefix +
                "Invalid graph since the shared opaque directory '{sharedOpaqueDirectory}', produced by '{sharedOpaqueDirectoryProducerDescription}', contains the exclusive opaque directory '{exclusiveOpaqueDirectory}', " +
                "produced by '{exclusiveOpaqueDirectoryProducerDescription}'.")]
        public abstract void ScheduleFailInvalidGraphSinceSharedOpaqueDirectoryContainsExclusiveOpaqueDirectory(
            LoggingContext context,
            string file,
            int line,
            int column,
            string sharedOpaqueDirectory,
            string sharedOpaqueDirectoryProducerDescription,
            string exclusiveOpaqueDirectory,
            string exclusiveOpaqueDirectoryProducerDescription);
        [GeneratedEvent(
           (int)LogEventId.InvalidGraphSinceOutputDirectoryCoincidesSourceFile,
           EventGenerators = EventGenerators.LocalOnly,
           EventLevel = Level.Error,
           Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
           EventTask = (int)Tasks.Scheduler,
           Message =
               EventConstants.ProvenancePrefix +
               "Invalid graph since '{outputDirectory}', produced by '{outputDirectoryProducerDescription}', coincides with the source file '{sourceFile}'.")]
        public abstract void ScheduleFailInvalidGraphSinceOutputDirectoryCoincidesSourceFile(
           LoggingContext context,
           string file,
           int line,
           int column,
           string outputDirectory,
           string outputDirectoryProducerDescription,
           string sourceFile);

        [GeneratedEvent(
            (int)LogEventId.InvalidGraphSinceOutputDirectoryContainsOutputFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Scheduler,
            Message =
                EventConstants.ProvenancePrefix +
                "Invalid graph since '{outputDirectory}', produced by '{outputDirectoryProducerDescription}', contains the output file '{outputFile}', produced by '{outputFileProducerDescription}'.")]
        public abstract void ScheduleFailInvalidGraphSinceOutputDirectoryContainsOutputFile(
            LoggingContext context,
            string file,
            int line,
            int column,
            string outputDirectory,
            string outputDirectoryProducerDescription,
            string outputFile,
            string outputFileProducerDescription);

        [GeneratedEvent(
            (int)LogEventId.InvalidGraphSinceOutputDirectoryCoincidesOutputFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Scheduler,
            Message =
                EventConstants.ProvenancePrefix +
                "Invalid graph since '{outputDirectory}', produced by '{outputDirectoryProducerDescription}', coincides with the output file '{outputFile}', produced by '{outputFileProducerDescription}'.")]
        public abstract void ScheduleFailInvalidGraphSinceOutputDirectoryCoincidesOutputFile(
            LoggingContext context,
            string file,
            int line,
            int column,
            string outputDirectory,
            string outputDirectoryProducerDescription,
            string outputFile,
            string outputFileProducerDescription);

        [GeneratedEvent(
            (int)LogEventId.InvalidGraphSinceOutputDirectoryContainsSourceFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Scheduler,
            Message =
                EventConstants.ProvenancePrefix +
                "Invalid graph since '{outputDirectory}', produced by '{outputDirectoryProducerDescription}', contains the source file '{sourceFile}'.")]
        public abstract void ScheduleFailInvalidGraphSinceOutputDirectoryContainsSourceFile(
            LoggingContext context,
            string file,
            int line,
            int column,
            string outputDirectory,
            string outputDirectoryProducerDescription,
            string sourceFile);

        [GeneratedEvent(
            (int)LogEventId.InvalidGraphSinceSourceSealedDirectoryCoincidesSourceFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Scheduler,
            Message =
                EventConstants.ProvenancePrefix +
                "Invalid graph since source sealed directory '{sourceSealedDirectory}' coincides with the source file '{sourceFile}'.")]
        public abstract void ScheduleFailInvalidGraphSinceSourceSealedDirectoryCoincidesSourceFile(
            LoggingContext context,
            string file,
            int line,
            int column,
            string sourceSealedDirectory,
            string sourceFile);

        [GeneratedEvent(
            (int)LogEventId.InvalidGraphSinceSourceSealedDirectoryContainsOutputDirectory,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Scheduler,
            Message =
                EventConstants.ProvenancePrefix +
                "Invalid graph since source sealed directory '{sourceSealedDirectory}', contains the output directory '{outputDirectory}', produced by '{outputDirectoryProducerDescription}'.")]
        public abstract void ScheduleFailInvalidGraphSinceSourceSealedDirectoryContainsOutputDirectory(
            LoggingContext context,
            string file,
            int line,
            int column,
            string sourceSealedDirectory,
            string outputDirectory,
            string outputDirectoryProducerDescription);

        [GeneratedEvent(
            (int)LogEventId.InvalidGraphSinceSourceSealedDirectoryCoincidesOutputFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Scheduler,
            Message =
                EventConstants.ProvenancePrefix +
                "Invalid graph since source sealed directory '{sourceSealedDirectory}' coincides with the output file '{outputFile}', produced by '{outputFileProducerDescription}'.")]
        public abstract void ScheduleFailInvalidGraphSinceSourceSealedDirectoryCoincidesOutputFile(
            LoggingContext context,
            string file,
            int line,
            int column,
            string sourceSealedDirectory,
            string outputFile,
            string outputFileProducerDescription);

        [GeneratedEvent(
            (int)LogEventId.InvalidGraphSinceSourceSealedDirectoryContainsOutputFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Scheduler,
            Message =
                EventConstants.ProvenancePrefix +
                "Invalid graph since source sealed directory '{sourceSealedDirectory}' contains the output file '{outputFile}', produced by '{outputFileProducerDescription}'.")]
        public abstract void ScheduleFailInvalidGraphSinceSourceSealedDirectoryContainsOutputFile(
            LoggingContext context,
            string file,
            int line,
            int column,
            string sourceSealedDirectory,
            string outputFile,
            string outputFileProducerDescription);

        [GeneratedEvent(
            (int)LogEventId.InvalidGraphSinceFullySealedDirectoryIncomplete,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Scheduler,
            Message =
                EventConstants.ProvenancePrefix +
                "Fully sealed directories must specify all files contained within the directory. Directory '{sealedDirectory}' does not contain '{missingFile}' which is a file referenced by pip {referencingPip}. Add that file to the Sealed Directory definition to fix this error.")]
        public abstract void InvalidGraphSinceFullySealedDirectoryIncomplete(
            LoggingContext context,
            string file,
            int line,
            int column,
            string sealedDirectory,
            string referencingPip,
            string missingFile);

        [GeneratedEvent(
            (int)LogEventId.InvalidPipDueToInvalidServicePipDependency,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Scheduler,
            Message = "The pip '{pipDescription}' could not be added because one of its service pip dependencies is not a service pip).")]
        public abstract void ScheduleFailAddPipDueToInvalidServicePipDependency(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId);

        [GeneratedEvent(
            (int)LogEventId.InvalidInputDueToMultipleConflictingRewriteCounts,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Scheduler,
            Message =
                EventConstants.ProvenancePrefix +
                "The pip '{pipDescription}' could not be added because it depends on multiple versions (different rewrite counts) of file '{dependencyFile}'.")]
        public abstract void ScheduleFailAddPipInvalidInputDueToMultipleConflictingRewriteCounts(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId,
            string dependencyFile);

        [GeneratedEvent(
            (int)LogEventId.InvalidProcessPipDueToNoOutputArtifacts,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Scheduler,
            Message =
                EventConstants.ProvenancePrefix +
                "The process pip '{pipDescription}' could not be added because it does not specify any output file or opaque directory in a non-temp location. At least one output file or opaque directory is required.")]
        public abstract void ScheduleFailAddProcessPipProcessDueToNoOutputArtifacts(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId);

        [GeneratedEvent(
            (int)LogEventId.InvalidOutputDueToMultipleConflictingRewriteCounts,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Scheduler,
            Message =
                EventConstants.ProvenancePrefix +
                "The process pip '{pipDescription}' could not be added because it outputs multiple versions (different rewrite counts) of file '{outputFile}'.")]
        public abstract void ScheduleFailAddPipInvalidOutputDueToMultipleConflictingRewriteCounts(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId,
            string outputFile);

        [GeneratedEvent(
            (int)LogEventId.InvalidOutputSinceOutputIsBothSpecifiedAsFileAndDirectory,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Scheduler,
            Message =
                EventConstants.ProvenancePrefix +
                "The pip '{pipDescription}' cannot be added because its output '{output}' is specified as both file and directory outputs.")]
        public abstract void ScheduleFailAddPipInvalidOutputSinceOutputIsBothSpecifiedAsFileAndDirectory(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId,
            string output);

        [GeneratedEvent(
            (int)LogEventId.ScheduleFailAddPipDueToInvalidAllowPreserveOutputsFlag,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Scheduler,
            Message = "The pip '{pipDescription}' could not be added because PreserveOutputWhitelist is set even though AllowPreserveOutputs is false for the pip).")]
        public abstract void ScheduleFailAddPipDueToInvalidAllowPreserveOutputsFlag(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId);

        [GeneratedEvent(
            (int)LogEventId.ScheduleFailAddPipDueToInvalidPreserveOutputWhitelist,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Scheduler,
            Message = "The pip '{pipDescription}' could not be added because one of PreserveOutputWhitelist is neither static file output nor directory output).")]
        public abstract void ScheduleFailAddPipDueToInvalidPreserveOutputWhitelist(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId);

        [GeneratedEvent(
            (int)LogEventId.InvalidCopyFilePipDueToSameSourceAndDestinationPath,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Scheduler,
            Message =
                EventConstants.ProvenancePrefix +
                "The copy-file pip '{pipDescription}' could not be added because the path '{filePath}' was used as both its source and destination.")]
        public abstract void ScheduleFailAddCopyFilePipDueToSameSourceAndDestinationPath(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId,
            string filePath);

        [GeneratedEvent(
            (int)LogEventId.InvalidWriteFilePipSinceOutputIsRewritten,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Scheduler,
            Message =
                EventConstants.ProvenancePrefix +
                "The write-file pip '{pipDescription}' could not be added since it rewrites its destination '{rewrittenFile}'. Write-file pips are not allowed to rewrite outputs, since they do not have any inputs by which to order the rewrite.")]
        public abstract void ScheduleFailAddWriteFilePipSinceOutputIsRewritten(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId,
            string rewrittenFile);

        [GeneratedEvent(
            (int)LogEventId.InvalidInputUnderNonReadableRoot,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Scheduler,
            Message =
                EventConstants.ProvenancePrefix +
                "The pip '{pipDescription}' cannot be added because its input '{outputFile}' is under a non-readable mount '{rootDirectory}'.")]
        public abstract void ScheduleFailAddPipInvalidInputUnderNonReadableRoot(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId,
            string outputFile,
            string rootDirectory);

        [GeneratedEvent(
            (int)LogEventId.InvalidInputSincePathIsWrittenAndThusNotSource,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Scheduler,
            Message =
                EventConstants.ProvenancePrefix +
                "The pip '{pipDescription}' cannot be added to the build graph because its input '{outputFile}' is produced by pip '{producingPipDesc}'. " +
                "In order for these pips to execute in the correct order, you should reference the value '{producingPipValueId}' rather than a literal path.")]
        public abstract void ScheduleFailAddPipInvalidInputSincePathIsWrittenAndThusNotSource(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId,
            string outputFile,
            long producingPipSemiStableHash,
            string producingPipDesc,
            string producingPipValueId);

        [GeneratedEvent(
            (int)LogEventId.InvalidInputSinceCorrespondingOutputIsTemporary,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Scheduler,
            Message =
                EventConstants.ProvenancePrefix +
                "The pip '{pipDescription}' cannot be added because it references temporary file '{outputFile}'. That file is produced by the pip '{producingPipDesc}'.")]
        public abstract void ScheduleFailAddPipInvalidInputSinceCorespondingOutputIsTemporary(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId,
            string outputFile,
            long producingPipSemiStableHash,
            string producingPipDesc,
            string producingPipValueId);

        [GeneratedEvent(
            (int)LogEventId.InvalidInputSinceInputIsRewritten,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Scheduler,
            Message =
                EventConstants.ProvenancePrefix +
                "The pip '{pipDescription}' cannot be added because its input '{rewrittenFile}' is re-written to produce a later version. " +
                "Only the final version of a re-written path may be used as a normal input. Consider referencing the later version produced by '{producingPipDescription}'.")]
        public abstract void ScheduleFailAddPipInvalidInputSinceInputIsRewritten(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId,
            string rewrittenFile,
            long producingPipSemiStableHash,
            string producingPipDescription,
            string producingPipValueId);

        [GeneratedEvent(
            (int)LogEventId.InvalidInputSinceInputIsOutputWithNoProducer,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Scheduler,
            Message =
                EventConstants.ProvenancePrefix +
                "The pip '{pipDescription}' cannot be added because its input '{inputFile}' is specified as an output file, but there is no pip producing the output file")]
        public abstract void ScheduleFailAddPipInvalidInputSinceInputIsOutputWithNoProducer(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId,
            string inputFile);

        [GeneratedEvent(
            (int)LogEventId.SourceDirectoryUsedAsDependency,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Scheduler,
            Message =
                EventConstants.ProvenancePrefix +
                "The pip '{pipDescription}' cannot be added because its input directory '{path}' is a source directory (only sealed directories can be used as directory inputs).")]
        public abstract void SourceDirectoryUsedAsDependency(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId,
            string path);

        [GeneratedEvent(
            (int)LogEventId.InvalidTempDirectoryInvalidPath,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Scheduler,
            Message =
                EventConstants.ProvenancePrefix +
                "The pip '{pipDescription}' cannot be added because its temp directory '{tempDirectory}' as specified by the environment variable '{tempEnvironmentVariableName}' is not a valid absolute path.")]
        public abstract void ScheduleFailAddPipInvalidTempDirectoryInvalidPath(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId,
            string tempDirectory,
            string tempEnvironmentVariableName);

        [GeneratedEvent(
            (int)LogEventId.InvalidTempDirectoryUnderNonWritableRoot,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Scheduler,
            Message =
                EventConstants.ProvenancePrefix +
                "The pip '{pipDescription}' cannot be added because its temp directory '{tempDirectory}' as specified by the environment variable '{tempEnvironmentVariableName}' is under a non-writable mount '{rootDirectory}'.")]
        public abstract void ScheduleFailAddPipInvalidTempDirectoryUnderNonWritableRoot(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId,
            string tempDirectory,
            string rootDirectory,
            string tempEnvironmentVariableName);

        [GeneratedEvent(
            (int)LogEventId.InvalidOutputSinceOutputIsSource,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Scheduler,
            Message =
                EventConstants.ProvenancePrefix +
                "The pip '{pipDescription}' cannot be added because its output '{rewrittenFile}' is already declared as a source file.")]
        public abstract void ScheduleFailAddPipInvalidOutputSinceOutputIsSource(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId,
            string rewrittenFile);

        [GeneratedEvent(
            (int)LogEventId.InvalidOutputUnderNonWritableRoot,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Scheduler,
            Message =
                EventConstants.ProvenancePrefix +
                "The pip '{pipDescription}' cannot be added because its output '{outputFile}' is under a non-writable mount '{rootDirectory}'.")]
        public abstract void ScheduleFailAddPipInvalidOutputUnderNonWritableRoot(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId,
            string outputFile,
            string rootDirectory);

        [GeneratedEvent(
            (int)LogEventId.InvalidOutputSinceRewrittenOutputMismatchedWithInput,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Scheduler,
            Message =
                EventConstants.ProvenancePrefix +
                "The pip '{pipDescription}' rewrites its input dependency '{rewrittenFile}', but that dependency's version does not match the rewritten output. " +
                "It must depend on the immediately prior version of that path, or not depend on that path at all.")]
        public abstract void ScheduleFailAddPipRewrittenOutputMismatchedWithInput(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId,
            string rewrittenFile);

        [GeneratedEvent(
            (int)LogEventId.InvalidOutputDueToSimpleDoubleWrite,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Scheduler,
            Message =
                EventConstants.ProvenancePrefix +
                "The pip '{pipDescription}' cannot be added because it would produce '{outputFile}' which is already being produced by '{producingPipDescription}'.")]
        public abstract void ScheduleFailAddPipInvalidOutputDueToSimpleDoubleWrite(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId,
            string outputFile,
            long producingPipSemiStableHash,
            string producingPipDescription,
            string producingPipValueId);

        [GeneratedEvent(
            (int)LogEventId.InvalidOutputSinceRewritingOldVersion,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Scheduler,
            Message = EventConstants.ProvenancePrefix + "The pip '{pipDescription}' cannot be added because its output '{outputFile}' has already been declared as being re-written. " +
                      "Re-writes must form a linear sequence (consider re-writing the latest version of the path from the pip '{producingPipDescription}' / value '{producingPipValueId}').")]
        public abstract void ScheduleFailAddPipInvalidOutputSinceRewritingOldVersion(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId,
            string outputFile,
            long producingPipSemiStableHash,
            string producingPipDescription,
            string producingPipValueId);

        [GeneratedEvent(
            (int)LogEventId.InvalidOutputSinceOutputHasUnexpectedlyHighWriteCount,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Scheduler,
            Message =
                EventConstants.ProvenancePrefix +
                "The pip '{pipDescription}' cannot be added because its output '{rewrittenFile}' has an unexpectedly high write count. The previous version of that path does not exist. This indicates an error in the build logic.")]
        public abstract void ScheduleFailAddPipInvalidOutputSinceOutputHasUnexpectedlyHighWriteCount(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId,
            string rewrittenFile);

        [GeneratedEvent(
            (int)LogEventId.InvalidOutputSincePreviousVersionUsedAsInput,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Scheduler,
            Message =
                EventConstants.ProvenancePrefix +
                "The pip '{pipDescription}' cannot be added because it declares it will rewrite output '{rewrittenFile}', which has already been specified a a non-rewritten input of another pip. " +
                "Only the final version of a re-written path may be used as a normal input.")]
        public abstract void ScheduleFailAddPipInvalidOutputSincePreviousVersionUsedAsInput(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId,
            string rewrittenFile);

        [GeneratedEvent(
            (int)LogEventId.InvalidOutputSinceFileHasBeenPartiallySealed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Scheduler,
            Message =
                EventConstants.ProvenancePrefix +
                "The pip '{pipDescription}' cannot be added because its output '{rewrittenFile}' has already been sealed as part of '{sealedDirectoryPath}' by the pip '{producingPipDescription}'. "
                + "Files which have been partially or fully sealed may no longer change. Consider sealing the final version of this file.")]
        public abstract void ScheduleFailAddPipInvalidOutputSinceFileHasBeenPartiallySealed(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId,
            string rewrittenFile,
            string sealedDirectoryPath,
            long producingPipSemiStableHash,
            string producingPipDescription,
            string producingPipValueId);

        [GeneratedEvent(
            (int)LogEventId.RewritingPreservedOutput,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Scheduler,
            Message =
                EventConstants.ProvenancePrefix +
                "The pip '{pipDescription}' rewrites '{outputFile}' which is already being produced by '{producingPipDescription}' who intends to preserve its outputs.")]
        public abstract void ScheduleAddPipInvalidOutputDueToRewritingPreservedOutput(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId,
            string outputFile,
            long producingPipSemiStableHash,
            string producingPipDescription,
            string producingPipValueId);

        [GeneratedEvent(
            (int)LogEventId.InvalidOutputSinceDirectoryHasBeenSealed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Scheduler,
            Message =
                EventConstants.ProvenancePrefix +
                "The pip '{pipDescription}' cannot be added because its output '{rewrittenFile}' would be written under the directory '{sealedDirectoryPath}', which has been sealed by the pip '{producingPipDescription}'. "
                + "The content of a fully-sealed directory can no longer change. Consider adding this file as a dependency of the sealed directory, or changing the directory to be 'partially' sealed.")]
        public abstract void ScheduleFailAddPipInvalidOutputSinceDirectoryHasBeenSealed(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId,
            string rewrittenFile,
            string sealedDirectoryPath,
            long producingPipSemiStableHash,
            string producingPipDescription,
            string producingPipValueId);

        [GeneratedEvent(
            (int)LogEventId.InvalidOutputSinceDirectoryHasBeenProducedByAnotherPip,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Scheduler,
            Message =
                EventConstants.ProvenancePrefix +
                "The pip '{pipDescription}' cannot be added because its output '{directory}' has been produced by another pip '{producingPipDescription}'")]
        public abstract void ScheduleFailAddPipInvalidOutputSinceDirectoryHasBeenProducedByAnotherPip(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId,
            string directory,
            long producingPipSemiStableHash,
            string producingPipDescription,
            string producingPipValueId);

        [GeneratedEvent(
            (int)LogEventId.PreserveOutputsDoNotApplyToSharedOpaques,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Scheduler,
            Message =
            "[{pipDescription}] This pip specifies shared opaque directories, but the option to preserve pip outputs is enabled. " +
            "Outputs produced in shared opaque directories are never preserved, even if this option is on.")]
        public abstract void PreserveOutputsDoNotApplyToSharedOpaques(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId);

        [GeneratedEvent(
                 (int)LogEventId.InvalidSharedOpaqueDirectoryDueToOverlap,
                 EventGenerators = EventGenerators.LocalOnly,
                 EventLevel = Level.Error,
                 Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
                 EventTask = (int)Tasks.Scheduler,
                 Message =
                     EventConstants.ProvenancePrefix +
                     "The pip '{pipDescription}' cannot be added because its shared output directory '{sharedOutputDirectory}' would be under the scope of the shared output directory '{parentSharedOutputDirectory}'. "
                     + "Shared output directories specified by the same pip should not be within each other.")]
        public abstract void ScheduleFailAddPipInvalidSharedOpaqueDirectoryDueToOverlap(
                 LoggingContext context,
                 string file,
                 int line,
                 int column,
                 long pipSemiStableHash,
                 string pipDescription,
                 string pipValueId,
                 string sharedOutputDirectory,
                 string parentSharedOutputDirectory);

        [GeneratedEvent(
            (int)LogEventId.InvalidSealDirectorySourceNotUnderMount,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Scheduler,
            Message =
                EventConstants.ProvenancePrefix +
                "Source directory '{sealedDirectoryPath}' (created via '{pipValueId}') cannot be sealed. This directory is not under a mount. Source sealed directories must be under a readable mount.")]
        public abstract void ScheduleFailAddPipInvalidSealDirectorySourceNotUnderMount(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId,
            string sealedDirectoryPath);

        [GeneratedEvent(
            (int)LogEventId.InvalidSealDirectorySourceNotUnderReadableMount,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Scheduler,
            Message =
                EventConstants.ProvenancePrefix +
                "Source directory '{sealedDirectoryPath}' (created via '{pipValueId}') cannot be sealed. This directory is under mount '{mountName}' with folder '{mountPath}' which is not declared as readable by the configuration.")]
        public abstract void ScheduleFailAddPipInvalidSealDirectorySourceNotUnderReadableMount(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId,
            string sealedDirectoryPath,
            string mountPath,
            string mountName);

        [GeneratedEvent(
            (int)LogEventId.InvalidSealDirectoryContentSinceNotUnderRoot,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Scheduler,
            Message =
                EventConstants.ProvenancePrefix +
                "The pip '{pipDescription}' cannot seal the file '{rewrittenFile}' as part of directory '{sealedDirectoryPath}' since that file is not a descendant. "
                + "When sealing a directory, all files under that directory must be specified (but no others outside of it).")]
        public abstract void ScheduleFailAddPipInvalidSealDirectoryContentSinceNotUnderRoot(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId,
            string rewrittenFile,
            string sealedDirectoryPath);

        [GeneratedEvent(
            (int)LogEventId.ScheduleFailAddPipInvalidComposedSealDirectoryIsNotSharedOpaque,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Scheduler,
            Message =
                EventConstants.ProvenancePrefix +
                "Composite directory '{compositeSealedDirectoryPath}' (created via '{pipValueId}') cannot be sealed. Directory '{sealDirectoryMemberPath}' is not a shared opaque.")]
        public abstract void ScheduleFailAddPipInvalidComposedSealDirectoryIsNotSharedOpaque(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId,
            string compositeSealedDirectoryPath,
            string sealDirectoryMemberPath);

        [GeneratedEvent(
            (int)LogEventId.ScheduleFailAddPipInvalidComposedSealDirectoryNotUnderRoot,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Scheduler,
            Message =
                EventConstants.ProvenancePrefix +
                "Composite directory '{compositeSealedDirectoryPath}' (created via '{pipValueId}') cannot be sealed. Directory '{sealDirectoryMemberPath}' is not nested within the composite directory root.")]
        public abstract void ScheduleFailAddPipInvalidComposedSealDirectoryNotUnderRoot(
            LoggingContext context,
            string file,
            int line,
            int column,
            long pipSemiStableHash,
            string pipDescription,
            string pipValueId,
            string compositeSealedDirectoryPath,
            string sealDirectoryMemberPath);

        [GeneratedEvent(
            (int)LogEventId.PipStaticFingerprint,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Scheduler,
            Message =
                "Static fingerprint of '{pipDescription}' is '{staticFingerprint}':\r\n{fingerprintText}.")]
        public abstract void PipStaticFingerprint(LoggingContext context, string pipDescription, string staticFingerprint, string fingerprintText);

        [GeneratedEvent(
            (int)LogEventId.StartFilterApplyTraversal,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Scheduler,
            EventOpcode = (byte)EventOpcode.Start,
            Message = EventConstants.PhasePrefix + "Traversing graph applying filter to pips")]
        public abstract void StartFilterApplyTraversal(LoggingContext context);

        [GeneratedEvent(
            (int)LogEventId.EndFilterApplyTraversal,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Scheduler,
            EventOpcode = (byte)EventOpcode.Stop,
            Message = EventConstants.PhasePrefix + "Done traversing graph applying filter to pips")]
        public abstract void EndFilterApplyTraversal(LoggingContext context);

        [GeneratedEvent(
            (int)LogEventId.NoPipsMatchedFilter,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Scheduler,
            Message = "No pips match this filter: {0}")]
        public abstract void NoPipsMatchedFilter(LoggingContext context, string pipFilter);
    }
}
#pragma warning restore CA1823 // Unused field
