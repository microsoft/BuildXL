// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Types;
using BuildXL.Utilities;
using static BuildXL.Utilities.FormattableStringEx;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Declarations
{
    /// <summary>
    /// Variable Declaration
    /// </summary>
    /// <remarks>
    /// This declaration declares global variables (non function-local variables).
    /// We do not enforce that the initializer to be given to support qualifier variable
    /// declaration.
    /// </remarks>
    public class VarDeclaration : Declaration
    {
        /// <summary>
        /// Declared name.
        /// </summary>
        public SymbolAtom Name { get; }

        /// <summary>
        /// Declared type.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1721:PropertyNamesShouldNotMatchGetMethods")]
        public Type Type { get; }

        /// <summary>
        /// Initializer.
        /// </summary>
        public Expression Initializer { get; }

        /// <nodoc />
        public VarDeclaration(
            SymbolAtom name,
            Type type,
            Expression initializer,
            DeclarationFlags modifier,
            LineInfo location)
            : base(modifier, location)
        {
            Contract.Requires(name.IsValid);

            Name = name;
            Type = type;
            Initializer = initializer;
        }

        /// <nodoc />
        public VarDeclaration(DeserializationContext context, LineInfo location)
            : base(context, location)
        {
            Name = context.Reader.ReadSymbolAtom();
            Type = ReadType(context);
            Initializer = ReadExpression(context);
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            base.DoSerialize(writer);
            writer.Write(Name);
            Serialize(Type, writer);
            Serialize(Initializer, writer);
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.VarDeclaration;

        /// <inheritdoc />
        public override string ToDebugString()
        {
            var type = Type != null ? " : " + Type : string.Empty;
            var initializer = Initializer != null ? " = " + Initializer : string.Empty;

            return I($"{GetModifierString()}let {ToDebugString(Name)}{type}{initializer};");
        }

        /// <inheritdoc/>
        public override string ToStringShort(StringTable stringTable) => "let " + Name.ToString(stringTable);
    }
}
