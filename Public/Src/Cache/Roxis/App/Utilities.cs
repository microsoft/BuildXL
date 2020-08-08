// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Logging;

namespace BuildXL.Cache.Roxis.App
{
    internal static class Utilities
    {
        public static async Task WithLoggerAsync(Func<ILogger, Task> action, string? logFilePath)
        {
            CsvFileLog? csvFileLog = null;
            ConsoleLog? consoleLog = null;
            Logger? logger = null;

            try
            {
                if (!string.IsNullOrEmpty(logFilePath))
                {
                    if (string.IsNullOrEmpty(Path.GetDirectoryName(logFilePath!)))
                    {
                        var cwd = Directory.GetCurrentDirectory();
                        logFilePath = Path.Combine(cwd, logFilePath);
                    }

                    csvFileLog = new CsvFileLog(logFilePath, new List<CsvFileLog.ColumnKind>()
                                {
                                    CsvFileLog.ColumnKind.PreciseTimeStamp,
                                    CsvFileLog.ColumnKind.ProcessId,
                                    CsvFileLog.ColumnKind.ThreadId,
                                    CsvFileLog.ColumnKind.LogLevel,
                                    CsvFileLog.ColumnKind.LogLevelFriendly,
                                    CsvFileLog.ColumnKind.Message,
                                });
                }

                consoleLog = new ConsoleLog(useShortLayout: false, printSeverity: true);

                var logs = new ILog?[] { csvFileLog, consoleLog };
                logger = new Logger(logs.Where(log => log != null).Cast<ILog>().ToArray());

                await action(logger);
            }
            finally
            {
                logger?.Dispose();
                csvFileLog?.Dispose();
                consoleLog?.Dispose();
            }
        }

    }
}
