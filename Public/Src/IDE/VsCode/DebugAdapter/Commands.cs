// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using VSCode.DebugProtocol;

#pragma warning disable 1591 // disabling warning about missing API documentation; TODO: Remove this line and write documentation!
#pragma warning disable SA1649 // File name must match first type name

namespace VSCode.DebugAdapter
{
    // ===========================================================================================
    // === COMMAND CLASSES
    //
    // All of the non-abstract command classes must be 'JSON deserializable', as mandated by
    // <see cref="JsonConvert.DeserializeObject{T}(string)"/>.  In short, each such class must
    // have a public constructor annotated with <see cref="JsonConstructor"/> whose formal
    // parameter names correspond to JSON property names.
    // ===========================================================================================
    public abstract class CommandBase
    {
        private Action<bool, object> m_sendResponseAction = null;

        public Action<bool, object> SendResponse
        {
            get { return m_sendResponseAction ?? new Action<bool, object>((b, o) => { }); }
            set { m_sendResponseAction = value; }
        }
    }

    public abstract class Command<TResult> : CommandBase, ICommand<TResult>
    {
        public TResult Result { get; private set; }

        public ErrorResult ErrorResult { get; private set; }

        public void SendErrorResult(int id, string format, bool showUser = true, bool sendTelemetry = false, string url = null, string urlLabel = null)
        {
            ErrorResult = new ErrorResult(new Message(id, format, showUser, sendTelemetry, url, urlLabel));
            SendResponse(false, ErrorResult);
        }

        public void SendResult(TResult result = default(TResult))
        {
            Result = result;
            SendResponse(true, result);
        }
    }

    public sealed class ContinueCommand : Command<IContinueResult>, IContinueCommand
    {
        public int? ThreadId { get; }

        [JsonConstructor]
        public ContinueCommand(int? threadId)
        {
            ThreadId = threadId;
        }
    }

    public sealed class EvaluateCommand : Command<IEvaluateResult>, IEvaluateCommand
    {
        public string Expression { get; }

        public string Context { get; }

        public int? FrameId { get; }

        [JsonConstructor]
        public EvaluateCommand(string expression, string context, int? frameId = null)
        {
            Expression = expression;
            Context = context;
            FrameId = frameId;
        }
    }

    public sealed class InitializeCommand : Command<ICapabilities>, IInitializeCommand
    {
        public string AdapterID { get; }

        public bool ColumnsStartAt1 { get; }

        public bool LinesStartAt1 { get; }

        public string PathFormat { get; }

        [JsonConstructor]
        public InitializeCommand(string adapterId, bool columnsStartAt1, bool linesStartAt1, string pathFormat)
        {
            AdapterID = adapterId;
            ColumnsStartAt1 = columnsStartAt1;
            LinesStartAt1 = linesStartAt1;
            PathFormat = pathFormat;
        }
    }

    public sealed class AttachCommand : Command<IAttachResult>, IAttachCommand { }

    public sealed class LaunchCommand : Command<ILaunchResult>, ILaunchCommand
    {
        public bool NoDebug { get; }

        [JsonConstructor]
        public LaunchCommand(bool noDebug)
        {
            NoDebug = noDebug;
        }
    }

    public sealed class NextCommand : Command<INextResult>, INextCommand
    {
        public int ThreadId { get; }

        [JsonConstructor]
        public NextCommand(int threadId)
        {
            ThreadId = threadId;
        }
    }

    public sealed class PauseCommand : Command<IPauseResult>, IPauseCommand
    {
        public int ThreadId { get; }

        [JsonConstructor]
        public PauseCommand(int threadId)
        {
            ThreadId = threadId;
        }
    }

    public sealed class ScopesCommand : Command<IScopesResult>, IScopesCommand
    {
        public int FrameId { get; }

        [JsonConstructor]
        public ScopesCommand(int frameId)
        {
            FrameId = frameId;
        }
    }

    public sealed class SetBreakpointsCommand : Command<ISetBreakpointsResult>, ISetBreakpointsCommand
    {
        public IReadOnlyList<ISourceBreakpoint> Breakpoints { get; }

