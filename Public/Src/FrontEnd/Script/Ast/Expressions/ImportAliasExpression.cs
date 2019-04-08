// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script;
using BuildXL.Utilities;
using JetBrains.Annotations;
using BuildXL.FrontEnd.Script.RuntimeModel.AstBridge;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;
using static BuildXL.Utilities.FormattableStringEx;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Expressions
{
    /// <summary>
    /// Represents <code>importFrom(path)</code> expression or `x` from the <code>import * as x from path</code> statement.
    /// </summary>
    /// <remarks>
    /// DScript V2 node.
    /// Expression is used in the semantic evaluation and is very similar to 'importFrom' ambient invocation.
    /// </remarks>
    public sealed class ImportAliasExpression : Expression
    {
        // Universal location of this expression (the _referencing_ expression)
        private readonly UniversalLocation m_referencingLocation;

        // The _referenced_ path defined in this expression
        private readonly AbsolutePath m_referencedPath;

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="referencedPath">Referenced absolute path to be aliased.</param>
        /// <param name="referencingLocation">Location of the expression. Includes absolute path for the file containing this expression, which may be consumed for error reporting.</param>
        public ImportAliasExpression(AbsolutePath referencedPath, UniversalLocation referencingLocation)
            : base(referencingLocation)
        {
            Contract.Requires(referencedPath.IsValid);

            m_referencedPath = referencedPath;
            m_referencingLocation = referencingLocation;
        }

        /// <nodoc />
        public ImportAliasExpression(DeserializationContext context, LineInfo location)
            : base(location)
        {
            var reader = context.Reader;
            m_referencedPath = reader.ReadAbsolutePath();
            m_referencingLocation = new UniversalLocation(context, location);
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            writer.Write(m_referencedPath);
            m_referencingLocation.DoSerialize(writer);
        }

        /// <inheritdoc/>
        public override void Accept(Visitor visitor)
        {
            // Intentionally left blank.
        }

        /// <inheritdoc/>
        public override SyntaxKind Kind => SyntaxKind.ImportAliasExpression;

        /// <inheritdoc/>
        public override string ToDebugString()
        {
            return I($"importFrom({m_referencedPath.ToDebuggerDisplay()})");
        }

        /// <inheritdoc/>
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            // To evaluate imported module following steps are required:
            // 1. Find an instantiated module by path specifier (the module was already resolved during workspace computation,
            //    so no additional work is required here).
            // 2. Coerce current qualifier with a qualifier defined in a target file.
            // 3. Instantiate the module and return it.
            UninstantiatedModuleInfo importedModuleInfo = context.ModuleRegistry.GetUninstantiatedModuleInfoByPath(m_referencedPath);

            var module = importedModuleInfo.FileModuleLiteral.Instantiate(context.ModuleRegistry, env.Qualifier);
            return EvaluationResult.Create(module);
        }
    }
}
