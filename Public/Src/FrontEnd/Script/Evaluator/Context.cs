// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Runtime.CompilerServices;
using System.Threading;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Tracing;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Sdk;
using BuildXL.Pips;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using JetBrains.Annotations;
using Closure = BuildXL.FrontEnd.Script.Values.Closure;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

#pragma warning disable SA1649 // File name must match first type name

namespace BuildXL.FrontEnd.Script.Evaluator
{
    /// <summary>
    /// Context for evaluation.
    /// </summary>
    /// <remarks>
    /// On top of the immutable base class, this class exposes all context mutation operations, mostly methods that manipulate the CallStack.
    /// Conceptually, one context is associated with one BuildXL-evaluation thread (not system-thread).
    /// Note that no mutation operations are allowed on this writer for as long as any created children writers have not yet been disposed.
    /// </remarks>
    public sealed class Context : ImmutableContextBase, ILogMessageObserver
    {
        private bool m_skipDecorator;

        /// <summary>
        /// We store discarded stack entries in a separate stack for future reuse.
        /// </summary>
        private StackEntry m_freeStackEntries;

        private readonly Logger m_logger;

        /// <summary>
        /// If LoggerOverride is not null, it will be used as the logger instead of the one provided by the context tree
        /// </summary>
        /// <remarks>
        /// Used by <see cref="SnippetEvaluationContext" /> to temporarily override the logger. Not intended to be used directly.
        /// </remarks>
        internal Logger LoggerOverride { get; set; }

        /// <inheritdoc />
        public override Logger Logger => LoggerOverride ?? m_logger;

        /// <summary>
        /// When true, the evaluation do not use the decorator, even if there is one set.
        /// </summary>
        public bool SkipDecorator
        {
            get => m_skipDecorator;

            set
            {
                if (value)
                {
                    HasDecorator = false;
                }
                else
                {
                    HasDecorator = ContextTree.Decorator != null;
                }

                m_skipDecorator = value;
            }
        }

        /// <summary>
        /// Returns true if decorator should be used during evaluation.
        /// </summary>
        public bool HasDecorator { get; private set; }

        /// <inheritdoc />
        public override IDecorator<EvaluationResult> Decorator => SkipDecorator ? null : ContextTree.Decorator;

        /// <summary>
        /// Property provenance.
        /// </summary>
        public LazyLocationData PropertyProvenance { get; private set; }

        /// <summary>
        /// Lock to synchronize lazy initialization of the pip construction helper
        /// </summary>
        private readonly object m_pipConstructionHelperLock = new object();

        /// <summary>
        /// PipConstruction helper
        /// </summary>
        private readonly Lazy<PipConstructionHelper> m_pipConstructionHelper;

        internal Context(
            ContextTree contextTree,
            ImmutableContextBase parent,
            ModuleLiteral module,
            TopLevelValueInfo topLevelValueInfo,
            FileType fileType,
            EvaluatorConfiguration configuration,
            [NotNull]IEvaluationScheduler evaluationScheduler)
            : base(contextTree, parent, module, topLevelValueInfo, fileType, configuration, evaluationScheduler)
        {
            Contract.Requires(contextTree.IsValid);
            Contract.Requires(parent == null || parent.ContextTree == contextTree);
            Contract.Requires(module != null);

            if (ContextTree.Decorator != null)
            {
                // When subscribing to messages from the logger, it is important that each context has a its own unique logger,
                // because otherwise each context would also receive messages that are pertinent to other contexts.
                m_logger = Logger.CreateLogger(ContextTree.Logger.PreserveLogEvents, ContextTree.Logger);
                Logger.AddObserver(this);
            }
            else
            {
                m_logger = ContextTree.Logger;
            }

            // Only add child after all preconditions have been checked; otherwise, bad things happen, as the Dispose() method will be angry
            parent?.AddChild();

            PropertyProvenance = LazyLocationData.Invalid;
            HasDecorator = ContextTree.Decorator != null;
            Interlocked.Increment(ref Statistics.Contexts);

            m_pipConstructionHelper = new Lazy<PipConstructionHelper>(() =>
            {
                var qualifierId = LastActiveModuleQualifier.QualifierId;
                var moduelIdString = Package.Id.ToString(FrontEndContext.StringTable);

                return PipConstructionHelper.Create(
                    FrontEndContext,
                    FrontEndHost.Engine.Layout.ObjectDirectory,
                    FrontEndHost.Engine.Layout.RedirectedDirectory,
                    FrontEndHost.Engine.Layout.TempDirectory,
                    ContextTree.FrontEndHost.PipGraph,
                    Package.ModuleId,
                    moduelIdString,

                    // This is a BIG HACK that still needs to be fixed. Since office doesn't generate stable spec files, i.e. a new value can
                    // rearrange all the values accross specs, we removed the spec from the SemiStableHash and Hash for unique output folders
                    // This was okay for exported values, but this is NOT true for private values. Here we now have a possible case where two
                    // private const will collide on either the output directory, or they will share semistable hash between one another causing
                    // issues with execution time prediction and user confusions in the logs. The right fix here is to include the spec relative
                    // path for private values, but that still needs to be plumbed through.
                    RelativePath.Invalid,
                    
                    TopLevelValueInfo.ValueName,
                    new LocationData(
                        TopLevelValueInfo.SpecFile,
                        TopLevelValueInfo.ValueDeclarationLineInfo.Line,
                        TopLevelValueInfo.ValueDeclarationLineInfo.Position),
                    qualifierId);
            });
        }

        /// <inheritdoc />
        void ILogMessageObserver.OnMessage(Diagnostic diagnostic)
        {
            Decorator.NotifyDiagnostics(this, diagnostic);
        }

