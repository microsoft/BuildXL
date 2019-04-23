// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities;
using static BuildXL.Utilities.FormattableStringEx;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Literals
{
    /// <summary>
    /// Path atom literal.
    /// </summary>
    public sealed class PathAtomLiteral : Expression, IPathLikeLiteral
    {
        /// <nodoc />
        public PathAtom Value { get; }

        /// <nodoc />
        public PathAtomLiteral(PathAtom value, LineInfo location)
            : base(location)
        {
            Contract.Requires(value.IsValid);
            Value = value;
        }

        /// <nodoc />
        public PathAtomLiteral(DeserializationContext context, LineInfo location)
            : base(location)
        {
            Value = context.Reader.ReadPathAtom();
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            writer.Write(Value);
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.PathAtomLiteral;

        /// <inheritdoc />
        public override string ToDebugString()
        {
            return I($"{Constants.Names.PathAtomInterpolationFactory}`{ToDebugString(Value.StringId)}`");
        }

        /// <inheritdoc />
        public string ToDisplayString(PathTable table, AbsolutePath currentFolder)
        {
            return Value.ToString(table.StringTable);
        }

        /// <inheritdoc />
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            return EvaluationResult.Create(Value);
        }
    }
}
