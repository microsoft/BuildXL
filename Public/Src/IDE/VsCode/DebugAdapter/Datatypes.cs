// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Globalization;
using Newtonsoft.Json;
using VSCode.DebugProtocol;

#pragma warning disable 1591 // disabling warning about missing API documentation; TODO: Remove this line and write documentation!
#pragma warning disable SA1649 // File name must match first type name

namespace VSCode.DebugAdapter
{
    // ===========================================================================================
    // === DATATYPE CLASSES
    //
    // All of these classes must be 'JSON serializable'; only ProtocolMessage and Request must be
    // 'JSON deserializable'.
    //
    // See file 'Commands.cs' for a description of 'JSON deserializable'.
    // ===========================================================================================
    public class ProtocolMessage : IProtocolMessage
    {
        public int Seq { get; set; }

        public string Type { get; }

        [JsonConstructor]
        public ProtocolMessage(string type, int seq = 0)
        {
            Type = type;
            Seq = seq;
        }

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }
    }

    public sealed class Request : ProtocolMessage, IRequest
    {
        public string Command { get; }

        public object Arguments { get; }

        [JsonConstructor]
        public Request(int seq, string command, object arguments)
            : base("request", seq)
        {
            Command = command;
            Arguments = arguments;
        }
    }

    public sealed class Response : ProtocolMessage, IResponse
    {
        public bool Success { get; }

        public string Message { get; }

        [JsonProperty(PropertyName = "request_seq")]
        public int RequestSeq { get; }

        public string Command { get; }

        public object Body { get; }

        public Response(int requestSeq, string command, bool success = true, object body = null, string message = null)
            : base("response")
        {
            RequestSeq = requestSeq;
            Command = command;
            Message = message;
            Success = success;
            Body = body;
        }
    }

    public class Event<T> : ProtocolMessage, IEvent<T>
    {
        [JsonProperty(PropertyName = "event")]
        public string EventType { get; }

        public T Body { get; }

        string IEvent<T>.EventType => EventType;

        public Event(string type, T body = default(T))
            : base("event")
            {
            EventType = type;
            Body = body;
        }
    }

    public sealed class Message : IMessage
    {
        public int Id { get; }

        public string Format { get; }

        public bool ShowUser { get; }

        public bool SendTelemetry { get; }

        public string Url { get; }

        public string UrlLabel { get; }

        public Message(int id, string format, bool showUser = true,
                       bool sendTelemetry = false, string url = null, string urlLabel = null)
        {
            Id = id;
            Format = format;
            ShowUser = showUser;
            SendTelemetry = sendTelemetry;
            Url = url;
            UrlLabel = urlLabel;
        }
    }

    public sealed class StackFrame : IStackFrame
    {
        public int Id { get; }

        public ISource Source { get; }

        public int Line { get; }

        public int Column { get; }

        public string Name { get; }

        public StackFrame(int id, string name, ISource source, int line, int column)
        {
            Id = id;
            Name = name;
            Source = source;
            Line = line;
            Column = column;
        }
    }

    public sealed class Scope : IScope
    {
        public string Name { get; }

        public int VariablesReference { get; }

        public bool Expensive { get; }

        public Scope(string name, int variablesReference, bool expensive = false)
        {
            Name = name;
            VariablesReference = variablesReference;
            Expensive = expensive;
        }
    }

    public sealed class Variable : IVariable
    {
        public string Name { get; }

        public string Value { get; }

        public int VariablesReference { get; }

        public Variable(string name, string value, int variablesReference = 0)
        {
            Name = name;
            Value = value;
            VariablesReference = variablesReference;
        }
    }

    public sealed class Thread : IThread
    {
        public int Id { get; }

        public string Name { get; }

        public Thread(int id, string name)
        {
            Id = id;
            Name = (name == null || name.Length == 0)
                ? string.Format(CultureInfo.InvariantCulture, "Thread #{0}", id)
                : name;
        }
    }

    public sealed class Source : ISource
    {
        public string Name { get; }

        public string Path { get; }

        public int SourceReference { get; }

        public string Origin { get { return null; } }

        public object AdapterData { get { return null; } }

        [JsonConstructor]
        public Source(string name, string path, int sourceReference = 0)
        {
            Name = name;
            Path = path;
            SourceReference = sourceReference;
        }

        public Source(string path, int sourceReference = 0)
            : this(System.IO.Path.GetFileName(path), path, sourceReference)
        {
        }
    }

    public sealed class Breakpoint : IBreakpoint
    {
        public int? Id { get; }

        public bool Verified { get; }

        public string Message { get; }

        public ISource Source { get; }

        public int Line { get; }

        public int? Column { get; }

        public Breakpoint(int? id, bool verified, string message, ISource source, int line, int? column)
        {
            Id = id;
            Verified = verified;
            Message = message;
            Source = source;
            Line = line;
            Column = column;
        }

        public Breakpoint(bool verified, int line)
            : this(null, verified, null, null, line, null)
        {
        }
    }

    public sealed class SourceBreakpoint : ISourceBreakpoint
    {
        public int? Column { get; }

        public string Condition { get; }

        public int Line { get; }

        [JsonConstructor]
        public SourceBreakpoint(int line, int? column, string condition)
        {
            Line = line;
            Column = column;
            Condition = condition;
        }
    }

    public sealed class FunctionBreakpoint : IFunctionBreakpoint
    {
        public string Name { get; }

        public string Condition { get; }

        [JsonConstructor]
        public FunctionBreakpoint(string name, string condition)
        {
            Name = name;
            Condition = condition;
        }
    }

    public sealed class ExceptionBreakpointsFilter : IExceptionBreakpointsFilter
    {
        public string Filter { get; }

        public string Label { get; }

        public bool Default { get; }

        public ExceptionBreakpointsFilter(string filter, string label, bool @default)
        {
            Filter = filter;
            Label = label;
            Default = @default;
        }
    }
}
