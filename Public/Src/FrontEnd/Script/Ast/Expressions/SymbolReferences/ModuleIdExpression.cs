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
    /// Module identifier expression.
    /// </summary>
    /// <remarks>
    /// Module identifier is made mutable for efficiency.
    /// This class is being used in custom name resolution in DScript v1.
    /// This class would be obsoleted once all the logic would be moved to V2.
    /// </remarks>
    public class ModuleIdExpression : SymbolReferenceExpression
    {
        /// <nodoc />
        public FullSymbol Name { get; private set; }

        /// <nodoc />
        public ModuleIdExpression(FullSymbol name, LineInfo location)
            : base(location)
        {
            Contract.Requires(name.IsValid);
            Name = name;
        }

        /// <nodoc />
        public ModuleIdExpression(DeserializationContext context, LineInfo location)
            : base(location)
        {
            Name = context.Reader.ReadFullSymbol();
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            writer.Write(Name);
        }

        /// <summary>
        /// Sets a new name for this module id expression.
        /// </summary>
        /// <remarks>
        /// This method is unsafe for multi-threaded. This method is only called by the parser which is single-threaded.
        /// </remarks>
        public void SetName(FullSymbol name)
        {
            Contract.Requires(name.IsValid);
            Name = name;
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.ModuleIdExpression;

        /// <inheritdoc />
        public override string ToDebugString()
        {
            return ToDebugString(Name);
        }

        /// <inheritdoc />
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            return env.GetNamespace(context, Name, recurs: true, origin: env, location: Location);
        }
    }
}
