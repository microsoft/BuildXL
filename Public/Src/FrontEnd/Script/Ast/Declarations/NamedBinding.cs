// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
