// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Utilities;
using static BuildXL.Utilities.FormattableStringEx;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Types
{
    /// <summary>
    /// Function type.
    /// </summary>
    public class FunctionType : Type
    {
        /// <nodoc />
        public Type ReturnType { get; }

        /// <nodoc />
        public IReadOnlyList<TypeParameter> TypeParameters { get; }

        /// <nodoc />
        public IReadOnlyList<Parameter> Parameters { get; }

        /// <nodoc />
        public FunctionType(
            IReadOnlyList<TypeParameter> typeParameters,
            IReadOnlyList<Parameter> parameters,
            Type returnType,
            LineInfo location)
            : base(location)
        {
            Contract.Requires(typeParameters != null);
            Contract.Requires(parameters != null);
            Contract.Requires(returnType != null);
            Contract.RequiresForAll(typeParameters, p => p != null);
            Contract.RequiresForAll(parameters, p => p != null);

            TypeParameters = typeParameters;
            Parameters = parameters;
            ReturnType = returnType;
        }

        /// <nodoc />
        public FunctionType(DeserializationContext context, LineInfo location)
            : base(location)
        {
            TypeParameters = ReadArrayOf<TypeParameter>(context);
            Parameters = ReadArrayOf<Parameter>(context);
            ReturnType = ReadType(context);
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            WriteArrayOf(TypeParameters, writer);
            WriteArrayOf(Parameters, writer);
            Serialize(ReturnType, writer);
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.FunctionType;

        /// <inheritdoc />
        public override string ToStringShort(StringTable stringTable)
        {
            return string.Join(", ", Parameters.Select(p => p.ToStringShort(stringTable)));
        }

        /// <inheritdoc />
        public override string ToDebugString()
        {
            string typeParameters = TypeParameters.Count > 0
                ? "<" + string.Join(", ", TypeParameters.Select(tp => tp.ToDebugString())) + ">"
                : string.Empty;

            string parameters = string.Join(", ", Parameters.Select(p => p.ToDebugString()));

            return I($"{typeParameters}({parameters}) => {ReturnType.ToDebugString()}");
        }
    }
}
