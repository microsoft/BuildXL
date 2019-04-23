// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Expressions
{
    /// <summary>
    /// Symbol reference that uses name as a symbol identity.
    /// </summary>
    /// <remarks>
    /// In DScript v1 implementation, custon name resolution logic was used.
    /// This 'symbol reference' allows the interpreter to find a name at runtime
    /// using custom name resolution logic.
    /// This class would be obsoleted once all the logic would be moved to V2.
    /// </remarks>
    public sealed class NameBasedSymbolReference : SymbolReferenceExpression
    {
        /// <summary>
        /// Id name.
        /// </summary>
        public SymbolAtom Name { get; }

        /// <nodoc />
        public NameBasedSymbolReference(SymbolAtom name, LineInfo location)
            : base(location)
        {
            Contract.Requires(name.IsValid);
            Name = name;
        }

        /// <nodoc/>
        public NameBasedSymbolReference(DeserializationContext context, LineInfo location)
            : base(location)
        {
            Name = context.Reader.ReadSymbolAtom();
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.NameBasedSymbolReference;

        /// <inheritdoc />
        public override string ToDebugString()
        {
            return ToDebugString(Name);
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            writer.Write(Name);
        }

        /// <inheritdoc />
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            return env.GetOrEvalField(context, Name, recurs: true, origin: env, location: Location);
        }
    }
}
