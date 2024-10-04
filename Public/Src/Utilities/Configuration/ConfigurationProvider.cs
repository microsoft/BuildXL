// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Utilities.Core;
using static BuildXL.Utilities.Core.FormattableStringEx;

namespace BuildXL.Utilities.Configuration
{
    /// <nodoc />
    public static class ConfigurationProvider
    {
        /// <summary>
        /// Returns a mutable config with its values initialized according with the specified infra.
        /// </summary>
        public static CommandLineConfiguration GetMutableDefaultConfig(Infra infra) => infra switch
        {
            Infra.Ado => GetAdoConfig(),
            Infra.CloudBuild => GetCbConfig(),
            // Any other infra value (Dev or even invalid value) results in a default configuration.
            _ => new CommandLineConfiguration() { Infra = Infra.Developer },
        };

        /// <summary>
        /// Takes a configuration and applies necessary overrides based on the configuration's Infra value.
        /// </summary>
        /// <remarks>
        /// When modifying the config object, please review this entire method as well as BuildXLEngine's PopulateAndValidateConfiguration and
        /// PopulateLoggingAndLayoutConfiguration methods to make sure that there is no foul interplay between feature A turning feature X on
        /// and feature B turning feature X off, i.e., what should happen when A and B are both enabled.
        /// </remarks>
        public static void ApplyRequiredOverrides(ConfigurationImpl config)
        { 
            switch(config.Infra)
            {
                // Currently, there are overrides only in CB infra.
                case Infra.CloudBuild:
                    ApplyCbOverride(config);
                    break;
                default:
                    break;
            }
        }

        private static CommandLineConfiguration GetCbConfig()
        {
            var config = new CommandLineConfiguration()
            {
                Infra = Infra.CloudBuild,
            };

            config.Server = ServerMode.Disabled;
            config.Logging.RemoteTelemetry = RemoteTelemetry.EnabledAndNotify;
            config.Cache.CacheGraph = true;

            config.Schedule.IncrementalScheduling = false;
            config.FrontEnd.EnableCredScan = true;

            // Allow a longer Aria telemetry flush time in CloudBuild since we're more willing to wait at the tail of builds there
            config.Logging.RemoteTelemetryFlushTimeout = TimeSpan.FromMinutes(1);

            // Unless explicitly specified, async logging is enabled by default in CloudBuild
            config.Logging.EnableAsyncLogging = true;

            config.Logging.SaveFingerprintStoreToLogs = true;

            config.Logging.DisplayWarningErrorTime = true;
            config.Logging.EnableCloudBuildEtwLoggingIntegration = true;

            // We can reuse weakIdentity for source files in CloudBuild where we know that there are not recent changes in those files.
            config.Cache.AllowReuseOfWeakIdenityForSourceFiles = true;

            config.Startup.EnsurePropertiesWhenRunInCloudBuild();
            config.Schedule.StopOnFirstInternalError = true;

            config.Engine.VerifyJunctionsDoNotConflictWithDirectoryTranslations = true;

            config.Engine.TrackBuildsInUserFolder = false;

            config.Schedule.MinimumDiskSpaceForPipsGb = 5;

            // Enable historic ram and CPU based throttling.
            config.Schedule.UseHistoricalRamUsageInfo = true;
            config.Schedule.UseHistoricalCpuUsageInfo = true;

            config.Cache.FileContentTableEntryTimeToLive = 100;

            config.Logging.ReplayWarnings = false;

            config.Cache.HistoricMetadataCache = HistoricMetadataCacheMode.HashToHashAndMetadata;

            config.Logging.StoreFingerprints = false;

            // The default is EmptyWorkingSet option due to the large page file size on CB machines.
            config.Schedule.ManageMemoryMode = ManageMemoryMode.EmptyWorkingSet;

            // Fire forget materialize output is enabled by default in CloudBuild as it improves the perf during meta build.
            config.Distribution.FireForgetMaterializeOutput = true;

            // We assume clean outputs in CloudBuild builds.
            config.Engine.AssumeCleanOutputs = true;

            return config;
        }

        private static CommandLineConfiguration GetAdoConfig()
        {
            var config = new CommandLineConfiguration()
            {
                Infra = Infra.Ado,
            };

            config.Distribution.EarlyWorkerRelease = true;
            config.Distribution.EarlyWorkerReleaseMultiplier = 1;
            // Machine are stateless in ADO, no point tracking builds.
            config.Engine.TrackBuildsInUserFolder = false;
            config.Logging.OptimizeConsoleOutputForAzureDevOps = true;
            config.Logging.StoreFingerprints = true;
            config.Sandbox.TimeoutMultiplier = 3;
            config.FrontEnd.EnableCredScan = true;
            config.Sandbox.RetryOnAzureWatsonExitCode = true;
            config.Server = ServerMode.Disabled;

            config.Schedule.UseHistoricalCpuUsageInfo = false;
            config.Schedule.UseHistoricalRamUsageInfo = true;
            config.Schedule.EnableLessAggressiveMemoryProjection = true;
            config.Schedule.MaximumRamUtilizationPercentage = 98;
            config.Schedule.DeprioritizeOnSemaphoreConstraints = true;

            return config;
        }

