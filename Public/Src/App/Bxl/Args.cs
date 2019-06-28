// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.Linq;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Native.IO;
using BuildXL.Pips.Operations;
using BuildXL.Storage;
using BuildXL.ToolSupport;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Tracing;
using static BuildXL.Utilities.FormattableStringEx;
using HelpLevel = BuildXL.Utilities.Configuration.HelpLevel;
using Strings = bxl.Strings;

#pragma warning disable SA1649 // File name must match first type name

namespace BuildXL
{
    /// <summary>
    /// Argument acquisition.
    /// </summary>
    /// <remarks>
    /// This class consumes command-line arguments to establish BuildXL's marching orders.
    /// The use of exceptions in this class is purely for expedient flow-control as the
    /// single public Acquire method merely returns null on failure. Parsing errors are
    /// output to stderr prior to return.
    /// </remarks>
    internal sealed class Args : IArgumentParser<ICommandLineConfiguration>, IDisposable
    {
        private static readonly string[] s_serviceLocationSeparator = { ":" };

        /// <summary>
        /// Canonical name for cached graph from last build
        /// </summary>
        public const string LastBuiltCachedGraphName = "lastbuild";

        private const string RedirectedUserProfileLocationInCloudBuild = @"d:\dbs";

        private readonly IConsole m_console;
        private readonly bool m_shouldDisposeConsole;

        // Stores the complete list of handlers for testing purposes
        private OptionHandler[] m_handlers;

        /// <nodoc />
        public Args(IConsole console)
        {
            m_console = console ?? new StandardConsole(colorize: true, animateTaskbar: false, supportsOverwriting: false);

            // Only dispose console if this instance creates it.
            m_shouldDisposeConsole = console == null;
        }

        /// <nodoc />
        public Args()
            : this(null)
        {
        }

        private struct OptionHandler
        {
            /// <summary>
            /// The name of the option with no suffixes.
            /// </summary>
            public readonly string OptionName;

            // The action to take in response to the option being set.
            public readonly Action<CommandLineUtilities.Option> Action;

            // After Action(opt) has run, determines whether the option is considered enabled or disabled.
            public readonly Func<bool> IsEnabled;

            // Whether the option is considered unsafe, this should trigger a warning printout.
            public readonly bool IsUnsafe;

            // Anything passed into the command line after the OptionName.
            public readonly string Suffix;

            /// <summary>
            /// Set when the option exist to prevent legacy command lines from failing, but no longer does anything.
            /// </summary>
            public readonly bool Inactive;

            public OptionHandler(string optionName, Action<CommandLineUtilities.Option> action, bool isUnsafe, Func<bool> isEnabled = null, string suffix = "", bool inactive = false)
            {
                OptionName = optionName;
                Action = action;
                IsUnsafe = isUnsafe;
                IsEnabled = isEnabled ?? (() => true);
                Suffix = suffix;
                Inactive = inactive;
            }
        }

        /// <summary>
        /// Factory class to create option handlers.
        /// </summary>
        private static class OptionHandlerFactory
        {
            // Returns a singleton array containing a single OptionHandler instance for given name/action.
            public static OptionHandler[] CreateOption(string name, Action<CommandLineUtilities.Option> action, bool isUnsafe = false)
            {
                return new[]
                {
                    new OptionHandler(name, action, isUnsafe),
                };
            }

            // Returns an array containing two OptionHandler instances for two given names, both having the same action.
            public static OptionHandler[] CreateOption2(string name1, string name2, Action<CommandLineUtilities.Option> action, bool isUnsafe = false)
            {
                return new[]
                {
                    new OptionHandler(name1, action, isUnsafe),
                    new OptionHandler(name2, action, isUnsafe),
                };
            }

            // Returns an array with three OptionHandler instances, having the following names: <name>, <name>+, <name>-.
            // The "action" argument must accept two arguments: the original CommandLineUtilities.Option,
            // and a boolean corresponding to the sign suffix (no suffix means true).
            public static OptionHandler[] CreateBoolOptionWithValue(
                string name,
                Action<CommandLineUtilities.Option, bool> action,
                bool isUnsafe = false,
                Func<bool> isEnabled = null,
                bool inactive = false)
            {
                return new[]
                {
                    new OptionHandler(name, opt => action(opt, true), isUnsafe, isEnabled: isEnabled, inactive: inactive),
                    new OptionHandler(name, opt => action(opt, true), isUnsafe, isEnabled: () => true, suffix: "+", inactive: inactive),
                    new OptionHandler(name, opt => action(opt, false), isUnsafe, isEnabled: () => false, suffix: "-", inactive: inactive),
                };
            }

            // Returns an array with three OptionHandler instances, having the following names: <name>, <name>+, <name>-.
            // The "action" argument must accept only one argument, which is a boolean corresponding to the sign suffix (no suffix means true).
            public static OptionHandler[] CreateBoolOption(string name, Action<bool> action, bool isUnsafe = false, bool inactive = false)
            {
                return CreateBoolOptionWithValue(name, (opt, sign) => action(sign), isUnsafe: isUnsafe, inactive: inactive);
            }
        }

        /// <nodoc />
        public static bool TryParseArguments(string[] args, PathTable pathTable, IConsole console, out ICommandLineConfiguration arguments)
        {
            using (var argsParser = new Args(console))
            {
                return argsParser.TryParse(args, pathTable, out arguments);
            }
        }

