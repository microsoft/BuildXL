// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

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

        /// <summary>
        /// If true machine reputation tracking is on.
        /// </summary>
        public bool Enabled { get; set; } = true;
    }

    /// <summary>
    /// Tracks machine reputation for distributed content location store.
    /// </summary>
    public class MachineReputationTracker
    {
        private readonly Context _context;
        private readonly IClock _clock;
        private readonly MachineReputationTrackerConfiguration _configuration;
        private readonly ClusterState _clusterState;
        private readonly Func<MachineId, MachineLocation> _machineLocationResolver;
        private readonly ConcurrentDictionary<MachineLocation, ReputationState> _reputations = new ConcurrentDictionary<MachineLocation, ReputationState>();

        private bool Enabled => _configuration.Enabled;

        /// <nodoc />
        public MachineReputationTracker(
            Context context,
            IClock clock,
            MachineReputationTrackerConfiguration configuration,
            Func<MachineId, MachineLocation> machineLocationResolver,
            ClusterState clusterState = null)
        {
            _context = context;
            _clock = clock;
            _configuration = configuration;
            _clusterState = clusterState;
            _machineLocationResolver = machineLocationResolver;
        }

        /// <summary>
        /// Reports about new reputation for a given location.
        /// </summary>
        public virtual void ReportReputation(MachineLocation location, MachineReputation reputation)
        {
            if (!Enabled)
            {
                return;
            }

            if (reputation == MachineReputation.Good
                && _clusterState != null
                && _clusterState.TryResolveMachineId(location, out var machineId)
                && _clusterState.IsMachineMarkedInactive(machineId))
            {
                _context.Debug($"Marked machine {machineId}='{location}' active due to report of good reputation.");
                _clusterState.MarkMachineActive(machineId);
            }

            var reputationState = _reputations.GetOrAdd(location, _ => new ReputationState());
            string displayLocation = location.ToString();

            ChangeReputation(reputation, reputationState, displayLocation, reason: "");
        }

        /// <summary>
        /// Gets a current reputation for a given machine and resets the reputation if bad reputation expires.
        /// </summary>
        public virtual MachineReputation GetReputation(MachineId machine) => GetReputation(_machineLocationResolver(machine));

        /// <summary>
        /// Gets a current reputation for a given machine and resets the reputation if bad reputation expires.
        /// </summary>
        public virtual MachineReputation GetReputation(MachineLocation machine)
        {
            if (!Enabled)
            {
                return MachineReputation.Good;
            }

            if (_clusterState != null 
                && _clusterState.TryResolveMachineId(machine, out var machineId))
            {
                if (_clusterState.IsMachineMarkedInactive(machineId))
                {
                    return MachineReputation.Inactive;
                }
            }

            if (!_reputations.TryGetValue(machine, out var state))
            {
                return MachineReputation.Good;
            }

            if (state.ExpireTime < _clock.UtcNow)
            {
                ChangeReputation(MachineReputation.Good, state, machine.ToString(), $" due to expiry (expire time: {state.ExpireTime}, current time: {_clock.UtcNow})");
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
                _context.Debug($"Changed reputation{reason} (new: {reputation}, old: {oldReputation}) for machine with location {displayLocation}.");
            }
        }

        private class ReputationState
        {
            public DateTime ExpireTime { get; set; }

            public MachineReputation Reputation { get; set; }
        }
    }
}
