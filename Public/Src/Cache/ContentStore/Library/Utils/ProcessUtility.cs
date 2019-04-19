// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.Text;
using System.Threading;

namespace BuildXL.Cache.ContentStore.Utils
{
    /// <nodoc />
    sealed public class ProcessUtility
    {
        private readonly Process _process;
        private readonly bool _createNoWindow;
        private readonly StringBuilder _outputString = new StringBuilder();
        private readonly StringBuilder _errorString = new StringBuilder();
        private readonly AutoResetEvent _outputWaitHandle = new AutoResetEvent(false);
        private readonly AutoResetEvent _errorWaitHandle = new AutoResetEvent(false);

        /// <summary>
        /// <see cref="Process.ExitCode"/>
        /// </summary>
        public int? ExitCode => (_process?.HasExited ?? false) ? _process?.ExitCode : null;

        /// <summary>
        /// Helper for creating a process.
        /// </summary>
        /// <param name="fileName">Application file name with which to start the process.</param>
        /// <param name="args">Arguments passed to the process.</param>
        /// <param name="createNoWindow">Whether the process shoudl not open a window.</param>
        public ProcessUtility(string fileName, string args, bool createNoWindow)
        {
            _createNoWindow = createNoWindow;
            _process = new Process();
            _process.StartInfo = new ProcessStartInfo(fileName, args)
            {
                // ReSharper disable ConditionIsAlwaysTrueOrFalse
                CreateNoWindow = _createNoWindow,
                UseShellExecute = !_createNoWindow,
                RedirectStandardOutput = _createNoWindow,
                RedirectStandardError = _createNoWindow

                // ReSharper restore ConditionIsAlwaysTrueOrFalse
            };

            if (_createNoWindow)
            {
                _process.OutputDataReceived += (sender, e) =>
                {
                    if (e.Data == null)
                    {
                        _outputWaitHandle.Set();
                    }
                    else
                    {
                        _outputString.Append(e.Data);
                    }
                };

                _process.ErrorDataReceived += (sender, e) =>
                {
                    if (e.Data == null)
                    {
                        _errorWaitHandle.Set();
                    }
                    else
                    {
                        _errorString.Append(e.Data);
                    }
                };
            }
        }

        /// <summary>
        /// Start the process.
        /// </summary>
        public void Start()
        {
            if (!System.IO.File.Exists(_process.StartInfo.FileName)) {
                throw new System.InvalidOperationException($"Main executable does not exist: ${_process.StartInfo.FileName}");
            }
            _process.Start();

            if (_createNoWindow)
            {
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();
            }
        }

        /// <summary>
        /// Wait for the process to exit.
        /// </summary>
        /// <param name="milliseconds">Number of milliseconds to wait. Will wait infinitely if set to -1.</param>
        /// <returns>Whether the process exited.</returns>
        public bool WaitForExit(int milliseconds = -1)
        {
            return _process.WaitForExit(milliseconds) && _outputWaitHandle.WaitOne(milliseconds) && _errorWaitHandle.WaitOne(milliseconds);
        }

        /// <summary>
        /// Whether the process has exited.
        /// </summary>
        public bool HasExited => _process.HasExited;

        /// <summary>
        /// Id for the process.
        /// </summary>
        public int Id => _process.Id;

        /// <summary>
        /// Request that the process be killed.
        /// </summary>
        public void Kill()
        {
            _process.Kill();
        }

        /// <summary>
        /// Get the stdout seen so far.
        /// </summary>
        public string GetStdOut()
        {
            return _outputString.ToString();
        }

        /// <summary>
        /// Get the stderr seen so far.
        /// </summary>
        public string GetStdErr()
        {
            return _errorString.ToString();
        }

        /// <summary>
        /// Get the stdout and stderr seen so far.
        /// </summary>
        public string GetLogs()
        {
            return GetStdOut() + GetStdErr();
        }
    }
}
