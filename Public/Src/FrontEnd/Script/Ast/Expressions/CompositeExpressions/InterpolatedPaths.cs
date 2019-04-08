// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script;
using BuildXL.FrontEnd.Script.Ambients;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Expressions.CompositeExpressions
{
    /// <summary>
    /// Represent a collection of path-like fragments to be combined. Evaluation happens lazily.
    /// </summary>
    public class InterpolatedPaths : Expression
    {
        private readonly IReadOnlyList<Expression> m_paths;

        /// <summary>
        /// Whether the head of the interpolation is a relative path or an absolute path
        /// </summary>
        public bool HeadIsRelativePath { get; }

        /// <nodoc/>
        public InterpolatedPaths(IReadOnlyList<Expression> paths, bool headIsRelativePath, LineInfo location)
            : base(location)
        {
            Contract.Requires(paths.Count > 0);

            m_paths = paths;
            HeadIsRelativePath = headIsRelativePath;
        }

        /// <nodoc />
        public InterpolatedPaths(DeserializationContext context, LineInfo location)
            : base(location)
        {
            m_paths = ReadArrayOf<Expression>(context);
            HeadIsRelativePath = context.Reader.ReadBoolean();
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            WriteArrayOf(m_paths, writer);
            writer.Write(HeadIsRelativePath);
        }

        /// <inheritdoc/>
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc/>
        public override SyntaxKind Kind => SyntaxKind.InterpolatedPaths;

        /// <inheritdoc/>
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            var root = m_paths[0].Eval(context, env, frame);
            if (root.IsErrorValue)
            {
                return EvaluationResult.Error;
            }

            var evaluatedPaths = new EvaluationResult[m_paths.Count - 1];

            for (var i = 1; i < m_paths.Count; i++)
            {
                var result = m_paths[i].Eval(context, env, frame);
                if (result.IsErrorValue)
                {
                    return EvaluationResult.Error;
                }

                evaluatedPaths[i - 1] = result;
            }

            try
            {
                return HeadIsRelativePath
                    ? AmbientRelativePath.Interpolate(context, root, evaluatedPaths)
                    : AmbientPath.Interpolate(context, root, evaluatedPaths);
            }
            catch (ConvertException convertException)
            {
                // ConversionException derives from EvaluationException but should be handled separatedly,
                context.Errors.ReportUnexpectedValueTypeOnConversion(env, convertException, Location);
            }
            catch (EvaluationException e)
            {
                e.ReportError(context.Errors, env, Location, expression: this, context: context);
            }
            catch (OperationCanceledException)
            {
                return EvaluationResult.Canceled;
            }
            catch (Exception exception)
            {
                context.Errors.ReportUnexpectedAmbientException(env, exception, Location);

                // Getting here indicates a bug somewhere in the evaluator. Who knows what went wrong.
                // Let's re-throw and let some other global exception handler deal with it!
                throw;
            }

            return EvaluationResult.Error;
        }

        /// <summary>
        /// Paths that are part of the interpolated expression
        /// </summary>
        public IReadOnlyList<Expression> GetPaths()
        {
            return m_paths;
        }
    }
}
