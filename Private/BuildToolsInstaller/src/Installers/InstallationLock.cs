// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;

namespace BuildToolsInstaller.Installers
{
    public enum InstallationStatus { FreshInstall, InstalledFromCache, None };

    /// <summary>
    /// Provides a way to avoid races against installation directories.
    /// The action that installs a tool in a directory should be invoked through
    /// LockInstallationDirectoryAsync, which will provide information on the
    /// status of that directory (assuming all potential writers use this class).
    /// </summary>
    public class InstallationDirectoryLock
    {
        public static InstallationDirectoryLock Instance { get; } = new InstallationDirectoryLock();

        private readonly ConcurrentDictionary<string, SemaphoreSlim> m_semaphores;
        private readonly ConcurrentDictionary<string, InstallationStatus> m_installationStatus;
        private InstallationDirectoryLock()
        {
            m_semaphores = new ConcurrentDictionary<string, SemaphoreSlim>();
            m_installationStatus = new ConcurrentDictionary<string, InstallationStatus>();
        }

        public async Task<InstallationStatus> PerformInstallationAction(string path, Func<InstallationStatus, Task<InstallationStatus>> installAction, ILogger logger)
        {
            var semaphore = m_semaphores.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));

            var timeout = TimeSpan.FromMinutes(10);
            bool acquired = await semaphore.WaitAsync(timeout);
            if (!acquired)
            {
                logger.Info($"Failed to acquire installation lock for {path} after {timeout.TotalMinutes} min");
                return InstallationStatus.None;
            }

            try
            {
                var currentInstallationStatus = m_installationStatus.GetOrAdd(path, _ => InstallationStatus.None);
                var result = await installAction(currentInstallationStatus);
                m_installationStatus[path] = result;
                return result;
            }
            finally
            {
                semaphore.Release();
            }
        }
    }
}
