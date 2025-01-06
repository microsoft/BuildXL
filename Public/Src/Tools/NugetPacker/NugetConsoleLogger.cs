// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using NuGet.Common;

namespace Tool.Nuget.Packer
{
    internal class NugetConsoleLogger : ILogger
    {
        public enum Verbosity
        {
            Normal,
            Quiet,
            Detailed
        }

        public void Log(LogLevel level, string data)
        {
            Console.WriteLine($"{level}: {data}");
        }

        public void Log(ILogMessage message)
        {
            Console.WriteLine($"{message.Level}: {message.Message}");
        }

        public Task LogAsync(LogLevel level, string data)
        {
            Log(level, data);
            return Task.CompletedTask;
        }

        public Task LogAsync(ILogMessage message)
        {
            Log(message);
            return Task.CompletedTask;
        }

        public void LogDebug(string data)
        {
            Console.WriteLine($"[DEBUG]: {data}");
        }

        public void LogError(string data)
        {
            Console.WriteLine($"[ERROR]: {data}");
        }

        public void LogInformation(string data)
        {
            Console.WriteLine($"[INFORMATION]: {data}");
        }

        public void LogInformationSummary(string data)
        {
            Console.WriteLine($"[INFORMATION SUMMARY]: {data}");
        }

        public void LogMinimal(string data)
        {
            Console.WriteLine($"{data}");
        }

        public void LogVerbose(string data)
        {
            Console.WriteLine($"[VERBOSE]: {data}");
        }

        public void LogWarning(string data)
        {
            Console.WriteLine($"[WARNING]: {data}");
        }
    }
}