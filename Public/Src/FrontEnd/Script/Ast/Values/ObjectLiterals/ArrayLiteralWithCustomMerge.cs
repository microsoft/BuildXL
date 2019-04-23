// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.Utilities;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Values
{
    /// <summary>
    /// An array literal associated with a custom merge function defined in DScript.
    /// </summary>
    public sealed class ArrayLiteralWithCustomMerge : EvaluatedArrayLiteral
    {
        private readonly EvaluationResult m_customMergeFunction;

        /// <summary>
        /// Creates array literal from an existing array, adding a custom merge function
        /// </summary>
        public static ArrayLiteralWithCustomMerge Create(ArrayLiteral arrayLiteral, Closure customMergeClosure, LineInfo location, AbsolutePath path)
        {
            Contract.Assert(arrayLiteral != null);
            Contract.Assert(customMergeClosure != null);
            Contract.Assert(path.IsValid);

            var data = new EvaluationResult[arrayLiteral.Length];
            arrayLiteral.Copy(0, data, 0, arrayLiteral.Length);

            return new ArrayLiteralWithCustomMerge(data, customMergeClosure, location, path);
        }

        /// <nodoc />
        internal ArrayLiteralWithCustomMerge(EvaluationResult[] data, Closure customMergeClosure, LineInfo location, AbsolutePath path)
            : base(data, location, path)
        {
            Contract.Requires(customMergeClosure != null);

            m_customMergeFunction = EvaluationResult.Create(customMergeClosure);
        }

        /// <inheritdoc/>
        protected override MergeFunction GetDefaultMergeFunction(Context context, EvaluationStackFrame captures)
        {
            return GetCustomMergeFunctionFromClosure(context, captures, m_customMergeFunction);
        }

        /// <inheritdoc/>
        protected override MergeFunction TryGetCustomMergeFunction(Context context, EvaluationStackFrame captures)
        {
            return GetCustomMergeFunctionFromClosure(context, captures, m_customMergeFunction);
        }
    }
}
