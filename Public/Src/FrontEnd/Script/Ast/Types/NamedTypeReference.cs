// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Types
{
    /// <summary>
    /// Named type.
    /// </summary>
    public class NamedTypeReference : Type
    {
        /// <summary>
        /// Type name.
        /// </summary>
        /// <remarks>This name is of the form dotted identifier.</remarks>
        public IReadOnlyList<SymbolAtom> TypeName { get; }

        /// <nodoc />
        public IReadOnlyList<Type> TypeArguments { get; }

        /// <nodoc />
        public NamedTypeReference(IReadOnlyList<SymbolAtom> typeName, IReadOnlyList<Type> typeArguments, LineInfo location)
            : base(location)
        {
            Contract.Requires(typeName != null);
            Contract.Requires(typeName.Count > 0);
            Contract.RequiresForAll(typeName, n => n.IsValid);
            Contract.Requires(typeArguments != null);
            Contract.RequiresForAll(typeArguments, t => t != null);

            TypeName = typeName;
            TypeArguments = typeArguments;
        }

        /// <nodoc />
        public NamedTypeReference(SymbolAtom typeName, IReadOnlyList<Type> typeArguments, LineInfo location)
            : this(new[] { typeName }, typeArguments, location)
        {
            Contract.Requires(typeName.IsValid);
            Contract.Requires(typeArguments != null);
            Contract.RequiresForAll(typeArguments, t => t != null);
        }

        /// <nodoc />
        public NamedTypeReference(SymbolAtom typeName, LineInfo location)
            : this(typeName, CollectionUtilities.EmptyArray<Type>(), location)
        {
            Contract.Requires(typeName.IsValid);
        }

        /// <nodoc />
        public NamedTypeReference(SymbolAtom typeName)
            : this(typeName, CollectionUtilities.EmptyArray<Type>(), location: default(LineInfo))
        {
            Contract.Requires(typeName.IsValid);
        }

        /// <nodoc />
        public NamedTypeReference(DeserializationContext context, LineInfo location)
            : base(location)
        {
            TypeName = context.Reader.ReadReadOnlyList(r => r.ReadSymbolAtom());
            TypeArguments = ReadArrayOf<Type>(context);
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            writer.WriteReadOnlyList(TypeName, (buildXLWriter, atom) => buildXLWriter.Write(atom));
            WriteArrayOf(TypeArguments, writer);
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.NamedTypeReference;

        /// <inheritdoc />
        public override string ToStringShort(StringTable stringTable)
        {
            return string.Join(".", TypeName.Select(t => t.ToString(stringTable)));
        }

        /// <inheritdoc />
        public override string ToDebugString()
        {
            var name = string.Join(".", TypeName.Select(ToDebugString));
            var args = TypeArguments.Count > 0
                ? "<" + string.Join(", ", TypeArguments.Select(a => a.ToDebugString())) + ">"
                : string.Empty;

            return name + args;
        }
    }
}
