// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities.Configuration;

namespace BuildXL.FrontEnd.Script.Ambients
{
    /// <nodoc />
    public sealed class AmbientContainerIsolationLevel : AmbientEnumDefinitionBase
    {
        /// <nodoc />
        public AmbientContainerIsolationLevel(PrimitiveTypes knownTypes)
            : base(knownTypes)
        {
            Contract.Requires(knownTypes != null);
        }

        /// <inheritdoc />
        protected override AmbientNamespaceDefinition? GetNamespaceDefinition()
        {
            var members = new List<NamespaceFunctionDefinition>();
            Type containerIsolationLevelType = typeof(ContainerIsolationLevel);
            foreach (string isolationLevelName in Enum.GetNames(containerIsolationLevelType))
            {
                var value = Enum.Parse(containerIsolationLevelType, isolationLevelName);
                var memberName = ToCamelCase(isolationLevelName);
                members.Add(Function(memberName, ModuleBinding.CreateEnum(Symbol(memberName), (int)value)));
            }

            return new AmbientNamespaceDefinition(
                typeof(ContainerIsolationLevel).Name,
                members.ToArray());
        }
    }
}
