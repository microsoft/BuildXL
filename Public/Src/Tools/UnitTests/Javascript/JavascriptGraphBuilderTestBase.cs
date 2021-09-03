// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.IO;
using BuildXL.FrontEnd.Utilities;
using BuildXL.Utilities;
using Newtonsoft.Json;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit.Abstractions;

namespace Test.Tool.Javascript
{
    public class JavascriptGraphBuilderTestBase : TemporaryStorageTestBase
    {
        public PathTable PathTable;
        public string UserProfile => Path.Combine(TestDeploymentDir, "userprofile");
        public string NodeTool => Path.Combine(TestDeploymentDir, "node", OperatingSystemHelper.IsUnixOS ? "node" : "node.exe");
        public string YarnTool => Path.Combine(TestDeploymentDir, "yarn", "bin", "yarn");

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

            startInfo.Environment["PATH"] += $";{Path.GetDirectoryName(NodeTool)}";
            startInfo.Environment["USERPROFILE"] = UserProfile;

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