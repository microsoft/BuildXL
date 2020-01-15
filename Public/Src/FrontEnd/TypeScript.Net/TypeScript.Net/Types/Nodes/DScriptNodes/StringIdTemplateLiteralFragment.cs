// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities;
using TypeScript.Net.TypeChecking;

namespace TypeScript.Net.Types
{
    /// <nodoc/>
    public sealed partial class StringIdTemplateLiteralFragment : Literal, ITemplateLiteralFragment
    {
        /// <inheritdoc />
        public override int Id => (int)WellKnownNodeIds.StringIdTemplateLiteralId;

        /// <nodoc/>
        public StringId TextAsStringId
        {
            get { return StringId.UnsafeCreateFrom(m_reservedInt); }
            internal set { m_reservedInt = value.Value; }
        }

        /// <inheritdoc />
        public override string Text
        {
            get => TextAsStringId.ToString(PathTable.StringTable);
            set { }
        }

        /// <inheritdoc />
        internal override void Accept(INodeVisitor visitor)
        {
            visitor.VisitTemplateLiteralFragment(this);
        }

        /// <inheritdoc />
        internal override TResult Accept<TResult>(INodeVisitor<TResult> visitor)
        {
            return visitor.VisitTemplateLiteralFragment(this);
        }
    }
}
