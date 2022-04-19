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

        /// <nodoc />
        public void WaitForExit(TimeSpan? timeout)
        {
            if (timeout == null)
            {
                _process.WaitForExit();
            }
            else
            {
                bool exited = _process.WaitForExit((int)timeout.Value.TotalMilliseconds);
                if (!exited)
                {
                    throw new InvalidOperationException($"The process with Id {Id} did not exit after '{timeout}'.");
                }
            }
        }

        /// <inheritdoc />
        public int ExitCode => _process.ExitCode;

        /// <inheritdoc />
        public DateTime? ExitTime => HasExited ? _process.ExitTime : null;

        /// <inheritdoc />
        public int Id => _started ? _process.Id : -1;

        /// <inheritdoc />
        public bool HasExited => _process.HasExited;

        /// <inheritdoc />
        public bool WaitForExit(TimeSpan timeout) => _process.WaitForExit((int)timeout.TotalMilliseconds);

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

            _process.OutputDataReceived += onOutputDataReceived;
            _process.ErrorDataReceived += onErrorDataReceived;

            _process.Exited += (sender, args) =>
                               {
                                   // Unsubscribing from the events before disposing.

                                   _process.OutputDataReceived -= onOutputDataReceived;
                                   _process.ErrorDataReceived -= onErrorDataReceived;

                                   // Dispose will drain all the existing items from the message queues.
                                   outputMessagesNagleQueue.Dispose();
                                   errorMessagesNagleQueue.Dispose();
                               };
            
            _process.Start();

            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
            _started = true;

            void onOutputDataReceived(object s, DataReceivedEventArgs e)
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    outputMessagesNagleQueue.Enqueue(e.Data);
                }
            }

            void onErrorDataReceived(object s, DataReceivedEventArgs e)
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    errorMessagesNagleQueue.Enqueue(e.Data);
                }
            }
        }
    }
}
