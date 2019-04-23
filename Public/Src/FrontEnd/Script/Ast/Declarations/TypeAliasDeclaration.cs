// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.FrontEnd.Script.Types;
using BuildXL.Utilities;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Declarations
{
    /// <summary>
    /// Type alias declaration.
    /// </summary>
    public class TypeAliasDeclaration : Declaration
    {
        /// <summary>
        /// Alias name.
        /// </summary>
        public SymbolAtom Name { get; }

        /// <summary>
        /// Aliased type.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1721:PropertyNamesShouldNotMatchGetMethods")]
        public Type Type { get; }

        /// <summary>
        /// Type parameters of this alias.
        /// </summary>
        public IReadOnlyList<TypeParameter> TypeParameters { get; }

        /// <nodoc />
        public TypeAliasDeclaration(SymbolAtom name, IReadOnlyList<TypeParameter> typeParameters, Type type, DeclarationFlags modifier, LineInfo location)
            : base(modifier, location)
        {
            Contract.Requires(name.IsValid);
            Contract.Requires(type != null);
            Contract.Requires(typeParameters != null);

            Name = name;
            Type = type;
            TypeParameters = typeParameters;
        }

        /// <nodoc />
        public TypeAliasDeclaration(DeserializationContext context, LineInfo location)
            : base(context, location)
        {
            Name = ReadSymbolAtom(context);
            Type = Read<Type>(context);
            TypeParameters = ReadArrayOf<TypeParameter>(context);
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            base.DoSerialize(writer);

            WriteSymbolAtom(Name, writer);
            Serialize(Type, writer);
            WriteArrayOf(TypeParameters, writer);
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.TypeAliasDeclaration;

        /// <inheritdoc />
        public override string ToDebugString()
        {
            string typeParams = TypeParameters.Count == 0 ? string.Empty : "<" + string.Join(", ", TypeParameters.Select(p => p.ToString())) + ">";
            return "type " + ToDebugString(Name) + typeParams + " = " + Type + ";";
        }
    }
}
