// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

namespace BuildXL.Cache.ContentStore.Synchronization
{
    /// <summary>
    /// Encapsulation of a lock mechanism corresponding to a filesystem folder.
    /// </summary>
    public sealed class DirectoryLock : IDisposable
    {
        private readonly object _lockObj = new object();
        private readonly AbsolutePath _directoryPath;
        private readonly string _component;
        private readonly TimeSpan _waitTimeout;
        private readonly DirectoryLockFile _directoryLockFile;
        private readonly Tracer _tracer = new Tracer(nameof(DirectoryLock));
        private Task<LockAcquisitionResult> _acquisitionTask;

        /// <summary>
        ///     Initializes a new instance of the <see cref="DirectoryLock"/> class.
        /// </summary>
        public DirectoryLock(AbsolutePath directoryPath, IAbsFileSystem fileSystem, TimeSpan waitTimeout, string component = null)
        {
            Contract.Requires(directoryPath != null);
            Contract.Requires(fileSystem != null);

            _directoryPath = directoryPath;
            _component = component ?? string.Empty;
            _directoryLockFile = new DirectoryLockFile(fileSystem, directoryPath / $"{_component}.lock", pollingInterval: TimeSpan.FromSeconds(1));
            _waitTimeout = waitTimeout;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _directoryLockFile.Dispose();
        }

        /// <summary>
        ///     AcquireAsync the lock, waiting as long as it takes or until the configured timeout.
        /// </summary>
        public Task<LockAcquisitionResult> AcquireAsync(Context context)
        {
            if (_acquisitionTask == null)
            {
                lock (_lockObj)
                {
                    if (_acquisitionTask == null)
                    {
                        _acquisitionTask = AcquireInternalAsync(context);
                    }
                }
            }

            return _acquisitionTask;
        }

        private async Task<LockAcquisitionResult> AcquireInternalAsync(Context context)
        {
            var componentDescription = string.IsNullOrEmpty(_component) ? string.Empty : $" component [{_component}]";
            _tracer.Info(context, $"Acquiring directory lock for [{_directoryPath}]{componentDescription}");
            DateTime timeoutTime = DateTime.UtcNow + _waitTimeout;

            var acquired = await _directoryLockFile.AcquireAsync(context, _waitTimeout);

            if (!acquired.LockAcquired)
            {
                return acquired;
            }

            return LockAcquisitionResult.Acquired();
        }
    }
}
