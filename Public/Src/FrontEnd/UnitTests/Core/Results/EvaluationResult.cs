// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text;
using static BuildXL.Utilities.FormattableStringEx;

namespace Test.BuildXL.FrontEnd.Core
{
    /// <summary>
    /// Simple value object that represents all exposed members of the evaluation.
    /// </summary>
    [DebuggerDisplay("{ToString(), nq}")]
    public sealed class EvaluatedValues
    {
        private readonly Dictionary<string, object> m_expressionNameToValueMap = new Dictionary<string, object>();

        private EvaluatedValues()
        { }

        /// <nodoc />
        public EvaluatedValues(IEnumerable<Tuple<string, object>> results)
        {
            Contract.Requires(results != null);

            foreach (var tpl in results)
            {
                AddEvaluatedExpression(tpl.Item1, tpl.Item2);
            }
        }

        /// <summary>
        /// Returns empty set of values (useful to represent parse results).
        /// </summary>
        public static EvaluatedValues Empty { get; } = new EvaluatedValues();

        /// <summary>
        /// Returns evaluated expression by the name.
        /// </summary>
        public object this[string name]
        {
            get
            {
                Contract.Requires(name != null);
                Contract.Ensures(Contract.Result<object>() != null);

                object result;
                if (!m_expressionNameToValueMap.TryGetValue(name, out result))
                {
                    throw new InvalidOperationException(
                       I($"Expression '{name}' was not found at the list of evaluated expressions. Available expressions: {string.Join(", ", m_expressionNameToValueMap.Keys)}"));
                }

                return result;
            }
        }

        /// <summary>
        /// Returns list of evaluted expressions.
        /// </summary>
        public object[] Values => m_expressionNameToValueMap.Values.ToArray();

        /// <summary>
        /// Returns evaluated expression of specified type.
        /// </summary>
        public T Get<T>(string name)
        {
            return (T)this[name];
        }

        /// <inheritdoc />
        public override string ToString()
        {
            var sb = new StringBuilder();

            foreach (var kvp in m_expressionNameToValueMap)
            {
                sb.AppendLine($"[{kvp.Key}]: {kvp.Value};");
            }

            return sb.ToString();
        }

        private void AddEvaluatedExpression(string expressionName, object value)
        {
            Contract.Requires(expressionName != null);
            Contract.Requires(value != null);

            m_expressionNameToValueMap[expressionName] = value;
        }
    }
}
