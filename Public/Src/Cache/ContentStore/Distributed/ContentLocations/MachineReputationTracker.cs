// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed
{
    /// <summary>
    /// Enumeration for different machine reputation states.
    /// NOTE: Higher values ordered later in when ordering machines by reputation
    /// </summary>
    public enum MachineReputation
    {
        /// <summary>
        /// The reputation is good (content was obtained successfully) or the reputation is unknown.
        /// </summary>
        Good,

        /// <summary>
        /// Content was missing on the machine.
        /// </summary>
        Missing,

        /// <summary>
        /// The machine is currently marked as inactive
        /// </summary>
        Inactive,

        /// <summary>
        /// The timeout occurred when a content was copied from the machine.
        /// </summary>
        Timeout,

        /// <summary>
        /// Error occurred when content was copied from the machine.
        /// </summary>
        Bad,

        /// <summary>
        /// A machine id is not known for the tracker.
        /// </summary>
        Unknown,
    }

    /// <summary>
    /// Configuration settings for <see cref="MachineReputationTracker"/>.
    /// </summary>
    public class MachineReputationTrackerConfiguration
    {
        /// <summary>
        /// Timeout when a bad reputation is removed for a machine.
        /// </summary>
        public TimeSpan BadReputationTtl { get; set; } = TimeSpan.FromMinutes(20);

        /// <summary>
        /// Timeout when a missing reputation is removed for a machine.
        /// </summary>
        public TimeSpan MissingContentReputationTtl { get; set; } = TimeSpan.FromMinutes(20);
    }

    /// <summary>
    /// Tracks machine reputation for distributed content location store.
    /// </summary>
    public class MachineReputationTracker
    {
        private static readonly Tracer Tracer = new Tracer(nameof(MachineReputationTracker));

        private readonly Context _context;
        private readonly IClock _clock;
        private readonly MachineReputationTrackerConfiguration _configuration;
        private readonly ClusterState _clusterState;
        private readonly ConcurrentDictionary<MachineId, ReputationState> _reputations = new ();

        /// <nodoc />
        public MachineReputationTracker(
            Context context,
            IClock clock,
            ClusterState clusterState,
            MachineReputationTrackerConfiguration? configuration = null)
        {
            _context = context;
            _clock = clock;
            _configuration = configuration ?? new MachineReputationTrackerConfiguration();
            _clusterState = clusterState;
        }

        /// <summary>
        /// Reports about new reputation for a given location.
        /// </summary>
        public virtual void ReportReputation(MachineLocation location, MachineReputation reputation)
        {
            if (_clusterState.TryResolveMachineId(location, out var machineId))
            {
                var reputationState = _reputations.GetOrAdd(machineId, _ => new ReputationState());
                string displayLocation = location.ToString();

                ChangeReputation(reputation, reputationState, displayLocation, reason: "");
            }
            else
            {
                // The machine is unknown.
                Tracer.Warning(_context, $"Can't change machine reputation for {location} because machine id resolution failed.");
            }
        }
        
        /// <summary>
        /// Gets a current reputation for a given machine and resets the reputation if bad reputation expires.
        /// </summary>
        public virtual MachineReputation GetReputationByMachineLocation(MachineLocation machine)
        {
            if (_clusterState.TryResolveMachineId(machine, out var machineId))
            {
                return GetReputation(machineId);
            }

            // This is unknown machine. Don't fail here
            return MachineReputation.Unknown;
        }

        /// <summary>
        /// Gets a current reputation for a given machine id and resets the reputation if bad reputation expires.
        /// </summary>
        /// <remarks>
        /// Returns <see cref="MachineReputation.Good"/> if the <paramref name="machineId"/> is unknown.
        /// </remarks>
        public virtual MachineReputation GetReputation(MachineId machineId)
        {
            if (_clusterState.IsMachineMarkedInactive(machineId) || _clusterState.IsMachineMarkedClosed(machineId))
            {
                return MachineReputation.Inactive;
            }

            if (!_reputations.TryGetValue(machineId, out var state))
            {
                // If the reputation was never reported, consider that it's good.
                return MachineReputation.Good;
            }

            if (state.ExpireTime < _clock.UtcNow)
            {
                string machineName;
                if (_clusterState.TryResolve(machineId, out var machine))
                {
                    machineName = machine.ToString();
                }
                else
                {
                    machineName = machineId.ToString();
                }

                ChangeReputation(MachineReputation.Good, state, machineName, $" due to expiry (expire time: {state.ExpireTime}, current time: {_clock.UtcNow})");
                return MachineReputation.Good;
            }

            return state.Reputation;
        }

        private void ChangeReputation(MachineReputation reputation, ReputationState reputationState, string displayLocation, string reason)
        {
            bool reputationChanged = false;
            var oldReputation = reputationState.Reputation;

            lock (reputationState)
            {
                reputationChanged = reputationState.Reputation != reputation;

                // Setting a new reputation.
                // NOTE: This means that the transition from 'Bad' to 'Missing' is possible here. We allow this because Missing means
                // we were able to connect to the machine, which means that we restored connectivity and the machine's reputation should no
                // longer be 'Bad'
                reputationState.Reputation = reputation;

                switch (reputation)
                {
                    case MachineReputation.Bad:
                    case MachineReputation.Timeout:
                        reputationState.ExpireTime = _clock.UtcNow + _configuration.BadReputationTtl;
                        break;
                    case MachineReputation.Missing:
                        reputationState.ExpireTime = _clock.UtcNow + _configuration.MissingContentReputationTtl;
                        break;
                    case MachineReputation.Good:
                        reputationState.ExpireTime = DateTime.MaxValue;
                        break;
                }
            }

            if (reputationChanged)
            {
                Tracer.Debug(_context, $"Changed reputation{reason} (new: {reputation}, old: {oldReputation}) for machine with location {displayLocation}.");
            }
        }

        private class ReputationState
        {
            public DateTime ExpireTime { get; set; }

            public MachineReputation Reputation { get; set; }
        }
    }
}
