// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Threading;

namespace BuildXL.Utilities.Core
{
    /// <summary>
    /// Counters for memory conservation.
    /// </summary>
    public enum MemoryConservationCounter
    {
        /// <summary>
        /// Number of times memory conservation became active.
        /// </summary>
        ActivationCount,

        /// <summary>
        /// Number of garbage collections requested after entering memory conservation.
        /// </summary>
        GarbageCollectionCount,

        /// <summary>
        /// Total time spent in memory conservation.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        ActiveDuration,
    }

    /// <summary>
    /// Responds to changes in memory conservation mode and releases expendable memory retained for performance.
    /// </summary>
    public interface IMemoryConservationTarget
    {
        /// <summary>
        /// Notifies the target that a coordinator entered or exited memory conservation mode. Targets should
        /// release expendable memory when activated and avoid rebuilding it until memory conservation ends.
        /// </summary>
        void OnMemoryConservationStateChanged(bool isActive);
    }

    /// <summary>
    /// Coordinates memory conservation for a BuildXL invocation.
    /// </summary>
    public sealed class MemoryConservation
    {
        private static readonly TimeSpan DefaultMinimumActiveDuration = TimeSpan.FromSeconds(30);

        private readonly object m_syncLock = new object();
        private readonly List<WeakReference<IMemoryConservationTarget>> m_targets = new List<WeakReference<IMemoryConservationTarget>>();

        // Conservation remains latched for this duration after activation. Memory pressure can fluctuate around the
        // scheduler threshold, and immediately exiting would allow the next pressure sample to clear caches and
        // request another GC. The default 30-second latch bounds how frequently those expensive actions can repeat.
        private readonly TimeSpan m_minimumActiveDuration;
        private readonly Action m_collectGarbage;

        private int m_isActive;
        private int m_registrationsSinceCleanup;

        // This remains fixed for the activation so it can enforce the minimum active duration.
        private long m_activeStartTimestamp;

        // This advances whenever active duration is added to the counter to avoid double-counting, so it cannot
        // also be used to enforce the minimum active duration.
        private long m_activeDurationStartTimestamp;

        /// <summary>
        /// Creates a memory conservation coordinator.
        /// </summary>
        public MemoryConservation()
            : this(DefaultMinimumActiveDuration, CollectGarbage)
        {
        }

        internal MemoryConservation(TimeSpan minimumActiveDuration)
            : this(minimumActiveDuration, CollectGarbage)
        {
        }

        internal MemoryConservation(TimeSpan minimumActiveDuration, Action collectGarbage)
        {
            Contract.Requires(minimumActiveDuration >= TimeSpan.Zero);
            Contract.RequiresNotNull(collectGarbage);

            m_minimumActiveDuration = minimumActiveDuration;
            m_collectGarbage = collectGarbage;
        }

        /// <summary>
        /// Statistics for memory conservation.
        /// </summary>
        public CounterCollection<MemoryConservationCounter> Counters { get; } = new CounterCollection<MemoryConservationCounter>();

        /// <summary>
        /// Whether memory conservation mode is active.
        /// </summary>
        public bool IsActive => Volatile.Read(ref m_isActive) != 0;

        /// <summary>
        /// Registers a participant without extending its lifetime.
        /// </summary>
        public void Register(IMemoryConservationTarget target)
        {
            Contract.Requires(target != null);

            lock (m_syncLock)
            {
                m_targets.Add(new WeakReference<IMemoryConservationTarget>(target));

                if (++m_registrationsSinceCleanup >= 256)
                {
                    RemoveDeadTargets();
                    m_registrationsSinceCleanup = 0;
                }

                if (IsActive)
                {
                    target.OnMemoryConservationStateChanged(isActive: true);
                }
            }
        }

