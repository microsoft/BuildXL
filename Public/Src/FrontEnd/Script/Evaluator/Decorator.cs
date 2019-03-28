// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities.Instrumentation.Common;
using JetBrains.Annotations;
using BuildXL.FrontEnd.Script;
using BuildXL.FrontEnd.Script.Values;

#pragma warning disable SA1649 // File name must match first type name

namespace BuildXL.FrontEnd.Script.Evaluator
{
    /// <summary>
    ///     This interface provides means to decorate DScript evaluation and/or observe certain
    ///     events generated during the evaluation.
    /// </summary>
    /// <typeparam name="T">Return type of the decorator.</typeparam>
    public interface IDecorator<T>
    {
        /// <summary>
        ///     Accepts all the arguments available at the time of node evaluation (i.e., node itself
        ///     (<paramref name="node" />, evaluation context (<paramref name="context" />),  environment
        ///     (<paramref name="env" />), and arguments (<paramref name="args" />)), as well as a no-arguments
        ///     continuation.  The fact that the continuation accepts no arguments means that
        ///     these decorators are supposed to be pure, i.e., not change any of the arguments that will
        ///     ultimately be passed down to <code cref="Node.Eval(Context, ModuleLiteral, EvaluationStackFrame)" />.
        ///     (the fact that the <code cref="Context" /> class is mutable, and so a decorator can
        ///     directly mutate it, is a separate issue)
        /// </summary>
        /// <param name="node">Node to be evaluated.</param>
        /// <param name="context">Evaluation context.</param>
        /// <param name="env">Evaluation environment.</param>
        /// <param name="args">Evaluation arguments.</param>
        /// <param name="continuation">Continuation function to be called by the decorator.</param>
        /// <returns>Up for the decorator to decide; typically it is whatever <paramref name="continuation" /> returns.</returns>
        T EvalWrapper([NotNull]Node node, [NotNull]Context context, [NotNull]ModuleLiteral env, [NotNull]EvaluationStackFrame args, [NotNull]Func<T> continuation);

        /// <summary>
        ///     The interpreter calls this method to notify the decorator that a diagnostic message has been logged.
        /// </summary>
        void NotifyDiagnostics([NotNull]Context context, Diagnostic diagnostic);

        /// <summary>
        ///     The interpreter calls this method to notify the decorator that the evaluation has finished.
        /// </summary>
        void NotifyEvaluationFinished(bool success, [NotNull]IEnumerable<IModuleAndContext> contexts);
    }

    /// <summary>
    ///     A tuple of <see cref="ModuleLiteral"/> and <see cref="ContextTree"/>.
    /// </summary>
    public interface IModuleAndContext
    {
        /// <nodoc />
        ModuleLiteral Module { get; }

        /// <nodoc />
        ContextTree Tree { get; }
    }

    /// <summary>
    ///     A straightforward implementation of <see cref="IModuleAndContext"/>.
    /// </summary>
    public sealed class ModuleAndContext : IModuleAndContext
    {
        /// <inheritdoc />
        public ModuleLiteral Module { get; }

        /// <inheritdoc />
        public ContextTree Tree { get; }

        /// <nodoc />
        public ModuleAndContext(ModuleLiteral module, ContextTree tree)
        {
            Module = module;
            Tree = tree;
        }
    }
}
