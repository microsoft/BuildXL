// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using BuildXL.FrontEnd.Script.Constants;
using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge.Rules
{
    /// <summary>
    /// Rule that prevents from using <code>glob()</code>.
    /// </summary>
    internal sealed class EnforceNoGlobRule : PolicyRule
    {
        private static readonly HashSet<string> s_globFunctions = GetGlobFunctions();

        /// <nodoc/>
        public override string Name => "NoGlob";

        /// <nodoc/>
        public override string Description => "Disallows all forms of globbing in specification files.";

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(this, ValidateNode, TypeScript.Net.Types.SyntaxKind.CallExpression);
        }

        private void ValidateNode(INode node, DiagnosticContext context)
        {
            var callExpression = node.Cast<ICallExpression>();

            // We're interested only in invocation expression that're using named identifier.
            var callee = callExpression.Expression.As<IIdentifier>();
            if (callee != null)
            {
                if (IsGlobFunction(callee.Text))
                {
                    context.Logger.ReportGlobFunctionsIsNotAllowed(context.LoggingContext, node.LocationForLogging(context.SourceFile), Name);
                }
            }
        }

        private static bool IsGlobFunction(string functionName)
        {
            return s_globFunctions.Contains(functionName);
        }

        private static HashSet<string> GetGlobFunctions()
        {
            return new HashSet<string>()
            {
                Names.GlobFunction,
                Names.GlobRFunction,
                Names.GlobRecursivelyFunction,
                Names.GlobFoldersFunction,
            };
        }
    }
}
