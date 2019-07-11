// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Diagnostics.Tracing;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Ide.JsonRpc;
using BuildXL.Ide.LanguageServer.Providers;
using BuildXL.Ide.LanguageServer.Tracing;
using BuildXL.Storage;
using JetBrains.Annotations;
using LanguageServer;
using LanguageServer.Json;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using Newtonsoft.Json.Linq;
using StreamJsonRpc;
using Location = Microsoft.VisualStudio.LanguageServer.Protocol.Location;

namespace BuildXL.Ide.LanguageServer
{
    /// <summary>
    /// Server app class (drives all interactions).
    /// </summary>
    public class App : IDisposable
    {
        /// <summary>
        /// A default timeout for language server operation. If the limit is reached an operation and its duration would be logged.
        /// </summary>
        private const int DefaultOperationDurationThreshold = 5000;

        private const string OpenLogFileCommandString = "openLogFile";
        private const string ReloadWorkspaceCommandString = "reloadWorkspace";

        private DScriptSettings m_settings;

        /// <summary>
        /// Non-null when in test mode
        /// </summary>
        private readonly TestContext? m_testContext;

        private readonly Tracer m_tracer;

        private Logger Logger => m_tracer.Logger;

        private LoggingContext LoggingContext => m_tracer.LoggingContext;

        [CanBeNull]
        private ProjectManagementProvider m_projectManagementProvider;

        private Uri m_rootUri;

        private readonly object m_loadWorkspaceLock = new object();

        private readonly ManualResetEvent m_exitEvent = new ManualResetEvent(false);

        private readonly IProgressReporter m_progressReporter;

        private readonly StreamJsonRpc.JsonRpc m_mainRpcChannel;

        private WorkspaceLoadingState m_workspaceLoadingState = WorkspaceLoadingState.Init;
        private CachedTask<(WorkspaceLoadingState, AppState, LanguageServiceProviders)> m_workspaceLoadingTask = 
            CachedTask<(WorkspaceLoadingState, AppState, LanguageServiceProviders)>.Create();

        /// <nodoc/>
        public App(Stream clientStream, Stream serverStream, string pathToLogFile)
        {
            Contract.Requires(clientStream != null);
            Contract.Requires(serverStream != null);
            Contract.Requires(!string.IsNullOrEmpty(pathToLogFile));

            ContentHashingUtilities.SetDefaultHashType();

            // Note that we cannot start listening (i.e. use Attach) until
            // we have finished constructing the app.
            // Otherwise we can receive an incoming message
            // before we finish initialization.
            var jsonRpcChannel = new JsonRpcWithException(clientStream, serverStream, this);
            m_mainRpcChannel = jsonRpcChannel;

            // We need to create the project management provider before we start listening on the
            // RPC channel as you cannot attach them after it has started listening.
            m_projectManagementProvider = new ProjectManagementProvider(GetAppStateDelegate(), m_mainRpcChannel);

            m_tracer = new Tracer(m_mainRpcChannel, pathToLogFile, EventLevel.Verbose, EventLevel.Informational);

            m_progressReporter = new ProgressReporter(m_mainRpcChannel, testContext: null);

            Logger.LanguageServerStarted(LoggingContext);
            Logger.LanguageServerLogFileLocation(LoggingContext, pathToLogFile);

            jsonRpcChannel.SetLoggingContext(Logger, LoggingContext);

            // Change minimal number of threads for performance reasons.
            // 5 is a reasonable number that should prevent thread pool exhaustion and will not spawn too many threads.
            ThreadPoolHelper.ConfigureWorkerThreadPools(Environment.ProcessorCount, 5);

            // This must be last after initialization
            m_mainRpcChannel.StartListening();

            SubscribeToUnhandledErrors();
        }

        private App(StreamJsonRpc.JsonRpc mainRpcChannel, TestContext testContext)
        {
            m_progressReporter = new ProgressReporter(mainRpcChannel, testContext);

            m_testContext = testContext;
            m_mainRpcChannel = mainRpcChannel;
            m_tracer = new Tracer();

            // We need to create the project management provider before we start listening on the
            // RPC channel as you cannot attach them after it has started listening.
            m_projectManagementProvider = new ProjectManagementProvider(GetAppStateDelegate(), m_mainRpcChannel);
        }

        internal static App CreateForTesting(StreamJsonRpc.JsonRpc jsonRpc, TestContext testContext)
        {
            Contract.Requires(jsonRpc != null);

            return new App(jsonRpc, testContext);
        }

