// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BuildXL.Storage;
using BuildXL.Storage.ChangeJournalService;
using BuildXL.Storage.ChangeTracking;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using Test.BuildXL.TestUtilities.Xunit;

namespace BuildXL.CloudTest.Gvfs
{
    public class JournalHelper : IDisposable, IObserver<ChangedPathInfo>
    {
        public string WorkingFolder {get;}

        private readonly FileChangeTrackingSet m_changeTracker;

        private readonly IChangeJournalAccessor m_journal;

        private List<ChangedPathInfo> m_changes = new List<ChangedPathInfo>();

        public JournalHelper()
        {
            WorkingFolder = Path.Combine(Environment.GetEnvironmentVariable("TEMP"), Guid.NewGuid().ToString());
            var workingRoot = Path.GetPathRoot(WorkingFolder);
            Directory.CreateDirectory(WorkingFolder);

            var loggingContext = new LoggingContext("JournalTesting", "Dummy");

            VolumeMap volumeMap = JournalUtils.TryCreateMapOfAllLocalVolumes(loggingContext);
            XAssert.IsNotNull(volumeMap);

            var maybeJournal = JournalUtils.TryGetJournalAccessorForTest(volumeMap);
            XAssert.IsTrue(maybeJournal.Succeeded);
            m_journal = maybeJournal.Result;

            m_changeTracker = FileChangeTrackingSet.CreateForAllCapableVolumes(
                loggingContext,
                volumeMap,
                m_journal
            );
        }

        public void TrackPath(string path)
        {
            Analysis.IgnoreResult(m_changeTracker.TryProbeAndTrackPath(path));
        }

        public void TrackPaths(params string[] paths)
        {
            foreach (var path in paths)
            {
                TrackPath(path);
            }
        }

        public void SnapCheckPoint(int? nrOfExpectedOperations = null)
        {
            m_changes.Clear();
            using (m_changeTracker.Subscribe(this))
            {
                // $REview: @Iman: Why does default null not mean infinite, I constatly get timeout errors...
                var result = m_changeTracker.TryProcessChanges(m_journal, TimeSpan.FromMinutes(1));
                XAssert.IsTrue(result.Succeeded);
            }
            
            if (nrOfExpectedOperations.HasValue)
            {
                XAssert.AreEqual(nrOfExpectedOperations.Value, m_changes.Count);
            }
        }

        public void AssertCreateFile(string filePath)
        {
            AssertOperation(new ChangedPathInfo(filePath, PathChanges.NewlyPresentAsFile));
        }

        private void AssertOperation(ChangedPathInfo expectedChange)
        {
            if (!m_changes.Remove(expectedChange))
            {
                XAssert.Fail(GetChangeReport($"Expected to find change {expectedChange.Path} -- {expectedChange.PathChanges.ToString()}"));
            }
        }

        private string GetChangeReport(string header)
        {
            var builder = new StringBuilder();
            builder.AppendLine(header);
            foreach (var change in m_changes)
            {
                builder.AppendLine($" - {change.Path} -- {change.PathChanges.ToString()}");
            }
            return builder.ToString();
        }

        public void Dispose()
        {
            if (m_changes.Count > 0)
            {
                XAssert.Fail(GetChangeReport($"Encountered {m_changes.Count} unexpected changes:"));
            }
        }

        void IObserver<ChangedPathInfo>.OnCompleted()
        {
        }

        void IObserver<ChangedPathInfo>.OnError(Exception error)
        {
            XAssert.Fail("Error processing: " + error);
        }

        void IObserver<ChangedPathInfo>.OnNext(ChangedPathInfo value)
        {
            m_changes.Add(value);
        }
    }
}