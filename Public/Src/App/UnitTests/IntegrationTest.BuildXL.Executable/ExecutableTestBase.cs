// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using BuildXL.Native.IO;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Tracing;
using Newtonsoft.Json;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit.Abstractions;

namespace IntegrationTest.BuildXL.Executable
{
    /// <summary>
    /// Test base for tests that call the BuildXL exe directly. This test base works across different qualifiers,
    /// though the deployment for a different test build depending on the qualifier.
    /// </summary>
    public class ExecutableTestBase : TemporaryStorageTestBase
    {
        /// <summary>
        /// Constants.
        /// </summary>
        public struct ArtifactNames
        {
            /// <summary>
            /// Name of test build deployment folder.
            /// </summary>
            public const string TestBuildFolder = "TestBuild";

            /// <summary>
            /// Name of Logs folder.
            /// </summary>
            public const string LogsFolder = "Logs";

            /// <summary>
            /// Name of primary log file.
            /// </summary>
            public const string LogFile = LogFileExtensions.DefaultLogPrefix + LogFileExtensions.Log;

            /// <summary>
            /// Name of bxl executable.
            /// </summary>
            public const string BxlExe = "bxl.exe";

            /// <summary>
            /// Name of bxl analyzer executable.
            /// </summary>
            public const string BxlAnalyzerExe = "bxlanalyzer.exe";

            /// <summary>
            /// Name of build config file.
            /// </summary>
            public const string ConfigFile = "config.dsc";

            /// <summary>
            /// Name of default cache config for tests.
            /// </summary>
            public const string DefaultCacheConfigFile = "DefaultTestCacheConfig.json";
        }

        /// <summary>
        /// Test builds have 1 minute to complete.
        /// </summary>
        private const int BuildTimeoutMs = 60000;

        /// <summary>
        /// An int used to generate unique file or directory paths.
        /// </summary>
        private int m_uniquePathId = 0;

        /// <summary>
        /// Bin folder for tests where test deployments go.
        /// </summary>
        private string TestBinRoot { get; }

        /// <summary>
        /// Bin folder where Bxl deployment for the tests go.
        /// </summary>
        protected string TestBxlDeploymentRoot { get; }

        /// <summary>
        /// Folder where the test build files are deployed.
        /// </summary>
        protected string TestBuildRoot { get; }

        /// <summary>
        /// Full path to the build executable to be tested.
        /// </summary>
        private string BxlExecutable { get; }

        /// <summary>
        /// Full path to the build execution analyzer executable.
        /// </summary>
        private string BxlAnalyzerExecutable { get; }

        /// <summary>
        /// The config for a default build to be built during tests.
        /// </summary>
        protected string DefaultTestBuildConfig { get; set; }

        /// <summary>
        /// The default config for the cache.
        /// </summary>
        private Lazy<string> m_defaultCacheConfig;

        /// <summary>
        /// The full path to the default test config for the cache.
        /// </summary>
        private string DefaultCacheConfig => m_defaultCacheConfig.Value;

        /// <summary>
        /// Full path to the cache directory.
        /// </summary>
        private string CacheDirectory { get; }

        /// <summary>
        /// Full path to the server directory deployment.
        /// </summary>
        private string ServerDeploymentRoot { get; }

        /// <summary>
        /// Servers spawned by a test that need to be killed at the end of the build.
        /// </summary>
        private HashSet<int> m_servers;

        /// <summary>
        /// Child test classes can set this to set the cache config for the entire class.
        /// </summary>
        protected string CacheConfig { get; set; } = null;

        /// <summary>
        /// Child test classes can set this to set the cache config for the entire class.
        /// </summary>
        protected string BuildConfig { get; set; } = null;

