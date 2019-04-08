// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using BuildXL.Utilities;
using BuildXL.Ide.JsonRpc;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using Nerdbank;
using StreamJsonRpc;
using Test.BuildXL.FrontEnd.Core;
using Newtonsoft.Json.Linq;
using static BuildXL.Utilities.FormattableStringEx;
using Xunit.Abstractions;

namespace BuildXL.Ide.LanguageServer.UnitTests.Helpers
{
    /// <summary>
    /// Helper for running integration tests against the language server app
    /// </summary>
    internal sealed class IntegrationTestHelper : IDisposable
    {
        private const string MainPrelude = "Prelude.bp";
        private const string Config = "config.bc";

        private static readonly string s_rootPath = Path.GetTempPath();
        private static readonly string s_preludeDir = $"{s_rootPath}Prelude" + Path.DirectorySeparatorChar;
        private static readonly string s_pathToConfig = $"{s_rootPath}{Config}";
        private static readonly string s_pathToPreludeModuleConfig = $"{s_preludeDir}module.config.bm";
        private static readonly string s_pathToMainPrelude = $"{s_preludeDir}Prelude.bp";

        /// <summary>
        /// One resolver pointing to the SDK. Implicit config module is supposed to pick up everything else
        /// </summary>
        private static readonly string s_defaultConfig = $@"
config({{
    resolvers: [
        {{
            kind: ""SourceResolver"",
            modules: [ f`{s_pathToPreludeModuleConfig}` ],
        }},
    ]
}});";

        /// <summary>
        /// Standard prelude
        /// </summary>
        private static readonly string s_defaultPreludeModule = $@"
package({{
   name: ""Sdk.Prelude"",
   main: f`{MainPrelude}`
}});";

        private readonly StreamJsonRpc.JsonRpc m_jsonRpc;
        private readonly App m_app;

        private int m_documentCount = 0;

        private readonly ClientStub m_clientStub = new ClientStub();
        private Tuple<Stream, Stream> m_streams;
        private JToken m_lastInvocationResult;

        /// <summary>
        /// Initial set of document items: main config and prelude
        /// </summary>
        private static TextDocumentItem[] ConfigWithPrelude =>
            new[]
            {
                // Main config
                new TextDocumentItem
                {
                    Uri = s_pathToConfig,
                    Text = s_defaultConfig
                },
                // Module configuration for prelude
                new TextDocumentItem
                {
                    Uri = s_pathToPreludeModuleConfig,
                    Text = s_defaultPreludeModule
                },
                // Prelude
                new TextDocumentItem
                {
                    Uri = s_pathToMainPrelude,
                    Text = SpecEvaluationBuilder.FullPreludeContent
                }
            };

        /// <nodoc/>
        public IList<PublishDiagnosticParams> PublishDiagnostics => m_clientStub.PublishDiagnostics;

        /// <nodoc/>
        public IList<ShowMessageParams> ShowMessages => m_clientStub.ShowMessages;

        /// <nodoc/>
        public IList<ShowMessageRequestParams> ShowMessageRequests => m_clientStub.ShowMessageRequests;

        /// <nodoc/>
        public IList<Microsoft.VisualStudio.LanguageServer.Protocol.LogMessageParams> LogMessages => m_clientStub.LogMessages;

        public IList<WorkspaceLoadingParams> WorkspaceLoadingMessages => m_clientStub.WorkspaceLoadingMessages;

        /// <summary>
        /// The last result after doing an <see cref="Invoke{T}(string,T,out Newtonsoft.Json.Linq.JToken)"/>
        /// </summary>
        /// <remarks>
        /// If there was no call to Invoke, this fails
        /// </remarks>
        public JToken LastInvocationResult
        {
            get
            {
                Contract.Assert(m_lastInvocationResult != null);
                return m_lastInvocationResult;
            }

            private set => m_lastInvocationResult = value;
        }

        /// <nodoc/>
        public static IntegrationTestHelper CreateApp(ITestOutputHelper output, params TextDocumentItem[] existedDocuments)
        {
            return new IntegrationTestHelper(output, existedDocuments);
        }