        /// <nodoc/>
        public void WaitForExit()
        {
            m_exitEvent.WaitOne();
        }

        private GetAppState GetAppStateDelegate()
        {
            return () =>
            {
                TryWaitForWorkspaceLoaded(out var appState, out _);
                return appState;
            };
        }

        /// <summary>
        /// Creates the BuildXL workspace and initializes the language service providers.
        /// </summary>
        /// <remarks>
        /// This must be called when a text document is actually opened. The order VSCode
        /// calls the plugin is "Initialize" -> "DidConfigurationChange" -> DidOpenTextDocument.
        /// So, if you attempt to create the workspace, and initialize the providers during the
        /// initialization phase, then you will not have proper access to any configuration settings
        /// the user may have made.
        /// </remarks>
        private Task<(WorkspaceLoadingState workspaceLoadingState, AppState appState, LanguageServiceProviders languageServiceProviders)> LoadWorkspaceAsync(TextDocumentManager documentManager = null)
        {
            var workspaceLoadingTask = m_workspaceLoadingTask.GetOrCreate(async () =>
            {
                var result = await Task.Run(() =>
                {
                    try
                    {
                        m_progressReporter.ReportWorkspaceInit();

                        WorkspaceLoadingState workspaceLoadingState;

                        if (m_rootUri == null)
                        {
                            throw new ArgumentException("Root directory was not passed from VSCode via the rootUri configuration value");
                        }

                        var appState = AppState.TryCreateWorkspace(documentManager, m_rootUri, (s, ea) =>
                            {
                                // "Casting" the EventArgs object from the BuildXL library to the "local" version
                                // to avoid pulling in all of its dependencies during build.
                                var rpcEventArgs = BuildXL.Ide.JsonRpc.WorkspaceProgressEventArgs.Create(
                                    (BuildXL.Ide.JsonRpc.ProgressStage)ea.ProgressStage,
                                    ea.NumberOfProcessedSpecs,
                                    ea.TotalNumberOfSpecs);
                                m_progressReporter.ReportWorkspaceInProgress(WorkspaceLoadingParams.InProgress(rpcEventArgs));
                            },
                            m_testContext,
                            m_settings);

                        if (appState == null || appState.HasUnrecoverableFailures())
                        {
                            workspaceLoadingState = WorkspaceLoadingState.Failure;
                            m_progressReporter.ReportWorkspaceFailure(m_tracer.LogFilePath, OpenLogFile);
                        }
                        else
                        {
                            workspaceLoadingState = WorkspaceLoadingState.Success;

                            m_progressReporter.ReportWorkspaceSuccess();
                        }

                        LanguageServiceProviders providers = null;

                        lock (m_loadWorkspaceLock)
                        {
                            if (workspaceLoadingState == WorkspaceLoadingState.Success)
                            {
                                // In practice you should not make callouts while holding
                                // a lock. Initializing the providers sets members of this
                                // class and also relies on members of the app-state.
                                // So, we do want to block callers until initialization of
                                // the providers is complete, however, be warned that
                                // if a provider ever calls back into the app it could
                                // cause a deadlock.
                                providers = InitializeProviders(appState);
                            }
                        }

                        return (workspaceLoadingState, appState, providers);
                    }
                    catch (Exception e)
                    {
                        var errorMessage = e.ToStringDemystified();
                        Logger.LanguageServerUnhandledInternalError(LoggingContext, errorMessage);
                        m_progressReporter.ReportWorkspaceFailure(m_tracer.LogFilePath, OpenLogFile);
                        m_testContext.GetValueOrDefault().ErrorReporter?.Invoke(errorMessage);
                        return (WorkspaceLoadingState.Failure, (AppState)null, (LanguageServiceProviders)null);
                    }
                });

                return result;
            });

            // Changing the workspace loading state once the task is finished.
            workspaceLoadingTask.ContinueWith(t =>
            {
                m_workspaceLoadingState = t.Result.Item1;
            });

            return workspaceLoadingTask;
        }

