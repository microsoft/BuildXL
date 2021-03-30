// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using BuildXL.Cache.Host.Configuration;
using Xunit;
using ContentStoreTest.Test;
using BuildXL.Cache.Host.Service;
using Xunit.Abstractions;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using FluentAssertions;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Utilities.Tasks;
using System.Text.Json;
using System.Runtime.Serialization;

namespace BuildXL.Cache.ContentStore.Distributed.Test
{
    public class DeploymentLauncherTests : TestBase
    {
        public const string ConfigurationPathEnvironmentVariableName = "ConfigurationPath";

        private static readonly string ConfigString = @"
{
    'Drops': [
        {
            'Url [Ring:Ring_0]': 'https://dev.azure.com/buildxlcachetest/drop/drops/dev/testdrop1?root=release/win-x64',
            'Url [Ring:Ring_1]': 'https://dev.azure.com/buildxlcachetest/drop/drops/dev/testdrop2?root=debug',
            'Url [Ring:Ring_2]': 'https://dev.azure.com/buildxlcachetest/drop/drops/dev/testdrop1?root=release/win-x64',
            'TargetRelativePath': 'bin'
        },
        {
            'Url': 'file://Env',
        },
        {
            'TargetRelativePath': 'info',
            'Url [Stamp:ST_S1]': 'file://Files/Foo.txt',
            'Url [Stamp:ST_S2]': 'file://Env/Foo.txt',
            'Url [Stamp:ST_S3]': 'file://Stamp3',
        }
    ],
    'Proxy': {
        'Seeds': 3,
        'ServiceConfiguration': {
            // NOTE: This should match the port used for grpc as asp.net server routes requests to grpc
            'Port': 8105,
            'RootPath': 'D:/cachedir/proxy',
            'ProxyAddressTimeToLive': '5m',
            'RetentionSizeGb': 5,
            'DeploymentServiceUrl': 'https://buildxlcachetestdep.azurewebsites.net'
        },
        'TargetRelativePath': 'config/ProxyConfiguration.json'
    },
    'AzureStorageSecretInfo': { 'Name': 'myregionalStorage{Region:LA}', 'TimeToLive':'60m' },
    'SasUrlTimeToLive': '3m',
    'Tool [Environment:MyEnvRunningOnWindows]': {
        'Executable': 'bin/service.exe',
        'Arguments': 'myargs',
        'EnvironmentVariables': {
            'ConfigPath': '../Foo.txt'
        }
    }
}
".Replace("'", "\"");

        public DeploymentLauncherTests(ITestOutputHelper output = null)
            : base(TestGlobal.Logger, output)
        {
        }

