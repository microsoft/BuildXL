// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Text;
using System.Threading;
using BuildXL.Ipc.Interfaces;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Ipc.Common
{
    /// <summary>
    /// Implementation of <see cref="ILogger"/> that forwards log requests to a file.
    /// </summary>
    public sealed class FileLogger : ILogger, IDisposable
    {
        // 24K buffer size means that internally, the StreamWriter will use 48KB for a char[] array, and 73731 bytes for an encoding byte array buffer --- all buffers <85000 bytes, and therefore are not in large object heap
        private const int LogFileBufferSize = 24 * 1024;
        
        // flush the log every ten minutes
        private static readonly TimeSpan s_flushInterval = TimeSpan.FromMinutes(10);

        private readonly TextWriter m_writer;
        private readonly Timer m_flushTimer;
        private bool m_disposed = false;
        private readonly object m_lock = new object();

        /// <nodoc />
        public bool IsLoggingVerbose { get; }

        /// <nodoc />
        public string Prefix { get; }

        /// <nodoc />
        public FileLogger(string logDirectory, string logFileName, string monikerId, bool logVerbose, string prefix = null)
        {
            IsLoggingVerbose = logVerbose;
            Prefix = prefix ?? string.Empty;
            int i = 1;
            string originalLogFileName = logFileName;
            string daemonLog;

            while (File.Exists(daemonLog = Path.Combine(logDirectory, $"{logFileName}-{monikerId}.log")))
            {
                logFileName = $"{originalLogFileName}{i++}";
            }

            LazilyCreatedStream stream = new LazilyCreatedStream(daemonLog);

            // Occasionally we see things logged that aren't valid unicode characters.
            // Emitting gibberish for these peculiar characters isn't a big deal
            var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);
            m_writer = TextWriter.Synchronized(new StreamWriter(stream, utf8NoBom, LogFileBufferSize));

            m_flushTimer = new Timer(FlushLog, null, s_flushInterval, Timeout.InfiniteTimeSpan);
        }

        /// <summary>
        /// Logs to Console.Out, unless <paramref name="level"/> is <see cref="Interfaces.LogLevel.Verbose"/>,
        /// and <see cref="IsLoggingVerbose"/> is <code>false</code>.
        /// </summary>
        public void Log(LogLevel level, string format, params object[] args)
        {
            if (!IsLoggingVerbose && level == LogLevel.Verbose)
            {
                return;
            }

            var message = Prefix + LoggerExtensions.Format(level, format, args);
            m_writer.WriteLine(message);
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
                m_disposed = true;

                m_flushTimer.Dispose();

                m_writer.Dispose();
            }
        }
    }
}
