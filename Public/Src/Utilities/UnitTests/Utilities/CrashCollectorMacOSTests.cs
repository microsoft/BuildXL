// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Utilities;
using BuildXL.Utilities.CrashReporting;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using static BuildXL.Utilities.CrashReporting.CrashCollectorMacOS;

namespace Test.BuildXL.Utilities
{
    public sealed class CrashCollectorMacOSTests : TemporaryStorageTestBase
    {
        private CrashCollectorMacOS m_crashCollector;

        private string m_systemCrashReportFolder;
        private string m_userCrashReportFolder;

        public CrashCollectorMacOSTests(ITestOutputHelper output)
            : base(output)
        {
            m_systemCrashReportFolder = CreateCrashReportsFolder(CrashType.Kernel);
            m_userCrashReportFolder = CreateCrashReportsFolder(CrashType.BuildXL);

            m_crashCollector = new CrashCollectorMacOS(m_systemCrashReportFolder, m_userCrashReportFolder);
        }

        [Fact]
        public void TestCrashCollectorMacOSConstruction()
        {
            var date = DateTime.Now;
            var crashCollector = new CrashCollectorMacOS(m_systemCrashReportFolder, m_userCrashReportFolder, new[] { CrashType.Kernel, CrashType.SandboxExec });

            CreateCrashReports(m_userCrashReportFolder, CrashType.SandboxExec, 4, date.AddHours(1), "Some sandbox exec crash report");
            CreateCrashReports(m_systemCrashReportFolder, CrashType.Kernel, 8, date.AddHours(1), SystemCrashReportFilter);
            CreateCrashReports(m_userCrashReportFolder, CrashType.BuildXL, 2, date.AddHours(1), "Some user crash report");

            var systemReports = crashCollector.GetCrashReports(CrashType.Kernel);
            XAssert.IsTrue(systemReports.Count == 8);

            var userReports = crashCollector.GetCrashReports(CrashType.BuildXL);
            XAssert.IsTrue(userReports.Count == 0);

            var sandboxReports = crashCollector.GetCrashReports(CrashType.SandboxExec);
            XAssert.IsTrue(sandboxReports.Count == 4);
        }

        [Fact]
        public void TestFileNameDateTimeParsing()
        {
            var fileName = "BuildXL_2018-11-16-102125-1_SomeHostName.panic";
            var date = m_crashCollector.ParseDateTimeFromFileName(fileName);
            XAssert.AreEqual(date, new DateTime(2018, 11, 16, 10, 21, 25));

            fileName = "BuildXL_2018-11-16-102125-2_SomeHostName.panic";
            date = m_crashCollector.ParseDateTimeFromFileName(fileName);
            XAssert.AreEqual(date, new DateTime(2018, 11, 16, 10, 21, 25));

            fileName = "BuildXL_2017-01-01-120023_SomeHostName.crash";
            date = m_crashCollector.ParseDateTimeFromFileName(fileName);
            XAssert.AreEqual(date, new DateTime(2017, 1, 1, 12, 0, 23));
        }

        [Fact]
        public void TestGetUserCrashReportsSinceDateTime()
        {
            var date = DateTime.Now;
            // Create some crash reports that are two days old
            CreateCrashReports(m_userCrashReportFolder, CrashType.BuildXL, 10, date.AddDays(-2), "Some data");

            // Get all crash reports
            var reports = m_crashCollector.GetCrashReports(CrashType.BuildXL);
            XAssert.IsTrue(reports.Count == 10);
            reports = m_crashCollector.GetCrashReports(CrashType.BuildXL, from: date.AddDays(-3));
            XAssert.IsTrue(reports.Count == 10);
            reports = m_crashCollector.GetCrashReports(CrashType.BuildXL, to: date);
            XAssert.IsTrue(reports.Count == 10);
            reports = m_crashCollector.GetCrashReports(CrashType.BuildXL, from: date.AddDays(-3), to: date);
            XAssert.IsTrue(reports.Count == 10);

            // Create some crash reports that are about a day old
            var queryDate = date.AddDays(-1);
            CreateCrashReports(m_userCrashReportFolder, CrashType.BuildXL, 5, queryDate, "Some more data");

            // First query with the current time as a time stamp, should not yield any results
            reports = m_crashCollector.GetCrashReports(CrashType.BuildXL, from: date);
            XAssert.IsTrue(reports.Count == 0);

            CreateCrashReports(m_userCrashReportFolder, CrashType.BuildXL, 1, date, "Even more data");

            // This should only yield the all the crash reports newer or as old as the query date
            reports = m_crashCollector.GetCrashReports(CrashType.BuildXL, from: queryDate, to: date);
            XAssert.IsTrue(reports.Count == 6);
        }

