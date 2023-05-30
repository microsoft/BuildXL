// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using Microsoft.VisualBasic;

namespace BuildXL.Cache.ContentStore.Service
{
    /// <summary>
    ///     Extension methods for interacting with hibernated session data.
    /// </summary>
    public static class HibernatedSessionsExtensions
    {
        /// <summary>
        ///     Check for the existence of hibernated sessions in the given filesystem root directory.
        /// </summary>
        public static bool HibernatedSessionsExists(this IAbsFileSystem fileSystem, AbsolutePath rootPath, string fileName)
        {
            Contract.Requires(fileSystem != null);
            Contract.Requires(rootPath != null);

            return fileSystem.DirectoryExists(rootPath) && fileSystem.FileExists(rootPath / fileName);
        }

        /// <summary>
        ///     Deserialize hibernated sessions information from the standard filename in the given directory.
        /// </summary>
        public static Task<HibernatedSessions<TInfo>> ReadHibernatedSessionsAsync<TInfo>(this IAbsFileSystem fileSystem, AbsolutePath rootPath, string fileName)
        {
            Contract.Requires(rootPath != null);

            var jsonPath = rootPath / fileName;

            using (Stream stream = fileSystem.OpenReadOnly(jsonPath, FileShare.None))
            {
                return Task.FromResult(stream.DeserializeFromJSON<HibernatedSessions<TInfo>>());
            }
        }

        /// <summary>
        ///     Serialize hibernated session information to the standard filename in the given directory.
        /// </summary>
        public static void Write<TInfo>(this HibernatedSessions<TInfo> sessions, IAbsFileSystem fileSystem, AbsolutePath rootPath, string fileName)
        {
            Contract.Requires(rootPath != null);

            // Due to abnormal process termination, the file that we'll be writing can be corrupted.
            // To prevent this issue we first write the file into a temporary location and then we "move it" into a final location.

            using (var tempFolder = new DisposableDirectory(fileSystem, rootPath / "Temp"))
            {
                var jsonTempPath = tempFolder.CreateRandomFileName();
                var jsonPath = rootPath / fileName;

                using (var stream = fileSystem.Open(jsonTempPath, FileAccess.Write, FileMode.Create, FileShare.None))
                {
                    sessions.SerializeToJSON(stream);
                }

                fileSystem.MoveFile(jsonTempPath, jsonPath, replaceExisting: true);
            }
        }

        /// <summary>
        ///     Delete the hibernated sessions file in the given filesystem root directory.
        /// </summary>
        public static Task DeleteHibernatedSessions(this IAbsFileSystem fileSystem, AbsolutePath rootPath, string fileName)
        {
            Contract.Requires(rootPath != null);

            return Task.Run(() =>
            {
                var jsonPath = rootPath / fileName;
                if (fileSystem.DirectoryExists(rootPath) && fileSystem.FileExists(jsonPath))
                {
                    fileSystem.DeleteFile(jsonPath);
                }
            });
        }
    }
}
