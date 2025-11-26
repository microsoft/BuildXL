using System;
using System.Net;
using System.Text.Json;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Ipc;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.Common.Connectivity;
using BuildXL.Plugin;
using BuildXL.Plugin.Grpc;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Instrumentation.Common;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using ILogger = Grpc.Core.Logging.ILogger;
using Xunit.Abstractions;

namespace Test.BuildXL.Plugin
{
    /// <summary>
    /// Tests for <see cref="PluginManager" />
    /// </summary>
    public class PluginManagerTests : TemporaryStorageTestBase, IAsyncLifetime
    {
        private readonly PluginManager m_pluginManager;
        private readonly LoggingContext m_loggingContext = new LoggingContext("UnitTest");
 
        private const string PluginPath1 = "test1";
        private const string PluginId1 = "test1";
        private static readonly string s_pluginPort1 = IpcFactory.GetProvider().LoadAndRenderMoniker(IpcMoniker.CreateNew().Id);

        private const string PluginPath2 = "test2";
        private const string PluginId2 = "test2";
        private static readonly string s_pluginPort2 = IpcFactory.GetProvider().LoadAndRenderMoniker(IpcMoniker.CreateNew().Id);
        private static readonly string s_pluginPort3 = IpcFactory.GetProvider().LoadAndRenderMoniker(IpcMoniker.CreateNew().Id);

        private readonly static Func<Task<PluginResponseResult<bool>>> s_booleanResponseSucceed = () => Task.FromResult(new PluginResponseResult<bool>(true, PluginResponseState.Succeeded, "0"));
        private readonly static Func<Task<PluginResponseResult<bool>>> s_booleanResponseFailed = () => Task.FromResult(new PluginResponseResult<bool>(PluginResponseState.Failed, "0", new Failure<string>("")));
        private readonly static Func<Task<PluginResponseResult<bool>>> s_booleanResponseThrowException = () => throw new Exception();

        private readonly static Func<Task<PluginResponseResult<LogParseResult>>> s_logParseResponseSucceeded = () => Task.FromResult(new PluginResponseResult<LogParseResult>(new LogParseResult() { ParsedMessage = ""}, PluginResponseState.Succeeded, "0"));
        private readonly static Func<Task<PluginResponseResult<LogParseResult>>> s_logParseResponseFailed = () => Task.FromResult(new PluginResponseResult<LogParseResult>(PluginResponseState.Failed, "0", new Failure<string>("")));
        private readonly static Func<Task<PluginResponseResult<LogParseResult>>> s_logParseResponseThrowException = () => throw new Exception();

        private readonly static Func<Task<PluginResponseResult<ProcessResultMessageResponse>>> s_processResultResponseSucceeded = () => Task.FromResult(new PluginResponseResult<ProcessResultMessageResponse>(new ProcessResultMessageResponse() { ExitCode = 1111 }, PluginResponseState.Succeeded, "0"));
        private readonly static Func<Task<PluginResponseResult<ProcessResultMessageResponse>>> s_processResultResponseFailed = () => Task.FromResult(new PluginResponseResult<ProcessResultMessageResponse>(PluginResponseState.Failed, "0", new Failure<string>("")));
        private readonly static Func<Task<PluginResponseResult<ProcessResultMessageResponse>>> s_processResultResponseThrowException = () => throw new Exception();

        private readonly static Func<Task<PluginResponseResult<List<PluginMessageType>>>> s_pluginMessageTypeResponseSucceed = () => Task.FromResult(new PluginResponseResult<List<PluginMessageType>>(new List<PluginMessageType>() { PluginMessageType.ParseLogMessage }, PluginResponseState.Succeeded, "0"));
        private readonly static Func<Task<PluginResponseResult<List<PluginMessageType>>>> s_unknownMessageTypeResponseSucceed = () => Task.FromResult(new PluginResponseResult<List<PluginMessageType>>(new List<PluginMessageType>(){ PluginMessageType.Unknown }, PluginResponseState.Succeeded, "0"));
        private readonly static Func<Task<PluginResponseResult<List<PluginMessageType>>>> s_pluginMessageTypeResponseFailed = () => Task.FromResult(new PluginResponseResult<List<PluginMessageType>>(PluginResponseState.Failed, "0", new Failure<string>("")));
        private readonly static Func<Task<PluginResponseResult<List<PluginMessageType>>>> s_pluginMessageTypeResponseThrowException = () => throw new Exception();

