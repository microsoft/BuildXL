// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;

namespace BuildXL.Cache.ContentStore.Service
{
    /// <summary>
    ///     Extension methods for interacting with hibernated session data.
    /// </summary>
    public static class HibernatedSessionsExtensions
    {
        /// <summary>
        ///     Name of serialized data file.
        /// </summary>
        public const string FileName = "sessions.json";

        /// <summary>
        ///     Check for the existence of hibernated sessions in the given filesystem root directory.
        /// </summary>
        public static bool HibernatedSessionsExists(this IAbsFileSystem fileSystem, AbsolutePath rootPath)
        {
            Contract.Requires(fileSystem != null);
            Contract.Requires(rootPath != null);

            return fileSystem.DirectoryExists(rootPath) && fileSystem.FileExists(rootPath / FileName);
        }

        /// <summary>
        ///     Deserialize hibernated sessions information from the standard filename in the given directory.
        /// </summary>
        public static async Task<HibernatedSessions> ReadHibernatedSessionsAsync(this IAbsFileSystem fileSystem, AbsolutePath rootPath)
        {
            Contract.Requires(fileSystem != null);
            Contract.Requires(rootPath != null);

            var jsonPath = rootPath / FileName;

            using (var stream = await fileSystem.OpenReadOnlySafeAsync(jsonPath, FileShare.None))
            {
                return stream.DeserializeFromJSON<HibernatedSessions>();
            }
        }

        /// <summary>
        ///     Serialize hibernated session information to the standard filename in the given directory.
        /// </summary>
        public static async Task WriteAsync(this HibernatedSessions sessions, IAbsFileSystem fileSystem, AbsolutePath rootPath)
        {
            Contract.Requires(fileSystem != null);
            Contract.Requires(rootPath != null);

            // Due to abnormal process termination, the file that we'll be writing can be corrupted.
            // To prevent this issue we first write the file into a temporary location and then we "move it" into a final location.

            using (var tempFolder = new DisposableDirectory(fileSystem, rootPath / "Temp"))
            {
                var jsonTempPath = tempFolder.CreateRandomFileName();
                var jsonPath = rootPath / FileName;

                using (var stream =
                    await fileSystem.OpenSafeAsync(jsonTempPath, FileAccess.Write, FileMode.Create, FileShare.None))
                {
                    sessions.SerializeToJSON(stream);
                }

                fileSystem.MoveFile(jsonTempPath, jsonPath, replaceExisting: true);
            }
        }

        /// <summary>
        ///     Delete the hibernated sessions file in the given filesystem root directory.
        /// </summary>
        public static Task DeleteHibernatedSessions(this IAbsFileSystem fileSystem, AbsolutePath rootPath)
        {
            Contract.Requires(fileSystem != null);
            Contract.Requires(rootPath != null);

            return Task.Run(() =>
            {
                var jsonPath = rootPath / FileName;
                if (fileSystem.DirectoryExists(rootPath) && fileSystem.FileExists(jsonPath))
                {
                    fileSystem.DeleteFile(jsonPath);
                }
            });
        }
    }
}
