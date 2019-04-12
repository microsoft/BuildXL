// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading;
using BuildXL.FrontEnd.Script.Ambients;
using BuildXL.FrontEnd.Script.RuntimeModel.AstBridge;
using BuildXL.FrontEnd.Script.Tracing;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Sdk;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using JetBrains.Annotations;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Evaluator
{
    /// <summary>
    /// Immutable class class for the context for evaluation.
    /// </summary>
    /// <remarks>
    /// This abstract context contains all (effectively) immutable aspects of the context.
    /// It also allows reading some of the mutable state, assuming that there is no concurrent writer.
    /// Writers that can be used concurrently with each other can be created using the <code>Create...</code> methods.
    /// </remarks>
    public abstract class ImmutableContextBase : IDisposable
    {
        /// <summary>
        /// Limits the size of call stack.
        /// </summary>
        public const int CallStackThreshold = 100;

        /// <summary>
        /// Shared information for all related contexts
        /// </summary>
        public ContextTree ContextTree { get; }

        #region Various properties accessible via the shared ContextTree.

        /// <summary>
        /// A decorator to be used around node evaluation (<see cref="Node.Eval(Context, ModuleLiteral, EvaluationStackFrame)" />).
        /// </summary>
        public virtual IDecorator<EvaluationResult> Decorator => ContextTree.Decorator;

        /// <summary>
        /// Configuration for fine-tuning evaluation.
        /// </summary>
        public EvaluatorConfiguration EvaluatorConfiguration { get; }

        /// <summary>
        /// Returns true if method invocation count tracking is enabled.
        /// </summary>
        public bool TrackMethodInvocationStatistics => EvaluatorConfiguration.TrackMethodInvocations;

        /// <summary>
        /// Additional state for debugging.
        /// </summary>
        public DebugState DebugState { get; }

        /// <summary>
        /// Evaluation statistics.
        /// </summary>
        public EvaluationStatistics Statistics => ContextTree.Statistics;

        /// <summary>
        /// Front end host.
        /// </summary>
        public FrontEndHost FrontEndHost => ContextTree.FrontEndHost;

        /// <summary>
        /// Constants
        /// </summary>
        public GlobalConstants Constants => ContextTree.Constants;

        /// <summary>
        /// Predefined types.
        /// </summary>
        public PredefinedTypes PredefinedTypes => Constants.PredefinedTypes;

        /// <summary>
        /// Front end context.
        /// </summary>
        public FrontEndContext FrontEndContext => ContextTree.FrontEndContext;

        /// <summary>
        /// Gets the current string table.
        /// </summary>
        public StringTable StringTable => FrontEndContext.StringTable;

        /// <summary>
        /// Gets the current path table.
        /// </summary>
        public PathTable PathTable => FrontEndContext.PathTable;

        /// <summary>
        /// Gets the logger instance.
        /// </summary>
        public virtual Logger Logger => ContextTree.Logger;

        /// <summary>
        /// Gets the context for the logging.
        /// </summary>
        public LoggingContext LoggingContext => FrontEndContext.LoggingContext;

        /// <summary>
        /// Gets whether it's being debugged.  Other components may find this useful in deciding
        /// how much information to keep around, in order to support the debugger.
        /// </summary>
        public bool IsBeingDebugged => ContextTree.IsBeingDebugged;

        /// <summary>
        /// Gets the module registry.
        /// </summary>
        public ModuleRegistry ModuleRegistry => ContextTree.ModuleRegistry;

        /// <summary>
        /// Gets the current qualifier instance.
        /// </summary>
        public QualifierValue LastActiveModuleQualifier => LastActiveUsedModule.GetFileQualifier();

        #endregion

        /// <summary>
        /// The parent context with which this context shares a part of the call stack.
        /// Child contexts are created to enable concurrent evaluation, and to mark evaluation switches to a different module.
        /// </summary>
        public ImmutableContextBase ParentContext { get; }

        /// <summary>
        /// Last active use module.
        /// </summary>
        protected readonly ModuleLiteral LastActiveUsedModule;

        /// <summary>
        /// Last active use path.
        /// </summary>
        public AbsolutePath LastActiveUsedPath
        {
            get
            {
                AbsolutePath path = LastActiveUsedModule.Path;

                Contract.Assume(path.IsValid, "The module of an evaluation context must have a path.");

                return path;
            }
        }

        /// <summary>
        /// Last active use module name.
        /// </summary>
        public string LastActiveUsedModuleName
        {
            get
            {
                return LastActiveUsedModule.Package.Id.Name.ToString(FrontEndContext.StringTable);
            }
        }

        /// <summary>
        /// Last active used package.
        /// </summary>
        public Package Package
        {
            get
            {
                Package package = LastActiveUsedModule.Package;
                Contract.Assume(package != null, "The module of an evaluation context must be owned by a package.");

                return package;
            }
        }

        /// <summary>
        /// Queue that manages pool of evaluation tasks.
        /// </summary>
        [NotNull]
        public IEvaluationScheduler EvaluationScheduler { get; }

        private RelativePath m_relativePath;

        /// <summary>
        /// Spec relative (to package root) directory path.
        /// </summary>
        public RelativePath SpecRelativePath
        {
            get
            {
                if (m_relativePath.IsValid)
                {
                    return m_relativePath;
                }

                var packageRoot = Package.Path.GetParent(FrontEndContext.PathTable);
                var specPath = LastActiveUsedPath;

                // Cache relative path computation because it turns out to be a bit expensive.
                if (packageRoot == specPath)
                {
                    m_relativePath = RelativePath.Create(FrontEndContext.StringTable, ".");
                }
                else
                {
                    bool relative = packageRoot.TryGetRelative(FrontEndContext.PathTable, specPath, out m_relativePath);
                    Contract.Assume(relative);
                }

                return m_relativePath;
            }
        }

        /// <summary>
        /// All information related to a top-level value.
        /// </summary>
        public TopLevelValueInfo TopLevelValueInfo { get; }

        /// <summary>
        /// The type of file we are processing
        /// </summary>
        protected FileType FileType { get; }

        /// <summary>
        /// Check whether the file being evaluated is the primary config file
        /// </summary>
        /// <remarks>
        /// This is only called by InputTracker for DScript. Temporary workaround
        /// </remarks>
        public bool IsGlobalConfigFile => FileType == FileType.GlobalConfiguration;

        /// <summary>
        /// In this field we track how many undisposed child contexts there are.
        /// </summary>
        /// <remarks>
        /// This mutable field is only used for contract checking purposes.
        /// </remarks>
        private int m_numberOfActiveChildren;

        /// <summary>
        /// Whether any nested child contexts are currently alive (not yet disposed).
        /// </summary>
        /// <remarks>
        /// No mutation operations should be allowed on this context while there are any undisposed child contexts
        /// </remarks>
        public bool HasChildren => Volatile.Read(ref m_numberOfActiveChildren) != 0;

        /// <summary>
        /// Cache for qualifier values.
        /// </summary>
        public QualifierValueCache QualifierValueCache => ContextTree.QualifierValueCache;

        internal void AddChild()
        {
            Interlocked.Increment(ref m_numberOfActiveChildren);
        }

        private void RemoveChild()
        {
            var activeChildren = Interlocked.Decrement(ref m_numberOfActiveChildren);
            Contract.Assume(activeChildren >= 0);
        }

        private int m_disposed;

        /// <nodoc />
        public bool IsDisposed => m_disposed != 0;

        /// <inheritdoc />
        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref m_disposed, 1, 0) == 0 && ParentContext != null)
            {
                ParentContext.RemoveChild();
            }
        }

        internal ImmutableContextBase(
            ContextTree contextTree,
            ImmutableContextBase parent,
            [NotNull]ModuleLiteral module,
            TopLevelValueInfo topLevelValueInfo,
            FileType fileType,
            EvaluatorConfiguration configuration,
            [NotNull]IEvaluationScheduler evaluationScheduler)
        {
            Contract.Requires(contextTree.IsValid);
            Contract.Requires(parent == null || parent.ContextTree == contextTree);
            Contract.Requires(module != null);
            Contract.Requires(evaluationScheduler != null);

            ParentContext = parent;
            LastActiveUsedModule = module;
            TopLevelValueInfo = topLevelValueInfo;
            ContextTree = contextTree;
            DebugState = new DebugState();
            m_relativePath = RelativePath.Invalid;
            EvaluatorConfiguration = configuration;
            EvaluationScheduler = evaluationScheduler;
            FileType = fileType;
            if (parent != null)
            {
                m_callStack = parent.m_callStack;
            }
        }

        /// <summary>
        /// Creates a mutable named child context.
        /// </summary>
        /// <remarks>
        /// Until all mutable child contexts have been disposed, no mutations are allowed on the current context.
        /// </remarks>
        public Context CreateWithName(Thunk activeThunk, FullSymbol name, ModuleLiteral module, object templateValue, LineInfo location)
        {
            Contract.Requires(name.IsValid);
            Contract.Requires(module != null);

            var topLevelValueState = new TopLevelValueInfo(
                activeThunk,
                name,
                module.Path,
                location,
                templateValue);

            return new Context(
                ContextTree,
                this,
                module,
                topLevelValueState,
                FileType,
                EvaluatorConfiguration,
                EvaluationScheduler);
        }

        /// <summary>
        /// Creates a mutable child context for evaluation in a different module.
        /// </summary>
        /// <remarks>
        /// Until all mutable child contexts have been disposed, no mutations are allowed on the current context.
        /// </remarks>
        public Context CreateWithModule(ModuleLiteral module)
        {
            Contract.Requires(module != null);

            return new Context(
                ContextTree,
                this,
                module,
                null,
                FileType,
                EvaluatorConfiguration,
                EvaluationScheduler);
        }

        /// <summary>
        /// The call stack is a linked list of stack entries.
        /// To allow for concurrent evaluation, contexts can fork, and then multiple child contexts can inherit a particular parent call stack --- which they are not allowed to mutate.
        /// Effectively, this results in the call stack data structure inducing a tree.
        /// </summary>
        protected StackEntry m_callStack;

        /// <summary>
        /// Call stack.
        /// </summary>
        public IEnumerable<StackEntry> CallStack
        {
            get
            {
                for (StackEntry entry = m_callStack; entry != null; entry = entry.Previous)
                {
                    yield return entry;
                }
            }
        }

        /// <summary>
        /// Depths of CallStack
        /// </summary>
        public int CallStackSize => m_callStack?.Depth ?? 0;

        /// <summary>
        /// Checks if stack has been overflow.
        /// </summary>
        public bool IsStackOverflow => CallStackSize > CallStackThreshold;

        /// <summary>
        /// How many stack frames were inherited and must not be popped / mutated
        /// </summary>
        public int ParentCallStackSize => ParentContext?.CallStackSize ?? 0;

        /// <summary>
        /// Method that provides a string representation of the current evaluation stack from specified <paramref name="location" />.
        /// </summary>
        public string GetStackTraceAsString(in UniversalLocation location)
        {
            return new DisplayStringHelper(this).GetStackTraceAsString(location, FrontEndContext.QualifierTable);
        }

        /// <summary>
        /// Returns string representation of the stack trace that could be immediately send to the logger.
        /// </summary>
        public string GetStackTraceAsErrorMessage(in UniversalLocation location)
        {
            var stackTrace = GetStackTraceAsString(location);

            if (!string.IsNullOrWhiteSpace(stackTrace))
            {
                return FormattableStringEx.I($"{Environment.NewLine}Stack trace: {stackTrace}.");
            }

            return string.Empty;
        }

        private EvaluationErrors m_errors;

        /// <summary>
        /// Error reporting object.
        /// </summary>
        public EvaluationErrors Errors
        {
            get
            {
                // The Errors object is only rarely ever accessed --- in case of on error, typically.
                // So we allocate it lazily.
                if (m_errors == null)
                {
                    Interlocked.CompareExchange(ref m_errors, new EvaluationErrors(this), null);
                }

                return m_errors;
            }
        }
    }
}
