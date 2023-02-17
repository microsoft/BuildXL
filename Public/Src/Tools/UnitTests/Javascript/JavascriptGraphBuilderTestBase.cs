// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.IO;
using BuildXL.FrontEnd.Utilities;
using BuildXL.Utilities.Core;
using Newtonsoft.Json;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit.Abstractions;

namespace Test.Tool.Javascript
{
    public class JavascriptGraphBuilderTestBase : TemporaryStorageTestBase
    {
        public PathTable PathTable;
        public string UserProfile => Path.Combine(TestDeploymentDir, "userprofile");
        public string NodeTool => Path.Combine(TestDeploymentDir, "node", OperatingSystemHelper.IsWindowsOS ? "node.exe" : "node");
        public string YarnTool => Path.Combine(TestDeploymentDir, "yarn", "bin", OperatingSystemHelper.IsWindowsOS ? "yarn.cmd" : "yarn");

        public JavascriptGraphBuilderTestBase(ITestOutputHelper output) : base(output)
        {
            Directory.CreateDirectory(UserProfile);
            PathTable = new PathTable();
        }

        /// <summary>
        /// Runs the GraphBuilderTool using the provided args.
        /// </summary>
        /// <param name="workingDirectory">Root directory of the js project.</param>
        /// <param name="args">Arguments to pass to Node.</param>
        /// <returns>True if the GraphBuilder tool returns a successful exit code.</returns>
        internal bool RunGraphBuilderTool(string workingDirectory, string args)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = NodeTool,
                Arguments = args,
                WorkingDirectory = workingDirectory,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };

            // Low level tools like sed and readlink needs to be in the path for the linux/mac case
            var path = $"{Path.PathSeparator}{Path.GetDirectoryName(NodeTool)}";
            if (!OperatingSystemHelper.IsWindowsOS)
            {
                path += Path.PathSeparator + "/usr/bin/";
            }

            startInfo.Environment["PATH"] += path;
            startInfo.Environment[OperatingSystemHelper.IsWindowsOS ? "USERPROFILE" : "HOME"] = UserProfile;

            var graphBuilderResult = Process.Start(startInfo);
            graphBuilderResult.WaitForExit();

            return graphBuilderResult.ExitCode == 0;
        }

        /// <summary>
        /// Creates a deserializer that can handle AbsolutePaths
        /// </summary>
        internal JsonSerializer GetJavascriptGraphSerializer()
        {
            var serializer = JsonSerializer.Create(new JsonSerializerSettings());
            serializer.Converters.Add(new AbsolutePathJsonConverter(PathTable));

            return serializer;
        }
    }
}