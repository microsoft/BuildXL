// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using VSCode.DebugProtocol;

#pragma warning disable 1591 // disabling warning about missing API documentation; TODO: Remove this line and write documentation!
#pragma warning disable SA1649 // File name must match first type name

namespace VSCode.DebugAdapter
{
    // ===========================================================================================
    // === EVENT CLASSES
    //
    // All of these classes must be 'JSON serializable'; none need be 'JSON deserializable' (as
    // events are only sent, never received).
    //
    // See file 'Commands.cs' for a description of 'JSON deserializable'.
    // ===========================================================================================
    public sealed class InitializedEvent : Event<object>, IInitializedEvent
    {
        public InitializedEvent()
            : base("initialized") { }
    }

    public sealed class StoppedEventBody : IStoppedEventBody
    {
        public int ThreadId { get; }

        public string Reason { get; }

        public string Text { get; }

        public bool AllThreadsStopped { get; }

        public StoppedEventBody(int threadId, string reason, string text, bool allThreadsStopped = false)
        {
            ThreadId = threadId;
            Reason = reason;
            Text = text;
            AllThreadsStopped = allThreadsStopped;
        }
    }

    public sealed class StoppedEvent : Event<IStoppedEventBody>, IStoppedEvent
    {
        public StoppedEvent(int tid, string reasn, string txt = null)
            : base("stopped", new StoppedEventBody(tid, reasn, txt)) { }
    }

    public sealed class ExitedEventBody : IExitedEventBody
    {
        public int ExitCode { get; }

        public ExitedEventBody(int exitCode)
        {
            ExitCode = exitCode;
        }
    }

    public sealed class ExitedEvent : Event<IExitedEventBody>, IExitedEvent
    {
        public ExitedEvent(int exitCode)
            : base("exited", new ExitedEventBody(exitCode)) { }
    }

    public sealed class TerminatedEventBody : ITerminatedEventBody
    {
        public bool Restart { get; }

        public TerminatedEventBody(bool restart)
        {
            Restart = restart;
        }
    }

    public sealed class TerminatedEvent : Event<ITerminatedEventBody>, ITerminatedEvent
    {
        public TerminatedEvent(bool restart = false)
            : base("terminated", new TerminatedEventBody(restart)) { }
    }

    public sealed class ThreadEventBody : IThreadEventBody
    {
        public int ThreadId { get; }

        public string Reason { get; }

        public ThreadEventBody(int threadId, string reason)
        {
            ThreadId = threadId;
            Reason = reason;
        }
    }

    public sealed class ThreadEvent : Event<IThreadEventBody>, IThreadEvent
    {
        public ThreadEvent(int threadId, string reason)
            : base("thread", new ThreadEventBody(threadId, reason)) { }
    }

    public sealed class OutputEventBody : IOutputEventBody
    {
        public string Category { get; }

        public string Output { get; }

        public object Data { get; }

        public OutputEventBody(string output, string category = "console", object data = null)
        {
            Output = output;
            Category = category;
            Data = data;
        }
    }

    public sealed class OutputEvent : Event<IOutputEventBody>, IOutputEvent
    {
        public OutputEvent(string category, string output)
            : base("output", new OutputEventBody(output, category)) { }
    }
}
