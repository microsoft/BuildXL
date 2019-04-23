// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using BuildXL.FrontEnd.Script.Ambients.Transformers;
using BuildXL.FrontEnd.Script.Values;

namespace BuildXL.FrontEnd.Script.Ambients
{
    /// <summary>
    ///     Ambient definition for ArgumentKind enum.
    /// </summary>
    public sealed class AmbientArgumentKind : AmbientEnumDefinitionBase
    {
        /// <nodoc />
        public AmbientArgumentKind(PrimitiveTypes knownTypes)
            : base(knownTypes)
        {
        }

        /// <inheritdoc />
        protected override AmbientNamespaceDefinition? GetNamespaceDefinition()
        {
            var members = new List<NamespaceFunctionDefinition>();

            foreach (ArgumentKind kind in Enum.GetValues(typeof(ArgumentKind)))
            {
                // Undefined is an artificial value.
                if (kind != ArgumentKind.Undefined)
                {
                    var memberName = ToCamelCase(kind.ToString());
                    members.Add(EnumMember(memberName, (int)kind));
                }
            }

            return new AmbientNamespaceDefinition(
                typeof(ArgumentKind).Name,
                members.ToArray());
        }
    }
}
