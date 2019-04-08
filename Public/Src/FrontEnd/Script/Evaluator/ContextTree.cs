// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Threading;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script;
using BuildXL.FrontEnd.Script.Ambients.Transformers;
using BuildXL.FrontEnd.Script.Evaluator;
using JetBrains.Annotations;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.FrontEnd.Script.Tracing;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Sdk;

namespace BuildXL.FrontEnd.Script.Evaluator
{
    /// <summary>
    /// All contexts created from a single root form a family --- the context tree.
    /// </summary>
    /// <remarks>
    /// The purpose of this class, and the fact that it's disposable, is not so much to eagerly release memory.
    /// It is rather to enforce the invariant that all contexts whose thunks may interact share a common root.
    /// This is important for cycle and deadlock detection.
    /// </remarks>
    public sealed class ContextTree : IDisposable
    {
        /// <summary>
        /// Front end host.
        /// </summary>
        public FrontEndHost FrontEndHost { get; private set; }

        /// <summary>
        /// Front end context.
        /// </summary>
        public FrontEndContext FrontEndContext { get; private set; }

        /// <summary>
        /// Constants
        /// </summary>
        public GlobalConstants Constants { get; private set; }

        /// <summary>
        /// Module registry.
        /// </summary>
        public ModuleRegistry ModuleRegistry { get; private set; }

        /// <summary>
        /// Logger instance.
        /// </summary>
        public Logger Logger { get; private set; }

        /// <summary>
        /// Evaluation statistics.
        /// </summary>
        public EvaluationStatistics Statistics { get; private set; }

        /// <summary>
        /// Whether it's being debugged.  Other components may find this useful in deciding
        /// how much information to keep around, in order to support the debugger.
        /// </summary>
        public bool IsBeingDebugged { get; }

        /// <summary>
        /// A decorator to be used around node evaluation.
        /// </summary>
        public IDecorator<EvaluationResult> Decorator { get; private set; }

        /// <summary>
        /// Root context, if created already.
        /// </summary>
        public Context RootContext { get; }

        /// <summary>
        /// Queue that manages pool of evaluation tasks.
        /// </summary>
        public IEvaluationScheduler EvaluationScheduler { get; }

        /// <summary>
        /// Cache for qualifier values.
        /// </summary>
        public QualifierValueCache QualifierValueCache { get; }

        /// <summary>
        /// Many pips when created use the same Tool. We can save time by doing that only ones relying on object identity for the pip.
        /// </summary>
        public ConcurrentDictionary<ObjectLiteral, CachedToolDefinition> ToolDefinitionCache { get; private set; }

        /// <summary>
        /// DScript exposes a value cache. This is the backing store. Values from this cache should never directly be returned
        /// They should always be cloned to or else there is an observable side effect which affects all forms of DScript caching and incrementality.
        /// </summary>
        /// <remarks>
        /// This will hopefully be a temporary solution untill we get a proper design. It is only exposed from Sdk.ValueCache modulde that we've educated
        /// office for mac about.
        /// Storing the cache here will also make it only availalbe to DScript and not the other frontends.
        /// </remarks>
        public ConcurrentDictionary<Fingerprint, EvaluationResult> ValueCache { get; private set; }

        /// <nodoc />
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        public ContextTree(
            [NotNull]FrontEndHost frontEndHost,
            [NotNull]FrontEndContext frontEndContext,
            [NotNull]GlobalConstants constants,
            [NotNull]ModuleRegistry moduleRegistry,
            [NotNull]Logger logger,
            [NotNull]EvaluationStatistics statistics,
            QualifierValueCache qualifierValueCache,
            bool isBeingDebugged,
            IDecorator<EvaluationResult> decorator,
            [NotNull]FileModuleLiteral module,
            [NotNull]EvaluatorConfiguration configuration,
            [NotNull]IEvaluationScheduler evaluationScheduler,
            FileType fileType)
        {
            Contract.Requires(frontEndHost != null);
            Contract.Requires(frontEndContext != null);
            Contract.Requires(constants != null);
            Contract.Requires(moduleRegistry != null);
            Contract.Requires(logger != null);
            Contract.Requires(statistics != null);
            Contract.Requires(module != null);

            EvaluationScheduler = evaluationScheduler;

            FrontEndHost = frontEndHost;
            FrontEndContext = frontEndContext;
            Constants = constants;
            ModuleRegistry = moduleRegistry;
            Logger = logger;
            Statistics = statistics;
            IsBeingDebugged = isBeingDebugged;
            Decorator = decorator;
            EvaluationScheduler = evaluationScheduler;
            QualifierValueCache = qualifierValueCache;
            ToolDefinitionCache = new ConcurrentDictionary<ObjectLiteral, CachedToolDefinition>();
            ValueCache = new ConcurrentDictionary<Fingerprint, EvaluationResult>();

            RootContext =
                new Context(
                    contextTree: this,
                    parent: null,
                    module: module,
                    topLevelValueInfo: null,
                    fileType: fileType,
                    configuration: configuration,
                    evaluationScheduler: evaluationScheduler);

            Interlocked.Increment(ref statistics.ContextTrees);
        }

        /// <summary>
        /// Whether this instance is valid, i.e., not disposed.
        /// </summary>
        public bool IsValid => FrontEndHost != null;

        /// <inheritdoc />
        [SuppressMessage("Microsoft.Usage", "CA2213:DisposableFieldsShouldBeDisposed", MessageId = "<RootContext>k__BackingField", Justification = "MeasuredDispose")]
        public void Dispose()
        {
            if (!IsBeingDebugged)
            {
                RootContext.Dispose();
                FrontEndHost = null;
                FrontEndContext = null;
                Constants = null;
                ModuleRegistry = null;
                Logger = null;
                Statistics = null;
                Decorator = null;
                ToolDefinitionCache = null;
                ValueCache = null;
            }
        }
    }
}