        [Fact]
        public void TestGetSystemCrashReportsSinceDateTime()
        {
            var date = DateTime.Now.AddHours(-3);

            // Create some system crash reports
            CreateCrashReports(m_systemCrashReportFolder, CrashType.Kernel, 5, date.AddHours(1), SystemCrashReportFilter);
            CreateCrashReports(m_systemCrashReportFolder, CrashType.Kernel, 5, date.AddHours(2), "Some other system error");
            CreateCrashReports(m_systemCrashReportFolder, CrashType.Kernel, 5, date.AddHours(-5), SystemCrashReportFilter);

            var reports = m_crashCollector.GetCrashReports(CrashType.Kernel, from: date);
            XAssert.IsNotNull(reports);
            XAssert.IsTrue(reports.Count == 10);
        }

        [Fact]
        public void TestGetSandboxExecCrashReportsSinceDateTime()
        {
            var date = DateTime.Now.AddHours(-3);

            // Create some sandbox crash reports
            CreateCrashReports(m_systemCrashReportFolder, CrashType.Kernel, 5, date.AddHours(1), SystemCrashReportFilter);
            CreateCrashReports(m_userCrashReportFolder, CrashType.SandboxExec, 5, date.AddHours(1), "Some sandbox crash report");
            CreateCrashReports(m_userCrashReportFolder, CrashType.BuildXL, 10, date.AddDays(-2), "Some data");
            CreateCrashReports(m_userCrashReportFolder, CrashType.SandboxExec, 5, date.AddHours(2), "Some other sandbox error");

            var reports = m_crashCollector.GetCrashReports(CrashType.SandboxExec, from: date);
            XAssert.IsNotNull(reports);
            XAssert.IsTrue(reports.Count == 10);
        }

        [Fact]
        public void TestRenameUserCrashReportsSinceDateTime()
        {
            var date = DateTime.Now;
            var recentData = "Some recent data";
            // Create some crash reports that are two days old
            CreateCrashReports(m_userCrashReportFolder, CrashType.BuildXL, 5, date.AddHours(-4), "Some very old data");
            CreateCrashReports(m_userCrashReportFolder, CrashType.BuildXL, 5, date.AddHours(-2), "Some old data");
            CreateCrashReports(m_userCrashReportFolder, CrashType.BuildXL, 5, date, recentData);
            m_crashCollector.RenameCrashReportsWithinRange(CrashType.BuildXL, from: date.AddHours(-4), to: date.AddHours(-1));
            var reports = m_crashCollector.GetCrashReports(CrashType.BuildXL);
            XAssert.IsNotNull(reports);
            XAssert.IsTrue(reports.Count == 5);
            XAssert.IsTrue(reports.All(r => r.Content.Contains(recentData)));
        }