        /// <inheritdoc />
        [SuppressMessage("Microsoft.Maintainability", "CA1505:AvoidUnmaintainableCode")]
        [SuppressMessage("Microsoft.Performance", "CA1809", Justification = "Man up!")]
        public bool TryParse(string[] args, PathTable pathTable, out ICommandLineConfiguration arguments)
        {
            try
            {
                var cl = new CommandLineUtilities(args);

                var configuration = new BuildXL.Utilities.Configuration.Mutable.CommandLineConfiguration();
                var startupConfiguration = configuration.Startup;
                var engineConfiguration = configuration.Engine;
                var layoutConfiguration = configuration.Layout;
                var sandboxConfiguration = configuration.Sandbox;
                var schedulingConfiguration = configuration.Schedule;
                var cacheConfiguration = configuration.Cache;
                var loggingConfiguration = configuration.Logging;
                var exportConfiguration = configuration.Export;
                var experimentalConfiguration = configuration.Experiment;
                var distributionConfiguration = configuration.Distribution;
                var frontEndConfiguration = configuration.FrontEnd;
                var ideConfiguration = configuration.Ide;
                var resolverDefaults = configuration.ResolverDefaults;

                bool unsafeUnexpectedFileAccessesAreErrorsSet = false;
                bool failPipOnFileAccessErrorSet = false;
                bool? enableProfileRedirect = null;
                ContentHashingUtilities.SetDefaultHashType();

                // Notes
                //
                //  * Handlers must be in alphabetical order according to their long forms.
                //
                //  * Boolean options must be listed three times (once naked, once with a + suffix, and once with a - suffix);
                //    Use CreateBoolOption or CreateBoolOptionWithValue helper functions to not repeat yourself.
                //
                //  * Options with a short form are listed twice (once in the long form, followed by the short form once);
                //    Use the option2 helper function to not repeat yourself.
                m_handlers =
                    new[]
                    {
                        OptionHandlerFactory.CreateOption2(
                            "additionalConfigFile",
                            "ac",
                            opt => ParsePathOption(opt, pathTable, startupConfiguration.AdditionalConfigFiles)),
                        OptionHandlerFactory.CreateOption(
                            "adminRequiredProcessExecutionMode",
                            opt => sandboxConfiguration.AdminRequiredProcessExecutionMode = CommandLineUtilities.ParseEnumOption<AdminRequiredProcessExecutionMode>(opt)),
                        OptionHandlerFactory.CreateBoolOption(
                            "allowFetchingCachedGraphFromContentCache",
                            sign => cacheConfiguration.AllowFetchingCachedGraphFromContentCache = sign),
                        OptionHandlerFactory.CreateBoolOption(
                            "allowInternalDetoursErrorNotificationFile",
                            sign => sandboxConfiguration.AllowInternalDetoursErrorNotificationFile = sign),
                        OptionHandlerFactory.CreateBoolOption(
                            "analyzeDependencyViolations",
                            opt => { /* DEPRECATED -- DO NOTHING */ }),
                        OptionHandlerFactory.CreateBoolOption(
                            "breakOnUnexpectedFileAccess",
                            opt => sandboxConfiguration.BreakOnUnexpectedFileAccess = opt),
                        OptionHandlerFactory.CreateOption(
                            "buildLockPolling",
                            opt => engineConfiguration.BuildLockPollingIntervalSec = CommandLineUtilities.ParseInt32Option(opt, 15, int.MaxValue)),
                        OptionHandlerFactory.CreateOption(
                            "buildWaitTimeout",
                            opt => engineConfiguration.BuildLockWaitTimeoutMins = CommandLineUtilities.ParseInt32Option(opt, 0, int.MaxValue)),
                        OptionHandlerFactory.CreateOption(
                            "cacheConfigFilePath",
                            opt => cacheConfiguration.CacheConfigFile = CommandLineUtilities.ParsePathOption(opt, pathTable)),
                        OptionHandlerFactory.CreateOption2(
                            "cacheDirectory",
                            "cd",
                            opt => layoutConfiguration.CacheDirectory = CommandLineUtilities.ParsePathOption(opt, pathTable)),
                        OptionHandlerFactory.CreateBoolOption(
                            "cacheGraph",
                            opt => cacheConfiguration.CacheGraph = opt),
                        OptionHandlerFactory.CreateOption(
                            "cacheMemoryUsage",
                            opt => cacheConfiguration.CacheMemoryUsage = CommandLineUtilities.ParseEnumOption<MemoryUsageOption>(opt)),
                        OptionHandlerFactory.CreateBoolOptionWithValue(
                            "cacheMiss",
                            (opt, sign) => ParseCacheMissAnalysisOption(opt, sign, loggingConfiguration, pathTable)),
                        OptionHandlerFactory.CreateOption(
                            "cacheSessionName",
                            opt => cacheConfiguration.CacheSessionName = CommandLineUtilities.ParseStringOption(opt)),
                        OptionHandlerFactory.CreateBoolOption(
                            "cacheSpecs",
                            sign => cacheConfiguration.CacheSpecs = sign ? SpecCachingOption.Enabled : SpecCachingOption.Disabled),
                        OptionHandlerFactory.CreateBoolOption(
                            "canonicalizeFilterOutputs",
                            sign => schedulingConfiguration.CanonicalizeFilterOutputs = sign),
                        OptionHandlerFactory.CreateBoolOption(
                            "checkDetoursMessageCount",
                            sign => sandboxConfiguration.CheckDetoursMessageCount = sign),
                        OptionHandlerFactory.CreateBoolOption(
                            "cleanOnly",
                            sign => engineConfiguration.CleanOnly = sign),
                        OptionHandlerFactory.CreateBoolOption(
                            "cleanTempDirectories",
                            sign => engineConfiguration.CleanTempDirectories = sign),
                        OptionHandlerFactory.CreateBoolOption(
                            "color",
                            sign => loggingConfiguration.Color = sign),
                        OptionHandlerFactory.CreateBoolOption(
                            "compressGraphFiles",
                            sign => engineConfiguration.CompressGraphFiles = sign),
                        OptionHandlerFactory.CreateOption2(
                            "config",
                            "c",
                            opt =>
                            {
                                startupConfiguration.ConfigFile = CommandLineUtilities.ParsePathOption(opt, pathTable);
                            }),
                        OptionHandlerFactory.CreateOption2(
                            "consoleVerbosity",
                            "cv",
                            opt => loggingConfiguration.ConsoleVerbosity = CommandLineUtilities.ParseEnumOption<VerbosityLevel>(opt)),
                        OptionHandlerFactory.CreateBoolOption(
                            "converge",
                            sign => engineConfiguration.Converge = sign),
                        OptionHandlerFactory.CreateOption(
                            "customLog",
                            opt => ParseKeyValueOption(opt, pathTable, loggingConfiguration.CustomLog)),
                        OptionHandlerFactory.CreateBoolOption(
                            "debuggerBreakOnExit",
                            opt => frontEndConfiguration.DebuggerBreakOnExit = opt),
                        OptionHandlerFactory.CreateOption(
                            "debuggerPort",
                            opt => frontEndConfiguration.DebuggerPort = CommandLineUtilities.ParseInt32Option(opt, 1024, 65535)),
                        OptionHandlerFactory.CreateBoolOption(
                            "debugScript",
                            opt => frontEndConfiguration.DebugScript = opt),
                        OptionHandlerFactory.CreateBoolOption(
                            "scriptShowSlowest",
                            opt => frontEndConfiguration.ShowSlowestElementsStatistics = opt),
                        OptionHandlerFactory.CreateBoolOption(
                            "scriptShowLargest",
                            opt => frontEndConfiguration.ShowLargestFilesStatistics = opt),
                        OptionHandlerFactory.CreateOption(
                            "debug_LoadGraph",
                            opt => HandleLoadGraphOption(opt, pathTable, cacheConfiguration)),
                        OptionHandlerFactory.CreateBoolOption(
                            "determinismProbe",
                            sign => cacheConfiguration.DeterminismProbe = sign),
                        OptionHandlerFactory.CreateOption2(
                            "diagnostic",
                            "diag",
                            opt => loggingConfiguration.Diagnostic |= CommandLineUtilities.ParseEnumOption<DiagnosticLevels>(opt)),
                        OptionHandlerFactory.CreateBoolOption(
                            "earlyWorkerRelease",
                            sign => schedulingConfiguration.EarlyWorkerRelease = sign),
                        OptionHandlerFactory.CreateOption(
                            "earlyWorkerReleaseMultiplier",
                            opt =>
                            schedulingConfiguration.EarlyWorkerReleaseMultiplier = CommandLineUtilities.ParseDoubleOption(opt, 0, 5)),
                        OptionHandlerFactory.CreateBoolOption(
                            "enforceAccessPoliciesOnDirectoryCreation",
                            sign => sandboxConfiguration.EnforceAccessPoliciesOnDirectoryCreation = sign),
                        OptionHandlerFactory.CreateBoolOption(
                            "emitSpotlightIndexingWarning",
                            sign => layoutConfiguration.EmitSpotlightIndexingWarning = sign),
                        OptionHandlerFactory.CreateBoolOption(
                            "disableConHostSharing",
                            sign => engineConfiguration.DisableConHostSharing = sign),
                        OptionHandlerFactory.CreateBoolOption(
                            "disableLoggedPathTranslation",
                            opt => loggingConfiguration.DisableLoggedPathTranslation = opt),
                        OptionHandlerFactory.CreateBoolOption(
                            "disableProcessRetryOnResourceExhaustion",
                            sign => schedulingConfiguration.DisableProcessRetryOnResourceExhaustion = sign),
                        OptionHandlerFactory.CreateBoolOption(
                            "distributeCacheLookups",
                            sign => distributionConfiguration.DistributeCacheLookups = sign),
                        OptionHandlerFactory.CreateOption2(
                            "distributedBuildRole",
                            "dbr",

                            opt =>
                            {
                                // TODO: Ideally we'd automatically turn on distributionConfiguration.ValidateDistribution whenever
                                // distribution is enabled. This probably won't work with WDG right now though, so it is not on by default
                                distributionConfiguration.BuildRole = CommandLineUtilities.ParseEnumOption<DistributedBuildRoles>(opt);
                            }),
                        OptionHandlerFactory.CreateOption2(
                            "distributedBuildServicePort",
                            "dbsp",
                            opt =>
                            distributionConfiguration.BuildServicePort = (ushort)CommandLineUtilities.ParseInt32Option(opt, 1, ushort.MaxValue)),
                        OptionHandlerFactory.CreateOption2(
                            "distributedBuildWorker",
                            "dbw",
                            opt => ParseServiceLocation(opt, distributionConfiguration.BuildWorkers)),
                        OptionHandlerFactory.CreateBoolOption(
                            "enableAsyncLogging",
                            sign => loggingConfiguration.EnableAsyncLogging = sign),
                        OptionHandlerFactory.CreateBoolOption(
                            "enableDedup",
                            sign =>
                            {
                                if (sign)
                                {
                                    ContentHashingUtilities.SetDefaultHashType(HashType.DedupNodeOrChunk);
                                    cacheConfiguration.UseDedupStore = true;
                                }
                            }),
                        OptionHandlerFactory.CreateBoolOption(
                            "enableGrpc",
                            sign => distributionConfiguration.IsGrpcEnabled = sign),
                        OptionHandlerFactory.CreateBoolOption(
                            "enableIncrementalFrontEnd",
                            sign => frontEndConfiguration.EnableIncrementalFrontEnd = sign),
                        OptionHandlerFactory.CreateBoolOptionWithValue(
                            "enableLazyOutputs",
                            (opt, sign) => HandleLazyOutputMaterializationOption(opt, sign, schedulingConfiguration)),
                        OptionHandlerFactory.CreateOption(
                            "engineCacheDirectory",
                            opt => layoutConfiguration.EngineCacheDirectory = CommandLineUtilities.ParsePathOption(opt, pathTable)),
                        OptionHandlerFactory.CreateBoolOption(
                            "ensureTempDirectoriesExistenceBeforePipExecution",
                            sign => sandboxConfiguration.EnsureTempDirectoriesExistenceBeforePipExecution = sign),
                        OptionHandlerFactory.CreateOption(
                            "environment",
                            opt => loggingConfiguration.Environment = CommandLineUtilities.ParseEnumOption<ExecutionEnvironment>(opt)),
                        OptionHandlerFactory.CreateOption2(
                            "experiment",
                            "exp",
                            opt => HandleExperimentalOption(
                                opt,
                                experimentalConfiguration,
                                engineConfiguration,
                                schedulingConfiguration,
                                sandboxConfiguration,
                                loggingConfiguration,
                                ideConfiguration,
                                frontEndConfiguration)),
                        OptionHandlerFactory.CreateOption(
                            "exportGraph",
                            opt =>
                            {
                                throw CommandLineUtilities.Error("The /exportGraph option has been deprecated. Use bxlanalyzer.exe /mode:ExportGraph");
                            }),
                        OptionHandlerFactory.CreateBoolOption(
                            "failPipOnFileAccessError",
                            sign =>
                            {
                                loggingConfiguration.FailPipOnFileAccessError = sign;

                                if (!sign)
                                {
                                    failPipOnFileAccessErrorSet = true;
                                }
                            }),
                        OptionHandlerFactory.CreateBoolOption(
                            "fancyConsole",
                            sign => loggingConfiguration.FancyConsole = sign),
                        OptionHandlerFactory.CreateOption(
                            "fancyConsoleMaxStatusPips",
                            opt => loggingConfiguration.FancyConsoleMaxStatusPips = CommandLineUtilities.ParseInt32Option(opt, 1, int.MaxValue)),
                        OptionHandlerFactory.CreateOption(
                            "fileChangeTrackerInitializationMode",
                            opt => engineConfiguration.FileChangeTrackerInitializationMode = CommandLineUtilities.ParseEnumOption<FileChangeTrackerInitializationMode>(opt)),
                        OptionHandlerFactory.CreateOption(
                            "fileChangeTrackingExclusionRoot",
                            opt => cacheConfiguration.FileChangeTrackingExclusionRoots.Add(CommandLineUtilities.ParsePathOption(opt, pathTable))),
                        OptionHandlerFactory.CreateOption(
                            "fileChangeTrackingInclusionRoot",
                            opt => cacheConfiguration.FileChangeTrackingInclusionRoots.Add(CommandLineUtilities.ParsePathOption(opt, pathTable))),
                        OptionHandlerFactory.CreateOption(
                            "fileContentTableFile",
                            opt => layoutConfiguration.FileContentTableFile = CommandLineUtilities.ParsePathOption(opt, pathTable)),
                        OptionHandlerFactory.CreateOption(
                            "fileContentTablePathMappingMode",
                            opt => { /* Do nothing Office still passes this flag. */  }),
                        OptionHandlerFactory.CreateOption(
                            "fileSystemMode",
                            opt => sandboxConfiguration.FileSystemMode = CommandLineUtilities.ParseEnumOption<FileSystemMode>(opt)),
                        OptionHandlerFactory.CreateOption2(
                            "fileVerbosity",
                            "fv",
                            opt => loggingConfiguration.FileVerbosity = CommandLineUtilities.ParseEnumOption<VerbosityLevel>(opt)),
                        OptionHandlerFactory.CreateOption2(
                            "filter",
                            "f",
                            opt => configuration.Filter = CommandLineUtilities.ParseStringOptionalOption(opt)),
                        OptionHandlerFactory.CreateOption(
                            "fingerprintStoreMaxEntryAgeMinutes",
                            opt => loggingConfiguration.FingerprintStoreMaxEntryAgeMinutes = CommandLineUtilities.ParseInt32Option(opt, 0, Int32.MaxValue)),
                        OptionHandlerFactory.CreateBoolOption(
                            "flushPageCacheToFileSystemOnStoringOutputsToCache",
                            sign => sandboxConfiguration.FlushPageCacheToFileSystemOnStoringOutputsToCache = sign),
                        OptionHandlerFactory.CreateBoolOption(
                            "forcePopulatePackageCache",
                            sign => frontEndConfiguration.ForcePopulatePackageCache = sign),
                        OptionHandlerFactory.CreateBoolOption(
                            "usePackagesFromFileSystem",
                            sign => frontEndConfiguration.UsePackagesFromFileSystem = sign),
                        OptionHandlerFactory.CreateBoolOption(
                            "forceUseEngineInfoFromCache",
                            sign => schedulingConfiguration.ForceUseEngineInfoFromCache = sign),
                        OptionHandlerFactory.CreateBoolOption(
                            "hardExitOnErrorInDetours",
                            sign => sandboxConfiguration.HardExitOnErrorInDetours = sign),
#if PLATFORM_OSX
                        OptionHandlerFactory.CreateOption(
                            "numberOfKextConnections", // TODO: deprecate and remove
                            opt =>
                            {
                                Console.WriteLine("*** WARNING: deprecated switch /numberOfKextConnections; don't use it as it has no effect any longer");
                            }),
                        OptionHandlerFactory.CreateOption(
                            "reportQueueSizeMb", // TODO: deprecate and remove
                            opt =>
                            {
                                Console.WriteLine("*** WARNING: deprecated switch /reportQueueSizeMb; please use /kextReportQueueSizeMb instead");
                                sandboxConfiguration.KextReportQueueSizeMb = CommandLineUtilities.ParseUInt32Option(opt, 16, 2048);
                            }),
                        OptionHandlerFactory.CreateBoolOption(
                            "kextEnableReportBatching",
                            sign => sandboxConfiguration.KextEnableReportBatching = sign),
                        OptionHandlerFactory.CreateBoolOption(
                            "kextMeasureProcessCpuTimes",
                            sign => sandboxConfiguration.KextMeasureProcessCpuTimes = sign),
                        OptionHandlerFactory.CreateOption(
                            "kextNumberOfConnections",
                            opt =>
                            {
                                Console.WriteLine("*** WARNING: deprecated switch /kextNumberOfKextConnections; don't use it as it has no effect any longer");
                            }),
                        OptionHandlerFactory.CreateOption(
                            "kextReportQueueSizeMb",
                            opt => sandboxConfiguration.KextReportQueueSizeMb = CommandLineUtilities.ParseUInt32Option(opt, 16, 2048)),
                        OptionHandlerFactory.CreateOption(
                            "kextThrottleCpuUsageBlockThresholdPercent",
                            opt => sandboxConfiguration.KextThrottleCpuUsageBlockThresholdPercent = CommandLineUtilities.ParseUInt32Option(opt, 0, 100)),
                        OptionHandlerFactory.CreateOption(
                            "kextThrottleCpuUsageWakeupThresholdPercent",
                            opt => sandboxConfiguration.KextThrottleCpuUsageWakeupThresholdPercent = CommandLineUtilities.ParseUInt32Option(opt, 0, 100)),
                        OptionHandlerFactory.CreateOption(
                            "kextThrottleMinAvailableRamMB",
                            opt => sandboxConfiguration.KextThrottleMinAvailableRamMB = CommandLineUtilities.ParseUInt32Option(opt, 0, uint.MaxValue)),
#endif
                        OptionHandlerFactory.CreateOption2(
                            "help",
                            "?",
                            opt =>
                            {
                                var help = ParseHelpOption(opt);
                                configuration.Help = help.Key;
                                configuration.HelpCode = help.Value;
                            }),
                        OptionHandlerFactory.CreateBoolOption(
                            "inCloudBuild",
                            sign => configuration.InCloudBuild = sign),
                        OptionHandlerFactory.CreateBoolOption(
                            "incremental",
                            sign => cacheConfiguration.Incremental = sign),
                        OptionHandlerFactory.CreateBoolOption(
                            "incrementalScheduling",
                            sign => schedulingConfiguration.IncrementalScheduling = sign),
                        OptionHandlerFactory.CreateBoolOption(
                            "inferNonExistenceBasedOnParentPathInRealFileSystem",
                            sign => schedulingConfiguration.InferNonExistenceBasedOnParentPathInRealFileSystem = sign),
                        OptionHandlerFactory.CreateOption(
                            "injectCacheMisses",
                            opt => HandleArtificialCacheMissOption(opt, cacheConfiguration)),
                        OptionHandlerFactory.CreateBoolOption(
                            "historicMetadataCache",
                            sign => cacheConfiguration.HistoricMetadataCache = sign),
                        OptionHandlerFactory.CreateBoolOption(
                            "launchDebugger",
                            sign => configuration.LaunchDebugger = sign),
                        OptionHandlerFactory.CreateBoolOption(
                            "logCounters",
                            sign => loggingConfiguration.LogCounters = sign),
                        OptionHandlerFactory.CreateBoolOption(
                            "logExecution",
                            sign => loggingConfiguration.LogExecution = sign),
                        OptionHandlerFactory.CreateBoolOption(
                            "logFileAccessTables",
                            sign => sandboxConfiguration.LogFileAccessTables = sign),
                        OptionHandlerFactory.CreateBoolOption(
                            "logMemory",
                            sign => loggingConfiguration.LogMemory = sign),
                        OptionHandlerFactory.CreateBoolOption(
                            "logObservedFileAccesses",
                            sign => sandboxConfiguration.LogObservedFileAccesses = sign),
                        OptionHandlerFactory.CreateOption(
                            "logOutput",
                            opt => sandboxConfiguration.OutputReportingMode = CommandLineUtilities.ParseEnumOption<OutputReportingMode>(opt)),
                         OptionHandlerFactory.CreateBoolOption(
                            "logPipStaticFingerprintTexts",
                            sign => schedulingConfiguration.LogPipStaticFingerprintTexts = sign),
                        OptionHandlerFactory.CreateOption(
                            "logPrefix",
                            opt => loggingConfiguration.LogPrefix = ParseLogPrefix(opt)),
                        OptionHandlerFactory.CreateBoolOption(
                            "logProcessData",
                            sign => sandboxConfiguration.LogProcessData = sign),
                        OptionHandlerFactory.CreateBoolOption(
                            "logProcessDetouringStatus",
                            sign => sandboxConfiguration.LogProcessDetouringStatus = sign),
                        OptionHandlerFactory.CreateBoolOption(
                            "logProcesses",
                            sign => sandboxConfiguration.LogProcesses = sign),
                        OptionHandlerFactory.CreateOption(
                            "logsDirectory",
                            opt => loggingConfiguration.LogsDirectory = CommandLineUtilities.ParsePathOption(opt, pathTable)),
                        OptionHandlerFactory.CreateBoolOption(
                            "logStats",
                            sign => loggingConfiguration.LogStats = sign),
                        OptionHandlerFactory.CreateBoolOption(
                            "logStatus",
                            sign => loggingConfiguration.LogStatus = sign),
                        OptionHandlerFactory.CreateOption(
                            "logsToRetain",
                            opt => loggingConfiguration.LogsToRetain = CommandLineUtilities.ParseInt32Option(opt, 1, 1000)),
                        OptionHandlerFactory.CreateBoolOption(
                            "lowPriority",
                            sign => schedulingConfiguration.LowPriority = sign),
                        OptionHandlerFactory.CreateBoolOption(
                            "maskUntrackedAccesses",
                            sign => sandboxConfiguration.MaskUntrackedAccesses = sign),
                        OptionHandlerFactory.CreateOption(
                            "masterCpuMultiplier",
                            opt =>
                            schedulingConfiguration.MasterCpuMultiplier = CommandLineUtilities.ParseDoubleOption(opt, 0, 1)),
                       OptionHandlerFactory.CreateOption(
                            "masterCacheLookupMultiplier",
                            opt =>
                            schedulingConfiguration.MasterCacheLookupMultiplier = CommandLineUtilities.ParseDoubleOption(opt, 0, 1)),
                        OptionHandlerFactory.CreateOption(
                            "maxCacheLookup",
                            opt => schedulingConfiguration.MaxCacheLookup = CommandLineUtilities.ParseInt32Option(opt, 1, int.MaxValue)),
                        OptionHandlerFactory.CreateOption(
                            "maxChooseWorkerCpu",
                            opt => schedulingConfiguration.MaxChooseWorkerCpu = CommandLineUtilities.ParseInt32Option(opt, 1, int.MaxValue)),
                        OptionHandlerFactory.CreateOption2(
                            "maxFrontEndConcurrency",
                            "mF",
                            opt => frontEndConfiguration.MaxFrontEndConcurrency = CommandLineUtilities.ParseInt32Option(opt, 1, int.MaxValue)),
                        OptionHandlerFactory.CreateOption(
                            "maxRestoreNugetConcurrency",
                            opt => frontEndConfiguration.MaxRestoreNugetConcurrency = CommandLineUtilities.ParseInt32Option(opt, 1, int.MaxValue)),
                        OptionHandlerFactory.CreateOption(
                            "maxIO",
                            opt => schedulingConfiguration.MaxIO = CommandLineUtilities.ParseInt32Option(opt, 1, int.MaxValue)),
                        OptionHandlerFactory.CreateOption(
                            "maxIOMultiplier",
                            opt =>
                            schedulingConfiguration.MaxIO =
                            (int)Math.Max(1, Environment.ProcessorCount * CommandLineUtilities.ParseDoubleOption(opt, 0, int.MaxValue))),
                        OptionHandlerFactory.CreateOption(
                            "maxLightProc",
                            opt => schedulingConfiguration.MaxLightProcesses = CommandLineUtilities.ParseInt32Option(opt, 1, int.MaxValue)),
                        OptionHandlerFactory.CreateOption(
                            "maxMaterialize",
                            opt => schedulingConfiguration.MaxMaterialize = CommandLineUtilities.ParseInt32Option(opt, 1, int.MaxValue)),
                        OptionHandlerFactory.CreateOption(
                            "maxProc",
                            opt => schedulingConfiguration.MaxProcesses = CommandLineUtilities.ParseInt32Option(opt, 1, int.MaxValue)),
                        OptionHandlerFactory.CreateOption(
                            "maxProcMultiplier",
                            opt =>
                            schedulingConfiguration.MaxProcesses =
                            (int)Math.Max(1, Environment.ProcessorCount * CommandLineUtilities.ParseDoubleOption(opt, 0, int.MaxValue))),
                        OptionHandlerFactory.CreateOption(
                            "maxRamUtilizationPercentage",
                            opt => schedulingConfiguration.MaximumRamUtilizationPercentage = CommandLineUtilities.ParseInt32Option(opt, 0, 100)),
                        OptionHandlerFactory.CreateOption(
                            "maxRelativeOutputDirectoryLength",
                            opt => engineConfiguration.MaxRelativeOutputDirectoryLength = CommandLineUtilities.ParseInt32Option(opt, 49, 260)),
                        OptionHandlerFactory.CreateOption(
                            "maxTypeCheckingConcurrency",
                            opt => frontEndConfiguration.MaxTypeCheckingConcurrency = CommandLineUtilities.ParseInt32Option(opt, 1, int.MaxValue)),
                        OptionHandlerFactory.CreateOption(
                            "minAvailableRamMb",
                            opt => schedulingConfiguration.MinimumTotalAvailableRamMb = CommandLineUtilities.ParseInt32Option(opt, 0, int.MaxValue)),
                        OptionHandlerFactory.CreateOption(
                            "minCacheContentReplica",
                            opt => cacheConfiguration.MinimumReplicaCountForStrongGuarantee = (byte)CommandLineUtilities.ParseInt32Option(opt, 0, 32)),
                        OptionHandlerFactory.CreateOption(
                            "minWorkers",
                            opt => distributionConfiguration.MinimumWorkers = CommandLineUtilities.ParseInt32Option(opt, 1, int.MaxValue)),
                        OptionHandlerFactory.CreateOption(
                            "noLog",
                            opt => ParseInt32ListOption(opt, loggingConfiguration.NoLog)),
                        OptionHandlerFactory.CreateOption(
                            "noExecutionLog",
                            opt => ParseInt32ListOption(opt, loggingConfiguration.NoExecutionLog)),
                        OptionHandlerFactory.CreateOption(
                            "noLogo",
                            opt => configuration.NoLogo = true),
                        OptionHandlerFactory.CreateBoolOption(
                            "normalizeReadTimestamps",
                            sign => sandboxConfiguration.NormalizeReadTimestamps = sign),
                        OptionHandlerFactory.CreateOption(
                            "noWarn",
                            opt => ParseInt32ListOption(opt, loggingConfiguration.NoWarnings)),
                        OptionHandlerFactory.CreateOption2(
                            "objectDirectory",
                            "o",
                            opt => layoutConfiguration.ObjectDirectory = CommandLineUtilities.ParsePathOption(opt, pathTable)),
                        OptionHandlerFactory.CreateOption(
                            "outputFileExtensionsForSequentialScanHandleOnHashing",
                            opt => schedulingConfiguration.OutputFileExtensionsForSequentialScanHandleOnHashing.AddRange(CommandLineUtilities.ParseRepeatingPathAtomOption(opt, pathTable.StringTable, ";"))),
                        OptionHandlerFactory.CreateOption(
                            "outputMaterializationExclusionRoot",
                            opt => schedulingConfiguration.OutputMaterializationExclusionRoots.Add(CommandLineUtilities.ParsePathOption(opt, pathTable))),
                        OptionHandlerFactory.CreateOption2(
                            "parameter",
                            "p",
                            opt => ParsePropertyOption(opt, startupConfiguration.Properties)),
                        OptionHandlerFactory.CreateOption(
                            "phase",
                            opt => engineConfiguration.Phase = CommandLineUtilities.ParseEnumOption<EnginePhases>(opt)),
                        OptionHandlerFactory.CreateBoolOption(
                            "pinCachedOutputs",
                            sign => schedulingConfiguration.PinCachedOutputs = sign),
                        OptionHandlerFactory.CreateOption(
                            "pipDefaultTimeout",
                            opt =>
                            sandboxConfiguration.DefaultTimeout =
                            CommandLineUtilities.ParseInt32Option(opt, 1, (int)Process.MaxTimeout.TotalMilliseconds)),
                        OptionHandlerFactory.CreateOption(
                            "pipDefaultWarningTimeout",
                            opt =>
                            sandboxConfiguration.DefaultWarningTimeout =
                            CommandLineUtilities.ParseInt32Option(opt, 1, (int)Process.MaxTimeout.TotalMilliseconds)),
                        OptionHandlerFactory.CreateOption(
                            "pipTimeoutMultiplier",
                            opt => sandboxConfiguration.TimeoutMultiplier = (int)CommandLineUtilities.ParseDoubleOption(opt, 0.000001, 1000000)),
                        OptionHandlerFactory.CreateOption(
                            "pipWarningTimeoutMultiplier",
                            opt =>
                            sandboxConfiguration.WarningTimeoutMultiplier = (int)CommandLineUtilities.ParseDoubleOption(opt, 0.000001, 1000000)),
                        OptionHandlerFactory.CreateOption(
                            "populateSymlinkDirectory",
                            opt => engineConfiguration.PopulateSymlinkDirectories.Add(CommandLineUtilities.ParsePathOption(opt, pathTable))),
                        OptionHandlerFactory.CreateOption(
                            "posixDeleteMode",
                            opt => FileUtilities.PosixDeleteMode = CommandLineUtilities.ParseEnumOption<PosixDeleteMode>(opt)),
                        OptionHandlerFactory.CreateOption(
                            "printFile2FileDependencies",
                            opt => frontEndConfiguration.FileToFileReportDestination = CommandLineUtilities.ParsePathOption(opt, pathTable)),
                        OptionHandlerFactory.CreateOption(
                            "processRetries",
                            opt => schedulingConfiguration.ProcessRetries = CommandLineUtilities.ParseInt32Option(opt, 0, int.MaxValue)),
                        OptionHandlerFactory.CreateOption(
                            "profileReportDestination",
                            opt => frontEndConfiguration.ProfileReportDestination = CommandLineUtilities.ParsePathOption(opt, pathTable)),
                        OptionHandlerFactory.CreateBoolOption(
                            "profileScript",
                            opt => frontEndConfiguration.ProfileScript = opt),
                        OptionHandlerFactory.CreateOption(
                            "property",
                            opt => ParsePropertyOption(opt, startupConfiguration.Properties)),
                        OptionHandlerFactory.CreateOption2(
                            "qualifier",
                            "q",
                            opt => ParseStringOption(opt, startupConfiguration.QualifierIdentifiers)),
                        OptionHandlerFactory.CreateBoolOption(
                            "redirectUserProfile",
                            opt => enableProfileRedirect = opt),
                        OptionHandlerFactory.CreateOption(
                            "redirectedUserProfileJunctionRoot",
                            opt => layoutConfiguration.RedirectedUserProfileJunctionRoot = CommandLineUtilities.ParsePathOption(opt, pathTable)),
                        OptionHandlerFactory.CreateOption(
                            "relatedActivityId",
                            opt => loggingConfiguration.RelatedActivityId = CommandLineUtilities.ParseStringOption(opt)),
                        OptionHandlerFactory.CreateBoolOptionWithValue(
                            "remoteTelemetry",
                            (opt, sign) =>
                            loggingConfiguration.RemoteTelemetry =
                            CommandLineUtilities.ParseBoolEnumOption(opt, sign, RemoteTelemetry.EnabledAndNotify, RemoteTelemetry.Disabled),
                            isEnabled: (() => loggingConfiguration.RemoteTelemetry != RemoteTelemetry.Disabled)),
                        OptionHandlerFactory.CreateBoolOption(
                            "replaceExistingFileOnMaterialization",
                            sign => cacheConfiguration.ReplaceExistingFileOnMaterialization = sign),
                        OptionHandlerFactory.CreateBoolOption(
                            "replayWarnings",
                            sign => loggingConfiguration.ReplayWarnings = sign),
                        OptionHandlerFactory.CreateOption(
                            "replicaRefreshProbability",
                            opt => cacheConfiguration.StrongContentGuaranteeRefreshProbability = CommandLineUtilities.ParseDoubleOption(opt, 0, 1)),
                        OptionHandlerFactory.CreateBoolOption(
                            "replicateOutputsToWorkers",
                            sign => distributionConfiguration.ReplicateOutputsToWorkers = sign),
                        OptionHandlerFactory.CreateBoolOption(
                            "respectWeakFingerprintForNugetUpToDateCheck",
                            sign => frontEndConfiguration.RespectWeakFingerprintForNugetUpToDateCheck = sign),
                        OptionHandlerFactory.CreateBoolOption(
                            "reuseEngineState",
                            sign => engineConfiguration.ReuseEngineState = sign),
                        OptionHandlerFactory.CreateBoolOption(
                            "reuseOutputsOnDisk",
                            sign => schedulingConfiguration.ReuseOutputsOnDisk = sign),
                        OptionHandlerFactory.CreateOption2(
                            "rootMap",
                            "rm",
                            opt => ParseKeyValueOption(opt, pathTable, engineConfiguration.RootMap)),
                        OptionHandlerFactory.CreateOption(
                            "sandboxKind",
                            opt =>
                            {
                                var parsedOption = CommandLineUtilities.ParseEnumOption<SandboxKind>(opt);
                                sandboxConfiguration.UnsafeSandboxConfigurationMutable.SandboxKind = parsedOption;
                                if ((parsedOption.ToString().StartsWith("Win") && OperatingSystemHelper.IsUnixOS) ||
                                    (parsedOption.ToString().StartsWith("Mac") && !OperatingSystemHelper.IsUnixOS))
                                {
                                    var osName = OperatingSystemHelper.IsUnixOS ? "Unix-based" : "Windows";
                                    throw CommandLineUtilities.Error(Strings.Args_SandboxKind_WrongPlatform, parsedOption, osName);
                                }
                            }),
                        OptionHandlerFactory.CreateBoolOption(
                            "scanChangeJournal",
                            sign => engineConfiguration.ScanChangeJournal = sign),
                        OptionHandlerFactory.CreateOption(
                            "scanChangeJournalTimeLimitInSec",
                            opt => engineConfiguration.ScanChangeJournalTimeLimitInSec = CommandLineUtilities.ParseInt32Option(opt, -1, int.MaxValue)),
                        OptionHandlerFactory.CreateBoolOption(
                            "scheduleMetaPips",
                            sign => schedulingConfiguration.ScheduleMetaPips = sign),
                        OptionHandlerFactory.CreateOption2(
                            "scriptFile",
                            "s",
                            opt =>
                            {
                                throw CommandLineUtilities.Error(Strings.Args_ScriptFile_Deprecated, CommandLineUtilities.ParseStringOption(opt));
                            }),
                        OptionHandlerFactory.CreateBoolOption(
                            "scrub",
                            sign => engineConfiguration.Scrub = sign),
                        OptionHandlerFactory.CreateOption(
                            "scrubDirectory",
                            opt => engineConfiguration.ScrubDirectories.Add(CommandLineUtilities.ParsePathOption(opt, pathTable))),
                        OptionHandlerFactory.CreateBoolOptionWithValue(
                            "server",
                            (opt, sign) =>
                            configuration.Server = CommandLineUtilities.ParseBoolEnumOption(opt, sign, ServerMode.Enabled, ServerMode.Disabled),
                            isEnabled: (() => configuration.Server != ServerMode.Disabled)),
                        OptionHandlerFactory.CreateOption(
                            "serverDeploymentDir",
                            opt => configuration.ServerDeploymentDirectory = CommandLineUtilities.ParsePathOption(opt, pathTable)),
                        OptionHandlerFactory.CreateOption(
                            "serverMaxIdleTimeInMinutes",
                            opt =>
                            configuration.ServerMaxIdleTimeInMinutes = CommandLineUtilities.ParseInt32Option(opt, 1, int.MaxValue)),
                        OptionHandlerFactory.CreateBoolOption(
                            "skipHashSourceFile",
                            sign =>
                            schedulingConfiguration.SkipHashSourceFile = sign),
                        OptionHandlerFactory.CreateOption(
                            "snap",
                            opt => exportConfiguration.SnapshotFile = CommandLineUtilities.ParsePathOption(opt, pathTable)),
                        OptionHandlerFactory.CreateOption(
                            "snapshotMode",
                            opt => exportConfiguration.SnapshotMode = CommandLineUtilities.ParseEnumOption<SnapshotMode>(opt)),
                        OptionHandlerFactory.CreateOption(
                            "solutionName",
                            opt =>
                            ideConfiguration.SolutionName = PathAtom.Create(pathTable.StringTable, CommandLineUtilities.ParseStringOption(opt))),
                        OptionHandlerFactory.CreateOption(
                            "statusFrequencyMs",
                            opt => loggingConfiguration.StatusFrequencyMs = CommandLineUtilities.ParseInt32Option(opt, 1, 60000)),
                        OptionHandlerFactory.CreateBoolOption(
                            "stopOnFirstError",
                            sign =>
                            {
                                schedulingConfiguration.StopOnFirstError = sign;
                                frontEndConfiguration.CancelEvaluationOnFirstFailure = sign;
                            }),

                        // TODO:not used. Deprecate
                        OptionHandlerFactory.CreateOption2(
                            "storageRoot",
                            "sr",
                            opt => layoutConfiguration.OutputDirectory = CommandLineUtilities.ParsePathOption(opt, pathTable)),
                        OptionHandlerFactory.CreateBoolOptionWithValue(
                            "storeFingerprints",
                            (opt, sign) =>
                            HandleStoreFingerprintsOption(
                                opt,
                                sign,
                                loggingConfiguration)),
                        OptionHandlerFactory.CreateBoolOption(
                            "storeOutputsToCache",
                            sign => schedulingConfiguration.StoreOutputsToCache = sign),
                        OptionHandlerFactory.CreateOption(
                            "substSource",
                            opt => loggingConfiguration.SubstSource = CommandLineUtilities.ParsePathOption(opt, pathTable)),
                        OptionHandlerFactory.CreateOption(
                            "substTarget",
                            opt => loggingConfiguration.SubstTarget = CommandLineUtilities.ParsePathOption(opt, pathTable)),
                        OptionHandlerFactory.CreateOption(
                            "symlinkDefinitionFile",
                            opt => layoutConfiguration.SymlinkDefinitionFile = CommandLineUtilities.ParsePathOption(opt, pathTable)),
                        OptionHandlerFactory.CreateOption(
                            "telemetryTagPrefix",
                            opt => schedulingConfiguration.TelemetryTagPrefix = CommandLineUtilities.ParseStringOption(opt)),
                        OptionHandlerFactory.CreateOption(
                            "tempDirectory",
                            opt => layoutConfiguration.TempDirectory = CommandLineUtilities.ParsePathOption(opt, pathTable)),
                        OptionHandlerFactory.CreateOption(
                            "traceInfo",
                            opt => ParsePropertyOption(opt, loggingConfiguration.TraceInfo)),
                        OptionHandlerFactory.CreateBoolOption(
                            "trackBuildsInUserFolder",
                            opt => engineConfiguration.TrackBuildsInUserFolder = opt),
                        OptionHandlerFactory.CreateBoolOption(
                            "trackMethodInvocations",
                            opt => frontEndConfiguration.TrackMethodInvocations = opt),
                        OptionHandlerFactory.CreateOption(
                            "translateDirectory",
                            opt => ParseTranslateDirectoriesOption(pathTable, opt, engineConfiguration.DirectoriesToTranslate)),
                        OptionHandlerFactory.CreateBoolOption(
                            "treatDirectoryAsAbsentFileOnHashingInputContent",
                            sign => schedulingConfiguration.TreatDirectoryAsAbsentFileOnHashingInputContent = sign),
                        OptionHandlerFactory.CreateBoolOption(
                            "typeCheck",
                            sign => { /* Do nothing Office still passes this flag even though it is deprecated. */ }),

                        OptionHandlerFactory.CreateOption(
                            "unexpectedSymlinkAccessReportingMode",
                            opt => schedulingConfiguration.UnexpectedSymlinkAccessReportingMode = CommandLineUtilities.ParseEnumOption<UnexpectedSymlinkAccessReportingMode>(opt)),

                        // <Begin unsafe arguments>
                        // Unsafe options should follow the pattern that enabling them (i.e. "/unsafe_option" or "/unsafe_option+") should lead to an unsafe configuration
                        // Unsafe options must pass the optional parameter isUnsafe as true
                        // IMPORTANT: If an unsafe option is added here, there should also be a new warning logging function added.
                        //            The logging function should be added to the unsafeOptionLoggers dictionary in LogAndValidateConfiguration()
                        OptionHandlerFactory.CreateBoolOption(
                            "unsafe_AllowCopySymlink",
                            sign => schedulingConfiguration.AllowCopySymlink = sign,
                            isUnsafe: true),
                        OptionHandlerFactory.CreateBoolOption(
                            "unsafe_DisableCycleDetection",
                            sign => frontEndConfiguration.DisableCycleDetection = sign,
                            isUnsafe: true),
                        OptionHandlerFactory.CreateBoolOption(
                            "unsafe_DisableDetours",
                            sign => sandboxConfiguration.UnsafeSandboxConfigurationMutable.SandboxKind = sign ? SandboxKind.None : SandboxKind.Default,
                            isUnsafe: true),
                        OptionHandlerFactory.CreateBoolOption(
                            "unsafe_DisableGraphPostValidation",
                            sign => schedulingConfiguration.UnsafeDisableGraphPostValidation = sign,
                            isUnsafe: true),
                        OptionHandlerFactory.CreateBoolOption(
                            "unsafe_DisableSharedOpaqueEmptyDirectoryScrubbing",
                            sign => schedulingConfiguration.UnsafeDisableSharedOpaqueEmptyDirectoryScrubbing = sign,
                            isUnsafe: true),
                        OptionHandlerFactory.CreateBoolOption(
                            "unsafe_ExistingDirectoryProbesAsEnumerations",
                            sign => sandboxConfiguration.UnsafeSandboxConfigurationMutable.ExistingDirectoryProbesAsEnumerations = sign,
                            isUnsafe: true),
                        OptionHandlerFactory.CreateBoolOptionWithValue(
                            "unsafe_ForceSkipDeps",
                            (opt, sign) => HandleForceSkipDependenciesOption(opt, sign, schedulingConfiguration),
                            isUnsafe: true),
                        OptionHandlerFactory.CreateOption(
                            "unsafe_GlobalPassthroughEnvVars",
                            opt => frontEndConfiguration.GlobalUnsafePassthroughEnvironmentVariables.AddRange(CommandLineUtilities.ParseRepeatingOption(opt, ";", v => v ))),
                        OptionHandlerFactory.CreateOption(
                            "unsafe_GlobalUntrackedScopes",
                            opt => sandboxConfiguration.GlobalUnsafeUntrackedScopes.AddRange(CommandLineUtilities.ParseRepeatingPathOption(opt, pathTable, ";"))),
                        OptionHandlerFactory.CreateBoolOption(
                            "unsafe_IgnoreGetFinalPathNameByHandle",
                            sign => sandboxConfiguration.UnsafeSandboxConfigurationMutable.IgnoreGetFinalPathNameByHandle = sign,
                            isUnsafe: true),
                        OptionHandlerFactory.CreateBoolOption(
                            "unsafe_IgnoreNonCreateFileReparsePoints",
                            sign =>
                            {
                                if (sign && OperatingSystemHelper.IsUnixOS)
                                {
                                    throw CommandLineUtilities.Error("/unsafe_IgnoreReparsePoints not allowed on non-Windows OS");
                                }
                                sandboxConfiguration.UnsafeSandboxConfigurationMutable.IgnoreNonCreateFileReparsePoints = sign;
                            },
                            isUnsafe: true),
                        OptionHandlerFactory.CreateBoolOption(
                            "unsafe_IgnoreNonExistentProbes", sign =>
                            {
                                // Error if it is set. Noop when unset for legacy command line compatibility. Make sure Office
                                // is no longer setting this before removing it.
                                if (sign)
                                {
                                    throw CommandLineUtilities.Error("/unsafe_IgnoreNonExistentProbes is deprecated.");
                                }
                            },
                            inactive: true),
                        OptionHandlerFactory.CreateBoolOption(
                            "unsafe_IgnoreNtCreateFile",
                            sign => sandboxConfiguration.UnsafeSandboxConfigurationMutable.MonitorNtCreateFile = !sign,
                            isUnsafe: true),
                        OptionHandlerFactory.CreateBoolOption(
                            "unsafe_IgnoreProducingSymlinks",
                            sign => { /* DO NOTHING - Flag deprecated  */},
                            isUnsafe: false,
                            inactive: true),
                        OptionHandlerFactory.CreateBoolOption(
                            "unsafe_IgnoreReparsePoints",
                            sign =>
                            {
                                if (sign && OperatingSystemHelper.IsUnixOS)
                                {
                                    throw CommandLineUtilities.Error("/unsafe_IgnoreReparsePoints not allowed on non-Windows OS");
                                }
                                sandboxConfiguration.UnsafeSandboxConfigurationMutable.IgnoreReparsePoints = sign;
                            },
                            isUnsafe: true),
                        OptionHandlerFactory.CreateBoolOption(
                            "unsafe_IgnorePreloadedDlls",
                            sign =>
                            {
                                sandboxConfiguration.UnsafeSandboxConfigurationMutable.IgnorePreloadedDlls = sign;
                            },
                            isUnsafe: true),
                        OptionHandlerFactory.CreateBoolOption(
                            "unsafe_IgnoreDynamicWritesOnAbsentProbes",
                            sign =>
                            {
                                sandboxConfiguration.UnsafeSandboxConfigurationMutable.IgnoreDynamicWritesOnAbsentProbes = sign;
                            },
                            isUnsafe: true),
                        OptionHandlerFactory.CreateBoolOption(
                            "unsafe_IgnoreSetFileInformationByHandle",
                            sign => sandboxConfiguration.UnsafeSandboxConfigurationMutable.IgnoreSetFileInformationByHandle = sign,
                            isUnsafe: true),
                        OptionHandlerFactory.CreateBoolOption(
                            "unsafe_IgnoreValidateExistingFileAccessesForOutputs",
                            sign => { /* Do nothing Office and WDG are still passing this flag even though it is deprecated. */ }),
                        OptionHandlerFactory.CreateBoolOption(
                            "unsafe_IgnoreZwCreateOpenQueryFamily",
                            sign => sandboxConfiguration.UnsafeSandboxConfigurationMutable.MonitorZwCreateOpenQueryFile = !sign,
                            isUnsafe: true),
                        OptionHandlerFactory.CreateBoolOption(
                            "unsafe_IgnoreZwOtherFileInformation",
                            sign => sandboxConfiguration.UnsafeSandboxConfigurationMutable.IgnoreZwOtherFileInformation = sign,
                            isUnsafe: true),
                        OptionHandlerFactory.CreateBoolOption(
                            "unsafe_IgnoreZwRenameFileInformation",
                            sign => sandboxConfiguration.UnsafeSandboxConfigurationMutable.IgnoreZwRenameFileInformation = sign,
                            isUnsafe: true),
                        OptionHandlerFactory.CreateBoolOption(
                            "unsafe_LazySymlinkCreation",
                            sign => schedulingConfiguration.UnsafeLazySymlinkCreation = sign,
                            isUnsafe: true),
                        OptionHandlerFactory.CreateBoolOption(
                            "unsafe_MonitorFileAccesses",
                            sign =>
                            sandboxConfiguration.UnsafeSandboxConfigurationMutable.MonitorFileAccesses = sign,
                            isUnsafe: true),
                        OptionHandlerFactory.CreateBoolOptionWithValue(
                            "unsafe_PreserveOutputs",
                            (opt, sign) =>
                            sandboxConfiguration.UnsafeSandboxConfigurationMutable.PreserveOutputs =
                            CommandLineUtilities.ParseBoolEnumOption(opt, sign, PreserveOutputsMode.Enabled, PreserveOutputsMode.Disabled),
                            isUnsafe: true,
                            isEnabled: (() => sandboxConfiguration.UnsafeSandboxConfiguration.PreserveOutputs != PreserveOutputsMode.Disabled)),
                        // TODO: Remove this!
                        OptionHandlerFactory.CreateBoolOption(
                            "unsafe_SourceFileCanBeInsideOutputDirectory",
                            sign => { /* DEPRECATED -- DO NOTHING */ },
                            isUnsafe: true),
                        OptionHandlerFactory.CreateBoolOption(
                            "unsafe_UnexpectedFileAccessesAreErrors",
                            sign =>
                            {
                                sandboxConfiguration.UnsafeSandboxConfigurationMutable.UnexpectedFileAccessesAreErrors = sign;
                                if (sign)
                                {
                                    unsafeUnexpectedFileAccessesAreErrorsSet = true;
                                }
                            },
                            isUnsafe: true),
                        OptionHandlerFactory.CreateBoolOption(
                            "unsafe_IgnoreUndeclaredAccessesUnderSharedOpaques",
                            sign =>
                            sandboxConfiguration.UnsafeSandboxConfigurationMutable.IgnoreUndeclaredAccessesUnderSharedOpaques = sign,
                            isUnsafe: true),
                        // </ end unsafe options>
                         OptionHandlerFactory.CreateBoolOption(
                            "useCustomPipDescriptionOnConsole",
                            sign => loggingConfiguration.UseCustomPipDescriptionOnConsole = sign),
                        OptionHandlerFactory.CreateBoolOption(
                            "useExtraThreadToDrainNtClose",
                            sign => sandboxConfiguration.UseExtraThreadToDrainNtClose = sign),
                        OptionHandlerFactory.CreateBoolOption(
                            "useFileContentTable",
                            sign => engineConfiguration.UseFileContentTable = sign),
                        OptionHandlerFactory.CreateBoolOption(
                            "useHardlinks",
                            sign => engineConfiguration.UseHardlinks = sign),
                        OptionHandlerFactory.CreateBoolOption(
                            "useHistoricalCpuUsageInfo",
                            sign => schedulingConfiguration.UseHistoricalCpuUsageInfo = sign),
                        OptionHandlerFactory.CreateBoolOption(
                            "useHistoricalRamUsageInfo",
                            sign => schedulingConfiguration.UseHistoricalRamUsageInfo = sign),
                        OptionHandlerFactory.CreateOption(
                            "useJournalForProbesMode",
                            opt => { /* Do nothing Office is still passing this flag even though it is deprecated. */ }),
                        OptionHandlerFactory.CreateBoolOption(
                            "useLargeNtClosePreallocatedList",
                            sign => sandboxConfiguration.UseLargeNtClosePreallocatedList = sign),
                        OptionHandlerFactory.CreateBoolOption(
                            "usePartialEvaluation",
                            sign => frontEndConfiguration.UsePartialEvaluation = sign),
                        OptionHandlerFactory.CreateBoolOption(
                            "validateDistribution",
                            sign => distributionConfiguration.ValidateDistribution = sign),
                        OptionHandlerFactory.CreateBoolOption(
                            "validateExistingFileAccessesForOutputs",
                            sign => { /* Do nothing Office and WDG are still passing this flag even though it is deprecated. */ }),
                        OptionHandlerFactory.CreateBoolOption(
                            "verifyCacheLookupPin",
                            sign => schedulingConfiguration.VerifyCacheLookupPin = sign),
                        OptionHandlerFactory.CreateOption(
                            "viewer",
                            opt => configuration.Viewer = CommandLineUtilities.ParseEnumOption<ViewerMode>(opt)),
                        OptionHandlerFactory.CreateBoolOption(
                            "vs",
                            sign => ideConfiguration.IsEnabled = sign),
                        OptionHandlerFactory.CreateBoolOption(
                            "vsOutputSrc",
                            sign => ideConfiguration.CanWriteToSrc = sign),
                        OptionHandlerFactory.CreateBoolOptionWithValue(
                            "warnAsError",
                            (opt, sign) =>
                            {
                                ParseInt32ListOption(opt, sign ? loggingConfiguration.WarningsAsErrors : loggingConfiguration.WarningsNotAsErrors);

                                // /warnAsError may be specified globally for all warnings, in which case it won't have any messages.
                                // In that case make sure the TreatWarningsAsErrors flag is set correctly
                                if (sign && loggingConfiguration.WarningsAsErrors.Count == 0)
                                {
                                    loggingConfiguration.TreatWarningsAsErrors = true;
                                }
                                else if (!sign && loggingConfiguration.WarningsNotAsErrors.Count == 0)
                                {
                                    loggingConfiguration.TreatWarningsAsErrors = false;
                                }
                            }
                        ),
                        /////////// ATTENTION
                        // When you insert new options, maintain the alphabetical order as much as possible, at least
                        // according to the long names of the options. For details, please read the constraints specified as notes
                        // at the top of this statement code.
                    }.SelectMany(x => x)
                        .OrderBy(opt => opt.OptionName, StringComparer.OrdinalIgnoreCase)
                        .ToArray();

#if DEBUG
                // Check that the table of m_handlers is properly sorted according to the options' long names.
                for (int i = 1; i < m_handlers.Length; i++)
                {
                    Contract.Assert(
                        string.Compare(m_handlers[i - 1].OptionName, m_handlers[i].OptionName, StringComparison.OrdinalIgnoreCase) <= 0,
                        "Option m_handlers must be sorted. Entry " + i + " = " + m_handlers[i].OptionName);
                }
#endif

                // Unsafe options that do not follow the correct enabled-is-unsafe configuration pattern.
                // These are options for which /unsafe_option- results in an unsafe mode.
                // This list should not grow! It exists to retroactively handle flags in use that we cannot change.
                var specialCaseUnsafeOptions =
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "unsafe_MonitorFileAccesses-",
                        "unsafe_UnexpectedFileAccessesAreErrors-",
                    };

                // Iterate through each argument, looking each argument up in the table.
                foreach (CommandLineUtilities.Option opt in cl.Options)
                {
                    int min = 0;
                    int limit = m_handlers.Length;
                    while (min < limit)
                    {
                        // This avoids overflow, while i = (min + limit) >> 1 could overflow.
                        int i = unchecked((int)(((uint)min + (uint)limit) >> 1));

                        int order = string.Compare(m_handlers[i].OptionName + m_handlers[i].Suffix, opt.Name, StringComparison.OrdinalIgnoreCase);
                        if (order < 0)
                        {
                            min = i + 1;
                        }
                        else
                        {
                            // Since i < limit, this is guaranteed to make progress.
                            limit = i;
                        }
                    }

                    // Check for equality
                    if (min < m_handlers.Length)
                    {
                        if (string.Equals(m_handlers[min].OptionName + m_handlers[min].Suffix, opt.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            // min it is
                            m_handlers[min].Action(opt);

                            // compile list of user-enabled unsafe options to log later
                            if (specialCaseUnsafeOptions.Contains(opt.Name) || (m_handlers[min].IsUnsafe && m_handlers[min].IsEnabled()))
                            {
                                configuration.CommandLineEnabledUnsafeOptions.Add(m_handlers[min].OptionName);
                            }

                            continue;
                        }
                    }

                    // unknown argument, fail
                    throw CommandLineUtilities.Error(Strings.Args_Args_NotRecognized, opt.Name);
                }

                foreach (string arg in cl.Arguments)
                {
                    startupConfiguration.ImplicitFilters.Add(arg);
                }

                // We require a config file for non-server mode builds and if the user does not pass /setupJournal argument
                if (configuration.Help == HelpLevel.None && Environment.GetEnvironmentVariable(Program.BuildXlAppServerConfigVariable) == null && !startupConfiguration.ConfigFile.IsValid)
                {
                    throw CommandLineUtilities.Error(Strings.Args_Args_NoConfigGiven);
                }

                // If incompatible flags were specified (/unsafe_UnexpectedFileAccessesAreErrors- and /failPipOnFileAccessError- ) fail.
                if (failPipOnFileAccessErrorSet && unsafeUnexpectedFileAccessesAreErrorsSet)
                {
                    throw CommandLineUtilities.Error(Strings.Args_FileAccessAsErrors_FailPipFlags);
                }

                // Validate logging configuration.
                ValidateLoggingConfiguration(loggingConfiguration);

                // FileSystemMode can be dynamic based on what build mode is being used
                if (sandboxConfiguration.FileSystemMode == FileSystemMode.Unset)
                {
                    // DScript is essentially always a partial graph. So use the minimal pip graph so the
                    // per-pip filesystem view is consistent build over build regardless of what's being built
                    sandboxConfiguration.FileSystemMode = FileSystemMode.RealAndMinimalPipGraph;
                }

                // Some behavior should be forced a particular way when run in CloudBuild. In general this list should
                // be kept relatively small in favor of configuration of default at a CloudBuild workflow layer, since
                // settings here are expensive to change and trump external behavior.
                if (configuration.InCloudBuild())
                {
                    configuration.Server = ServerMode.Disabled;
                    loggingConfiguration.RemoteTelemetry = RemoteTelemetry.EnabledAndNotify;
                    cacheConfiguration.CacheGraph = true;

                    // Forcefully disable incremental scheduling in CB.
                    schedulingConfiguration.IncrementalScheduling = false;

                    // if not explicitly disabled, enable user profile redirect and force the location
                    if (!enableProfileRedirect.HasValue || enableProfileRedirect.Value)
                    {
                        layoutConfiguration.RedirectedUserProfileJunctionRoot = AbsolutePath.Create(pathTable, RedirectedUserProfileLocationInCloudBuild);
                        enableProfileRedirect = true;
                    }
                }

                if (!OperatingSystemHelper.IsUnixOS)
                {
                    // if /enableProfileRedirect was set, RedirectedUserProfileJunctionRoot must have been set as well
                    // (either explicitly via /redirectedUserProfilePath argument or implicitly via /inCloudBuild flag)
                    if (enableProfileRedirect.HasValue && enableProfileRedirect.Value && !layoutConfiguration.RedirectedUserProfileJunctionRoot.IsValid)
                    {
                        throw CommandLineUtilities.Error(Strings.Args_ProfileRedirectEnabled_NoPathProvided);
                    }

                    if (!enableProfileRedirect.HasValue || enableProfileRedirect.HasValue && !enableProfileRedirect.Value)
                    {
                        layoutConfiguration.RedirectedUserProfileJunctionRoot = AbsolutePath.Invalid;
                    }
                }
                else
                {
                    // profile redirection only happens on Windows
                    layoutConfiguration.RedirectedUserProfileJunctionRoot = AbsolutePath.Invalid;
                }

                if (OperatingSystemHelper.IsUnixOS)
                {
                    // TODO: Non Windows OS doesn't support admin-required process external execution mode.
                    if (sandboxConfiguration.AdminRequiredProcessExecutionMode != AdminRequiredProcessExecutionMode.Internal)
                    {
                        throw CommandLineUtilities.Error(Strings.Args_AdminRequiredProcessExecutionMode_NotSupportedOnNonWindows, sandboxConfiguration.AdminRequiredProcessExecutionMode.ToString());
                    }
                }

                // Disable reuseEngineState (enabled by default) in case of /server- or /cacheGraph- (even if /reuseEngineState+ is passed)
                if (configuration.Server == ServerMode.Disabled || !cacheConfiguration.CacheGraph)
                {
                    engineConfiguration.ReuseEngineState = false;
                }

                if (ideConfiguration.IsEnabled)
                {
                    // Disable incrementalScheduling if the /vs is passed. IDE generator needs to catch all scheduled nodes and should not ignore the skipped ones due to the incremental scheduling
                    schedulingConfiguration.IncrementalScheduling = false;
                }

                // Disable any options that may prevent cache convergence
                if (engineConfiguration.Converge)
                {
                    schedulingConfiguration.IncrementalScheduling = false;
                }

                // Disable any option that may interfere with determinism validation
                if (cacheConfiguration.DeterminismProbe)
                {
                    schedulingConfiguration.IncrementalScheduling = false;
                }

                arguments = configuration;

                return true;
            }
            catch (Exception ex)
            {
                // TODO: The IToolParameterValue interface doesn't fit BuildXL well since it makes the command line
                // argument code report errors. That should really be a responsibility of the hosting application. We
                // can get by as-is for now since in server mode the client application always parses the command line
                // and the server can reasonably assume there won't be command line errors by the time it is invoked.
                m_console.WriteOutputLine(MessageLevel.Error, ex.GetLogEventMessage());
                m_console.WriteOutputLine(MessageLevel.Error, string.Empty);
                m_console.WriteOutputLine(MessageLevel.Error, Environment.CommandLine);
                arguments = null;
                return false;
            }
        }

