// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Expressions
{
    /// <summary>
    /// Wraps an expression (currently hardcoded to <see cref="ImportAliasExpression"/>) that evaluates to 
    /// a <see cref="ModuleLiteral"/>, then converts the resulting module literal into an <see cref="ObjectLiteral"/>,
    /// by adding a property to the object literal for every binding (<see cref="ModuleLiteral.GetAllBindings(Context)"/>)
    /// found in the module literal.
    /// </summary>
    public sealed class ModuleToObjectLiteral : Expression
    {
        /// <summary>
        /// This expression is expected to evaluate to a <see cref="ModuleLiteral"/>.
        /// </summary>
        private readonly Expression m_expression;

        /// <nodoc/>
        public ModuleToObjectLiteral(ImportAliasExpression importExpression)
            : base(importExpression.Location)
        {
            m_expression = importExpression;
        }

        /// <nodoc />
        public ModuleToObjectLiteral(DeserializationContext context, LineInfo location)
            : base(location)
        {
            m_expression = ReadExpression(context);
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            m_expression.Serialize(writer);
        }

        /// <inheritdoc/>
        public override void Accept(Visitor visitor)
        {
            // Intentionally left blank.
        }

        /// <inheritdoc/>
        public override SyntaxKind Kind => SyntaxKind.ModuleToObjectLiteral;

        /// <inheritdoc/>
        public override string ToDebugString()
        {
            return m_expression?.ToDebugString() ?? string.Empty;
        }

        /// <inheritdoc/>
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            var result = m_expression.Eval(context, env, frame);
            if (result.IsErrorValue)
            {
                return result;
            }

            if (!(result.Value is ModuleLiteral module))
            {
                var thisNodeType = nameof(ModuleToObjectLiteral);
                throw Contract.AssertFailure(
                    $"AstConverter should never create a '{thisNodeType}' node that wraps an expression that evaluates to something other than {nameof(ModuleLiteral)}. " +
                    $"Instead, this '{thisNodeType}' wraps an expression of type '{m_expression.GetType().Name}' which evaluated to an instance of type '{result.Value?.GetType().Name}'.");
            }

            var bindings = module
                .GetAllBindings(context)
                .Where(kvp => kvp.Key != Constants.Names.RuntimeRootNamespaceAlias)
                .Select(kvp =>
                {
                    var name = SymbolAtom.Create(context.StringTable, kvp.Key);
                    var location = kvp.Value.Location;
                    var evalResult = module.GetOrEvalFieldBinding(context, name, kvp.Value, location);
                    return new Binding(name, evalResult.Value, location);
                })
                .ToArray();

            if (bindings.Any(b => b.Body.IsErrorValue()))
            {
                return EvaluationResult.Error;
            }

            var objectLiteral = ObjectLiteral.Create(bindings);
            return EvaluationResult.Create(objectLiteral);
        }
    }
}
