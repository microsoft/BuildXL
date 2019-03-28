// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BuildXL.Cache.ContentStore.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Logging;

namespace ContentStoreTest.Test
{
    public static class TestGlobal
    {
        private static readonly object Lock = new object();
        private static volatile ILogger _logger;

        public static ILogger Logger
        {
            get
            {
                if (_logger == null)
                {
                    lock (Lock)
                    {
                        if (_logger == null)
                        {
                            _logger = ChooseLogger(GetConfiguration());
                        }
                    }
                }

                return _logger;
            }
        }

        private static LoggingConfiguration GetConfiguration()
        {
            string assemblyFilePath = Assembly.GetExecutingAssembly().Location;
            string assemblyDirPath = Path.GetDirectoryName(assemblyFilePath);

            // ReSharper disable once AssignNullToNotNullAttribute
            string configFilePath = Path.Combine(assemblyDirPath, "LoggingConfiguration.json");

            if (File.Exists(configFilePath))
            {
                var bytes = File.ReadAllBytes(configFilePath);
                using (var stream = new MemoryStream(bytes))
                {
                    var configuration = stream.DeserializeFromJSON<LoggingConfiguration>();
                    return configuration;
                }
            }

            return new LoggingConfiguration();
        }

        private static ILogger ChooseLogger(LoggingConfiguration configuration)
        {
            string logFileBaseName = configuration.FileBaseName ?? "ContentStore.log";

            // Leave default if a log type has not been chosen in config.
            var logTypes = configuration.Types ?? new[] {"RollingMemory", "Console"};

            // Override console log severity from config or use default.
            var consoleSeverity = Severity.Debug;
            if (configuration.ConsoleSeverity != null)
            {
                Enum.TryParse(configuration.ConsoleSeverity, true, out consoleSeverity);
            }

            // Override file log severity from config or use default.
            var fileSeverity = Severity.Diagnostic;
            if (configuration.FileSeverity != null)
            {
                if (!Enum.TryParse(configuration.FileSeverity, true, out fileSeverity))
                {
                    fileSeverity = Severity.Diagnostic;
                }
            }

            // Override file log autoflush from config or use default.
            var logFileAutoFlush = configuration.FileAutoFlush;

            // Set directory where we write log file.
            var logDirectoryPath = Path.Combine(Path.GetTempPath(), "CloudStore");
            var localQTestDirectory = Environment.GetEnvironmentVariable("LOCALDIR");
            if (!string.IsNullOrEmpty(localQTestDirectory))
            {
                logDirectoryPath = Path.Combine(localQTestDirectory, "saved");
                logTypes = new[] {"File", "RollingMemory"};
            }

            // Setup possible logs that can be chosen.
            var logsByName = new Dictionary<string, Func<ILog>>(StringComparer.OrdinalIgnoreCase)
            {
                {"Console", () => new ConsoleLog(consoleSeverity) {SkipColor = true}},
                {"DebugPrint", () => new DebugPrintLog(consoleSeverity)},
                {"File", () => new FileLog(logDirectoryPath, logFileBaseName, fileSeverity, logFileAutoFlush)},
                {"RollingMemory", () => new RollingMemoryLog(Severity.Diagnostic)}
            };

            var logs = new List<ILog>();
            foreach (var logType in logTypes)
            {
                Func<ILog> createLog;
                if (logsByName.TryGetValue(logType, out createLog))
                {
                    logs.Add(createLog());
                }
            }

            return new Logger(true, logs.ToArray());
        }
    }
}