        [Fact]
        public async Task TestDeployAndRun()
        {
            var host = new TestHost();

            string serviceUrl = "casaas://service";

            var settings = new LauncherSettings()
            {
                ServiceUrl = serviceUrl,
                RetentionSizeGb = 1,
                RunInBackgroundOnStartup = false,
                DeploymentParameters = new DeploymentParameters()
                {

                },
                ServiceLifetimePollingIntervalSeconds = 0.01,
                DownloadConcurrency = 1,
                TargetDirectory = TestRootDirectoryPath.Path
            };

            var executableRelativePath = @"bin\casaas.exe";
            var serviceId = "testcasaas";
            var firstRunExecutableContent = "This is the content of casaas.exe for run 1";

            var watchedConfigPath = "config/toolconfig.json";

            var toolConfiguraiton = new ServiceLaunchConfiguration()
            {
                ServiceId = serviceId,
                WatchedFiles = new[]
                    {
                        // Changing path case and separator to verify path is normalized
                        // for categorizing watched files
                        watchedConfigPath.Replace('/', '\\').ToUpper()
                    },
                Arguments = new[]
                    {
                        "arg1",
                        "arg2",
                        "arg3 with spaces"
                    },
                EnvironmentVariables =
                    {
                        { "hello", "world" },
                        { "foo", "bar" },
                        { ConfigurationPathEnvironmentVariableName, $"%ServiceDir%/{watchedConfigPath}" }
                    },
                Executable = @"bin\casaas.exe",
                ShutdownTimeoutSeconds = 60,
            };

            var configContent1 = "Config content 1";
            var manifest = new LauncherManifestWithExtraMembers()
            {
                IsComplete = true,
                ContentId = "Deployment 1",
                Deployment = new DeploymentManifest.LayoutSpec()
                {
                    { executableRelativePath, host.TestClient.AddContent(firstRunExecutableContent) },
                    { @"bin\lib.dll", host.TestClient.AddContent("This is the content of lib.dll") },
                    { watchedConfigPath, host.TestClient.AddContent(configContent1) },
                },
                Tool = new ServiceLaunchConfiguration()
                {
                    ServiceId = serviceId,
                    WatchedFiles = new[]
                    {
                        // Changing path case and separator to verify path is normalized
                        // for categorizing watched files
                        watchedConfigPath.Replace('/', '\\').ToUpper()
                    },
                    Arguments = new[]
                    {
                        "arg1",
                        "arg2",
                        "arg3 with spaces"
                    },
                    EnvironmentVariables =
                    {
                        { "hello", "world" },
                        { "foo", "bar" },
                        { ConfigurationPathEnvironmentVariableName, $"%ServiceDir%/{watchedConfigPath}" }
                    },
                    Executable = @"bin\casaas.exe",
                    ShutdownTimeoutSeconds = 60,
                }
            };

            host.TestClient.GetManifest = launcherSettings =>
            {
                //  Use JSON serialization and deserialization to clone manifest
                // Also tests JSON roundtripping
                var manifestText = JsonSerializer.Serialize(manifest);
                return JsonSerializer.Deserialize<LauncherManifest>(manifestText);
            };

            var launcher = new DeploymentLauncher(settings,
                FileSystem,
                host);

            using var cts = new CancellationTokenSource();
            var context = new OperationContext(new Context(Logger), cts.Token);

            await launcher.StartupAsync(context).ThrowIfFailureAsync();

            await launcher.GetDownloadAndRunDeployment(context).ShouldBeSuccess();

            // Test the process is launched.
            (launcher.CurrentRun?.RunningProcess).Should().NotBeNull();
            launcher.CurrentRun.IsActive.Should().BeTrue();
            var testProcess1 = (TestProcess)launcher.CurrentRun.RunningProcess;
            testProcess1.IsRunningService.Should().BeTrue();

            // Verify executable, arguments, enviroment variables
            ReadAllText(testProcess1.StartInfo.FileName).Should().Be(firstRunExecutableContent);
            testProcess1.StartInfo.Arguments.Should().Be("arg1 arg2 \"arg3 with spaces\"");
            testProcess1.StartInfo.Environment["hello"].Should().Be("world");
            testProcess1.StartInfo.Environment["foo"].Should().Be("bar");

            // Verify that same manifest does not launch new process
            await launcher.GetDownloadAndRunDeployment(context).ShouldBeSuccess();
            (launcher.CurrentRun?.RunningProcess).Should().Be(testProcess1);
            testProcess1.IsRunningService.Should().BeTrue();

            // Modify manifest and mark as incomplete to signify case where all files have not yet been
            // replicated to region-specific storage
            manifest.IsComplete = false;
            manifest.ContentId = "Deployment 2";
            var secondRunExecutableContent = "This is the content of casaas.exe for run 2";
            manifest.Deployment[executableRelativePath] = host.TestClient.AddContent(secondRunExecutableContent);

            // Verify that incomplete updated manifest does not launch new process
            await launcher.GetDownloadAndRunDeployment(context).ShouldBeSuccess();
            ReadAllText(testProcess1.StartInfo.FileName).Should().Be(firstRunExecutableContent);
            (launcher.CurrentRun?.RunningProcess).Should().Be(testProcess1);
            testProcess1.IsRunningService.Should().BeTrue();

            // Verify that complete updated manifest launches new process
            manifest.IsComplete = true;
            await launcher.GetDownloadAndRunDeployment(context).ShouldBeSuccess();
            (launcher.CurrentRun?.RunningProcess).Should().NotBe(testProcess1);
            testProcess1.IsRunningService.Should().BeFalse();
            var testProcess2 = (TestProcess)launcher.CurrentRun.RunningProcess;
            testProcess2.IsRunningService.Should().BeTrue();

            // Verify updated casaas.exe file
            ReadAllText(testProcess2.StartInfo.FileName).Should().Be(secondRunExecutableContent);

            // Verify shutdown launches new processes
            await launcher.LifetimeManager.ShutdownServiceAsync(context, serviceId);

            await launcher.ShutdownAsync(context).ThrowIfFailureAsync();
        }

