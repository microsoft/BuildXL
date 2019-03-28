// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.FrontEnd.Script;
using BuildXL.FrontEnd.Script.Util;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Types;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Sdk;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script
{
    /// <summary>
    /// Class for AST node.
    /// </summary>
    [DebuggerDisplay("{" + nameof(ToDebugString) + "(),nq}")]
    public abstract partial class Node
    {
        /// <summary>
        /// Gets the node location.
        /// </summary>
        /// <remarks>
        /// Line and position are computed lazily.
        /// </remarks>
        public LineInfo Location { get; }

        /// <nodoc />
        protected Node(LineInfo location)
        {
            Location = location;
        }

        /// <summary>
        /// Accepts visitor.
        /// </summary>
        public abstract void Accept(Visitor visitor);

        /// <summary>
        /// Applies the decorator (<code cref="ImmutableContextBase.Decorator"/>) specified by <paramref name="context"/>
        /// around the evaluation of this node (<code cref="DoEval(Context, ModuleLiteral, EvaluationStackFrame)"/>).
        /// </summary>
        /// <remarks>
        /// This method returns <see cref="EvaluationResult"/> and not an object to avoid additional allocations (like wrapping every value in ReturnValue instance)
        /// and will allow to avoid boxing allocations when the result of an evaluation is a value type (like AbsolutePath) that is used as an argument to for a next call.
        /// </remarks>
        public EvaluationResult Eval(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            return context.HasDecorator
                ? DoEvalWithDecorator(context, env, args)
                : DoEval(context, env, args);
        }

        /// <summary>
        /// Evaluates a node without applying the decorator, even if a decorator is specified
        /// </summary>
        public EvaluationResult EvalWithoutDecorator(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            return DoEval(context, env, args);
        }

        /// <summary>
        /// Applies <see cref="ImmutableContextBase.Decorator"/> around evaluation of this node (<see cref="DoEval"/>.
        /// </summary>
        protected EvaluationResult DoEvalWithDecorator(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            Contract.Requires(context.Decorator != null);
            return context.Decorator.EvalWrapper(this, context, env, args, () => DoEval(context, env, args));
        }

        /// <summary>
        /// Evaluates this node.
        /// </summary>
        protected abstract EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame);

        /// <summary>
        /// Gets syntax kind.
        /// </summary>
        public abstract SyntaxKind Kind { get; }

        /// <summary>
        /// Gets the string representation of a given decorators.
        /// </summary>
        protected static string ToDebugString(IReadOnlyList<Expression> decorators)
        {
            Contract.Requires(decorators != null);

            return decorators.Count > 0
                ? string.Join(" ", decorators.Select(d => "@@" + d.ToString())) + " "
                : string.Empty;
        }

        /// <summary>
        /// Gets short representation of the current node.
        /// </summary>
        public virtual string ToStringShort(StringTable stringTable) => ToDebugString();

        /// <summary>
        /// Gets a string representation of a node.
        /// </summary>
        public abstract string ToDebugString();

        /// <summary>
        /// Gets the string representation of a given string id.
        /// </summary>
        protected static string ToDebugString(StringId name)
        {
            Contract.Requires(name.IsValid);
#if DEBUG
            return FrontEndContext.DebugContext != null ? name.ToString(FrontEndContext.DebugContext.StringTable) : name.ToString();
#else
            return name.ToString();
#endif
        }

        /// <summary>
        /// Gets the string representation of a symbol atom.
        /// </summary>
        protected static string ToDebugString(SymbolAtom name)
        {
            Contract.Requires(name.IsValid);
#if DEBUG
            return FrontEndContext.DebugContext != null ? name.ToString(FrontEndContext.DebugContext.StringTable) : name.StringId.ToString();
#else
            return name.StringId.ToString();
#endif
        }

        /// <summary>
        /// Gets the string representation of a given absolute path.
        /// </summary>
        protected static string ToDebugString(RelativePath path)
        {
            Contract.Requires(path.IsValid);
#if DEBUG
            return FrontEndContext.DebugContext != null
                ? path.ToString(FrontEndContext.DebugContext.StringTable, PathFormat.Script)
                : string.Join(PathFormatter.GetPathSeparator(PathFormat.Script).ToString(), path.Components.ToString());
#else
            return string.Join(PathFormatter.GetPathSeparator(PathFormat.Script).ToString(), path.Components.ToString());
#endif
        }

        /// <summary>
        /// Gets the string representation of a given absolute path.
        /// </summary>
        protected static string ToDebugString(AbsolutePath path)
        {
            Contract.Requires(path.IsValid);
#if DEBUG
            return FrontEndContext.DebugContext != null ? path.ToString(FrontEndContext.DebugContext.PathTable) : path.ToString();
#else
            return path.ToString();
#endif
        }

        /// <summary>
        /// Gets the string representation of a given full symbol.
        /// </summary>
        protected static string ToDebugString(FullSymbol symbol)
        {
            Contract.Requires(symbol.IsValid);
#if DEBUG
            return FrontEndContext.DebugContext != null ? symbol.ToString(FrontEndContext.DebugContext.SymbolTable) : symbol.ToString();
#else
            return symbol.ToString();
#endif
        }

        /// <summary>
        /// Returns location of the node that is requred for logging.
        /// </summary>
        protected Location LocationForLogging(ImmutableContextBase context, ModuleLiteral currentModule)
        {
            return Location.AsLoggingLocation(currentModule, context);
        }

        /// <inheritdoc/>
        [Obsolete]
#pragma warning disable CS0809 // Obsolete member overrides non-obsolete member
        public override string ToString()
#pragma warning restore CS0809 // Obsolete member overrides non-obsolete member
        {
            return ToDebugString();
        }
    }
}
