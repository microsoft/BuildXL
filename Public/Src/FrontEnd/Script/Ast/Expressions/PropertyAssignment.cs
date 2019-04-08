// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script;
using BuildXL.Utilities;
using JetBrains.Annotations;
using BuildXL.FrontEnd.Script.Util;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Expressions
{
    /// <summary>
    /// Property assignment.
    /// </summary>
    public class PropertyAssignment : Expression
    {
        /// <nodoc />
        public StringId Name { get; private set; }

        /// <summary>
        /// Expression.
        /// </summary>
        /// <remarks>
        /// If null, then this is a shorthand property assignment.
        /// </remarks>
        [CanBeNull]
        public Expression Expression { get; private set; }

        /// <nodoc />
        public PropertyAssignment(StringId name, Expression expression, LineInfo location)
            : base(location)
        {
            Contract.Requires(name.IsValid);

            Name = name;
            Expression = expression;
        }

        /// <nodoc />
        public PropertyAssignment(DeserializationContext context, LineInfo location)
            : base(location)
        {
            Name = context.Reader.ReadStringId();
            Expression = ReadExpression(context);
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            writer.Write(Name);
            Serialize(Expression, writer);
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.PropertyAssignment;

        /// <summary>
        /// Replaces the values
        /// </summary>
        /// <remarks>
        /// This is a mutating Api. This should not be used by regular interpretor
        /// </remarks>
        public void SetName(StringId name)
        {
            Contract.Requires(name.IsValid);
            Name = name;
        }

        /// <summary>
        /// Replace the value at the given index.
        /// </summary>
        /// <remarks>
        /// This is a mutating Api. This should not be used by regular interpretor
        /// </remarks>
        public void SetExpression(Expression expression)
        {
            Expression = expression;
        }

        /// <inheritdoc />
        public override string ToDebugString()
        {
            string name = ToDebugString(Name);

            if (!StringUtil.IsValidId(name))
            {
                name = '\"' + name + '\"';
            }

            return name + (Expression != null ? " : " + Expression.ToDebugString() : string.Empty);
        }
    }
}
