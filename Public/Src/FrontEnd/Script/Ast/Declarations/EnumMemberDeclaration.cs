// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities;
using static BuildXL.Utilities.FormattableStringEx;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Declarations
{
    /// <summary>
    /// Member of enum declaration.
    /// </summary>
    public class EnumMemberDeclaration : Declaration
    {
        /// <summary>
        /// Name.
        /// </summary>
        public SymbolAtom Name { get; }

        /// <summary>
        /// Expression.
        /// </summary>
        public Expression Expression { get; }

        /// <summary>
        /// Decorators.
        /// </summary>
        public IReadOnlyList<Expression> Decorators { get; }

        /// <nodoc />
        public EnumMemberDeclaration(SymbolAtom name, Expression expression, DeclarationFlags modifier, IReadOnlyList<Expression> decorators, LineInfo location)
            : base(modifier, location)
        {
            Contract.Requires(name.IsValid);
            Contract.Requires(decorators != null);
            Contract.RequiresForAll(decorators, d => d != null);

            Name = name;
            Expression = expression;
            Decorators = decorators;
        }

        /// <nodoc />
        public EnumMemberDeclaration(DeserializationContext context, LineInfo location)
            : base(ReadModifier(context.Reader), location)
        {
            Name = context.Reader.ReadSymbolAtom();
            Expression = ReadExpression(context);
            Decorators = ReadExpressions(context);
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            WriteModifier(Modifier, writer);
            writer.Write(Name);
            Expression.Serialize(writer);
            WriteExpressions(Decorators, writer);
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.EnumMember;

        /// <inheritdoc />
        public override string ToDebugString()
        {
            string decorators = ToDebugString(Decorators);

            var initializer = Expression == null ? string.Empty : I($" = {Expression}");
            return I($"{decorators}{ToDebugString(Name)}{initializer}");
        }
    }
}
