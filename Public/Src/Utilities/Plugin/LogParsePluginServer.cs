// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Plugin.Grpc;
using Grpc.Core.Logging;

namespace BuildXL.Plugin
{
    /// <nodoc />
    public class LogParsePluginServer: PluginServiceServer
    {
        /// <nodoc />
        public override IList<SupportedOperationResponse.Types.SupportedOperation> SupportedOperations { get; } =  new[] 
        { 
            SupportedOperationResponse.Types.SupportedOperation.LogParse,
            SupportedOperationResponse.Types.SupportedOperation.ProcessResult,
        };

        /// <nodoc />
        public LogParsePluginServer(int port, ILogger logger): base(port, logger)
        {
        }

        /// <inheritdoc />
        protected override Task<PluginMessageResponse> HandleLogParse(LogParseMessage logParseMessage)
        {
            var logParseResult = ParseLog(logParseMessage);
            var logParseResponse = new LogParseMessageResponse() { LogType = logParseMessage.LogType, LogParseResult = logParseResult };
            return Task.FromResult<PluginMessageResponse>(new PluginMessageResponse() { Status = true, LogParseMessageResponse = logParseResponse });
        }

        private LogParseResult ParseLog(LogParseMessage logParseMessage)
        {
            string parsedMessage = "[plugin]:" + logParseMessage.Message;
            return new LogParseResult() { ParsedMessage = parsedMessage };
        }

        /// <inheritdoc />
        protected override Task<PluginMessageResponse> ProcessResult(ProcessResultMessage processResultMessage)
        {
            int retryExitCode = 1111;
            return Task.FromResult<PluginMessageResponse>(new PluginMessageResponse
            {
                Status = true,
                ProcessResultMessageResponse = new ProcessResultMessageResponse
                {
                    ExitCode = messagesToRetry.Contains(processResultMessage.StandardOut.Content) ? retryExitCode : processResultMessage.ExitCode,
                },
            });
        }

        private HashSet<string> messagesToRetry = new HashSet<string>
        {
            "RETRY",
        };
    }
}
