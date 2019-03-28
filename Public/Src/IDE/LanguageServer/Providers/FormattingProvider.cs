// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.FrontEnd.Script.Analyzer.Analyzers;
using LanguageServer;
using BuildXL.FrontEnd.Sdk;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using TypeScript.Net.DScript;
using TypeScript.Net.Parsing;
using TypeScript.Net.Types;
using CancellationToken = System.Threading.CancellationToken;

namespace BuildXL.Ide.LanguageServer.Providers
{
        /// <nodoc />
    public sealed class FormattingProvider : IdeProviderBase
    {
        private readonly FrontEndEngineAbstraction m_engineAbstraction;

        /// <nodoc />
        public FormattingProvider(ProviderContext providerContext, FrontEndEngineAbstraction engineAbstraction)
            : base(providerContext)
        {
            m_engineAbstraction = engineAbstraction;
        }

        /// <nodoc />
        public Result<TextEdit[], ResponseError> FormatDocument(DocumentFormattingParams @params, CancellationToken token)
        {
            // TODO: support cancellation
            string uri = @params.TextDocument.Uri;
            if (!TryGetSourceFile(uri, out var spec, out var error))
            {
                string errorMessage = $"Could not open the file '{uri}'.{Environment.NewLine}{error}";
                return Result.InternalError<TextEdit[]>(errorMessage);
            }

            var formattedText = PrettyPrint.GetFormattedText(spec);

            var textEdit = new TextEdit
            {
                NewText = formattedText,
                Range = spec.ToRange(),
            };

            return Result<TextEdit[], ResponseError>.Success(new[] { textEdit });
        }

        private bool TryGetSourceFile(string uri, out ISourceFile sourceFile, out string error)
        {
            // We first check if the requested file is already part of the workspace, so we don't have to parse it again
            // Things like configuration files are not part of the workspace, but could be part of the VSCode workspace
            if (TryFindSourceFile(uri, out sourceFile))
            {
                error = null;
                return true;
            }

            var pathToDocument = uri.ToAbsolutePath(PathTable);

            var content = m_engineAbstraction.GetFileContentAsync(pathToDocument).GetAwaiter().GetResult();
            if (!content.Succeeded)
            {
                sourceFile = null;
                error = content.Failure.Describe();
                return false;
            }
            
            var parser = new Parser();
            sourceFile = parser.ParseSourceFileContent(
                pathToDocument.ToString(PathTable),
                content.Result.GetContentAsString(),
                ParsingOptions.DefaultParsingOptions);

            error = null;
            return true;
        }
    }
}
