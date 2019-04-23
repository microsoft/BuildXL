// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Types
{
    /// <summary>
    /// Type parameter.
    /// </summary>
    public class TypeParameter : Node
    {
        /// <nodoc />
        public SymbolAtom ParameterName { get; }

        /// <nodoc />
        public Type ExtendedType { get; }

        /// <nodoc />
        public TypeParameter(SymbolAtom parameterName, Type extendedType, LineInfo location)
            : base(location)
        {
            Contract.Requires(parameterName.IsValid);

            ParameterName = parameterName;
            ExtendedType = extendedType;
        }

        /// <nodoc />
        public TypeParameter(DeserializationContext context, LineInfo location)
            : base(location)
        {
            var reader = context.Reader;
            ParameterName = reader.ReadSymbolAtom();
            ExtendedType = ReadType(context);
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            writer.Write(ParameterName);
            Serialize(ExtendedType, writer);
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
        public override SyntaxKind Kind => SyntaxKind.TypeParameter;

        /// <inheritdoc />
        public override string ToDebugString()
        {
            return ToDebugString(ParameterName) + (ExtendedType != null ? " extends " + ExtendedType : string.Empty);
        }
    }
}
