// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using BuildXL.FrontEnd.Script.Ambients.Transformers;
using BuildXL.FrontEnd.Script.Values;

namespace BuildXL.FrontEnd.Script.Ambients
{
    /// <summary>
    /// Ambient definition for ArtifactKind enum.
    /// </summary>
    public sealed class AmbientArtifactKind : AmbientEnumDefinitionBase
    {
        /// <nodoc />
        public AmbientArtifactKind(PrimitiveTypes knownTypes)
            : base(knownTypes)
        {
        }

        /// <inheritdoc />
        protected override AmbientNamespaceDefinition? GetNamespaceDefinition()
        {
            var members = new List<NamespaceFunctionDefinition>();
            foreach (ArtifactKind kind in Enum.GetValues(typeof(ArtifactKind)))
            {
                // Undefined is an artificial value.
                if (kind != ArtifactKind.Undefined)
                {
                    var memberName = ToCamelCase(kind.ToString());
                    members.Add(Function(memberName, ModuleBinding.CreateEnum(Symbol(memberName), (int)kind)));
                }
            }

            return new AmbientNamespaceDefinition(
                typeof(ArtifactKind).Name,
                members.ToArray());
        }
    }
}
