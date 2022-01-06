// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// ACTION: Uncomment this to run test. Comment out before checking in.
//#define RUNLAUNCHERTESTS

using Xunit;
using BuildXL.Cache.ContentStore.App;
using System;
using System.Threading.Tasks;
using System.Threading;
using BuildXL.Launcher.Server;
using Xunit.Abstractions;
using System.IO;
using System.Text;
using FluentAssertions;
using System.Collections.Generic;
using System.Diagnostics;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.Host.Service;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Utilities.CLI;
using BuildXL.Utilities;
using BuildXL.Utilities.Tasks;

namespace BuildXL.Cache.Host.Test
{
    public class LauncherIntegrationTests
    {
        public ITestOutputHelper Output { get; }

        public LauncherIntegrationTests(ITestOutputHelper output)
        {
            Output = output;
            var converter = new Converter(output);
            Console.SetOut(converter);
        }

        // Inputs (see ACTION for how to populate necessary value)
        private const string TestPathRoot = @"E:\bin\depsvc";

        // ACTION: This is a path to a local BuildXL enlistment
        private const string BuildXLPathRoot = @"E:\Code\BuildXL";

        // Source root with configuration files
        // ACTION: Clone CacheConfig repo to this path
        private const string SourceRoot = TestPathRoot + @"\src\dev";

        // Drop read token
        // ACTION: Generate this from Azure DevOps PAT page
        private const string DropToken = @"[INSERT VALUE HERE]";

        // Azure credential environment variables
        // ACTION: Retrieve these from the app registration in the Azure portaion
        public Dictionary<string, string> AzureCredentialEnvironmentVariables = new Dictionary<string, string>()
        {
            { "AZURE_TENANT_ID", "[INSERT VALUE HERE]" },
            { "AZURE_CLIENT_ID", "[INSERT VALUE HERE]" },
            { "AZURE_CLIENT_SECRET", "[INSERT VALUE HERE]" }
        };

        // ACTION: Get the url of the necessary key vault
        public const string KeyVaultUri = "[INSERT VALUE HERE]";

        // Secret value for cachedeploykey1
        // ACTION: Retrieve this from the key vault above
        public const string AuthorizationSecret = "[INSERT VALUE HERE]";


        public static string LauncherSettingsContent(string machine) => DetokenizeMachine(machine:machine, value: @"
{
  'DeploymentParameters': {
    'Environment': 'Dev',
    'Machine': '{Machine}',
    'Stamp': 'CO_J1',
    'Ring': 'Ring_0',
    'Region': 'CO',
    'MachineFunction': 'DevBox',
    'GetContentInfoOnly': {GetContentInfoOnly},
    'AuthorizationSecretName': 'cachedeploykey1',
    'AuthorizationSecret': '{AuthorizationSecret}'
  },
  'ServiceUrl': 'http://localhost:5000/Deployment',
  'QueryIntervalSeconds': 1,
  //'ServiceLifetimePollingIntervalSeconds': 1,
  'TargetDirectory': '{TestPathRoot}/{Machine}/launcher',
  //'DownloadConcurrency': 4,
  'RetentionSizeGb': 5
}
"
    .Trim()
    .Replace("'", "\"")
    .Replace("{AuthorizationSecret}", AuthorizationSecret)
    // ACTION: Set this to false to actually download files
    .Replace("{GetContentInfoOnly}", "false")
    .Replace("{TestPathRoot}", TestPathRoot.Replace('\\', '/')));

        // Path to drop.exe
        // ACTION: This can be obtained by running drop.cmd in BuildXL root
        // afterwards it will be downloaded to {BuildXLRoot}\Out\SelfHost\Drop.App\lib\net45\drop.exe
        private const string DropExePath = BuildXLPathRoot + @"\Out\SelfHost\Drop.App\lib\net45\drop.exe";

        // Computed values
        private static string LauncherSettingsPath(string machine) => DetokenizeMachine(TestPathRoot + @"\{Machine}.LauncherSettings.json", machine);
        private const string DeploymentRoot = TestPathRoot + @"\root";

#if RUNLAUNCHERTESTS
#if NET_COREAPP
        [Fact]
#endif
#endif
        public async Task TestLauncherWorkflowAsync()
        {
            using var cts = new CancellationTokenSource();

            UpdateLauncherSettings("M1");
            UpdateLauncherSettings("M2");

            RunIngester(cts.Token);

            var testHost = new TestHost();

            DeploymentLauncher.OverrideHost = testHost;

            var depServiceTask = StartDeploymentServiceAsync(cts.Token);

            var launcher1Task = StartLauncherAsync("M1", cts.Token);

            await Task.Delay(TimeSpan.FromSeconds(10));

            var launcher2Task = StartLauncherAsync("M2", cts.Token);

            await launcher1Task;
            await launcher2Task;
            await depServiceTask;
        }

        public static async Task<int> RunDeploymentProxyWithCacheServiceAsync(string[] args = null, CancellationToken token = default)
        {
            try
            {
                await Task.Yield();

                args ??= new[]
                {
                    "cacheService",
                    "--cacheconfigurationPath", SourceRoot + @"\CacheConfiguration.json",
                    "--proxyconfigurationPath", SourceRoot + @"\ProxyConfiguration.json",
                    "--standalone", "true"
                };
#if NET_COREAPP
                await DeploymentProgram.RunAsync(args, token);
#endif
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception in RunDeploymentProxyWithCacheServiceAsync: " + ex.ToString());
                return -1;
            }
        }