        private static void ApplyCbOverride(ConfigurationImpl mutableConfig)
        {
            // The default in CB is 'false'. A user might have overridden it via a command line argument.
            // Force 'false' value.
            mutableConfig.Engine.TrackBuildsInUserFolder = false;

            // Enable fail fast for null reference exceptions caught by
            // ExceptionUtilities.IsUnexpectedException
            EngineEnvironmentSettings.FailFastOnNullReferenceException.Value = true;
            EngineEnvironmentSettings.SkipExtraneousPins.TrySet(true);

            mutableConfig.Engine.ScanChangeJournal = false;

            // lazy scrubbing is only meant for speeding up single-machine dev builds
            mutableConfig.Schedule.UnsafeLazySODeletion = false;

            // Minimize output materialization in cloudbuild
            mutableConfig.Schedule.EnableLazyWriteFileMaterialization = true;
            mutableConfig.Schedule.WriteIpcOutput = false;

            // Use compression for graph files to greatly reduce the size
            mutableConfig.Engine.CompressGraphFiles = true;

            // Ensure that historic perf data is retrieved from cache because engine cache
            // can be reused for multiple locations
            mutableConfig.Schedule.ForceUseEngineInfoFromCache = true;

            mutableConfig.Cache.CacheGraph = true;

            mutableConfig.Schedule.ScheduleMetaPips = false;

            // Add additional context to environment fingerprint
            foreach (var entry in mutableConfig.Logging.TraceInfo)
            {
                if (entry.Key.Equals(TraceInfoExtensions.CustomFingerprint, StringComparison.OrdinalIgnoreCase))
                {
                    mutableConfig.Schedule.EnvironmentFingerprint += I($"|{TraceInfoExtensions.CustomFingerprint}:{entry.Value}");
                }
                else if (entry.Key.Equals(TraceInfoExtensions.CloudBuildQueue, StringComparison.OrdinalIgnoreCase))
                {
                    mutableConfig.Schedule.EnvironmentFingerprint += I($"|{TraceInfoExtensions.CloudBuildQueue}:{entry.Value}");
                }
            }

            if (mutableConfig.Logging.CacheMissAnalysisOption.Mode == CacheMissMode.Local)
            {
                // BuildXL should not use local fingerprintstore for cache miss analysis.
                // Because you do not know how old is that fingerprintstore, the data is not really useful.
                mutableConfig.Logging.CacheMissAnalysisOption.Mode = CacheMissMode.Disabled;
            }

            mutableConfig.Sandbox.RetryOnAzureWatsonExitCode = true;

            // Spec cache is disabled as most builds are happening on SSDs.
            mutableConfig.Cache.CacheSpecs = SpecCachingOption.Disabled;

            if (mutableConfig.Logging.Environment == ExecutionEnvironment.OsgLab)
            {
                // For Cosine builds in CloudBuild, enable delayedCacheLookup by default.
                if (mutableConfig.Schedule.DelayedCacheLookupMaxMultiplier == null &&
                    mutableConfig.Schedule.DelayedCacheLookupMinMultiplier == null)
                {
                    mutableConfig.Schedule.DelayedCacheLookupMaxMultiplier = 2;
                    mutableConfig.Schedule.DelayedCacheLookupMinMultiplier = 1;
                }

                // For Cosine builds, IsObsoleteCheck is very expensive, so we disable it.
                // Cosine specs are automatically generated, so that check was not necessary.
                mutableConfig.FrontEnd.DisableIsObsoleteCheckDuringConversion = true;
            }

            // Since VFS lives inside BXL process currently. Disallow on office enlist and meta build because
            // they materialize files which would be needed by subsequent invocations and thus requires VFS to
            // span multiple invocations. Just starting at Product build is sufficient.
            if (mutableConfig.Logging.Environment == ExecutionEnvironment.OfficeMetaBuildLab
                || mutableConfig.Logging.Environment == ExecutionEnvironment.OfficeEnlistmentBuildLab)
            {
                mutableConfig.Cache.VfsCasRoot = AbsolutePath.Invalid;
            }

            // Unless otherwise specified, distributed metabuilds in CloudBuild should replicate outputs to all machines
            // TODO: Remove this once reduced metabuild materialization is fully tested
            if (mutableConfig.Distribution.ReplicateOutputsToWorkers == null
                && mutableConfig.Logging.Environment == ExecutionEnvironment.OfficeMetaBuildLab
                && mutableConfig.Distribution.BuildRole.IsOrchestrator())
            {
                mutableConfig.Distribution.ReplicateOutputsToWorkers = true;
            }

            // When running in cloudbuild we want to ignore the user setting the interactive flag
            // and force it to be false since we never want to pop up UI there.
            mutableConfig.Interactive = false;

            // Although this is a 'default' and not an override, we are not setting these values in ConfigurationProvider
            // because we need RemoteWorkerCount.
            if (!mutableConfig.Schedule.MaxWorkersPerModule.HasValue)
            {
                // Module affinity is enabled by default in CloudBuild builds.
                mutableConfig.Schedule.MaxWorkersPerModule = mutableConfig.Distribution.RemoteWorkerCount + 1;
                mutableConfig.Schedule.ModuleAffinityLoadFactor = 1;
            }
        }
    }
}
