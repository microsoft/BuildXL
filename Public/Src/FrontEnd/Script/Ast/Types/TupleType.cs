// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Utilities;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Types
{
    /// <summary>
    /// Tuple type.
    /// </summary>
    public class TupleType : Type
    {
        /// <summary>
        /// Element types.
        /// </summary>
        public IReadOnlyList<Type> ElementTypes { get; }

        /// <nodoc />
        public TupleType(IReadOnlyList<Type> elementTypes, LineInfo location)
            : base(location)
        {
            Contract.Requires(elementTypes != null);
            Contract.Requires(elementTypes.Count > 0);
            Contract.RequiresForAll(elementTypes, t => t != null);

            ElementTypes = elementTypes;
        }

        /// <nodoc />
        public TupleType(DeserializationContext context, LineInfo location)
            : base(location)
        {
            ElementTypes = ReadArrayOf<Type>(context);
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            WriteArrayOf(ElementTypes, writer);
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.TupleType;

        /// <inheritdoc />
        public override string ToStringShort(StringTable stringTable)
        {
            return "[" + string.Join(", ", ElementTypes.Select(t => t.ToStringShort(stringTable))) + "]";
        }

        /// <inheritdoc />
        public override string ToDebugString()
        {
            return "[" + string.Join(", ", ElementTypes.Select(t => t.ToDebugString())) + "]";
        }
    }
}
