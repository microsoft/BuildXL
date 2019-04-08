// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using static BuildXL.Utilities.FormattableStringEx;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Types
{
    /// <summary>
    /// Function type.
    /// </summary>
    public class CallSignature : Signature
    {
        /// <summary>
        /// Return type.
        /// </summary>
        public Type ReturnType { get; }

        /// <summary>
        /// Type parameters.
        /// </summary>
        public IReadOnlyList<TypeParameter> TypeParameters { get; }

        /// <summary>
        /// Parameters.
        /// </summary>
        public IReadOnlyList<Parameter> Parameters { get; }

        /// <nodoc />
        public CallSignature(
            IReadOnlyList<TypeParameter> typeParameters,
            IReadOnlyList<Parameter> parameters,
            Type returnType,
            LineInfo location)
            : base(location)
        {
            Contract.Requires(typeParameters != null);
            Contract.Requires(parameters != null);
            Contract.RequiresForAll(typeParameters, p => p != null);
            Contract.RequiresForAll(parameters, p => p != null);

            TypeParameters = typeParameters;
            Parameters = parameters;
            ReturnType = returnType;
        }

        /// <nodoc />
        public CallSignature(
            IReadOnlyList<Parameter> parameters,
            Type returnType,
            LineInfo location)
            : this(CollectionUtilities.EmptyArray<TypeParameter>(), parameters, returnType, location)
        {
            Contract.Requires(parameters != null);
            Contract.RequiresForAll(parameters, p => p != null);
        }

        /// <nodoc />
        public CallSignature(DeserializationContext context, LineInfo location)
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
        public override SyntaxKind Kind => SyntaxKind.CallSignature;

        /// <inheritdoc />
        public override string ToStringShort(StringTable stringTable)
        {
            return I($"({string.Join(", ", Parameters.Select(p => p.ToStringShort(stringTable)))})");
        }

        /// <inheritdoc />
        public override string ToDebugString()
        {
            string typeParameters = TypeParameters.Count > 0
                ? "<" + string.Join(", ", TypeParameters.Select(tp => tp.ToDebugString())) + ">"
                : string.Empty;

            string parameters = I($"({string.Join(", ", Parameters.Select(p => p.ToDebugString()))})");

            string returnType = ReturnType != null ? ": " + ReturnType.ToDebugString() : string.Empty;

            return typeParameters + parameters + returnType;
        }
    }
}
