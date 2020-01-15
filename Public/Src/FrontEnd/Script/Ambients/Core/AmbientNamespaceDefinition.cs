// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
