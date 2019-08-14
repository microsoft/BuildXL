// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Storage.InputChange;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Storage.ChangeTracking
{
    /// <summary>
    /// Class for processing changes on files tracked by <see cref="FileChangeTracker" />.
    /// </summary>
    public class FileChangeProcessor
    {
        private readonly FileChangeTracker m_fileChangeTracker;
        private readonly InputChangeList m_inputChangeList;
        private readonly List<FileChangeTrackerUnsubscriber> m_fileChangeTrackerUnsubscribers = new List<FileChangeTrackerUnsubscriber>(2);
        private readonly List<InputChangeListUnsubscriber> m_inputChangeListUnsubscribers = new List<InputChangeListUnsubscriber>(2);
        private readonly LoggingContext m_loggingContext;

        /// <summary>
        /// Creates an instance of <see cref="FileChangeProcessor"/>.
        /// </summary>
        public FileChangeProcessor(
            LoggingContext loggingContext, 
            FileChangeTracker fileChangeTracker, 
            InputChangeList inputChangeList = null)
        {
            Contract.Requires(loggingContext != null);
            Contract.Requires(fileChangeTracker != null);
            Contract.Requires(fileChangeTracker.IsTrackingChanges);

            m_loggingContext = loggingContext;
            m_fileChangeTracker = fileChangeTracker;
            m_inputChangeList = inputChangeList;
        }

        /// <summary>
        /// Subscribes an instance of <see cref="IFileChangeTrackingObserver"/>.
        /// </summary>
        public void Subscribe(IFileChangeTrackingObserver observer)
        {
            Contract.Requires(observer != null);

            var fileChangeTrackerUnsubscriber = m_fileChangeTracker.Subscribe(observer) as FileChangeTrackerUnsubscriber;
            Contract.Assert(fileChangeTrackerUnsubscriber != null);

            m_fileChangeTrackerUnsubscribers.Add(fileChangeTrackerUnsubscriber);

            if (m_inputChangeList != null)
            {
                var inputChangeListUnsubscriber = m_inputChangeList.Subscribe(observer) as InputChangeListUnsubscriber;
                Contract.Assert(inputChangeListUnsubscriber != null);

                m_inputChangeListUnsubscribers.Add(inputChangeListUnsubscriber);
            }
        }

        /// <summary>
        /// Processes changes to files since the last checkpoint.
        /// </summary>
        public ScanningJournalResult TryProcessChanges(
            TimeSpan? timeLimit = null,
            JournalProcessingStatistics.LogMessage logMessage = null,
            JournalProcessingStatistics.LogStats logStats = null)
        {
            InitObservers();

            // Input change list needs to be processed first because file change tracker
            // makes a completion for the observers currently.
            // TODO: Make a better design for many-to-many observable-observer relations.
            m_inputChangeList?.ProcessChanges();
            var scanningJournalResult = m_fileChangeTracker.TryProcessChanges(timeLimit);

            UnsubscribeObservers();

            var journalStatistics = new JournalProcessingStatistics(scanningJournalResult);
            journalStatistics.Log(m_loggingContext, logMessage, logStats);

            return scanningJournalResult;
        }

        private void InitObservers()
        {
            // Only file change tracker's observers need to be initialized.
            foreach (var unsubscriber in m_fileChangeTrackerUnsubscribers)
            {
                unsubscriber.Observer.OnInit();
            }
        }

        private void UnsubscribeObservers()
        {
            foreach (var unsubscriber in m_fileChangeTrackerUnsubscribers)
            {
                unsubscriber.Dispose();
            }

            foreach (var unsubscriber in m_inputChangeListUnsubscribers)
            {
                unsubscriber.Dispose();
            }
        }
    }
}
