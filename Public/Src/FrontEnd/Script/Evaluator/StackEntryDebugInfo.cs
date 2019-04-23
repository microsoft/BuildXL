// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using JetBrains.Annotations;

#pragma warning disable SA1649 // File name must match first type name

namespace BuildXL.FrontEnd.Script.Evaluator
{
    /// <summary>
    /// A representation of a local variable.
    /// </summary>
    public interface ILocalVar
    {
        /// <summary>Value of this local variable.</summary>
        object Value { get; }

        /// <summary>Name of this local variable.  May be <see cref="SymbolAtom.Invalid"/>.</summary>
        SymbolAtom Name { get; }

        /// <summary>Position of this local variable in its corresponding frame.</summary>
        int Index { get; }
    }

    /// <summary>
    /// Additional debug information associated with a <see cref="StackEntry"/>.
    /// </summary>
    public sealed class DebugInfo
    {
        private readonly LocalVarImpl[] m_locals;

        /// <nodoc/>
        public ModuleLiteral InvocationEnv { get; }

        /// <nodoc/>
        public EvaluationStackFrame Frame { get; }

        private DebugInfo(ModuleLiteral env, EvaluationStackFrame frame)
        {
            Contract.Requires(frame != null);

            InvocationEnv = env;
            Frame = frame.Detach();
            m_locals = frame.Frame.Select((val, idx) => new LocalVarImpl(frame, idx)).ToArray();
        }

        /// <summary>
        /// Creates a helper class with information about locals.
        /// Returns null if not under the debugger.
        /// </summary>
        [CanBeNull]
        internal static DebugInfo Create(Context context, ModuleLiteral env, EvaluationStackFrame locals)
        {
            return context.IsBeingDebugged
                ? new DebugInfo(env, locals)
                : null;
        }

        /// <summary>
        /// Returns a list of currently accessible local variables for a given stack entry, respecting variable shadowing inside of lambdas.
        /// </summary>
        public static IReadOnlyList<ILocalVar> ComputeCurrentLocals(StackEntry stackEntry)
        {
            Contract.Ensures(Contract.Result<IReadOnlyList<ILocalVar>>().All(lvar => lvar.Name.IsValid));

            var debugInfo = stackEntry?.DebugInfo;
            if (debugInfo == null)
            {
                return CollectionUtilities.EmptyArray<ILocalVar>();
            }

            return stackEntry.DebugInfo.m_locals
                .Select(arg => (ILocalVar)new ComputedLocalVar(arg.Index, FigureOutLocalVarName(stackEntry, arg), arg.Value))
                .Where(p => p.Name.IsValid)
                .GroupBy(p => p.Name)
                .Select(grp => grp.Last())
                .ToArray();
        }

        internal void SetLocalVarName(int idx, SymbolAtom name)
        {
            if (m_locals != null && idx < m_locals.Length && idx >= 0)
            {
                m_locals[idx].Name = name;
            }
        }

        private static SymbolAtom FigureOutLocalVarName(StackEntry stackEntry, ILocalVar lvar)
        {
            Contract.Requires(stackEntry != null);
            Contract.Requires(lvar != null);

            // if lvar.Name is explicitly set --> return lvar.Name
            if (lvar.Name.IsValid)
            {
                return lvar.Name;
            }

            // if lvar.Index is less than the number of captured variables in this frame --> look up the name in the parent frame.
            if (stackEntry.Lambda.Captures > lvar.Index)
            {
                while ((stackEntry = stackEntry.Previous) != null)
                {
                    var localVarInParentFrame = stackEntry.DebugInfo.m_locals[lvar.Index];
                    if (localVarInParentFrame.Name.IsValid)
                    {
                        return localVarInParentFrame.Name;
                    }
                }
            }

            // Invalid if all previous efforts failed.
            return SymbolAtom.Invalid;
        }

        /// <summary>
        /// Internal representation of a local variable whose name can be set during evaluation.
        /// </summary>
        private sealed class LocalVarImpl : ILocalVar
        {
            private readonly EvaluationStackFrame m_objects;
            private readonly int m_index;

            /// <inheritdoc/>
            public object Value => m_objects[m_index].Value;

            /// <inheritdoc/>
            public SymbolAtom Name { get; set; }

            /// <inheritdoc/>
            public int Index => m_index;

            internal LocalVarImpl(EvaluationStackFrame frame, int index)
            {
                m_objects = frame;
                m_index = index;
            }
        }

        /// <summary>
        /// Immutable representation of a local variable for external consumption.
        /// </summary>
        private sealed class ComputedLocalVar : ILocalVar
        {
            private readonly object m_value;
            private readonly SymbolAtom m_name;
            private readonly int m_index;

            object ILocalVar.Value => m_value;

            SymbolAtom ILocalVar.Name => m_name;

            int ILocalVar.Index => m_index;

            internal ComputedLocalVar(int index, SymbolAtom name, object value)
            {
                m_index = index;
                m_name = name;
                m_value = value;
            }
        }
    }
}
