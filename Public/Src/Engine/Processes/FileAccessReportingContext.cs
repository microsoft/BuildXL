// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
        private readonly bool m_reportWhitelistedAccesses;
        private readonly FileAccessWhitelist m_fileAccessWhitelist;

        private List<ReportedFileAccess> m_violations;
        private List<ReportedFileAccess> m_whitelistedAccesses;
        private int m_numWhitelistedButNotCacheableFileAccessViolations;
        private int m_numWhitelistedAndCacheableFileAccessViolations;

        /// <summary>
        /// Creates a context. All <see cref="Counters"/> are initially zero and will increase as accesses are reported.
        /// </summary>
        public FileAccessReportingContext(LoggingContext loggingContext, PipExecutionContext context, ISandboxConfiguration config, Process pip, bool reportWhitelistedAccesses, FileAccessWhitelist whitelist = null)
        {
            Contract.Requires(loggingContext != null);
            Contract.Requires(context != null);
            Contract.Requires(config != null);
            Contract.Requires(pip != null);

            m_loggingContext = loggingContext;
            m_context = context;
            m_config = config;
            m_pip = pip;
            m_reportWhitelistedAccesses = reportWhitelistedAccesses;
            m_fileAccessWhitelist = whitelist;
        }

        /// <summary>
        /// Count of unexpected file accesses reported, by primary classification (e.g. whitelisted, cacheable, or a violation).
        /// </summary>
        public UnexpectedFileAccessCounters Counters => new UnexpectedFileAccessCounters(
            numFileAccessesWhitelistedButNotCacheable: m_numWhitelistedButNotCacheableFileAccessViolations,
            numFileAccessViolationsNotWhitelisted: m_violations == null ? 0 : m_violations.Count,
            numFileAccessesWhitelistedAndCacheable: m_numWhitelistedAndCacheableFileAccessViolations);

        /// <summary>
        /// Gets the collection of unexpected file accesses reported so far that were not whitelisted. These are 'violations'.
        /// </summary>
        public IReadOnlyList<ReportedFileAccess> FileAccessViolationsNotWhitelisted => m_violations;

        /// <summary>
        /// Gets the collection of unexpected file accesses reported so far that were whitelisted. These may be violations
        /// in distributed builds.
        /// </summary>
        public IReadOnlyList<ReportedFileAccess> WhitelistedFileAccessViolations => m_whitelistedAccesses;

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
        /// a single path), reports each constituent access and computes an aggregate whitelist match type (the least permissive of any
        /// individual access).
        /// </summary>
        public FileAccessWhitelist.MatchType MatchAndReportUnexpectedObservedFileAccess(ObservedFileAccess unexpectedObservedFileAccess)
        {
            var aggregateMatch = FileAccessWhitelist.MatchType.MatchesAndCacheable;
            foreach (ReportedFileAccess reportedAccess in unexpectedObservedFileAccess.Accesses)
            {
                FileAccessWhitelist.MatchType thisMatch = MatchAndReportUnexpectedFileAccess(reportedAccess);

                switch (thisMatch)
                {
                    case FileAccessWhitelist.MatchType.NoMatch:
                        aggregateMatch = FileAccessWhitelist.MatchType.NoMatch;
                        break;
                    case FileAccessWhitelist.MatchType.MatchesButNotCacheable:
                        if (aggregateMatch == FileAccessWhitelist.MatchType.MatchesAndCacheable)
                        {
                            aggregateMatch = FileAccessWhitelist.MatchType.MatchesButNotCacheable;
                        }

                        break;
                    default:
                        Contract.Assert(thisMatch == FileAccessWhitelist.MatchType.MatchesAndCacheable);
                        break;
                }
            }

            return aggregateMatch;
        }

        /// <summary>
        /// Reports an access that - ignoring whitelisting - was unexpected. This can be due to a manifest-side or BuildXL-side denial decision.
        /// </summary>
        private FileAccessWhitelist.MatchType MatchAndReportUnexpectedFileAccess(ReportedFileAccess unexpectedFileAccess)
        {
            if (m_fileAccessWhitelist != null && m_fileAccessWhitelist.HasEntries)
            {
                Contract.Assert(
                    m_config.FailUnexpectedFileAccesses == false,
                    "Having a file-access whitelist requires that Detours failure injection is off.");

                FileAccessWhitelist.MatchType matchType = m_fileAccessWhitelist.Matches(unexpectedFileAccess, m_pip);
                switch (matchType)
                {
                    case FileAccessWhitelist.MatchType.NoMatch:
                        AddUnexpectedFileAccessNotWhitelisted(unexpectedFileAccess);
                        ReportUnexpectedFileAccessNotWhitelisted(unexpectedFileAccess);
                        break;
                    case FileAccessWhitelist.MatchType.MatchesButNotCacheable:
                        AddUnexpectedFileAccessWhitelisted(unexpectedFileAccess);
                        m_numWhitelistedButNotCacheableFileAccessViolations++;
                        ReportWhitelistedFileAccessNonCacheable(unexpectedFileAccess);
                        break;
                    case FileAccessWhitelist.MatchType.MatchesAndCacheable:
                        AddUnexpectedFileAccessWhitelisted(unexpectedFileAccess);
                        m_numWhitelistedAndCacheableFileAccessViolations++;
                        ReportWhitelistedFileAccessCacheable(unexpectedFileAccess);
                        break;
                    default:
                        throw Contract.AssertFailure("Unknown whitelist-match type.");
                }

                return matchType;
            }
            else
            {
                AddUnexpectedFileAccessNotWhitelisted(unexpectedFileAccess);
                ReportUnexpectedFileAccessNotWhitelisted(unexpectedFileAccess);
                return FileAccessWhitelist.MatchType.NoMatch;
            }
        }

        private void AddUnexpectedFileAccessNotWhitelisted(ReportedFileAccess reportedFileAccess)
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

        private void AddUnexpectedFileAccessWhitelisted(ReportedFileAccess reportedFileAccess)
        {
            if (m_whitelistedAccesses == null)
            {
                m_whitelistedAccesses = new List<ReportedFileAccess>();
            }

            if (reportedFileAccess.Operation != ReportedFileOperation.NtCreateFile || m_config.UnsafeSandboxConfiguration.MonitorNtCreateFile)
            {
                m_whitelistedAccesses.Add(reportedFileAccess);
            }
        }

        private void ReportUnexpectedFileAccessNotWhitelisted(ReportedFileAccess reportedFileAccess)
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

        private void ReportWhitelistedFileAccessNonCacheable(ReportedFileAccess reportedFileAccess)
        {
            string path = reportedFileAccess.GetPath(m_context.PathTable);
            string description = reportedFileAccess.Describe();

            if (m_reportWhitelistedAccesses)
            {
                BuildXL.Processes.Tracing.Logger.Log.PipProcessUncacheableWhitelistNotAllowedInDistributedBuilds(
                    m_loggingContext,
                    m_pip.SemiStableHash,
                    m_pip.GetDescription(m_context),
                    description,
                    path);

                AddUnexpectedFileAccessNotWhitelisted(reportedFileAccess);
            }
            else
            {
                BuildXL.Processes.Tracing.Logger.Log.PipProcessDisallowedFileAccessWhitelistedNonCacheable(
                    m_loggingContext,
                    m_pip.SemiStableHash,
                    m_pip.GetDescription(m_context),
                    description,
                    path);
            }
        }

        private void ReportWhitelistedFileAccessCacheable(ReportedFileAccess reportedFileAccess)
        {
            string path = reportedFileAccess.GetPath(m_context.PathTable);
            string description = reportedFileAccess.Describe();

            BuildXL.Processes.Tracing.Logger.Log.PipProcessDisallowedFileAccessWhitelistedCacheable(
                m_loggingContext,
                m_pip.SemiStableHash,
                m_pip.GetDescription(m_context),
                description,
                path);
        }
    }

    /// <summary>
    /// Counters accumulated in a <see cref="FileAccessReportingContext"/> per classification of 'unexpected' file access.
    /// An unexpected access is either a violation or was whitelisted.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1815:ShouldOverrideEquals")]
    public readonly struct UnexpectedFileAccessCounters
    {
        /// <summary>
        /// Count of accesses such that the access was whitelisted, but was not in the cache-friendly part of the whitelist. The pip should not be cached.
        /// </summary>
        public readonly int NumFileAccessesWhitelistedButNotCacheable;

        /// <summary>
        /// Count of accesses such that the access was whitelisted, via the cache-friendly part of the whitelist. The pip may be cached.
        /// </summary>
        public readonly int NumFileAccessesWhitelistedAndCacheable;

        /// <summary>
        /// Count of accesses such that the access was not whitelisted at all, and should be reported as a violation.
        /// </summary>
        public readonly int NumFileAccessViolationsNotWhitelisted;

        /// <nodoc />
        public UnexpectedFileAccessCounters(
            int numFileAccessesWhitelistedButNotCacheable,
            int numFileAccessesWhitelistedAndCacheable,
            int numFileAccessViolationsNotWhitelisted)
        {
            NumFileAccessViolationsNotWhitelisted = numFileAccessViolationsNotWhitelisted;
            NumFileAccessesWhitelistedAndCacheable = numFileAccessesWhitelistedAndCacheable;
            NumFileAccessesWhitelistedButNotCacheable = numFileAccessesWhitelistedButNotCacheable;
        }

        /// <summary>
        /// Returns the sum of counters for those accesses that were violations or were whitelisted. This total excludes only those accesses
        /// that were allowed on their own merit, i.e., not 'unexpected' and handled.
        /// </summary>
        public int TotalUnexpectedFileAccesses => NumFileAccessesWhitelistedButNotCacheable + NumFileAccessesWhitelistedAndCacheable + NumFileAccessViolationsNotWhitelisted;

        /// <summary>
        /// Indicates if this context has reported accesses which should mark the owning process as cache-ineligible.
        /// </summary>
        public bool HasUncacheableFileAccesses => (NumFileAccessViolationsNotWhitelisted + NumFileAccessesWhitelistedButNotCacheable) > 0;
    }
}