        /// <summary>
        /// Validates logging configuration.
        /// </summary>
        /// <remarks>
        /// This method is only used to validate logging configuration that is used before BuildXL engine is created.
        /// All other configurations should be validated after the engine is created and use the PopulateAndValidateConfiguration method.
        /// </remarks>
        private static void ValidateLoggingConfiguration(ILoggingConfiguration loggingConfiguration)
        {
            Contract.Requires(loggingConfiguration != null);

            if (!string.IsNullOrEmpty(loggingConfiguration.RelatedActivityId))
            {
                if (!Guid.TryParse(loggingConfiguration.RelatedActivityId, out _))
                {
                    throw CommandLineUtilities.Error(Strings.Args_Malformed_RelatedActivityGuid);
                }
            }
        }

        private static void HandleLazyOutputMaterializationOption(
            CommandLineUtilities.Option opt,
            bool sign,
            BuildXL.Utilities.Configuration.Mutable.ScheduleConfiguration scheduleConfiguration)
        {
            if (string.IsNullOrEmpty(opt.Value))
            {
                scheduleConfiguration.EnableLazyOutputMaterialization = sign;
            }
            else
            {
                scheduleConfiguration.RequiredOutputMaterialization =
                    CommandLineUtilities.ParseEnumOption<RequiredOutputMaterialization>(opt);
            }
        }

