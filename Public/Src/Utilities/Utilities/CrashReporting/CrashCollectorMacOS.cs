// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.Linq;
using CrashReportStateFileData = System.ValueTuple<string, System.DateTime>;

namespace BuildXL.Utilities.CrashReporting
{
    /// <summary>
    /// Types of macOS crash reports
    /// </summary>
    public enum CrashType
    {
        /// <nodoc />
        SandboxExec,

        /// <nodoc />
        [Description("bxl")]
        BuildXL,

        /// <nodoc />
        Kernel
    }

    internal static class CrashTypeExtensions
    {
        internal static string GetDescription(this Enum someEnum)
        {
            var type = someEnum.GetType();
            var members = type.GetMember(someEnum.ToString());

            if (members != null && members.Length > 0)
            {
                var attributes = members[0].GetCustomAttributes(typeof(DescriptionAttribute), false);
                if (attributes != null && attributes.Count() > 0)
                {
                    return ((DescriptionAttribute)attributes.ElementAt(0)).Description;
                }
            }

            return someEnum.ToString();
        }
    }

    /// <summary>
    /// A class encapsulating all relevant info about a macOS crash report
    /// </summary>
    public struct CrashReport
    {
        /// <nodoc />
        public string FileName;

        /// <nodoc />
        public CrashType Type;

        /// <nodoc />
        public DateTime CrashDate;

        /// <nodoc />
        public string Content;
    }

    /// <summary>
    /// Facilities to search, process and upload macOS crash reports from the build host to our remote telemetry stream
    /// </summary>
    public class CrashCollectorMacOS
    {
        private readonly string m_systemCrashReportsFolder;
        private readonly string m_userCrashReportsFolder;

        private const int TimeFormatLength = 6;
        private const char TimeFormatSplitString = '-';

        private delegate List<CrashReport> Fetch(CrashType type, DateTime? from = null, DateTime? to = null, string filter = null);
        private delegate void Cleanup(CrashType type, DateTime from, DateTime to);

        private struct CrashContext
        {
            /// <nodoc />
            public CrashType Type;

            /// <nodoc />
            public Fetch FetchFunc;

            /// <nodoc />
            public Cleanup CleanupFunc;

            /// <nodoc />
            public string CrashReportFolderPath;

            /// <nodoc />
            public string CrashReportExntension;

            /// <nodoc />
            public string CrashReportFilter;
        }


        /// <summary>
        /// Used to specify all the crash reports to look up, extend this if you add a new type to CrashType
        /// First string is the crash report folder, second one the crash report file extension
        /// </summary>
        private readonly IReadOnlyList<CrashContext> m_crashReportTypeContext;
        private readonly CrashType[] m_collectableCrashTypes;

        internal const string DateTimeFormatter = "yyyy-MM-dd-HHmmss";
        internal const string SystemCrashReportFilter = "com.microsoft.";
        internal const string ScrapedCrashReportFileNameSuffix = "_Scraped";

        internal const string CrashReportStateFile = "CrashReportState";

        /// <summary>
        /// The signature of the delegate repsonsible for uploading crash reports for a specific session id
        /// </summary>
        /// <param name="reports">A list of crash reports (system + user)</param>
        /// <param name="sessionId">A session id correlating the crash reports</param>
        public delegate bool Upload(IReadOnlyList<CrashReport> reports, string sessionId);

