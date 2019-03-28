// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
