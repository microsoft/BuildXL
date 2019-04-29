// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Literals
{
    /// <summary>
    /// Undefined literal.
    /// </summary>
    public sealed class UndefinedLiteral : Expression, IConstantExpression
    {
        /// <summary>
        /// Singleton instance (without location).
        /// </summary>
        public static UndefinedLiteral Instance { get; } = new UndefinedLiteral(location: default(LineInfo));

        /// <inheritdoc />
        object IConstantExpression.Value => UndefinedValue.Instance;

        /// <nodoc />
        private UndefinedLiteral(LineInfo location)
            : base(location)
        {
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.UndefinedLiteral;

        /// <inheritdoc />
        public override string ToDebugString()
        {
            return "undefined";
        }

        /// <inheritdoc />
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            return EvaluationResult.Undefined;
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            // Do nothing! There is nothing to serialize here.
            // Observe that the location is being serialized by the base class. This is just for
            // uniformity reasons, the location is read back but not used.
            // We can consider removing the location if this becomes a perf issue for serializing/deserializing.
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj != null && obj.GetType() == GetType();
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return -27;
        }
    }
}
