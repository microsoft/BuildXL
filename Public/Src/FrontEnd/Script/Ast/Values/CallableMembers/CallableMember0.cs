// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities;
using BuildXL.FrontEnd.Script;
using BuildXL.FrontEnd.Script.Evaluator;

namespace BuildXL.FrontEnd.Script.Values
{
    /// <summary>
    /// Call signature for a member that takes a receiver with but no arguments.
    /// </summary>
    public delegate EvaluationResult CallableMemberSignature0<T>(Context context, T receiver, EvaluationStackFrame captures);

    /// <summary>
    /// Callable member that takes no additional arguments.
    /// </summary>
    public sealed class CallableMember0<T> : CallableMember<T>
    {
        private readonly CallableMemberSignature0<T> m_function;

        /// <nodoc />
        public CallableMember0(FunctionStatistic statistic, SymbolAtom name, CallableMemberSignature0<T> function, bool isProperty)
            : base(statistic, name, 0, 0, rest: false)
        {
            m_function = function;
            IsProperty = isProperty;
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.Function0;

        /// <inheritdoc />
        public override bool IsProperty { get; }

        /// <inheritdoc />
        public override EvaluationResult Apply(Context context, T receiver, EvaluationStackFrame captures)
        {
            return m_function(context, receiver, captures);
        }
    }
}
