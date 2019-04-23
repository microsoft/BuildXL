// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Script.Values
{
    /// <summary>
    /// Call signature for a member that takes receiver and arbitrary set of arguments.
    /// </summary>
    public delegate EvaluationResult CallableMemberSignatureN<T>(Context context, T receiver, EvaluationResult[] args, EvaluationStackFrame captures);

    /// <summary>
    /// Callable member that takes exactly arbitrary set of arguments.
    /// </summary>
    public sealed class CallableMemberN<T> : CallableMember<T>
    {
        private readonly CallableMemberSignatureN<T> m_function;

        /// <nodoc />
        public CallableMemberN(FunctionStatistic statistic, SymbolAtom name, CallableMemberSignatureN<T> function, short minArity, short maxArity, bool rest)
            : base(statistic, name, minArity, maxArity, rest)
        {
            m_function = function;
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.FunctionN;

        /// <inheritdoc />
        public override EvaluationResult Apply(Context context, T receiver, EvaluationResult[] args, EvaluationStackFrame captures)
        {
            return m_function(context, receiver, args, captures);
        }
    }
}
