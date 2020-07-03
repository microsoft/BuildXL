// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Tracing;
using global::BuildXL.Utilities.Instrumentation.Common;
using Instrumentation = global::BuildXL.Utilities.Instrumentation;

namespace Test.BuildXL.Utilities
{
    public class TestEvents : EventSource
    {
        private static readonly TestEvents s_log = new TestEvents();

        [SuppressMessage("Microsoft.Performance", "CA1810:InitializeReferenceTypeStaticFieldsInline")]
        static TestEvents()
        {
            // we must declare a static ctor in order to trigger beforefieldinit semantics. Otherwise the log
            // gets created before the listener exists and that leads to failures cause the listener doesn't
            // recognize keywords for some reason
        }

        private TestEvents()
#if NET_FRAMEWORK_451
            : base()
#else
            : base(EventSourceSettings.EtwSelfDescribingEventFormat)
#endif

        {
        }

        /// <summary>
        /// Gets the primary BuildXL event source instance.
        /// </summary>
        public static TestEvents Log
        {
            get { return s_log; }
        }

        public enum EventId
        {
            None = 0,
            VerboseEvent = 10000,
            InfoEvent = 10001,
            WarningEvent = 10002,
            ErrorEvent = 10003,
            CriticalEvent = 10004,
            AlwaysEvent = 10005,
            VerboseEventWithProvenance = 10006,
            DiagnosticEventInOtherTask = 10007,
            DiagnosticEvent = 10008,
            InfrastructureErrorEvent = 10009,
            UserErrorEvent = 10010
        }

        [Event(
             (int)EventId.VerboseEvent,
            Level = EventLevel.Verbose,
            Task = Instrumentation.Common.Tasks.UnitTest,
            Keywords = Keywords.UserMessage,
            Message = "{0}")]
        public void VerboseEvent(string message)
        {
            WriteEvent((int)EventId.VerboseEvent, message);
        }

        [Event(
            (int)EventId.DiagnosticEvent,
            Level = EventLevel.Verbose,
            Task = Instrumentation.Common.Tasks.UnitTest,
            Keywords = Keywords.Diagnostics,
            Message = "{0}")]
        public void DiagnosticEvent(string message)
        {
            WriteEvent((int)EventId.DiagnosticEvent, message);
        }

        [Event(
            (int)EventId.DiagnosticEventInOtherTask,
            Level = EventLevel.Verbose,
            Task = Instrumentation.Common.Tasks.UnitTest2,
            Keywords = Keywords.Diagnostics,
            Message = "{0}")]
        public void DiagnosticEventInOtherTask(string message)
        {
            WriteEvent((int)EventId.DiagnosticEventInOtherTask, message);
        }

        [Event(
            (int)EventId.InfoEvent,
            Level = EventLevel.Informational,
            Task = Instrumentation.Common.Tasks.UnitTest,
            Keywords = Keywords.UserMessage,
            Message = "{0}")]
        public void InfoEvent(string message)
        {
            WriteEvent((int)EventId.InfoEvent, message);
        }

        [Event(
            (int)EventId.WarningEvent,
            Level = EventLevel.Warning,
            Task = Instrumentation.Common.Tasks.UnitTest,
            Keywords = Keywords.UserMessage,
            Message = "{0}")]
        public void WarningEvent(string message)
        {
            WriteEvent((int)EventId.WarningEvent, message);
        }

        [Event(
            (int)EventId.ErrorEvent,
            Level = EventLevel.Error,
            Task = Instrumentation.Common.Tasks.UnitTest,
            Keywords = Keywords.UserMessage,
            Message = "{0}")]
        public void ErrorEvent(string message)
        {
            WriteEvent((int)EventId.ErrorEvent, message);
        }

        [Event(
            (int)EventId.InfrastructureErrorEvent,
            Level = EventLevel.Error,
            Task = Instrumentation.Common.Tasks.UnitTest,
            Keywords = Keywords.UserMessage | Keywords.InfrastructureError,
            Message = "{0}")]
        public void InfrastructureErrorEvent(string message)
        {
            WriteEvent((int)EventId.InfrastructureErrorEvent, message);
        }

        [Event(
            (int)EventId.UserErrorEvent,
            Level = EventLevel.Error,
            Task = Instrumentation.Common.Tasks.UnitTest,
            Keywords = Keywords.UserMessage | Keywords.UserError,
            Message = "{0}")]
        public void UserErrorEvent(string message)
        {
            WriteEvent((int)EventId.UserErrorEvent, message);
        }

        [Event(
            (int)EventId.CriticalEvent,
            Level = EventLevel.Critical,
            Task = Instrumentation.Common.Tasks.UnitTest,
            Keywords = Keywords.UserMessage,
            Message = "{0}")]
        public void CriticalEvent(string message)
        {
            WriteEvent((int)EventId.CriticalEvent, message);
        }

        [Event(
            (int)EventId.AlwaysEvent,
            Level = EventLevel.LogAlways,
            Task = Instrumentation.Common.Tasks.UnitTest,
            Keywords = Keywords.UserMessage,
            Message = "{0}")]
        public void AlwaysEvent(string message)
        {
            WriteEvent((int)EventId.AlwaysEvent, message);
        }

        [Event(
            (int)EventId.VerboseEventWithProvenance,
            Level = EventLevel.Verbose,
            Task = Instrumentation.Common.Tasks.UnitTest,
            Keywords = Keywords.UserMessage,
            Message = EventConstants.ProvenancePrefix + "{3}")]
        public void VerboseEventWithProvenance(string file, int line, int column, string message)
        {
            WriteEvent((int)EventId.VerboseEventWithProvenance, file, line, column, message);
        }
    }
}
