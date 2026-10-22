// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Pips.Operations;
using BuildXL.Plugin;
using BuildXL.Plugin.Grpc;
using BuildXL.ProcessPipExecutor;
using BuildXL.Processes;
using BuildXL.Utilities.Core;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Adapts scheduler plugins to the process executor's plugin-neutral contract.
    /// </summary>
    internal sealed class ProcessPipExecutionPlugin : IProcessPipExecutionPlugin
    {
        private readonly PluginManager m_pluginManager;
        private readonly Process m_pip;
        private readonly PathTable m_pathTable;
        private SandboxedProcessInfo m_processInfo;

        public IReadOnlyCollection<string> LoadedPluginSupportedMessageTypes =>
            m_pluginManager.GetSupportedMessageTypesOfLoadedPlugins().Select(messageType => messageType.ToString()).ToArray();

        public ProcessPipExecutionPlugin(PluginManager pluginManager, Process pip, PathTable pathTable)
        {
            m_pluginManager = pluginManager;
            m_pip = pip;
            m_pathTable = pathTable;
        }

        public void SetProcessInfo(SandboxedProcessInfo processInfo)
        {
            m_processInfo = processInfo;
        }

        public async Task<string> ProcessStdOutAndErrorAsync(string message, bool isErrorOutput)
        {
            if (m_processInfo == null)
            {
                return message;
            }

            var parsedResult = await m_pluginManager.LogParseAsync(m_processInfo.FileName, message, isErrorOutput);
            return parsedResult.Succeeded ? parsedResult.Result.ParsedMessage : message;
        }

        public async Task<ProcessPipExecutionPluginResult> ProcessResultAsync(SandboxedProcessResult result)
        {
            if (m_processInfo == null || result.StandardOutput.HasException || result.StandardError.HasException)
            {
                return null;
            }

            StandardInputInfo standardInput = m_processInfo.StandardInputSourceInfo
                ?? StandardInputInfoExtensions.CreateForProcess(m_pip, m_pathTable);

            ProcessStream inputContent = null;
            if (standardInput != null)
            {
                inputContent = new ProcessStream();
                if (standardInput.Data != null)
                {
                    inputContent.Content = standardInput.Data;
                }
                else if (standardInput.File != null)
                {
                    inputContent.FilePath = standardInput.File;
                }
            }

            var processedResult = await m_pluginManager.ProcessResultAsync(
                m_processInfo.FileName,
                m_processInfo.Arguments,
                inputContent,
                await GetProcessStreamFromOutputAsync(result.StandardOutput),
                await GetProcessStreamFromOutputAsync(result.StandardError),
                result.ExitCode,
                m_pip.FormattedSemiStableHash);

            if (!processedResult.Succeeded)
            {
                return null;
            }

            return new ProcessPipExecutionPluginResult
            {
                StandardOutToAppend = processedResult.Result.StandardOutToAppend,
                StandardErrToAppend = processedResult.Result.StandardErrToAppend,
                ExitCode = processedResult.Result.HasExitCode ? processedResult.Result.ExitCode : null,
            };
        }

        private static async Task<ProcessStream> GetProcessStreamFromOutputAsync(SandboxedProcessOutput output)
        {
            return output.IsSaved
                ? new ProcessStream { FilePath = output.FileName }
                : new ProcessStream { Content = await output.ReadValueAsync() };
        }
    }
}