        [Fact]
        public void TestUploadCrashReports()
        {
            var date = DateTime.Now;
            var stateFile = Path.Combine(m_userCrashReportFolder, CrashReportStateFile);

            // Generate some random state matching build invocations with crashes
            CreateCrashReports(m_userCrashReportFolder, CrashType.BuildXL, 5, date.AddDays(-7), "Some very old data"); // Crashes from before crash uploading ever happened
            m_crashCollector.CreateCrashReportStateFileEntry("test_session1", date.AddDays(-4), stateFile);
            CreateCrashReports(m_userCrashReportFolder, CrashType.BuildXL, 4, date.AddDays(-4).AddMinutes(1), "Some old data"); // Crashes from session 1

            m_crashCollector.CreateCrashReportStateFileEntry("test_session2", date.AddDays(-3), stateFile);
            CreateCrashReports(m_userCrashReportFolder, CrashType.BuildXL, 3, date.AddDays(-3).AddMinutes(1), "Some recent data"); // Crashes from session 2

            m_crashCollector.CreateCrashReportStateFileEntry("test_session3", date.AddDays(-2), stateFile);
            CreateCrashReports(m_userCrashReportFolder, CrashType.BuildXL, 2, date.AddDays(-2).AddMinutes(1), "Some new data"); // Crashes from session 3

            m_crashCollector.CreateCrashReportStateFileEntry("test_session4", date.AddDays(-1), stateFile);
            CreateCrashReports(m_userCrashReportFolder, CrashType.BuildXL, 1, date.AddDays(-1).AddMinutes(1), "Newest data"); // Crashes from session 4

            m_crashCollector.UploadCrashReportsFromLastSession("test_session5", m_userCrashReportFolder, out _, (IReadOnlyList<CrashReport> reports, string sessionId) =>
            {
                switch (sessionId)
                {
                    case "test_session1":
                        // Old crash data and crash data after the 'test_session1' run
                        XAssert.IsTrue(reports.Count == 9);
                        XAssert.IsTrue(reports.All(r => r.Content.Contains("old data")));
                        return true;
                    case "test_session2":
                        XAssert.IsTrue(reports.Count == 3);
                        XAssert.IsTrue(reports.All(r => r.Content.Contains("recent data")));
                        return false; // Fail upload process
                    case "test_session3":
                        XAssert.IsTrue(reports.Count == 2);
                        XAssert.IsTrue(reports.All(r => r.Content.Contains("new data")));
                        return true;
                    case "test_session4":
                        XAssert.IsTrue(reports.Count == 1);
                        XAssert.IsTrue(reports.All(r => r.Content.Contains("Newest data")));
                        return false; // Fail upload process
                    default:
                        XAssert.Fail("Only test session 1-4 should be present");
                        return true;
                }
            }, date.AddMinutes(1));

            var failedState = m_crashCollector.GetStateData(stateFile);
            XAssert.IsTrue(failedState.Count == 3);

            CreateCrashReports(m_userCrashReportFolder, CrashType.BuildXL, 1, date.AddMinutes(1).AddSeconds(30), "Future data"); // Crashes from session 5

            var remainingReports = m_crashCollector.GetCrashReports(CrashType.BuildXL);
            XAssert.IsTrue(remainingReports.Count == 5);

            m_crashCollector.UploadCrashReportsFromLastSession("test_session6", m_userCrashReportFolder, out _, (IReadOnlyList<CrashReport> reports, string sessionId) =>
            {
                switch (sessionId)
                {
                    case "test_session2":
                        XAssert.IsTrue(reports.Count == 3);
                        XAssert.IsTrue(reports.All(r => r.Content.Contains("recent data")));
                        return true;
                    case "test_session4":
                        XAssert.IsTrue(reports.Count == 1);
                        XAssert.IsTrue(reports.All(r => r.Content.Contains("Newest data")));
                        return false; // Fail upload process
                    case "test_session5":
                        XAssert.IsTrue(reports.Count == 1);
                        XAssert.IsTrue(reports.All(r => r.Content.Contains("Future data")));
                        return true;
                    default:
                        XAssert.Fail("Only test session 2, 4 and 5 should be present");
                        return true;
                }
            }, date.AddMinutes(3));

            remainingReports = m_crashCollector.GetCrashReports(CrashType.BuildXL);
            XAssert.IsTrue(remainingReports.Count == 1);

            m_crashCollector.UploadCrashReportsFromLastSession("test_session7", m_userCrashReportFolder, out _, (IReadOnlyList<CrashReport> reports, string sessionId) =>
            {
                switch (sessionId)
                {
                    case "test_session4":
                        XAssert.IsTrue(reports.Count == 1);
                        XAssert.IsTrue(reports.All(r => r.Content.Contains("Newest data")));
                        return true;
                    default:
                        XAssert.Fail("Only test session 4 should be present");
                        return true;
                }
            }, date.AddMinutes(5));

            // Make sure we only have test session 7 peristed, 6 got removed as we had no crash reports happen
            var state = m_crashCollector.GetStateData(stateFile);
            XAssert.IsTrue(state.Count == 1);
            XAssert.IsTrue(state[0].Item1.Equals("test_session7"));

            remainingReports = m_crashCollector.GetCrashReports(CrashType.BuildXL);
            XAssert.IsTrue(remainingReports.Count == 0);
        }

        [Fact]
        public void TestStateResetsWhenNoCrashReportsAreFound()
        {
            var stateFile = Path.Combine(m_userCrashReportFolder, CrashReportStateFile);

            m_crashCollector.UploadCrashReportsFromLastSession("test_session1", m_userCrashReportFolder, out _, (IReadOnlyList<CrashReport> reports, string sessionId) =>
            {
                return false;
            });

            m_crashCollector.UploadCrashReportsFromLastSession("test_session2", m_userCrashReportFolder, out _, (IReadOnlyList<CrashReport> reports, string sessionId) =>
            {
                return false;
            });

            // Make sure we only persist one session if we had no crash reports in previous runs
            var state = m_crashCollector.GetStateData(stateFile);
            XAssert.IsTrue(state.Count == 1);
            XAssert.IsTrue(state[0].Item1.Equals("test_session2"));
        }

        // Test if no crashes are there there is only ever one state in several runs
        private string CreateCrashReportsFolder(CrashType type)
        {
            string crashReportPath = Path.Combine(TemporaryDirectory, type.GetDescription());
            Directory.CreateDirectory(crashReportPath);

            return crashReportPath;
        }

        private void CreateCrashReports(string crashReportPath, CrashType type, int count, DateTime date, string contents)
        {
            var extension = type == CrashType.Kernel ? "panic" : "crash";

            for (int i = 0; i < count; i++)
            {
                var fileName = date.ToString(DateTimeFormatter);
                var suffix = i < 2 ? $"-{i + 1}" : "";

                // Create some test files e.g. BuildXL_2018-11-16-102125-1_SomeHostName.crash
                var reportPath = Path.Combine(crashReportPath, $"{type.GetDescription()}_{fileName}{suffix}_SomeHostName.{extension}");
                File.WriteAllText(reportPath, contents);

                date = date.AddMinutes(i);
            }
        }
    }
}