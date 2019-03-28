// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Types;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Declarations
{
    /// <summary>
    /// Interface declaration.
    /// </summary>
    public class InterfaceDeclaration : Declaration
    {
        /// <summary>
        /// Interface name.
        /// </summary>
        public SymbolAtom Name { get; }

        /// <summary>
        /// Type parameters.
        /// </summary>
        public IReadOnlyList<TypeParameter> TypeParameters { get; }

        /// <summary>
        /// Extended types.
        /// </summary>
        public IReadOnlyList<NamedTypeReference> ExtendedTypes { get; }

        /// <summary>
        /// Decorators.
        /// </summary>
        public IReadOnlyList<Expression> Decorators { get; }

        /// <summary>
        /// Body.
        /// </summary>
        public ObjectType Body { get; }

        /// <nodoc />
        public InterfaceDeclaration(
            SymbolAtom name,
            IReadOnlyList<TypeParameter> typeParameters,
            IReadOnlyList<NamedTypeReference> extendedTypes,
            IReadOnlyList<Expression> decorators,
            ObjectType body,
            DeclarationFlags modifier,
            LineInfo location)
            : base(modifier, location)
        {
            Contract.Requires(name.IsValid);
            Contract.Requires(typeParameters != null);
            Contract.RequiresForAll(typeParameters, p => p != null);
            Contract.Requires(extendedTypes != null);
            Contract.RequiresForAll(extendedTypes, t => t != null);
            Contract.Requires(decorators != null);
            Contract.RequiresForAll(decorators, d => d != null);
            Contract.Requires(body != null);

            Name = name;
            TypeParameters = typeParameters;
            ExtendedTypes = extendedTypes;
            Body = body;
            Decorators = decorators;
        }

        /// <nodoc />
        public InterfaceDeclaration(DeserializationContext context, LineInfo location)
            : base(context, location)
        {
            Name = ReadSymbolAtom(context);
            TypeParameters = ReadArrayOf<TypeParameter>(context);
            ExtendedTypes = ReadArrayOf<NamedTypeReference>(context);
            Body = Read<ObjectType>(context);
            Decorators = ReadExpressions(context);
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            base.DoSerialize(writer);

            WriteSymbolAtom(Name, writer);
            WriteArrayOf(TypeParameters, writer);
            WriteArrayOf(ExtendedTypes, writer);
            Serialize(Body, writer);
            WriteExpressions(Decorators, writer);
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.InterfaceDeclaration;

        /// <inheritdoc />
        public override string ToDebugString()
        {
            var decorators = ToDebugString(Decorators);

            string typeParameters = TypeParameters.Count > 0
                ? "<" + string.Join(", ", TypeParameters.Select(p => p.ToString())) + ">"
                : string.Empty;
            string extendedTypes = ExtendedTypes.Count > 0
                ? " extends " + string.Join(", ", ExtendedTypes.Select(t => t.ToString()))
                : string.Empty;

            return decorators + GetModifierString() + "interface " + ToDebugString(Name)
                   + typeParameters
                   + extendedTypes
                   + Body;
        }
    }
}