        /// <summary>
        /// Constructor.
        /// </summary>
        public ExecutableTestBase(ITestOutputHelper output) : base(output)
        {
#region pre-existing paths
            TestBinRoot = Path.GetDirectoryName(new Uri(System.Reflection.Assembly.GetExecutingAssembly().CodeBase).AbsolutePath);
            BxlAnalyzerExecutable = Path.Combine(TestBinRoot, ArtifactNames.BxlAnalyzerExe);

            TestBxlDeploymentRoot = TestBinRoot;
            BxlExecutable = Path.Combine(TestBxlDeploymentRoot, ArtifactNames.BxlExe);

            TestBuildRoot = Path.Combine(TestBxlDeploymentRoot, ArtifactNames.TestBuildFolder);
            DefaultTestBuildConfig = Path.Combine(TestBuildRoot, ArtifactNames.ConfigFile);
#endregion pre-existing paths

            // If the test is run outside of BuildXL, there will be no directory scrubbing to clean directories
            FileUtilities.DeleteDirectoryContents(TemporaryDirectory, deleteRootDirectory: true);
            FileUtilities.DeleteDirectoryContents(Path.Combine(TestBuildRoot, "Out"), deleteRootDirectory: true);

#region test-created paths
            CacheDirectory = Path.Combine(TestBuildRoot, "Out", "Cache");
            Directory.CreateDirectory(CacheDirectory);
            // Test cache config will be different than the normal default config because not all test environments will be able to connect to the remote cache
            // Lazily write the config itself the first time it is used
            m_defaultCacheConfig = new Lazy<string>(() =>
            {
                var path = Path.Combine(CacheDirectory, ArtifactNames.DefaultCacheConfigFile);
                File.WriteAllText(path, GetLocalCacheOnlyConfig());
                return path;
            });
#endregion test-created paths

            m_servers = new HashSet<int>();
            // Required by example build
            System.Environment.SetEnvironmentVariable("BUILDXL_BIN", TestBxlDeploymentRoot);
        }

        public string CreateUniqueDirectory(string prefix = null, string root = null)
        {
            var path = CreateUniquePath(prefix, root);
            Directory.CreateDirectory(path);
            return path;
        }

        public string CreateUniquePath(string prefix = null, string root = null)
        {
            var rootToUse = root ?? TemporaryDirectory;
            return Path.Combine(rootToUse, string.Format(CultureInfo.InvariantCulture, "{0}_{1}", prefix, m_uniquePathId++));
        }

        private string CreateUniqueLogsDirectory()
        {
            return CreateUniquePath(root: Path.Combine(TemporaryDirectory, ArtifactNames.LogsFolder));
        }

        private Process ExecuteProcess(string exe, string args)
        {
            var startInfo = new ProcessStartInfo()
            {
                FileName = exe,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };
            var exeProcess = new Process
            {
                StartInfo = startInfo
            };

            exeProcess.Start();
            exeProcess.WaitForExit(BuildTimeoutMs);

            return exeProcess;
        }

        /// <summary>
        /// Runs the build executable.
        /// </summary>
        /// <param name="args">
        /// Command line args to be passed into the build. Note that config or cacheConfig parameters should be used for those args.
        /// </param>
        /// <param name="config">
        /// Path to a custom build config.
        /// </param>
        /// <param name="cacheConfig">
        /// Path to a custom cache config.
        /// </param>
        public BuildResult RunBuild(string args, string config = null, string cacheConfig = null)
        {
            var configToBuild = config ?? BuildConfig ?? DefaultTestBuildConfig;
            var cacheConfigToUse = cacheConfig ?? CacheConfig ?? DefaultCacheConfig;

            // The logs directory location must be specified so it's easy to find them later
            var logsDirectory = CreateUniqueLogsDirectory();
            string argsToUse = $"/c:{configToBuild} /logsDirectory:{logsDirectory} /cacheConfigFilePath:{cacheConfigToUse} "
                                + args;

            var bxlProcess = ExecuteProcess(BxlExecutable, argsToUse);

            // To avoid leaving processes lying around, always search for the server PID in case an unexpected one is created
            var serverPid = GetServerPid(logsDirectory);
            if (serverPid != -1)
            {
                m_servers.Add(serverPid);
            }

            return new BuildResult(bxlProcess, logsDirectory);
        }