        /// <summary>
        /// Waits for the workspace to finish loading and returns true if the workspace loaded successfully.
        /// Returns an application state and initialized providers.
        /// </summary>
        private bool TryWaitForWorkspaceLoaded(out AppState appState, out LanguageServiceProviders providers)
        {
            try
            {
                // LoadWorkspaceAsync returns a cached task. It means that if the initialization is finished
                // we'll get the result immediately.
                var result = LoadWorkspaceAsync().GetAwaiter().GetResult();
                if (result.workspaceLoadingState == WorkspaceLoadingState.Success)
                {
                    appState = result.appState;
                    providers = result.languageServiceProviders;
                    return true;
                }
            }
            catch (TaskCanceledException)
            {
            }

            appState = null;
            providers = null;
            return false;
        }

        /// <summary>
        /// Opens the current log file for the user.
        /// </summary>
        private void OpenLogFile()
        {
            // TODO: SO... Since the file is still in use at this point, if the user has
            // TODO: something like word-pad associated with the log file extension,
            // TODO: It will fail to open since word-pad tries to open the file exclusively.
            // TODO: Applications like good old notepad work just fine.
            // TODO: So, in theory we should copy the current contents of the file
            // TODO: to a temporary file and open that one.
            Process.Start(m_tracer.LogFilePath);
        }

        /// <nodoc />
        [SuppressMessage("Microsoft.Naming", "CA2204:LiteralsShouldBeSpelledCorrectly")]
        [JsonRpcMethod("initialize")]
        protected object Initialize(JToken token)
        {
            var result = GetCapabilities();
            var initializeParams = token.ToObject<InitializeParams>();

            if (initializeParams.RootUri != null)
            {
                m_rootUri = new Uri(initializeParams.RootUri);
            }

            // We can start loading the workspace as soon as a folder gets opened
            if (m_testContext?.ForceSynchronousMessages == true)
            {
                // We synchronously wait for the workspace to load in this case
                TryWaitForWorkspaceLoaded(out _, out _);
            }
            
            if (initializeParams.InitializationOptions != null)
            {
                var initializationOptions = ((JToken)initializeParams.InitializationOptions).ToObject<InitializationOptions>();
                var clientType = initializationOptions.ClientType.ToString();

                Logger.LanguageServerClientType(LoggingContext, clientType);
            }

            return result;
        }

        /// <summary>
        /// Initialized is called by the client after it receives the response to the
        /// <see cref="Initialize(JToken)"/> request.
        /// Until then, it isn't actually ready to handle custom messages 
        /// (such as log file location and workspace loading messages) and
        /// will ignore such messages as the client (such as VSCode) does not
        /// allow connection of custom messages until after it receives the response.
        /// </summary>
        /// <param name="token"></param>
        [SuppressMessage("Microsoft.Naming", "CA2204:LiteralsShouldBeSpelledCorrectly")]
        [JsonRpcMethod("initialized")]
        protected void Initialized(JToken token)
        {
            Analysis.IgnoreResult(
                m_mainRpcChannel.NotifyWithParameterObjectAsync(
                    "dscript/logFileLocation",
                    new LogFileLocationParams {File = m_tracer.LogFilePath}),
                justification: "Fire and Forget"
            );

            Analysis.IgnoreResult(LoadWorkspaceAsync(), "Fire and forget");
        }

        // The capabilities that we are registering to provide
        private static InitializeResult GetCapabilities()
        {
            // The commands we can execute require a little bit of special sauce
            var commandList = CodeActionProvider.GetCommands();
            commandList.AddRange(ExecuteCommandProvider.GetCommands());
            commandList.Add(ReloadWorkspaceCommandString);
            commandList.Add(OpenLogFileCommandString);

            return new InitializeResult
            {
                Capabilities = new ServerCapabilities
                {
                    CodeActionProvider = true,
                    CodeLensProvider = new CodeLensOptions()
                    {
                        ResolveProvider = false,
                    },
                    CompletionProvider = new CompletionOptions
                    {
                        ResolveProvider = true,
                        TriggerCharacters = new[] { "." },
                    },
                    DefinitionProvider = true,
                    DocumentFormattingProvider = true,
                    DocumentSymbolProvider = true,
                    ExecuteCommandProvider = new ExecuteCommandOptions()
                    {
                        Commands = commandList.ToArray(),
                    },
                    HoverProvider = true,
                    RenameProvider = true,
                    ReferencesProvider = true,
                    SignatureHelpProvider = new SignatureHelpOptions()
                    {
                        TriggerCharacters = new[] { "(", "," },
                    },
                    TextDocumentSync = new TextDocumentSyncOptions
                    {
                        Change = TextDocumentSyncKind.Full,
                        OpenClose = true,
                    },
                },
            };
        }

