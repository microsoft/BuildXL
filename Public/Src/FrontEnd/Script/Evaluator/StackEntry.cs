// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.FrontEnd.Script.Util;
using JetBrains.Annotations;
using BuildXL.FrontEnd.Script;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Values;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Evaluator
{
    /// <summary>
    /// Entry for stack in context.
    /// </summary>
    public sealed class StackEntry
    {
        [CanBeNull]
        private ApplyExpression m_ambientCall;

        /// <summary>
        /// Previous stack entry.
        /// </summary>
        public StackEntry Previous { get; private set; }

        /// <summary>
        /// Method that is called.
        /// </summary>
        [CanBeNull]
        public FunctionLikeExpression Lambda { get; private set; }

        /// <summary>
        /// Path.
        /// </summary>
        public AbsolutePath Path { get; private set; }

        /// <summary>
        /// Location of the invocation.
        /// </summary>
        public LineInfo InvocationLocation { get; private set; }

        /// <summary>
        /// <b>Optional: </b> local values.
        ///
        /// Not all implementation must provide this information, for memory efficiency reasons.
        /// Typically, this information is not needed for the standard DScript evaluation,
        /// but a debugger might make a good use of it.
        /// </summary>
        public DebugInfo DebugInfo { get; private set; }

        /// <summary>
        /// Environment of the <see cref="Closure"/> (if any).
        /// </summary>
        [CanBeNull]
        public ModuleLiteral Env { get; private set; }

        /// <summary>
        /// Gets the current depth
        /// </summary>
        public int Depth { get; private set; }

        /// <nodoc />
        internal StackEntry() { }

        /// <nodoc />
        internal StackEntry Initialize(Closure closure, ApplyExpression ambientCall, AbsolutePath path, LineInfo location, DebugInfo debugInfo)
            => Initialize(closure?.Function, closure?.Env, ambientCall, path, location, debugInfo);

        internal StackEntry Initialize(FunctionLikeExpression lambda, ModuleLiteral closureEnv, ApplyExpression ambientCall, AbsolutePath path, LineInfo location, DebugInfo debugInfo)
        {
            Lambda = lambda;
            Env = closureEnv;
            m_ambientCall = ambientCall;
            Path = path;
            InvocationLocation = location;
            DebugInfo = debugInfo;
            return this;
        }

        internal DisplayStackTraceEntry CreateDisplayStackTraceEntry(ImmutableContextBase context, LineInfo lastCallSite)
        {
            if (m_ambientCall != null)
            {
                var loc = new Location { File = "[ambient call]", Line = -1, Position = -1 };
                var functionName = m_ambientCall.Functor.ToDisplayString(context);
                return new DisplayStackTraceEntry(loc, functionName, this);
            }

            if (Lambda != null)
            {
                var location = lastCallSite.AsLoggingLocation(Env, context);
                SymbolAtom? methodName = Lambda?.Name;
                var functionName = methodName?.IsValid == true ? methodName.Value.ToString(context.StringTable) : "<lambda>";
                return new DisplayStackTraceEntry(location, functionName, this);
            }

            throw Contract.AssertFailure("The code is effectively unreacheable.");
        }

        /// <nodoc />
        internal void Link(ref StackEntry current)
        {
            Contract.Requires(Previous == null);

            Depth = current?.Depth + 1 ?? 1;
            Previous = current;
            current = this;
        }

        /// <nodoc />
        internal static StackEntry Unlink(ref StackEntry current)
        {
            Contract.Requires(current != null);

            var freedStackEntry = current;
            current = current.Previous;
            freedStackEntry.Previous = null;
            return freedStackEntry;
        }

        /// <nodoc />
        internal void Clear()
        {
            Contract.Requires(Previous == null);

            Lambda = null;
            Env = null;
            m_ambientCall = null;
            DebugInfo = null;

            // Path, LineInfo, Depth do not hold references, so we don't care
        }
    }
}