        public ISource Source { get; }

        public SetBreakpointsCommand(ISource source, ISourceBreakpoint[] breakpoints)
        {
            Source = source;
            Breakpoints = breakpoints;
        }

        [JsonConstructor]
        public SetBreakpointsCommand(Source source, SourceBreakpoint[] breakpoints)
            : this((ISource)source, (ISourceBreakpoint[])breakpoints)
        { }
    }

    public sealed class SetExceptionBreakpointsCommand : Command<ISetExceptionBreakpointsResult>, ISetExceptionBreakpointsCommand
    {
        public IReadOnlyList<string> Filters { get; }

        [JsonConstructor]
        public SetExceptionBreakpointsCommand(string[] filters)
        {
            Filters = filters;
        }
    }

    public sealed class SetFunctionBreakpointsCommand : Command<ISetFunctionBreakpointsResult>, ISetFunctionBreakpointsCommand
    {
        public IReadOnlyList<IFunctionBreakpoint> Breakpoints { get; }

        public SetFunctionBreakpointsCommand(IFunctionBreakpoint[] breakpoints)
        {
            Breakpoints = breakpoints;
        }

        [JsonConstructor]
        public SetFunctionBreakpointsCommand(FunctionBreakpoint[] breakpoints)
            : this((IFunctionBreakpoint[])breakpoints)
        { }
    }

    public sealed class SourceCommand : Command<ISourceResult>, ISourceCommand
    {
        public int SourceReference { get; }

        [JsonConstructor]
        public SourceCommand(int sourceReference)
        {
            SourceReference = sourceReference;
        }
    }

    public sealed class StackTraceCommand : Command<IStackTraceResult>, IStackTraceCommand
    {
        public int ThreadId { get; }

        public int? StartFrame { get; }

        public int? Levels { get; }

        [JsonConstructor]
        public StackTraceCommand(int threadId, int? startFrame, int? levels)
        {
            ThreadId = threadId;
            StartFrame = startFrame;
            Levels = levels;
        }
    }

    public sealed class StepInCommand : Command<IStepInResult>, IStepInCommand
    {
        public int ThreadId { get; }

        [JsonConstructor]
        public StepInCommand(int threadId)
        {
            ThreadId = threadId;
        }
    }

    public sealed class StepOutCommand : Command<IStepOutResult>, IStepOutCommand
    {
        public int ThreadId { get; }

        [JsonConstructor]
        public StepOutCommand(int threadId)
        {
            ThreadId = threadId;
        }
    }

    public sealed class VariablesCommand : Command<IVariablesResult>, IVariablesCommand
    {
        public int VariablesReference { get; }

        [JsonConstructor]
        public VariablesCommand(int variablesReference)
        {
            VariablesReference = variablesReference;
        }
    }

    public sealed class DisconnectCommand : Command<IDisconnectResult>, IDisconnectCommand { }

    public sealed class ThreadsCommand : Command<IThreadsResult>, IThreadsCommand { }

    public sealed class ConfigurationDoneCommand : Command<IConfigurationDoneResult>, IConfigurationDoneCommand { }

    public sealed class CompletionsCommand : Command<ICompletionsResult>, ICompletionsCommand
    {
        public int Column { get; }

        public int? FrameId { get; }

        public int? Line { get; }

        public string Text { get; }

        [JsonConstructor]
        public CompletionsCommand(int column, int? frameId, int? line, string text)
        {
            Column = column;
            FrameId = frameId;
            Line = line;
            Text = text;
        }
    }

    // ===========================================================================================
    // === RESULT CLASSES
    //
    // These result classes need not be 'JSON deserializable', as they are never received from
    // the client debugger.  They are, however, sent to the the client debugger, so they must be
    // 'JSON serializable'.
    // ===========================================================================================
    public sealed class Capabilities : ICapabilities
    {
        public bool SupportsConfigurationDoneRequest { get; }

        public bool SupportsFunctionBreakpoints { get; }