        /// <summary>
        /// Starts the app and call 'initialize'
        /// </summary>
        private IntegrationTestHelper(ITestOutputHelper output, params TextDocumentItem[] existedDocuments)
        {
            m_streams = FullDuplexStream.CreateStreams();

            var testContext = new TestContext(
                existedDocuments.Union(ConfigWithPrelude),
                forceSynchronousMessages: true,
                forceNoRecomputationDelay: true,
                output.WriteLine);

            m_jsonRpc = new StreamJsonRpc.JsonRpc(m_streams.Item1, m_streams.Item2);
            m_app = App.CreateForTesting(m_jsonRpc, testContext);

            RegisterLocalTargets();

            m_jsonRpc.StartListening();

            var init = new InitializeParams { RootUri = s_rootPath };

            m_jsonRpc.InvokeWithParameterObjectAsync<object>("initialize", JToken.FromObject(init)).GetAwaiter().GetResult();
        }

        /// <nodoc/>
        public TextDocumentItem GetDocumentItemFromContent(string content)
        {
            return GetDocumentItemFromContent(".", content);
        }

        /// <nodoc/>
        public static TextDocumentItem CreateDocument(string relativeFileName, string content)
        {
            return new TextDocumentItem { Text = content, Uri = Path.Combine(s_rootPath, relativeFileName) };
        }

        /// <nodoc/>
        public TextDocumentItem GetDocumentItemFromContent(string relativePath, string content)
        {
            var pathToDocument = Path.Combine(s_rootPath, relativePath);
            var documentName = I($"spec{m_documentCount++}.bp");

            var item =
                new TextDocumentItem {Text = content, Uri = Path.Combine(pathToDocument, documentName)};

            return item;
        }

        /// <summary>
        /// Sends a 'textDocument/didOpen' invocation on a document with a given content and default location
        /// </summary>
        public IntegrationTestHelper NotifyDocumentOpened(string documentContent)
        {
            var item = GetDocumentItemFromContent(documentContent);

            return NotifyDocumentOpened(item);
        }

        /// <summary>
        /// Sends a 'textDocument/didOpen' invocation on a document with a given content and a location relative to the default root path
        /// </summary>
        public IntegrationTestHelper NotifyDocumentOpened(string relativePath, string documentContent)
        {
            var item = GetDocumentItemFromContent(relativePath, documentContent);

            return NotifyDocumentOpened(item);
        }

        /// <summary>
        /// Sends a 'textDocument/didOpen' invocation on a text document item. Meant to be used with <see cref="GetDocumentItemFromContent(string)"/>
        /// </summary>
        public IntegrationTestHelper NotifyDocumentOpened(TextDocumentItem item)
        {
            var documentOpenedParams =
                new DidOpenTextDocumentParams {TextDocument = item};

            return NotifyDocumentOpened(documentOpenedParams);
        }

        /// <summary>
        /// Sends a notification to the app
        /// </summary>
        public IntegrationTestHelper Notify<T>(string messageName, T param)
        {
            m_jsonRpc.NotifyWithParameterObjectAsync(messageName, JToken.FromObject(param)).GetAwaiter().GetResult();
            return this;
        }

        /// <summary>
        /// Sends an invocation to the app, returning a generic token as a result
        /// </summary>
        public IntegrationTestHelper Invoke<T>(string messageName, T param, out JToken jtoken)
        {
            jtoken = m_jsonRpc.InvokeWithParameterObjectAsync<JToken>(messageName, JToken.FromObject(param)).GetAwaiter().GetResult();

            LastInvocationResult = jtoken;

            return this;
        }

        /// <summary>
        /// Sends an invocation to the app and ignores the result
        /// </summary>
        public IntegrationTestHelper Invoke<T>(string messageName, T param)
        {
            Invoke(messageName, param, out _);

            return this;
        }

        /// <summary>
        /// Sends an invocation to the app with no parameters and ignores the result
        /// </summary>
        public IntegrationTestHelper Invoke(string messageName)
        {
            Analysis.IgnoreResult(
                m_jsonRpc.InvokeAsync(messageName),
                justification: "Fire and forget"
            );
            return this;
        }

        /// <summary>
        /// Sends an invocation to the app, returning a result with an expected type
        /// </summary>
        /// <remarks>
        /// The returned result must have a type with a parameterless constructor so it can be populated from a <see cref="JToken"/>
        /// </remarks>
        public IntegrationTestHelper Invoke<TParam, TResult>(string messageName, TParam param, out TResult result)
        {
            var jtoken = m_jsonRpc.InvokeWithParameterObjectAsync<JToken>(messageName, JToken.FromObject(param)).GetAwaiter().GetResult();

            LastInvocationResult = jtoken;

            result = CreateInstanceAndPopulate<TResult>(jtoken);

            return this;
        }

        /// <summary>
        /// Sends an notification to the app.
        /// </summary>
        public IntegrationTestHelper SendNotification<TParam>(string messageName, TParam param)
        {
            m_jsonRpc.InvokeWithParameterObjectAsync<JToken>(messageName, JToken.FromObject(param)).GetAwaiter().GetResult();

            return this;
        }

