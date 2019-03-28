// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities.Configuration.Mutable;
using Test.BuildXL.TestUtilities.Xunit;
using TypeScript.Net.Parsing;
using TypeScript.Net.Types;
using static BuildXL.Utilities.FormattableStringEx;

namespace Test.DScript.Workspaces.Utilities
{
    /// <summary>
    /// Utility functions for DScript workspace tests
    /// </summary>
    public static class WorkspaceTestUtilities
    {
        /// <nodoc/>
        public static readonly Parser Parser = new Parser();

        /// <nodoc/>
        public static ISourceFile ParseSourceFile(string content)
        {
            var config = Parser.ParseSourceFileContent("sourcefile.dsc", content);
            XAssert.IsTrue(config.ParseDiagnostics.Count == 0);

            return config;
        }

        /// <nodoc/>
        public static string GetModuleConfigurationContent(string moduleName)
        {
            return I(
$@"module({{
     name: ""{moduleName}"",
}});");
        }

        /// <nodoc/>
        public static IObjectLiteralExpression GetResolverSettingsLiteral(string content)
        {
            var source = Parser.ParseSourceFileContent("sourcefile.dsc", content);
            XAssert.IsTrue(source.ParseDiagnostics.Count == 0);

            return source.Statements[0]
                .Cast<IExpressionStatement>().Expression
                .Cast<ITypeAssertion>().Expression
                .Cast<IObjectLiteralExpression>();
        }

        /// <nodoc/>
        public static NugetResolverSettings GetNugetResolverSettings(string content)
        {
            var resolverSettingsLiteral = GetResolverSettingsLiteral(content);

            var settingsProvider = new FakeNugetResolverSettingsProvider();
            var maybeResolverSettings = settingsProvider.TryGetResolverSettings(resolverSettingsLiteral);

            XAssert.IsTrue(maybeResolverSettings.Succeeded);

            var nugetResolverSettings = maybeResolverSettings.Result as NugetResolverSettings;
            XAssert.IsNotNull(nugetResolverSettings);

            return nugetResolverSettings;
        }
    }
}
