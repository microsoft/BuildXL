// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.Threading;
using BuildXL.Ide.JsonRpc;
using LanguageServer;
using LanguageServer.Json;
using BuildXL.FrontEnd.Sdk;
using BuildXL.Ide.LanguageServer.Completion;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace BuildXL.Ide.LanguageServer.Providers
{
    /// <summary>
    /// Contains all providers of the language server.
    /// </summary>
    internal sealed class LanguageServiceProviders
    {
        private readonly AutoCompleteProvider m_autoCompleteProvider;
        private readonly GotoDefinitionProvider m_gotoDefinitionProvider;
        private readonly FindReferencesProvider m_findReferencesProvider;
        private readonly CodeActionProvider m_codeActionProvider;
        private readonly FormattingProvider m_formattingProvider;
        private readonly DiagnosticProvider m_diagnosticProvider;
        private readonly HoverProvider m_hoverProvider;
        private readonly CodeLensProvider m_codeLensProvider;
        private readonly ExecuteCommandProvider m_executeCommandProvider;
        private readonly SignatureHelpProvider m_signatureHelpProvider;
        private readonly SymbolProvider m_symbolProvider;
        private readonly RenameProvider m_renameProvider;

        public LanguageServiceProviders(ProviderContext providerContext, FrontEndEngineAbstraction engineAbstraction, IProgressReporter progressReporter)
        {
            Contract.Requires(providerContext != null);
            Contract.Requires(engineAbstraction != null);

            m_autoCompleteProvider = new AutoCompleteProvider(providerContext);
            m_gotoDefinitionProvider = new GotoDefinitionProvider(providerContext);
            m_findReferencesProvider = new FindReferencesProvider(providerContext, progressReporter);
            m_formattingProvider = new FormattingProvider(providerContext, engineAbstraction);
            m_executeCommandProvider = new ExecuteCommandProvider(providerContext);
            m_codeActionProvider = new CodeActionProvider(providerContext, m_executeCommandProvider);
            m_codeLensProvider = new CodeLensProvider(providerContext);
            m_hoverProvider = new HoverProvider(providerContext);
            m_diagnosticProvider = new DiagnosticProvider(providerContext);
            m_signatureHelpProvider = new SignatureHelpProvider(providerContext);
            m_symbolProvider = new SymbolProvider(providerContext);
            m_renameProvider = new RenameProvider(providerContext, m_findReferencesProvider);
        }

        /// <nodoc />
        public void ReportDiagnostics(TextDocumentItem document)
        {
            m_diagnosticProvider.ReportDiagnostics(document);
        }

        /// <nodoc />
        public Result<ArrayOrObject<CompletionItem, CompletionList>, ResponseError> Completion(TextDocumentPositionParams position, CancellationToken token)
        {
            return m_autoCompleteProvider.Completion(position, token);
        }

        /// <nodoc />
        public Result<TextEdit[], ResponseError> FormatDocument(DocumentFormattingParams formatOptions, CancellationToken token)
        {
            return m_formattingProvider.FormatDocument(formatOptions, token);
        }

        /// <nodoc />
        public Result<ArrayOrObject<Location, Location>, ResponseError> GetDefinitionAtPosition(TextDocumentPositionParams documentPosition, CancellationToken token)
        {
            return m_gotoDefinitionProvider.GetDefinitionAtPosition(documentPosition, token);
        }

        /// <nodoc />
        public Result<Location[], ResponseError> GetReferencesAtPosition(ReferenceParams referenceParams, CancellationToken token)
        {
            return m_findReferencesProvider.GetReferencesAtPosition(referenceParams, token);
        }

        /// <nodoc />
        public Result<Command[], ResponseError> CodeAction(CodeActionParams codeActionParams, CancellationToken token)
        {
            return m_codeActionProvider.CodeAction(codeActionParams, token);
        }

        /// <nodoc />
        public Result<WorkspaceEdit, ResponseError> Rename(RenameParams renameParams, CancellationToken token)
        {
            return m_renameProvider.GetWorkspaceEdits(renameParams, token);
        }

        /// <nodoc />
        public Result<object, ResponseError> ExecuteCommand(ExecuteCommandParams executeParams, CancellationToken token)
        {
            return m_executeCommandProvider.ExecuteCommand(executeParams, token);
        }

        /// <nodoc />
        public Result<CodeLens[], ResponseError> CodeLens(CodeLensParams codeLens, CancellationToken token)
        {
            return m_codeLensProvider.CodeLens(codeLens, token);
        }

        /// <nodoc />
        public Result<Hover, ResponseError> Hover(TextDocumentPositionParams position, CancellationToken token)
        {
            return m_hoverProvider.Hover(position, token);
        }

        /// <nodoc />
        public Result<SymbolInformation[], ResponseError> DocumentSymbols(DocumentSymbolParams documentSymbol, CancellationToken token)
        {
            return m_symbolProvider.DocumentSymbols(documentSymbol, token);
        }

        /// <nodoc />
        public Result<SignatureHelp, ResponseError> SignatureHelp(TextDocumentPositionParams textDocumentPosition, CancellationToken token)
        {
            return m_signatureHelpProvider.SignatureHelp(textDocumentPosition, token);
        }

        /// <nodoc />
        public Result<CompletionItem, ResponseError> ResolveCompletionItem(CompletionItem completion, CancellationToken token)
        {
            return m_autoCompleteProvider.ResolveCompletionItem(completion, token);
        }
    }
}