        public bool SupportsConditionalBreakpoints { get; }

        public bool SupportsEvaluateForHovers { get; }

        public IReadOnlyList<IExceptionBreakpointsFilter> ExceptionBreakpointFilters { get; }

        public bool SupportsCompletionsRequest { get; }

        public Capabilities(
            bool supportsConfigurationDoneRequest,
            bool supportsFunctionBreakpoints,
            bool supportsConditionalBreakpoints,
            bool supportsEvaluateForHovers,
            bool supportsCompletionsRequest,
            IReadOnlyList<IExceptionBreakpointsFilter> exceptionBreakpointFilters = null)
        {
            SupportsConfigurationDoneRequest = supportsConfigurationDoneRequest;
            SupportsFunctionBreakpoints = supportsFunctionBreakpoints;
            SupportsConditionalBreakpoints = supportsConditionalBreakpoints;
            SupportsEvaluateForHovers = supportsEvaluateForHovers;
            SupportsCompletionsRequest = supportsCompletionsRequest;
            ExceptionBreakpointFilters = exceptionBreakpointFilters ?? new IExceptionBreakpointsFilter[0];
        }
    }

    public sealed class ErrorResult : IErrorResult
    {
        public IMessage Error { get; }

        public ErrorResult(IMessage error)
        {
            Error = error;
        }
    }

    public sealed class StackTraceResult : IStackTraceResult
    {
        public IReadOnlyList<IStackFrame> StackFrames { get; }

        public int? TotalFrames { get; }

        public StackTraceResult(List<IStackFrame> frames = null, int? totalFrames = null)
        {
            StackFrames = frames == null ? new StackFrame[0] : frames.ToArray();
            TotalFrames = totalFrames;
        }
    }

    public sealed class ScopesResult : IScopesResult
    {
        public IReadOnlyList<IScope> Scopes { get; }

        public ScopesResult(List<Scope> scps = null)
        {
            Scopes = (scps == null) ? new Scope[0] : scps.ToArray();
        }
    }

    public sealed class VariablesResult : IVariablesResult
    {
        public IReadOnlyList<IVariable> Variables { get; }

        public VariablesResult(List<IVariable> vars = null)
        {
            Variables = (vars == null) ? new IVariable[0] : vars.ToArray();
        }
    }

    public sealed class ThreadsResult : IThreadsResult
    {
        public IReadOnlyList<IThread> Threads { get; }

        public ThreadsResult(List<Thread> vars = null)
        {
            Threads = (vars == null) ? new Thread[0] : vars.ToArray();
        }
    }

    public sealed class ContinueResult : IContinueResult
    {
        public bool AllThreadsContinued { get; }

        public ContinueResult(bool allThreadsContinued = true)
        {
            AllThreadsContinued = allThreadsContinued;
        }
    }

    public sealed class EvaluateResult : IEvaluateResult
    {
        public string Result { get; }

        public int VariablesReference { get; }

        public EvaluateResult(string result, int variablesReference = 0)
        {
            Result = result;
            VariablesReference = variablesReference;
        }
    }

    public sealed class SetBreakpointsResult : ISetBreakpointsResult
    {
        public IReadOnlyList<IBreakpoint> Breakpoints { get; }

        public SetBreakpointsResult(List<IBreakpoint> bpts = null)
        {
            Breakpoints = (bpts == null) ? new Breakpoint[0] : bpts.ToArray();
        }
    }

    public sealed class CompletionsResult : ICompletionsResult
    {
        public IReadOnlyList<ICompletionItem> Targets { get; }

        public CompletionsResult(List<ICompletionItem> targets)
        {
            Targets = targets;
        }
    }

    public sealed class CompletionItem : ICompletionItem
    {
        public string Label { get; }

        public string Text { get; }

        public CompletionItemType Type { get; }

        public int? Start { get; }

        public int? Length { get; }

        public CompletionItem(string label, string text, CompletionItemType type, int? start = null, int? length = null)
        {
            Label = label;
            Text = text;
            Type = type;
            Start = start;
            Length = length;
        }
    }
}
