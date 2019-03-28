// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.FrontEnd.Script;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Types;
using LineInfo = TypeScript.Net.Utilities.LineInfo;
using SyntaxKind = TypeScript.Net.Types.SyntaxKind;

namespace BuildXL.FrontEnd.Script.Expressions
{
    /// <summary>
    /// Apply expression with type arguments.
    /// </summary>
    /// <remarks>
    /// Not using space efficient tricks here because apply expression with type arguments are much more rare right now.
    /// </remarks>
    public class ApplyExpressionWithTypeArguments : ApplyExpression
    {
        /// <nodoc />
        public IReadOnlyList<Type> TypeArguments { get; }

        /// <nodoc />
        public ApplyExpressionWithTypeArguments(Expression functor, IReadOnlyList<Type> typeArguments, Expression[] arguments, LineInfo location)
            : base(functor, arguments, location)
        {
            Contract.Requires(typeArguments != null);
            Contract.Requires(typeArguments.Count > 0);
            Contract.RequiresForAll(typeArguments, t => t != null);

            TypeArguments = typeArguments;
        }

        /// <nodoc />
        public ApplyExpressionWithTypeArguments(DeserializationContext context, LineInfo location)
            : base(ReadExpression(context), ReadExpressions(context), location)
        {
            TypeArguments = ReadArrayOf<Type>(context);
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            this.Functor.Serialize(writer);
            Node.WriteExpressions(Arguments, writer);
            WriteArrayOf(TypeArguments, writer);
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.ApplyExpressionWithTypeArguments;

        /// <inheritdoc />
        public override string ToDebugString()
        {
            var types = "<" + string.Join(", ", TypeArguments.Select(t => t.ToDebugString())) + ">";
            var args = "(" + string.Join(", ", Arguments.Select(arg => arg.ToDebugString())) + ")";

            return Functor + types + args;
        }
    }
}
