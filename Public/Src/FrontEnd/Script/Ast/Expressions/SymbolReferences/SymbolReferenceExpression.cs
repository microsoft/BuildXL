// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using TypeScript.Net.Utilities;

namespace BuildXL.FrontEnd.Script.Expressions
{
    /// <summary>
    /// Abstract class that represents an expression, that references another expression using some identity.
    /// </summary>
    public abstract class SymbolReferenceExpression : Expression
    {
        /// <nodoc/>
        protected SymbolReferenceExpression(LineInfo location)
            : base(location)
        {
        }
    }
}
