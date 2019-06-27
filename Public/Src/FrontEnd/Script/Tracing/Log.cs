// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using BuildXL.FrontEnd.Sdk.Tracing;
using BuildXL.Tracing;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;
#if FEATURE_MICROSOFT_DIAGNOSTICS_TRACING
using Microsoft.Diagnostics.Tracing;
#else
using System.Diagnostics.Tracing;
#endif

#pragma warning disable 1591
#pragma warning disable CA1823 // Unused field
#pragma warning disable SA1600 // Element must be documented

namespace BuildXL.FrontEnd.Script.Tracing
{
    /// <summary>
    /// Logging
    /// </summary>
    [EventKeywordsType(typeof(Keywords))]
    [EventTasksType(typeof(Tasks))]
    public abstract partial class Logger : LoggerBase
    {
        private bool m_preserveLogEvents;
        private Logger m_forwardDiagnosticsTo;
        private int m_errorCount;

        private readonly ConcurrentQueue<Diagnostic> m_capturedDiagnostics = new ConcurrentQueue<Diagnostic>();
        private readonly ConcurrentBag<ILogMessageObserver> m_messageObservers = new ConcurrentBag<ILogMessageObserver>();

        // Internal logger will prevent public users from creating an instance of the logger
        internal Logger()
        { }

        /// <summary>
        /// True when at least one error occurred.
        /// </summary>
        public bool HasErrors => ErrorCount != 0;

        /// <summary>
        /// Returns number of errors.
        /// </summary>
        public int ErrorCount => m_errorCount;

        /// <summary>
        /// Whether the logger preserves log events
        /// </summary>
        public bool PreserveLogEvents { get; }

        /// <summary>
        /// Factory method that creates instances of the logger.
        /// </summary>
        /// <param name="preserveLogEvents">When specified all logged events would be stored in the internal data structure.</param>
        /// <param name="forwardDiagnosticsTo">Logger to forward diagnostics to.</param>
        /// <param name="notifyContextWhenErrorsAreLogged">Whether to notify the logging context that errors are logged.</param>
        public static Logger CreateLogger(bool preserveLogEvents = false, Logger forwardDiagnosticsTo = null, bool notifyContextWhenErrorsAreLogged = true)
        {
            return new LoggerImpl()
            {
                m_preserveLogEvents = preserveLogEvents,
                m_forwardDiagnosticsTo = forwardDiagnosticsTo,
                m_notifyContextWhenErrorsAreLogged = notifyContextWhenErrorsAreLogged,
            };
        }

        /// <summary>
        /// Provides diagnostics captured by the logger.
        /// Would be non-empty only when preserveLogEvents flag was specified in the <see cref="Logger.CreateLogger"/> factory method.
        /// </summary>
        public IReadOnlyList<Diagnostic> CapturedDiagnostics => m_capturedDiagnostics.ToList();

        /// <inheritdoc />
        public override bool InspectMessageEnabled => true;

        /// <inheritdoc />
        protected override void InspectMessage(int logEventId, EventLevel level, string message, Location? location = null)
        {
            m_forwardDiagnosticsTo?.InspectMessage(logEventId, level, message, location);

            var diagnostic = new Diagnostic(logEventId, level, message, location);
            if (m_preserveLogEvents)
            {
                m_capturedDiagnostics.Enqueue(diagnostic);
            }

            foreach (var observer in m_messageObservers)
            {
                observer.OnMessage(diagnostic);
            }

            if (level.IsError())
            {
                Interlocked.Increment(ref m_errorCount);
            }
        }

        /// <summary>
        /// Tries to empty the collection of diagnostics.
        /// </summary>
        /// <returns>Whether it succeeded emptying the diagnostics</returns>
        public bool TryClearCapturedDiagnostics()
        {
            while (!m_capturedDiagnostics.IsEmpty)
            {
                if (!m_capturedDiagnostics.TryDequeue(out Diagnostic result))
                {
                    return false;
                }

                if (result.Level.IsError())
                {
                    Interlocked.Decrement(ref m_errorCount);
                }
            }

            return true;
        }

        public void AddObserver(ILogMessageObserver observer)
        {
            if (observer != null && !m_messageObservers.Contains(observer))
            {
                m_messageObservers.Add(observer);
            }
        }

