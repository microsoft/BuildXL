// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading.Tasks;
using BuildXL.Ide.LanguageServer.Providers;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace BuildXL.Ide.LanguageServer
{
    /// <summary>
    /// A class to contain helpers for all server-initiated RPCs.
    /// </summary>
    public static class RpcExtensions
    {
        /// <summary>
        /// Sends the textDocument/publishDiagnostics notification to the LSP client.
        /// </summary>
        /// <returns>A task to send the textDocument/publishDiagnostics message.</returns>
        public static Task PublishDiagnosticsAsync(this ProviderContext providerContext, PublishDiagnosticParams @params)
        {
            return Dispatch(providerContext, "textDocument/publishDiagnostics", @params);
        }

        /// <summary>
        /// Sends the window/showMessage notification to the LSP client.
        /// </summary>
        /// <returns>The task to send the window/showMessage notification.</returns>
        public static Task ShowMessageAsync(this StreamJsonRpc.JsonRpc jsonRpc, TestContext? testContext, MessageType messageType, string message)
        {
            return Dispatch(jsonRpc, testContext, "window/showMessage", 
                new ShowMessageParams()
                {
                    MessageType = messageType,
                    Message = message,
                });
        }

        /// <summary>
        /// Sends the window/showMessage notification to the LSP client.
        /// </summary>
        /// <returns>The task to send the window/showMessage notification.</returns>
        public static Task ShowMessageAsync(this ProviderContext providerContext, MessageType messageType, string message)
        {
            return Dispatch(providerContext, "window/showMessage",
                new ShowMessageParams()
                {
                    MessageType = messageType,
                    Message = message,
                });
        }

        /// <summary>
        /// Sends the window/showMessageRequest request to the LSP client.
        /// </summary>
        /// <returns>A task to send the window/showMessageRequest request.</returns>
        public static Task<MessageActionItem> ShowMessageRequestAsync(this ProviderContext providerContext, MessageType messageType, string message, params MessageActionItem[] actionItems)
        {
            return ShowMessageRequestAsync(providerContext.JsonRpc, messageType, message, actionItems);
        }

        /// <summary>
        /// Sends the window/showMessageRequest request to the LSP client.
        /// </summary>
        /// <returns>A task to send the window/showMessageRequest request.</returns>
        public static Task<MessageActionItem> ShowMessageRequestAsync(this StreamJsonRpc.JsonRpc jsonRpc, MessageType messageType, string message, params MessageActionItem[] actionItems)
        {
            return jsonRpc.InvokeWithParameterObjectAsync<MessageActionItem>(
                "window/showMessageRequest",
                new ShowMessageRequestParams
                {
                    MessageType = messageType,
                    Message = message,
                    Actions = actionItems,
                });
        }

        /// <summary>
        /// Sends the window/logMessage notification to the LSP client.
        /// </summary>
        /// <returns>A task to send the window/logMessage notification.</returns>
        public static Task LogMessageAsync(this ProviderContext providerContext, MessageType messageType, string message)
        {
            return Dispatch(providerContext, "window/logMessage",
                new LogMessageParams()
                {
                    MessageType = messageType,
                    Message = message,
                });
        }

        private static Task Dispatch<T>(ProviderContext providerContext, string message, T param)
        {
            return Dispatch(providerContext.JsonRpc, providerContext.TestContext, message, param);
        }

        private static Task Dispatch<T>(StreamJsonRpc.JsonRpc jsonRpc, TestContext? testContext, string message, T param)
        {
            if (testContext?.ForceSynchronousMessages == true)
            {
                // If we need to force messages to be synchronous, then we always
                // do 'invoke' instead of 'notify', and we always wait for the task
                // to finish
                jsonRpc.InvokeWithParameterObjectAsync<object>(message, param).GetAwaiter().GetResult();
                
                // Return a 'do nothing' task in this case
                return Task.FromResult(0);
            }
                
            return jsonRpc.NotifyWithParameterObjectAsync(message, param);
        }
    }
}
