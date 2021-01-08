// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using ContentStoreTest.Test;
using BuildXL.Cache.ContentStore.FileSystem;
using Xunit.Abstractions;
using FluentAssertions;
using System;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using System.Threading;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using System.IO;
using BuildXL.Native.IO;
using ContentStoreTest.Extensions;

namespace BuildXL.Cache.ContentStore.App.Test
{
    public class AppTests : TestBase
    {
        private static readonly string AppExe = Path.Combine("app", $"ContentStoreApp{(OperatingSystemHelper.IsWindowsOS ? ".exe" : "")}");
        private static readonly Random Random = new Random();


        public AppTests(ILogger logger = null, ITestOutputHelper output = null)
            : base(logger ?? TestGlobal.Logger, output)
        {
        }

        [Fact]
        public async Task PutFileThenPlaceTestAsync()
        {
            using var fileSystem = new PassThroughFileSystem(Logger);
            using var dir = new DisposableDirectory(fileSystem);
            var cacheDir = dir.Path / "cache";
            var file = dir.Path / "theFile.txt";

            fileSystem.WriteAllText(file, "Foo");

            var args = new Dictionary<string, string>
            {
                ["cachePath"] = cacheDir.Path,
                ["path"] = file.Path,
                ["hashType"] = "MD5",
                ["LogSeverity"] = "Diagnostic",
            };

            var hash = "1356C67D7AD1638D816BFB822DD2C25D";

            await RunAppAsync("PutFile", args, Logger);

            var destination = dir.Path / "destination.txt";

            args["hash"] = hash;
            args["path"] = destination.Path;

            await RunAppAsync("PlaceFile", args, Logger);

            fileSystem.ReadAllText(destination).Should().Be("Foo");
        }

        [Fact]
        public async Task ServiceTestAsync()
        {
            using var fileSystem = new PassThroughFileSystem(Logger);
            using var dir = new DisposableDirectory(fileSystem);
            var cacheDir = dir.Path / "cache";
            var dataPath = dir.Path / "data";

            var port = PortExtensions.GetNextAvailablePort();

            var args = new Dictionary<string, string>
            {
                ["paths"] = cacheDir.Path,
                ["names"] = "Default",
                ["grpcPort"] = port.ToString(),
                ["LogSeverity"] = "Diagnostic",
                ["dataRootPath"] = dataPath.Path,
                ["Scenario"] = "AppTests",
                ["grpcPortFileName"] = "AppTestsMMF"
            };

            var serviceProcess = RunService("Service", args, Logger);

            try
            {
                await RunAppAsync("ServiceRunning", new Dictionary<string, string> { { "waitSeconds", "5" }, { "Scenario", "AppTests" } }, Logger);

                var context = new Context(Logger);

                var config = new ServiceClientContentStoreConfiguration("Default", new ServiceClientRpcConfiguration { GrpcPort = port }, scenario: "AppTests");
                using var store = new ServiceClientContentStore(Logger, fileSystem, config);
                await store.StartupAsync(context).ShouldBeSuccess();

                var sessionResult = store.CreateSession(context, "Default", ImplicitPin.None).ShouldBeSuccess();
                using var session = sessionResult.Session;
                await session.StartupAsync(context).ShouldBeSuccess();

                var source = dir.Path / "source.txt";
                var contents = new byte[1024];
                Random.NextBytes(contents);
                fileSystem.WriteAllBytes(source, contents);

                var putResult = await session.PutFileAsync(context, HashType.MD5, source, FileRealizationMode.Any, CancellationToken.None).ShouldBeSuccess();
                var hash = putResult.ContentHash;

                await session.PinAsync(context, hash, CancellationToken.None).ShouldBeSuccess();

                var destination = dir.Path / "destination.txt";
                await session.PlaceFileAsync(
                    context,
                    hash,
                    destination,
                    FileAccessMode.ReadOnly,
                    FileReplacementMode.FailIfExists,
                    FileRealizationMode.Any,
                    CancellationToken.None).ShouldBeSuccess();

                fileSystem.ReadAllBytes(destination).Should().BeEquivalentTo(contents);
            }
            finally
            {
                if (!serviceProcess.HasExited)
                {
                    serviceProcess.Kill();
#pragma warning disable AsyncFixer02 // WaitForExitAsync should be used instead
                    serviceProcess.WaitForExit();
#pragma warning restore AsyncFixer02
                }
            }
        }

        public static async Task RunAppAsync(string verb, Dictionary<string, string> args, ILogger logger)
        {
            FileUtilities.TrySetExecutePermissionIfNeeded(Path.Combine(Environment.CurrentDirectory, AppExe));
            var info = new ProcessStartInfo
            {
                FileName = AppExe,
                Arguments = $"{verb} {string.Join(" ", args.Select(kvp => $" /{ kvp.Key}:{ kvp.Value}"))}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            var process = new Process
            {
                StartInfo = info
            };

            process.OutputDataReceived += (sender, data) => logger.Info(data.Data);
            process.ErrorDataReceived += (sender, data) => logger.Error(data.Data);

            logger.Info($"Running {process.StartInfo.FileName} {process.StartInfo.Arguments}");

            process.Start();
            process.BeginErrorReadLine();
            process.BeginOutputReadLine();

            var result = await Task.Run(() =>
            {
#pragma warning disable AsyncFixer02 // WaitForExitAsync should be used instead
                process.WaitForExit();
#pragma warning restore AsyncFixer02
                return process.ExitCode;
            });

            result.Should().Be(0);
        }

        public static Process RunService(string verb, Dictionary<string, string> args, ILogger logger)
        {
            FileUtilities.TrySetExecutePermissionIfNeeded(Path.Combine(Environment.CurrentDirectory, AppExe));

            var info = new ProcessStartInfo
            {
                FileName = AppExe,
                Arguments = $"{verb} {string.Join(" ", args.Select(kvp => $" /{ kvp.Key}:{ kvp.Value}"))}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false
            };

            var process = new Process
            {
                StartInfo = info
            };

            process.OutputDataReceived += (sender, data) => logger.Info(data.Data);
            process.ErrorDataReceived += (sender, data) => logger.Error(data.Data);

            logger.Info($"Running {process.StartInfo.FileName} {process.StartInfo.Arguments}");

            process.Start();
            process.BeginErrorReadLine();
            process.BeginOutputReadLine();

            return process;
        }
    }
}
