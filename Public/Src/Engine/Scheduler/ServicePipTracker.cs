// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.ContractsLight;
using System.Threading;
using BuildXL.Pips.Operations;
using BuildXL.Tracing;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Responsible for whatever tracking of service pips we might want to do, e.g., drop overhang.
    /// </summary>
    public sealed class ServicePipTracker
    {
        private readonly object m_lock = new object();
        private readonly PipExecutionContext m_context;
        private long m_lastNonServicePipCompletionTimeTicks;
        private long m_lastServicePipCompletionTimeTicks;
        private ImmutableDictionary<StringId, StringId> m_tagsToTrack;
        private readonly ConcurrentDictionary<StringId, long> m_tagToLastCompletionTimeMap;

        /// <summary>
        /// Time of the last completed non-service pip.
        /// </summary>
        public DateTime LastNonServicePipCompletionTime => new DateTime(Volatile.Read(ref m_lastNonServicePipCompletionTimeTicks));

        /// <summary>
        /// Time of the last completed service pip.
        /// </summary>
        public DateTime LastServicePipCompletionTime => new DateTime(Volatile.Read(ref m_lastServicePipCompletionTimeTicks));

        /// <nodoc/>
        public ServicePipTracker(PipExecutionContext context)
        {
            m_context = context;
            m_tagsToTrack = ImmutableDictionary<StringId, StringId>.Empty;
            m_tagToLastCompletionTimeMap = new ConcurrentDictionary<StringId, long>();
        }

        /// <nodoc/>
        public void ReportServicePipStarted(ServiceInfo serviceInfo)
        {
            Contract.Requires(serviceInfo.Kind == ServicePipKind.Service);
            Contract.Requires(serviceInfo.TagToTrack.IsValid, "Service must specify a valid tag to be added to the tracker");
            Contract.Requires(serviceInfo.DisplayNameForTrackableTag.IsValid);

            // Need to acquire a lock because multiple services might start concurrently.
            lock (m_lock)
            {
                // Several services might specify the same trackable tag (for example, a build producing multiple drops)
                // in this case we keep the first pair.
                if (!m_tagsToTrack.ContainsKey(serviceInfo.TagToTrack))
                {
                    m_tagsToTrack = m_tagsToTrack.Add(serviceInfo.TagToTrack, serviceInfo.DisplayNameForTrackableTag);
                }
            }
        }

        /// <nodoc/>
        public void ReportPipCompleted(Pip pip)
        {
            Contract.Requires(pip != null);

            if (!IsSupportedPipType(pip.PipType))
            {
                return;
            }

            long completedAt = DateTime.UtcNow.Ticks;

            // No need for a lock here because we iterate over the immutable collection. While a new service might start
            // (m_tagsToTrack changed in ReportServicePipStarted) at the same time a pip finished, that pip should not have
            // a tag specified by that service. For example, all drop pips can only finish after the corresponding 
            // DropDaemon service has started, i.e., if it's a drop pip, its tag is already in m_tagsToTrack.
            var tag = pip.Tags.IsValid ? TryGetTag(pip.Tags, m_tagsToTrack, m_context.StringTable) : null;
            if (tag != null)
            {
                Interlocked.Exchange(ref m_lastServicePipCompletionTimeTicks, completedAt);

                m_tagToLastCompletionTimeMap.AddOrUpdate(
                    tag.Value,
                    static (key, value) => value,
                    static (key, oldValue, value) => value,
                    completedAt);
            }
            else
            {
                Interlocked.Exchange(ref m_lastNonServicePipCompletionTimeTicks, completedAt);
            }
        }

        private bool IsSupportedPipType(PipType pipType)
        {
            switch (pipType)
            {
                case PipType.WriteFile:
                case PipType.CopyFile:
                case PipType.Process:
                case PipType.Ipc:
                case PipType.SealDirectory:
                    return true;

                case PipType.Value:
                case PipType.SpecFile:
                case PipType.Module:
                case PipType.HashSourceFile:
                    return false;

                default:
                    throw Contract.AssertFailure($"Unknown pip type: {pipType}");
            }
        }

        private static StringId? TryGetTag(IReadOnlyList<StringId> pipTags, ImmutableDictionary<StringId, StringId> tagsToTrack, StringTable stringTable)
        {
            StringId? result = null;
            foreach (var tag in pipTags.AsStructEnumerable())
            {
                if (tagsToTrack.ContainsKey(tag))
                {
                    Contract.Assert(result == null, $"Pip contains more than one trackable tag: {tag.ToString(stringTable)}, {result.Value.ToString(stringTable)}");
                    result = tag;
                }
            }

            return result;
        }

        /// <nodoc/>
        public void LogStats(LoggingContext loggingContext)
        {
            Contract.Requires(loggingContext != null);

            var lastNonServicePipCompletionTime = Volatile.Read(ref m_lastNonServicePipCompletionTimeTicks);
            if (m_tagToLastCompletionTimeMap.Count > 0 && lastNonServicePipCompletionTime > 0)
            {
                var stats = new Dictionary<string, long>();
                foreach (var kvp in m_tagToLastCompletionTimeMap)
                {
                    var overhangMs = (long)TimeSpan.FromTicks(kvp.Value - lastNonServicePipCompletionTime).TotalMilliseconds;
                    stats.Add(m_tagsToTrack[kvp.Key].ToString(m_context.StringTable), overhangMs);
                }

                Logger.Log.BulkStatistic(loggingContext, stats);
            }
        }
    }
}
