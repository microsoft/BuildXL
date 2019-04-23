// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.RuntimeModel.AstBridge;
using BuildXL.FrontEnd.Workspaces.Core;
using TypeScript.Net.Extensions;
using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.Analyzer.Analyzers
{
    /// <nodoc />
    public class LegacyLiteralCreation : Analyzer
    {
        /// <inheritdoc />
        public override AnalyzerKind Kind { get; } = AnalyzerKind.LegacyLiteralCreation;

        /// <inheritdoc />
        public override bool Initialize()
        {
            RegisterSyntaxNodeAction(LiteralFix, TypeScript.Net.Types.SyntaxKind.CallExpression);

            return base.Initialize();
        }

        /// <nodoc />
        private bool LiteralFix(INode node, DiagnosticsContext context)
        {
            var call = node.Cast<ICallExpression>();
            var symbol = context.SemanticModel.GetSymbolAtLocation(call.Expression);
            if (symbol != null)
            {
                if (!FixCreateFunction(call, symbol, context, "a", "PathAtom"))
                {
                    return false;
                }

                if (!FixCreateFunction(call, symbol, context, "r", "RelativePath"))
                {
                    return false;
                }
            }

            return true;
        }

        private bool FixCreateFunction(ICallExpression call, ISymbol symbol, DiagnosticsContext context, string formatFunction, string interfaceName)
        {
            if (Matches(context, symbol, "Sdk.Prelude", interfaceName + ".create"))
            {
                var arg = call.Arguments.FirstOrDefault();

                var fix = ComputeFix(formatFunction, arg);

                if (Fix)
                {
                    call.Replace(fix);
                }
                else
                {
                    var existingExpression = call.ToDisplayString();
                    var fixExpression = fix.ToDisplayString();
                    Logger.LegacyLiteralFix(LoggingContext, call.LocationForLogging(context.SourceFile), fixExpression, existingExpression);
                    return false;
                }
            }

            return true;
        }

        private static INode ComputeFix(string formatFunction, IExpression arg)
        {
            // Short-circuit common single string
            var stringLiteralArgument = arg.As<IStringLiteral>();
            if (stringLiteralArgument != null)
            {
                return new TaggedTemplateExpression(formatFunction, stringLiteralArgument.Text);
            }

            var expressions = new List<IExpression>();
            TryFlattenAdditions(arg, expressions);
            var firstStringLiteral = expressions[0].As<IStringLiteral>();
            Contract.Assert(firstStringLiteral != null, "Invariant of TryFlattenAdditions is that the first expression is a StringLiteral. Bug in TryFlattenAdditions");
            var firstText = firstStringLiteral?.Text;

            // Another common case where after flattening we have a single string
            if (expressions.Count == 1)
            {
                return new TaggedTemplateExpression(formatFunction, firstText);
            }

            var spans = new List<ITemplateSpan>();
            for (int i = 1; i < expressions.Count; i++)
            {
                var first = expressions[i];
                Contract.Assert(
                    first.As<IStringLiteral>() == null,
                    "Cannot have two string literals in sequence in this list. Bug in TryFlattenAdditions");

                // Check if second is a string literal expression
                string second = string.Empty;
                if (i < expressions.Count - 1)
                {
                    var secondAsString = expressions[i + 1].As<IStringLiteral>();
                    if (secondAsString != null)
                    {
                        // We found a string. make it the second and skip the node
                        second = secondAsString.Text;
                        i++;
                    }
                }

                var last = i == expressions.Count - 1;

                spans.Add(new TemplateSpan(first, second, last));
            }

            return new TaggedTemplateExpression
                   {
                       Tag = new Identifier(formatFunction),
                       TemplateExpression = new TemplateExpression(
                               firstText,
                               spans.ToArray()),
                   };
        }

        /// <summary>
        /// Attempts to flatten multiple additions into a list of expressions.
        /// If multiple string literals are added, then we already concat them here to avoid another pass.
        /// </summary>
        private static void TryFlattenAdditions(IExpression arg, List<IExpression> expressions)
        {
            var binOp = arg.As<IBinaryExpression>();
            if (binOp != null)
            {
                if (binOp.OperatorToken.Kind == TypeScript.Net.Types.SyntaxKind.PlusToken)
                {
                    TryFlattenAdditions(binOp.Left, expressions);
                    TryFlattenAdditions(binOp.Right, expressions);
                    return;
                }
            }

            // Auto-concat string literals
            var argExprString = arg.As<IStringLiteral>();
            if (argExprString != null && expressions.Count > 0)
            {
                var lastExpr = expressions[expressions.Count - 1];
                var lastExprString = lastExpr.As<IStringLiteral>();
                if (lastExprString != null)
                {
                    expressions[expressions.Count - 1] = new LiteralExpression(lastExprString.Text + argExprString.Text);
                    return;
                }
            }

            // Format string structure always starts with a string, insert empty string if none here yet.
            if (argExprString == null && expressions.Count == 0)
            {
                expressions.Add(new LiteralExpression(string.Empty));
            }

            expressions.Add(arg);
            return;
        }

        /// <summary>
        /// Checks if the given symbol is defined in a module whose name is <see ref="moduleName"/> and has a fully expanded name of <see ref="fullName"/>.
        /// </summary>
        private static bool Matches(DiagnosticsContext context, ISymbol symbol, string moduleName, string fullName)
        {
            var decl = symbol.DeclarationList.FirstOrDefault();
            var owningSpec = decl?.GetSourceFile()?.GetAbsolutePath(context.PathTable);

            ParsedModule parsedModule;
            if (owningSpec.HasValue && (parsedModule = context.Workspace.TryGetModuleBySpecFileName(owningSpec.Value)) != null)
            {
                var actualFullName = context.SemanticModel.GetFullyQualifiedName(symbol);
                var actualModuleName = parsedModule.Descriptor.Name;
                if (string.Equals(moduleName, actualModuleName, StringComparison.Ordinal) &&
                    string.Equals(fullName, actualFullName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
