// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.Utilities;
using JetBrains.Annotations;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Values
{
    /// <summary>
    /// Base type that represents bound callable member (i.e., <see cref="CallableMember"/> with a receiver).
    /// </summary>
    public abstract class CallableValue : Expression
    {
        /// <nodoc />
        protected CallableValue()
            : base(location: default(LineInfo)) // ambient values doesn't have location.
        {
        }

        /// <summary>
        /// Member function that can be invoked.
        /// </summary>
        public abstract CallableMember CallableMember { get; }

        /// <summary>
        /// Returns true when callable value is actually a property but not a method.
        /// </summary>
        public bool IsProperty => CallableMember.IsProperty;

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        { }

        /// <inheritdoc />
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            return EvaluationResult.Create(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.BoundFunction;

        /// <summary>
        /// Applies ambient.
        /// </summary>
        public abstract EvaluationResult Apply([JetBrains.Annotations.NotNull]Context context, EvaluationStackFrame captures);

        /// <summary>
        /// Applies ambient.
        /// </summary>
        public abstract EvaluationResult Apply(Context context, [CanBeNull]EvaluationResult arg, [JetBrains.Annotations.NotNull]EvaluationStackFrame captures);

        /// <summary>
        /// Applies ambient.
        /// </summary>
        public abstract EvaluationResult Apply([JetBrains.Annotations.NotNull]Context context, [CanBeNull]EvaluationResult arg1, [CanBeNull]EvaluationResult arg2, [JetBrains.Annotations.NotNull]EvaluationStackFrame captures);

        /// <summary>
        /// Applies ambient.
        /// </summary>
        public abstract EvaluationResult Apply([JetBrains.Annotations.NotNull]Context context, [CanBeNull]EvaluationResult[] args, [JetBrains.Annotations.NotNull]EvaluationStackFrame captures);
    }

    /// <summary>
    /// Value that can be invoked.
    /// Created by binding together <see cref="CallableMember"/> and a receiver.
    /// </summary>
    /// <remarks>
    /// Callable value is a curried function that was created from callable member and the receiver.
    /// </remarks>
    public sealed class CallableValue<T> : CallableValue
    {
        /// <summary>
        /// Receiver of the member function.
        /// </summary>
        private readonly T m_receiver;

        /// <summary>
        /// Member function itself.
        /// </summary>
        private readonly CallableMember<T> m_callableMember;

        /// <inheritdoc />
        public override CallableMember CallableMember => m_callableMember;

        /// <nodoc />
        public CallableValue(T receiver, CallableMember<T> callableMember)
        {
            Contract.Requires(receiver != null);
            Contract.Requires(callableMember != null);

            m_receiver = receiver;
            m_callableMember = callableMember;
        }

        /// <inheritdoc />
        public override EvaluationResult Apply(Context context, EvaluationStackFrame captures)
        {
            return m_callableMember.Apply(context, m_receiver, captures);
        }

        /// <inheritdoc />
        public override EvaluationResult Apply(Context context, EvaluationResult arg, EvaluationStackFrame captures)
        {
            return m_callableMember.Apply(context, m_receiver, arg, captures);
        }

        /// <inheritdoc />
        public override EvaluationResult Apply(Context context, EvaluationResult arg1, EvaluationResult arg2, EvaluationStackFrame captures)
        {
            return m_callableMember.Apply(context, m_receiver, arg1, arg2, captures);
        }

        /// <inheritdoc />
        public override EvaluationResult Apply(Context context, EvaluationResult[] args, EvaluationStackFrame captures)
        {
            return m_callableMember.Apply(context, m_receiver, args, captures);
        }

        /// <inheritdoc/>
        public override string ToStringShort(StringTable stringTable)
        {
            return m_callableMember.ToStringShort(stringTable);
        }
    }
}
