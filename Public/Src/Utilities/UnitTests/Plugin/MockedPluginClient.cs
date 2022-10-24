using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Plugin;
using BuildXL.Plugin.Grpc;

namespace Test.BuildXL.Plugin
{
    internal class MockedPluginClient : IPluginClient, IDisposable
    {
        public Func<Task<PluginResponseResult<List<PluginMessageType>>>> MockedSupportedMessageTypeFunc;
        public Func<Task<PluginResponseResult<bool>>> MockedStartFunc;
        public Func<Task<PluginResponseResult<bool>>> MockedStopFunc;
        public Func<Task<PluginResponseResult<LogParseResult>>> MockedLogParseFunc;
        public Func<Task<PluginResponseResult<ExitCodeParseResult>>> MockedHandleExitCodeFunc;

        public MockedPluginClient(Func<Task<PluginResponseResult<bool>>> startFunc,
                                  Func<Task<PluginResponseResult<bool>>> stopFunc,
                                  Func<Task<PluginResponseResult<List<PluginMessageType>>>> supportedMessageTyepFunc,
                                  Func<Task<PluginResponseResult<LogParseResult>>> logparseFunc,
                                  Func<Task<PluginResponseResult<ExitCodeParseResult>>> exitcodeFunc)
        {
            MockedSupportedMessageTypeFunc = supportedMessageTyepFunc;
            MockedStartFunc = startFunc;
            MockedStopFunc = stopFunc;
            MockedLogParseFunc = logparseFunc;
            MockedHandleExitCodeFunc = exitcodeFunc;
        }

        public void Dispose() 
        { 
        }

        public Task<PluginResponseResult<List<PluginMessageType>>> GetSupportedPluginMessageType()
        {
            return MockedSupportedMessageTypeFunc.Invoke();
        }

        public Task<PluginResponseResult<LogParseResult>> ParseLogAsync(string message, bool isErrorStdOutput)
        {
            return MockedLogParseFunc.Invoke();
        }

        public Task<PluginResponseResult<ExitCodeParseResult>> HandleExitCodeAsync(string content, string filePath, bool isError)
        {
            return MockedHandleExitCodeFunc.Invoke();
        }

        public Task<PluginResponseResult<bool>> StartAsync()
        {
            return MockedStartFunc.Invoke();
        }

        public Task<PluginResponseResult<bool>> StopAsync()
        {
            return MockedStopFunc.Invoke();
        }
    }
}
