// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Types
{
    /// <summary>
    /// Type query (typeof).
    /// </summary>
    public class TypeQuery : Type
    {
        /// <summary>
        /// Type query
        /// </summary>
        public NamedTypeReference EntityName { get; }

        /// <nodoc />
        public TypeQuery(NamedTypeReference entityName, LineInfo location)
            : base(location)
        {
            Contract.Requires(entityName != null);
            EntityName = entityName;
        }

        /// <nodoc />
        public TypeQuery(DeserializationContext context, LineInfo location)
            : base(location)
        {
            EntityName = Read<NamedTypeReference>(context);
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            EntityName.Serialize(writer);
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.TypeQuery;

        /// <inheritdoc />
        public override string ToStringShort(StringTable stringTable)
        {
            return "typeof " + EntityName.ToStringShort(stringTable);
        }

        /// <inheritdoc />
        public override string ToDebugString()
        {
            return "typeof " + EntityName.ToDebugString();
        }
    }
}
