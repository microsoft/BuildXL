// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using BuildXL.AdoBuildRunner;

namespace Test.Tool.AdoBuildRunner
{
    public class MockLauncher : IBuildXLLauncher
    {
        /// <nodoc />
        public MockLauncher(int returnCode = 0)
        {
            ReturnCode = returnCode;
        }

        /// <summary>
        /// Set this task from a test if you want the completion to be delayed or controlled by a task completion source, etc.
        /// </summary>
        public Task CompletionTask = Task.CompletedTask;

        /// <summary>
        /// Value to return for the mocked BuildXL invocation
        /// </summary>
        public int ReturnCode { get; set; }

        /// <nodoc />
        public bool Launched { get; private set; }

        /// <nodoc />
        public bool Exited { get; private set; }

        /// <nodoc />
        public string Arguments { get; private set; } = "";

        /// <inheritdoc />
        async Task<int> IBuildXLLauncher.LaunchAsync(string args, Action<string> outputDataReceived, Action<string> errorDataRecieved)
        {
            Launched = true;
            Arguments = args;
            await CompletionTask;
            Exited = true;
            return ReturnCode;
        }
    }
}
