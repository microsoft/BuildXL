// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities;
using BuildXL.Utilities.Tracing;
using VSCode.DebugAdapter;
using VSCode.DebugProtocol;
using VSThread = VSCode.DebugAdapter.Thread;

namespace BuildXL.FrontEnd.Script.Debugger
{
    /// <summary>
    /// This class is responsible for, and only for, handling requests from the client debugger.
    ///
    /// Therefore, the invariant is that all the protocol methods may only be accessed from a single
    /// thread.  Thus, all the private state of this class (e.g., (<code cref="m_scopeHandles"/>)
    /// needs not be thread-safe, nor is any synchronization needed when accessing it.
    ///
    /// The constructor of this class receives a shared debugger state (<code cref="m_state"/>),
    /// which is assumed to be thread-safe.
    ///
    /// A debug session, representing a single client debugger issuing requests to the DScript back end.
    ///
    /// Weakly immutable.
    /// </summary>
    public sealed class DebugSession : ISession
    {
        // private state that no one outside of this class can access
        private readonly Handles<FrameContext> m_scopeHandles = new Handles<FrameContext>();
        private readonly Barrier m_sessionInitializedBarrier = new Barrier();
        private readonly Renderer m_renderer;
        private readonly ExpressionEvaluator m_expressionEvaluator;

        // shared state, received via the constructor.
        private readonly DebuggerState m_state;
        private readonly PathTranslator m_buildXLToUserPathTranslator;
        private readonly PathTranslator m_userToBuildXLPathTranslator;

        internal DebuggerState State => m_state;

        /// <summary>Connected debugger.</summary>
        public IDebugger Debugger { get; }

        /// <nodoc/>
        public DebugSession(DebuggerState state, PathTranslator buildXLToUserPathTranslator, IDebugger debugger)
        {
            m_state = state;
            m_buildXLToUserPathTranslator = buildXLToUserPathTranslator;
            m_userToBuildXLPathTranslator = buildXLToUserPathTranslator?.GetInverse();
            Debugger = debugger;
            m_renderer = new Renderer(state);
            m_expressionEvaluator = new ExpressionEvaluator(state);
        }

        /// <summary>
        ///     A barrier for waiting until the debug session has been initialized
        ///     (the <code cref="Attach"/> request has been received).
        /// </summary>
        public void WaitSessionInitialized()
        {
            m_sessionInitializedBarrier.Wait();
        }

        // ===========================================================================================
        // === DEBUG PROTOCOL METHODS ================================================================
        // ===========================================================================================

        /// <inheritdoc/>
        public void Initialize(IInitializeCommand cmd)
        {
            Debugger.SendEvent(new InitializedEvent());

            var capabilities = new Capabilities(
                supportsConfigurationDoneRequest: true,
                supportsConditionalBreakpoints: false,
                supportsEvaluateForHovers: false,
                supportsFunctionBreakpoints: false,
                supportsCompletionsRequest: true);

            cmd.SendResult(capabilities);
        }

        /// <inheritdoc/>
        public void SetBreakpoints(ISetBreakpointsCommand cmd)
        {
            var sourceBreakpoints = cmd.Breakpoints;
            var source = TranslateUserPath(Path.GetFullPath(cmd.Source.Path));

            var sourcePath = AbsolutePath.Create(m_state.PathTable, source);
            var breakpoints = m_state.MasterBreakpoints.Set(sourcePath, sourceBreakpoints);

            cmd.SendResult(new SetBreakpointsResult(breakpoints.ToList()));
        }

        /// <inheritdoc/>
        public void ConfigurationDone(IConfigurationDoneCommand cmd)
        {
            cmd.SendResult(null);
        }

        /// <inheritdoc/>
        public void Attach(IAttachCommand cmd)
        {
            cmd.SendResult(null);
            m_sessionInitializedBarrier.Signal();
        }

        /// <inheritdoc/>
        public void Continue(IContinueCommand cmd)
        {
            int? threadId = cmd.ThreadId;
            if (threadId != null)
            {
                // unblock single thread
                var evalState = m_state.RemoveStoppedThread(threadId.Value);
                cmd.SendResult(new ContinueResult(allThreadsContinued: false));
                evalState.Resume();
            }
            else
            {
                // unblock all
                cmd.SendResult(new ContinueResult(allThreadsContinued: true));
                foreach (var kvp in m_state.ClearStoppedThreads())
                {
                    var evalState = kvp.Value;
                    evalState.Resume();
                }
            }
        }

        /// <inheritdoc/>
        public void Threads(IThreadsCommand cmd)
        {
            var threads = m_state.GetStoppedThreadsClone().Select(kvp => new VSThread(kvp.Key, kvp.Value.ThreadName())).ToList();
            cmd.SendResult(new ThreadsResult(threads));
        }

        /// <inheritdoc/>
        public void StackTrace(IStackTraceCommand cmd)
        {
            var threadState = m_state.GetThreadState(cmd.ThreadId);
            int startFrame = cmd.StartFrame ?? 0;
            int maxNumberOfFrames = cmd.Levels ?? int.MaxValue;
            var frames = threadState.StackTrace.Skip(startFrame).Take(maxNumberOfFrames).Select((entry, idx) =>
            {
                var functionName = entry.FunctionName;
                var source = new Source(TranslateBuildXLPath(entry.File));
                var id = m_scopeHandles.Create(new FrameContext(cmd.ThreadId, startFrame + idx, threadState));
                return (IStackFrame)new StackFrame(id, functionName, source, entry.Line, entry.Position);
            }).ToList();

            cmd.SendResult(new StackTraceResult(frames));
        }

