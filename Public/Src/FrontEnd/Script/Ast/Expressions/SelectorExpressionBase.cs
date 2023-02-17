// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities.Core;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Expressions
{
    /// <summary>
    /// Base class for different kinds of selector expressions.
    /// </summary>
    /// <remarks>
    /// DScript V2 introduced new selector expression that is based on the result of a semantic resolution.
    /// This is a base type that represents a commonality in our domain model.
    /// </remarks>
    public abstract class SelectorExpressionBase : Expression
    {
        /// <nodoc/>
        [SuppressMessage("StyleCop.CSharp.NamingRules", "SA1304:NonPrivateReadonlyFieldsMustBeginWithUpperCaseLetter")]
        protected readonly Expression m_thisExpression;

        /// <nodoc/>
        protected SelectorExpressionBase(Expression thisExpression, LineInfo location)
            : base(location)
        {
            Contract.Requires(thisExpression != null);

            m_thisExpression = thisExpression;
        }

        /// <summary>
        /// Selector.
        /// </summary>
        public abstract SymbolAtom Selector { get; }

        /// <summary>
        /// This expression
        /// </summary>
        public Expression ThisExpression => m_thisExpression;
    }
}