        /// <summary>
        /// Runs the build execution analyzer.
        /// </summary>
        /// <param name="args">
        /// Execution analyzer command line args.
        /// </param>
        public int RunAnalyzer(string args)
        {
            return ExecuteProcess(BxlAnalyzerExecutable, args).ExitCode;
        }

        private string GetLocalCacheOnlyConfig()
        {
            var localCache = new Dictionary<string, string>()
            {
                { "Assembly", "BuildXL.Cache.MemoizationStoreAdapter" },
                { "Type", "BuildXL.Cache.MemoizationStoreAdapter.MemoizationStoreCacheFactory" },
                { "CacheId", "SelfhostCS2L1" },
                { "MaxCacheSizeInMB", "20240"},
                { "CacheRootPath", "[BuildXLSelectedRootPath]"},
                { "CacheLogPath", "[BuildXLSelectedLogPath]" },
                { "UseStreamCAS", "true" },
            };

            return JsonConvert.SerializeObject(localCache);
        }

        /// <summary>
        /// Writes a cache config to use the local cache only to the given path.
        /// </summary>
        public void WriteLocalCacheOnlyConfig(string configPath)
        {
            File.WriteAllText(configPath, GetLocalCacheOnlyConfig());
        }

        /// <summary>
        /// Copies majority of the functionality included in bxl.cmd command, with the same defaults.
        /// However, remote cache will be unable to connect if run by a non-authorized user.
        /// Cache config will have to support some form of authentication (ex: personal access token) before this can be used in testing.
        /// </summary>
        private void WriteCustomCacheConfig(string configPath, bool useSharedCache = true, bool publishToSharedCache = false, string vsoAccount = "mseng", string cacheNamespace = "DominoSelfHost")
        {
            var localCache = GetLocalCacheOnlyConfig();

            if (!useSharedCache)
            {
                File.WriteAllText(configPath, localCache);
                return;
            }
            else
            {
                throw new NotImplementedException("Unable to use remote cache in tests because not all testing environments will have authentication. Changes must be made to the cache layer to expose an interface for personal access tokens before remote cache can be used in tests.");
            }

            /* Default remote cache config included here for completeness

            var remoteCache = new Dictionary<string, string>()
            {
                { "Assembly", "BuildXL.Cache.BuildCacheAdapter" },
                { "Type", "BuildXL.Cache.BuildCacheAdapter.BuildCacheFactory" },
                { "CacheId", "L3Cache" },
                { "CacheLogPath", "[BuildXLSelectedLogPath].new" },
                { "CacheServiceFingerprintEndpoint", $"https://{vsoAccount}.artifacts.visualstudio.com/DefaultCollection" },
                { "CacheServiceContentEndpoint", $"https://{vsoAccount}.vsblob.visualstudio.com/DefaultCollection" },
                { "UseBlobContentHashLists", "true" },
                { "CacheNamespace", cacheNamespace },
            };

            var fullCache = new Dictionary<string, string>()
            {
                { "Assembly", "BuildXL.Cache.VerticalAggregator" },
                { "Type", "BuildXL.Cache.VerticalAggregator.VerticalCacheAggregatorFactory" },
                { "RemoteIsReadOnly", (!publishToSharedCache).ToString() },
                { "LocalCache", $"{localCache}" },
                { "RemoteCache", $"{JsonConvert.SerializeObject(remoteCache)}" },
            };

            File.WriteAllText(configPath, JsonConvert.SerializeObject(fullCache));
            */
        }

        /// <summary>
        /// Parses a string for the first int.
        /// </summary>
        /// <param name="str">
        /// The string to parse.
        /// </param>
        /// <param name="beforeInt">
        /// Optional prefix string; if provided, the search does not start until after this string.
        /// </param>
        /// <param name="afterInt">
        /// Optional suffix string; if provided, the search ends at this string.
        /// </param>
        /// <returns>
        /// The first int if found; otherwise, -1.
        /// </returns>
        public int ParseStringForInt(string str, string beforeInt = "", string afterInt = "")
        {
            var regex = new Regex($"{beforeInt}.*?(?<num>[0-9]+).*{afterInt}");
            var match = regex.Match(str);
            if (match.Success)
            {
                return int.Parse(match.Groups["num"].Value);
            }

            return -1;
        }