        /// <nodoc />
        [JsonRpcMethod("shutdown")]
        protected object Shutdown()
        {
            m_exitEvent.Set();
            // Free up any resources here
            // TODO: Should we call Dispose?
            return null;
        }

        /// <nodoc />
        [JsonRpcMethod("exit")]
        protected void Exit()
        {
            m_exitEvent.Set();
        }

        private LanguageServiceProviders InitializeProviders(AppState appState)
        {
            var providerContext = new ProviderContext(m_mainRpcChannel, appState.IncrementalWorkspaceProvider, appState.PathTable, Logger, LoggingContext, () => appState, m_testContext);
            return new LanguageServiceProviders(providerContext, appState.EngineAbstraction, m_progressReporter);
        }

        /// <nodoc />
        [JsonRpcMethod("textDocument/didOpen")]
        protected void DidOpenTextDocument(JToken token)
        {
            if (TryWaitForWorkspaceLoaded(out var appState, out var providers))
            {
                var document = token.ToObject<DidOpenTextDocumentParams>().TextDocument;

                var absolutePath = document.Uri.ToAbsolutePath(appState.PathTable);
                appState.DocumentManager.Add(absolutePath, document);

                if (!appState.ContainsSpec(absolutePath))
                {
                    // This is an unknown document. Force the full workspace reload.
                    // Temporary solution: the work item for a proper solution is - 1178366
                    Logger.LanguageServerNewFileWasAdded(LoggingContext, document.Uri);
                    if (ReloadWorkspaceAndWaitForCompletion(appState.DocumentManager, out appState, out providers))
                    {
                        // Workspace reconstruction recreates providers
                        providers.ReportDiagnostics(document);
                    }
                }
            }
        }

        /// <nodoc />
        [JsonRpcMethod("textDocument/didChange")]
        protected void DidChangeTextDocument(JToken token)
        {
            if (TryWaitForWorkspaceLoaded(out var appState, out _))
            {
                var paramsObject = token.ToObject<DidChangeTextDocumentParams>();

                // It is possible to have a race condition here:
                // One thread could be processing DidOpenTextDocument
                // and another one could process DidChangeTextDocument for a document that was not added to a new workspace yet.
                var absolutePath = paramsObject.TextDocument.Uri.ToAbsolutePath(appState.PathTable);
                if (appState.ContainsSpec(absolutePath))
                {
                    appState.DocumentManager.Change(
                        absolutePath,
                        paramsObject.TextDocument,
                        paramsObject.ContentChanges);
                }
            }
        }

        /// <nodoc />
        [JsonRpcMethod("textDocument/didClose")]
        protected void DidCloseTextDocument(JToken token)
        {
            if (TryWaitForWorkspaceLoaded(out var appState, out var providers))
            {
                var paramsObject = token.ToObject<DidCloseTextDocumentParams>();

                var absolutePath = paramsObject.TextDocument.Uri.ToAbsolutePath(appState.PathTable);
                appState.DocumentManager.Remove(absolutePath);

                // The document can be deleted. Lets check it.
                // The file path must not be in URI format, it has to be the 
                // file system version.
                if (!File.Exists(absolutePath.ToString(appState.PathTable)))
                {
                    // The file was removed. Force the full workspace reload.
                    // Temporary solution: the work item for a proper solution is - 1178366
                    Logger.LanguageServerFileWasRemoved(LoggingContext, paramsObject.TextDocument.Uri);
                    ReloadWorkspaceAndWaitForCompletion(appState.DocumentManager, out _, out _);
                }
            }
        }

        /// <summary>
        /// Handle changes to the user settings.
        /// </summary>
        /// <remarks>
        /// This is called quickly after launch of  the plugin and any time the user changes the configuration.
        /// </remarks>
        [JsonRpcMethod("workspace/didChangeConfiguration")]
        protected void DidChangeConfiguration(JToken token)
        {
            // Note: These settings are configured in 2 files.
            // The setting itself (the schema, type, default, etc.) is in \Public\Src\FrontEnd\IDE\VsCode\package.json.
            // The text titles (text resources) is in \Public\Src\FrontEnd\IDE\VsCode\package.nls.json

            var paramsObject = token.ToObject<DidChangeConfigurationParams>();

            Logger.ReportConfigurationChanged(LoggingContext, paramsObject.Settings.ToString());

            var settings = (JObject)paramsObject.Settings;
            var parsedSettings = settings["DScript"].ToObject<DScriptSettings>();

            UpdateSettings(parsedSettings);
        }

