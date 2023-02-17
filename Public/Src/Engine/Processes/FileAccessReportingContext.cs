// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Pips.Operations;
using BuildXL.Utilities.Core;
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
        private int m_numFileExistenceAccessViolationsNotAllowlisted;

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
            numFileAccessesAllowlistedAndCacheable: m_numAllowlistedAndCacheableFileAccessViolations,
            numFileExistenceAccessViolationsNotAllowlisted: m_numFileExistenceAccessViolationsNotAllowlisted);

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
        /// Matches an instance of <see cref="ReportedFileAccess"/> with allow list entries.
        /// </summary>
        public FileAccessAllowlist.MatchType MatchReportedFileAccess(ReportedFileAccess fileAccess) =>
            m_fileAccessAllowlist?.HasEntries == true
            ? m_fileAccessAllowlist.Matches(m_loggingContext, fileAccess, m_pip)
            : FileAccessAllowlist.MatchType.NoMatch;

        /// <summary>
        /// Matches an instance of <see cref="ObservedFileAccess"/> with allow list entries.
        /// </summary>
        public (FileAccessAllowlist.MatchType aggregateMatchType, (ReportedFileAccess, FileAccessAllowlist.MatchType)[] reportedMatchTypes) MatchObservedFileAccess(ObservedFileAccess observedFileAccess)
        {
            var aggregateMatch = FileAccessAllowlist.MatchType.MatchesAndCacheable;
            var rfas = new (ReportedFileAccess, FileAccessAllowlist.MatchType)[observedFileAccess.Accesses.Count];
            int index = 0;
            foreach (ReportedFileAccess reportedAccess in observedFileAccess.Accesses)
            {
                FileAccessAllowlist.MatchType thisMatch = MatchReportedFileAccess(reportedAccess);
                rfas[index++] = (reportedAccess, thisMatch);
                aggregateMatch = AggregateMatchType(aggregateMatch, thisMatch);
            }

            return (aggregateMatch, rfas);
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
                aggregateMatch = AggregateMatchType(aggregateMatch, thisMatch);
            }

            return aggregateMatch;
        }

        private static FileAccessAllowlist.MatchType AggregateMatchType(FileAccessAllowlist.MatchType aggregateType, FileAccessAllowlist.MatchType currentType) 
        {
            switch (currentType)
            {
                case FileAccessAllowlist.MatchType.NoMatch:
                    aggregateType = FileAccessAllowlist.MatchType.NoMatch;
                    break;
                case FileAccessAllowlist.MatchType.MatchesButNotCacheable:
                    if (aggregateType == FileAccessAllowlist.MatchType.MatchesAndCacheable)
                    {
                        aggregateType = FileAccessAllowlist.MatchType.MatchesButNotCacheable;
                    }

                    break;
                default:
                    Contract.Assert(currentType == FileAccessAllowlist.MatchType.MatchesAndCacheable);
                    break;
            }

            return aggregateType;
        }

        /// <summary>
        /// Reports an access that - ignoring allowlisting - was unexpected. This can be due to a manifest-side or BuildXL-side denial decision.
        /// </summary>
        private FileAccessAllowlist.MatchType MatchAndReportUnexpectedFileAccess(ReportedFileAccess unexpectedFileAccess)
        {
            FileAccessAllowlist.MatchType matchType = FileAccessAllowlist.MatchType.NoMatch;

            if (m_fileAccessAllowlist != null && m_fileAccessAllowlist.HasEntries)
            {
                Contract.Assert(
                    m_config.FailUnexpectedFileAccesses == false,
                    "Having a file-access allowlist requires that Detours failure injection is off.");

                matchType = m_fileAccessAllowlist.Matches(m_loggingContext, unexpectedFileAccess, m_pip);
            }

            ReportFileAccess(unexpectedFileAccess, matchType);
            return matchType;
        }

        /// <summary>
        /// Reports file access to this reporting context.
        /// </summary>
        public void ReportFileAccess(ReportedFileAccess fileAccess, FileAccessAllowlist.MatchType matchType)
        {
            switch (matchType)
            {
                case FileAccessAllowlist.MatchType.NoMatch:
                    AddUnexpectedFileAccessNotAllowlisted(fileAccess);
                    ReportUnexpectedFileAccessNotAllowlisted(fileAccess);
                    break;
                case FileAccessAllowlist.MatchType.MatchesButNotCacheable:
                    AddUnexpectedFileAccessAllowlisted(fileAccess);
                    m_numAllowlistedButNotCacheableFileAccessViolations++;
                    ReportAllowlistedFileAccessNonCacheable(fileAccess);
                    break;
                case FileAccessAllowlist.MatchType.MatchesAndCacheable:
                    AddUnexpectedFileAccessAllowlisted(fileAccess);
                    m_numAllowlistedAndCacheableFileAccessViolations++;
                    ReportAllowlistedFileAccessCacheable(fileAccess);
                    break;
                default:
                    throw Contract.AssertFailure("Unknown allowlist-match type.");
            }
        }

        private void AddUnexpectedFileAccessNotAllowlisted(ReportedFileAccess reportedFileAccess)
        {
            m_violations ??= new List<ReportedFileAccess>();

            if (reportedFileAccess.Operation != ReportedFileOperation.NtCreateFile || m_config.UnsafeSandboxConfiguration.MonitorNtCreateFile)
            {
                m_violations.Add(reportedFileAccess);

                if (reportedFileAccess.Method == FileAccessStatusMethod.FileExistenceBased)
                {
                    m_numFileExistenceAccessViolationsNotAllowlisted++;
                }
            }
        }

        private void AddUnexpectedFileAccessAllowlisted(ReportedFileAccess reportedFileAccess)
        {
            m_allowlistedAccesses ??= new List<ReportedFileAccess>();

            if (reportedFileAccess.Operation != ReportedFileOperation.NtCreateFile || m_config.UnsafeSandboxConfiguration.MonitorNtCreateFile)
            {
                m_allowlistedAccesses.Add(reportedFileAccess);
            }
        }

        private void ReportUnexpectedFileAccessNotAllowlisted(ReportedFileAccess reportedFileAccess)
        {
            string path = reportedFileAccess.GetPath(m_context.PathTable);
            string description = reportedFileAccess.Describe();

            if (path.StartsWith(PipEnvironment.RestrictedTemp, OperatingSystemHelper.PathComparison))
            {
                Tracing.Logger.Log.PipProcessDisallowedTempFileAccess(
                    m_loggingContext,
                    m_pip.SemiStableHash,
                    m_pip.GetDescription(m_context),
                    description,
                    path);
            }
            else
            {
                Tracing.Logger.Log.PipProcessDisallowedFileAccess(
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
                    Tracing.Logger.Log.PipProcessDisallowedNtCreateFileAccessWarning(
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
                Tracing.Logger.Log.PipProcessUncacheableAllowlistNotAllowedInDistributedBuilds(
                    m_loggingContext,
                    m_pip.SemiStableHash,
                    m_pip.GetDescription(m_context),
                    description,
                    path);

                AddUnexpectedFileAccessNotAllowlisted(reportedFileAccess);
            }
            else
            {
                Tracing.Logger.Log.PipProcessDisallowedFileAccessAllowlistedNonCacheable(
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

            Tracing.Logger.Log.PipProcessDisallowedFileAccessAllowlistedCacheable(
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

        /// <summary>
        /// Count of accesses such that the access is not allowlisted based on an existing file (as opposed to determined by policy).
        /// </summary>
        /// <remarks>Counts a subset of <see cref="NumFileAccessViolationsNotAllowlisted"/></remarks>
        public readonly int NumFileExistenceAccessViolationsNotAllowlisted;

        /// <nodoc />
        public UnexpectedFileAccessCounters(
            int numFileAccessesAllowlistedButNotCacheable,
            int numFileAccessesAllowlistedAndCacheable,
            int numFileAccessViolationsNotAllowlisted,
            int numFileExistenceAccessViolationsNotAllowlisted)
        {
            NumFileAccessViolationsNotAllowlisted = numFileAccessViolationsNotAllowlisted;
            NumFileAccessesAllowlistedAndCacheable = numFileAccessesAllowlistedAndCacheable;
            NumFileAccessesAllowlistedButNotCacheable = numFileAccessesAllowlistedButNotCacheable;
            NumFileExistenceAccessViolationsNotAllowlisted = numFileExistenceAccessViolationsNotAllowlisted;
        }

        /// <summary>
        /// Indicates if this context has reported accesses which should mark the owning process as cache-ineligible.
        /// </summary>
        /// <remarks>
        /// File existence based violations don't necessarily make the pip uncacheable, since there may be relaxing policies in place
        /// </remarks>
        public bool HasUncacheableFileAccesses => (NumFileAccessViolationsNotAllowlisted + NumFileAccessesAllowlistedButNotCacheable) - NumFileExistenceAccessViolationsNotAllowlisted > 0;
    }
}