        private string ReadAllText(string path)
        {
            return File.ReadAllText(path);
        }

        public class LauncherManifestWithExtraMembers : LauncherManifest
        {
            public string ExtraMember { get; set; } = "This should be ignored";
        }

        public class TestHost : IDeploymentLauncherHost
        {
            public TestProcess Process { get; set; }
            public TestClient TestClient { get; } = new TestClient();

            public ILauncherProcess CreateProcess(ProcessStartInfo info)
            {
                Process = new TestProcess(info);
                return Process;
            }

            public IDeploymentServiceClient CreateServiceClient()
            {
                return TestClient;
            }
        }

        public class TestProcess : ILauncherProcess
        {
            public const int GracefulShutdownExitCode = 0;
            public const int KilledExitCode = -1;

            public TaskCompletionSource<int> ExitSignal = new TaskCompletionSource<int>();

            public ProcessStartInfo StartInfo { get; }

            public TestProcess(ProcessStartInfo startInfo)
            {
                StartInfo = startInfo;
            }

            public int ExitCode { get; set; }

            public int Id { get; } = 12;

            public bool HasExited { get; set; }
            public bool HasStarted { get; set; }
            public bool IsRunningService { get; set; }

            public event Action Exited;

            public void Exit(int exitCode)
            {
                if (!HasExited)
                {
                    IsRunningService = false;
                    ExitCode = exitCode;
                    HasExited = true;
                    Exited?.Invoke();

                    ExitSignal.TrySetResult(exitCode);
                }
            }

            public void Kill(OperationContext context)
            {
                Exit(KilledExitCode);
            }

            public void Start(OperationContext context)
            {
                HasStarted = true;

                runAsync().Forget();

                async Task runAsync()
                {
                    int exitCode = await ServiceLifetimeManager.RunDeployedInterruptableServiceAsync(context, async token =>
                    {
                        try
                        {
                            IsRunningService = true;
                            using var reg = token.Register(() =>
                            {
                                ExitSignal.SetResult(GracefulShutdownExitCode);
                            });
                            return await ExitSignal.Task;
                        }
                        finally
                        {
                            IsRunningService = false;
                        }
                    },
                    getEnvironmentVariable: name => StartInfo.EnvironmentVariables[name]);

                    Exit(exitCode);
                }
            }
        }

        public class TestClient : IDeploymentServiceClient
        {
            public Func<LauncherSettings, LauncherManifest> GetManifest { get; set; }

            public Dictionary<string, byte[]> Content { get; } = new Dictionary<string, byte[]>();

            public void Dispose()
            {
            }

            public Task<LauncherManifest> GetLaunchManifestAsync(OperationContext context, LauncherSettings settings)
            {
                return Task.FromResult(GetManifest(settings));
            }

            public Task<Stream> GetStreamAsync(OperationContext context, string downloadUrl)
            {
                return Task.FromResult<Stream>(new MemoryStream(Content[downloadUrl]));
            }

            public DeploymentManifest.FileSpec AddContent(string content)
            {
                var bytes = Encoding.UTF8.GetBytes(content);

                var hash = HashInfoLookup.GetContentHasher(HashType.MD5).GetContentHash(bytes).ToString();
                var downloadUrl = $"casaas://files?hash={hash}";
                Content[downloadUrl] = bytes;
                return new DeploymentManifest.FileSpec()
                {
                    Size = bytes.Length,
                    Hash = hash,
                    DownloadUrl = downloadUrl
                };
            }

            public Task<string> GetProxyBaseAddress(OperationContext context, string serviceUrl, HostParameters parameters, string token)
            {
                throw new NotImplementedException();
            }
        }
    }
}
