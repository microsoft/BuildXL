// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.FrontEnd.Script;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Util;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Values
{
    /// <summary>
    /// Base class that represents light weight object literals: literals with number of fields from 1 to 5.
    /// </summary>
    public abstract class ObjectLiteralSlim : ObjectLiteral
    {
        /// <nodoc/>
        protected ObjectLiteralSlim(LineInfo location, AbsolutePath path)
            : base(location, path)
        { }
    }

    /// <summary>
    /// Generic light weight object literal based on <see cref="StructArray"/>.
    /// </summary>
    /// <remarks>
    /// DScript has tons of object literals with small number of fields, e.g., object literals from
    /// command line argument and key-value pair from environment variables.
    /// Using struct arrays instead of using dictionary for member lookup saves a lot of memory during evaluation phase.
    /// </remarks>
    [SuppressMessage("Microsoft.Maintainability", "CA1501")]
    internal sealed class ObjectLiteralSlim<TArray> : ObjectLiteralSlim
        where TArray : IReadOnlyArraySlim<NamedValue>
    {
        // This field intentionally left as non-readonly to avoid uncesseray copies.
        private TArray m_values;

        public ObjectLiteralSlim(TArray values, LineInfo location, AbsolutePath path)
            : base(location, path)
        {
            m_values = values;
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            // Current type is generic, so to avoid making visitor generic as well
            // We're creating ObjectLiteralN from the current instance
            // and calling visit on the result.
            // This is totally safe, because visitation is only used for pretty printing.
            visitor.Visit(new ObjectLiteralN(m_values.ToReadOnlyList<IReadOnlyArraySlim<NamedValue>, NamedValue>().ToList(), Location, Path));
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.ObjectLiteralSlim;

        private bool TryResolveByName(StringId name, out EvaluationResult result)
        {
            for (int i = 0; i < m_values.Length; i++)
            {
                if (m_values[i].NameId == name.Value)
                {
                    result = m_values[i].Value;
                    return true;
                }
            }

            result = default(EvaluationResult);
            return false;
        }

        /// <inheritdoc />
        public override EvaluationResult GetOrEvalField(Context context, StringId name, bool recurs, ModuleLiteral origin, LineInfo location)
        {
            if (TryResolveByName(name, out EvaluationResult result))
            {
                return result;
            }

            return EvaluationResult.Undefined;
        }

        /// <inheritdoc />
        public override EvaluationResult this[SymbolAtom name]
        {
            get
            {
                Contract.Requires(name.IsValid);
                return this[name.StringId];
            }
        }

        /// <inheritdoc />
        public override EvaluationResult this[StringId name]
        {
            get
            {
                Contract.Requires(name.IsValid);
                if (TryResolveByName(name, out EvaluationResult result))
                {
                    return result;
                }

                return EvaluationResult.Undefined;
            }
        }

        /// <inheritdoc />
        public override int Count => m_values.Length;

        /// <inheritdoc />
        public override bool HasKey(StringId key)
        {
            return TryResolveByName(key, out EvaluationResult dummyValue);
        }

        /// <inheritdoc />
        public override IEnumerable<KeyValuePair<StringId, EvaluationResult>> Members
        {
            get
            {
                for (int i = 0; i < m_values.Length; i++)
                {
                    if (m_values[i].IsValid)
                    {
                        yield return m_values[i].AsKeyValuePair();
                    }
                }
            }
        }

        /// <inheritdoc />
        public override IEnumerable<StringId> Keys
        {
            get
            {
                for (int i = 0; i < m_values.Length; i++)
                {
                    if (m_values[i].IsValid)
                    {
                        yield return new StringId(m_values[i].NameId);
                    }
                }
            }
        }

        private static readonly ObjectPool<List<NamedValue>> NamedValuesObjectPool = Pools.CreateListPool<NamedValue>();

        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            using (var listWrapper = NamedValuesObjectPool.GetInstance())
            {
                var evaluatedValues = listWrapper.Instance;

                for (int i = 0; i < m_values.Length; i++)
                {
                    if (m_values[i].IsValid)
                    {
                        var evaluatedValue = m_values[i].Eval(context, env, frame);
                        if (evaluatedValue.IsError)
                        {
                            return EvaluationResult.Error;
                        }

                        evaluatedValues.Add(evaluatedValue);
                    }
                }

                return EvaluationResult.Create(Create(evaluatedValues, Location, Path));
            }
        }
    }
}
