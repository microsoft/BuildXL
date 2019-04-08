// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Types;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Expressions
{
    /// <summary>
    /// Cast expression, consisting of a sub-expression and a cast type.
    /// </summary>
    public sealed class CastExpression : Expression
    {
        /// <summary>
        /// Used to denote syntactic variations of writing cast expressions
        /// </summary>
        public enum TypeAssertionKind : byte
        {
            /// <summary>Denotes this syntactic form: &lt;type&gt;expr</summary>
            TypeCast,

            /// <summary>Denotes this syntactic form: expr as type</summary>
            AsCast,
        }

        /// <summary>
        /// Expression being cast.
        /// </summary>
        public Expression Expression { get; }

        /// <summary>
        /// Type to cast to.
        /// </summary>
        public Type TargetType { get; }

        /// <summary>
        /// Denotes whether the cast was written on the left side of the expression
        /// (using angle brackets) or on the right side of the expresssion (using
        /// "as" notation).
        ///
        /// There is no semantic difference between these two kinds of cast.
        /// </summary>
        public TypeAssertionKind CastKind { get; }

        /// <nodoc />
        public CastExpression(Expression expression, Type type, TypeAssertionKind castKind, LineInfo location)
            : base(location)
        {
            Contract.Requires(expression != null);
            Contract.Requires(type != null);

            Expression = expression;
            TargetType = type;
            CastKind = castKind;
        }

        /// <nodoc />
        public CastExpression(DeserializationContext context, LineInfo location)
            : base(location)
        {
            Expression = ReadExpression(context);
            TargetType = ReadType(context);
            CastKind = (TypeAssertionKind)context.Reader.ReadByte();
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            Expression.Serialize(writer);
            TargetType.Serialize(writer);
            writer.Write((byte)CastKind);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.CastExpression;

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override string ToDebugString()
        {
            switch (CastKind)
            {
                case TypeAssertionKind.TypeCast:
                    return $"<{TargetType.ToDebugString()}> ({Expression.ToDebugString()})";
                case TypeAssertionKind.AsCast:
                    return $"{Expression} as {TargetType}";
                default:
                    Contract.Assert(false, $"Unknown cast kind: {CastKind}");
                    return null;
            }
        }

        /// <inheritdoc />
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            // We ignore types, so no need to evaluate the type.
            return Expression.Eval(context, env, frame);
        }
    }
}
