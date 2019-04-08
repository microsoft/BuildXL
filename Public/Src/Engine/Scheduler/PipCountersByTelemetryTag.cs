// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Counter collections for process pips grouped by a special telemetry tag.
    /// </summary>
    public sealed class PipCountersByTelemetryTag : PipCountersByGroupCategoryBase<StringId>
    {
        private const string DefaultTelemetryTagPrefix = "telemetry:";

        private readonly string m_telemetryTagPrefix;

        private readonly StringTable m_stringTable;

        /// <summary>
        /// Creates an instance of <see cref="PipCountersByTelemetryTag"/>.
        /// </summary>
        public PipCountersByTelemetryTag(LoggingContext loggingContext, StringTable stringTable, string customTelemetryTagPrefix = null)
            : base(loggingContext)
        {
            Contract.Requires(stringTable != null);

            m_stringTable = stringTable;
            m_telemetryTagPrefix = !string.IsNullOrWhiteSpace(customTelemetryTagPrefix) 
                ? (customTelemetryTagPrefix.EndsWith(":") ? customTelemetryTagPrefix : customTelemetryTagPrefix + ":") // Ensure prefix ends with ':'.
                : DefaultTelemetryTagPrefix;
        }

        /// <inheritdoc />
        protected override string CategoryToString(StringId category) => category.ToString(m_stringTable);

        /// <inheritdoc />
        protected override IEnumerable<StringId> GetCategories(Process process)
        {
            foreach (var tag in process.Tags)
            {
                var expandedTag = tag.ToString(m_stringTable);
                if (expandedTag.StartsWith(m_telemetryTagPrefix))
                {
                    var telemetryTag = StringId.Create(m_stringTable, expandedTag.Substring(m_telemetryTagPrefix.Length));
                    yield return telemetryTag;
                }
            }
        }

        /// <summary>
        /// Gets counter value by tag.
        /// </summary>
        public long GetCounterValue(StringId tag, PipCountersByGroup counter) => CountersByGroup.TryGetValue(tag, out var pipCounter) ? pipCounter.GetCounterValue(counter) : 0;

        /// <summary>
        /// Gets elapsed time by tag.
        /// </summary>
        public TimeSpan GetElapsedTime(StringId tag, PipCountersByGroup counter) => CountersByGroup.TryGetValue(tag, out var pipCounter) ? pipCounter.GetElapsedTime(counter) : TimeSpan.Zero;

        /// <summary>
        /// Gets counter value by tag.
        /// </summary>
        public long GetCounterValue(string tag, PipCountersByGroup counter) => GetCounterValue(StringId.Create(m_stringTable, tag), counter);

        /// <summary>
        /// Gets elapsed time by tag.
        /// </summary>
        public TimeSpan GetElapsedTime(string tag, PipCountersByGroup counter) => GetElapsedTime(StringId.Create(m_stringTable, tag), counter);

        /// <summary>
        /// Gets elapsed time by tag.
        /// </summary>
        public Dictionary<string, TimeSpan> GetElapsedTimes(PipCountersByGroup counter) => CountersByGroup.Keys.ToDictionary(x => x.ToString(m_stringTable), x => GetElapsedTime(x, counter));
    }
}
