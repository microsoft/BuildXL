// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BuildXL.Processes;
using BuildXL.Processes.Tracing;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.ProcessPipExecutor
{
    /// <summary>
    /// Scans a process' standard output and standard error for BuildXL hint lines of the form <c>##bxl[&lt;key&gt;]=value</c>
    /// and turns recognized hints into scheduling information for the pip.
    /// </summary>
    /// <remarks>
    /// The hint grammar is intentionally generic so new hint kinds can be added without changing the scanning machinery; today
    /// the only understood hint is <c>##bxl[runtimeSecs]=value</c>, whose value (a whole number of seconds) is injected as the
    /// pip's execution time so the scheduler can honor the running time of work that actually happens outside the pip (e.g. a
    /// CloudTest job). Each output stream is fed by a single thread, so each stream gets its own accumulator field and needs no
    /// synchronization; the two results are merged in <see cref="GetInjectedProcessRuntime"/> after the process completes.
    /// </remarks>
    internal sealed class BuildXLHintProcessor
    {
        /// <summary>
        /// Marker prefix that identifies a BuildXL hint line emitted on a process' standard output/error.
        /// </summary>
        private const string BuildXLHintPrefix = "##bxl";

        /// <summary>
        /// Hint key carrying the running time (a whole number of seconds) to inject as the pip's execution time.
        /// </summary>
        private const string BuildXLRuntimeHintKey = "runtimeSecs";

        /// <summary>
        /// Generic pattern for a BuildXL hint line: <c>##bxl[&lt;key&gt;]=&lt;value&gt;</c>. The key and value are captured so
        /// the observer can dispatch on the key. Kept intentionally broad so new hint kinds can be added without changing
        /// the scanning machinery.
        /// </summary>
        private static readonly ExpandedRegexDescriptor s_buildXLHintRegexDescriptor = new ExpandedRegexDescriptor(@"^" + BuildXLHintPrefix + @"\[(?<key>[^\]]+)\]=(?<value>.*)$", RegexOptions.None);

        private readonly LoggingContext m_loggingContext;
        private readonly long m_pipSemiStableHash;
        private readonly string m_pipDescription;

        private readonly Task<Regex> m_regexTask;
        private Regex m_regex;

        // Per-stream running times (in milliseconds) gathered from '##bxl[runtimeSecs]=<value>' hint lines (the hint value is
        // expressed in whole seconds and converted to milliseconds here). Each output stream is fed by a single thread, so each
        // field is mutated by only one thread and needs no synchronization.
        private long? m_standardOutputInjectedRuntimeMs;
        private long? m_standardErrorInjectedRuntimeMs;

        /// <summary>
        /// Creates a hint processor. When <paramref name="enableBuildXLHintScanning"/> is false the processor is inert: it
        /// installs no observers and never produces an injected runtime.
        /// </summary>
        public BuildXLHintProcessor(bool enableBuildXLHintScanning, LoggingContext loggingContext, long pipSemiStableHash, string pipDescription)
        {
            m_loggingContext = loggingContext;
            m_pipSemiStableHash = pipSemiStableHash;
            m_pipDescription = pipDescription;

            if (enableBuildXLHintScanning)
            {
                // Obtained through the same cached factory used for warning/error regexes.
                m_regexTask = RegexFactory.GetRegexAsync(s_buildXLHintRegexDescriptor);
            }
        }

        /// <summary>
        /// When hint scanning is enabled, augments the standard output and standard error observers already configured on
        /// <paramref name="info"/> (e.g. the warning observer) so a single streaming pass over each stream also feeds the
        /// <c>##bxl</c> hint scanner. When scanning is disabled the observers are left untouched.
        /// </summary>
        public async Task UpdateObserversWithHintScanning(SandboxedProcessInfo info)
        {
            if (m_regexTask == null)
            {
                return;
            }

            // The hint pattern is a fixed, internally-defined constant, so this is not expected to throw. It is awaited here
            // (before the process starts streaming) so the compiled regex is ready by the time the observers run.
            m_regex = await m_regexTask;

            // Give each stream its own field so the observers never share mutable state and can run lock-free on their
            // respective reader threads. The two results are reconciled in GetInjectedProcessRuntime after the process completes.
            info.StandardOutputObserver = CombineObservers(info.StandardOutputObserver, line => ObserveBuildXLHint(line, ref m_standardOutputInjectedRuntimeMs));
            info.StandardErrorObserver = CombineObservers(info.StandardErrorObserver, line => ObserveBuildXLHint(line, ref m_standardErrorInjectedRuntimeMs));
        }

        /// <summary>
        /// Returns the running time (in milliseconds) to inject as the pip's execution time based on the <c>##bxl[runtimeSecs]</c>
        /// hints observed while the process was streaming output, or null when scanning is disabled, the process was
        /// <paramref name="canceled"/>, or no runtime hint was observed. This is independent of pip success: the hint carries
        /// scheduling information that we want to honor regardless of exit code.
        /// </summary>
        public long? GetInjectedProcessRuntime(bool canceled)
        {
            if (m_regexTask == null || canceled)
            {
                return null;
            }

            // Each stream accumulated independently; merge the two results here.
            return ReconcileBuildXLHints(m_standardOutputInjectedRuntimeMs, m_standardErrorInjectedRuntimeMs);
        }

        /// <summary>
        /// Combines two output observers into a single <see cref="Action{String}"/> that invokes each one in
        /// order for every line.
        /// </summary>
        private static Action<string> CombineObservers(Action<string> observer1, Action<string> observer2)
        {
            Action<string> combined = null;

            if (observer1 != null)
            {
                combined += observer1;
            }

            if (observer2 != null)
            {
                combined += observer2;
            }

            return combined;
        }

        /// <summary>
        /// Per-line observer that scans for <c>##bxl</c> hint lines while the process streams its output and, for a
        /// <c>##bxl[runtimeSecs]=value</c> hint, records the running time to inject as the pip's execution time into the
        /// supplied per-stream <paramref name="injectedRuntimeMs"/> (the hint value is expressed in whole seconds and
        /// converted to milliseconds here).
        /// </summary>
        private void ObserveBuildXLHint(string line, ref long? injectedRuntimeMs)
        {
            if (m_regex == null || string.IsNullOrEmpty(line))
            {
                return;
            }

            // The marker is rare, so use a cheap prefix check to skip the regex on the vast majority of output lines.
            if (!line.StartsWith(BuildXLHintPrefix, StringComparison.Ordinal))
            {
                return;
            }

            Match match = m_regex.Match(line);
            if (!match.Success)
            {
                // Starts with the marker but is not a well-formed hint line.
                Logger.Log.PipProcessBuildXLHintUnrecognized(m_loggingContext, m_pipSemiStableHash, m_pipDescription, line);
                return;
            }

            string key = match.Groups["key"].Value;
            // This is the only hint we support today
            if (!string.Equals(key, BuildXLRuntimeHintKey, StringComparison.Ordinal))
            {
                // Well-formed hint but an unknown key. Other hint kinds may be supported in the future; for now, warn and ignore.
                Logger.Log.PipProcessBuildXLHintUnrecognized(m_loggingContext, m_pipSemiStableHash, m_pipDescription, line);
                return;
            }

            if (!long.TryParse(match.Groups["value"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long runtimeSecs))
            {
                // Value does not parse as an integer number of seconds; treat as an unrecognized hint.
                Logger.Log.PipProcessBuildXLHintUnrecognized(m_loggingContext, m_pipSemiStableHash, m_pipDescription, line);
                return;
            }

            if (runtimeSecs < 0)
            {
                // Negative value is the emitter's "no data" sentinel; ignore it.
                return;
            }

            // The hint is expressed in whole seconds; the rest of the pipeline works in milliseconds.
            long runtimeMs = runtimeSecs * 1000;

            // Single-threaded for this stream: no synchronization required.
            if (injectedRuntimeMs.HasValue)
            {
                // Duplicate runtime hint on this stream: keep the first value and warn about the extra one.
                Logger.Log.PipProcessBuildXLHintDuplicateRuntime(m_loggingContext, m_pipSemiStableHash, m_pipDescription, injectedRuntimeMs.Value, runtimeMs);
                return;
            }

            injectedRuntimeMs = runtimeMs;
        }

        /// <summary>
        /// Merges the per-stream <c>##bxl</c> hint scanning results into the single running time to inject as the pip's
        /// execution time. When a runtime hint was observed on both the standard output and standard error streams, the
        /// standard output value is honored and the standard error value is reported as a duplicate.
        /// </summary>
        private long? ReconcileBuildXLHints(long? standardOutputRuntimeMs, long? standardErrorRuntimeMs)
        {
            if (standardOutputRuntimeMs.HasValue && standardErrorRuntimeMs.HasValue)
            {
                Logger.Log.PipProcessBuildXLHintDuplicateRuntime(
                    m_loggingContext,
                    m_pipSemiStableHash,
                    m_pipDescription,
                    standardOutputRuntimeMs.Value,
                    standardErrorRuntimeMs.Value);

                return standardOutputRuntimeMs;
            }

            return standardOutputRuntimeMs ?? standardErrorRuntimeMs;
        }
    }
}
