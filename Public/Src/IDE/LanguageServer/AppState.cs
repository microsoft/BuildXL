// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using BuildXL.Engine;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.FrontEnd.Script.Analyzer;
using BuildXL.FrontEnd.Core;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Sdk.FileSystem;
using BuildXL.Ide.LanguageServer.Providers;

namespace BuildXL.Ide.LanguageServer
{
    /// <summary>
    /// Minimal application state needed to start up both the application and unit tests.
    /// </summary>
    public sealed class AppState : IDisposable
    {
        private bool m_disposed;
        private readonly Workspace m_workspace;

        /// <nodoc />
        public FrontEndEngineAbstraction EngineAbstraction { get; }

        /// <nodoc />
        public TextDocumentManager DocumentManager { get; }

        /// <nodoc />
        public PathTable PathTable { get; }

        /// <nodoc />
        public IncrementalWorkspaceProvider IncrementalWorkspaceProvider { get; }

        private AppState(
            FrontEndEngineAbstraction engineAbstraction,
            TextDocumentManager documentManager,
            PathTable pathTable,
            IncrementalWorkspaceProvider incrementalWorkspaceProvider,
            Workspace workspace)
        {
            EngineAbstraction = engineAbstraction;
            DocumentManager = documentManager;
            PathTable = pathTable;
            IncrementalWorkspaceProvider = incrementalWorkspaceProvider;
            m_workspace = workspace;
        }

        /// <nodoc />
        public static AppState TryCreateWorkspace(
            Uri rootFolder,
            EventHandler<WorkspaceProgressEventArgs> progressHandler,
            TestContext? testContext = null)
        {
            var documentManager = new TextDocumentManager(new PathTable());

            return TryCreateWorkspace(documentManager, rootFolder, progressHandler, testContext);
        }
        
        /// <nodoc />
        [SuppressMessage("Microsoft.Reliability", "CA2000:DisposeObjectsBeforeLosingScope")]
        public static AppState TryCreateWorkspace(
            TextDocumentManager documentManager,
            Uri rootFolder,
            EventHandler<WorkspaceProgressEventArgs> progressHandler,
            TestContext? testContext = null,
            DScriptSettings settings = null)
        {
            documentManager = documentManager ?? new TextDocumentManager(new PathTable());
            // Need to clear event handlers to detach old instances like IncrementalWorkspaceProvider from the new events.
            documentManager.ClearEventHandlers();

            var pathTable = documentManager.PathTable;
            // Check if we need to prepopulate the document manager
            // with some documents (for testing)
            if (testContext.HasValue)
            {
                foreach (var document in testContext.Value.PrePopulatedDocuments)
                {
                    documentManager.Add(AbsolutePath.Create(pathTable, document.Uri), document);
                }
            }

            // Bootstrap the DScript frontend to parse/type-check the workspace given a build config
            var loggingContext = new LoggingContext("LanguageServer");
            var fileSystem = new PassThroughFileSystem(pathTable);
            var engineContext = EngineContext.CreateNew(
                cancellationToken: System.Threading.CancellationToken.None,
                pathTable: pathTable,
                fileSystem: fileSystem);
            var engineAbstraction = new LanguageServiceEngineAbstraction(documentManager, pathTable, engineContext.FileSystem);

            var frontEndContext = engineContext.ToFrontEndContext(loggingContext);

            var rootFolderAsAbsolutePath = rootFolder.ToAbsolutePath(pathTable);

            if (!WorkspaceBuilder.TryBuildWorkspaceForIde(
                frontEndContext: frontEndContext,
                engineContext: engineContext,
                frontEndEngineAbstraction: engineAbstraction,
                rootFolder: rootFolderAsAbsolutePath,
                progressHandler: progressHandler,
                workspace: out var workspace,
                skipNuget: settings?.SkipNuget ?? false, // Do not skip nuget by default.
                controller: out var controller))
            {
                // The workspace builder fails when the workspace contains any errors
                // however, we bail out only if the workspace is null, which happens only
                // when the config could not be parsed
                if (workspace == null || controller == null)
                {
                    return null;
                }
            }

            var incrementalWorkspaceProvider = new IncrementalWorkspaceProvider(controller, pathTable, workspace, documentManager, testContext);

            return new AppState(engineAbstraction, documentManager, pathTable, incrementalWorkspaceProvider, workspace);
        }

        /// <summary>
        /// Returns true if a given spec is part of the workspace.
        /// </summary>
        public bool ContainsSpec(AbsolutePath path)
        {
            Contract.Requires(path.IsValid);
            return m_workspace.ContainsSpec(path);
        }

        /// <summary>
        /// Returns true if at least one error occurred during workspace computation.
        /// </summary>
        public bool HasUnrecoverableFailures()
        {
            // If there is no spec modules and workspace computation failed,
            // then we've got some unrecoverable error during workspace construction (like module resolution error).
            return !m_workspace.Succeeded && m_workspace.SpecModules.Count == 0;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (!m_disposed)
            {
                m_disposed = true;

                IncrementalWorkspaceProvider.Dispose();
            }
        }
    }
}
