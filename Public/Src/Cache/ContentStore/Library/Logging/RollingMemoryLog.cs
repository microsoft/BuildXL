// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.Linq;
using BuildXL.Cache.ContentStore.Interfaces.Logging;

namespace BuildXL.Cache.ContentStore.Logging
{
    /// <summary>
    ///     An in-memory ILog that accumulates a fixed-length backlog of messages, accessible for diagnostic context.
    /// </summary>
    public sealed class RollingMemoryLog : ILog
    {
        private const int EntryLimit = 1000;

        /// <summary>
        ///     Formats and returns the N most recent logging entries, each on a new line.
        /// </summary>
        /// <param name="count">Number of logging entries to return.</param>
        /// <returns>The N most recent logging entries, each on a new line.</returns>
        public string RecentEntriesString(int count)
        {
            Contract.Requires(count >= 0);
            return Environment.NewLine + string.Join(Environment.NewLine, RecentEntries(count));
        }

        /// <summary>
        ///     Returns the N most recent logging entries.
        /// </summary>
        /// <param name="count">Number of logging entries to return.</param>
        /// <returns>The N most recent logging entries.</returns>
        private IEnumerable<string> RecentEntries(int count)
        {
            Contract.Requires(count >= 0);
            return Enumerable.Range(0, count).Zip(
                _entries.Reverse(),
                (i, message) => string.Format(CultureInfo.CurrentCulture, "[{0},{1}]:{2}", i, message.Item1, message.Item2));
        }

        /// <summary>
        ///     The accumulated logging entries.
        /// </summary>
        private readonly ConcurrentQueue<Tuple<Severity, string>> _entries =
            new ConcurrentQueue<Tuple<Severity, string>>();

        /// <summary>
        ///     Initializes a new instance of the <see cref="RollingMemoryLog" /> class.
        /// </summary>
        /// <param name="severity">Logging severity to use.</param>
        public RollingMemoryLog(Severity severity)
        {
            CurrentSeverity = severity;
        }

        /// <inheritdoc />
        public void Dispose()
        {
        }

        /// <inheritdoc />
        public Severity CurrentSeverity { get; }

        /// <inheritdoc />
        public void Flush()
        {
        }

        /// <inheritdoc />
        public void Write(DateTime dateTime, int threadId, Severity severity, string message)
        {
            _entries.Enqueue(Tuple.Create(severity, message));
            Tuple<Severity, string> oldEntry;
            while (_entries.Count > EntryLimit && _entries.TryDequeue(out oldEntry))
            {
            }
        }
    }
}
