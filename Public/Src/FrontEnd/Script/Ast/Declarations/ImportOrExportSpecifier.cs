// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using TypeScript.Net.Utilities;

namespace BuildXL.FrontEnd.Script.Declarations
{
    /// <summary>
    /// Import-export specifier.
    /// </summary>
    public abstract class ImportOrExportSpecifier : Declaration
    {
        /// <nodoc />
        protected ImportOrExportSpecifier(LineInfo location)
            : base(DeclarationFlags.None, location)
        {
        }

        /// <nodoc />
        protected ImportOrExportSpecifier(DeserializationContext context, LineInfo lineInfo)
            : base(context, lineInfo)
        {
        }
    }
}
