// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using BuildXL.FrontEnd.Script.Values;

namespace BuildXL.FrontEnd.Script.Ambients
{
    /// <summary>
    /// Definition of the namespace-level ambient function.
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    public readonly struct NamespaceFunctionDefinition
    {
        /// <nodoc />
        public NamespaceFunctionDefinition(string name, ModuleBinding functionDefinition)
        {
            Name = name;
            FunctionDefinition = functionDefinition;
        }

        /// <summary>
        /// Function name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Definition of the ambient function.
        /// </summary>
        public ModuleBinding FunctionDefinition { get; }
    }
}
