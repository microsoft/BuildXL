// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Values;
using TypeScript.Net.Utilities;

namespace BuildXL.FrontEnd.Script.Types
{
    /// <summary>
    /// Signature.
    /// </summary>
    public abstract class Signature : Node
    {
        /// <nodoc />
        protected Signature(LineInfo location)
            : base(location)
        {
        }

        /// <inheritdoc />
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            throw new NotImplementedException();
        }
    }
}
