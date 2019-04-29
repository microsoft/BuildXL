// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.Utilities;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Values
{
    /// <summary>
    /// Closure.
    /// </summary>
    public sealed class Closure : Expression
    {
        /// <summary>
        /// Module literal.
        /// </summary>
        public ModuleLiteral Env { get; }

        /// <summary>
        /// Function expression.
        /// </summary>
        public FunctionLikeExpression Function { get; }

        /// <summary>
        /// Captured stack frame.
        /// </summary>
        public EvaluationStackFrame Frame { get; }

        /// <nodoc />
        public Closure(ModuleLiteral env, FunctionLikeExpression function, EvaluationStackFrame frame)
            : base(location: default(LineInfo)) // Closures don't have locations
        {
            Contract.Requires(env != null);
            Contract.Requires(function != null);
            Contract.Requires(frame != null);

            Env = env;
            Function = function;
            // Now, closure instances are created only for anonymous functions.
            // In this case, frame that represents a captured state should be cloned,
            // because frame are pooled.
            Frame = frame.Detach();
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        { }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.Closure;

        /// <inheritdoc />
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            return EvaluationResult.Create(this);
        }

        /// <nodoc />
        public bool Equals(Closure c)
        {
            return c.Env.Path == Env.Path
                   && c.Function.Name == Function.Name
                   && c.Location == Location;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            // Closures support equality comparison.
            // function f() {return 42;}
            // const x = f === f; // should be true.

            if (ReferenceEquals(null, obj))
            {
                return false;
            }

            if (ReferenceEquals(this, obj))
            {
                return true;
            }

            return obj is Closure closure && Equals(closure);
        }

        /// <inheritdoc/>
        public override int GetHashCode() => throw new System.InvalidOperationException("Instances of this type should not be stored in hash tables.");

        /// <inheritdoc/>
        public override string ToStringShort(StringTable stringTable)
        {
            return $"<closure>{Function.ToStringShort(stringTable)}";
        }

        /// <inheritdoc/>
        public override string ToDebugString()
        {
            return $"<closure>{Function.ToDebugString()}";
        }
    }
}