        private void UpdateSettings(DScriptSettings settings)
        {
            if (settings.DebugOnStart)
            {
                Debugger.Launch();
            }

            ((JsonRpcWithException)m_mainRpcChannel).FailFastOnException = settings.FailFastOnError;

            m_settings = settings;
        }

        /// <nodoc />
        [JsonRpcMethod("textDocument/completion")]
        protected Result<ArrayOrObject<CompletionItem, CompletionList>, ResponseError> Completion(JToken jToken, CancellationToken cancellationToken)
        {
            var position = jToken.ToObject<TextDocumentPositionParams>();

            return TryExecuteHandler((providers, token) => providers.Completion(position, token), cancellationToken);
        }

        /// <nodoc />
        [JsonRpcMethod("completionItem/resolve")]
        protected Result<CompletionItem, ResponseError> ResolveCompletionItem(JToken jToken, CancellationToken cancellationToken)
        {
            var paramsObject = jToken.ToObject<CompletionItem>();

            return TryExecuteHandler((providers, token) => providers.ResolveCompletionItem(paramsObject, token), cancellationToken);
        }

        /// <nodoc />
        [JsonRpcMethod("textDocument/formatting")]
        protected Result<TextEdit[], ResponseError> DocumentFormatting(JToken jToken, CancellationToken cancellationToken)
        {
            var paramsObject = jToken.ToObject<DocumentFormattingParams>();

            return TryExecuteHandler((providers, token) => providers.FormatDocument(paramsObject, token), cancellationToken);
        }

        /// <nodoc />
        [JsonRpcMethod("textDocument/definition")]
        protected Result<ArrayOrObject<Location, Location>, ResponseError> GotoDefinition(JToken jToken, CancellationToken cancellationToken)
        {
            var documentPosition = jToken.ToObject<TextDocumentPositionParams>();

            return TryExecuteHandler((providers, token) => providers.GetDefinitionAtPosition(documentPosition, token), cancellationToken);
        }

        /// <nodoc />
        [JsonRpcMethod("textDocument/references")]
        protected Result<Location[], ResponseError> FindReferences(JToken jToken, CancellationToken cancellationToken)
        {
            var referenceParams = jToken.ToObject<ReferenceParams>();
            var uri = referenceParams.TextDocument.Uri;
            Contract.Assert(uri != null);

            // The timeout for this operation is way higher.
            var timeout = DefaultOperationDurationThreshold * 20;
            return TryExecuteHandler(
                (providers, token) => providers.GetReferencesAtPosition(referenceParams, token),
                cancellationToken,
                timeout);
        }

        /// <nodoc />
        [JsonRpcMethod("textDocument/codeAction")]
        protected Result<Command[], ResponseError> CodeAction(JToken jToken, CancellationToken cancellationToken)
        {
            var codeActionParams = jToken.ToObject<CodeActionParams>();

            return TryExecuteHandler((providers, token) => providers.CodeAction(codeActionParams, token), cancellationToken);
        }

        /// <nodoc />
        [JsonRpcMethod("textDocument/rename")]
        protected object Rename(JToken jToken, CancellationToken cancellationToken)
        {
            var renameParams = jToken.ToObject<RenameParams>();

            return TryExecuteHandler((providers, token) => providers.Rename(renameParams, token), cancellationToken);
        }

        /// <nodoc />
        [JsonRpcMethod("workspace/executeCommand")]
        protected dynamic ExecuteCommand(JToken jToken, CancellationToken cancellationToken)
        {
            var paramsObject = jToken.ToObject<ExecuteCommandParams>();

            // We treat reload workspace and open log file a bit special. It is possible that we
            // can get here before the workspace parsing has completed succesffully.
            if (paramsObject.Command.Equals(ReloadWorkspaceCommandString, StringComparison.Ordinal))
            {
                ReloadWorkspace();

                return Result.Success;
            }

            if (paramsObject.Command.Equals(OpenLogFileCommandString, StringComparison.Ordinal))
            {
                OpenLogFile();

                return Result.Success;
            }

            return TryExecuteHandler((providers, token) => providers.ExecuteCommand(paramsObject, token), cancellationToken);
        }

