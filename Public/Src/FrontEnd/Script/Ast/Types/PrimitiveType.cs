// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Types
{
    /// <summary>
    /// Defines a primitive type like any, object, number etc..
    /// </summary>
    public sealed class PrimitiveType : Type
    {
        /// <nodoc/>
        public static readonly PrimitiveType AnyType = new PrimitiveType(PrimitiveTypeKind.Any);

        /// <nodoc/>
        public static readonly PrimitiveType UnitType = new PrimitiveType(PrimitiveTypeKind.Unit);

        /// <nodoc/>
        public static readonly PrimitiveType BooleanType = new PrimitiveType(PrimitiveTypeKind.Boolean);

        /// <nodoc/>
        public static readonly PrimitiveType NumberType = new PrimitiveType(PrimitiveTypeKind.Number);

        /// <nodoc/>
        public static readonly PrimitiveType StringType = new PrimitiveType(PrimitiveTypeKind.String);

        /// <nodoc/>
        public static readonly PrimitiveType VoidType = new PrimitiveType(PrimitiveTypeKind.Void);

        private PrimitiveTypeKind TypeKind { get; }

        internal PrimitiveType(PrimitiveTypeKind typeKind, LineInfo location)
            : base(location)
        {
            TypeKind = typeKind;
        }

        private PrimitiveType(PrimitiveTypeKind typeKind)
            : this(typeKind, default(LineInfo))
        {
            TypeKind = typeKind;
        }

        /// <nodoc />
        public PrimitiveType(DeserializationContext context, LineInfo location)
            : base(location)
        {
            TypeKind = (PrimitiveTypeKind)context.Reader.ReadByte();
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            writer.Write((byte)TypeKind);
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.PredefinedType;

        /// <inheritdoc />
        public override string ToDebugString()
        {
            switch (TypeKind)
            {
                case PrimitiveTypeKind.Any:
                    return "any";
                case PrimitiveTypeKind.Number:
                    return "number";
                case PrimitiveTypeKind.Boolean:
                    return "boolean";
                case PrimitiveTypeKind.Unit:
                    return "unit";
                case PrimitiveTypeKind.String:
                    return "string";
                case PrimitiveTypeKind.Void:
                    return "void";
                default:
                    Contract.Assume(false);
                    return TypeKind.ToString();
            }
        }
    }
}
