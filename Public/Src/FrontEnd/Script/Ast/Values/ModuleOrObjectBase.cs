// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.Utilities.Core;
using JetBrains.Annotations;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Values
{
    /// <summary>
    /// Base class for modules and objects.
    /// </summary>
    public abstract class ModuleOrObjectBase : Expression
    {
        /// <nodoc />
        protected ModuleOrObjectBase(LineInfo location)
            : base(location)
        {
        }

        /// <summary>
        /// Gets or evaluates field.
        /// </summary>
        /// <remarks>
        /// Fields may need to be evaluated. This happens in the case of module fields that have thunked expressions.
        /// </remarks>
        public abstract EvaluationResult GetOrEvalField(
            [NotNull]Context context,
            SymbolAtom name,
            bool recurs,
            [NotNull]ModuleLiteral origin,
            LineInfo location);
    }
}
