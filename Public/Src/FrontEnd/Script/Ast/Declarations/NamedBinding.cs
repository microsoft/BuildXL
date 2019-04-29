// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using TypeScript.Net.Utilities;

namespace BuildXL.FrontEnd.Script.Declarations
{
    /// <summary>
    /// Named binding.
    /// </summary>
    public abstract class NamedBinding : Declaration
    {
        /// <nodoc />
        protected NamedBinding(LineInfo location)
            : base(DeclarationFlags.None, location)
        {
        }

        /// <nodoc />
        protected NamedBinding(DeserializationContext context, LineInfo location)
            : base(context, location)
        {
        }
    }
}