        /// <summary>
        /// Initializes a crash collector with the default crash report paths
        /// </summary>
        public CrashCollectorMacOS(CrashType[] collectableCrashTypes = null)
            : this("/Library/Logs/DiagnosticReports", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Logs/DiagnosticReports"), collectableCrashTypes)
        {
        }

        /// <summary>
        /// Constructor for testing purposes only
        /// </summary>
        /// <param name="systemCrashReportsFolder">The path to look for system / kernel crash reports</param>
        /// <param name="userCrashReportsFolder">The path to look for user crash reports</param>
        /// <param name="collectableCrashTypes">A list of crash report types to collect</param>
        internal CrashCollectorMacOS(string systemCrashReportsFolder, string userCrashReportsFolder, CrashType[] collectableCrashTypes = null)
        {
            m_systemCrashReportsFolder = systemCrashReportsFolder;
            m_userCrashReportsFolder = userCrashReportsFolder;
            m_collectableCrashTypes = collectableCrashTypes;

            m_crashReportTypeContext = new List<CrashContext>()
            {
                new CrashContext() {
                    Type = CrashType.SandboxExec,
                    FetchFunc = GetCrashReports,
                    CleanupFunc = RenameCrashReportsWithinRange,
                    CrashReportFolderPath = m_userCrashReportsFolder,
                    CrashReportExntension = "crash",
                    CrashReportFilter = null
                },
                new CrashContext() {
                    Type = CrashType.Kernel,
                    FetchFunc = GetCrashReports,
                    CleanupFunc = RenameCrashReportsWithinRange,
                    CrashReportFolderPath = m_systemCrashReportsFolder,
                    CrashReportExntension = "panic",
                    CrashReportFilter = SystemCrashReportFilter
                },
                new CrashContext() {
                    Type = CrashType.BuildXL,
                    FetchFunc = GetCrashReports,
                    CleanupFunc = RenameCrashReportsWithinRange,
                    CrashReportFolderPath = m_userCrashReportsFolder,
                    CrashReportExntension = "crash",
                    CrashReportFilter = null
                },

            }.FindAll(entry => m_collectableCrashTypes == null || m_collectableCrashTypes.Contains(entry.Type));
        }

        /// <summary>
        /// Collects all crash reports possibly created by the last build engine invocation and uploads them to the remote telemetry stream.
        /// If the operation is successful, the crash reports state file is updated and the uploaded crash reports are deleted.
        /// </summary>
        /// <param name="sessionId">The current build engine session ID</param>
        /// <param name="stateFileDirectory">The directory specifying where the crash reports state file can be found</param>
        /// <param name="stateFilePath">The full path of the state file passed out for logging</param>
        /// <param name="upload">A function taking all crash reports belong to a session id and uploading them to remote telemetry</param>
        /// <param name="timestamp">An externally provided timestamp for crash report bucket creation, only used for testing</param>
        public void UploadCrashReportsFromLastSession(string sessionId, string stateFileDirectory, out string stateFilePath, Upload upload, DateTime? timestamp = null)
        {
            stateFilePath = Path.Combine(stateFileDirectory, CrashReportStateFile);
            if (!timestamp.HasValue)
            {
                timestamp = DateTime.Now;
            }

            CreateCrashReportStateFileEntry(sessionId, timestamp.Value, stateFilePath);
            UploadCrashReports(stateFilePath, upload);
        }

        private void UploadCrashReports(string stateFilePath, Upload upload)
        {
            var stateData = GetStateData(stateFilePath);
            var sortedStateData = stateData.OrderBy(report => report.Item2).ToArray();

            var localStateData = new HashSet<CrashReportStateFileData>();

            for (int i = 0; i < sortedStateData.Count(); i++)
            {
                var from = i == 0 ? sortedStateData[i].Item2.AddYears(-1) : sortedStateData[i].Item2;
                var to = i == (sortedStateData.Count() -1) ? sortedStateData[i].Item2.AddYears(1) : sortedStateData[i+1].Item2;

                foreach (var context in m_crashReportTypeContext)
                {
                    var reports = context.FetchFunc(context.Type, from, to, context.CrashReportFilter);
                    if (reports.Count > 0)
                    {
                        var success = upload(reports, sortedStateData[i].Item1);
                        if (!success)
                        {
                            // If uploading fails, keep the state around
                            localStateData.Add(sortedStateData[i]);
                        }
                        else
                        {
                            context.CleanupFunc(context.Type, from, to);
                        }
                    }
                    else
                    {
                        // Keep the last found state around, it is going the be the bucket for the next crash collection
                        if (i == sortedStateData.Count() - 1)
                        {
                            localStateData.Add(sortedStateData[i]);
                        }
                    }
                }
            }

            ResetStateFileAfterUploadAttempt(stateFilePath, localStateData.ToList());
        }

        private void ResetStateFileAfterUploadAttempt(string stateFilePath, List<CrashReportStateFileData> states)
        {
            if (File.Exists(stateFilePath))
            {
                File.Delete(stateFilePath);
            }

            foreach (var state in states)
            {
                CreateCrashReportStateFileEntry(state.Item1, state.Item2, stateFilePath);
            }
        }

        /// <summary>
        /// This method gets the crash report state data persisted on disk. The state data file is a list of entries, each of the following format:
        ///     'SessionId|SessionStartDate'
        /// Everytime the engine runs it reads this file and uses the data to assign crash reports found on disk to previous sessions. This happens through
        /// searching for crash reports that are in between the session start dates e.g. if the state file has the following entries,
        ///     'session1|2018-11-16-202233'
        ///     'session2|2018-11-17-100155'
        /// the logic in this class searches and assigns crash reports whos creation date is smaller or equal to the session2 start date and bigger pr equal to
        /// the session1 start date.
        /// </summary>
        /// <param name="stateFilePath">The path to the crash report state file</param>
        /// <returns></returns>
        internal List<CrashReportStateFileData> GetStateData(string stateFilePath)
        {
            return File.ReadAllLines(stateFilePath)
                .Select(line => line.Split('|'))
                .Where(parts => parts.Length > 1)
                .Select(parts => (parts[0], DateTime.ParseExact(parts[1], DateTimeFormatter, CultureInfo.InvariantCulture)))
                .ToList();
        }

        internal void CreateCrashReportStateFileEntry(string sessionId, DateTime timestamp, string stateFilePath)
        {
            // Session Id | Session start time stamp
            var stateData = $"{sessionId}|{timestamp.ToString(DateTimeFormatter, CultureInfo.InvariantCulture)}\n";
            File.AppendAllText(stateFilePath, stateData);
        }

        /// <summary>
        /// Gets the date time from a crash report file name
        /// </summary>
        /// <param name="fileName">The crash report file name in macOS standard fashion e.g. BuildXL_2018-11-16-102125-1_SomeHostName.crash</param>
        /// <returns></returns>
        internal DateTime ParseDateTimeFromFileName(string fileName)
        {
            var parts = fileName.Split('_');

            // Get the date
            var date = parts[1];
            var time = date.Split(TimeFormatSplitString);

            var isLastPartTime = time.Last().Length == TimeFormatLength;
            var patchedTime = string.Join(TimeFormatSplitString.ToString(), isLastPartTime ? time : time.Take(time.Length - 1));

            return DateTime.ParseExact(patchedTime, DateTimeFormatter, CultureInfo.InvariantCulture);
        }

        private DateTime NormalizeDateTime(DateTime time)
        {
            // Normalize the timestamps so we don't have millisecond deviations
            return new DateTime(time.Year, time.Month, time.Day, time.Hour, time.Minute, time.Second);
        }

        internal List<CrashReport> GetCrashReports(CrashType type, DateTime? from = null, DateTime? to = null, string filter = null)
        {
            if (from.HasValue)
            {
                from = NormalizeDateTime(from.Value);
            }

            if (to.HasValue)
            {
                to = NormalizeDateTime(to.Value);
            }

            var context = m_crashReportTypeContext.FirstOrDefault(f => f.Type == type);
            if (context.CrashReportFolderPath != null && context.CrashReportExntension != null)
            {
                var crashReportsFolder = context.CrashReportFolderPath;
                var crashReportExtension = context.CrashReportExntension;

                var filePattern = $"{type.GetDescription()}_*.{crashReportExtension}";
                var crashReports = Directory.EnumerateFiles(crashReportsFolder, filePattern, SearchOption.AllDirectories);

                var reports = crashReports.Where(r => !r.Contains(ScrapedCrashReportFileNameSuffix)).Select(r => new CrashReport()
                {
                    FileName = r,
                    Type = type,
                    CrashDate = ParseDateTimeFromFileName(Path.GetFileName(r)),
                    Content = File.ReadAllText(r)
                });

                reports = (from.HasValue || to.HasValue) ? reports.Where(report =>
                {
                    if (from.HasValue && to.HasValue)
                    {
                        return report.CrashDate >= from.Value && report.CrashDate <= to.Value;
                    }
                    else if (!from.HasValue && to.HasValue)
                    {
                        return report.CrashDate <= to.Value;
                    }
                    else
                    {
                        return report.CrashDate >= from.Value;
                    }
                }) : reports;

                return string.IsNullOrEmpty(filter) ? reports.ToList() : reports.Where(report => report.Content.Contains(filter)).ToList();
            }

            return new List<CrashReport>();
        }

        /// <summary>
        /// Renames all crash reports of a specific type that are within a specified time range, and adds a suffix to their file name to not be re-uploaded later
        /// </summary>
        /// <param name="type">The crash report type <see cref="CrashType"/></param>
        /// <param name="from">The time stamp used to mark the beginning of the range</param>
        /// <param name="to">Yhe time stamp used to mark the end of the range</param>
        internal void RenameCrashReportsWithinRange(CrashType type, DateTime from, DateTime to)
        {
            Contract.Requires(from < to);

            from = NormalizeDateTime(from);
            to = NormalizeDateTime(to);

            var context = m_crashReportTypeContext.FirstOrDefault(f => f.Type == type);
            if (context.FetchFunc != null)
            {
                var reports = context.FetchFunc(context.Type, from, to, context.CrashReportFilter);
                reports.Where(r => r.CrashDate >= from && r.CrashDate <= to).ToList().ForEach(report =>
                {
                    var path = Path.GetDirectoryName(report.FileName);
                    var fileName = Path.GetFileNameWithoutExtension(report.FileName);
                    var extension = Path.GetExtension(report.FileName);

                    File.Move(report.FileName, Path.Combine(path, fileName + ScrapedCrashReportFileNameSuffix + extension));
                });
            }
        }
    }
}