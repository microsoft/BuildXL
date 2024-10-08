// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using BuildXL.Ipc.Interfaces;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Ipc.Common
{
    /// <summary>
    /// Implementation of <see cref="IIpcLogger "/> that forwards log requests to a file.
    /// </summary>
    public sealed class FileLogger : IIpcLogger, IDisposable
    {
        // 24K buffer size means that internally, the StreamWriter will use 48KB for a char[] array, and 73731 bytes for an encoding byte array buffer --- all buffers <85000 bytes, and therefore are not in large object heap
        private const int LogFileBufferSize = 24 * 1024;

        // flush the log every ten minutes
        private static readonly TimeSpan s_flushInterval = TimeSpan.FromMinutes(1);

        /// <summary>
        /// The writer is not synchronized, so all operations must be under a lock. 
        /// </summary>
        private readonly TextWriter m_writer;
        private readonly Timer m_flushTimer;
        private bool m_disposed = false;
        private readonly object m_lock = new object();

        /// <nodoc />
        public bool IsLoggingVerbose { get; }

        /// <nodoc />
        public string Prefix { get; }

        /// <summary>Full path to the log file</summary>
        public string LogFilePath { get; }

        /// <nodoc />
        public FileLogger(string logDirectory, string logFileName, string monikerId, bool logVerbose, string prefix = null)
        {
            IsLoggingVerbose = logVerbose;
            Prefix = prefix ?? string.Empty;
            int i = 1;
            string originalLogFileName = logFileName;
            string logFullPath;

            while (File.Exists(logFullPath = Path.Combine(logDirectory, $"{logFileName}-{monikerId}.log")))
            {
                logFileName = $"{originalLogFileName}{i++}";
            }

            LogFilePath = logFullPath;
            LazilyCreatedStream stream = new LazilyCreatedStream(logFullPath);

            // Occasionally we see things logged that aren't valid unicode characters.
            // Emitting gibberish for these peculiar characters isn't a big deal
            var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);
            m_writer = new StreamWriter(stream, utf8NoBom, LogFileBufferSize);

            m_flushTimer = new Timer(FlushLog, null, s_flushInterval, Timeout.InfiniteTimeSpan);
        }

        /// <summary>
        /// Logs to a file, unless <paramref name="level"/> is <see cref="Interfaces.LogLevel.Verbose"/>,
        /// and <see cref="IsLoggingVerbose"/> is <code>false</code>.
        /// </summary>
        public void Log(LogLevel level, string format, params object[] args)
        {
            if (!IsLoggingVerbose && level == LogLevel.Verbose)
            {
                return;
            }

            lock (m_lock)
            {
                m_writer.Write(Prefix);
                m_writer.WriteLine(LoggerExtensions.Format(level, format, args));
            }
        }

        /// <summary>
        /// Logs to a file, unless <paramref name="level"/> is <see cref="Interfaces.LogLevel.Verbose"/>,
        /// and <see cref="IsLoggingVerbose"/> is <code>false</code>.
        /// </summary>
        public void Log(LogLevel level, StringBuilder message)
        {
            if (!IsLoggingVerbose && level == LogLevel.Verbose)
            {
                return;
            }

            lock (m_lock)
            {
                m_writer.Write(Prefix);
                m_writer.Write(LoggerExtensions.GetFormattedTimestamp(level));
                m_writer.WriteLine(message);
            }
        }

        /// <summary>
        /// Logs to a file, unless <paramref name="level"/> is <see cref="Interfaces.LogLevel.Verbose"/>,
        /// and <see cref="IsLoggingVerbose"/> is <code>false</code>.
        /// </summary>
        public void Log(LogLevel level, string header, IEnumerable<string> items, bool placeItemsOnSeparateLines)
        {
            if (!IsLoggingVerbose && level == LogLevel.Verbose)
            {
                return;
            }

            lock (m_lock)
            {
                m_writer.Write(Prefix);
                m_writer.Write(LoggerExtensions.GetFormattedTimestamp(level));
                m_writer.WriteLine(header);
                foreach (var item in items)
                {
                    if (placeItemsOnSeparateLines)
                    {
                        m_writer.WriteLine(item);
                    }
                    else
                    {
                        m_writer.Write(item);
                    }
                }
            }
        }

        /// <nodoc />
        public void Flush()
        {
            FlushLog(null);
        }

        private void FlushLog(object obj)
        {
            lock (m_lock)
            {
                if (m_disposed)
                {
                    return;
                }

                m_writer.Flush();
                m_flushTimer.Change(s_flushInterval, Timeout.InfiniteTimeSpan);
            }
        }

        /// <nodoc />
        public void Dispose()
        {
            if (m_disposed)
            {
                return;
            }

            lock (m_lock)
            {
                if (m_disposed)
                {
                    return;
                }

                m_disposed = true;

                m_flushTimer.Dispose();

                // flush the writer before disposing
                m_writer.Flush();
                m_writer.Close();
                m_writer.Dispose();
            }
        }
    }
}
