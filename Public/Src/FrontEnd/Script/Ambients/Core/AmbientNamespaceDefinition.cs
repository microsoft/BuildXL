// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace BuildXL.FrontEnd.Script.Ambients
{
    /// <summary>
    /// Definition of an ambient namespace.
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    public readonly struct AmbientNamespaceDefinition
    {
        private readonly NamespaceFunctionDefinition[] m_functionDefinitions;

        /// <nodoc />
        public AmbientNamespaceDefinition(string name, NamespaceFunctionDefinition[] functionDefinitions)
        {
            m_functionDefinitions = functionDefinitions;
            Name = name;
        }

        /// <summary>
        /// Name of the namespace.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// List of function definitions for the namespace.
        /// </summary>
        public IReadOnlyList<NamespaceFunctionDefinition> FunctionDefinitions => m_functionDefinitions;
    }
}
