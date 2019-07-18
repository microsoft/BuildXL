// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Timers;

namespace BuildXL.Cache.ContentStore.Logging
{
    /// <summary>
    ///     A simple ILogger that supports multiple ILog instances and messages to them all.
    /// </summary>
    public sealed class Logger : ILogger
    {
        private readonly bool _synchronous;
        private readonly List<ILog> _logs;
        private readonly Task _writerTask;
        private readonly IntervalTimer _flushTimer;
        private BlockingCollection<Request> _requests;
        private int _errorCount;
        private bool _disposed;

        // Setting the maximum severity by default.
        private Severity _currentSeverity = Severity.Always;

        private Logger(bool synchronous, TimeSpan? flushInterval, params ILog[] logs)
        {
            _synchronous = synchronous;
            _logs = new List<ILog>(logs);

            _currentSeverity = _logs.Count == 0 ? Severity.Always : _logs.Min(l => l.CurrentSeverity);

            if (!_synchronous)
            {
                _requests = new BlockingCollection<Request>();
                _writerTask = Task.Factory.StartNew(Writer, TaskCreationOptions.LongRunning);
                if (flushInterval.HasValue)
                {
                    _flushTimer = new IntervalTimer(() => FlushIfIdle(), flushInterval.Value);
                }
            }
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="Logger" /> class.
        /// </summary>
        public Logger(TimeSpan flushInterval, params ILog[] logs)
            : this(false, flushInterval, logs)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="Logger" /> class.
        /// </summary>
        public Logger(bool synchronous, params ILog[] logs)
            : this(synchronous, null, logs)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="Logger" /> class.
        /// </summary>
        public Logger(params ILog[] logs)
            : this(false, logs)
        {
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Flush();

            if (!_synchronous)
            {
                _flushTimer?.Dispose();
                _requests?.Add(new ShutdownRequest());
                _writerTask?.Wait();
                _requests?.Dispose();
                _requests = null;
            }

            _disposed = true;
        }

        /// <inheritdoc />
        public Severity CurrentSeverity => _currentSeverity;

        /// <inheritdoc />
        public int ErrorCount => _errorCount;

        /// <summary>
        ///     Add an ILog to the existing set of loggers receiving messages.
        /// </summary>
        public void AddLog(ILog log)
        {
            Contract.Requires(log != null);
            _logs.Add(log);

            // Get current lowest severity being logged by the current collection of logs.
            // In other words, at least one log in the collection will write at this severity.
            _currentSeverity = (Severity)Math.Min((int)_currentSeverity, (int)log.CurrentSeverity);
        }

        /// <summary>
        ///     Return all logs of given type.
        /// </summary>
        public IEnumerable<T> GetLog<T>()
        {
            return _logs.Where(log => log is T).Cast<T>();
        }

        /// <inheritdoc />
        public void Flush()
        {
            if (_synchronous)
            {
                FlushImpl();
            }
            else
            {
                _requests?.Add(new FlushRequest());
            }
        }

        /// <inheritdoc />
        public void Always(string messageFormat, params object[] messageArgs)
        {
            LogFormat(Severity.Always, messageFormat, messageArgs);
        }

        /// <inheritdoc />
        public void Fatal(string messageFormat, params object[] messageArgs)
        {
            LogFormat(Severity.Fatal, messageFormat, messageArgs);
            Flush();
        }

        /// <inheritdoc />
        public void Fatal(Exception exception, string messageFormat, params object[] messageArgs)
        {
            var messageIn = string.Format(CultureInfo.CurrentCulture, messageFormat, messageArgs);
            var message = string.Format(CultureInfo.CurrentCulture, "{0}, Exception=[{1}]", messageIn, exception);
            LogString(Severity.Fatal, message);
            Flush();
        }

        /// <inheritdoc />
        public void Error(string messageFormat, params object[] messageArgs)
        {
            LogFormat(Severity.Error, messageFormat, messageArgs);
            Flush();
        }

        /// <inheritdoc />
        public void Error(Exception exception, string messageFormat, params object[] messageArgs)
        {
            var messageIn = string.Format(CultureInfo.CurrentCulture, messageFormat, messageArgs);
            var message = string.Format(CultureInfo.CurrentCulture, "{0}, Exception=[{1}]", messageIn, exception);
            LogString(Severity.Error, message);
            Flush();
        }

        /// <inheritdoc />
        [ExcludeFromCodeCoverage]
        public void ErrorThrow(Exception exception, string messageFormat, params object[] messageArgs)
        {
            Error(exception, messageFormat, messageArgs);
            throw exception;
        }

        /// <inheritdoc />
        public void Warning(string messageFormat, params object[] messageArgs)
        {
            LogFormat(Severity.Warning, messageFormat, messageArgs);
        }

        /// <inheritdoc />
        public void Info(string messageFormat, params object[] messageArgs)
        {
            LogFormat(Severity.Info, messageFormat, messageArgs);
        }

        /// <inheritdoc />
        public void Debug(string messageFormat, params object[] messageArgs)
        {
            LogFormat(Severity.Debug, messageFormat, messageArgs);
        }

        /// <inheritdoc />
        public void Debug(Exception exception)
        {
            LogString(Severity.Debug, exception.ToString());
        }

        /// <inheritdoc />
        public void Diagnostic(string messageFormat, params object[] messageArgs)
        {
            LogFormat(Severity.Diagnostic, messageFormat, messageArgs);
        }

        /// <inheritdoc />
        public void Log(Severity severity, string message)
        {
            LogString(severity, message);
        }

        /// <inheritdoc />
        public void LogFormat(Severity severity, string messageFormat, params object[] messageArgs)
        {
            LogString(severity, string.Format(CultureInfo.InvariantCulture, messageFormat, messageArgs));
        }

        private void LogString(Severity severity, string message)
        {
            if (severity == Severity.Error)
            {
                Interlocked.Increment(ref _errorCount);
            }

            DateTime dateTime = DateTime.Now;
            int threadId = Thread.CurrentThread.ManagedThreadId;

            if (_synchronous)
            {
                LogStringImpl(dateTime, threadId, severity, message);
            }
            else
            {
                var request = new LogStringRequest(dateTime, threadId, severity, message);
                _requests?.Add(request);
            }
        }

        private void LogStringImpl(DateTime dateTime, int threadId, Severity severity, string message)
        {
            foreach (ILog log in _logs)
            {
                log.Write(dateTime, threadId, severity, message);
            }
        }

        private void FlushImpl()
        {
            foreach (ILog log in _logs)
            {
                log.Flush();
            }
        }

        private void Writer()
        {
            try
            {
                foreach (var request in _requests.GetConsumingEnumerable())
                {
                    if (request.Type == RequestType.Shutdown)
                    {
                        break;
                    }

                    if (request.Type == RequestType.Flush)
                    {
                        FlushImpl();
                    }

                    if (request.Type == RequestType.LogString)
                    {
                        LogStringImpl(request.DateTime, request.ThreadId, request.Severity, request.Message);
                    }
                }
            }
            catch (Exception e)
            {
                // Go ahead and let the exception escape and bring the process down because
                // something is seriously broken, but show the exception on the way out.
                Console.WriteLine("Logger.Writer unexpected exception=[{0}]", e);
                throw;
            }
        }

        private void FlushIfIdle()
        {
            if (!_synchronous && _requests?.Count == 0)
            {
                Flush();
            }
        }
    }

    /// <summary>
    ///     Message to the background thread.
    /// </summary>
    internal enum RequestType
    {
        /// <summary>
        ///     Shutdown the thread.
        /// </summary>
        Shutdown,

        /// <summary>
        ///     Flush the log.
        /// </summary>
        Flush,

        /// <summary>
        ///     Log on the background thread.
        /// </summary>
        LogString
    }
}
