using System;
using System.Net;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Ipc;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.Common.Connectivity;
using BuildXL.Plugin;
using BuildXL.Plugin.Grpc;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using ILogger = Grpc.Core.Logging.ILogger;

namespace Test.BuildXL.Plugin
{
    /// <summary>
    /// Tests for <see cref="PluginManager" />
    /// </summary>
    public class PluginManagerTests: IAsyncLifetime
    {
        private PluginManager m_pluginManager;
        private LoggingContext m_loggingContext = new LoggingContext("UnitTest");

        private const string PluginPath1 = "test1";
        private const string PluginId1 = "test1";
        private static readonly string m_pluginPort1 = IpcFactory.GetProvider().LoadAndRenderMoniker(IpcFactory.GetProvider().CreateNewMoniker().Id);

        private const string PluginPath2 = "test2";
        private const string PluginId2 = "test2";
        private static readonly string m_pluginPort2 = IpcFactory.GetProvider().LoadAndRenderMoniker(IpcFactory.GetProvider().CreateNewMoniker().Id);
        private static readonly string m_pluginPort3 = IpcFactory.GetProvider().LoadAndRenderMoniker(IpcFactory.GetProvider().CreateNewMoniker().Id);

        private static Func<Task<PluginResponseResult<bool>>> s_booleanResponseSucceed = () => Task.FromResult(new PluginResponseResult<bool>(true, PluginResponseState.Succeeded, "0", 0));
        private static Func<Task<PluginResponseResult<bool>>> s_booleanResponsetFailed = () => Task.FromResult(new PluginResponseResult<bool>(PluginResponseState.Failed, "0", 0, new Failure<string>("")));
        private static Func<Task<PluginResponseResult<bool>>> s_booleanResponseThrowException = () => throw new Exception();

        private static Func<Task<PluginResponseResult<LogParseResult>>> s_logParseResponseSucceed = () => Task.FromResult(new PluginResponseResult<LogParseResult>(new LogParseResult() { ParsedMessage=""}, PluginResponseState.Succeeded, "0", 0));
        private static Func<Task<PluginResponseResult<LogParseResult>>> s_logParseResponsetFailed = () => Task.FromResult(new PluginResponseResult<LogParseResult>(PluginResponseState.Failed, "0", 0, new Failure<string>("")));
        private static Func<Task<PluginResponseResult<LogParseResult>>> s_logParseResponseThrowException = () => throw new Exception();

        private static Func<Task<PluginResponseResult<List<PluginMessageType>>>> s_pluginMessageTypeResponseSucceed = () => Task.FromResult(new PluginResponseResult<List<PluginMessageType>>(new List<PluginMessageType>() { PluginMessageType.ParseLogMessage }, PluginResponseState.Succeeded, "0", 0));
        private static Func<Task<PluginResponseResult<List<PluginMessageType>>>> s_unknownMessageTypeResponseSucceed = () => Task.FromResult(new PluginResponseResult<List<PluginMessageType>>(new List<PluginMessageType>(){ PluginMessageType.Unknown }, PluginResponseState.Succeeded, "0", 0));
        private static Func<Task<PluginResponseResult<List<PluginMessageType>>>> s_pluginMessageTypeResponseFaialed = () => Task.FromResult(new PluginResponseResult<List<PluginMessageType>>(PluginResponseState.Failed, "0", 0, new Failure<string>("")));
        private static Func<Task<PluginResponseResult<List<PluginMessageType>>>> s_pluginMessageTypeResponseThrowException = () => throw new Exception();

        private readonly MockedPluginClient m_mockedPluginClient = new MockedPluginClient(
            startFunc: s_booleanResponseSucceed,
            stopFunc: s_booleanResponseSucceed,
            supportedMessageTyepFunc: s_pluginMessageTypeResponseSucceed,
            logparseFunc: s_logParseResponseSucceed
        );

        private readonly ILogger m_logger = new MockLogger();
        private readonly int m_port = TcpIpConnectivity.ParsePortNumber(m_pluginPort3);

        public PluginManagerTests()
        {
            m_pluginManager = new PluginManager(m_loggingContext, "empty", new[] { "empty" });
        }

        private PluginCreationArgument GetMockPluginCreationArguments(Func<PluginConnectionOption, IPluginClient> pluginClientCreator)
        {
            return new PluginCreationArgument()
            {
                PluginPath = PluginPath1,
                RunInSeparateProcess = false,
                PluginId = PluginId1,
                ConnectionOption = new PluginConnectionOption()
                {
                    IpcMoniker = m_pluginPort1,
                    LogDir = "",
                    Logger = PluginLogUtils.CreateLoggerForPluginClients(m_loggingContext, PluginId1)
                },

                CreatePluginClientFunc = pluginClientCreator,
                //mocked plugin client doesn't requires to start plugin server running
                RunInPluginThreadAction = () => { },
            };
        }

