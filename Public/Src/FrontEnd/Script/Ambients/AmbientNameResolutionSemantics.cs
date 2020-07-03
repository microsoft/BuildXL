// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities.Configuration;

namespace BuildXL.FrontEnd.Script.Ambients
{
    /// <summary>
    /// Ambient definition for NameResolutionSemantics enum.
    /// </summary>
    public sealed class AmbientNameResolutionSemantics : AmbientEnumDefinitionBase
    {
        /// <nodoc />
        public AmbientNameResolutionSemantics(PrimitiveTypes knownTypes)
            : base(knownTypes)
        {
        }

        /// <inheritdoc />
        protected override AmbientNamespaceDefinition? GetNamespaceDefinition()
        {
            var members = new List<NamespaceFunctionDefinition>();
            foreach (NameResolutionSemantics semantics in Enum.GetValues(typeof(NameResolutionSemantics)))
            {
                var memberName = ToCamelCase(semantics.ToString());
                members.Add(Function(memberName, ModuleBinding.CreateEnum(Symbol(memberName), (int)semantics)));
            }

            return new AmbientNamespaceDefinition(
                typeof(NameResolutionSemantics).Name,
                members.ToArray());
        }
    }
}