        private readonly MockedPluginClient m_mockedPluginClient = new MockedPluginClient(
            startFunc: s_booleanResponseSucceed,
            stopFunc: s_booleanResponseSucceed,
            supportedMessageTyepFunc: s_pluginMessageTypeResponseSucceed,
            logparseFunc: s_logParseResponseSucceeded,
            processResultFunc: s_processResultResponseSucceeded
        );

        private readonly ILogger m_logger = new MockLogger();
        private readonly int m_port = TcpIpConnectivity.ParsePortNumber(s_pluginPort3);

        public PluginManagerTests(ITestOutputHelper output) : base(output)
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
                    IpcMoniker = s_pluginPort1,
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
                    IpcMoniker = s_pluginPort2,
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
                    IpcMoniker = s_pluginPort3,
                    LogDir = "",
                    Logger = m_logger
                },

                CreatePluginClientFunc = pluginClientCreator,
                RunInPluginThreadAction = () =>
                {
                    using (var pluginServer = new MockPluginServer(m_port, new MockLogger()))
                    {
                        pluginServer.Start();

                        pluginServer.ShutdownCompletionTask.GetAwaiter().GetResult();
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
            Assert.Equal(1, m_pluginManager.PluginLoadedSuccessfulCount);
            Assert.Equal(1, m_pluginManager.PluginsCount);
            Assert.True(m_pluginManager.CanHandleMessage(PluginMessageType.ParseLogMessage));

            var logParseResult = await m_pluginManager.LogParseAsync("", "", true);
            Assert.True(logParseResult.Succeeded);
        }

        [Fact]
        public async Task LoadMockedPluginFromConfigShouldSucceedAsync()
        {
            string configFile = GetFullPath("testPlugin.config");

            int customTimeout = 1234;
            string testExe = "test.exe";
            List<string> supportedProcesses = new List<string> { testExe };
            List<PluginMessageType> supportedMessageTypes = new List<PluginMessageType> { PluginMessageType.ParseLogMessage };

            WriteFile(configFile, JsonSerializer.Serialize(new PluginConfig
            {
                PluginPath = PluginPath1,
                Timeout = customTimeout,
                SupportedProcesses = supportedProcesses,
                MessageTypes = supportedMessageTypes,
            }));

            var args = GetMockPluginCreationArguments((options) => m_mockedPluginClient);
            args.PluginPath = configFile;
            var res = await m_pluginManager.GetOrCreateAsync(args);

            Assert.True(res.Succeeded);

            var plugin = res.Result;

            Assert.Equal(PluginStatus.Running, plugin.Status);
            Assert.True(plugin.StartCompletionTask.IsCompleted);
            Assert.Equivalent(supportedMessageTypes, plugin.SupportedMessageType);
            Assert.Equivalent(supportedProcesses, plugin.PluginClient.SupportedProcesses);
            Assert.Equal(customTimeout, plugin.PluginClient.RequestTimeout);
            Assert.Equal(PluginMessageType.ParseLogMessage, plugin.SupportedMessageType[0]);
            Assert.Equal(1, m_pluginManager.PluginLoadedSuccessfulCount);
            Assert.Equal(1, m_pluginManager.PluginsCount);
            Assert.True(m_pluginManager.CanHandleMessage(PluginMessageType.ParseLogMessage));

            var logParseResult = await m_pluginManager.LogParseAsync(testExe, "", true);
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
            Assert.Equal(1, m_pluginManager.PluginLoadedSuccessfulCount);
            Assert.Equal(1, m_pluginManager.PluginsCount);
        }

        [Fact]
        public async Task LoadTwoPluginsWithSameSupportedMessageTypeShouldSucceedAsync()
        {
            var args = GetMockPluginCreationArguments((options) => m_mockedPluginClient);
            var res = await m_pluginManager.GetOrCreateAsync(args);
            Assert.True(res.Succeeded);

            args = GetMockSecondPluginCreationArguments((options) => m_mockedPluginClient);
            res = await m_pluginManager.GetOrCreateAsync(args);

            Assert.Equal(2, m_pluginManager.PluginLoadedSuccessfulCount);
            Assert.Equal(2, m_pluginManager.PluginsCount);
            Assert.Equal(1, m_pluginManager.PluginHandlersCount); // Although 2 plugins are loaded, only 1 plugin handler exists
                                                                  // (we currently can't have two plugins handling the same message type,
                                                                  // so this unit test is a bit of a misnomer)
        }

        [Fact]
        public async Task StopAndCleanAllPluginsShouldSucceedAsync()
        {
            var args = GetMockPluginCreationArguments((options) => m_mockedPluginClient);
            var res = await m_pluginManager.GetOrCreateAsync(args);

            await m_pluginManager.Stop();
            m_pluginManager.Clear();

            Assert.Equal(0, m_pluginManager.PluginsCount);
        }

        [Fact]
        public async Task FailedToLoadPluginAsync()
        {
            m_mockedPluginClient.MockedStartFunc = s_booleanResponseFailed;

            var args = GetMockPluginCreationArguments((options) => m_mockedPluginClient);
            var res = await m_pluginManager.GetOrCreateAsync(args);

            Assert.False(res.Succeeded);
            Assert.Equal(0, m_pluginManager.PluginLoadedSuccessfulCount);
            Assert.Equal(0, m_pluginManager.PluginsCount);
            Assert.Equal(1, m_pluginManager.PluginLoadedFailureCount);
        }

        [Fact]
        public async Task FailedToLoadPluginWithExceptionAsync()
        {
            m_mockedPluginClient.MockedStartFunc = s_booleanResponseThrowException;

            var args = GetMockPluginCreationArguments((options) => m_mockedPluginClient);
            var res = await m_pluginManager.GetOrCreateAsync(args);

            Assert.False(res.Succeeded);
            Assert.Equal(0, m_pluginManager.PluginLoadedSuccessfulCount);
            Assert.Equal(0, m_pluginManager.PluginsCount);
            Assert.Equal(1, m_pluginManager.PluginLoadedFailureCount);
        }

        [Fact]
        public async Task LoadPluginWithUnknownSupportedMessageTypeAsync()
        {
            m_mockedPluginClient.MockedSupportedMessageTypeFunc = s_unknownMessageTypeResponseSucceed;

            var args = GetMockPluginCreationArguments((options) => m_mockedPluginClient);
            var res = await m_pluginManager.GetOrCreateAsync(args);

            Assert.True(res.Succeeded);
            Assert.Equal(1, m_pluginManager.PluginLoadedSuccessfulCount);
            Assert.Equal(1, m_pluginManager.PluginsCount);

            var logParseResult =await  m_pluginManager.LogParseAsync("", "", true);
            Assert.False(logParseResult.Succeeded);
        }

        [Fact]
        public async Task FailedToGetPluginSupportedMessageTypeAsync()
        {
            m_mockedPluginClient.MockedSupportedMessageTypeFunc = s_pluginMessageTypeResponseFailed;

            var args = GetMockPluginCreationArguments((options) => m_mockedPluginClient);
            var res = await m_pluginManager.GetOrCreateAsync(args);

            Assert.True(res.Succeeded);
            Assert.Equal(1, m_pluginManager.PluginLoadedSuccessfulCount);

            // plugin is loaded, so still count it
            Assert.Equal(1, m_pluginManager.PluginsCount);
            // unknow message type will not being registered
            Assert.Equal(0, m_pluginManager.PluginHandlersCount);
        }

        [Fact]
        public async Task FailedToGetPluginSupportedMessageTypeWithExceptionAsync()
        {
            m_mockedPluginClient.MockedSupportedMessageTypeFunc = s_pluginMessageTypeResponseThrowException;

            var args = GetMockPluginCreationArguments((options) => m_mockedPluginClient);
            var res = await m_pluginManager.GetOrCreateAsync(args);

            Assert.True(res.Succeeded);
            Assert.Equal(1, m_pluginManager.PluginLoadedSuccessfulCount);

            Assert.Equal(1, m_pluginManager.PluginsCount);
        }

        [Fact]
        public async Task FailedToGetLogParseResponseAsync()
        {
            m_mockedPluginClient.MockedLogParseFunc = s_logParseResponseFailed;

            var args = GetMockPluginCreationArguments((options) => m_mockedPluginClient);
            var res  = await m_pluginManager.GetOrCreateAsync(args);
            var logParseResult = await m_pluginManager.LogParseAsync("", "", true);

            Assert.False(logParseResult.Succeeded);
        }

        [Fact]
        public async Task FailedToGetLogParseResponseWithExceptionAsync()
        {
            m_mockedPluginClient.MockedLogParseFunc = s_logParseResponseThrowException;

            var args = GetMockPluginCreationArguments((options) => m_mockedPluginClient);
            var res = await m_pluginManager.GetOrCreateAsync(args);
            var logParseResult = await m_pluginManager.LogParseAsync("", "", true);

            Assert.False(logParseResult.Succeeded);
        }

#if NET6_0_OR_GREATER
        [Fact]
        public async Task LoadNonMockedLogParsePluginShouldSucceedAsync()
        {
            var args = GetPluginCreationArguments((options) =>
            {
                return new PluginClient(IPAddress.Loopback.ToString(), m_port, m_logger);
            });

            var res = await m_pluginManager.GetOrCreateAsync(args);
            XAssert.PossiblySucceeded(res);
            Assert.Equal(1, m_pluginManager.PluginLoadedSuccessfulCount);
            Assert.Equal(1, m_pluginManager.PluginsCount);
            Assert.True(m_pluginManager.CanHandleMessage(PluginMessageType.ParseLogMessage));

            var logParseResult = await m_pluginManager.LogParseAsync("", "", true);
            Assert.True(logParseResult.Succeeded);
            XAssert.Contains(logParseResult.Result.ParsedMessage, "[plugin]");
        }
#endif

        [Fact]
        public async Task FailedToGetProcessResultResponseAsync() //Not sure if this is necessary - it's nearly identical to FailedToGetLogParseResponseAsync and doesn't test much new functionality
        {
            m_mockedPluginClient.MockedProcessResultFunc = s_processResultResponseFailed;

            var args = GetMockPluginCreationArguments((options) => m_mockedPluginClient);
            var res  = await m_pluginManager.GetOrCreateAsync(args);
            var processResultMessageResponse = await m_pluginManager.ProcessResultAsync("", "", null, null, null, 0, "");

            Assert.False(processResultMessageResponse.Succeeded);
        }

        [Fact]
        public async Task FailedToGetProcessResultResponseWithExceptionAsync() //Not sure if this is necessary - it's nearly identical to FailedToGetLogParseResponseWithExceptionAsync and doesn't test much new functionality
        {
            m_mockedPluginClient.MockedProcessResultFunc = s_processResultResponseThrowException;

            var args = GetMockPluginCreationArguments((options) => m_mockedPluginClient);
            var res = await m_pluginManager.GetOrCreateAsync(args);
            var processResultMessageResponse = await m_pluginManager.ProcessResultAsync("", "", null, null, null, 0, "");

            Assert.False(processResultMessageResponse.Succeeded);
        }

#if NET6_0_OR_GREATER
        [Fact]
        public async Task LoadNonMockedProcessResultPluginShouldSucceedAsync()
        {
            var args = GetPluginCreationArguments((options) =>
            {
                return new PluginClient(IPAddress.Loopback.ToString(), m_port, m_logger);
            });

            var res = await m_pluginManager.GetOrCreateAsync(args);
            XAssert.PossiblySucceeded(res);
            Assert.Equal(1, m_pluginManager.PluginLoadedSuccessfulCount);
            Assert.Equal(1, m_pluginManager.PluginsCount);
            Assert.True(m_pluginManager.CanHandleMessage(PluginMessageType.ProcessResult));

            int processExitCode = 1234;
            int retryExitCode = 1111;
            var processOutput = new ProcessStream
            {
                Content = "RETRY",
            };

            var processResultMessageResponse = await m_pluginManager.ProcessResultAsync("", "", null, processOutput, null, processExitCode, "");
            Assert.True(processResultMessageResponse.Succeeded);
            Assert.Equal(retryExitCode, processResultMessageResponse.Result.ExitCode);
        }

        [Fact]
        public async Task LoadNonMockedProcessResultPluginShouldFailAsync()
        {
            var args = GetPluginCreationArguments((options) =>
            {
                return new PluginClient(IPAddress.Loopback.ToString(), m_port, m_logger);
            });

            var res = await m_pluginManager.GetOrCreateAsync(args);
            XAssert.PossiblySucceeded(res);
            Assert.Equal(1, m_pluginManager.PluginLoadedSuccessfulCount);
            Assert.Equal(1, m_pluginManager.PluginsCount);
            Assert.True(m_pluginManager.CanHandleMessage(PluginMessageType.ProcessResult));

            int processExitCode = 1234;
            var processOutput = new ProcessStream
            {
                Content = "Don't retry",
            };

            var processResultMessageResponse = await m_pluginManager.ProcessResultAsync("", "", null, processOutput, null, processExitCode, "");
            Assert.True(processResultMessageResponse.Succeeded);
            Assert.Equal(processExitCode, processResultMessageResponse.Result.ExitCode);
        }
#endif
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
