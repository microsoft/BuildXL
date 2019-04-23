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
    /// Parameter of the function.
    /// </summary>
    /// <remarks>
    /// Note, that default values for arguments are not supported in DScript.
    /// </remarks>
    public class Parameter : Node
    {
        /// <nodoc />
        public SymbolAtom ParameterName { get; }

        /// <nodoc />
        public Type ParameterType { get; }

        /// <nodoc />
        public ParameterKind ParameterKind { get; }

        /// <nodoc />
        public Parameter(SymbolAtom parameterName, Type parameterType, ParameterKind parameterKind, LineInfo location)
            : base(location)
        {
            Contract.Requires(parameterName.IsValid || parameterType != null);

            ParameterName = parameterName;
            ParameterType = parameterType;
            ParameterKind = parameterKind;
        }

        /// <nodoc />
        public Parameter(Type parameterType, ParameterKind parameterKind, LineInfo location)
            : this(SymbolAtom.Invalid, parameterType, parameterKind, location)
        {
            Contract.Requires(parameterType != null);
        }

        /// <nodoc />
        public Parameter(SymbolAtom parameterName, ParameterKind parameterKind, LineInfo location)
            : this(parameterName, null, parameterKind, location)
        {
            Contract.Requires(parameterName.IsValid);
        }

        /// <nodoc />
        public Parameter(DeserializationContext context, LineInfo location)
            : base(location)
        {
            var reader = context.Reader;
            ParameterName = reader.ReadSymbolAtom();
            ParameterType = ReadType(context);
            ParameterKind = (ParameterKind)reader.ReadByte();
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            writer.Write(ParameterName);
            Serialize(ParameterType, writer);
            writer.Write((byte)ParameterKind);
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
        public override SyntaxKind Kind => SyntaxKind.Parameter;

        /// <inheritdoc />
        public override string ToStringShort(StringTable stringTable)
        {
            var name = ParameterName.IsValid ? ParameterName.ToString(stringTable) : string.Empty;

            if (ParameterType != null)
            {
                name = string.Concat(name, ": ", ParameterType.ToStringShort(stringTable));
            }

            return name;
        }

        /// <inheritdoc />
        public override string ToDebugString()
        {
            string name = ParameterName.IsValid
                ? (ParameterKind == ParameterKind.Rest ? "..." : string.Empty) + ToDebugString(ParameterName) +
                  (ParameterKind == ParameterKind.Optional ? "?" : string.Empty)
                : string.Empty;
            name = ParameterName.IsValid && ParameterType != null ? name + ": " : name;

            return name + (ParameterType?.ToDebugString() ?? string.Empty);
        }
    }
}
