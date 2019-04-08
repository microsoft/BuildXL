// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