        private static void ParseCacheMissAnalysisOption(
            CommandLineUtilities.Option opt,
            bool sign,
            BuildXL.Utilities.Configuration.Mutable.LoggingConfiguration loggingConfiguration,
            PathTable pathTable)
        {
            if (string.IsNullOrEmpty(opt.Value))
            {
                loggingConfiguration.CacheMissAnalysisOption = sign ? CacheMissAnalysisOption.LocalMode() : CacheMissAnalysisOption.Disabled();
            }
            else if (opt.Value.StartsWith("[") && opt.Value.EndsWith("]"))
            {
                loggingConfiguration.CacheMissAnalysisOption = CacheMissAnalysisOption.RemoteMode(
                    opt.Value.Substring(1, opt.Value.Length - 2)
                        .Replace(" ", string.Empty)
                        .Split(':'));
            }
            else
            {
                loggingConfiguration.CacheMissAnalysisOption = CacheMissAnalysisOption.CustomPathMode(CommandLineUtilities.ParsePathOption(opt, pathTable));
            }
        }

        private static void HandleForceSkipDependenciesOption(
            CommandLineUtilities.Option opt,
            bool sign,
            BuildXL.Utilities.Configuration.Mutable.ScheduleConfiguration scheduleConfiguration)
        {
            if (string.IsNullOrEmpty(opt.Value))
            {
                scheduleConfiguration.ForceSkipDependencies = sign ? ForceSkipDependenciesMode.Always : ForceSkipDependenciesMode.Disabled;
            }
            else
            {
                scheduleConfiguration.ForceSkipDependencies =
                    CommandLineUtilities.ParseEnumOption<ForceSkipDependenciesMode>(opt);
            }
        }

