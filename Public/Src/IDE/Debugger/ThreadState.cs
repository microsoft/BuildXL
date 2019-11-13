// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Script.Debugger
{
    /// <summary>
    ///     Base class for associating some state with a thread stopped in the debugger.
    /// </summary>
    public abstract class ThreadState
    {
        /// <summary>Corresponding thread ID.</summary>
        public int ThreadId { get; }

        /// <summary>Barrier on which to wait (and on which to signal when the thread is to be unblocked).</summary>
        private readonly Barrier m_barrier;

        /// <nodoc />
        protected ThreadState(int threadId)
        {
            ThreadId = threadId;
            m_barrier = new Barrier();
        }

        /// <summary>Returns some user-friendly name of the thread.  Subclasses may provide a more meaningful name.</summary>
        public virtual string ThreadName()
        {
            return ThreadId.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Returns the stack trace for this thread</summary>
        public abstract IReadOnlyList<DisplayStackTraceEntry> StackTrace { get; }

        /// <summary>Returns all variable scopes in the specified stack frame in this thread</summary>
        public abstract IEnumerable<ObjectContext> GetSupportedScopes(int frameIndex);

        /// <summary>Calls <see cref="Barrier.Signal"/> on the barier associated with this thread.</summary>
        public virtual void Resume(DebugAction.ActionKind kind = DebugAction.ActionKind.Continue)
        {
            m_barrier.Signal();
        }

        /// <summary>Calls <see cref="Barrier.Wait"/> on the barier associated with this thread.</summary>
        public void PauseEvaluation()
        {
            m_barrier.Wait();
        }
    }

    /// <summary>
    /// Simple memento class for saving all the state needed when a DScript evaluation thread is blocked on a breakpoint.
    /// </summary>
    /// <remarks>
    /// Immutable.
    /// </remarks>
    public sealed class EvaluationState : ThreadState
    {
        /// <summary>Node being evaluated.</summary>
        public Node Node { get; }

        /// <summary>Context of the node evaluation.</summary>
        public Context Context { get; }

        /// <summary>Environment of the node evaluation.</summary>
        public ModuleLiteral Env { get; }

        /// <summary>Arguments for the node evaluation.</summary>
        public IReadOnlyList<object> Args { get; }

        /// <summary>Backing field for the lazily initialized property <see cref="StackTrace"/>.</summary>
        private DisplayStackTraceEntry[] m_stackTrace = null;

        /// <nodoc/>
        public EvaluationState(int threadId, Node node, Context context, ModuleLiteral env, EvaluationStackFrame args)
            : base(threadId)
        {
            Node = node;
            Context = context;
            Env = env;
            
            // Debugger has to move the stack frame to evaluation heap to prolong the lifetime of the frame.
            Args = args.Detach().Frame.Select(f => f.Value).ToArray();
        }

        /// <summary>
        /// Returns a UI-friendly thread name.
        ///
        /// The thread name is formatted as <code>$"{value} ({path})"</code>, where
        ///     - 'value' is the name of the top-level DScript variable being evaluated;
        ///     - 'path' is the path of the build spec being evaluated, relative the the build root (config.dsc).
        /// </summary>
        public override string ThreadName()
        {
            var pathTable = Context.PathTable;
            var specPath = Context.LastActiveUsedPath;
            var lastUsedName = Context.TopLevelValueInfo?.ValueName.ToDisplayString(Context) ?? "<config>";

            string specPathStr;
            if (Context.FrontEndHost.PrimaryConfigFile.GetParent(pathTable).TryGetRelative(pathTable, specPath, out RelativePath specRelativePath))
            {
                specPathStr = specRelativePath.ToString(Context.StringTable);
            }
            else
            {
                specPathStr = specPath.ToString(Context.PathTable);
            }

            return string.Format(CultureInfo.InvariantCulture, "{0} ({1})", lastUsedName, specPathStr);
        }

        /// <inheritdoc />
        public override void Resume(DebugAction.ActionKind kind = DebugAction.ActionKind.Continue)
        {
            Context.DebugState.Action = new DebugAction(kind, Node, StackTrace);
            base.Resume(kind);
        }

        /// <summary>
        ///     Returns the current call stack, as set in given <paramref name="context"/>; the other two
        ///     arguments (<paramref name="env"/> and <paramref name="node"/>) are used to compute the
        ///     location of the current <paramref name="node"/>
        ///     (<seealso cref="DisplayStringHelper.GetStackTrace(in BuildXL.FrontEnd.Script.RuntimeModel.AstBridge.UniversalLocation)"/>).
        /// </summary>
        public static DisplayStackTraceEntry[] GetStackTrace(Context context, ModuleLiteral env, Node node)
        {
            return new DisplayStringHelper(context).GetStackTrace(env, node);
        }

        /// <summary>
        ///     A lazily initialized property for getting the stack trace. See also <see cref="GetStackTrace"/>.
        /// </summary>
        public override IReadOnlyList<DisplayStackTraceEntry> StackTrace => m_stackTrace ?? (m_stackTrace = GetStackTrace(Context, Env, Node));

        /// <inheritdoc />
        public override IEnumerable<ObjectContext> GetSupportedScopes(int frameIndex)
        {
            return new List<ObjectContext>
            {
                new ObjectContext(Context, new ScopeLocals(this, frameIndex)),
                new ObjectContext(Context, new ScopeCurrentModule(Env)),
                new ObjectContext(Context, new ScopePipGraph(Context.GetPipConstructionHelper().PipGraph)),
            };
        }

        internal int StackTraceSize => StackTrace.Count;

        internal DisplayStackTraceEntry GetDisplayStackEntryForFrame(int frameIndex)
        {
            if (frameIndex >= 0 && frameIndex < StackTraceSize)
            {
                return StackTrace[frameIndex];
            }
            else
            {
                throw new InvalidFrameIndexException(ThreadId, frameIndex, StackTraceSize);
            }
        }

        internal StackEntry GetStackEntryForFrame(int frameIndex)
        {
            return GetDisplayStackEntryForFrame(frameIndex).Entry;
        }

        internal EvaluationStackFrame GetArgsForFrame(int frameIndex)
        {
            var entry = GetStackEntryForFrame(frameIndex);
            if (entry == null)
            {
                return EvaluationStackFrame.Empty();
            }

            return entry.DebugInfo.Frame;
        }

        internal ModuleLiteral GetEnvForFrame(int frameIndex)
        {
            if (frameIndex == 0)
            {
                return Env;
            }

            var entryEnv = GetStackEntryForFrame(frameIndex)?.Env;
            if (entryEnv != null)
            {
                return entryEnv;
            }

            return StackTrace[frameIndex - 1].Entry?.DebugInfo.InvocationEnv;
        }
    }

    /// <summary>
    ///     Simple memento class for saving all the state needed when the DScriptHost thread is blocked after /phase:Evaluate has completed.
    /// </summary>
    /// <remarks>
    ///     Immutable.
    /// </remarks>
    public sealed class DScriptHostState : ThreadState
    {
        private readonly IReadOnlyList<ObjectContext> m_scopes;

        /// <nodoc />
        public DScriptHostState(int threadId, IEnumerable<IModuleAndContext> evaluatedModules)
            : base(threadId)
        {
            var firstContext = evaluatedModules?.FirstOrDefault()?.Tree.RootContext;
            m_scopes = firstContext != null
                ? new List<ObjectContext>
                    {
                        new ObjectContext(firstContext, new ScopeAllModules(evaluatedModules.ToArray())),

                        // new ObjectContext(firstContext, new ScopePipGraph(firstContext?.GetTransformerContext()?.Graph))
                    }
                : new List<ObjectContext>(0);
        }

        /// <inheritdoc />
        public override IReadOnlyList<DisplayStackTraceEntry> StackTrace => new[] { new DisplayStackTraceEntry("[ambient call]", -1, -1, null, null) };

        /// <inheritdoc />
        public override IEnumerable<ObjectContext> GetSupportedScopes(int frameIndex) => m_scopes;
    }
}
