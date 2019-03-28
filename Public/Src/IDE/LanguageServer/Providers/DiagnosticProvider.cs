// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Workspaces.Core;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace BuildXL.Ide.LanguageServer.Providers
{
    /// <summary>
    /// Provider to provide error diagnostic functionality ('red-squigglies', and error message on mouse-hover).
    /// </summary>
    public sealed class DiagnosticProvider : IdeProviderBase
    {
        /// <nodoc/>
        public DiagnosticProvider(ProviderContext providerContext)
            : base(providerContext)
        {
        }

        /// <summary>
        /// Every time the workspace is recomputed we want to provide diagnostics based on the last text event that triggered the recomputation
        /// </summary>
        protected override void OnWorkspaceRecomputed(object sender, WorkspaceRecomputedEventArgs workspaceRecomputedEventArgs)
        {
            base.OnWorkspaceRecomputed(sender, workspaceRecomputedEventArgs);
            
            DoReportDiagnostics(workspaceRecomputedEventArgs.UpdatedWorkspace, workspaceRecomputedEventArgs.TextDocumentItems.First(), PathTable, ProviderContext);
        }

        /// <nodoc/>
        public void ReportDiagnostics(TextDocumentItem document)
        {
            DoReportDiagnostics(Workspace, document, PathTable, ProviderContext);
        }

        private static void DoReportDiagnostics(Workspace workspace, TextDocumentItem document, PathTable pathTable, ProviderContext providerContext)
        {
            // We report diagnostics for the document
            var documentUri = new Uri(document.Uri);

            var semanticModel = workspace.GetSemanticModel();

            Contract.Assert(semanticModel != null, "We should always get a semantic model since we don't cancel on first failure");

            // These are all the syntactic diagnostics that come from the workspace, across all files
            var allDiagnostics = workspace.GetAllParsingAndBindingErrors();

            // If the file is part of the workspace, we add the type checking errors for that file only
            var path = documentUri.ToAbsolutePath(pathTable);
            if (workspace.TryGetSourceFile(path, out var spec))
            {
                allDiagnostics = allDiagnostics.Union(semanticModel.GetTypeCheckingDiagnosticsForFile(spec));
            }

            var publishDiagnostics = FilterDiagnosticsForSpec(documentUri, allDiagnostics, pathTable);

            // Need to report even if publishedDiagnostics is empty to clear the diagnostics list in IDE.
            Analysis.IgnoreResult(providerContext.PublishDiagnosticsAsync(publishDiagnostics), "Fire and forget");
        }

        /// <summary>
        /// Filters a set of diagnostics for a specific file and converts the result so it can be returned to the client
        /// </summary>
        private static PublishDiagnosticParams FilterDiagnosticsForSpec(Uri documentUri, IEnumerable<TypeScript.Net.Diagnostics.Diagnostic> syntacticDiagnostics, PathTable pathTable)
        {
            // we use the path table to unify potential differences in slashes
            var pathToFile = documentUri.ToAbsolutePath(pathTable);

            // filter all diagnostics to the specific file that was requested
            var diagnostics = 
                syntacticDiagnostics
                .Where(diag => diag.File != null && diag.File.GetAbsolutePath(pathTable) == pathToFile)
                .Select(d => d.ToProtocolDiagnostic())
                .ToArray();

            return new PublishDiagnosticParams
            {
                Diagnostics = diagnostics.ToArray(),
                Uri = documentUri.ToString(),
            };
        }

        
    }
}
