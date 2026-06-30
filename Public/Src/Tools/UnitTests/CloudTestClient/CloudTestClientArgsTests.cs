// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tool.CloudTestClient;
using Xunit;

namespace Test.Tool.CloudTestClient
{
    public class CloudTestClientArgsTests
    {
        #region Argument parsing

        [Fact]
        public void ParseGenerateSessionConfigArgs()
        {
            var args = new CloudTestClientArgs(new[]
            {
                "/mode:generateSessionConfig",
                "/sessionInputFile:" + Path.Combine(Path.GetTempPath(), "session-input.json"),
                "/configOutputFile:" + Path.Combine(Path.GetTempPath(), "session-config.json"),
            });

            Assert.Equal(CloudTestMode.GenerateSessionConfig, args.Mode);
            Assert.Equal(Path.Combine(Path.GetTempPath(), "session-input.json"), args.SessionInputFile);
            Assert.Equal(Path.Combine(Path.GetTempPath(), "session-config.json"), args.ConfigOutputFile);
        }

        [Fact]
        public void ParseGenerateUpdateDynamicJobConfigArgs()
        {
            using var temp = new TempDirectory();
            string tempSessionIdFile = temp.WriteFile("session-id.txt", "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

            var args = new CloudTestClientArgs(new[]
            {
                "/mode:generateUpdateDynamicJobConfig",
                "/image:ubuntu22.04",
                "/sku:Standard_D4s_v3",
                "/sessionIdFile:" + tempSessionIdFile,
                "/jobId:11111111-2222-3333-4444-555555555555",
                "/testFolder:TestSuite_A",
                "/jobExecutable:" + temp.GetPath("run.sh"),
                "/testExecutionType:Exe",
                "/configOutputFile:" + temp.GetPath("update-config.json"),
            });

            Assert.Equal(CloudTestMode.GenerateUpdateDynamicJobConfig, args.Mode);
            Assert.Equal("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", args.SessionId);
            Assert.Equal("11111111-2222-3333-4444-555555555555", args.JobId);
            Assert.Equal("TestSuite_A", args.TestFolder);
            Assert.Equal("Exe", args.TestExecutionType);
        }

        [Fact]
        public void MissingModeThrows()
        {
            Assert.ThrowsAny<Exception>(() => new CloudTestClientArgs(new[] { "/tenant:foo" }));
        }

        [Fact]
        public void InvalidModeThrows()
        {
            Assert.ThrowsAny<Exception>(() => new CloudTestClientArgs(new[] { "/mode:nonexistent", "/tenant:foo" }));
        }

        [Fact]
        public void MissingMandatoryArgThrows()
        {
            // generateSessionConfig without configOutputFile
            Assert.ThrowsAny<Exception>(() => new CloudTestClientArgs(new[]
            {
                "/mode:generateSessionConfig",
                "/sessionInputFile:" + Path.Combine(Path.GetTempPath(), "session-input.json"),
            }));
        }

        [Fact]
        public void MissingGroupFileThrows()
        {
            // generateSessionConfig without the session input file
            Assert.ThrowsAny<Exception>(() => new CloudTestClientArgs(new[]
            {
                "/mode:generateSessionConfig",
                "/configOutputFile:" + Path.Combine(Path.GetTempPath(), "session-config.json"),
            }));
        }

        #endregion

        #region Path validation

        [Fact]
        public void ValidPathsDoNotThrow()
        {
            // Ensure well-formed paths pass validation without throwing
            var args = new CloudTestClientArgs(new[]
            {
                "/mode:generateSessionConfig",
                "/sessionInputFile:" + Path.Combine(Path.GetTempPath(), "session-input.json"),
                "/configOutputFile:" + Path.Combine(Path.GetTempPath(), "valid-path", "output.json"),
            });

            Assert.Equal(Path.Combine(Path.GetTempPath(), "valid-path", "output.json"), args.ConfigOutputFile);
        }

        #endregion
    }
}
