// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using BuildXL.Engine.Tracing;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Engine
{
    /// <summary>
    /// Class that helps with tracking BuildXL invocations
    /// </summary>
    public sealed class Invocations
    {
        private const int CurrentLineFormatVersion = 0;

        private const string GlobalMutexName = @"Global\BuildXL.BuildInvocationFile";

        private const string BuildsTsvFileName = "builds.tsv";

        private const string BuildsTsvAppName = "BuildXL";

        private int m_maxNumberOfBuildsTracked = 256;

        private string m_buildsTsvFilePathForTesting;

        /// <summary>
        ///  Represetns a single invocation
        /// </summary>
        public readonly struct Invocation
        {
            /// <summary>
            /// The version number of the format of invocation line in the tsv file.
            /// </summary>
            public int LineVersion { get; }

            /// <summary>
            /// The sessionid of the build. This should match the telemetry session
            /// </summary>
            public string SessionId { get; }

            /// <summary>
            /// The start time of the build (in UTC)
            /// </summary>
            public DateTime BuildStartTimeUtc { get; }

            /// <summary>
            /// The primary config file of the build
            /// </summary>
            public string PrimaryConfigFile { get; }

            /// <summary>
            /// The folder where the logs are written
            /// </summary>
            /// <remarks>
            /// Note that this is written when the build starts, so there is no guarantee that the logs are there if the build crashed before writing the log, graph or xlg.
            /// Or that after that many builds the logs have not been cycled through.
            /// It is the responsibility of consumers of this data to decide to show or not.
            /// </remarks>
            public string LogsFolder { get; }

            /// <summary>
            /// The build version of BuildXL used.
            /// </summary>
            /// <remarks>
            /// This is only present on builds produced by the official buid lab, developer builds will have "[DevBuild]"
            /// </remarks>
            public string EngineVersion { get; }

            /// <summary>
            /// The folder from which bxl.exe was run.
            /// </summary>
            /// <remarks>
            /// The intent is to find matching dll's in this location for loading the graph and xlg files.
            /// </remarks>
            public string EngineBinFolder { get; }

            /// <summary>
            /// The commitId of the BuildXL version used.
            /// </summary>
            /// <remarks>
            /// This is only present on builds produced by the official buid lab, developer builds will have "[DevBuild]"
            /// </remarks>
            public string EngineCommitId { get; }

            /// <nodoc />
            public Invocation(string sessionId, DateTime buildStartTimeUtc, string primaryConfigFile, string logsFolder, string engineVersion, string engineBinFolder, string engineCommitId)
                : this(CurrentLineFormatVersion, sessionId, buildStartTimeUtc, primaryConfigFile, logsFolder, engineVersion, engineBinFolder, engineCommitId)
            {
            }

            private Invocation(int lineVersion, string sessionId, DateTime buildStartTimeUtc, string primaryConfigFile, string logsFolder, string engineVersion, string engineBinFolder, string engineCommitId)
            {
                LineVersion = lineVersion;
                SessionId = sessionId;
                BuildStartTimeUtc = buildStartTimeUtc;
                PrimaryConfigFile = primaryConfigFile;
                LogsFolder = logsFolder;
                EngineVersion = engineVersion ?? "[DevBuild]";
                EngineBinFolder = engineBinFolder;
                EngineCommitId = engineCommitId ?? "[DevBuild]";
            }

            /// <summary>
            /// Parses an invocation from a TSV Line
            /// </summary>
            /// <remarks>
            /// This code is version aware. For now we only have one version and we'll treat all future line format versions
            /// as unsupported (indisinguisable from wrong file formats). This will make new lines not show up in old
            /// consumers. Updated version should support reading old ones.
            /// </remarks>
            public static bool TryParseTsvLine(string line, out Invocation invocation, out string errorDetails)
            {
                invocation = default;
                if (string.IsNullOrEmpty(line))
                {
                    errorDetails = "empty line";
                    return false;
                }

                var parts = line.Split('\t');

                // Check for line format version
                if (parts.Length < 1 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var lineVersion))
                {
                    errorDetails = "Error extracting line version";
                    return false;
                }

                switch (lineVersion)
                {
                    case 0:
                        if (parts.Length != 8)
                        {
                            errorDetails = "Unexpected number of parts for v1: " + parts.Length;
                            return false;
                        }

                        var sessionId = parts[1];
                        if (string.IsNullOrEmpty(sessionId))
                        {
                            errorDetails = "Failed to parse sessionId";
                            return false;
                        }

                        if (!DateTime.TryParseExact(parts[2], "O", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var buildStartTimeUtc))
                        {
                            errorDetails = "Failed to parse activityId";
                            return false;
                        }

                        var primaryConfigFile = parts[3];
                        if (string.IsNullOrEmpty(primaryConfigFile))
                        {
                            errorDetails = "Failed to parse primaryConfigFile";
                            return false;
                        }

                        var logsFolder = parts[4];
                        if (string.IsNullOrEmpty(logsFolder))
                        {
                            errorDetails = "Failed to parse logsFolder";
                            return false;
                        }

                        var engineVersion = parts[5];
                        if (string.IsNullOrEmpty(engineVersion))
                        {
                            errorDetails = "Failed to parse engineVersion";
                            return false;
                        }

                        var engineBinFolder = parts[6];
                        if (string.IsNullOrEmpty(engineBinFolder))
                        {
                            errorDetails = "Failed to parse engineBinFolder";
                            return false;
                        }

                        var engineCommitId = parts[7];
                        if (string.IsNullOrEmpty(engineCommitId))
                        {
                            errorDetails = "Failed to parse engineCommitId";
                            return false;
                        }

                        invocation = new Invocation(
                            lineVersion,
                            sessionId,
                            buildStartTimeUtc.ToUniversalTime(),
                            primaryConfigFile,
                            logsFolder,
                            engineVersion,
                            engineBinFolder,
                            engineCommitId
                        );
                        errorDetails = null;
                        return true;
                    default:
                        // Unsupported line format version
                        errorDetails = "Unsupported version:" + lineVersion;
                        return false;
                }
            }

            /// <summary>
            /// Prints an invocation to tsv line format
            /// </summary>
            /// <remarks>
            /// TSV file format was chosen over csv to avoid having to encode comma's in file paths and BuildXL versions.
            /// </remarks>
            public string ToTsvLine()
            {
                return I($"{LineVersion:D}\t{SessionId}\t{BuildStartTimeUtc:O}\t{PrimaryConfigFile}\t{LogsFolder}\t{EngineVersion}\t{EngineBinFolder}\t{EngineCommitId}");
            }
        }

        /// <nodoc />
        public static Invocations CreateForTesting(int maxNumberOfBuildsTracked, string buildsTsvFilePathForTesting)
        {
            Contract.Requires(maxNumberOfBuildsTracked > 0);
            Contract.Requires(!string.IsNullOrEmpty(buildsTsvFilePathForTesting));
            return new Invocations()
                   {
                       m_maxNumberOfBuildsTracked = maxNumberOfBuildsTracked,
                       m_buildsTsvFilePathForTesting = buildsTsvFilePathForTesting,
                   };
        }

        /// <summary>
        /// Records an invocation to the build invocation tsv file in the user folder.
        /// </summary>
        /// <remarks>
        /// There are a max number of entries. It will drop the first line if there are more than expected.
        /// This will also leave all the other lines unmodified, so if the format is ever revved they are guaranteed to be maintained.
        /// </remarks>
        public void RecordInvocation(LoggingContext loggingContext, in Invocation invocation)
        {
            var tsvLine = invocation.ToTsvLine();

            GlobalSyncronizedFileAccess(
                (filePath) =>
                {
                    if (!File.Exists(filePath))
                    {
                        File.WriteAllLines(filePath, new [] {tsvLine});
                    }
                    else
                    {
                        var lines = File.ReadAllLines(filePath);

                        var newLineCount = Math.Min(m_maxNumberOfBuildsTracked, lines.Length + 1);
                        var newLines = new string[newLineCount];

                        if (lines.Length < newLineCount)
                        {
                            // Copy over the entire old lines
                            Array.Copy(lines, newLines, lines.Length);
                        }
                        else
                        {
                            // Copy over the old lines shifting by one
                            Array.Copy(lines, lines.Length - newLineCount + 1, newLines, 0, newLineCount - 1);
                        }

                        // Set the last line
                        newLines[newLineCount - 1] = tsvLine;

                        // Write back out to the file
                        File.WriteAllLines(filePath, newLines);
                    }

                    Logger.Log.WrittenBuildInvocationToUserFolder(loggingContext, tsvLine, filePath);
                },
                (filePath, errorMessage) =>
                {
                    Logger.Log.FailedToWriteBuildInvocationToUserFolder(loggingContext, tsvLine, filePath, errorMessage);
                }
            );
        }

        /// <summary>
        /// Gets all the invocations from the tracefile
        /// </summary>
        public IReadOnlyList<Invocation> GetInvocations(LoggingContext loggingContext)
        {
            string[] lines = null;

            GlobalSyncronizedFileAccess(
                (filePath) =>
                {
                    if (!File.Exists(filePath))
                    {
                        lines = new string[0];
                    }
                    else
                    {
                        lines = File.ReadAllLines(filePath);
                    }
                },
                (filePath, errorMessage) =>
                {
                    Logger.Log.FailedToReadBuildInvocationToUserFolder(loggingContext, filePath, errorMessage);
                }
            );

            if (lines == null)
            {
                return new Invocation[0];
            }

            List<Invocation> invocations = new List<Invocation>(lines.Length);
            foreach (var line in lines)
            {
                // We skip
                if (Invocation.TryParseTsvLine(line, out var invocation, out _))
                {
                    invocations.Add(invocation);
                }
            }

            return invocations;
        }

        /// <summary>
        /// Gets the last invocation
        /// </summary>
        /// <remarks>
        /// In case of error or no builds yet this return null
        /// </remarks>
        public Invocation? GetLastInvocation(LoggingContext loggingContext)
        {
            var invocations = GetInvocations(loggingContext);
            if (invocations.Count == 0)
            {
                return null;
            } 
            
            return invocations.Last();
        }

        /// <summary>
        /// Gets the path to where the tsv file can be found.
        /// </summary>
        public string GetBuildTsvFilePath()
        {
            if (m_buildsTsvFilePathForTesting != null)
            {
                return m_buildsTsvFilePathForTesting;
            }
            else
            {
                var settingsFolder = FileUtilities.GetUserSettingsFolder(BuildsTsvAppName);
                return Path.Combine(settingsFolder, BuildsTsvFileName);
            }
        }

        private void GlobalSyncronizedFileAccess(Action<string> performFileOperations, Action<string, string> handleFailure)
        {
            string tsvFilePath = GetBuildTsvFilePath();

            using (var mutex = new Mutex(initiallyOwned: false, name: GlobalMutexName))
            {
                var hasMutexHandle = false;
                try
                {
                    try
                    {
                        hasMutexHandle = mutex.WaitOne(5000, false);
                        if (!hasMutexHandle)
                        {
                            handleFailure(tsvFilePath, "Timeout waiting for mutex");
                            return;
                        }
                    }
                    catch (AbandonedMutexException)
                    {
                        hasMutexHandle = true;
                    }

                    try
                    {
                        ExceptionUtilities.HandleRecoverableIOException(
                            () => performFileOperations(tsvFilePath),
                            ex => handleFailure(tsvFilePath, ex.Message)
                        );
                    }
                    catch (BuildXLException e)
                    {
                        handleFailure(tsvFilePath, e.LogEventMessage);
                        return;
                    }
                }
                finally
                {
                    if (hasMutexHandle)
                    {
                        mutex.ReleaseMutex();
                    }
                }
            }
        }
    }
}
