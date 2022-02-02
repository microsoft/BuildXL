// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Threading;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Defines settings initialized from environment variables
    /// </summary>
    public static class EngineEnvironmentSettings
    {
        /// <nodoc />
        public static readonly Setting<int?> DesiredCommitPercentToFreeSlack = CreateSetting("BuildXLDesiredCommitPercentToFreeSlack", value => ParseInt32(value));

        /// <nodoc />
        public static readonly Setting<int?> DesiredRamPercentToFreeSlack = CreateSetting("BuildXLDesiredRamPercentToFreeSlack", value => ParseInt32(value));

        /// <nodoc />
        public static readonly Setting<bool> DisableUseAverageCountersForResume = CreateSetting("BuildXLDisableUseAverageCountersForResume", value => value == "1");

        /// <nodoc />
        public static readonly Setting<bool> SetMaxWorkingSetToMin = CreateSetting("BuildXLSetMaxWorkingSetToMin", value => value == "1");

        /// <nodoc />
        public static readonly Setting<bool> SetMaxWorkingSetToPeakBeforeResume = CreateSetting("BuildXLSetMaxWorkingSetToPeakBeforeResume", value => value == "1");

        /// <summary>
        /// The maximum number pips to perform the on-the-fly cache miss analysis
        /// </summary>
        public static readonly Setting<int> MaxNumPipsForCacheMissAnalysis = CreateSetting("MaxNumPipsForCacheMissAnalysis", value => ParseInt32(value) ?? 1000);

        /// <summary>
        /// The maximum number of RPC messages to batch together for worker/orcehstrator communication
        /// </summary>
        public static readonly Setting<int> MaxMessagesPerBatch = CreateSetting("MaxMessagesPerBatch", value => ParseInt32(value) ?? 1000);

        /// <summary>
        /// Defines whether BuildXL should launch the debugger after a particular engine phase.
        /// </summary>
        public static readonly Setting<EnginePhases?> LaunchDebuggerAfterPhase = CreateSetting("BuildXLDebugAfterPhase", value => ParsePhase(Environment.GetEnvironmentVariable("BuildXLDebugAfterPhase")));

        /// <summary>
        /// Defines optional text used to salt pip fingerprints.
        /// Unspecified means salt is not added to fingerprint.
        /// '*' corresponds to using a random guid as the salt.
        /// Otherwise, specified text is added to fingerprint as salt.
        /// </summary>
        public static readonly Setting<string> DebugFingerprintSalt = CreateSetting("BUILDXL_FINGERPRINT_SALT", value => ProcessFingerprintSalt(value));

        /// <summary>
        /// Defines optional text used to salt graph fingerprints.
        /// Unspecified means salt is not added to fingerprint.
        /// '*' corresponds to using a random guid as the salt.
        /// Otherwise, specified text is added to fingerprint as salt.
        /// </summary>
        public static readonly Setting<string> DebugGraphFingerprintSalt = CreateSetting("BUILDXL_GRAPH_FINGERPRINT_SALT", value => ProcessFingerprintSalt(value));

        /// <summary>
        /// Defines optional text used to salt a historic metadata cache fingerprint.
        /// Unspecified means salt is not added to fingerprint.
        /// '*' corresponds to using a random guid as the salt.
        /// Otherwise, specified text is added to fingerprint as salt.
        /// </summary>
        public static readonly Setting<string> DebugHistoricMetadataCacheFingerprintSalt = CreateSetting("BUILDXL_HISTORIC_METADATA_CACHE_FINGERPRINT_SALT", value => ProcessFingerprintSalt(value));

        /// <summary>
        /// Path pointing to VM command proxy needed for build in VM feature.
        /// </summary>
        public static readonly Setting<string> VmCommandProxyPath = CreateSetting("BUILDXL_VMCOMMANDPROXY_PATH", value => value);

        /// <summary>
        /// Directory where AnyBuild client is installed.
        /// </summary>
        /// <remarks>
        /// This property can be set to override the default AnyBuild client installation directory. AnyBuild client is needed
        /// for process remoting via AnyBuild.
        /// </remarks>
        public static readonly Setting<string> AnyBuildInstallDir = CreateSetting("BUILDXL_ANYBUILD_CLIENT_INSTALL_DIR", value => value);

        /// <summary>
        /// Extra arguments to be passed to AnyBuild daemon for process remoting.
        /// </summary>
        public static readonly Setting<string> AnyBuildExtraArgs = CreateSetting("BUILDXL_ANYBUILD_EXTRA_ARGS", value => value);

        /// <summary>
        /// Indicates whether the application should fail fast on null reference exceptions
        /// </summary>
        public static readonly Setting<bool> FailFastOnNullReferenceException = CreateSetting("FailFastOnNullReferenceException", value => value == "1");

        /// <summary>
        /// Indicates whether the application should fail fast on critical exceptions occurred in the cache codebase.
        /// </summary>
        public static readonly Setting<bool> FailFastOnCacheCriticalError = CreateSetting("FailFastOnCacheCriticalError", value => value == "1");

        /// <summary>
        /// I/O concurrency for the parser.
        /// </summary>
        /// <remarks>
        /// It has been found that depending on the I/O characteristics of the machine, oversubscribing the I/O can lead
        /// to better performance. This allows it to be set when a better concurrency level is known, such as for a
        /// specific build lab machine sku.
        /// </remarks>
        public static readonly Setting<string> ParserIOConcurrency = CreateSetting("BuildXLParserIOConcurrency", value => value);

        /// <summary>
        /// The minimum amount of time the build must run before the optimization data structures are serialized. This avoids overhead
        /// of serializing these data structures for extremely short builds.
        /// </summary>
        public static readonly Setting<TimeSpan> PostExecOptimizeThreshold = CreateSetting("PostExecOptimizeThresholdSec", value => ParseTimeSpan(value, ts => TimeSpan.FromSeconds(ts)) ??
            TimeSpan.FromMinutes(3));

        /// <summary>
        /// Indicates whether if historic metadata cache should be purged proactively (i.e. entries are immediately purged when TTL reaches 0)
        /// </summary>
        public static readonly Setting<bool> ProactivePurgeHistoricMetadataEntries = CreateSetting("ProactivePurgeHistoricMetadataEntries", value => value == "1");

        /// <summary>
        /// Specifies the default time to live of the historic metadata cache
        /// </summary>
        public static readonly Setting<int?> HistoricMetadataCacheDefaultTimeToLive = CreateSetting("HistoricMetadataCacheDefaultTimeToLive", value => ParseInt32(value));

        /// <summary>
        /// Specifies whether extraneous pins should be skipped such as pins before OpenStream/PutStream calls. Current pin is required before OpenStream/PlaceFile
        /// for certain cache implementations (i.e. BasicFileSystemCache). This setting is used to disable this behavior in most cases where not applicable
        /// until all cache implementations remove the need to pin before content retrieval operations.
        /// </summary>
        public static readonly Setting<bool> SkipExtraneousPins = CreateSetting("BuildXLSkipExtraneousPins", value => value == "1");

        /// <summary>
        /// Specifies whether remote workers should inline and block waiting for execution log notification messages sent to orchestrator to be processed.
        /// </summary>
        public static readonly Setting<bool> InlineWorkerXLGHandling = CreateSetting("BuildXLInlineWorkerXLGHandling", value => value == "1");

        /// <summary>
        /// Allows to overwrite the current system username with a custom value. If present, Aria telemetry and BuildXL.Native.UserUtilities
        /// will return this value. Often lab build machines are setup / provisioned with the same system username (e.g. in Apex builds) so we allow
        /// for this to be settable from the outside, thus partners can provide more fine grained telemetry data.
        /// </summary>
        // $Rename: Due to telemetry backend scripts this cannot be renamed to BuildXL
        public static readonly Setting<string> BuildXLUserName = CreateSetting("BUILDXL_USERNAME", value => value);

        /// <summary>
        /// Specifies whether a new pip should not be inlined if it runs on the same queue with the pip that schedules it.
        /// </summary>
        public static readonly Setting<bool> DoNotInlineWhenNewPipRunInSameQueue = CreateSetting("BuildXLDoNotInlineWhenNewPipRunInSameQueue", value => value == "1");

        /// <summary>
        /// Specifies the maximum load that the worker can acquire before BuildXL visits the next preferred work when <see cref="ScheduleConfigurationExtensions.ModuleAffinityEnabled"/> returns true.
        /// </summary>
        public static readonly Setting<double?> BuildXLModuleAffinityMultiplier = CreateSetting("BuildXLModuleAffinityMultiplier", value => ParseDouble(value));

        /// <summary>
        /// Disable pausing chooseworkerthreads
        /// </summary>
        public static readonly Setting<bool> DoNotPauseChooseWorkerThreads = CreateSetting("BuildXLDoNotPauseChooseWorkerThreads", value => value == "1");

        /// <summary>
        /// Disable delayed cache lookup.
        /// </summary>
        public static readonly Setting<bool> DisableDelayedCacheLookup = CreateSetting("BuildXLDisableDelayedCacheLookup", value => value == "1");

        #region Distribution-related timeouts

        /// <nodoc/>
        public static readonly TimeSpan DefaultWorkerAttachTimeout = TimeSpan.FromMinutes(75);
        /// <summary>
        /// Allows optionally specifying an alternative timeout for workers to wait for attach from orchestrator
        /// </summary>
        public static readonly Setting<TimeSpan> WorkerAttachTimeout = CreateSetting("BuildXLWorkerAttachTimeoutMin", value => ParseTimeSpan(value, ts => TimeSpan.FromMinutes(ts)) ?? DefaultWorkerAttachTimeout);

        /// <nodoc/>
        public static readonly TimeSpan DefaultDistributionConnectTimeout = TimeSpan.FromMinutes(10);

        /// <summary>
        /// Maximum time to wait while establishing a connection to the remote machine (both orchestrator->worker and worker->orchestrator)
        /// </summary>
        public static readonly Setting<TimeSpan> DistributionConnectTimeout = CreateSetting("BuildXLDistribConnectTimeoutSec", value => ParseTimeSpan(value, ts => TimeSpan.FromSeconds(ts)) ?? DefaultDistributionConnectTimeout);

        /// <summary>
        /// Whether KeepAlive is enabled for grpc. It allows http2 pings between client and server over transport.
        /// </summary>
        /// <remarks>
        /// Default enabled
        /// </remarks>
        public static readonly Setting<bool> GrpcKeepAliveEnabled = CreateSetting("BuildXLGrpcKeepAliveEnabled", value => string.IsNullOrWhiteSpace(value) ? true : value == "1");

        /// <summary>
        /// How many retry attempts when a grpc message is failed to send in the given deadline period.
        /// </summary>
        /// <remarks>
        /// Default 2
        /// </remarks>
        public static readonly Setting<int> GrpcMaxAttempts = CreateSetting("BuildXLGrpcMaxAttempts", value => ParseInt32(value) ?? 2);

        /// <summary>
        /// Maximum time waiting for a pip to be executed remotely. If this timeout is hit the worker is disconnected, so this number should be conservative
        /// </summary>
        /// <remarks>
        /// Defaults to 5.25 hours
        /// </remarks>
        public static readonly Setting<TimeSpan> RemotePipTimeout = CreateSetting("BuildXLRemotePipTimeoutMin", value => ParseTimeSpan(value, t => TimeSpan.FromMinutes(t)) ??
        TimeSpan.FromHours(5.25));

        /// <summary>
        /// Whether new .Net client is enabled for grpc. 
        /// </summary>
        /// <remarks>
        /// Default disabled
        /// </remarks>
        public static readonly Setting<bool> GrpcDotNetClientEnabled = CreateSetting("BuildXLGrpcDotNetClientEnabled", value => value == "1");

        /// <summary>
        /// Whether grpc encryption is enabled.
        /// </summary>
        /// <remarks>
        /// Default disabled
        /// </remarks>
        public static readonly Setting<bool> GrpcEncryptionEnabled = CreateSetting("BuildXLGrpcEncryptionEnabled", value => value == "1");

        /// <summary>
        /// Whether new kestrel server is enabled for grpc.
        /// </summary>
        /// <remarks>
        /// Default disabled
        /// </remarks>
        public static readonly Setting<bool> GrpcKestrelServerEnabled = CreateSetting("BuildXLGrpcKestrelServerEnabled", value => value == "1");

        /// <summary>
        /// Whether logging verbosity is enabled for grpc service.
        /// </summary>
        /// <remarks>
        /// Default disabled
        /// </remarks>
        public static readonly Setting<bool> GrpcVerbosityEnabled = CreateSetting("BuildXLGrpcVerbosityEnabled", value => value == "1");

        /// <summary>
        /// Grpc verbosity level
        /// </summary>
        /// <remarks>
        /// See GrpcEnvironmentOptions.GrpcVerbosity
        /// </remarks>
        public static readonly Setting<int?> GrpcVerbosityLevel = CreateSetting("BuildXLGrpcVerbosityLevel", value => ParseInt32(value, allowZero: true));

        /// <summary>
        /// Comma separated list of grpc tracers for grpc logging. Only relevant if GrpcVerbosityEnabled is true
        /// </summary>
        /// <remarks>
        /// See GRPC_TRACE in https://github.com/grpc/grpc/blob/master/doc/environment_variables.md
        /// </remarks>
        public static readonly Setting<string> GrpcTraceList = CreateSetting("BuildXLGrpcTraceList", value => value);

        /// <summary>
        /// Authorization token location in AutoPilot machines at CloudBuild 
        /// </summary>
        public static readonly Setting<string> CBBuildIdentityTokenPath = CreateSetting("CB_BUILDIDENTITYTOKEN_PATH", value => value);

        /// <summary>
        /// Authorization token location in AutoPilot machines at CloudBuild 
        /// </summary>
        public static readonly Setting<string> CBBuildUserCertificateName = CreateSetting("CB_BUILDUSERCERTIFICATE_NAME", value => value);

        /// <summary>
        /// The amount of concurrency to allow for input/output materialization
        /// </summary>
        public static readonly Setting<int> MaterializationConcurrency = CreateSetting(
            "BuildXL.MaterializationConcurrency",
            value => ParseInt32(value) ?? Environment.ProcessorCount);

        /// <summary>
        /// The amount of concurrency to allow for storing outputs
        /// </summary>
        public static readonly Setting<int> StoringOutputsToCacheConcurrency = CreateSetting(
            "BuildXL.StoringOutputsToCacheConcurrency",
            value => ParseInt32(value) ?? Environment.ProcessorCount);

        /// <summary>
        /// The amount of concurrency to allow for hashing files
        /// </summary>
        public static readonly Setting<int> HashingConcurrency = CreateSetting(
            "BuildXL.HashingConcurrency",
            value => ParseInt32(value) ?? Environment.ProcessorCount * 2);

        /// <summary>
        /// Whether we skip IPC pips when materializing outputs
        /// </summary>
        /// <remarks>
        /// Default disabled (we skip IPC pips when materializing outputs)
        /// </remarks>
        public static readonly Setting<bool> DoNotSkipIpcWhenMaterializingOutputs = CreateSetting("BuildXLDoNotSkipIpcWhenMaterializingOutputs", value => value == "1");

        #endregion

        #region Cache-related timeouts

        /// <summary>
        /// Timeout for pin and materialize operations.
        /// </summary>
        public static readonly Setting<int?> ArtifactContentCacheOperationTimeout = CreateSetting("BuildXLArtifactContentCacheOperationTimeout", value => ParseInt32(value));

        /// <summary>
        /// Timeout for fingerprintstore operations.
        /// </summary>
        public static readonly Setting<int?> FingerprintStoreOperationTimeout = CreateSetting("BuildXLFingerprintStoreOperationTimeout", value => ParseInt32(value));

        #endregion

        /// <summary>
        /// Enables runtime cache miss analyzer perform for all pips.
        /// </summary>
        public static readonly Setting<bool> RuntimeCacheMissAllPips = CreateSetting("BuildXLRuntimeCacheMissAllPips", value => value == "1");

        /// <summary>
        /// The minimum step duration for the tracer to log
        /// </summary>
        public static readonly Setting<int> MinStepDurationSecForTracer = CreateSetting("BuildXLMinStepDurationSecForTracer", value => ParseInt32(value, allowZero: true) ?? 30);

        /// <summary>
        /// Disables retries due to detours failures
        /// </summary>
        public static readonly Setting<bool> DisableDetoursRetries = CreateSetting("BuildXLDisableDetoursRetries", value => value == "1");

        /// <summary>
        /// Threshold in bytes for large string buffer in string table.
        /// </summary>
        public static readonly Setting<int?> LargeStringBufferThresholdBytes = CreateSetting("BuildXLLargeStringBufferThresholdBytes", value => ParseInt32(value));

        /// <summary>
        /// Specifies the UTC time when CB will terminate the build due to timeout
        /// </summary>
        public static readonly Setting<long?> CbUtcTimeoutTicks = CreateSetting("BuildXL_CbTimeoutUtcTicks", value => value == null ? (long?)null : long.Parse(value));

        /// <summary>
        /// Number of pipe read retries on cancellation; -1 of unlimited retries.
        /// </summary>
        public static readonly Setting<int?> SandboxNumRetriesPipeReadOnCancel = CreateSetting("BuildXLSandboxNumRetriesPipeReadOnCancel", value => ParseInt32(value));

        /// <summary>
        /// Sets the variable for consumption by settings
        /// </summary>
        public static void SetVariable(string name, string value)
        {
            SettingsEnvironment.SetVariable(name, value);
        }

        /// <summary>
        /// Resets settings environment to initial state based on passed in environment variables
        /// </summary>
        public static void Reset()
        {
            SettingsEnvironment.Reset();
        }

        private static int? ParseInt32(string valueString, bool allowZero = false)
        {
            int result;
            if (int.TryParse(valueString, out result) && (result != 0 || allowZero))
            {
                return result;
            }

            return null;
        }

        private static double? ParseDouble(string valueString)
        {
            double result;
            if (double.TryParse(valueString, out result))
            {
                return result;
            }

            return null;
        }

        private static string ProcessFingerprintSalt(string saltEnvironmentValue)
        {
            if (string.IsNullOrEmpty(saltEnvironmentValue))
            {
                return string.Empty;
            }

            if (saltEnvironmentValue == "*")
            {
                saltEnvironmentValue = Guid.NewGuid().ToString();
            }

            return "[BuildXLFingerprintSalt:" + saltEnvironmentValue + "]";
        }

        private static TimeSpan? ParseTimeSpan(string timespanString, Func<double, TimeSpan> timespanFactory)
        {
            if (string.IsNullOrEmpty(timespanString))
            {
                return null;
            }

            double timespanUnits;
            if (double.TryParse(timespanString, out timespanUnits) && timespanUnits != 0)
            {
                return timespanFactory(timespanUnits);
            }

            return null;
        }

        private static EnginePhases? ParsePhase(string value)
        {
            EnginePhases phase;
            if (Enum.TryParse(value, ignoreCase: true, result: out phase))
            {
                return phase;
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Launches the debugger at the end of the given phase if specified
        /// </summary>
        public static void TryLaunchDebuggerAfterPhase(EnginePhases phase)
        {
            if (LaunchDebuggerAfterPhase != null && LaunchDebuggerAfterPhase.Value == phase)
            {
                Debugger.Launch();
            }
        }

        private static Setting<T> CreateSetting<T>(string name, Func<string, T> valueFactory)
        {
            return new Setting<T>(name, valueFactory);
        }

        private static class SettingsEnvironment
        {
#pragma warning disable CA2211 // Non-constant fields should not be visible
            public static int Version = 1;
#pragma warning restore CA2211 // Non-constant fields should not be visible

            private static ConcurrentDictionary<string, string> s_variables = GetInitialVariables();

            private static ConcurrentDictionary<string, string> GetInitialVariables()
            {
                ConcurrentDictionary<string, string> variables = new ConcurrentDictionary<string, string>(OperatingSystemHelper.EnvVarComparer);
                foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
                {
                    variables[(string)entry.Key] = (string)entry.Value;
                }

                return variables;
            }

            public static void Reset()
            {
                s_variables = GetInitialVariables();
                Interlocked.Increment(ref Version);
            }

            public static string GetVariable(string name, out int version)
            {
                string value;
                version = Version;
                if (!s_variables.TryGetValue(name, out value))
                {
                    // For back compat to the old code name.
                    s_variables.TryGetValue(name.Replace("BuildXL", "Domino"), out value);
                }
                return value;
            }

            public static void SetVariable(string name, string value)
            {
                s_variables[name] = value;
                Interlocked.Increment(ref Version);
            }
        }

        /// <summary>
        /// Represents a computed environment setting
        /// </summary>
        /// <typeparam name="T">the value type</typeparam>
        public sealed class Setting<T>
        {
            private int m_version;
            private bool m_isExplicitlySet = false;
            private Optional<T> m_value;
            private Optional<string> m_stringValue;
            private readonly object m_syncLock = new object();
            private readonly Func<string, T> m_valueFactory;

            /// <summary>
            /// The name of the environment variable
            /// </summary>
            public string Name { get; }

            /// <summary>
            /// The string value of the environment variable
            /// </summary>
            public string StringValue
            {
                get
                {
                    Update();

                    var value = m_stringValue;
                    if (value.HasValue)
                    {
                        return value.Value;
                    }

                    lock (m_syncLock)
                    {
                        value = m_stringValue;
                        if (value.HasValue)
                        {
                            return value.Value;
                        }

                        value = SettingsEnvironment.GetVariable(Name, out m_version);
                        m_stringValue = value;
                        return m_stringValue.Value;
                    }
                }
            }

            /// <summary>
            /// The computed value from the environment variable
            /// </summary>
            public T Value
            {
                get
                {
                    Update();

                    var value = m_value;
                    if (value.HasValue)
                    {
                        return value.Value;
                    }

                    lock (m_syncLock)
                    {
                        value = m_value;
                        if (value.HasValue)
                        {
                            return value.Value;
                        }

                        value = m_valueFactory(StringValue);
                        m_value = value;
                        return m_value.Value;
                    }
                }

                set
                {
                    Reset();
                    lock (m_syncLock)
                    {
                        m_value = value;
                        m_isExplicitlySet = true;
                    }
                }
            }

            /// <summary>
            /// Class constructor
            /// </summary>
            public Setting(string name, Func<string, T> valueFactory)
            {
                Contract.Requires(!string.IsNullOrEmpty(name));
                Contract.Requires(valueFactory != null);

                Name = name;
                m_valueFactory = valueFactory;
            }

            // /// <inheritdoc />
            // public override string ToString()
            // {
            //     // Usually this type is used as Setting<string>
            //     // so this method should have the same semantic as the implicit conversion operator.
            //     return StringValue;
            // }

            private void Update()
            {
                if (!m_isExplicitlySet && m_version != SettingsEnvironment.Version)
                {
                    lock (m_syncLock)
                    {
                        m_value = Optional<T>.Empty;
                        m_stringValue = Optional<string>.Empty;
                    }
                }
            }

            /// <summary>
            /// Attempts to set the setting if not value is currently specified
            /// </summary>
            public bool TrySet(T value)
            {
                if (!string.IsNullOrEmpty(StringValue) || m_isExplicitlySet)
                {
                    // Can't set it already has an explicitly set value
                    return false;
                }
                else
                {
                    Value = value;
                    return true;
                }
            }

            /// <summary>
            /// Resets the setting causing the value to be recomputed on next access
            /// </summary>
            /// <returns>true if the environment variable changed since the last access. Otherwise, false.</returns>
            public bool Reset()
            {
                if (!m_stringValue.HasValue)
                {
                    return false;
                }

                var newStringValue = SettingsEnvironment.GetVariable(Name, out m_version);
                if (newStringValue != StringValue)
                {
                    lock (m_syncLock)
                    {
                        m_stringValue = newStringValue;
                        m_value = Optional<T>.Empty;
                    }

                    return true;
                }

                return false;
            }

            /// <summary>
            /// Implicit conversion of TokenData to LocationData.
            /// </summary>
            [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2225:OperatorOverloadsHaveNamedAlternates")]
            public static implicit operator T(Setting<T> setting)
            {
                return setting.Value;
            }
        }
    }
}