        private static void HandleStoreFingerprintsOption(
            CommandLineUtilities.Option opt,
            bool sign,
            BuildXL.Utilities.Configuration.Mutable.LoggingConfiguration loggingConfiguration)
        {
            if (!string.IsNullOrWhiteSpace(opt.Value))
            {
                loggingConfiguration.FingerprintStoreMode = CommandLineUtilities.ParseEnumOption<FingerprintStoreMode>(opt);
                loggingConfiguration.StoreFingerprints = true;
            }
            else
            {
                loggingConfiguration.StoreFingerprints = sign;
            }
        }

        private static string ParseLogPrefix(CommandLineUtilities.Option opt)
        {
            string logPrefix = CommandLineUtilities.ParseStringOption(opt);
            if (!PathAtom.Validate((StringSegment)logPrefix))
            {
                throw CommandLineUtilities.Error(string.Format(CultureInfo.CurrentCulture, Strings.Args_LogPrefix_Invalid, logPrefix));
            }

            return logPrefix;
        }

        private static void ParsePathOption(CommandLineUtilities.Option opt, PathTable pathTable, List<AbsolutePath> list)
        {
            list.Add(CommandLineUtilities.ParsePathOption(opt, pathTable));
        }

        private static void ParseStringOption(CommandLineUtilities.Option opt, List<string> list)
        {
            list.Add(CommandLineUtilities.ParseStringOption(opt));
        }