        private static string DetokenizeMachine(string value, string machine)
        {
            return value.Replace("{Machine}", machine);
        }

        private static void UpdateLauncherSettings(string machine)
        {
            File.WriteAllText(LauncherSettingsPath(machine), LauncherSettingsContent(machine));
        }

        public static async Task<int> RunCacheServiceAsync(string[] args = null, CancellationToken token = default)
        {
            args = args ?? new[]
            {
                "cacheService",
                @"-configurationPath=" + SourceRoot + @"\CacheConfiguration.json"
            };

            var result = await Program.RunAppAsync(args, token);
            result.Should().Be(0);
            return result;
        }

        public void RunIngester(CancellationToken token)
        {
            Program.RunApp(new[]
            {
                "deploy",
                @"-sourceRoot=" + SourceRoot,
                @"-targetDirectory=" + DeploymentRoot,
                @"-deploymentConfigPath=" + SourceRoot + @"\DeploymentConfiguration.json",
                @"-dropExePath=" + DropExePath,
                @"-dropToken=" + DropToken,
                @"-LogSeverity=Debug"
                //@"-retentionSizeGb=" + 50,
            },
            token).Should().Be(0);
        }

        public Task StartDeploymentServiceAsync(CancellationToken token)
        {
            foreach (var kvp in AzureCredentialEnvironmentVariables)
            {
                Environment.SetEnvironmentVariable(kvp.Key, kvp.Value);
            }

#if NET_COREAPP
            return DeploymentProgram.RunAsync(new[]
            {
                "--KeyVaultUri", KeyVaultUri,
                "--DeploymentRoot", DeploymentRoot
            },
            token);
#else
            return Task.CompletedTask;
#endif
        }

        public async Task StartLauncherAsync(string machine, CancellationToken token)
        {
            //return;
            var result = await Program.RunAppAsync(new[]
            {
                "launcher",
                @"-settingsPath=" + LauncherSettingsPath(machine),
                @"-LogSeverity=Debug"
            },
            token);

            result.Should().Be(0);
        }

        public class TestHost : IDeploymentLauncherHost
        {
            public TestProcess Process { get; set; }

            public ILauncherProcess CreateProcess(ProcessStartInfo info)
            {
                Process = new TestProcess(info);
                return Process;
            }

            public IDeploymentServiceClient CreateServiceClient()
            {
                return new Client(DeploymentLauncherHost.Instance.CreateServiceClient());
            }

            private class Client : IDeploymentServiceClient
            {
                private readonly IDeploymentServiceClient _inner;

                public Client(IDeploymentServiceClient inner)
                {
                    _inner = inner;
                }

                public void Dispose()
                {
                    _inner.Dispose();
                }

                public Task<LauncherManifest> GetLaunchManifestAsync(OperationContext context, LauncherSettings settings)
                {
                    return _inner.GetLaunchManifestAsync(context, settings);
                }

                public Task<string> GetProxyBaseAddress(OperationContext context, string serviceUrl, HostParameters parameters, string token)
                {
                    return _inner.GetProxyBaseAddress(context, serviceUrl, parameters, token);
                }

                public Task<Stream> GetStreamAsync(OperationContext context, string downloadUrl)
                {
                    var newDownloadUrl = downloadUrl.Replace("m1", "localhost");
                    return _inner.GetStreamAsync(context, newDownloadUrl);
                }
            }
        }

        public class TestProcess : ILauncherProcess
        {
            public const int GracefulShutdownExitCode = 0;
            public const int KilledExitCode = -1;

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
                }
            }

            public void Kill(OperationContext context)
            {
                Exit(KilledExitCode);
            }

            public void Start(OperationContext context)
            {
                HasStarted = true;

                var task = runAsync();
                Analysis.IgnoreArgument(task);

                async Task runAsync()
                {
                    try
                    {
                        IsRunningService = true;

                        foreach (var envVar in StartInfo.Environment)
                        {
                            Environment.SetEnvironmentVariable(envVar.Key, envVar.Value);
                        }

                        var args = AbstractParser.CommonSplitArgs(StartInfo.Arguments);
                        var exitCode = StartInfo.FileName.EndsWith("DepSvc.exe", StringComparison.OrdinalIgnoreCase)
                            ? await RunDeploymentProxyWithCacheServiceAsync(args)
                            : await RunCacheServiceAsync(args);

                        Exit(exitCode);
                    }
                    catch (Exception ex)
                    {
                        Exit(-2);
                        Analysis.IgnoreArgument(ex);
                    }
                    finally
                    {
                        IsRunningService = false;
                    }
                }
            }
        }

        private class Converter : TextWriter
        {
            private readonly ITestOutputHelper _output;
            public Converter(ITestOutputHelper output)
            {
                _output = output;
            }
            public override Encoding Encoding
            {
                get { return Encoding.UTF8; }
            }
            public override void WriteLine(string message)
            {
                _output.WriteLine(message);
            }
            public override void WriteLine(string format, params object[] args)
            {
                _output.WriteLine(format, args);
            }
        }
    }
}
