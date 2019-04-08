// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using JetBrains.Annotations;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Types;
using static BuildXL.Utilities.FormattableStringEx;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Statements
{
    /// <summary>
    /// Variable statement/declaration.
    /// </summary>
    public class VarStatement : Statement
    {
        /// <nodoc />
        public SymbolAtom Name { get; }

        /// <nodoc />
        public int Index { get; }

        /// <summary>
        /// Declared type.
        /// </summary>
        [SuppressMessage("Microsoft.Naming", "CA1721:PropertyNamesShouldNotMatchGetMethods")]
        [CanBeNull]
        public Type Type { get; }

        /// <nodoc />
        [CanBeNull]
        public Expression Initializer { get; }

        /// <nodoc />
        public VarStatement(
            SymbolAtom name,
            Type type,
            Expression initializer,
            LineInfo location)
            : this(name, 0, type, initializer, location)
        {
            Contract.Requires(name.IsValid);
        }

        /// <nodoc />
        public VarStatement(
            SymbolAtom name,
            int index,
            Type type,
            Expression initializer,
            LineInfo location)
            : base(location)
        {
            Contract.Requires(name.IsValid);
            Contract.Requires(index >= 0);

            Name = name;
            Type = type;
            Initializer = initializer;
            Index = index;
        }

        /// <nodoc />
        public VarStatement(DeserializationContext context, LineInfo location)
            : base(location)
        {
            var reader = context.Reader;
            Name = reader.ReadSymbolAtom();
            Type = ReadType(context);
            Initializer = ReadExpression(context);
            Index = reader.ReadInt32Compact();
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            writer.Write(Name);
            Serialize(Type, writer);
            Serialize(Initializer, writer);
            writer.WriteCompact(Index);
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.VarStatement;

        /// <inheritdoc />
        public override string ToDebugString()
        {
            var type = Type != null ? " : " + Type.ToDebugString() : string.Empty;
            var initializer = Initializer != null ? " = " + Initializer.ToDebugString() : string.Empty;

            return I($"let {ToDebugString(Name)}{type}{initializer};");
        }

        /// <inheritdoc />
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            Contract.Assume(frame.Length > Index);

            if (Initializer != null)
            {
                var initializer = Initializer.Eval(context, env, frame);
                frame[Index] = initializer;

                if (initializer.IsErrorValue)
                {
                    return EvaluationResult.Error;
                }
            }

            return EvaluationResult.Undefined;
        }
    }
}
