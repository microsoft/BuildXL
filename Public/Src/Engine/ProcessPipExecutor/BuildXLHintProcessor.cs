// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BuildXL.Processes;
using BuildXL.Processes.Tracing;
using BuildXL.Utilities.Instrumentation.Common;
using HintType = BuildXL.Pips.Operations.Process.HintType;

namespace BuildXL.ProcessPipExecutor
{
    /// <summary>
    /// Scans a process' standard output and standard error for BuildXL hint lines of the form <c>##bxl[&lt;key&gt;]=value</c>
    /// and turns recognized hints into scheduling information for the pip. Also forwards Azure DevOps test result
    /// publishing commands to the console on the executing machine.
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

        private const string AzureDevOpsCommandPrefix = "##vso[";
        private const string AzureDevOpsResultsPublishPrefix = AzureDevOpsCommandPrefix + "results.publish";

        private static readonly char[] s_invalidCommandCharacters = { '\r', '\n', '\0' };

        /// <summary>
        /// Generic pattern for a BuildXL hint line: <c>##bxl[&lt;key&gt;]=&lt;value&gt;</c>. The key and value are captured so
        /// the observer can dispatch on the key. Kept intentionally broad so new hint kinds can be added without changing
        /// the scanning machinery.
        /// </summary>
        private static readonly ExpandedRegexDescriptor s_buildXLHintRegexDescriptor = new ExpandedRegexDescriptor(@"^" + BuildXLHintPrefix + @"\[(?<key>[^\]]+)\]=(?<value>.*)$", RegexOptions.None);

        private readonly LoggingContext m_loggingContext;
        private readonly long m_pipSemiStableHash;
        private readonly string m_pipDescription;
        private readonly HintType m_hintTypes;

        private readonly Task<Regex> m_regexTask;
        private Regex m_regex;

        // Per-stream running times (in milliseconds) gathered from '##bxl[runtimeSecs]=<value>' hint lines (the hint value is
        // expressed in whole seconds and converted to milliseconds here). Each output stream is fed by a single thread, so each
        // field is mutated by only one thread and needs no synchronization.
        private long? m_standardOutputInjectedRuntimeMs;
        private long? m_standardErrorInjectedRuntimeMs;

        /// <summary>
        /// Creates a processor for the selected <paramref name="hintTypes"/>. When none are selected, the processor
        /// installs no observers.
        /// </summary>
        public BuildXLHintProcessor(HintType hintTypes, LoggingContext loggingContext, long pipSemiStableHash, string pipDescription)
        {
            m_loggingContext = loggingContext;
            m_pipSemiStableHash = pipSemiStableHash;
            m_pipDescription = pipDescription;
            m_hintTypes = hintTypes;

            if ((hintTypes & HintType.BuildXL) != 0)
            {
                // Obtained through the same cached factory used for warning/error regexes.
                m_regexTask = RegexFactory.GetRegexAsync(s_buildXLHintRegexDescriptor);
            }
        }

