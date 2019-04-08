// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using LineInfo = TypeScript.Net.Utilities.LineInfo;

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
