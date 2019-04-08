// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using Microsoft.VisualStudio.Services.Content.Common.Tracing;

namespace BuildXL.Cache.ContentStore.Vsts
{
    internal class AppTraceSourceContextAdapter : IAppTraceSource
    {
        private readonly Context _context;
        private readonly string _name;
        private readonly IReadOnlyDictionary<Severity, Action<string>> _severityToContextAction;

        private static readonly Dictionary<TraceEventType, SourceLevels> TraceEventTypeToLevel = new Dictionary<TraceEventType, SourceLevels>()
        {
            { TraceEventType.Critical, SourceLevels.Critical },
            { TraceEventType.Error, SourceLevels.Error },
            { TraceEventType.Information, SourceLevels.Information },

            { TraceEventType.Resume, SourceLevels.ActivityTracing },
            { TraceEventType.Start, SourceLevels.ActivityTracing },
            { TraceEventType.Stop, SourceLevels.ActivityTracing },
            { TraceEventType.Suspend, SourceLevels.ActivityTracing },
            { TraceEventType.Transfer, SourceLevels.ActivityTracing },

            { TraceEventType.Verbose, SourceLevels.Verbose },
            { TraceEventType.Warning, SourceLevels.Warning }
        };

        private static readonly IReadOnlyDictionary<SourceLevels, Severity> SourceLevelsToSeverity = new Dictionary<SourceLevels, Severity>
        {
            { SourceLevels.Critical, Severity.Always },
            { SourceLevels.Error, Severity.Error },
            { SourceLevels.Warning, Severity.Warning },
            { SourceLevels.Information, Severity.Info },
            { SourceLevels.Verbose, Severity.Diagnostic },
            { SourceLevels.ActivityTracing, Severity.Debug }
        };

        public AppTraceSourceContextAdapter(Context context, string name, SourceLevels leastSignificantToLog)
        {
            _context = context;
            _name = name;
            SwitchLevel = leastSignificantToLog;

            _severityToContextAction = new Dictionary<Severity, Action<string>>
            {
                { Severity.Always, _context.Always },
                { Severity.Error, _context.Error },
                { Severity.Warning, _context.Warning },
                { Severity.Info, _context.Info },
                { Severity.Diagnostic, _context.Debug },
                { Severity.Debug, _context.Debug }
            };
        }

        public TraceListenerCollection Listeners => null;

        public SourceLevels SwitchLevel { get; }

        public bool HasError => false;

        public void Critical(string format, params object[] args) => CriticalBase(id: null, exception: null, format, args);

        public void Critical(int id, string format, params object[] args) => CriticalBase(id, exception: null, format, args);

        public void Critical(Exception ex) => CriticalBase(id: null, ex, format: null, args: null);

        public void Critical(int id, Exception ex) => CriticalBase(id, ex, format: null, args: null);

        public void Critical(Exception ex, string format, params object[] args) => CriticalBase(id: null, ex, format, args);

        public void Critical(int id, Exception ex, string format, params object[] args) => CriticalBase(id, ex, format, args);

        public void Error(string format, params object[] args) => ErrorBase(id: null, exception: null, format, args);

        public void Error(int id, string format, params object[] args) => ErrorBase(id, exception: null, format, args);

        public void Error(Exception ex) => ErrorBase(id: null, ex, format: null, args: null);

        public void Error(int id, Exception ex) => ErrorBase(id, ex, format: null, args: null);

        public void Error(Exception ex, string format, params object[] args) => ErrorBase(id: null, ex, format, args);

        public void Error(int id, Exception ex, string format, params object[] args) => ErrorBase(id, ex, format, args);

        public void Info(string format, params object[] args) => InfoBase(id: null, format, args);

        public void Info(int id, string format, params object[] args) => InfoBase(id, format, args);

        public void TraceEvent(TraceEventType eventType, int id) => TraceEventBase(eventType, id, format: null, args: null);

        public void TraceEvent(TraceEventType eventType, int id, string message) => TraceEventBase(eventType, id, message, args: null);

        public void TraceEvent(TraceEventType eventType, int id, string format, params object[] args) => TraceEventBase(eventType, id, format, args);

