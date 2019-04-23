// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Script.Values
{
    /// <summary>
    /// Call signature for a member that takes receiver and two arguments.
    /// </summary>
    public delegate EvaluationResult CallableMemberSignature2<T>(Context context, T receiver, EvaluationResult arg1, EvaluationResult arg2, EvaluationStackFrame captures);

    /// <summary>
    /// Callable member that takes exactly two argument
    /// </summary>
    public sealed class CallableMember2<T> : CallableMember<T>
    {
        private readonly CallableMemberSignature2<T> m_function;

        /// <nodoc />
        public CallableMember2(FunctionStatistic statistic, SymbolAtom name, CallableMemberSignature2<T> function, short minArity, bool rest)
            : base(statistic, name, minArity, 2, rest)
        {
            m_function = function;
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.Function2;

        /// <inheritdoc />
        public override EvaluationResult Apply(Context context, T receiver, EvaluationResult arg1, EvaluationResult arg2, EvaluationStackFrame captures)
        {
            return m_function(context, receiver, arg1, arg2, captures);
        }
    }
}
