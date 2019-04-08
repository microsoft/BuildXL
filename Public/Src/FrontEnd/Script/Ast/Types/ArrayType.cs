// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Types
{
    /// <summary>
    /// Array type.
    /// </summary>
    public class ArrayType : Type
    {
        /// <summary>
        /// The type of the elements in the array.
        /// </summary>
        public Type ElementType { get; }

        /// <nodoc />
        public ArrayType(Type elementType, LineInfo location)
            : base(location)
        {
            Contract.Requires(elementType != null);
            ElementType = elementType;
        }

        /// <nodoc />
        public ArrayType(Type elementType)
            : this(elementType, location: default(LineInfo))
        {
        }

        /// <nodoc />
        public ArrayType(DeserializationContext context, LineInfo location)
            : base(location)
        {
            ElementType = ReadType(context);
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            ElementType.Serialize(writer);
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.ArrayType;

        /// <inheritdoc />
        public override string ToStringShort(StringTable stringTable)
        {
            return ElementType.ToStringShort(stringTable) + "[]";
        }

        /// <inheritdoc />
        public override string ToDebugString()
        {
            return ElementType.ToDebugString() + "[]";
        }
    }
}
