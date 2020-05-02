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
using Xunit;
using Xunit.Abstractions;

namespace BuildXL.CloudTest.Gvfs
{
    public class JournalHelper : IDisposable, IObserver<ChangedPathInfo>
    {
        public string WorkingFolder {get;}

        public string TestFolder {get;}

        public ITestOutputHelper TestOutput { get; }

        private FileChangeTrackingSet m_changeTracker;

        private IChangeJournalAccessor m_journal;

        private List<ChangedPathInfo> m_changes = new List<ChangedPathInfo>();

        public JournalHelper(ITestOutputHelper testOutput)
        {
            TestOutput = testOutput;
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

        public FileChangeTrackingSet.ProbeResult TrackPath(string path)
        {
            if (m_changeTracker == null)
            {
                StartTracking();
            }

            var result = m_changeTracker.TryProbeAndTrackPath(path);
            XAssert.PossiblySucceeded(result);
            return result.Result;
        }

        public void TrackPaths(params string[] paths)
        {
            foreach (var path in paths)
            {
                TrackPath(path);
            }
        }

        public virtual void SnapCheckPoint(int? nrOfExpectedOperations = null)
        {
            XAssert.IsNotNull(m_changeTracker);
            
            m_changes.Clear();
            using (m_changeTracker.Subscribe(this))
            {
                var result = m_changeTracker.TryProcessChanges(m_journal, null);
                XAssert.IsTrue(result.Succeeded);
            }

            TestOutput.WriteLine(GetChangeReport("Snapped changes"));

            if (nrOfExpectedOperations.HasValue)
            {
                XAssert.AreEqual(nrOfExpectedOperations.Value, m_changes.Count);
            }
        }

        public void AssertCreateFile(string filePath)
        {
            AssertExactChanges(filePath, PathChanges.NewlyPresentAsFile);
        }

        public void AssertChangeFile(string filePath)
        {
            AssertExactChanges(filePath, PathChanges.DataOrMetadataChanged);
        }

        public void AssertDeleteFile(string filePath)
        {
            AssertExactChanges(filePath, PathChanges.Removed);
        }

        public void AssertDeleteOrChangeFile(string filePath, bool retrackFile = true)
        {
            var changesForPath = AssertAnyChange(filePath, PathChanges.Removed | PathChanges.DataOrMetadataChanged);
            if (retrackFile && changesForPath.HasFlag(PathChanges.Removed))
            {
                TrackPath(filePath);
            }
        }

        public PathChanges ChangesForPath(string filePath) => m_changes
            .Where(c => c.Path == filePath)
            .Aggregate(PathChanges.None, (acc, elem) => acc | elem.PathChanges);

        public void AssertNoChange(string filePath)
        {
            AssertExactChanges(filePath, PathChanges.None);
        }

        public PathChanges AssertAnyChange(string filePath, PathChanges changes)
        {
            var changesForPath = ChangesForPath(filePath);
            if ((changesForPath & changes) == 0)
            {
                XAssert.Fail(GetChangeReport($"Expected to find at least ONE of {changes} changes for '{filePath}'; instead, found {changesForPath}"));
            }
            return changesForPath;
        }

        public void AssertAllChanges(string filePath, PathChanges changes)
        {
            var changesForPath = ChangesForPath(filePath);
            if (changesForPath.HasFlag(changes))
            {
                XAssert.Fail(GetChangeReport($"Expected to find ALL of {changes} changes for '{filePath}'; instead, found {changesForPath}"));
            }
        }

        public void AssertExactChanges(string filePath, PathChanges changes)
        {
            XAssert.AreEqual(changes, ChangesForPath(filePath), "\nChanges for file '{0}' disagree", filePath);
        }

        public string GetChangeReport(string header)
        {
            var builder = new StringBuilder();
            builder.AppendLine(header);
            builder.AppendLine("Changes found:");
            foreach (var change in m_changes)
            {
                builder.AppendLine($" - {change.Path} -- {change.PathChanges}");
            }
            return builder.ToString();
        }

        public virtual void Dispose()
        {
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