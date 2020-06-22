// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Processes
{
    /// <summary>
    /// Facilities for classifying and reporting the <see cref="ReportedFileAccess"/>es for a <see cref="Process"/> pip.
    /// </summary>
    /// <remarks>
    /// Here 'reporting' means to log an event (perhaps error or warning level) with attached pip provenance;
    /// to do this correctly requires pip options (error or warning level), the pip (provenance), and the access.
    /// </remarks>
    public sealed class FileAccessReportingContext
    {
        private readonly LoggingContext m_loggingContext;
        private readonly PipExecutionContext m_context;
        private readonly ISandboxConfiguration m_config;
        private readonly Process m_pip;
        private readonly bool m_reportAllowlistedAccesses;
        private readonly FileAccessAllowlist m_fileAccessAllowlist;

        private List<ReportedFileAccess> m_violations;
        private List<ReportedFileAccess> m_allowlistedAccesses;
        private int m_numAllowlistedButNotCacheableFileAccessViolations;
        private int m_numAllowlistedAndCacheableFileAccessViolations;

        /// <summary>
        /// Creates a context. All <see cref="Counters"/> are initially zero and will increase as accesses are reported.
        /// </summary>
        public FileAccessReportingContext(LoggingContext loggingContext, PipExecutionContext context, ISandboxConfiguration config, Process pip, bool reportAllowlistedAccesses, FileAccessAllowlist allowlist = null)
        {
            Contract.Requires(loggingContext != null);
            Contract.Requires(context != null);
            Contract.Requires(config != null);
            Contract.Requires(pip != null);

            m_loggingContext = loggingContext;
            m_context = context;
            m_config = config;
            m_pip = pip;
            m_reportAllowlistedAccesses = reportAllowlistedAccesses;
            m_fileAccessAllowlist = allowlist;
        }

        /// <summary>
        /// Count of unexpected file accesses reported, by primary classification (e.g. allowlisted, cacheable, or a violation).
        /// </summary>
        public UnexpectedFileAccessCounters Counters => new UnexpectedFileAccessCounters(
            numFileAccessesAllowlistedButNotCacheable: m_numAllowlistedButNotCacheableFileAccessViolations,
            numFileAccessViolationsNotAllowlisted: m_violations == null ? 0 : m_violations.Count,
            numFileAccessesAllowlistedAndCacheable: m_numAllowlistedAndCacheableFileAccessViolations);

        /// <summary>
        /// Gets the collection of unexpected file accesses reported so far that were not allowlisted. These are 'violations'.
        /// </summary>
        public IReadOnlyList<ReportedFileAccess> FileAccessViolationsNotAllowlisted => m_violations;

        /// <summary>
        /// Gets the collection of unexpected file accesses reported so far that were allowlisted. These may be violations
        /// in distributed builds.
        /// </summary>
        public IReadOnlyList<ReportedFileAccess> AllowlistedFileAccessViolations => m_allowlistedAccesses;

        /// <summary>
        /// Reports an access with <see cref="FileAccessStatus.Denied"/>.
        /// </summary>
        public void ReportFileAccessDeniedByManifest(ReportedFileAccess unexpectedFileAccess)
        {
            Contract.Requires(unexpectedFileAccess.Status == FileAccessStatus.Denied || unexpectedFileAccess.Status == FileAccessStatus.CannotDeterminePolicy);
            MatchAndReportUnexpectedFileAccess(unexpectedFileAccess);
        }

        /// <summary>
        /// For an unexpected <see cref="ObservedFileAccess"/> (which is actually an aggregation of <see cref="ReportedFileAccess"/>es to
        /// a single path), reports each constituent access and computes an aggregate allowlist match type (the least permissive of any
        /// individual access).
        /// </summary>
        public FileAccessAllowlist.MatchType MatchAndReportUnexpectedObservedFileAccess(ObservedFileAccess unexpectedObservedFileAccess)
        {
            var aggregateMatch = FileAccessAllowlist.MatchType.MatchesAndCacheable;
            foreach (ReportedFileAccess reportedAccess in unexpectedObservedFileAccess.Accesses)
            {
                FileAccessAllowlist.MatchType thisMatch = MatchAndReportUnexpectedFileAccess(reportedAccess);

                switch (thisMatch)
                {
                    case FileAccessAllowlist.MatchType.NoMatch:
                        aggregateMatch = FileAccessAllowlist.MatchType.NoMatch;
                        break;
                    case FileAccessAllowlist.MatchType.MatchesButNotCacheable:
                        if (aggregateMatch == FileAccessAllowlist.MatchType.MatchesAndCacheable)
                        {
                            aggregateMatch = FileAccessAllowlist.MatchType.MatchesButNotCacheable;
                        }

                        break;
                    default:
                        Contract.Assert(thisMatch == FileAccessAllowlist.MatchType.MatchesAndCacheable);
                        break;
                }
            }

            return aggregateMatch;
        }

        /// <summary>
        /// Reports an access that - ignoring allowlisting - was unexpected. This can be due to a manifest-side or BuildXL-side denial decision.
        /// </summary>
        private FileAccessAllowlist.MatchType MatchAndReportUnexpectedFileAccess(ReportedFileAccess unexpectedFileAccess)
        {
            if (m_fileAccessAllowlist != null && m_fileAccessAllowlist.HasEntries)
            {
                Contract.Assert(
                    m_config.FailUnexpectedFileAccesses == false,
                    "Having a file-access allowlist requires that Detours failure injection is off.");

                FileAccessAllowlist.MatchType matchType = m_fileAccessAllowlist.Matches(m_loggingContext, unexpectedFileAccess, m_pip);
                switch (matchType)
                {
                    case FileAccessAllowlist.MatchType.NoMatch:
                        AddUnexpectedFileAccessNotAllowlisted(unexpectedFileAccess);
                        ReportUnexpectedFileAccessNotAllowlisted(unexpectedFileAccess);
                        break;
                    case FileAccessAllowlist.MatchType.MatchesButNotCacheable:
                        AddUnexpectedFileAccessAllowlisted(unexpectedFileAccess);
                        m_numAllowlistedButNotCacheableFileAccessViolations++;
                        ReportAllowlistedFileAccessNonCacheable(unexpectedFileAccess);
                        break;
                    case FileAccessAllowlist.MatchType.MatchesAndCacheable:
                        AddUnexpectedFileAccessAllowlisted(unexpectedFileAccess);
                        m_numAllowlistedAndCacheableFileAccessViolations++;
                        ReportAllowlistedFileAccessCacheable(unexpectedFileAccess);
                        break;
                    default:
                        throw Contract.AssertFailure("Unknown allowlist-match type.");
                }

                return matchType;
            }
            else
            {
                AddUnexpectedFileAccessNotAllowlisted(unexpectedFileAccess);
                ReportUnexpectedFileAccessNotAllowlisted(unexpectedFileAccess);
                return FileAccessAllowlist.MatchType.NoMatch;
            }
        }

        private void AddUnexpectedFileAccessNotAllowlisted(ReportedFileAccess reportedFileAccess)
        {
            if (m_violations == null)
            {
                m_violations = new List<ReportedFileAccess>();
            }

            if (reportedFileAccess.Operation != ReportedFileOperation.NtCreateFile || m_config.UnsafeSandboxConfiguration.MonitorNtCreateFile)
            {
                m_violations.Add(reportedFileAccess);
            }
        }

        private void AddUnexpectedFileAccessAllowlisted(ReportedFileAccess reportedFileAccess)
        {
            if (m_allowlistedAccesses == null)
            {
                m_allowlistedAccesses = new List<ReportedFileAccess>();
            }

            if (reportedFileAccess.Operation != ReportedFileOperation.NtCreateFile || m_config.UnsafeSandboxConfiguration.MonitorNtCreateFile)
            {
                m_allowlistedAccesses.Add(reportedFileAccess);
            }
        }

        private void ReportUnexpectedFileAccessNotAllowlisted(ReportedFileAccess reportedFileAccess)
        {
            string path = reportedFileAccess.GetPath(m_context.PathTable);
            string description = reportedFileAccess.Describe();

            if (path.StartsWith(PipEnvironment.RestrictedTemp, StringComparison.OrdinalIgnoreCase))
            {
                BuildXL.Processes.Tracing.Logger.Log.PipProcessDisallowedTempFileAccess(
                    m_loggingContext,
                    m_pip.SemiStableHash,
                    m_pip.GetDescription(m_context),
                    description,
                    path);
            }
            else
            {
                BuildXL.Processes.Tracing.Logger.Log.PipProcessDisallowedFileAccess(
                    m_loggingContext,
                    m_pip.SemiStableHash,
                    m_pip.GetDescription(m_context),
                    m_pip.Provenance.Token.Path.ToString(m_context.PathTable),
                    m_pip.WorkingDirectory.ToString(m_context.PathTable),
                    description,
                    path);

                if (reportedFileAccess.Operation == ReportedFileOperation.NtCreateFile &&
                     !m_config.UnsafeSandboxConfiguration.MonitorNtCreateFile)
                {
                    // If the unsafe_IgnoreNtCreate is set, disallowed ntCreateFile accesses are not marked as violations.
                    // Since there will be no error or warning for the ignored NtCreateFile violations in the FileMonitoringViolationAnalyzer, 
                    // this is the only place for us to log a warning for those.
                    // We also need to emit a dx09 verbose above for those violations due to WrapItUp. 
                    BuildXL.Processes.Tracing.Logger.Log.PipProcessDisallowedNtCreateFileAccessWarning(
                        m_loggingContext,
                        m_pip.SemiStableHash,
                        m_pip.GetDescription(m_context),
                        m_pip.Provenance.Token.Path.ToString(m_context.PathTable),
                        m_pip.WorkingDirectory.ToString(m_context.PathTable),
                        description,
                        path);
                }
            }
        }

        private void ReportAllowlistedFileAccessNonCacheable(ReportedFileAccess reportedFileAccess)
        {
            string path = reportedFileAccess.GetPath(m_context.PathTable);
            string description = reportedFileAccess.Describe();

            if (m_reportAllowlistedAccesses)
            {
                BuildXL.Processes.Tracing.Logger.Log.PipProcessUncacheableAllowlistNotAllowedInDistributedBuilds(
                    m_loggingContext,
                    m_pip.SemiStableHash,
                    m_pip.GetDescription(m_context),
                    description,
                    path);

                AddUnexpectedFileAccessNotAllowlisted(reportedFileAccess);
            }
            else
            {
                BuildXL.Processes.Tracing.Logger.Log.PipProcessDisallowedFileAccessAllowlistedNonCacheable(
                    m_loggingContext,
                    m_pip.SemiStableHash,
                    m_pip.GetDescription(m_context),
                    description,
                    path);
            }
        }

        private void ReportAllowlistedFileAccessCacheable(ReportedFileAccess reportedFileAccess)
        {
            string path = reportedFileAccess.GetPath(m_context.PathTable);
            string description = reportedFileAccess.Describe();

            BuildXL.Processes.Tracing.Logger.Log.PipProcessDisallowedFileAccessAllowlistedCacheable(
                m_loggingContext,
                m_pip.SemiStableHash,
                m_pip.GetDescription(m_context),
                description,
                path);
        }
    }

    /// <summary>
    /// Counters accumulated in a <see cref="FileAccessReportingContext"/> per classification of 'unexpected' file access.
    /// An unexpected access is either a violation or was allowlisted.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1815:ShouldOverrideEquals")]
    public readonly struct UnexpectedFileAccessCounters
    {
        /// <summary>
        /// Count of accesses such that the access was allowlisted, but was not in the cache-friendly part of the allowlist. The pip should not be cached.
        /// </summary>
        public readonly int NumFileAccessesAllowlistedButNotCacheable;

        /// <summary>
        /// Count of accesses such that the access was allowlisted, via the cache-friendly part of the allowlist. The pip may be cached.
        /// </summary>
        public readonly int NumFileAccessesAllowlistedAndCacheable;

        /// <summary>
        /// Count of accesses such that the access was not allowlisted at all, and should be reported as a violation.
        /// </summary>
        public readonly int NumFileAccessViolationsNotAllowlisted;

        /// <nodoc />
        public UnexpectedFileAccessCounters(
            int numFileAccessesAllowlistedButNotCacheable,
            int numFileAccessesAllowlistedAndCacheable,
            int numFileAccessViolationsNotAllowlisted)
        {
            NumFileAccessViolationsNotAllowlisted = numFileAccessViolationsNotAllowlisted;
            NumFileAccessesAllowlistedAndCacheable = numFileAccessesAllowlistedAndCacheable;
            NumFileAccessesAllowlistedButNotCacheable = numFileAccessesAllowlistedButNotCacheable;
        }

        /// <summary>
        /// Returns the sum of counters for those accesses that were violations or were allowlisted. This total excludes only those accesses
        /// that were allowed on their own merit, i.e., not 'unexpected' and handled.
        /// </summary>
        public int TotalUnexpectedFileAccesses => NumFileAccessesAllowlistedButNotCacheable + NumFileAccessesAllowlistedAndCacheable + NumFileAccessViolationsNotAllowlisted;

        /// <summary>
        /// Indicates if this context has reported accesses which should mark the owning process as cache-ineligible.
        /// </summary>
        public bool HasUncacheableFileAccesses => (NumFileAccessViolationsNotAllowlisted + NumFileAccessesAllowlistedButNotCacheable) > 0;
    }
}
