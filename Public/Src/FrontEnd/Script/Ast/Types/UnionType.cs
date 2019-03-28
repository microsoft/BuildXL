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
    /// Union type.
    /// </summary>
    public class UnionType : Type
    {
        /// <nodoc />
        public IReadOnlyList<Type> Types { get; }

        /// <nodoc />
        public UnionType(IReadOnlyList<Type> types, LineInfo location)
            : base(location)
        {
            Contract.Requires(types != null);
            Contract.Requires(types.Count > 0);
            Contract.RequiresForAll(types, t => t != null);

            Types = types;
        }

        /// <nodoc />
        public UnionType(DeserializationContext context, LineInfo location)
            : base(location)
        {
            Types = ReadArrayOf<Type>(context);
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            WriteArrayOf(Types, writer);
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.UnionType;

        /// <inheritdoc />
        public override string ToStringShort(StringTable stringTable)
        {
            return string.Join(" | ", Types.Select(t => t.ToStringShort(stringTable)));
        }

        /// <inheritdoc />
        public override string ToDebugString()
        {
            return string.Join(" | ", Types.Select(t => t.ToDebugString()));
        }
    }
}
