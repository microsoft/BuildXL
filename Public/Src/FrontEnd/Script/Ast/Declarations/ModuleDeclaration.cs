// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Utilities;
using static BuildXL.Utilities.FormattableStringEx;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Declarations
{
    /// <summary>
    /// Module declaration, i.e., namespace or module from TypeScript AST perspective.
    /// </summary>
    /// <example>
    /// For code like:
    /// <code>
    /// namespace X
    /// {}
    /// </code>
    /// X is a module. From TypeScript AST perspective this is IModuleDeclaration.
    /// </example>
    public class ModuleDeclaration : Declaration
    {
        /// <summary>
        /// Name.
        /// </summary>
        public SymbolAtom Name { get; }

        /// <summary>
        /// Declarations.
        /// </summary>
        public IReadOnlyList<Declaration> Declarations { get; }

        /// <nodoc />
        public ModuleDeclaration(SymbolAtom name, IReadOnlyList<Declaration> declarations, DeclarationFlags modifier, LineInfo location)
            : base(modifier, location)
        {
            Contract.Requires(name.IsValid);
            Contract.Requires(declarations != null);
            Contract.RequiresForAll(declarations, d => d != null);

            Name = name;
            Declarations = declarations;
        }

        /// <nodoc />
        public ModuleDeclaration(DeserializationContext context, LineInfo location)
            : base(context, location)
        {
            Name = ReadSymbolAtom(context);
            Declarations = ReadArrayOf<Declaration>(context);
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            base.DoSerialize(writer);

            WriteSymbolAtom(Name, writer);
            WriteArrayOf(Declarations, writer);
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.ModuleDeclaration;

        /// <inheritdoc />
        public override string ToDebugString()
        {
            var nl = Environment.NewLine;
            string declarations = string.Join(nl, Declarations.Select(d => d.ToDebugString()));

            return I($"{GetModifierString()}module {ToDebugString(Name)}{nl}{{{nl}{declarations}{nl}}}");
        }
    }
}
