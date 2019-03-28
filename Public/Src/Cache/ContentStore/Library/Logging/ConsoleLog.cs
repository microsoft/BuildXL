// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using BuildXL.Cache.ContentStore.Interfaces.Logging;

namespace BuildXL.Cache.ContentStore.Logging
{
    /// <summary>
    ///     An ILog that displays messages on the console.
    /// </summary>
    public class ConsoleLog : ILog
    {
        private const int SeverityFieldMinLength = 5;

        private static readonly IReadOnlyDictionary<Severity, string> SeverityNames = new Dictionary
            <Severity, string>
        {
            {Severity.Unknown, string.Empty},
            {Severity.Diagnostic, "DIAG"},
            {Severity.Debug, "DEBUG"},
            {Severity.Info, "INFO"},
            {Severity.Warning, "WARN"},
            {Severity.Error, "ERROR"},
            {Severity.Fatal, "ERROR"},
            {Severity.Always, "ALWAYS"}
        };

        private readonly object _lock = new object();
        private readonly ConsoleColor _prevConsoleColor;

        private readonly bool _printSeverity;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ConsoleLog" /> class with given configuration.
        /// </summary>
        /// <param name="severity">Only messages with up to this severity are displayed.</param>
        /// <param name="useShortLayout">If true, only show bare messages without any timestamp.</param>
        /// <param name="printSeverity">If true then message severity name will log a part of every message logged</param>
        public ConsoleLog(Severity severity = Severity.Diagnostic, bool useShortLayout = true, bool printSeverity = false)
        {
            UseShortLayout = useShortLayout;
            CurrentSeverity = severity;
            _prevConsoleColor = Console.ForegroundColor;
            _printSeverity = printSeverity;
        }

        /// <summary>
        ///     Sets a value indicating whether set true to not manipulate output color.
        /// </summary>
        public bool SkipColor { private get; set; }

        /// <summary>
        ///     Sets a value indicating whether set the line format to short or long form.
        /// </summary>
        public bool UseShortLayout { private get; set; }

        /// <inheritdoc />
        public Severity CurrentSeverity { get; set; }

        /// <inheritdoc />
        public void Flush()
        {
        }

        /// <inheritdoc />
        public void Write(DateTime dateTime, int threadId, Severity severity, string message)
        {
            if (severity < CurrentSeverity)
            {
                return;
            }

            lock (_lock)
            {
                ConsoleColor thisColor = _prevConsoleColor;
                switch (severity)
                {
                    case Severity.Always:
                        thisColor = ConsoleColor.White;
                        break;
                    case Severity.Fatal:
                        thisColor = ConsoleColor.Magenta;
                        break;
                    case Severity.Error:
                        thisColor = ConsoleColor.Red;
                        break;
                    case Severity.Warning:
                        thisColor = ConsoleColor.Yellow;
                        break;
                    case Severity.Info:
                        thisColor = ConsoleColor.Gray;
                        break;
                    case Severity.Debug:
                        thisColor = ConsoleColor.DarkGreen;
                        break;
                    case Severity.Diagnostic:
                        thisColor = ConsoleColor.DarkGray;
                        break;
                }

                string line;
                if (UseShortLayout)
                {
                    line = message;
                }
                else if (_printSeverity)
                {
                    string severityName = GetSeverityName(severity);
                    line = string.Join(" ", dateTime.ToString("hh:mm:ss,fff", CultureInfo.CurrentCulture), severityName, message);
                }
                else
                {
                    line = string.Join(" ", dateTime.ToString("hh:mm:ss,fff", CultureInfo.CurrentCulture), message);
                }

                if (!SkipColor)
                {
                    Console.ForegroundColor = thisColor;
                }

                if (severity == Severity.Error || severity == Severity.Fatal)
                {
                    WriteError(line);
                }
                else
                {
                    WriteLine(line);
                }

                if (!SkipColor)
                {
                    Console.ForegroundColor = _prevConsoleColor;
                }
            }
        }

        /// <nodoc />
        protected virtual void WriteError(string line) => Console.Error.WriteLine(line);

        /// <nodoc />
        protected virtual void WriteLine(string line) => Console.WriteLine(line);

        /// <inheritdoc />
        public void Dispose()
        {
        }

        private static string GetSeverityName(Severity severity)
        {
            // Format as left-justified when the name is shorter than minimum length of the field
            var format = "{0,-" + SeverityFieldMinLength + "}";
            return string.Format(CultureInfo.CurrentCulture, format, SeverityNames[severity]);
        }
    }
}
