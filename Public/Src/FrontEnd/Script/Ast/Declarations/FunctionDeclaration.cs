// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Statements;
using BuildXL.FrontEnd.Script.Types;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities;
using JetBrains.Annotations;
using static BuildXL.Utilities.FormattableStringEx;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Declarations
{
    /// <summary>
    /// Function Declaration
    /// </summary>
    public sealed class FunctionDeclaration : Declaration
    {
        /// <summary>
        /// Function name.
        /// </summary>
        public SymbolAtom Name { get; }

        /// <summary>
        /// Signature of the current function.
        /// </summary>
        [JetBrains.Annotations.NotNull]
        public CallSignature CallSignature { get; }

        /// <summary>
        /// Function body.
        /// </summary>
        [JetBrains.Annotations.NotNull]
        public Statement Body { get; }

        /// <summary>
        /// Number of captured variables.
        /// </summary>
        /// <remarks>
        /// DScript does not support local functions, only lambda expressions,
        /// but even top-level functions can capture anclosing context.
        /// </remarks>
        public int Captures { get; }

        /// <summary>
        /// Number of local variables.
        /// </summary>
        public int Locals { get; }

        /// <summary>
        /// Function calls statistics..
        /// </summary>
        [JetBrains.Annotations.NotNull]
        public FunctionStatistic Statistic { get; }

        /// <nodoc />
        public FunctionDeclaration(
            [JetBrains.Annotations.NotNull] IReadOnlyList<SymbolAtom> @namespace,
            SymbolAtom name,
            [JetBrains.Annotations.NotNull] CallSignature callSignature,
            [JetBrains.Annotations.NotNull] Statement body,
            int captures,
            int locals,
            DeclarationFlags modifier,
            LineInfo location,
            StringTable stringTable)
            : base(modifier, location)
        {
            Contract.Requires(name.IsValid);
            Contract.Requires(callSignature != null);
            Contract.Requires(body != null);
            Contract.Requires(captures >= 0);
            Contract.Requires(locals >= 0);

            Name = name;
            CallSignature = callSignature;
            Body = body;
            Captures = captures;
            Locals = locals;

            var fullName = @namespace.ToList();
            fullName.Add(name);
            Statistic = new FunctionStatistic(fullName, callSignature, stringTable);
        }

        /// <nodoc />
        public FunctionDeclaration(DeserializationContext context, LineInfo location)
            : base(context, location)
        {
            Name = ReadSymbolAtom(context);
            CallSignature = Read<CallSignature>(context);
            Body = Read<Statement>(context);
            Captures = context.Reader.ReadInt32Compact();
            Locals = context.Reader.ReadInt32Compact();
            string fullName = context.Reader.ReadString();
            Statistic = new FunctionStatistic(fullName);
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            base.DoSerialize(writer);

            WriteSymbolAtom(Name, writer);
            Serialize(CallSignature, writer);
            Serialize(Body, writer);
            writer.WriteCompact(Captures);
            writer.WriteCompact(Locals);
            writer.Write(Statistic.FullName);
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.FunctionDeclaration;

        /// <inheritdoc />
        public override string ToDebugString()
        {
            return GetModifierString() + "function " + ToDebugString(Name)
                   + CallSignature
                   + ((Modifier & DeclarationFlags.Ambient) != 0 ? ";" : " " + Body);
        }

        /// <inheritdoc/>
        public override string ToStringShort(StringTable stringTable) => I($"function '{Name.ToString(stringTable)}'");

        /// <inheritdoc />
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            if ((Modifier & DeclarationFlags.Ambient) != 0)
            {
                throw new InvalidOperationException("Unable to evaluate ambient function");
            }

            Contract.Assert(false, "Should never happen");
            var lambda = FunctionLikeExpression.CreateFunction(Name, CallSignature, Body, Captures, Locals, location: Location, statistic: Statistic);
            return EvaluationResult.Create(new Closure(env, lambda, frame));
        }
    }
}