        public void Verbose(string format, params object[] args) => VerboseBase(id: null, exception: null, format, args);

        public void Verbose(int id, string format, params object[] args) => VerboseBase(id, exception: null, format, args);

        public void Verbose(Exception ex) => VerboseBase(id: null, ex, format: null, args: null);

        public void Verbose(int id, Exception ex) => VerboseBase(id, ex, format: null, args: null);

        public void Verbose(Exception ex, string format, params object[] args) => VerboseBase(id: null, ex, format, args);

        public void Verbose(int id, Exception ex, string format, params object[] args) => VerboseBase(id, ex, format, args);

        public void Warn(string format, params object[] args) => WarnBase(id: null, exception: null, format, args);

        public void Warn(int id, string format, params object[] args) => WarnBase(id, exception: null, format, args);

        public void Warn(Exception ex) => WarnBase(id: null, ex, format: null, args: null);

        public void Warn(int id, Exception ex) => WarnBase(id, ex, format: null, args: null);

        public void Warn(Exception ex, string format, params object[] args) => WarnBase(id: null, ex, format, args);

        public void Warn(int id, Exception ex, string format, params object[] args) => WarnBase(id, ex, format, args);

        public void AddConsoleTraceListener()
        {
        }

        public void ResetErrorDetection()
        {
        }

        public void AddFileTraceListener(string fullFileName)
        {
        }

        // SourceLevel flag values are in descending order, so "most severe" represented as 1 and "least severe" is represented by the highest value
        private bool ShouldLog(SourceLevels level) => SwitchLevel == SourceLevels.All || level <= SwitchLevel;

        private void Log(Severity severity, string message) => _severityToContextAction[severity]($"{_name}: {message}");

        private string FormatMessage(int? id, Exception exception, string format, params object[] args)
        {
            var message = new StringBuilder();

            if (id.HasValue)
            {
                message.Append($"{id}, ");
            }

            if (!string.IsNullOrEmpty(format))
            {
                message.AppendFormatSafe(format, args);
                if (exception != null)
                {
                    message.Append(" ");
                }
            }

            if (exception != null)
            {
                message.Append(exception.ToString());
            }

            return message.ToString();
        }

        private void CriticalBase(int? id, Exception exception, string format, params object[] args)
        {
            if (ShouldLog(SourceLevels.Critical))
            {
                Log(SourceLevelsToSeverity[SourceLevels.Critical], FormatMessage(id, exception, format, args));
            }
        }

        private void ErrorBase(int? id, Exception exception, string format, params object[] args)
        {
            if (ShouldLog(SourceLevels.Error))
            {
                Log(SourceLevelsToSeverity[SourceLevels.Error], FormatMessage(id, exception, format, args));
            }
        }

        private void InfoBase(int? id, string format, params object[] args)
        {
            if (ShouldLog(SourceLevels.Information))
            {
                Log(SourceLevelsToSeverity[SourceLevels.Information], FormatMessage(id, null, format, args));
            }
        }

        private void TraceEventBase(TraceEventType eventType, int id, string format, params object[] args)
        {
            if (ShouldLog(SourceLevels.ActivityTracing))
            {
                var message = new StringBuilder();
                var level = TraceEventTypeToLevel[eventType];

                message.Append(id);
                if (level == SourceLevels.ActivityTracing)
                {
                    message.Append($", {eventType}");
                }

                if (!string.IsNullOrEmpty(format))
                {
                    message.AppendFormatSafe($", {format}", args);
                }

                Log(SourceLevelsToSeverity[SourceLevels.ActivityTracing], message.ToString());
            }
        }

        private void VerboseBase(int? id, Exception exception, string format, params object[] args)
        {
            if (ShouldLog(SourceLevels.Verbose))
            {
                Log(SourceLevelsToSeverity[SourceLevels.Verbose], FormatMessage(id, exception, format, args));
            }
        }

        private void WarnBase(int? id, Exception exception, string format, params object[] args)
        {
            if (ShouldLog(SourceLevels.Warning))
            {
                Log(SourceLevelsToSeverity[SourceLevels.Warning], FormatMessage(id, exception, format, args));
            }
        }
    }
}
