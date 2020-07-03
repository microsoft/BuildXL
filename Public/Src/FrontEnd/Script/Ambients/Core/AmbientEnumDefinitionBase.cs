// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.FrontEnd.Script.Values;

namespace BuildXL.FrontEnd.Script.Ambients
{
    /// <summary>
    /// Base type for global enum definition.
    /// </summary>
    /// <remarks>
    /// Enums in TypeScript have different meanings but in DScript in terms of ambient enums
    /// they're very similar to namepsaces: they just a global "namespaces" with a list of values.
    /// </remarks>
    public abstract class AmbientEnumDefinitionBase : AmbientDefinitionBase
    {
        /// <nodoc />
        protected AmbientEnumDefinitionBase(PrimitiveTypes knownTypes)
            : base(null, knownTypes)
        {
        }

        /// <nodoc />
        protected static string ToCamelCase(string memberName)
        {
            return char.ToLowerInvariant(memberName[0]).ToString() + memberName.Substring(1);
        }
    }
}