        [GeneratedEvent(
            (ushort)LogEventId.ScriptDebugLog,
            EventGenerators = BuildXL.Tracing.EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            EventTask = (ushort)Tasks.Parser,
            Message = "{message}",
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void ScriptDebugLog(LoggingContext loggingContext, string message);

        [GeneratedEvent(
            (ushort)LogEventId.PackageDescriptorsIsNotArrayLiteral,
            EventGenerators = BuildXL.Tracing.EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = "Package descriptors in '{packageDescriptorPath}' does not evaluate to an array literal.",
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics | Keywords.UserError))]
        public abstract void PackageDescriptorsIsNotArrayLiteral(LoggingContext loggingContext, string frontEndName, string packageDescriptorPath);

        [GeneratedEvent(
            (ushort)LogEventId.PackageDescriptorIsNotObjectLiteral,
            EventGenerators = BuildXL.Tracing.EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = "Package descriptor '{number}' in '{packageDescriptorPath}' does not evaluate to an object literal.",
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics | Keywords.UserError))]
        public abstract void PackageDescriptorIsNotObjectLiteral(LoggingContext loggingContext, string frontEndName, string packageDescriptorPath, int number);

        [GeneratedEvent(
            (ushort)LogEventId.PackageMainFileIsNotInTheSameDirectoryAsPackageConfiguration,
            EventGenerators = BuildXL.Tracing.EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = "Package main file '{packageMainFile}' is not in the same directory as package configuration '{packageConfiguration}'.",
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics | Keywords.UserError))]
        public abstract void PackageMainFileIsNotInTheSameDirectoryAsPackageConfiguration(LoggingContext loggingContext, string frontEndName, string packageMainFile, string packageConfiguration);

