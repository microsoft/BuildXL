// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Ambients;
using BuildXL.FrontEnd.Script;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;
using static BuildXL.Utilities.FormattableStringEx;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Expressions
{
    /// <summary>
    /// AST expression.
    /// </summary>
    [DebuggerDisplay("{" + nameof(ToDebugString) + "(), nq}")]
    public abstract class Expression : Node
    {
        /// <nodoc />
        protected Expression(LineInfo location)
            : base(location)
        {
        }

        /// <inheritdoc />
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            throw new NotImplementedException(
                I($"Current type '{GetType()}' does not implement '{nameof(DoEval)}' operation"));
        }

        /// <summary>
        /// Checks if a value is truthy.
        /// </summary>
        public static bool IsTruthy(EvaluationResult value)
        {
            // TODO: move this to EvaluationResult itself.
            if (value.IsErrorValue || value.IsUndefined)
            {
                return false;
            }

            if (value.Value is bool b)
            {
                return b;
            }

            return true;
        }

        /// <summary>
        /// Checks if a value is truthy.
        /// </summary>
        public static bool IsTruthy(object value)
        {
            if (value == UndefinedValue.Instance)
            {
                return false;
            }

            if (value == null)
            {
                // TODO: This section can be deprecated because undefined is no longer evaluated to null.
                return false;
            }

            if (value is bool b)
            {
                return b;
            }

            // Enable the block below if you want to have the truthy as in JavaScript.
            // if (value is int)
            // {
            //    return ((int) value) != 0;
            // }

            // string s;
            // if ((s = value as string) != null && string.IsNullOrEmpty(s))
            // {
            //    return false;
            // }

            // if (value is AbsolutePath)
            // {
            //    return ((AbsolutePath) value).IsValid;
            // }

            // if (value is FileArtifact)
            // {
            //    return ((FileArtifact) value).IsValid;
            // }

            // if (value is DirectoryArtifact)
            // {
            //    return ((DirectoryArtifact) value).IsValid;
            // }

            // if (value is PathAtom)
            // {
            //    return ((PathAtom) value).IsValid;
            // }

            // if (value is RelativePath)
            // {
            //    return ((RelativePath) value).IsValid;
            // }

            // var enumValue = value as EnumValue;

            // if (enumValue != null)
            // {
            //    return enumValue.Value != 0;
            // }
            return true;
        }

        /// <summary>
        /// Tries to project this expression based on the given name.
        /// </summary>
        /// <remarks>
        /// This method returns true if the kind of expression is eligible for projection.
        /// The returned value can be undefined though.
        /// </remarks>
        public virtual bool TryProject(
            Context context,
            SymbolAtom name,
            ModuleLiteral origin,
            PredefinedTypes predefinedTypes,
            out EvaluationResult result,
            LineInfo location)
        {
            Contract.Requires(context != null);
            Contract.Requires(name.IsValid);
            Contract.Requires(origin != null);
            Contract.Requires(predefinedTypes != null);

            result = EvaluationResult.Error;
            return false;
        }

        /// <inheritdoc />
        public override string ToDebugString() => $"<expression> {Kind}";
    }
}
