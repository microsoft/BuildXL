// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Distributed;

namespace BuildXL.Cache.ContentStore.Distributed.Redis.Credentials
{
    /// <summary>
    /// Connection string provider for Redis
    /// </summary>
    public class ExecutableConnectionStringProvider : IConnectionStringProvider
    {
        /// <summary>
        /// Intent for the setting provider.
        /// </summary>
        public enum RedisConnectionIntent
        {
            /// <summary>
            /// Redis connection provider for content
            /// </summary>
            Content = 0,

            /// <summary>
            /// Redis connection provider for metadata
            /// </summary>
            Metadata = 1,

            /// <summary>
            /// Redis connection provider for machine locations
            /// </summary>
            MachineLocations = 2
        }

        /// <summary>
        /// Env variable denoting path to Redis credential provider executable file
        /// </summary>
        public const string CredentialProviderVariableName = "CloudStoreRedisCredentialProviderPath";
        private readonly RedisConnectionIntent _redisConnectionIntent;
        private readonly TimeSpan _credentialProviderTimeout = TimeSpan.FromMinutes(15);

        /// <summary>
        /// Initializes a new instance of the <see cref="ExecutableConnectionStringProvider"/> class.
        /// </summary>
        public ExecutableConnectionStringProvider(RedisConnectionIntent redisConnectionIntent)
        {
            _redisConnectionIntent = redisConnectionIntent;
        }

        /// <inheritdoc/>
        public Task<ConnectionStringResult> GetConnectionString()
        {
            return Task.Run(() => Execute(CancellationToken.None));
        }

        private ConnectionStringResult Execute(CancellationToken cancellationToken)
        {
            try
            {
                var stdOut = new StringBuilder();
                var stdError = new StringBuilder();
                string argumentString = $"-intent \"{_redisConnectionIntent}\"";

                string environmentVariable = Environment.GetEnvironmentVariable(CredentialProviderVariableName);
                if (string.IsNullOrEmpty(environmentVariable))
                {
                    return
                        ConnectionStringResult.CreateFailure(
                            $"Credential provider environment variable {CredentialProviderVariableName} not set.");
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = environmentVariable,
                    Arguments = argumentString,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                    ErrorDialog = false
                };

                var process = Process.Start(startInfo);
                if (process == null)
                {
                    return ConnectionStringResult.CreateFailure($"Failed to start credential provider process {environmentVariable}.");
                }

                process.OutputDataReceived += (o, e) => { stdOut.Append(e.Data); };
                process.ErrorDataReceived += (o, e) => { stdError.Append(e.Data); };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                using (cancellationToken.Register(() => Kill(process)))
                {
                    if (!process.WaitForExit((int)_credentialProviderTimeout.TotalMilliseconds))
                    {
                        Kill(process);
                        return
                            ConnectionStringResult.CreateFailure(
                                $"Credential provider took longer {_credentialProviderTimeout.TotalSeconds} secs.");
                    }

                    // Give time for the Async event handlers to finish by calling WaitForExit again.
                    // if the first one succeeded
                    // Note: Read remarks from https://msdn.microsoft.com/en-us/library/ty0d8k56(v=vs.110).aspx
                    // for reason.
                    process.WaitForExit();
                }

                process.CancelErrorRead();
                process.CancelOutputRead();

                if (process.ExitCode != 0)
                {
                    return ConnectionStringResult.CreateFailure(
                        $"Credential provider execution failed with exit code {process.ExitCode}",
                        $"StdOut: \n{stdOut}\nStdErr: \n{stdError}\n");
                }

                return ConnectionStringResult.CreateSuccess(stdOut.ToString());
            }
            catch (Exception e)
            {
                return ConnectionStringResult.CreateFailure(e);
            }
        }

        private void Kill(Process p)
        {
            if (p.HasExited)
            {
                return;
            }

            try
            {
                p.Kill();
            }
            catch (InvalidOperationException)
            {
                // the process may have exited,
                // in this case ignore the exception
            }
        }
    }
}