        private static void ParseTranslateDirectoriesOption(PathTable pathTable, CommandLineUtilities.Option opt, List<TranslateDirectoryData> list)
        {
            list.Add(ParseTranslatePathOption(pathTable, opt));
        }

        private static void ParseInt32ListOption(CommandLineUtilities.Option opt, List<int> list)
        {
            ParseInt32ListOption(opt.Value, opt.Name, list);
        }

        private static void ParseInt32ListOption(string value, string optionName, List<int> list)
        {
            foreach (var item in value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!int.TryParse(item, out int parsed))
                {
                    throw CommandLineUtilities.Error(
                        "The /{0} argument contains an item '{1} that could not be converted to an integer event ID.",
                        optionName,
                        item);
                }

                list.Add(parsed);
            }
        }

        private static void ParsePropertyOption(CommandLineUtilities.Option opt, Dictionary<string, string> map)
        {
            Contract.Requires(map != null);

            var keyValuePair = CommandLineUtilities.ParseKeyValuePair(opt);
            if (!string.IsNullOrEmpty(keyValuePair.Value))
            {
                map[keyValuePair.Key] = keyValuePair.Value;
            }
            else
            {
                // a blank property specified after an existing one should blank it out
                map.Remove(keyValuePair.Key);
            }
        }

        private static void ParseKeyValueOption(
            CommandLineUtilities.Option opt,
            PathTable pathTable,
            Dictionary<string, AbsolutePath> map)
        {
            Contract.Requires(map != null);

            var keyValuePair = CommandLineUtilities.ParseKeyValuePair(opt);
            map[keyValuePair.Key] = CommandLineUtilities.GetFullPath(keyValuePair.Value, opt, pathTable);
        }