        /// <summary>
        /// Gets the last result of an invocation with a specific type. <see cref="LastInvocationResult"/>
        /// </summary>
        /// <remarks>
        /// The returned result must have a type with a parameterless constructor so it can be populated from a <see cref="JToken"/>
        /// </remarks>
        public T GetLastInvocationResult<T>()
        {
            return CreateInstanceAndPopulate<T>(LastInvocationResult);
        }

        private IntegrationTestHelper NotifyDocumentOpened(DidOpenTextDocumentParams didopenTextDocumentParams)
        {
            m_jsonRpc.InvokeWithParameterObjectAsync<object>("textDocument/didOpen", JToken.FromObject(didopenTextDocumentParams)).GetAwaiter().GetResult();

            return this;
        }

        private IntegrationTestHelper NotifyDocumentChanged(DidChangeTextDocumentParams documentParams)
        {
            m_jsonRpc.InvokeWithParameterObjectAsync<object>("textDocument/didChange", JToken.FromObject(documentParams)).GetAwaiter().GetResult();

            return this;
        }

        private TResult CreateInstanceAndPopulate<TResult>(JToken jtoken)
        {
            var result = Activator.CreateInstance<TResult>();
            m_jsonRpc.JsonSerializer.Populate(jtoken.CreateReader(), result);

            return result;
        }

        /// <nodoc/>
        public void Dispose()
        {
            m_jsonRpc.Dispose();
            m_app.Dispose();
        }

        private void RegisterLocalTargets()
        {
            m_jsonRpc.AddLocalRpcTarget(m_app);
            m_jsonRpc.AddLocalRpcMethod("textDocument/publishDiagnostics", new Action<JToken>(param => m_clientStub.PublishDiagnostic(param)));
            m_jsonRpc.AddLocalRpcMethod("window/showMessage", new Action<JToken>(param => m_clientStub.ShowMessage(param)));
            m_jsonRpc.AddLocalRpcMethod("window/showMessageRequest", new Action<JToken>(param => m_clientStub.ShowMessageRequest(param)));
            m_jsonRpc.AddLocalRpcMethod("window/logMessage", new Action<JToken>(param => m_clientStub.LogMessage(param)));
            m_jsonRpc.AddLocalRpcMethod("dscript/workspaceLoading", new Action<JToken>(param => m_clientStub.WorkspaceLoadingMessage(param)));
        }
    }

    /// <summary>
    /// A stub for the app to receive notifications
    /// </summary>
    /// <remarks>
    /// It collects every type of notification in a separate collection
    /// </remarks>
    public sealed class ClientStub
    {
        /// <nodoc/>
        public IList<PublishDiagnosticParams> PublishDiagnostics { get; } = new List<PublishDiagnosticParams>();

        /// <nodoc/>
        public IList<ShowMessageParams> ShowMessages { get; } = new List<ShowMessageParams>();

        /// <nodoc/>
        public IList<ShowMessageRequestParams> ShowMessageRequests { get; } = new List<ShowMessageRequestParams>();

        /// <nodoc/>
        public IList<Microsoft.VisualStudio.LanguageServer.Protocol.LogMessageParams> LogMessages { get; } = new List<Microsoft.VisualStudio.LanguageServer.Protocol.LogMessageParams>();

        /// <nodoc/>
        public IList<WorkspaceLoadingParams> WorkspaceLoadingMessages { get; } = new List<WorkspaceLoadingParams>();

        /// <nodoc/>
        internal void PublishDiagnostic(JToken @params)
        {
            var diagnostics = @params.ToObject<PublishDiagnosticParams>();
            PublishDiagnostics.Add(diagnostics);
        }

        /// <nodoc/>
        internal void ShowMessage(JToken @params)
        {
            ShowMessages.Add(@params.ToObject<ShowMessageParams>());
        }

        /// <nodoc/>
        internal void ShowMessageRequest(JToken @params)
        {
            ShowMessageRequests.Add(@params.ToObject<ShowMessageRequestParams>());
        }

        /// <nodoc/>
        internal void LogMessage(JToken @params)
        {
            LogMessages.Add(@params.ToObject<Microsoft.VisualStudio.LanguageServer.Protocol.LogMessageParams>());
        }

        internal void WorkspaceLoadingMessage(JToken @params)
        {
            WorkspaceLoadingMessages.Add(@params.ToObject<WorkspaceLoadingParams>());
        }
    }
}