        /// <summary>
        /// When hint scanning is enabled, augments the standard output and standard error observers already configured on
        /// <paramref name="info"/> (e.g. the warning observer) so a single streaming pass over each stream also feeds the
        /// selected hint scanners. When scanning is disabled the observers are left untouched.
        /// </summary>
        public async Task UpdateObserversWithHintScanning(SandboxedProcessInfo info)
        {
            if (m_hintTypes == HintType.None)
            {
                return;
            }

            // The hint pattern is a fixed, internally-defined constant, so this is not expected to throw. It is awaited here
            // (before the process starts streaming) so the compiled regex is ready by the time the observers run.
            if (m_regexTask != null)
            {
                m_regex = await m_regexTask;
            }

            // Give each stream its own field so the observers never share mutable state and can run lock-free on their
            // respective reader threads. The two results are reconciled in GetInjectedProcessRuntime after the process completes.
            info.StandardOutputObserver = CombineObservers(info.StandardOutputObserver, line => ObserveHint(line, ref m_standardOutputInjectedRuntimeMs));
            info.StandardErrorObserver = CombineObservers(info.StandardErrorObserver, line => ObserveHint(line, ref m_standardErrorInjectedRuntimeMs));
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
        /// When publishing is enabled, prevents Azure DevOps commands from executing via logged or forwarded pip output.
        /// Rejected hints can also contain command markers, which the agent recognizes anywhere in a line.
        /// The captured stdout/stderr files are left unchanged.
        /// </summary>
        public string SanitizeOutputForLogging(string output) =>
            (m_hintTypes & HintType.AzureDevOpsTestResults) == 0 ? output : output.Replace(AzureDevOpsCommandPrefix, "##vsoSANITIZED[");

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
        /// Per-line observer that scans for enabled hints while the process streams its output and, for a
        /// <c>##bxl[runtimeSecs]=value</c> hint, records the running time to inject as the pip's execution time into the
        /// supplied per-stream <paramref name="injectedRuntimeMs"/> (the hint value is expressed in whole seconds and
        /// converted to milliseconds here).
        /// </summary>
        private void ObserveHint(string line, ref long? injectedRuntimeMs)
        {
            if (string.IsNullOrEmpty(line))
            {
                return;
            }

            // The marker is rare, so use a cheap prefix check to skip the regex on the vast majority of output lines.
            if (!line.StartsWith(BuildXLHintPrefix, StringComparison.Ordinal))
            {
                if ((m_hintTypes & HintType.AzureDevOpsTestResults) != 0)
                {
                    ObserveAzureDevOpsResultsPublish(line);
                }

                return;
            }

            if (m_regex == null)
            {
                return;
            }

            Match match = m_regex.Match(line);
            if (!match.Success)
            {
                // Starts with the marker but is not a well-formed hint line.
                Logger.Log.PipProcessBuildXLHintUnrecognized(m_loggingContext, m_pipSemiStableHash, m_pipDescription, SanitizeOutputForLogging(line));
                return;
            }

            string key = match.Groups["key"].Value;
            // This is the only hint we support today
            if (!string.Equals(key, BuildXLRuntimeHintKey, StringComparison.Ordinal))
            {
                // Well-formed hint but an unknown key. Other hint kinds may be supported in the future; for now, warn and ignore.
                Logger.Log.PipProcessBuildXLHintUnrecognized(m_loggingContext, m_pipSemiStableHash, m_pipDescription, SanitizeOutputForLogging(line));
                return;
            }

            if (!long.TryParse(match.Groups["value"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long runtimeSecs))
            {
                // Value does not parse as an integer number of seconds; treat as an unrecognized hint.
                Logger.Log.PipProcessBuildXLHintUnrecognized(m_loggingContext, m_pipSemiStableHash, m_pipDescription, SanitizeOutputForLogging(line));
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

        private void ObserveAzureDevOpsResultsPublish(string line)
        {
            int commandStart = line.IndexOf(AzureDevOpsCommandPrefix, StringComparison.Ordinal);
            if (commandStart < 0)
            {
                return;
            }

            string command = line.Substring(commandStart);
            if (!command.StartsWith(AzureDevOpsResultsPublishPrefix, StringComparison.Ordinal))
            {
                return;
            }

            int prefixLength = AzureDevOpsResultsPublishPrefix.Length;
            // Only indentation or MSBuild's numeric N> / N:M> prefix may precede a command.
            if ((commandStart > 0 && !Regex.IsMatch(line.Substring(0, commandStart), @"\A[ \t]*(?:[0-9]+(?::[0-9]+)?>[ \t]*)?\z")) ||
                command.Length <= prefixLength ||
                (command[prefixLength] != ' ' && command[prefixLength] != ']') ||
                command.IndexOf(']', prefixLength) < 0 ||
                line.IndexOfAny(s_invalidCommandCharacters) >= 0 ||
                command.IndexOf(AzureDevOpsCommandPrefix, prefixLength, StringComparison.Ordinal) >= 0)
            {
                Logger.Log.PipProcessBuildXLHintUnrecognized(m_loggingContext, m_pipSemiStableHash, m_pipDescription, SanitizeOutputForLogging(line));
                return;
            }

            Logger.Log.PipProcessAzureDevOpsResultsPublish(m_loggingContext, m_pipSemiStableHash, m_pipDescription, command);
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
