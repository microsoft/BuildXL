// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Utilities.Collections;

namespace BuildXL.Cache.Host.Service
{
    /// <summary>
    /// A lightweight wrapper around launched process.
    /// </summary>
    internal sealed class LauncherProcess : ILauncherProcess
    {
        private static readonly Tracer _tracer = new Tracer(nameof(LauncherProcess));

        private bool _started;
        private readonly Process _process;

        public LauncherProcess(ProcessStartInfo info)
        {
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;

            _process = new Process()
                       {
                           StartInfo = info,
                           EnableRaisingEvents = true
                       };

            _process.Exited += (sender, e) => Exited?.Invoke();
        }

        /// <inheritdoc />
        public int ExitCode => _process.ExitCode;

        /// <inheritdoc />
        public int Id => _started ? _process.Id : -1;

        /// <inheritdoc />
        public bool HasExited => _process.HasExited;

        /// <inheritdoc />
        public event Action Exited;

        /// <inheritdoc />
        public void Kill(OperationContext context)
        {
            _process.Kill();
        }

        /// <inheritdoc />
        public void Start(OperationContext context)
        {
            // Using nagle queues to "batch" messages together and to avoid writing them to the logs one by one.
            var outputMessagesNagleQueue = NagleQueue<string>.Create(
                messages =>
                {
                    _tracer.Debug(context, $"Service Output: {string.Join(Environment.NewLine, messages)}");
                    return Task.CompletedTask;
                },
                maxDegreeOfParallelism: 1, interval: TimeSpan.FromSeconds(1), batchSize: 1024);

            var errorMessagesNagleQueue = NagleQueue<string>.Create(
                messages =>
                {
                    _tracer.Error(context, $"Service Error: {string.Join(Environment.NewLine, messages)}");
                    return Task.CompletedTask;
                },
                maxDegreeOfParallelism: 1, interval: TimeSpan.FromSeconds(1), batchSize: 1024);

            _process.OutputDataReceived += (s, e) =>
                                           {
                                               if (!string.IsNullOrEmpty(e.Data))
                                               {
                                                   outputMessagesNagleQueue.Enqueue(e.Data);
                                               }
                                           };

            _process.ErrorDataReceived += (s, e) =>
                                          {
                                              if (!string.IsNullOrEmpty(e.Data))
                                              {
                                                  errorMessagesNagleQueue.Enqueue(e.Data);
                                              }
                                          };

            _process.Exited += (sender, args) =>
                               {
                                   // Dispose will drain all the existing items from the message queues.
                                   outputMessagesNagleQueue.Dispose();
                                   errorMessagesNagleQueue.Dispose();
                               };
            
            _process.Start();

            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
            _started = true;
        }
    }
}