        /// <inheritdoc/>
        public void Scopes(IScopesCommand cmd)
        {
            var frameRef = m_scopeHandles.Get(cmd.FrameId, null);
            var scopeContexts = frameRef.ThreadState.GetSupportedScopes(frameRef.FrameIndex);
            var scopes = scopeContexts.Select(m_renderer.CreateScope).ToList();
            cmd.SendResult(new ScopesResult(scopes));
        }

        /// <inheritdoc/>
        public void Variables(IVariablesCommand cmd)
        {
            var vars = m_renderer.GetVariablesForScope(cmd.VariablesReference);
            cmd.SendResult(new VariablesResult(vars.ToList()));
        }

        /// <inheritdoc/>
        public void Disconnect(IDisconnectCommand cmd)
        {
            cmd.SendResult(null);
            m_state.StopDebugging();
            m_sessionInitializedBarrier.Signal();
        }

        /// <inheritdoc/>
        public void Evaluate(IEvaluateCommand cmd)
        {
            if (!cmd.FrameId.HasValue)
            {
                SendErrorEvaluationInGlobalScopeNotSupported(cmd);
                return;
            }

            var frameRef = m_scopeHandles.Get(cmd.FrameId.Value, null);
            var ans = m_expressionEvaluator.EvaluateExpression(frameRef, cmd.Expression);
            if (ans.Succeeded)
            {
                ObjectContext objContext = ans.Result;
                var variable = m_renderer.ObjectToVariable(objContext.Context, value: objContext.Object, variableName: null);
                cmd.SendResult(new EvaluateResult(variable.Value, variable.VariablesReference));
            }
            else
            {
                SendErrorEvaluationFailed(cmd, ans.Failure.Describe());
            }
        }

        /// <inheritdoc/>
        public void Launch(ILaunchCommand cmd)
        {
            SendErrorNotSupported(cmd, "launch");
        }

        /// <inheritdoc/>
        public void Next(INextCommand cmd)
        {
            Step(cmd.ThreadId, cmd, DebugAction.ActionKind.StepOver);
        }

        /// <inheritdoc/>
        public void StepIn(IStepInCommand cmd)
        {
            Step(cmd.ThreadId, cmd, DebugAction.ActionKind.StepIn);
        }

        /// <inheritdoc/>
        public void StepOut(IStepOutCommand cmd)
        {
            Step(cmd.ThreadId, cmd, DebugAction.ActionKind.StepOut);
        }

        /// <inheritdoc/>
        public void Pause(IPauseCommand cmd)
        {
            SendErrorNotSupported(cmd, "'pause'");
        }

        /// <inheritdoc/>
        public void SetFunctionBreakpoints(ISetFunctionBreakpointsCommand cmd)
        {
            SendErrorNotSupported(cmd, "'set function breakpoints'");
        }

        /// <inheritdoc/>
        public void SetExceptionBreakpoints(ISetExceptionBreakpointsCommand cmd)
        {
            SendErrorNotSupported(cmd, "'set exception breakpoints'");
        }

        /// <inheritdoc/>
        public void Source(ISourceCommand cmd)
        {
            cmd.SendErrorResult(2000, "'source' not implemented");
        }

        /// <inheritdoc/>
        public void Completions(ICompletionsCommand cmd)
        {
            if (!cmd.FrameId.HasValue)
            {
                SendErrorEvaluationInGlobalScopeNotSupported(cmd);
                return;
            }

            var frameRef = m_scopeHandles.Get(cmd.FrameId.Value, null);
            var ans = m_expressionEvaluator.EvaluateExpression(frameRef, GetCompletionTextToEvaluate(cmd));

            List<ICompletionItem> items;
            if (!ans.Succeeded)
            {
                items = new List<ICompletionItem>(0);
            }
            else
            {
                var resultAsObjLiteral = ans.Result.Object as ObjectLiteral;
                var memberProps = resultAsObjLiteral != null ? Renderer.ObjectLiteralInfo(ans.Result.Context, resultAsObjLiteral).Properties : Property.Empty;
                var ambientProps = m_renderer.GetAmbientProperties(ans.Result.Context, ans.Result.Object);
                items = memberProps.Concat(ambientProps).Select(p => (ICompletionItem)new CompletionItem(p.Name, p.Name, p.Kind)).ToList();
            }

            cmd.SendResult(new CompletionsResult(items));
        }

        // ===========================================================================================
        // === PRIVATE AUXILIARY METHODS =============================================================
        // ===========================================================================================
        private void Step<T>(int threadId, ICommand<T> cmd, DebugAction.ActionKind kind, T result = default(T))
        {
            var evalState = m_state.RemoveStoppedThread(threadId);
            evalState.Resume(kind);
            cmd.SendResult(result);
        }

        private string TranslateUserPath(string path) => m_userToBuildXLPathTranslator != null ? m_userToBuildXLPathTranslator.Translate(path) : path;

        private string TranslateBuildXLPath(string path) => m_buildXLToUserPathTranslator != null ? m_buildXLToUserPathTranslator.Translate(path) : path;

        private static string GetCompletionTextToEvaluate(ICompletionsCommand cmd)
        {
            var idx = cmd.Column - 1;
            var text = idx <= cmd.Text.Length ? cmd.Text.Substring(0, idx) : cmd.Text;
            return text.TrimEnd('.');
        }

        private static void SendErrorNotSupported<T>(ICommand<T> cmd, string category) => cmd.SendErrorResult(1000, category + " not supported");

        private static void SendErrorEvaluationFailed<T>(ICommand<T> cmd, string message) => cmd.SendErrorResult(2000, "Evaluation failed: " + message);

        private static void SendErrorEvaluationInGlobalScopeNotSupported<T>(ICommand<T> cmd) => SendErrorNotSupported(cmd, "Evaluation in global scope");
    }
}
