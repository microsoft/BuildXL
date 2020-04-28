// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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

        public string TestFolder {get;}

        private FileChangeTrackingSet m_changeTracker;

        private IChangeJournalAccessor m_journal;

        private List<ChangedPathInfo> m_changes = new List<ChangedPathInfo>();

        public JournalHelper()
        {
            TestFolder = Path.GetDirectoryName(Assembly.GetAssembly(typeof(JournalHelper)).Location);

            WorkingFolder = Path.Combine(Environment.GetEnvironmentVariable("TEMP"), Guid.NewGuid().ToString());
            var workingRoot = Path.GetPathRoot(WorkingFolder);
            Directory.CreateDirectory(WorkingFolder);
        }

        public void StartTracking()
        {
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

        public virtual string GetPath(string path)
        {
            return Path.Combine(WorkingFolder, path);
        }

        public void TrackPath(string path)
        {
            if (m_changeTracker == null)
            {
                StartTracking();
            }

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
            XAssert.IsNotNull(m_changeTracker);
            
            m_changes.Clear();
            using (m_changeTracker.Subscribe(this))
            {
                var result = m_changeTracker.TryProcessChanges(m_journal, null);
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

        public void AssertChangeFile(string filePath)
        {
            AssertOperation(new ChangedPathInfo(filePath, PathChanges.DataOrMetadataChanged));
        }

        public void AssertDeleteFile(string filePath)
        {
            AssertOperation(new ChangedPathInfo(filePath, PathChanges.Removed));
        }

        private void AssertOperation(ChangedPathInfo expectedChange)
        {
            if (!m_changes.Remove(expectedChange))
            {
                XAssert.Fail(GetChangeReport($"Expected to find change {PrintChange(expectedChange)}"));
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

        private string PrintChange(ChangedPathInfo change)
        {
            return $"{change.Path} -- {change.PathChanges.ToString()}";
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