        private static void ParseKeyValueOption(
            CommandLineUtilities.Option opt,
            PathTable pathTable,
            Dictionary<AbsolutePath, IReadOnlyList<int>> map)
        {
            Contract.Requires(map != null);

            var keyValuePair = CommandLineUtilities.ParseKeyValuePair(opt);

            var key = CommandLineUtilities.GetFullPath(keyValuePair.Key, opt, pathTable);
            if (!map.TryGetValue(key, out IReadOnlyList<int> values))
            {
                values = new List<int>(0);
            }

            var newValues = new List<int>(values);
            ParseInt32ListOption(keyValuePair.Value, opt.Name, newValues);

            map[key] = newValues;
        }

        /// <summary>
        /// Parse an option that contains from and to path translation.
        /// </summary>
        /// <remarks>
        /// Expected format. /name:from&lt;to or /name:from::to
        /// We support currently two from/to path separators.
        /// It is recommended to use "::" one.
        /// The '&lt;' is supported for backward compatibility.
        ///
        /// </remarks>
        public static TranslateDirectoryData ParseTranslatePathOption(PathTable pathTable, CommandLineUtilities.Option opt)
        {
            var value = opt.Value;
            if (string.IsNullOrEmpty(value))
            {
                throw CommandLineUtilities.Error("The value provided for the /{0} argument is invalid.", opt.Name);
            }

            var firstLessThan = value.IndexOf("<", StringComparison.OrdinalIgnoreCase);
            if (firstLessThan == 0)
            {
                throw CommandLineUtilities.Error(
                    "The value '{0}' provided for the /{1} argument is invalid. It can't start with a '<' separator",
                    value,
                    opt.Name);
            }

            bool usingDblColon = false;

            if (firstLessThan < 0)
            {
                var firstDblColon = value.IndexOf("::", StringComparison.OrdinalIgnoreCase);
                if (firstDblColon == 0)
                {
                    throw CommandLineUtilities.Error(
                        "The value '{0}' provided for the /{1} argument is invalid. It can't start with a '::' separator",
                        value,
                        opt.Name);
                }
                else if (firstDblColon < 0)
                {
                    throw CommandLineUtilities.Error(
                        "The value '{0}' provided for the /{1} argument is invalid. It should contain a '::' or '<' separator",
                        value,
                        opt.Name);
                }
                else
                {
                    // found a colon as a separator of the fromPath and toPath strings.
                    firstLessThan = firstDblColon;
                    usingDblColon = true;
                }
            }

            var key = value.Substring(0, firstLessThan);
            if (firstLessThan >= value.Length - 1)
            {
                throw CommandLineUtilities.Error(
                    "The value '{0}' provided for the /{1} argument is invalid. It should have a valid translateTo path",
                    value,
                    opt.Name);
            }

            bool success = AbsolutePath.TryCreate(pathTable, key, out AbsolutePath fromPath);
            if (!success)
            {
                throw CommandLineUtilities.Error(
                    "The value '{0}' provided for the /{1} argument is invalid. It should have a valid translateFrom path",
                    key,
                    opt.Name);
            }

            success = AbsolutePath.TryCreate(pathTable, value.Substring(firstLessThan + 1 + (usingDblColon ? 1 : 0)), out AbsolutePath toPath);
            if (!success)
            {
                throw CommandLineUtilities.Error(
                    "The value '{0}' provided for the /{1} argument is invalid. It should have a valid translateTo path",
                    value.Substring(firstLessThan + 1 + (usingDblColon ? 1 : 0)),
                    opt.Name);
            }

            return new TranslateDirectoryData(opt.Value, fromPath, toPath);
        }

        private static void HandleLogOptionWithPath(
            CommandLineUtilities.Option opt,
            bool enable,
            PathTable pathTable,
            Action<bool> enableSetter,
            Action<AbsolutePath> pathSetter)
        {
            enableSetter(enable);
            if (!string.IsNullOrWhiteSpace(opt.Value))
            {
                if (enable)
                {
                    pathSetter(CommandLineUtilities.ParsePathOption(opt, pathTable));
                }
                else
                {
                    throw CommandLineUtilities.Error("The /{0} argument disables logging; a filename cannot be provided.", opt.Name);
                }
            }
            else if (!enable)
            {
                pathSetter(AbsolutePath.Invalid);
            }
        }

