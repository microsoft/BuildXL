// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using JetBrains.Annotations;
using TypeScript.Net.Parsing;
using TypeScript.Net.Types;

namespace TypeScript.Net.DScript
{
    /// <summary>
    /// Builder type for constructing variable declarations.
    /// </summary>
    public sealed class VariableDeclarationBuilder
    {
        private Visibility m_visibility = DScript.Visibility.None;
        private bool m_isConst = true;
        private IExpression m_expression;
        private ITypeNode m_type;
        private string m_name;

        /// <nodoc />
        public VariableDeclarationBuilder Visibility(Visibility visibility)
        {
            m_visibility = visibility;
            return this;
        }

        /// <nodoc />
        public VariableDeclarationBuilder Const()
        {
            m_isConst = true;
            return this;
        }

        /// <nodoc />
        public VariableDeclarationBuilder Let()
        {
            m_isConst = false;
            return this;
        }

        /// <nodoc />
        public VariableDeclarationBuilder Name(string name)
        {
            Contract.Requires(name != null);
            m_name = name;
            return this;
        }

        /// <nodoc />
        public VariableDeclarationBuilder Initializer(IExpression expression)
        {
            Contract.Requires(expression != null);
            m_expression = expression;
            return this;
        }

        /// <nodoc />
        public VariableDeclarationBuilder Type(ITypeNode type)
        {
            Contract.Requires(type != null);
            m_type = type;
            return this;
        }

        /// <nodoc />
        [NotNull]
        public IVariableStatement Build()
        {
            if (m_expression == null)
            {
                throw new InvalidOperationException(FormattableStringEx.I($"The initializer expression was not specified. Did you forget to call Initializer method?"));
            }

            if (m_name == null)
            {
                throw new InvalidOperationException(FormattableStringEx.I($"The name was not specified. Did you forget to call Initializer method?"));
            }

            var nodeFlags = m_isConst ? NodeFlags.Const : NodeFlags.None;
            if (m_visibility == DScript.Visibility.Export || m_visibility == DScript.Visibility.Public)
            {
                nodeFlags |= NodeFlags.Export;
            }

            var result = new VariableStatement(
                m_name,
                m_expression,
                type: m_type,
                flags: nodeFlags);

            if (m_visibility == DScript.Visibility.Public)
            {
                result.WithPublicDecorator();
            }

            return result;
        }
    }
}
