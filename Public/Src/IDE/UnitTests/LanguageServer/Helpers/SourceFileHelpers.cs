// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using TypeScript.Net;
using TypeScript.Net.Binding;
using TypeScript.Net.Parsing;
using TypeScript.Net.Types;

namespace BuildXL.Ide.LanguageServer.UnitTests.Helpers
{
    internal class SourceFileHelpers
    {
        public static ISourceFile ParseAndBindContent(string script)
        {
            var parser = new Parser();
            var binder = new Binder();

            var chars = script.ToCharArray();
            var sourceFile = parser.ParseSourceFileContent(TextSource.FromCharArray(chars, chars.Length));
            binder.BindSourceFile(sourceFile, new CompilerOptions());

            return sourceFile;
        }
    }
}