        /// <summary>
        /// Custom argument parsing for service location for now.
        /// </summary>
        private void HandleExperimentalOption(
            CommandLineUtilities.Option opt,
            BuildXL.Utilities.Configuration.Mutable.ExperimentalConfiguration experimentalOptions,
            BuildXL.Utilities.Configuration.Mutable.EngineConfiguration engineConfiguration,
            BuildXL.Utilities.Configuration.Mutable.ScheduleConfiguration scheduleConfiguration,
            BuildXL.Utilities.Configuration.Mutable.SandboxConfiguration sandboxConfiguration,
            BuildXL.Utilities.Configuration.Mutable.LoggingConfiguration loggingConfiguration,
            BuildXL.Utilities.Configuration.Mutable.IdeConfiguration buildXlConfiguration,
            BuildXL.Utilities.Configuration.Mutable.FrontEndConfiguration frontEndConfiguration)
        {
            (string, bool) experimentalOptionAndValue = CommandLineUtilities.ParseStringOptionWithBoolSuffix(opt, boolDefault: true);

            switch (experimentalOptionAndValue.Item1.ToUpperInvariant())
            {
                case "DONTBRINGOUTPUTSTOMASTER":
                    // deprecated (no effect)
                    break;
                case "USEHARDLINKS":
                    // Deprecated alias: Hardlinks are no longer experimental, so we treat this as an alias for the new /useHardLinks option.
                    engineConfiguration.UseHardlinks = experimentalOptionAndValue.Item2;
                    break;
                case "SCANCHANGEJOURNAL":
                    // Deprecated alias: ScanChangeJournal are no longer experimental, so we treat this as an alias for the new /scanChangeJournal option.
                    engineConfiguration.ScanChangeJournal = experimentalOptionAndValue.Item2;
                    break;
                case "INCREMENTALSCHEDULING":
                    scheduleConfiguration.IncrementalScheduling = experimentalOptionAndValue.Item2;
                    break;
                case "USESUBSTTARGETFORCACHE":
                    experimentalOptions.UseSubstTargetForCache = experimentalOptionAndValue.Item2;
                    break;

                // TODO: These options are left to give consumers window to remove them from wrapper scripts.
                case "TWOPHASEFINGERPRINTING":
                case "NEWCACHE":
                case "HASHSYMLINKSASTARGETPATH":
                    break;
                case "ANALYZEDEPENDENCYVIOLATIONS":
                    // Deprecated
                    break;
                case "FORCESTACKOVERFLOW":
                    ForceStackOverflow();
                    break;
                case "FORCECONTRACTFAILURE":
                    experimentalOptions.ForceContractFailure = experimentalOptionAndValue.Item2;
                    break;
                case "FORCEREADONLYFORREQUESTEDREADWRITE":
                    sandboxConfiguration.ForceReadOnlyForRequestedReadWrite = experimentalOptionAndValue.Item2;
                    break;
                case "FANCYCONSOLE":
                    loggingConfiguration.FancyConsole = experimentalOptionAndValue.Item2;
                    break;
                case "USEWORKSPACE":
                    ReportObsoleteOption(opt);
                    break;
                case "USELEGACYDOMINOSCRIPT":
                    // Office-specific flag (at this point) to isolate them from changes
                    frontEndConfiguration.UseLegacyOfficeLogic = experimentalOptionAndValue.Item2;
                    break;

                case "USEDOMINOSCRIPTV2":
                    // Only Office is passing this at this moment.
                    // Deprecated option: Nobody should be passing this anymore. Its on by default now. Since some are still passing this variable we don't use ReportObsoleteOption yet.
                    if (!experimentalOptionAndValue.Item2)
                    {
                        throw CommandLineUtilities.Error("The /exp:useDominoScriptv2 option has been deprecated. Remove any usage of this variable, it has no effect anymore");
                    }
                    break;
                case "AUTOMATICALLYEXPORTNAMESPACES":
                    // Deprecated option: Nobody should be passing this anymore. Its on by default now.
                    break;
                case "ADAPTIVEIO":
                    scheduleConfiguration.AdaptiveIO = experimentalOptionAndValue.Item2;
                    break;
                case "USEGRAPHPATCHING":
                    frontEndConfiguration.UseGraphPatching = experimentalOptionAndValue.Item2;
                    break;
                case "CONSTRUCTANDSAVEBINDINGFINGERPRINT":
                    frontEndConfiguration.ConstructAndSaveBindingFingerprint = experimentalOptionAndValue.Item2;
                    break;
                case "USESPECPUBLICFACADEANDASTWHENAVAILABLE":
                    frontEndConfiguration.UseSpecPublicFacadeAndAstWhenAvailable = experimentalOptionAndValue.Item2;
                    break;
                case "CONVERTPATHLIKELITERALSATPARSETIME":
                    // Deprecated option: Nobody should be passing this anymore. Its on by default now.
                    break;
                case "ESCAPEIDENTIFIERS":
                    // Deprecated option: Nobody should be passing this anymore. Its on by default now.
                    break;
                case "GRAPHAGNOSTICINCREMENTALSCHEDULING":
                    // Incremental scheduling is by default graph agnostic. One can use this option to make it graph inagnostic.
                    scheduleConfiguration.GraphAgnosticIncrementalScheduling = experimentalOptionAndValue.Item2;
                    break;
                case "CREATEHANDLEWITHSEQUENTIALSCANONHASHINGOUTPUTFILES":
                    scheduleConfiguration.CreateHandleWithSequentialScanOnHashingOutputFiles = experimentalOptionAndValue.Item2;
                    break;
                case "RUNINCONTAINERANDALLOWDOUBLEWRITES":
                    sandboxConfiguration.ContainerConfiguration.RunInContainer = experimentalOptionAndValue.Item2;
                    sandboxConfiguration.ContainerConfiguration.ContainerIsolationLevel = ContainerIsolationLevel.IsolateAllOutputs;
                    sandboxConfiguration.UnsafeSandboxConfigurationMutable.DoubleWritePolicy = DoubleWritePolicy.UnsafeFirstDoubleWriteWins;
                    break;
                default:
                    throw CommandLineUtilities.Error(Strings.Args_Experimental_UnsupportedValue, experimentalOptionAndValue.Item1);
            }
        }

        private void ReportObsoleteOption(CommandLineUtilities.Option opt)
        {
            m_console.WriteOutputLine(MessageLevel.Warning, I($"Option '{opt.Name}' is obsolete and has no effect any longer."));
        }

        /// <summary>
        /// Forces a stack overflow to induce a crash. Used for debugging crash handling.
        /// </summary>
        // ReSharper disable once FunctionRecursiveOnAllPaths
        private static bool ForceStackOverflow()
        {
            return ForceStackOverflow();
        }

        /// <summary>
        /// Custom argument parsing for service location for now.
        /// </summary>
        private static void ParseServiceLocation(CommandLineUtilities.Option opt, List<IDistributionServiceLocation> workers)
        {
            if (!string.IsNullOrWhiteSpace(opt.Value))
            {
                string[] remoteLocationParts = opt.Value.Split(s_serviceLocationSeparator, StringSplitOptions.RemoveEmptyEntries);
                if (remoteLocationParts.Length != 2)
                {
                    throw CommandLineUtilities.Error(Strings.Args_DistributedBuildWorker_InvalidServiceLocation, opt.Value);
                }

                if (!ushort.TryParse(remoteLocationParts[1], out ushort port))
                {
                    throw CommandLineUtilities.Error(Strings.Args_DistributedBuildWorker_InvalidServiceLocation, opt.Value);
                }

                workers.Add(
                    new BuildXL.Utilities.Configuration.Mutable.DistributionServiceLocation
                    {
                        IpAddress = remoteLocationParts[0],
                        BuildServicePort = port,
                    });

                return;
            }

            throw CommandLineUtilities.Error(Strings.Args_DistributedBuildWorker_InvalidServiceLocation, opt.Value);
        }

        private static void HandleArtificialCacheMissOption(
            CommandLineUtilities.Option opt,
            BuildXL.Utilities.Configuration.Mutable.CacheConfiguration cacheConfiguration)
        {
            if (cacheConfiguration.ArtificialCacheMissOptions != null)
            {
                throw CommandLineUtilities.Error(Strings.Args_ArtificialCacheMiss_AlreadyProvided);
            }

            var missOptions = TryParseArtificialCacheMissOptions(opt.Value);
            if (missOptions == null)
            {
                throw CommandLineUtilities.Error(Strings.Args_ArtificialCacheMiss_Invalid, opt.Value);
            }

            cacheConfiguration.ArtificialCacheMissOptions = missOptions;
        }

        private static void HandleLoadGraphOption(
            CommandLineUtilities.Option opt,
            PathTable pathTable,
            BuildXL.Utilities.Configuration.Mutable.CacheConfiguration cacheConfiguration)
        {
            if (ContentHashingUtilities.TryParse(opt.Value, out ContentHash identifierFingerprint))
            {
                cacheConfiguration.CachedGraphIdToLoad = identifierFingerprint.ToString();
            }
            else if (string.Equals(opt.Value, LastBuiltCachedGraphName, StringComparison.OrdinalIgnoreCase))
            {
                cacheConfiguration.CachedGraphLastBuildLoad = true;
            }
            else
            {
                cacheConfiguration.CachedGraphPathToLoad = CommandLineUtilities.ParsePathOption(opt, pathTable);
            }
        }

        /// <summary>
        /// Tries to parse the string representation of artificial cache miss syntax. Returns null on parse failure
        /// </summary>
        private static BuildXL.Utilities.Configuration.Mutable.ArtificialCacheMissConfig TryParseArtificialCacheMissOptions(string value)
        {
            var option = BuildXL.Pips.ArtificialCacheMissOptions.TryParse(value, CultureInfo.InvariantCulture);

            return new BuildXL.Utilities.Configuration.Mutable.ArtificialCacheMissConfig
                   {
                       Rate = (ushort)(option.Rate * ushort.MaxValue),
                       IsInverted = option.IsInverted,
                       Seed = option.Seed,
                   };
        }

        internal static KeyValuePair<HelpLevel, int> ParseHelpOption(CommandLineUtilities.Option opt)
        {
            if (string.IsNullOrWhiteSpace(opt.Value))
            {
                return new KeyValuePair<HelpLevel, int>(HelpLevel.Standard, 0);
            }

            string trimmed = opt.Value.TrimStart('d');
            trimmed = trimmed.TrimStart('x');

            return int.TryParse(trimmed, out int dxCode)
                ? new KeyValuePair<HelpLevel, int>(HelpLevel.DxCode, dxCode)
                : new KeyValuePair<HelpLevel, int>(CommandLineUtilities.ParseEnumOption<HelpLevel>(opt), 0);
        }

        public void Dispose()
        {
            if (m_shouldDisposeConsole)
            {
                m_console.Dispose();
            }
        }

        /// <summary>
        /// Used to obtain a list of all available options for testing.
        /// Returned a list of string names to avoid exposing OptionHandler as a public class.
        /// </summary>
        public IEnumerable<string> GetParsedOptionNames()
        {
            return m_handlers.Where(handler => !handler.Inactive).Select(handler => handler.OptionName);
        }
    }
}
