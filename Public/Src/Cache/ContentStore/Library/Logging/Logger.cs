// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Timers;

#nullable enable

namespace BuildXL.Cache.ContentStore.Logging
{
    /// <summary>
    ///     A simple ILogger that supports multiple ILog instances and messages to them all.
    /// </summary>
    /// <remarks>
    ///     This is used only when CASaaS works standalone. Concretely, this means:
    ///       - ContentStoreApp
    ///       - Utilities
    ///       - Monitor
    ///       - ...
    ///     Clients usually provide their own logging infrastructure via <see cref="ILogger"/>
    /// </remarks>
    public class Logger : ILogger, IAsyncDisposable
    {
        /// <summary>
        /// Gets and sets a queue length used by an async version of the logger.
        /// </summary>
        public static int QueueLength { get; set; } = 100_000;

        /// <summary>
        /// Gets and sets a mode that defines the behavior of the queue when its full.
        /// </summary>
        public static BoundedChannelFullMode QueueFullMode { get; set; } = BoundedChannelFullMode.Wait;

        [MemberNotNullWhen(false, "_requests", "_writerTask")]
        private bool Synchronous { get; } // using a property and not a field, because 'MemberNotNullWhen' can't be used with fields.

        private readonly List<ILog> _logs;
        private readonly IntervalTimer? _flushTimer;

        private readonly Channel<Request>? _requests;
        private readonly Task? _writerTask;

        private int _errorCount;
        private bool _disposed;

        private Severity _currentSeverity;
        private int _pendingRequest;

        private Logger(bool synchronous, TimeSpan? flushInterval, params ILog[] logs)
        {
            Synchronous = synchronous;
            _logs = new List<ILog>(logs);

            _currentSeverity = _logs.Count == 0 ? Severity.Always : _logs.Min(l => l.CurrentSeverity);

            if (!Synchronous)
            {
                _requests = Channel.CreateBounded<Request>(
                    new BoundedChannelOptions(QueueLength)
                    {
                        AllowSynchronousContinuations = true,
                        FullMode = QueueFullMode,
                        // We consume the queue from a single thread but produce the log entries from multiple threads.
                        SingleWriter = false,
                        SingleReader = true,
                    });

                _writerTask = WriteAsync();
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
            DisposeAsync().GetAwaiter().GetResult();
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;

            Flush();

            if (!Synchronous)
            {
                _flushTimer?.Dispose();

                // No need to send any requests, just completing the channel to stop it.
                _requests.Writer.Complete();
                await _writerTask;
            }
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
            if (Synchronous)
            {
                FlushImpl();
            }
            else
            {
                SendRequest(Request.FlushRequest);
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
            var messageIn = messageFormat;
            if (messageArgs != null && messageArgs.Length > 0)
            {
                messageIn = string.Format(CultureInfo.CurrentCulture, messageFormat, messageArgs);
            }

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
            var messageIn = messageFormat;
            if (messageArgs != null && messageArgs.Length > 0)
            {
                messageIn = string.Format(CultureInfo.CurrentCulture, messageFormat, messageArgs);
            }

            var message = string.Format(CultureInfo.CurrentCulture, "{0}, Exception=[{1}]", messageIn, Interfaces.Results.Error.GetExceptionString(exception));
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
            if (messageArgs != null && messageArgs.Length > 0)
            {
                messageFormat = string.Format(CultureInfo.InvariantCulture, messageFormat, messageArgs);
            }

            LogString(severity, messageFormat);
        }

        private void LogString(Severity severity, string message)
        {
            if (severity == Severity.Error)
            {
                Interlocked.Increment(ref _errorCount);
            }

            DateTime dateTime = DateTime.Now;
            int threadId = Thread.CurrentThread.ManagedThreadId;

            if (Synchronous)
            {
                LogStringImpl(dateTime, threadId, severity, message);
            }
            else
            {
                var request = Request.LogStringRequest(dateTime, threadId, severity, message);
                SendRequest(request);
            }
        }

        private void SendRequest(Request request)
        {
            // Not checking that the instance is disposed, because we don't want to lock here and without
            // synchronization we'll have a race condition.
            // So instead, we just try and ignore the error if the instance is disposed.
            Interlocked.Increment(ref _pendingRequest);
            bool written = _requests!.Writer.TryWrite(request);

            if (QueueFullMode == BoundedChannelFullMode.Wait && !_disposed)
            {
                // Asserting that the message was written only when the full mode is 'wait', otherwise the messages can be dropped
                // and 'written' might be false in this case.

                Contract.Assert(written);
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

        private Task WriteAsync()
        {
            Contract.Requires(_requests is not null);

            return Task.Run(
                async () =>
                {
                    // Not using 'Reader.ReadAllAsync' because its not available in the version we use here.
                    // So we do what 'ReadAllAsync' does under the hood.
                    while (await _requests.Reader.WaitToReadAsync(CancellationToken.None).ConfigureAwait(false))
                    {
                        while (_requests.Reader.TryRead(out var request))
                        {
                            Interlocked.Decrement(ref _pendingRequest);

                            try
                            {
                                if (request.Type == RequestType.Flush)
                                {
                                    FlushImpl();
                                }
                                else if (request.Type == RequestType.LogString)
                                {
                                    LogStringImpl(request.DateTime, request.ThreadId, request.Severity, request.Message ?? string.Empty);
                                }
                            }
                            catch (Exception)
#pragma warning disable ERP022 // Unobserved exception in a generic exception handler
                            {
                                // We can't do anything here, but we don't want to break the message processing loop.
                            }
#pragma warning restore ERP022 // Unobserved exception in a generic exception handler
                        }
                    }
                });

        }
        
        private void FlushIfIdle()
        {
            if (Synchronous)
            {
                return;
            }

            // _requests.Reader.Count is only available in .net 5 and 6 so have to count the number of pending requests manually.
            if (Volatile.Read(ref _pendingRequest) == 0)
            {
                Flush();
            }
        }
    }
}
