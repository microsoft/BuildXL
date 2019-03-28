// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Expressions;
using LineInfo = TypeScript.Net.Utilities.LineInfo;
using SyntaxKind = TypeScript.Net.Types.SyntaxKind;

namespace BuildXL.FrontEnd.Script.Declarations
{
    /// <summary>
    /// Enum Declaration
    /// </summary>
    public class EnumDeclaration : Declaration
    {
        /// <summary>
        /// Name.
        /// </summary>
        public SymbolAtom Name { get; }

        /// <summary>
        /// Enumerable enumerators.
        /// </summary>
        public IReadOnlyList<EnumMemberDeclaration> EnumMemberDeclarations { get; }

        /// <summary>
        /// Decorators.
        /// </summary>
        public IReadOnlyList<Expression> Decorators { get; }

        /// <nodoc />
        public EnumDeclaration(
            SymbolAtom name,
            IReadOnlyList<EnumMemberDeclaration> enumMemberDeclarations,
            IReadOnlyList<Expression> decorators,
            DeclarationFlags modifier,
            LineInfo location)
            : base(modifier, location)
        {
            Contract.Requires(name.IsValid);
            Contract.Requires(enumMemberDeclarations != null);
            Contract.RequiresForAll(enumMemberDeclarations, e => e != null);
            Contract.Requires(decorators != null);
            Contract.RequiresForAll(decorators, e => e != null);

            Name = name;
            EnumMemberDeclarations = enumMemberDeclarations;
            Decorators = decorators;
        }

        /// <nodoc />
        public EnumDeclaration(DeserializationContext context, LineInfo location)
            : base(context, location)
        {
            Name = ReadSymbolAtom(context);
            EnumMemberDeclarations = ReadArrayOf<EnumMemberDeclaration>(context);
            Decorators = ReadExpressions(context);
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            base.DoSerialize(writer);

            WriteSymbolAtom(Name, writer);
            WriteArrayOf(EnumMemberDeclarations, writer);
            WriteExpressions(Decorators, writer);
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.EnumDeclaration;

        /// <inheritdoc />
        public override string ToDebugString()
        {
            string decorators = ToDebugString(Decorators);

            return decorators + GetModifierString() + "enum " + ToDebugString(Name)
                   + " { "
                   + string.Join(", ", EnumMemberDeclarations.Select(e => e.ToString()))
                   + " }";
        }

        /// <inheritdoc/>
        public override string ToStringShort(StringTable stringTable) => "enum " + Name.ToString(stringTable);
    }
}