        /// <summary>
        /// Whether a target is currently registered. Intended for tests only.
        /// </summary>
        internal bool IsRegisteredForTesting(IMemoryConservationTarget target)
        {
            lock (m_syncLock)
            {
                foreach (var weakReference in m_targets)
                {
                    if (weakReference.TryGetTarget(out var registeredTarget) && ReferenceEquals(target, registeredTarget))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// Enters memory conservation mode.
        /// </summary>
        /// <remarks>
        /// Repeated calls while conservation is latched are no-ops. Targets release memory and a collection is
        /// requested only on the initial inactive-to-active transition.
        /// </remarks>
        /// <returns>Whether memory conservation became active.</returns>
        public bool Enter()
        {
            long now = Stopwatch.GetTimestamp();
            bool activated = false;

            lock (m_syncLock)
            {
                if (!IsActive)
                {
                    Volatile.Write(ref m_isActive, 1);
                    m_activeStartTimestamp = now;
                    m_activeDurationStartTimestamp = now;
                    Counters.IncrementCounter(MemoryConservationCounter.ActivationCount);

                    foreach (var target in GetLiveTargets())
                    {
                        target.OnMemoryConservationStateChanged(isActive: true);
                    }

                    activated = true;
                }
            }

            if (activated)
            {
                Counters.IncrementCounter(MemoryConservationCounter.GarbageCollectionCount);
                m_collectGarbage();
            }

            return activated;
        }

        /// <summary>
        /// Returns to normal memory retention behavior.
        /// </summary>
        /// <remarks>
        /// By default, conservation remains active until the minimum active duration has elapsed. This prevents
        /// alternating healthy and constrained memory samples from repeatedly releasing caches and requesting GC.
        /// Forced exit is intended only for invocation teardown, when there will be no subsequent reactivation.
        /// </remarks>
        /// <param name="force">Whether to bypass the minimum active duration.</param>
        /// <returns>Whether memory conservation became inactive.</returns>
        public bool Exit(bool force = false)
        {
            long now = Stopwatch.GetTimestamp();

            lock (m_syncLock)
            {
                if (!IsActive)
                {
                    return false;
                }

                if (!force && GetElapsedTime(m_activeStartTimestamp, now) < m_minimumActiveDuration)
                {
                    return false;
                }

                return ExitCore();
            }
        }

        /// <summary>
        /// Includes the current active interval in <see cref="Counters"/> without exiting memory conservation.
        /// </summary>
        /// <remarks>
        /// This may be called repeatedly; each call accounts only for time elapsed since the previous snapshot.
        /// The original activation timestamp remains unchanged so snapshotting does not extend the exit latch.
        /// </remarks>
        public void SnapshotCounters()
        {
            long now = Stopwatch.GetTimestamp();

            lock (m_syncLock)
            {
                if (IsActive)
                {
                    Counters.AddToCounter(MemoryConservationCounter.ActiveDuration, GetElapsedTime(m_activeDurationStartTimestamp, now));
                    m_activeDurationStartTimestamp = now;
                }
            }
        }

        private bool ExitCore()
        {
            if (IsActive)
            {
                SnapshotCounters();
                m_activeStartTimestamp = 0;
                m_activeDurationStartTimestamp = 0;
                Volatile.Write(ref m_isActive, 0);
                foreach (var target in GetLiveTargets())
                {
                    target.OnMemoryConservationStateChanged(isActive: false);
                }

                return true;
            }

            return false;
        }

        private List<IMemoryConservationTarget> GetLiveTargets()
        {
            var liveTargets = new List<IMemoryConservationTarget>(m_targets.Count);

            for (int i = m_targets.Count - 1; i >= 0; i--)
            {
                if (m_targets[i].TryGetTarget(out var target))
                {
                    liveTargets.Add(target);
                }
                else
                {
                    m_targets.RemoveAt(i);
                }
            }

            return liveTargets;
        }

        private void RemoveDeadTargets()
        {
            for (int i = m_targets.Count - 1; i >= 0; i--)
            {
                if (!m_targets[i].TryGetTarget(out _))
                {
                    m_targets.RemoveAt(i);
                }
            }
        }

        private static TimeSpan GetElapsedTime(long startTimestamp, long endTimestamp)
        {
            if (startTimestamp == 0)
            {
                return TimeSpan.MaxValue;
            }

            return TimeSpan.FromSeconds((double)(endTimestamp - startTimestamp) / Stopwatch.Frequency);
        }

        private static void CollectGarbage()
        {
            // Reclaimed caches are generally long-lived, so request a background Gen2 collection.
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, blocking: false, compacting: false);
        }
    }
}
