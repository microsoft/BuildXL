// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Plugin;
using BuildXL.Plugin.Grpc;
using BuildXL.Utilities;

namespace BuildXL.Processes
{
    /// <summary>
    /// Passes calls from SandboxedProcessPipExecutor to PluginManager
    /// </summary>
    public class PluginEndpoints
    {
        private readonly PluginManager m_pluginManager;

        /// <summary>
        /// Additional information about the process such as executable or arguments
        /// </summary>
        public SandboxedProcessInfo ProcessInfo { get; set; }

        /// <summary>
        /// Creates a wrapper to pass relevant information to PluginManager
        /// </summary>
        public PluginEndpoints(PluginManager pluginManager)
        {
            m_pluginManager = pluginManager;
        }

        /// <summary>
        /// Call PluginManager.LogParseAsync
        /// </summary>
        public async Task<string> ProcessStdOutAndErrorAsync(string message, bool isErrorOutput)
        {
            if (m_pluginManager != null)
            {
                var parsedResult = await m_pluginManager.LogParseAsync(message, isErrorOutput);
                return parsedResult.Succeeded ? parsedResult.Result.ParsedMessage : message;
            }

            return message;
        }

        /// <summary>
        /// Call PluginManager.ProcessResultAsync
        /// </summary>
        public async Task<SandboxedProcessResult> ProcessResultAsync(SandboxedProcessResult result)
        {
            if (m_pluginManager == null || ProcessInfo == null || result.StandardOutput.HasException || result.StandardError.HasException)
            {
                return result;
            }

            ProcessStream inputContent = new ProcessStream
            {
                Content = ProcessInfo.StandardInputSourceInfo.Data,
                FilePath = ProcessInfo.StandardInputSourceInfo.File,
            };
            ProcessStream outputContent = await GetPluginInput(result.StandardOutput);
            ProcessStream errorContent = await GetPluginInput(result.StandardError);

            var processedResult = await m_pluginManager.ProcessResultAsync(ProcessInfo.FileName, ProcessInfo.Arguments, inputContent, outputContent, errorContent, result.ExitCode);

            if (processedResult.Result.StandardOutToAppend != null)
            {
                result.StandardOutput = await AppendToContent(result.StandardOutput, processedResult.Result.StandardOutToAppend);
            }

            if (processedResult.Result.StandardErrToAppend != null)
            {
                result.StandardError = await AppendToContent(result.StandardError, processedResult.Result.StandardErrToAppend);
            }

            if (processedResult.Result.HasExitCode)
            {
                result.ExitCode = processedResult.Result.ExitCode;
            }

            return result;
        }

        private async Task<ProcessStream> GetPluginInput(SandboxedProcessOutput output)
        {
            return new ProcessStream
            {
                Content = output.IsSaved ? null : await output.ReadValueAsync(),
                FilePath = output.IsSaved ? output.FileName : null,
            };
        }

        private async Task<SandboxedProcessOutput> AppendToContent(SandboxedProcessOutput output, string content)
        {
            if (!output.HasException && !string.IsNullOrEmpty(content))
            {
                int maxMemoryLength = 16384; // Taken from default value in SandboxedProcessOutputInfo
                using (SandboxedProcessOutputBuilder builder = new SandboxedProcessOutputBuilder(output.Encoding,
                                                                                                maxMemoryLength,
                                                                                                output.FileStorage,
                                                                                                output.File,
                                                                                                null))
                {
                    using (TextReader reader = output.CreateReader())
                    {
                        // Rewrite the content of the output with a new SandboxedProcessOutputBuilder
                        string chunk = null;
                        while ((chunk = await reader.ReadLineAsync()) != null)
                        {
                            builder.AppendLine(chunk);
                        }

                        // Append the new content from the plugin
                        builder.AppendLine(content);

                        return builder.Freeze();
                    }
                }
            }

            return output;
        }
    }
}