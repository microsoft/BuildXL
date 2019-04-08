// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using BuildXL.FrontEnd.Script.Values;

namespace BuildXL.FrontEnd.Script.Ambients
{
    /// <nodoc />
    public sealed class AmbientTextEncoding : AmbientEnumDefinitionBase
    {
        /// <nodoc />
        public AmbientTextEncoding(PrimitiveTypes knownTypes)
            : base(knownTypes)
        {
        }

        /// <inheritdoc />
        protected override AmbientNamespaceDefinition? GetNamespaceDefinition()
        {
            var members = new List<NamespaceFunctionDefinition>();

            foreach (TextEncoding kind in Enum.GetValues(typeof(TextEncoding)))
            {
                var memberName = ToCamelCase(kind.ToString());
                members.Add(EnumMember(memberName, (int)kind));
            }

            return new AmbientNamespaceDefinition(
                typeof(TextEncoding).Name,
                members.ToArray());
        }
    }
}
