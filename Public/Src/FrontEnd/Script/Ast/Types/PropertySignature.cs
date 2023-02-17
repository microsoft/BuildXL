// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.Utilities.Core;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Types
{
    /// <summary>
    /// Property signature.
    /// </summary>
    public class PropertySignature : Signature
    {
        /// <nodoc />
        public SymbolAtom PropertyName { get; }

        /// <nodoc />
        public Type PropertyType { get; }

        /// <nodoc />
        public bool IsOptional { get; }

        /// <nodoc />
        public IReadOnlyList<Expression> Decorators { get; }

        /// <nodoc />
        public PropertySignature(SymbolAtom propertyName, Type propertyType, bool isOptional, IReadOnlyList<Expression> decorators, LineInfo location)
            : base(location)
        {
            Contract.Requires(propertyName.IsValid);
            Contract.Requires(propertyType != null);
            Contract.RequiresForAll(decorators, d => d != null);

            PropertyName = propertyName;
            PropertyType = propertyType;
            IsOptional = isOptional;
            Decorators = decorators;
        }

        /// <nodoc />
        public PropertySignature(DeserializationContext context, LineInfo location)
            : base(location)
        {
            var reader = context.Reader;
            PropertyName = reader.ReadSymbolAtom();
            PropertyType = ReadType(context);
            IsOptional = reader.ReadBoolean();
            Decorators = ReadExpressions(context);
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            writer.Write(PropertyName);
            Serialize(PropertyType, writer);
            writer.Write(IsOptional);
            WriteExpressions(Decorators, writer);
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.PropertySignature;

        /// <inheritdoc />
        public override string ToDebugString()
        {
            return ToDebugString(Decorators) + ToDebugString(PropertyName) + (IsOptional ? "?" : string.Empty) + ": " + PropertyType.ToDebugString();
        }
    }
}
