// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities.Configuration;
using BuildXL.FrontEnd.Script.Values;

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
