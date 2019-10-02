// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Debugger;
using BuildXL.FrontEnd.Script.Evaluator;
using VSCode.DebugAdapter;
using VSCode.DebugProtocol;

namespace Test.DScript.Debugger
{
    public sealed class MockService : IService
    {
        public MockDebugger Debugger { get; }

        public MockService(PathTable pathTable, LoggingContext loggingContext)
        {
            Debugger = new MockDebugger(pathTable, loggingContext);
        }

        public Task<IDebugger> StartAsync()
        {
            return Task.FromResult<IDebugger>(Debugger);
        }

        public void ShutDown() { }
    }

    public sealed class MockDebugger : IDebugger
    {
        public const int ReceiveEventTimeout = 300000; // 5 minutes

        private readonly Queue m_queuedUpEvents = new Queue();
        private readonly IList m_allReceivedEvents = new ArrayList();
        private readonly object m_lock = new object();

        private bool m_stopped = false;

        public DebuggerState State { get; }

        public DebugSession Session { get; }

        public bool Stopped => m_stopped;

        public IDecorator<EvaluationResult> Decorator { get; }

        ISession IDebugger.Session => Session;

        public MockDebugger(PathTable pathTable, LoggingContext loggingContext)
        {
            State = new DebuggerState(pathTable, loggingContext, DScriptDebugerRenderer.Render, new DScriptExprEvaluator(loggingContext));
            Session = new DebugSession(State, null, this);
            Decorator = new EvaluationDecorator(this, State, false);
        }

        public void SendEvent<T>(IEvent<T> e)
        {
            lock (m_lock)
            {
                m_allReceivedEvents.Add(e);
                m_queuedUpEvents.Enqueue(e);
                Monitor.PulseAll(m_lock);
            }
        }

        public Task<T> ReceiveEvent<T>(int timeoutMilliseconds = ReceiveEventTimeout)
        {
            return Task.Run<T>(() =>
            {
                lock (m_lock)
                {
                    while (true)
                    {
                        // search
                        while (m_queuedUpEvents.Count > 0)
                        {
                            var ev = m_queuedUpEvents.Dequeue();
                            if (ev is T)
                            {
                                return (T)ev;
                            }
                        }

                        // not found && already stopped --> throw EventNotReceivedException
                        if (m_stopped)
                        {
                            throw EventNotReceivedException.Stopped(typeof(T), EventNotReceivedReason.DebuggerFinished);
                        }

                        // not found && not stopped --> wait
                        bool timedOut = !Monitor.Wait(m_lock, timeoutMilliseconds);

                        if (timedOut)
                        {
                            ShutDown();
                            throw EventNotReceivedException.Timeout(typeof(T), EventNotReceivedReason.TimedOut);
                        }
                    }
                }
            });
        }

        public void ShutDown()
        {
            lock (m_lock)
            {
                m_stopped = true;
                Session.Disconnect(new DisconnectCommand());
                SendEvent(new TerminatedEvent());
            }
        }

        internal ICapabilities SendRequest(InitializeCommand cmd)
        {
            Session.Initialize(cmd);
            return cmd.Result;
        }

        internal ISetBreakpointsResult SendRequest(SetBreakpointsCommand cmd)
        {
            Session.SetBreakpoints(cmd);
            return cmd.Result;
        }

        internal IAttachResult SendRequest(AttachCommand cmd)
        {
            Session.Attach(cmd);
            return cmd.Result;
        }

        internal IThreadsResult SendRequest(ThreadsCommand cmd)
        {
            Session.Threads(cmd);
            return cmd.Result;
        }

        internal IEvaluateResult Evaluate(string expr, int? frameId, string context = "repl")
        {
            var cmd = new EvaluateCommand(expr, context, frameId);
            Session.Evaluate(cmd);
            return cmd.Result;
        }

        internal IContinueResult Continue(int? threadId = null)
        {
            var cmd = new ContinueCommand(threadId);
            Session.Continue(cmd);
            return cmd.Result;
        }

        internal INextResult Next(int threadId)
        {
            var cmd = new NextCommand(threadId);
            Session.Next(cmd);
            return cmd.Result;
        }

        internal IStepInResult StepIn(int threadId)
        {
            var cmd = new StepInCommand(threadId);
            Session.StepIn(cmd);
            return cmd.Result;
        }

        internal IStepOutResult StepOut(int threadId)
        {
            var cmd = new StepOutCommand(threadId);
            Session.StepOut(cmd);
            return cmd.Result;
        }

        internal IStackTraceResult SendRequest(StackTraceCommand cmd)
        {
            Session.StackTrace(cmd);
            return cmd.Result;
        }

        internal IScopesResult SendRequest(ScopesCommand cmd)
        {
            Session.Scopes(cmd);
            return cmd.Result;
        }

        internal IVariablesResult SendRequest(VariablesCommand cmd)
        {
            Session.Variables(cmd);
            return cmd.Result;
        }

        internal IDisconnectResult SendRequest(DisconnectCommand cmd)
        {
            Session.Disconnect(cmd);
            return cmd.Result;
        }
    }
}
