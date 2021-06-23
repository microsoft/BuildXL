// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.Utilities;
using TypeScript.Net.Types;
using LineInfo = TypeScript.Net.Utilities.LineInfo;
using NotNullAttribute = JetBrains.Annotations.NotNullAttribute;

namespace BuildXL.FrontEnd.Script.Values
{
    /// <summary>
    /// Special kind of <see cref="ObjectLiteral"/> that represents a lazy evaluated expression
    /// </summary>
    /// <remarks>
    /// This class is essentially a decorator of a regular object literal, plus a type annotation representing the expected type of the lazy evaluated expression.
    /// TypeScript does not preserve types at execution time, so expressions like 'typeof T' with T generic are not allowed. Therefore the DScript declaration LazyEval&amp;T&amp;
    /// needs special treatment to carry the declared expected type so it can be validated at runtime model evaluation time.
    /// </remarks>
    [SuppressMessage("Microsoft.Naming", "CA1710")]
    public class LazyEvalObjectLiteral : ObjectLiteral
    {
        private readonly ObjectLiteral m_literal;

        /// <nodoc/>
        public static ObjectLiteral Create(IReadOnlyList<Binding> bindings, LineInfo location, AbsolutePath path, string returnType)
        {
            return new LazyEvalObjectLiteral(ObjectLiteral.Create(bindings, location, path), returnType);
        }

        private LazyEvalObjectLiteral(ObjectLiteral literal, string returnType): base(literal.Location, literal.Path)
        {
            m_literal = literal;
            ReturnType = returnType;
        }

        /// <inheritdoc/>
        public override EvaluationResult this[SymbolAtom name] => m_literal[name];

        /// <inheritdoc/>
        public override EvaluationResult this[StringId name] => m_literal[name];

        /// <inheritdoc/>
        public override int Count => m_literal.Count;

        /// <inheritdoc/>
        public override IEnumerable<KeyValuePair<StringId, EvaluationResult>> Members => m_literal.Members;

        /// <inheritdoc/>
        public override IEnumerable<StringId> Keys => m_literal.Keys;

        /// <inheritdoc/>
        public override SyntaxKind Kind => m_literal.Kind;

        /// <summary>
        /// Expected type of the lazy evaluated expression, as the type checker prints it.
        /// </summary>
        public string ReturnType { get; }

        /// <inheritdoc/>
        public override void Accept(Visitor visitor)
        {
            m_literal.Accept(visitor);
        }

        /// <inheritdoc/>
        public override EvaluationResult GetOrEvalField([NotNull] Context context, StringId name, bool recurs, [NotNull] ModuleLiteral origin, LineInfo location)
        {
            return m_literal.GetOrEvalField(context, name, recurs, origin, location);
        }

        /// <inheritdoc/>
        public override bool HasKey(StringId key)
        {
            return m_literal.HasKey(key);
        }

        /// <inheritdoc/>
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            var evaluatedObjectLiteral = m_literal.Eval(context, env, frame).Value as ObjectLiteral;
            Contract.AssertNotNull(evaluatedObjectLiteral);

            return EvaluationResult.Create(new LazyEvalObjectLiteral(evaluatedObjectLiteral, ReturnType));
        }
     
    }
}