        /// <summary>
        /// Parses the relevant log file for the server PID.
        /// </summary>
        private int GetServerPid(string logsDirectory)
        {
            var logFile = Path.Combine(logsDirectory, ArtifactNames.LogFile);
            if (!File.Exists(logFile))
            {
                return -1;
            }
            using (var fs = File.Open(logFile, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var bs = new BufferedStream(fs))
            using (var sr = new StreamReader(bs))
            {
                string line;
                var appServerBuildStartRegex = new Regex($"verbose DX[0]*{(int)EventId.AppServerBuildStart}(?<serverInfo>.*)");
                while ((line = sr.ReadLine()) != null)
                {
                    var match = appServerBuildStartRegex.Match(line);
                    if (match.Success)
                    {
                        var pid = ParseStringForInt(match.Groups["serverInfo"].Value, "PID: ", ",");
                        if (pid == -1)
                        {
                            throw new Exception($"Server mode was started but test was unable to find the server PID in the logs: {logFile}.");
                        }

                        return pid;
                    }
                }
            }

            return -1;
        }

        /// <summary>
        /// Kill all the servers generated by a test.
        /// </summary>
        private void KillTestServers()
        {
            // If test set-up failed, this can occur
            if (m_servers == null)
            {
                return;
            }

            foreach (var s in m_servers)
            {
                var serverProcess = Process.GetProcessById(s);
                serverProcess.Kill();
                XAssert.IsTrue(serverProcess.HasExited);
            }
        }

        protected override void Dispose(bool disposing)
        {
            try
            {
                KillTestServers();
            }
            finally
            {
                base.Dispose(disposing);
            }
        }

        /// <summary>
        /// Encapsulates the results of a build.
        /// </summary>
        public class BuildResult
        {
            /// <summary>
            /// Constructor.
            /// </summary>
            public BuildResult(Process buildProcess, string logsDirectory)
            {
                LogsDirectory = logsDirectory;
                ExitCode = buildProcess.ExitCode;
                m_outStreamReader = buildProcess.StandardOutput;
                m_errStreamReader = buildProcess.StandardError;
            }

            /// <summary>
            /// Exit code of build exectuable.
            /// </summary>
            public int ExitCode { get; }

            /// <summary>
            /// Build logs directory.
            /// </summary>
            public string LogsDirectory { get; }

            /// <summary>
            /// Primary build log file.
            /// </summary>
            public string LogFile => Path.Combine(LogsDirectory, LogFileExtensions.DefaultLogPrefix + LogFileExtensions.Log);

            /// <summary>
            /// Build error file.
            /// </summary>
            public string ErrFile => Path.Combine(LogsDirectory, LogFileExtensions.DefaultLogPrefix + LogFileExtensions.Errors);

            /// <summary>
            /// Build warnings file.
            /// </summary>
            public string WarnFile => Path.Combine(LogsDirectory, LogFileExtensions.DefaultLogPrefix + LogFileExtensions.Warnings);

            private string m_stdOut;
            private StreamReader m_outStreamReader;

            /// <summary>
            /// All standard console output as a string.
            /// </summary>
            public string StandardOutput
            {
                get
                {
                    if (!m_outStreamReader.EndOfStream)
                    {
                        m_stdOut = m_outStreamReader.ReadToEnd();
                    }

                    return m_stdOut;
                }
            }

            private string m_stdErr;
            private StreamReader m_errStreamReader;

            /// <summary>
            /// All standard error output as a string.
            /// </summary>
            public string StandardError
            {
                get
                {
                    if (!m_errStreamReader.EndOfStream)
                    {
                        m_stdErr = m_errStreamReader.ReadToEnd();
                    }

                    return m_stdErr;
                }
            }

            public BuildResult AssertSuccess()
            {
                if (ExitCode != 0)
                {
                    XAssert.Fail($"Build exited with error:{Environment.NewLine}{StandardError}");
                }

                return this;
            }
        }
    }
}
