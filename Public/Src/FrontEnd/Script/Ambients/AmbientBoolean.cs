// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Script.Ambients
{
    /// <summary>
    /// Ambient definition for type Boolean.
    /// </summary>
    public sealed class AmbientBoolean : AmbientDefinition<bool>
    {
        /// <nodoc />
        public AmbientBoolean(PrimitiveTypes knownTypes)
            : base("Boolean", knownTypes)
        {
        }

        /// <inheritdoc />
        protected override Dictionary<StringId, CallableMember<bool>> CreateMembers()
        {
            // Boolean does not have any new member functions.
            return new Dictionary<StringId, CallableMember<bool>>();
        }
    }
}