        private PluginCreationArgument GetMockSecondPluginCreationArguments(Func<PluginConnectionOption, IPluginClient> pluginClientCreator)
        {
            return new PluginCreationArgument()
            {
                PluginPath = PluginPath2,
                RunInSeparateProcess = false,
                PluginId = PluginId2,
                ConnectionOption = new PluginConnectionOption()
                {
                    IpcMoniker = m_pluginPort2,
                    LogDir = "",
                    Logger = PluginLogUtils.CreateLoggerForPluginClients(m_loggingContext, PluginId2)
                },

                CreatePluginClientFunc = pluginClientCreator,
                //mocked plugin client doesn't requires to start plugin server running
                RunInPluginThreadAction = () => {  },
            };
        }

        private PluginCreationArgument GetPluginCreationArguments(Func<PluginConnectionOption, IPluginClient> pluginClientCreator)
        {
            return new PluginCreationArgument()
            {
                PluginPath = PluginPath1,
                RunInSeparateProcess = false,
                PluginId = PluginId1,
                ConnectionOption = new PluginConnectionOption()
                {
                    IpcMoniker = m_pluginPort3,
                    LogDir = "",
                    Logger = m_logger
                },

                CreatePluginClientFunc = pluginClientCreator,
                RunInPluginThreadAction = async () =>
                {
                    using (var pluginServer = new LogParsePluginServer(m_port, new MockLogger()))
                    {
                        pluginServer.Start();

                        await pluginServer.ShutdownCompletionTask;
                    }
                }
            };
        }

        [Fact]
        public async Task LoadMockedPluginShouldSucceedAsync()
        {
            var args = GetMockPluginCreationArguments((options) => m_mockedPluginClient);
            var res = await m_pluginManager.GetOrCreateAsync(args);

            Assert.True(res.Succeeded);

            var plugin = res.Result;

            Assert.Equal(plugin.Status, PluginStatus.Running);
            Assert.True(plugin.StartCompletionTask.IsCompleted);
            Assert.Equal(plugin.SupportedMessageType.Count, 1);
            Assert.Equal(plugin.SupportedMessageType[0], PluginMessageType.ParseLogMessage);
            Assert.Equal(m_pluginManager.PluginLoadedSuccessfulCount, 1);
            Assert.Equal(m_pluginManager.PluginsCount, 1);
            Assert.True(m_pluginManager.CanHandleMessage(PluginMessageType.ParseLogMessage));

            var logParseResult = await m_pluginManager.LogParseAsync("", true);
            Assert.True(logParseResult.Succeeded);
        }

        [Fact]
        public async Task LoadDuplicatedPluginShouldBeNoopAsync()
        { 
            var args = GetMockPluginCreationArguments((options) => m_mockedPluginClient);
            var res = await m_pluginManager.GetOrCreateAsync(args);
            
            //load duplicated plugin
            res = await m_pluginManager.GetOrCreateAsync(args);

            Assert.True(res.Succeeded);
            //duplicated plugin path only load once
            Assert.Equal(m_pluginManager.PluginLoadedSuccessfulCount, 1);
            Assert.Equal(m_pluginManager.PluginsCount, 1);
        }

        [Fact]
        public async Task LoadTwoPluginsWithSameSupportedMessageTypeShouldSucceedAsync()
        {
            var args = GetMockPluginCreationArguments((options) => m_mockedPluginClient);
            var res = await m_pluginManager.GetOrCreateAsync(args);
            Assert.True(res.Succeeded);

            args = GetMockSecondPluginCreationArguments((options) => m_mockedPluginClient);
            res = await m_pluginManager.GetOrCreateAsync(args);

            Assert.Equal(m_pluginManager.PluginLoadedSuccessfulCount, 2);
            Assert.Equal(m_pluginManager.PluginsCount, 2);
            Assert.Equal(m_pluginManager.PluginHandlersCount, 1);
        }

        [Fact]
        public async Task StopAndCleanAllPluginsShouldSucceedAsync()
        {
            var args = GetMockPluginCreationArguments((options) => m_mockedPluginClient);
            var res = await m_pluginManager.GetOrCreateAsync(args);

            await m_pluginManager.Stop();
            m_pluginManager.Clear();

            Assert.Equal(m_pluginManager.PluginsCount, 0);
        }

        [Fact]
        public async Task FailedToLoadPluginAsync()
        {
            m_mockedPluginClient.MockedStartFunc = s_booleanResponsetFailed;

            var args = GetMockPluginCreationArguments((options) => m_mockedPluginClient);
            var res = await m_pluginManager.GetOrCreateAsync(args);

            Assert.False(res.Succeeded);
            Assert.Equal(m_pluginManager.PluginLoadedSuccessfulCount, 0);
            Assert.Equal(m_pluginManager.PluginsCount, 0);
            Assert.Equal(m_pluginManager.PluginLoadedFailureCount, 1);
        }