        /// <summary>
        /// If a <see cref="DebugInfo" /> object is attached to top stack, calls
        /// <see cref="DebugInfo.SetLocalVarName" /> to set the name of the variable at a given index.
        /// </summary>
        public void SetLocalVarName(int index, SymbolAtom name)
        {
            if (IsBeingDebugged)
            {
                m_callStack?.DebugInfo?.SetLocalVarName(index, name);
            }
        }

        /// <summary>
        /// Gets the top of the stack for use as part of an ambient function evaluation.
        /// </summary>
        public StackEntry TopStack
        {
            get
            {
                Contract.RequiresDebug(!IsDisposed);
                Contract.RequiresDebug(CallStackSize > 0);
                Contract.Ensures(Contract.Result<StackEntry>() != null);

                return m_callStack;
            }
        }

        /// <summary>
        /// Pushes a stack entry and returns a token that is needed for popping.
        /// </summary>
        private object Push(StackEntry stackEntry)
        {
            Contract.RequiresDebug(!IsDisposed);
            Contract.RequiresDebug(!HasChildren);

            // Nested child contexts shared the stack entries with us, so no mutations are allowed for as long as there are undisposed children!
            Contract.RequiresDebug(stackEntry != null);

            stackEntry.Link(ref m_callStack);
            return stackEntry;
        }

        /// <nodoc />
        public PopDisposable PushStackEntry(FunctionLikeExpression lambda, ModuleLiteral targetEnv, ModuleLiteral currentEnv, LineInfo location, EvaluationStackFrame frame)
        {
            var debugInfo = DebugInfo.Create(this, currentEnv, frame);
            var entry = CreateStackEntry(lambda, targetEnv, ambientCall: null, path: currentEnv.Path, location: location, debugInfo: debugInfo);
            return new PopDisposable(Push(entry), this);
        }

        /// <summary>
        /// Disposable struct that pops the evaluation stack in Dispose method.
        /// </summary>
        public readonly struct PopDisposable : IDisposable
        {
            private readonly Context m_context;
            private readonly object m_marker;

            /// <nodoc />
            public PopDisposable(object marker, Context context)
            {
                m_marker = marker;
                m_context = context;
            }

            /// <inheritdoc />
            public void Dispose()
            {
                m_context.Pop(m_marker);
            }
        }

        /// <summary>
        /// Invokes a given closure with a given frame.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public EvaluationResult InvokeClosure(Closure closure, EvaluationStackFrame frame)
        {
            using (PrepareStackEntry(closure, frame))
            {
                return closure.Function.Invoke(this, closure.Env, frame);
            }
        }

        private PopDisposable PrepareStackEntry(Closure closure, EvaluationStackFrame frame)
        {
            var entry = CreateStackEntry(closure.Function, closure.Env, null, m_callStack.Path, m_callStack.InvocationLocation, DebugInfo.Create(this, null, frame));
            return new PopDisposable(Push(entry), this);
        }

        /// <nodoc />
        public PopDisposable PrepareStackEntryForAmbient(ApplyExpression ambientCall, ModuleLiteral currentEnv, EvaluationStackFrame frame)
        {
            var debugInfo = DebugInfo.Create(this, currentEnv, frame);

            var entry = CreateStackEntry(lambda: null, closureEnv: null, ambientCall: ambientCall, path: currentEnv.Path, location: ambientCall.Location, debugInfo: debugInfo);
            return new PopDisposable(Push(entry), this);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private StackEntry CreateStackEntry(FunctionLikeExpression lambda, ModuleLiteral closureEnv, ApplyExpression ambientCall, AbsolutePath path, LineInfo location, DebugInfo debugInfo)
        {
            var stackEntry = m_freeStackEntries != null ? StackEntry.Unlink(ref m_freeStackEntries) : new StackEntry();
            return stackEntry.Initialize(lambda, closureEnv, ambientCall, path, location, debugInfo);
        }

        /// <summary>
        /// Pops a stack entry, given a token obtained when pushing.
        /// </summary>
        private void Pop(object token)

        {
            Contract.RequiresDebug(!IsDisposed);
            Contract.RequiresDebug(!HasChildren);

            // Nested child contexts shared the stack entries with us, so no mutations are allowed for as long as there are undisposed children!
            Contract.RequiresDebug(CallStackSize > ParentCallStackSize);
            Contract.Assume(m_callStack == token || EvaluationScheduler.CancellationToken.IsCancellationRequested); // pops must correspond to pushes

            var freedStackEntry = StackEntry.Unlink(ref m_callStack);
            freedStackEntry.Clear();
            freedStackEntry.Link(ref m_freeStackEntries);
        }

        /// <summary>
        /// Sets property provenance.
        /// </summary>
        public void SetPropertyProvenance(AbsolutePath path, LineInfo location)
        {
            PropertyProvenance = new LazyLocationData(location, path);
        }

        /// <summary>
        /// Unsets property provenance.
        /// </summary>
        public void UnsetPropertyProvenance()
        {
            PropertyProvenance = LazyLocationData.Invalid;
        }

        internal void RegisterStatistic(FunctionStatistic functionStatistic)
        {
            Statistics.FunctionInvocationStatistics.Add(functionStatistic);
        }

        /// <summary>
        /// Returns the helper to create pips
        /// </summary>
        public PipConstructionHelper GetPipConstructionHelper()
        {
            Contract.Assume(TopLevelValueInfo != null, "Can only call this when accessing from top-level value");
            Contract.Assume(LastActiveUsedModule != null, "Can only call this when accessing from top-level value which implies a module");

            return m_pipConstructionHelper.Value;
        }
    }
}
