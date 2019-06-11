// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Pips.Operations;
using BuildXL.Processes;
using BuildXL.Utilities;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.Scheduler;
using Xunit.Abstractions;

namespace Test.BuildXL.Processes
{
    public class SandboxedProcessTestBase : PipTestBase, ISandboxedProcessFileStorage
    {
        public SandboxedProcessTestBase(ITestOutputHelper output) : base(output)
        {
        }

        /// <remarks>
        /// Sets <see cref="FileAccessManifest.FailUnexpectedFileAccesses"/> to false if not explicitly set
        /// </remarks>
        protected SandboxedProcessInfo ToProcessInfo(
            Process process,
            string pipDescription = null,
            FileAccessManifest fileAccessManifest = null,
            IDetoursEventListener detoursListener = null,
            bool disableConHostSharing = false,
            Dictionary<string, string> overrideEnvVars = null)
        {
            var envVars = Override(
                BuildParameters.GetFactory().PopulateFromEnvironment().ToDictionary(),
                overrideEnvVars);

            var methodName = DiscoverCurrentlyExecutingXunitTestMethodFQN();
            pipDescription = pipDescription != null
                ? methodName + " - " + pipDescription
                : methodName;

            var info = new SandboxedProcessInfo(
                Context.PathTable,
                this,
                process.Executable.Path.ToString(Context.PathTable),
                detoursEventListener: detoursListener,
                sandboxedKextConnection: GetSandboxedKextConnection(),
                disableConHostSharing: disableConHostSharing,
                fileAccessManifest: fileAccessManifest)
            {
                PipSemiStableHash = 0x1234,
                PipDescription = pipDescription,
                WorkingDirectory = TemporaryDirectory,
                Arguments = process.Arguments.ToString(Context.PathTable),
                Timeout = TimeSpan.FromMinutes(15),
                EnvironmentVariables = BuildParameters.GetFactory().PopulateFromDictionary(envVars)
            };

            if (fileAccessManifest == null)
            {
                info.FileAccessManifest.FailUnexpectedFileAccesses = false;
            }

            foreach (var path in process.UntrackedPaths)
                info.FileAccessManifest.AddPath(path, values: FileAccessPolicy.AllowAll, mask: FileAccessPolicy.MaskNothing);

            foreach (var dir in process.UntrackedScopes)
                info.FileAccessManifest.AddScope(dir, FileAccessPolicy.MaskNothing, FileAccessPolicy.AllowAll);

            info.FileAccessManifest.PipId = GetNextPipId();

            return info;
        }

        private static IReadOnlyDictionary<TK, TV> Override<TK, TV>(IReadOnlyDictionary<TK, TV> baseDict, Dictionary<TK, TV> overrideDict)
        {
            if (overrideDict == null || overrideDict.Count == 0)
            {
                return baseDict;
            }

            var result = new Dictionary<TK, TV>();
            foreach (var kvp in baseDict)
            {
                result[kvp.Key] = kvp.Value;
            }

            foreach (var kvp in overrideDict)
            {
                result[kvp.Key] = kvp.Value;
            }

            return result;
        }

        protected Process EchoProcess(string message = "Success", bool useStdErr = false)
            => ToProcess(new[] { Operation.Echo(message, useStdErr) });

        protected SandboxedProcessInfo EchoProcessInfo(string message = "Success", IDetoursEventListener detoursListener = null)
            => ToProcessInfo(EchoProcess(message), detoursListener: detoursListener);

        protected FileArtifact AbsentFile(AbsolutePath root, params string[] atoms)
        {
            return FileArtifact.CreateSourceFile(Combine(root, atoms));
        }

        protected static Task<ISandboxedProcess> StartProcessAsync(SandboxedProcessInfo info, bool forceSandboxing = true)
        {
            return SandboxedProcessFactory.StartAsync(info, forceSandboxing);
        }

        public string GetFileName(SandboxedProcessFile file)
        {
            return GetFullPath(file.ToString());
        }

        protected static async Task<SandboxedProcessResult> RunProcess(SandboxedProcessInfo info)
        {
            using (ISandboxedProcess process = await StartProcessAsync(info))
            {
                return await process.GetResultAsync();
            }
        }
    }
}
