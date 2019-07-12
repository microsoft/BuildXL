// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Diagnostics.Tracing;

namespace BuildXL.Utilities.Tracing
{
    /// <summary>
    /// Allows events to be disabled/enabled on a per-event basis
    /// </summary>
    public sealed class EventMask
    {
        /// <summary>
        /// True if events in <see cref="m_events"/> are selectively enabled. Otherwise they are selectively disabled
        /// </summary>
        public readonly bool IsSelectivelyEnabled;

        private readonly HashSet<int> m_events;

        /// <summary>
        /// Level at which events may not be masked.
        /// </summary>
        private readonly EventLevel? m_nonMaskableLevel;

        /// <summary>
        /// Constructor. Exactly one of enabledEvents disabledEvents must be specified. The other must be null.
        /// </summary>
        /// <param name="nonMaskableLevel">Level at which events may not be masked. Any event at this or a higher precedence (lower
        /// numeric level) will not be masked</param>
        /// <param name="enabledEvents">events to be enabled</param>
        /// <param name="disabledEvents">events to be disabled</param>
        public EventMask(IEnumerable<int> enabledEvents, IEnumerable<int> disabledEvents, EventLevel? nonMaskableLevel)
            : this(enabledEvents, disabledEvents)
        {
            Contract.Requires(
                enabledEvents == null ^ disabledEvents == null,
                "Either enabledEvents or disabledEvents must be specified. Both may not be");

            m_nonMaskableLevel = nonMaskableLevel;
        }

        /// <summary>
        /// Constructor. Exactly one of enabledEvents disabledEvents must be specified. The other must be null.
        /// </summary>
        /// <param name="enabledEvents">events to be enabled</param>
        /// <param name="disabledEvents">events to be disabled</param>
        public EventMask(IEnumerable<int> enabledEvents, IEnumerable<int> disabledEvents)
        {
            Contract.Requires(
                enabledEvents == null ^ disabledEvents == null,
                "Either enabledEvents or disabledEvents must be specified. Both may not be");

            if (enabledEvents != null)
            {
                m_events = new HashSet<int>(enabledEvents);
                IsSelectivelyEnabled = true;
            }
            else
            {
                m_events = new HashSet<int>(disabledEvents);
                IsSelectivelyEnabled = false;
            }
        }

        /// <summary>
        /// Adds additional EventIds to the the existing EventMask
        /// </summary>
        public void AddEventIds(IEnumerable<int> additionalEventIds)
        {
            foreach (int additionalEvent in additionalEventIds)
            {
                m_events.Add(additionalEvent);
            }
        }

        /// <summary>
        /// Checks whether an event is enabled
        /// </summary>
        /// <param name="eventLevel">EventLevel for the event in question</param>
        /// <param name="eventId">EventId for the event in question</param>
        /// <returns>whether the event is enabled</returns>
        public bool IsEnabled(EventLevel eventLevel, int eventId)
        {
            // events at a lower level than m_maskableLevel may not be masked
            if (m_nonMaskableLevel.HasValue && eventLevel <= m_nonMaskableLevel.Value)
            {
                return true;
            }

            return IsSelectivelyEnabled ^ !m_events.Contains(eventId);
        }
    }
}
