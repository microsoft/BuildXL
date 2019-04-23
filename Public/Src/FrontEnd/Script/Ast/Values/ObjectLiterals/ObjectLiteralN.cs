// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.Utilities;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Values
{
    /// <summary>
    /// Object literal of arbitrary size.
    /// </summary>
    [SuppressMessage("Microsoft.Naming", "CA1710")]
    public sealed class ObjectLiteralN : ObjectLiteral
    {
        /// <summary>
        /// Internal mapping from property names to values.
        /// </summary>
        /// <remarks>
        /// The map is simply from integers to object values, where the integers are the ids of <see cref="StringId"/>.
        /// The map used to be from StringId to object values, but both microbenchmarking and profiling on self-host
        /// shows the bottle-neck is in TryGetValue. One possible reason is the GetHashCode and Equality are not inlined by JIT.
        /// although the attribute MethodImpl(MethodImplOptions.AggressiveInlining) has been specified. Passing explicitly
        /// the comparer does not help either.
        /// </remarks>
        private readonly Dictionary<int, EvaluationResult> m_values;

        /// <summary>
        /// Values.
        /// </summary>
        // TODO:ST: consider hiding implementation details. IDictionary is mutable and could be harmful to expose.
        internal Dictionary<int, EvaluationResult> Values => m_values;

        /// <nodoc />
        public ObjectLiteralN(IReadOnlyList<Binding> bindings, LineInfo location, AbsolutePath path)
            : base(location, path)
        {
            Contract.Requires(bindings != null);
            Contract.Requires(bindings.Count > 0);
            Contract.RequiresForAll(bindings, b => b.Name.IsValid);

            m_values = bindings.ToDictionary(b => b.Name.Value, b => EvaluationResult.Create(b.Body));
        }

        internal ObjectLiteralN(IReadOnlyList<NamedValue> bindings, LineInfo location, AbsolutePath path)
            : base(location, path)
        {
            Contract.Requires(bindings != null);
            Contract.Requires(bindings.Count > 0);
            Contract.RequiresForAll(bindings, b => b.IsValid);

            m_values = bindings.ToDictionary(b => b.NameId, b => b.Value);
        }

        /// <nodoc />
        public ObjectLiteralN(Dictionary<int, EvaluationResult> values, LineInfo location, AbsolutePath path)
            : base(location, path)
        {
            Contract.Requires(values != null);
            Contract.Requires(values.Count > 0);

            m_values = values;
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.ObjectLiteralN;

        /// <inheritdoc />
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            var newValues = new Dictionary<int, EvaluationResult>(m_values.Count);

            foreach (var binding in m_values)
            {
                var v = EvalExpression(context, env, binding.Value, frame);

                if (v.IsErrorValue)
                {
                    return v;
                }

                newValues.Add(binding.Key, v);
            }

            return EvaluationResult.Create(new ObjectLiteralN(newValues, Location, Path));
        }

        /// <inheritdoc />
        public override EvaluationResult GetOrEvalField(Context context, StringId name, bool recurs, ModuleLiteral origin, LineInfo location)
        {
            if (m_values.TryGetValue(name.Value, out EvaluationResult result))
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
                return !m_values.TryGetValue(name.StringId.Value, out EvaluationResult value) ? EvaluationResult.Undefined : value;
            }
        }

        /// <inheritdoc />
        public override EvaluationResult this[StringId name]
        {
            get
            {
                Contract.Requires(name.IsValid);
                return !m_values.TryGetValue(name.Value, out EvaluationResult value) ? EvaluationResult.Undefined : value;
            }
        }

        /// <inheritdoc />
        public override int Count => m_values.Count;

        /// <inheritdoc />
        public override IEnumerable<KeyValuePair<StringId, EvaluationResult>> Members
        {
            get
            {
                return m_values.Select(kvp => new KeyValuePair<StringId, EvaluationResult>(new StringId(kvp.Key), kvp.Value));
            }
        }

        /// <inheritdoc />
        public override IEnumerable<StringId> Keys
        {
            get { return m_values.Keys.Select(k => new StringId(k)); }
        }

        /// <inheritdoc />
        public override bool HasKey(StringId key)
        {
            return m_values.ContainsKey(key.Value);
        }
    }
}
