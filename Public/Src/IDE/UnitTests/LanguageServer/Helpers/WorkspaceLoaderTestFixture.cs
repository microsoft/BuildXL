// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Reflection;
using BuildXL.Ide.LanguageServer.Providers;
using BuildXL.Ide.LanguageServer.Tracing;
using BuildXL.Utilities;
using StreamJsonRpc;

namespace BuildXL.Ide.LanguageServer.UnitTests.Helpers
{
    /// <summary>
    /// Creates a workspace and, via the magic of xunit, allows that single
    /// instance to be shared amongst multiple tests.
    /// </summary>
    public class WorkspaceLoaderTestFixture : IDisposable
    {
        private readonly string m_rootPath;

        static WorkspaceLoaderTestFixture()
        {
            AppDomain.CurrentDomain.AssemblyResolve += AssemblyLoaderHelper.Newtonsoft11DomainAssemblyResolve;  
        } 

        public ProviderContext ProviderContext { get; }

        private readonly AppState m_appState;

        public AppState GetAppState() => m_appState;

        public WorkspaceLoaderTestFixture()
        {
            m_rootPath = Path.Combine(Path.GetDirectoryName(AssemblyHelper.GetAssemblyLocation(Assembly.GetExecutingAssembly())), @"testData");
            m_appState = AppState.TryCreateWorkspace(new Uri(m_rootPath), progressHandler: null);

            var logger = Logger.CreateLogger();
            var context = new BuildXL.Utilities.Instrumentation.Common.LoggingContext("DsLanguageServer");
            ProviderContext = new ProviderContext(StreamJsonRpc.JsonRpc.Attach(new MemoryStream(), this), m_appState.IncrementalWorkspaceProvider, m_appState.PathTable, logger, context, () => m_appState);
        }

        public Uri GetChildUri(string pathRelativeToRoot)
        {
            return new Uri(Path.Combine(m_rootPath, pathRelativeToRoot));
        }

        public void Dispose()
        { }
    }
}
