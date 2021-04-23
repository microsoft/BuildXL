// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using Microsoft.VisualBasic;
using System.Security.Cryptography;

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
        public static async Task<HibernatedSessions<TInfo>> ReadHibernatedSessionsAsync<TInfo>(this IAbsFileSystem fileSystem, AbsolutePath rootPath, string fileName)
        {
            Contract.Requires(fileSystem != null);
            Contract.Requires(rootPath != null);

            var jsonPath = rootPath / fileName;

            using (Stream stream = await fileSystem.OpenReadOnlySafeAsync(jsonPath, FileShare.None))
            {
                return stream.DeserializeFromJSON<HibernatedSessions<TInfo>>();
            }
        }

        /// <summary>
        ///     Deserialize hibernated sessions information from the standard filename in the given directory.
        /// </summary>
        public static async Task<HibernatedSessions<TInfo>> ReadProtectedHibernatedSessionsAsync<TInfo>(this IAbsFileSystem fileSystem, AbsolutePath rootPath, string fileName)
        {
            Contract.Requires(fileSystem != null);
            Contract.Requires(rootPath != null);

            var jsonPath = rootPath / fileName;

            using var fileStreamWithLength = await fileSystem.OpenReadOnlySafeAsync(jsonPath, FileShare.None);
            var bytes = new byte[fileStreamWithLength.Length];
            await fileStreamWithLength.Stream.ReadAsync(bytes, 0, (int)fileStreamWithLength.Length);

            var protectedBytes = ProtectedData.Unprotect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);

            using var stream = new MemoryStream(protectedBytes);
            return stream.DeserializeFromJSON<HibernatedSessions<TInfo>>();
        }

        /// <summary>
        ///     Serialize hibernated session information to the standard filename in the given directory.
        /// </summary>
        public static async Task WriteAsync<TInfo>(this HibernatedSessions<TInfo> sessions, IAbsFileSystem fileSystem, AbsolutePath rootPath, string fileName)
        {
            Contract.Requires(fileSystem != null);
            Contract.Requires(rootPath != null);

            // Due to abnormal process termination, the file that we'll be writing can be corrupted.
            // To prevent this issue we first write the file into a temporary location and then we "move it" into a final location.

            using (var tempFolder = new DisposableDirectory(fileSystem, rootPath / "Temp"))
            {
                var jsonTempPath = tempFolder.CreateRandomFileName();
                var jsonPath = rootPath / fileName;

                using (var stream =
                    await fileSystem.OpenSafeAsync(jsonTempPath, FileAccess.Write, FileMode.Create, FileShare.None))
                {
                    sessions.SerializeToJSON(stream);
                }

                fileSystem.MoveFile(jsonTempPath, jsonPath, replaceExisting: true);
            }
        }

        /// <summary>
        ///     Serialize hibernated session information to the standard filename in the given directory.
        /// </summary>
        public static async Task WriteProtectedAsync<TInfo>(this HibernatedSessions<TInfo> sessions, IAbsFileSystem fileSystem, AbsolutePath rootPath, string fileName)
        {
            Contract.Requires(fileSystem != null);
            Contract.Requires(rootPath != null);

            // Due to abnormal process termination, the file that we'll be writing can be corrupted.
            // To prevent this issue we first write the file into a temporary location and then we "move it" into a final location.

            using (var tempFolder = new DisposableDirectory(fileSystem, rootPath / "Temp"))
            {
                var jsonTempPath = tempFolder.CreateRandomFileName();
                var jsonPath = rootPath / fileName;

                using (var memoryStream = new MemoryStream())
                {
                    sessions.SerializeToJSON(memoryStream);

                    var bytes = memoryStream.ToArray();

                    var protectedBytes = ProtectedData.Protect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);

                    using var fileStream = await fileSystem.OpenSafeAsync(jsonTempPath, FileAccess.Write, FileMode.Create, FileShare.None);
                    await fileStream.Stream.WriteAsync(protectedBytes, 0, protectedBytes.Length);
                }

                fileSystem.MoveFile(jsonTempPath, jsonPath, replaceExisting: true);
            }
        }

        /// <summary>
        ///     Delete the hibernated sessions file in the given filesystem root directory.
        /// </summary>
        public static Task DeleteHibernatedSessions(this IAbsFileSystem fileSystem, AbsolutePath rootPath, string fileName)
        {
            Contract.Requires(fileSystem != null);
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