        [Fact]
        public async Task FailedToLoadPluginWithExceptionAsync()
        {
            m_mockedPluginClient.MockedStartFunc = s_booleanResponseThrowException;

            var args = GetMockPluginCreationArguments((options) => m_mockedPluginClient);
            var res = await m_pluginManager.GetOrCreateAsync(args);

            Assert.False(res.Succeeded);
            Assert.Equal(m_pluginManager.PluginLoadedSuccessfulCount, 0);
            Assert.Equal(m_pluginManager.PluginsCount, 0);
            Assert.Equal(m_pluginManager.PluginLoadedFailureCount, 1);
        }

        [Fact]
        public async Task LoadPluginWithUnknownSupportedMessageTypeAsync()
        {
            m_mockedPluginClient.MockedSupportedMessageTypeFunc = s_unknownMessageTypeResponseSucceed;

            var args = GetMockPluginCreationArguments((options) => m_mockedPluginClient);
            var res = await m_pluginManager.GetOrCreateAsync(args);

            Assert.True(res.Succeeded);
            Assert.Equal(m_pluginManager.PluginLoadedSuccessfulCount, 1);
            Assert.Equal(m_pluginManager.PluginsCount, 1);

            var logParseResult =await  m_pluginManager.LogParseAsync("", true);
            Assert.False(logParseResult.Succeeded);
        }

        [Fact]
        public async Task FailedToGetPluginSupportedMessageTypeAsync()
        {
            m_mockedPluginClient.MockedSupportedMessageTypeFunc = s_pluginMessageTypeResponseFaialed;

            var args = GetMockPluginCreationArguments((options) => m_mockedPluginClient);
            var res = await m_pluginManager.GetOrCreateAsync(args);

            Assert.True(res.Succeeded);
            Assert.Equal(m_pluginManager.PluginLoadedSuccessfulCount, 1);

            // plugin is loaded, so still count it
            Assert.Equal(m_pluginManager.PluginsCount, 1);
            // unknow message type will not being registered
            Assert.Equal(m_pluginManager.PluginHandlersCount, 0);
        }

        [Fact]
        public async Task FailedToGetPluginSupportedMessageTypeWithExceptionAsync()
        {
            m_mockedPluginClient.MockedSupportedMessageTypeFunc = s_pluginMessageTypeResponseThrowException;

            var args = GetMockPluginCreationArguments((options) => m_mockedPluginClient);
            var res = await m_pluginManager.GetOrCreateAsync(args);

            Assert.True(res.Succeeded);
            Assert.Equal(m_pluginManager.PluginLoadedSuccessfulCount, 1);

            Assert.Equal(m_pluginManager.PluginsCount, 1);
        }

        [Fact]
        public async Task FailedToGetLogParseResponseAsync()
        {
            m_mockedPluginClient.MockedLogParseFunc = s_logParseResponsetFailed;

            var args = GetMockPluginCreationArguments((options) => m_mockedPluginClient);
            var res  = await m_pluginManager.GetOrCreateAsync(args);
            var logParseResult = await m_pluginManager.LogParseAsync("", true);

            Assert.False(logParseResult.Succeeded);
        }

        [Fact]
        public async Task FailedToGetLogParseResponseWithExceptionAsync()
        {
            m_mockedPluginClient.MockedLogParseFunc = s_logParseResponseThrowException;

            var args = GetMockPluginCreationArguments((options) => m_mockedPluginClient);
            var res = await m_pluginManager.GetOrCreateAsync(args);
            var logParseResult = await m_pluginManager.LogParseAsync("", true);

            Assert.False(logParseResult.Succeeded);
        }

        [Fact]
        public async Task LoadNonMockedLogParsePluginShouldSucceedAsync()
        {
            var args = GetPluginCreationArguments((options) =>
            {
                return new PluginClient(IPAddress.Loopback.ToString(), m_port, m_logger);
            });

            var res = await m_pluginManager.GetOrCreateAsync(args);
            Assert.True(res.Succeeded);
            Assert.Equal(m_pluginManager.PluginLoadedSuccessfulCount, 1);
            Assert.Equal(m_pluginManager.PluginsCount, 1);
            Assert.True(m_pluginManager.CanHandleMessage(PluginMessageType.ParseLogMessage));

            var logParseResult = await m_pluginManager.LogParseAsync("", true);
            Assert.True(logParseResult.Succeeded);
            XAssert.Contains(logParseResult.Result.ParsedMessage, "[plugin]");
        }

        public Task InitializeAsync()
        {
            return Task.Run(() => { });
        }

        public Task DisposeAsync()
        {
            return Task.Run(() =>
            {
                m_pluginManager.Stop().Wait();
                m_pluginManager.Clear();
            });
        }
    }
}
