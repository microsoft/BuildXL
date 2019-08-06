// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
        /// <summary>
        /// The maximum number pips to perform the on-the-fly cache miss analysis
        /// </summary>
        public static readonly Setting<int> MaxNumPipsForCacheMissAnalysis = CreateSetting("MaxNumPipsForCacheMissAnalysis", value => ParseInt32(value) ?? 1000);

        /// <summary>
        /// The maximum number of RPC 
        /// </summary>
        public static readonly Setting<int> MaxMessagesPerBatch = CreateSetting("MaxMessagesPerBatch", value => ParseInt32(value) ?? 100);

        /// <summary>
        /// Defines whether BuildXL should launch the debugger after a particular engine phase.
        /// </summary>
        public static readonly Setting<EnginePhases?> LaunchDebuggerAfterPhase = CreateSetting("BuildXLDebugAfterPhase", value => ParsePhase(Environment.GetEnvironmentVariable("BuildXLDebugAfterPhase")));

        /// <summary>
        /// Defines whether file sizes and hashes should be exported into the json graph
        /// </summary>
        public static readonly Setting<bool> ExportFileDetails = CreateSetting("BuildXLExportFileDetails", value => value == "1");

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
        /// Path pointing to VM command proxy needed for build in VM feature.
        /// </summary>
        public static readonly Setting<string> VmCommandProxyPath = CreateSetting("BUILDXL_VMCOMMANDPROXY_PATH", value => value);

        /// <summary>
        /// Bypass NuGet up to date checks
        /// </summary>
        public static readonly Setting<bool> BypassNugetDownload = CreateSetting("BuildXLBypassNugetDownload", value => value == "1");

        /// <summary>
        /// Emit file with all symlink definitions
        /// </summary>
        public static readonly Setting<bool> DebugSymlinkDefinitions = CreateSetting("DebugSymlinkDefinitions", value => value == "1");

        /// <summary>
        /// Allows optionally specifying an alternative timeout fo connect between IDE service and BuildXL task
        /// </summary>
        public static readonly Setting<TimeSpan> IdeConnectTimeout = CreateSetting("BuildXLIdeConnectTimeoutSec", value => ParseTimeSpan(value, ts => TimeSpan.FromSeconds(ts)) ??
            TimeSpan.FromSeconds(30));

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
        /// Allows to overwrite the current system username with a custom value. If present, Aria telemetry and BuildXL.Native.UserUtilities 
        /// will return this value. Often lab build machines are setup / provisioned with the same system username (e.g. in Apex builds) so we allow
        /// for this to be settable from the outside, thus partners can provide more fine grained telemetry data.
        /// </summary>
        // $Rename: Due to telemetry backend scripts this cannot be renamed to BuildXL
        public static readonly Setting<string> BuildXLUserName = CreateSetting("BUILDXL_USERNAME", value => value);


        #region Distribution-related timeouts

        /// <summary>
        /// Allows optionally specifying an alternative timeout for workers to wait for attach from master
        /// </summary>
        public static readonly Setting<TimeSpan> WorkerAttachTimeout = CreateSetting("BuildXLWorkerAttachTimeoutMin", value => ParseTimeSpan(value, ts => TimeSpan.FromMinutes(ts)) ??
            TimeSpan.FromMinutes(45));

        /// <summary>
        /// Maximum time to wait while establishing a connection to the remote machine (both master->worker and worker->master)
        /// </summary>
        public static readonly Setting<TimeSpan> DistributionConnectTimeout = CreateSetting("BuildXLDistribConnectTimeoutSec", value => ParseTimeSpan(value, ts => TimeSpan.FromSeconds(ts)) ??
            TimeSpan.FromMinutes(5));

        /// <summary>
        /// Inactivity timeout
        ///     - Master - if it cannot send a single heartbeat message to a worker within this interval, declares the worker as dead and stops using it
        ///     - Worker - if it doesn't receive any call from the master within this interval, decides that the master is dead and exits
        /// </summary>
        public static readonly Setting<TimeSpan> DistributionInactiveTimeout = CreateSetting("BuildXLDistribInactiveTimeoutMin", value => ParseTimeSpan(value, ts => TimeSpan.FromMinutes(ts)) ??
            TimeSpan.FromMinutes(30));

        /// <summary>
        /// The number of threads in the grpc thread pool.
        /// </summary>
        /// <remarks>
        /// Cache layer was using 70 by default, so we set the default limit based on their usage.
        /// </remarks>
        public static readonly Setting<int> GrpcThreadPoolSize = CreateSetting("BuildXLGrpcThreadPoolSize", value => ParseInt32(value) ?? 70);

        /// <summary>
        /// Whether HandlerInlining is enabled for grpc.
        /// </summary>
        /// <remarks>
        /// Default disabled
        /// </remarks>
        public static readonly Setting<bool> GrpcHandlerInliningEnabled = CreateSetting("BuildXLGrpcHandlerInliningEnabled", value => string.IsNullOrWhiteSpace(value) ? false : value == "1");

        /// <summary>
        /// An artificial delay in reporting notifications to force batching
        /// </summary>
        public static readonly Setting<TimeSpan> DistributionBatchArtificialDelay = CreateSetting("BuildXLDistributionBatchArtificialDelay", value => ParseTimeSpan(value, ts => TimeSpan.FromMilliseconds(ts)) ??
            TimeSpan.Zero);

        /// <summary>
        /// The amount of concurrency to allow for input/output materialization
        /// </summary>
        public static readonly Setting<int> MaterializationConcurrency = CreateSetting(
            "BuildXL.MaterializationConcurrency",
            value => ParseInt32(value) ?? Environment.ProcessorCount);

        /// <summary>
        /// The amount of concurrency to allow for hashing files
        /// </summary>
        public static readonly Setting<int> HashingConcurrency = CreateSetting(
            "BuildXL.HashingConcurrency",
            value => ParseInt32(value) ?? Environment.ProcessorCount * 2);

        #endregion

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

        private static int? ParseInt32(string valueString)
        {
            int result;
            if (int.TryParse(valueString, out result) && result != 0)
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

        private static TimeSpan? ParseTimeSpan(string timespanString, Func<int, TimeSpan> timespanFactory)
        {
            if (string.IsNullOrEmpty(timespanString))
            {
                return null;
            }

            int timespanUnits;
            if (int.TryParse(timespanString, out timespanUnits) && timespanUnits != 0)
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
                ConcurrentDictionary<string, string> variables = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
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
            private bool isExplicitlySet = false;
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
                    if (value.IsValid)
                    {
                        return value.Value;
                    }

                    lock (m_syncLock)
                    {
                        value = m_stringValue;
                        if (value.IsValid)
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
                    if (value.IsValid)
                    {
                        return value.Value;
                    }

                    lock (m_syncLock)
                    {
                        value = m_value;
                        if (value.IsValid)
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
                        isExplicitlySet = true;
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

            private void Update()
            {
                if (!isExplicitlySet && m_version != SettingsEnvironment.Version)
                {
                    lock (m_syncLock)
                    {
                        m_value = Optional<T>.Invalid;
                        m_stringValue = Optional<string>.Invalid;
                    }
                }
            }

            /// <summary>
            /// Resets the setting causing the value to be recomputed on next access
            /// </summary>
            /// <returns>true if the environment variable changed since the last access. Otherwise, false.</returns>
            public bool Reset()
            {
                if (!m_stringValue.IsValid)
                {
                    return false;
                }

                var newStringValue = SettingsEnvironment.GetVariable(Name, out m_version);
                if (newStringValue != StringValue)
                {
                    lock (m_syncLock)
                    {
                        m_stringValue = newStringValue;
                        m_value = Optional<T>.Invalid;
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