        private bool ReloadWorkspaceAndWaitForCompletion(TextDocumentManager currentDocumentManager, out AppState appState, out LanguageServiceProviders providers)
        {
            // In case of adding/removing a document we should recompute the workspace and wait for completion,
            // some other code may rely that the workspace is ready when the method that calls this one is finished.
            ReloadWorkspace(currentDocumentManager);
            return TryWaitForWorkspaceLoaded(out appState, out providers);
        }

        private void ReloadWorkspace(TextDocumentManager currentDocumentManager = null)
        {
            lock (m_loadWorkspaceLock)
            {
                if (m_workspaceLoadingState != WorkspaceLoadingState.InProgress)
                {
                    m_workspaceLoadingState = WorkspaceLoadingState.InProgress;
                    m_workspaceLoadingTask.Reset();
                }
            }

            Analysis.IgnoreResult(LoadWorkspaceAsync(currentDocumentManager), "Fire and forget");
        }

        /// <nodoc />
        [JsonRpcMethod("textDocument/codeLens")]
        protected Result<CodeLens[], ResponseError> CodeLens(JToken jToken, CancellationToken cancellationToken)
        {
            var codeLensParams = jToken.ToObject<CodeLensParams>();

            return TryExecuteHandler((providers, token) => providers.CodeLens(codeLensParams, token), cancellationToken);
        }

        /// <nodoc />
        [JsonRpcMethod("textDocument/hover")]
        protected Result<Hover, ResponseError> Hover(JToken jToken, CancellationToken cancellationToken)
        {
            var paramsObject = jToken.ToObject<TextDocumentPositionParams>();

            return TryExecuteHandler((providers, token) => providers.Hover(paramsObject, token), cancellationToken);
        }

        /// <nodoc />
        [JsonRpcMethod("textDocument/documentSymbol")]
        protected Result<SymbolInformation[], ResponseError> DocumentSymbols(JToken jToken, CancellationToken cancellationToken)
        {
            var documentSymbolsParams = jToken.ToObject<DocumentSymbolParams>();

            return TryExecuteHandler((providers, token) => providers.DocumentSymbols(documentSymbolsParams, token), cancellationToken);
        }

        /// <nodoc />
        [JsonRpcMethod("textDocument/signatureHelp")]
        protected Result<SignatureHelp, ResponseError> SignatureHelp(JToken jToken, CancellationToken cancellationToken)
        {
            var textDocumentPosition = jToken.ToObject<TextDocumentPositionParams>();

            return TryExecuteHandler((providers, token) => providers.SignatureHelp(textDocumentPosition, token), cancellationToken);
        }

        private Result<TResult, ResponseError> TryExecuteHandler<TResult>(
            Func<LanguageServiceProviders, CancellationToken, Result<TResult, ResponseError>> provider,
            CancellationToken cancellationToken,
            int operationThreshold = DefaultOperationDurationThreshold,
            [CallerMemberName] string operationName = null)
        {
            if (!TryWaitForWorkspaceLoaded(out var appState, out var providers))
            {
                return Result.InternalError<TResult>(BuildXL.Ide.LanguageServer.Strings.WorkspaceParsingFailedCannotPerformAction);
            }

            Contract.Assert(providers != null);

            var sw = Stopwatch.StartNew();

            var result = provider(providers, cancellationToken);

            if (sw.ElapsedMilliseconds > operationThreshold)
            {
                Logger.LanguageServerOperationIsTooLong(LoggingContext, operationName, (int) sw.ElapsedMilliseconds);
            }

            return result;
        }

        /// <nodoc />
        public void Dispose()
        {
            Logger.LanguageServerStopped(LoggingContext);

            m_mainRpcChannel.Dispose();

            m_projectManagementProvider = null;

            var workspaceLoadTask = LoadWorkspaceAsync();
            if (workspaceLoadTask.IsCompleted)
            {
                workspaceLoadTask.Result.Item2?.Dispose();
            }

            m_tracer?.Dispose();
        }

        private void SubscribeToUnhandledErrors()
        {
            TaskScheduler.UnobservedTaskException += (sender, ea) =>
            {
                Logger.LanguageServerUnhandledInternalError(LoggingContext, ea.Exception.ToStringDemystified());
            };

            AppDomain.CurrentDomain.UnhandledException += (sender, ea) =>
            {
                var exception = ea.ExceptionObject as Exception;
                Logger.LanguageServerUnhandledInternalError(LoggingContext, exception?.ToStringDemystified() ?? ea.ExceptionObject.ToString());
            };
        }
    }
}
