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
    /// Literal that represents a file.
    /// </summary>
    /// <remarks>
    /// This literal is created from the following syntax:
    /// <code>
    /// let f1 = f`foo.cs`;
    /// </code>
    /// FileLiteralExpression could also be used to represent the above code;
    /// this is just an optimization which doesn't have to evaluate a subexpression
    /// like FileLiteralExpression does.
    /// </remarks>
    public class FileLiteral : Expression, IConstantExpression
    {
        /// <summary>
        /// Expression that was specified inside backticks in the f`` factory method.
        /// </summary>
        public FileArtifact Value { get; }

        /// <inheritdoc />
        object IConstantExpression.Value => Value;

        /// <summary>
        /// Constructor that takes path literal expression.
        /// </summary>
        public FileLiteral(AbsolutePath value, LineInfo location)
            : base(location)
        {
            Contract.Requires(value.IsValid);
            Value = FileArtifact.CreateSourceFile(value);
        }

        /// <summary>
        /// Constructor that takes path literal and rewrite count.
        /// </summary>
        public FileLiteral(AbsolutePath value, int rewriteCount, LineInfo location)
            : base(location)
        {
            Contract.Requires(value.IsValid);
            Value = new FileArtifact(value, rewriteCount);
        }

        /// <nodoc />
        public FileLiteral(DeserializationContext context, LineInfo location)
            : base(location)
        {
            Value = context.Reader.ReadFileArtifact();
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
        public override SyntaxKind Kind => SyntaxKind.FileLiteral;

        /// <inheritdoc />
        public override string ToDebugString()
        {
            return I($"{Constants.Names.FileInterpolationFactory}`{Value}`");
        }

        /// <inheritdoc />
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            return EvaluationResult.Create(Value);
        }
    }
}