        [GeneratedEvent(
            (ushort)LogEventId.FailAddingPackageDueToPackageOwningAllProjectsExists,
            EventGenerators = BuildXL.Tracing.EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = "Failed adding package '{addedPackage}' in '{packageConfigPath}' because a package, '{packageOwningAll}', that owns all projects exists. When defining multiple packages in a single package configuration file, none of them may implicitly own all projects (by omitting to specify the 'projects' field).",
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics | Keywords.UserError))]
        public abstract void FailAddingPackageDueToPackageOwningAllProjectsExists(LoggingContext loggingContext, string frontEndName, string packageConfigPath, string addedPackage, string packageOwningAll);

        [GeneratedEvent(
            (ushort)LogEventId.FailAddingPackageBecauseItWantsToOwnAllProjectsButSomeAreOwnedByOtherPackages,
            EventGenerators = BuildXL.Tracing.EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = "Failed adding package '{addedPackage}' in '{packageConfigPath}' because the package wants to own all projects, but some of them are owned by other existing packages. When defining multiple packages in a single package configuration file, none of them may implicitly own all projects (by omitting to specify the 'projects' field).",
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics | Keywords.UserError))]
        public abstract void FailAddingPackageBecauseItWantsToOwnAllProjectsButSomeAreOwnedByOtherPackages(LoggingContext loggingContext, string frontEndName, string packageConfigPath, string addedPackage);

        [GeneratedEvent(
            (ushort)LogEventId.FailAddingPackageBecauseItsProjectIsOwnedByAnotherPackage,
            EventGenerators = BuildXL.Tracing.EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = "Failed adding package '{addedPackage}' in '{packageConfigPath}' because its project '{projectPath}' is owned by another package '{anotherPackage}'.",
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics | Keywords.UserError))]
        public abstract void FailAddingPackageBecauseItsProjectIsOwnedByAnotherPackage(LoggingContext loggingContext, string frontEndName, string packageConfigPath, string addedPackage, string projectPath, string anotherPackage);

        [GeneratedEvent(
            (ushort)LogEventId.ConversionException,
            EventGenerators = BuildXL.Tracing.EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "{exceptionMessage}.",
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics | Keywords.UserError))]
        public abstract void ReportConversionException(LoggingContext loggingContext, Location location, string frontEndName, string exceptionMessage);

        [GeneratedEvent(
            (ushort)LogEventId.UnexpectedResolverException,
            EventGenerators = BuildXL.Tracing.EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Unexpected exception: {exceptionMessage}.",
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void ReportUnexpectedResolverException(LoggingContext loggingContext, Location location, string frontEndName, string exceptionMessage);

        [GeneratedEvent(
            (ushort)LogEventId.MissingField,
            EventGenerators = BuildXL.Tracing.EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = "Missing field in configuration file '{fileModulePath}': {fieldName}.",
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics | Keywords.UserError))]
        public abstract void ReportMissingField(LoggingContext loggingContext, string frontEndName, string fileModulePath, string fieldName);

        [GeneratedEvent(
            (ushort)LogEventId.InvalidPackageNameDueToUsingConfigPackage,
            EventGenerators = BuildXL.Tracing.EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Invalid package name {name} (case insensitive) because the name is reserved.",
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics | Keywords.UserError))]
        public abstract void ReportInvalidPackageNameDueToUsingConfigPackage(LoggingContext loggingContext, Location location, string frontEndName, string fileModulePath, string name);

        [GeneratedEvent(
            (ushort)LogEventId.SourceResolverPackageFileNotWithinConfiguration,
            EventGenerators = BuildXL.Tracing.EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Specified package file '{packagePath}' is not within the configuration directory '{configFilePath}'.",
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics | Keywords.UserError))]
        public abstract void ReportSourceResolverPackageFileNotWithinConfiguration(LoggingContext loggingContext, Location location, string frontEndName, string packagePath, string configFilePath);

        [GeneratedEvent(
            (ushort)LogEventId.SourceResolverPackageFilesDoNotExist,
            EventGenerators = BuildXL.Tracing.EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Specified package (configuration) file '{packageConfigPath}' does not exist.",
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics | Keywords.UserError))]
        public abstract void ReportSourceResolverPackageFilesDoNotExist(LoggingContext loggingContext, Location location, string frontEndName, string packageConfigPath);
        
        [GeneratedEvent(
            (ushort)LogEventId.SourceResolverRootDirForPackagesDoesNotExist,
            EventGenerators = BuildXL.Tracing.EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = "{locationInfo}Directory specified for the SourceResolver.root property ('{rootDirPath}') does not exist.",
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics | Keywords.UserError))]
        public abstract void ReportSourceResolverRootDirForPackagesDoesNotExist(LoggingContext loggingContext, string frontEndName, string rootDirPath, string locationInfo);

        [GeneratedEvent(
            (ushort)LogEventId.SourceResolverFailEvaluateUnregisteredFileModule,
            EventGenerators = BuildXL.Tracing.EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = "Failed evaluating file module '{fileModulePath}' because it is unregistered; the file module may have not been parsed.",
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void ReportSourceResolverFailEvaluateUnregisteredFileModule(LoggingContext loggingContext, string frontEndName, string fileModulePath);

        [GeneratedEvent(
            (ushort)LogEventId.SourceResolverConfigurationIsNotObjectLiteral,
            EventGenerators = BuildXL.Tracing.EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Configuration in '{configPath}' does not evaluate to an object literal.",
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics | Keywords.UserError))]
        public abstract void ReportSourceResolverConfigurationIsNotObjectLiteral(LoggingContext loggingContext, Location location, string frontEndName, string configPath);

        [GeneratedEvent(
            (ushort)LogEventId.MissingTypeChecker,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Unable to perform type checking because no type checker is set for the front end.",
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void ReportMissingTypeChecker(LoggingContext loggingContext, Location location, string frontEndName);

        [GeneratedEvent(
            (ushort)LogEventId.UnableToEnumerateFilesOnCollectingPackagesAndProjects,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            EventTask = (ushort)Tasks.Parser,
            Message = "{locationInfo}Unable to enumerate files in directory {dirName} on collecting packages/projects: {reason}.",
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void ReportUnableToEnumerateFilesOnCollectingPackagesAndProjects(
            LoggingContext loggingContext,
            string frontEndName,
            string dirName,
            string reason,
            string locationInfo);

        [GeneratedEvent(
            (ushort)LogEventId.UnableToEnumerateDirectoriesOnCollectingPackagesAndProjects,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            EventTask = (ushort)Tasks.Parser,
            Message = "{locationInfo}Unable to enumerate directories in directory {dirName} on collecting packages/projects: {reason}.",
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void ReportUnableToEnumerateDirectoriesOnCollectingPackagesAndProjects(
            LoggingContext loggingContext,
            string frontEndName,
            string dirName,
            string reason,
            string locationInfo);

        [GeneratedEvent(
            (ushort)LogEventId.DebugDumpCallStack,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Debug CallStack: {message}:\r\n{stackTrace}",
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void DebugDumpCallStack(
            LoggingContext loggingContext,
            Location location,
            string message,
            string stackTrace);

        [GeneratedEvent(
            (ushort)LogEventId.ImplicitSemanticsDoesNotAdmitMainFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = "Module '{moduleName}' defined in '{pathToModule}' is specifying incompatible options. " +
                      "A main file cannot be provided if the module has implicit reference semantics.",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportImplicitSemanticsDoesNotAdmitMainFile(LoggingContext loggingContext, string pathToModule, string moduleName);

        [GeneratedEvent(
            (ushort)LogEventId.CannotUsePackagesAndModulesSimultaneously,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (ushort)Tasks.Engine,
            Message = EventConstants.LabeledProvenancePrefix + "Cannot specify 'modules' and 'packages' at the same time in '{pathToConfig}'. Field 'packages' is obsolete, please use 'modules' instead.")]
        internal abstract void CannotUsePackagesAndModulesSimultaneously(LoggingContext context, Location location, string pathToConfig);

        [GeneratedEvent(
            (ushort)LogEventId.EvaluationCancellationRequestedAfterFirstFailure,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            EventTask = (ushort)Tasks.Parser,
            Message = "Evaluation cancellation requested after first failure.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void EvaluationCancellationRequestedAfterFirstFailure(LoggingContext loggingContext);

        [GeneratedEvent(
            (ushort)LogEventId.EvaluationCanceled,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = "Evaluation cancellation requested.",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void EvaluationCanceled(LoggingContext loggingContext);

        [GeneratedEvent(
            (ushort)LogEventId.ExplicitSemanticsDoesNotAdmitAllowedModuleDependencies,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = "Module '{moduleName}' defined in '{pathToModule}' is specifying incompatible options. " +
                      "Declaring a list of allowed module dependencies is not allowed if the module has explicit reference semantics.",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportExplicitSemanticsDoesNotAdmitAllowedModuleDependencies(LoggingContext loggingContext, string pathToModule, string moduleName);

        [GeneratedEvent(
            (ushort)LogEventId.ExplicitSemanticsDoesNotAdmitCyclicalFriendModules,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = "Module '{moduleName}' defined in '{pathToModule}' is specifying incompatible options. " +
                      "Declaring a list of friend modules with cyclic references is not allowed if the module has explicit reference semantics.",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportExplicitSemanticsDoesNotAdmitCyclicalFriendModules(LoggingContext loggingContext, string pathToModule, string moduleName);

        [GeneratedEvent(
            (ushort)LogEventId.CyclicalFriendModulesNotEnabledByPolicy,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = "Module '{moduleName}' defined in '{pathToModule}' is not allowed to declare 'cyclicalFriendModules' as the policy is not enabled in the global configuration file.",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportCyclicalFriendModulesNotEnabledByPolicy(LoggingContext loggingContext, string pathToModule, string moduleName);

        [GeneratedEvent(
            (ushort)LogEventId.DuplicateAllowedModuleDependencies,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = "Module '{moduleName}' defined in '{pathToModule}' is specifying duplicate module names in the 'allowedDependencies' list. Duplicate module names: {duplicatedModuleDependencies}.",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportDuplicateAllowedModuleDependencies(LoggingContext loggingContext, string pathToModule, string moduleName, string duplicatedModuleDependencies);

        [GeneratedEvent(
            (ushort)LogEventId.DuplicateCyclicalFriendModules,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = "Module '{moduleName}' defined in '{pathToModule}' is specifying duplicate module names in the 'cyclicalFriendModules' list. Duplicate module names: {duplicatedCyclicalFriends}.",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportDuplicateCyclicalFriendModules(LoggingContext loggingContext, string pathToModule, string moduleName, string duplicatedCyclicalFriends);

        [GeneratedEvent(
            (ushort)LogEventId.PropertyAccessOnValueWithTypeAny,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Selector '{selector}' cannot be applied to receiver '{receiver}' in expression '{receiver}.{selector}' because receiver is of type 'any'. Values with type 'any' cannot be inspected in {ShortScriptName}.",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportPropertyAccessOnValueWithTypeAny(LoggingContext loggingContext, Location location, string receiver, string selector);
    }

    /// <summary>
    /// Represents an event forwarded from a worker
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815")]
    public struct UnsupportedQualifierValue
    {
        /// <summary>
        /// The location of the error
        /// </summary>
        public Location Location;

        /// <summary>
        /// The key of the qualifier
        /// </summary>
        public string QualifierKey { get; set; }

        /// <summary>
        /// The invalid value passed.
        /// </summary>
        public string InvalidValue { get; set; }

        /// <summary>
        /// The set of legal values
        /// </summary>
        public string LegalValues { get; set; }
    }

    /// <nodoc />
    public interface ILogMessageObserver
    {
        void OnMessage(Diagnostic diagnostic);
    }
}

#pragma warning restore CA1823 // Unused field
#pragma warning restore SA1600 // Element must be documented
