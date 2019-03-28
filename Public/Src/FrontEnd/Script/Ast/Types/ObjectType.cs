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
    /// Object type.
    /// </summary>
    public class ObjectType : Type
    {
        /// <nodoc />
        public IReadOnlyList<Signature> Members { get; }

        /// <nodoc />
        public ObjectType(IReadOnlyList<Signature> members, LineInfo location)
            : base(location)
        {
            Contract.Requires(members != null);
            Contract.RequiresForAll(members, m => m != null);

            Members = members;
        }

        /// <nodoc />
        public ObjectType(DeserializationContext context, LineInfo location)
            : base(location)
        {
            Members = ReadArrayOf<Signature>(context);
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            WriteArrayOf(Members, writer);
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.ObjectType;

        /// <inheritdoc />
        public override string ToStringShort(StringTable stringTable)
        {
            return "{ " + string.Join(", ", Members.Select(m => m.ToStringShort(stringTable))) + " }";
        }

        /// <inheritdoc />
        public override string ToDebugString()
        {
            return "{ " + string.Join(", ", Members.Select(m => m.ToDebugString())) + " }";
        }
    }
}
