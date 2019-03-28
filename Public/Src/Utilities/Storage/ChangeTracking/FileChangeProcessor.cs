// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Storage.ChangeTracking
{
    /// <summary>
    /// Class for processing changes on files tracked by <see cref="FileChangeTracker" />.
    /// </summary>
    public class FileChangeProcessor
    {
        private readonly FileChangeTracker m_fileChangeTracker;
        private ScanningJournalResult m_scanningJournalResult;
        private readonly List<FileChangeTrackerUnsubscriber> m_unsubscribers = new List<FileChangeTrackerUnsubscriber>(2);
        private readonly LoggingContext m_loggingContext;

        /// <summary>
        /// Creates an instance of <see cref="FileChangeProcessor"/>.
        /// </summary>
        public FileChangeProcessor(LoggingContext loggingContext, FileChangeTracker fileChangeTracker)
        {
            Contract.Requires(loggingContext != null);
            Contract.Requires(fileChangeTracker != null);
            Contract.Requires(fileChangeTracker.IsTrackingChanges);

            m_loggingContext = loggingContext;
            m_fileChangeTracker = fileChangeTracker;
        }

        /// <summary>
        /// Subscribes an instance of <see cref="IFileChangeTrackingObserver"/>.
        /// </summary>
        public void Subscribe(IFileChangeTrackingObserver observer)
        {
            Contract.Requires(observer != null);

            var unsubscriber = m_fileChangeTracker.Subscribe(observer) as FileChangeTrackerUnsubscriber;
            Contract.Assert(unsubscriber != null);

            m_unsubscribers.Add(unsubscriber);
        }

        /// <summary>
        /// Processes changes to files since the last checkpoint.
        /// </summary>
        public ScanningJournalResult TryProcessChanges(
            TimeSpan? timeLimit = null,
            JournalProcessingStatistics.LogMessage logMessage = null,
            JournalProcessingStatistics.LogStats logStats = null)
        {
            if (m_scanningJournalResult != null)
            {
                return m_scanningJournalResult;
            }

            // Initialize observers.
            foreach (var unsubscriber in m_unsubscribers)
            {
                unsubscriber.Observer.OnInit();
            }

            m_scanningJournalResult = m_fileChangeTracker.TryProcessChanges(timeLimit);

            // Unsubscribe observers.
            foreach (var unsubscriber in m_unsubscribers)
            {
                unsubscriber.Dispose();
            }

            var journalStatistics = new JournalProcessingStatistics(m_scanningJournalResult);
            journalStatistics.Log(m_loggingContext, logMessage, logStats);

            return m_scanningJournalResult;
        }
    }
}
