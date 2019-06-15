// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using BuildXL.Native.IO;
using BuildXL.Native.IO.Windows;
using BuildXL.Storage.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using Microsoft.Win32.SafeHandles;

namespace BuildXL.Storage
{
    /// <summary>
    /// Map local volumes, allowing lookup of volume path by serial number and opening files by ID.
    /// </summary>
    /// <remarks>
    /// X:\ marks the spot.
    /// </remarks>
    public sealed class VolumeMap
    {
        private readonly Dictionary<ulong, VolumeGuidPath> m_volumePathsBySerial = new Dictionary<ulong, VolumeGuidPath>();
        private readonly Dictionary<string, FileIdAndVolumeId> m_junctionRootFileIds = new Dictionary<string, FileIdAndVolumeId>();

        /// <summary>
        /// Unchanged junction roots
        /// </summary>
        public IReadOnlyList<string> UnchangedJunctionRoots = new List<string>();

        /// <summary>
        /// Changed junction roots
        /// </summary>
        public IReadOnlyList<string> ChangedJunctionRoots = new List<string>();

        /// <summary>
        /// Hooks used by unit tests to skip tracking volumes that do not have journal capability.
        /// </summary>
        public bool SkipTrackingJournalIncapableVolume { get; set; }

        private VolumeMap()
        {
        }

        /// <summary>
        /// Creates a map of local volumes. In the event of a collision which prevents constructing a serial -> path mapping,
        /// a warning is logged and those volumes are excluded from the map. On failure, returns null.
        /// </summary>
        [ContractVerification(false)]
        public static VolumeMap TryCreateMapOfAllLocalVolumes(LoggingContext loggingContext, IReadOnlyList<string> junctionRoots = null)
        {
            var map = new VolumeMap();

            var guidPaths = new HashSet<VolumeGuidPath>();
            List<Tuple<VolumeGuidPath, ulong>> volumes = FileUtilities.ListVolumeGuidPathsAndSerials();
            foreach (var volume in volumes)
            {
                bool guidPathUnique = guidPaths.Add(volume.Item1);
                Contract.Assume(guidPathUnique, "Duplicate guid path");

                VolumeGuidPath collidedGuidPath;
                if (map.m_volumePathsBySerial.TryGetValue(volume.Item2, out collidedGuidPath))
                {
                    if (collidedGuidPath.IsValid)
                    {
                        // This could be an error. Instead, we optimistically create a partial map and hope that theese volumes are not relevant to the build.
                        // Some users have reported VHD-creation automation (concurrent with BuildXL) causing a collision.
                        Tracing.Logger.Log.StorageVolumeCollision(loggingContext, volume.Item2, volume.Item1.Path, collidedGuidPath.Path);

                        // Poison this entry so that we know it is unusable on lookup (ambiguous)
                        map.m_volumePathsBySerial[volume.Item2] = VolumeGuidPath.Invalid;
                    }
                }
                else
                {
                    map.m_volumePathsBySerial.Add(volume.Item2, volume.Item1);
                }
            }

            if (junctionRoots != null)
            {
                foreach (var pathStr in junctionRoots)
                {
                    FileIdAndVolumeId? id = TryGetFinalFileIdAndVolumeId(pathStr);
                    if (id.HasValue)
                    {
                        map.m_junctionRootFileIds[pathStr] = id.Value;
                    }
                }
            }

            // No failure cases currently (but there used to be).
            return map;
        }

        /// <summary>
        /// Gets all (volume serial, volume guid path) pairs in this map.
        /// </summary>
        public IEnumerable<KeyValuePair<ulong, VolumeGuidPath>> Volumes =>
                // Exclude collision markers from enumeration of valid volumes.
                m_volumePathsBySerial.Where(kvp => kvp.Value.IsValid);

        /// <summary>
        /// Validate junction roots
        /// </summary>
        public void ValidateJunctionRoots(LoggingContext loggingContext, VolumeMap previousMap)
        {
            var changed = new List<string>();
            var unchanged = new List<string>();

            foreach (var junction in m_junctionRootFileIds)
            {
                string path = junction.Key;
                FileIdAndVolumeId previousId;
                JunctionRootValidationResult result;
                if (previousMap.m_junctionRootFileIds.TryGetValue(junction.Key, out previousId))
                {
                    if (previousId == junction.Value)
                    {
                        unchanged.Add(path);
                        result = JunctionRootValidationResult.Unchanged;
                    }
                    else
                    {
                        changed.Add(path);
                        result = JunctionRootValidationResult.Changed;
                    }
                }
                else
                {
                    changed.Add(path);
                    result = JunctionRootValidationResult.NotFound;
                }

                Logger.Log.ValidateJunctionRoot(loggingContext, path, result.ToString());
            }

            ChangedJunctionRoots = changed;
            UnchangedJunctionRoots = unchanged;
        }

        /// <summary>
        /// Looks up the GUID path that corresponds to the given serial. Returns <see cref="VolumeGuidPath.Invalid"/> if there is no volume with the given
        /// serial locally.
        /// </summary>
        /// <remarks>
        /// The serial should have 64 significant bits when available on Windows 8.1+ (i.e., the serial returned by <c>GetVolumeInformation</c>
        /// is insufficient). The appropriate serial can be retrieved from any handle on the volume via <see cref="FileUtilities.GetVolumeSerialNumberByHandle"/>.
        /// </remarks>
        public VolumeGuidPath TryGetVolumePathBySerial(ulong volumeSerial)
        {
            VolumeGuidPath maybePath;
            Analysis.IgnoreResult(m_volumePathsBySerial.TryGetValue(volumeSerial, out maybePath));

            // Note that maybePath may be Invalid even if found; we store an Invalid guid path to mark a volume-serial collision, and exclude those volumes from the map.
            return maybePath;
        }

        /// <summary>
        /// Looks up the GUID path for the volume containing the given file handle. Returns <see cref="VolumeGuidPath.Invalid"/> if a matching volume cannot be found
        /// (though that suggests that this volume map is incomplete).
        /// </summary>
        public VolumeGuidPath TryGetVolumePathForHandle(SafeFileHandle handle)
        {
            Contract.Requires(handle != null);

            return TryGetVolumePathBySerial(FileUtilities.GetVolumeSerialNumberByHandle(handle));
        }

        /// <summary>
        /// Creates a <see cref="FileAccessor"/> which can open files based on this volume map.
        /// </summary>
        public FileAccessor CreateFileAccessor()
        {
            return new FileAccessor(this);
        }

        /// <summary>
        /// Creates a <see cref="VolumeAccessor"/> which can open volume handles based on this volume map.
        /// </summary>
        public VolumeAccessor CreateVolumeAccessor()
        {
            return new VolumeAccessor(this);
        }

        /// <summary>
        /// Serializes this instance of <see cref="VolumeMap"/>.
        /// </summary>
        public void Serialize(BuildXLWriter writer)
        {
            Contract.Requires(writer != null);
            writer.WriteCompact(m_volumePathsBySerial.Count);

            foreach (var volumeGuidPath in m_volumePathsBySerial)
            {
                writer.Write(volumeGuidPath.Key);
                if (volumeGuidPath.Value.IsValid)
                {
                    writer.Write(true);
                    writer.Write(volumeGuidPath.Value.Path);
                }
                else
                {
                    writer.Write(false);
                }
            }

            writer.WriteCompact(m_junctionRootFileIds.Count);
            foreach (var junction in m_junctionRootFileIds)
            {
                writer.Write(junction.Key);
                junction.Value.Serialize(writer);
            }
        }

        /// <summary>
        /// Deserializes into an instance of <see cref="VolumeMap"/>.
        /// </summary>
        public static VolumeMap Deserialize(BuildXLReader reader)
        {
            Contract.Requires(reader != null);

            var volumeMap = new VolumeMap();
            int count = reader.ReadInt32Compact();

            for (int i = 0; i < count; ++i)
            {
                ulong serialNumber = reader.ReadUInt64();
                bool isValid = reader.ReadBoolean();
                VolumeGuidPath path = isValid ? VolumeGuidPath.Create(reader.ReadString()) : VolumeGuidPath.Invalid;
                volumeMap.m_volumePathsBySerial.Add(serialNumber, path);
            }

            int numJunctionRoots = reader.ReadInt32Compact();
            for (int i = 0; i < numJunctionRoots; ++i)
            {
                string path = reader.ReadString();
                var id = FileIdAndVolumeId.Deserialize(reader);
                volumeMap.m_junctionRootFileIds.Add(path, id);
            }

            return volumeMap;
        }

        private static FileIdAndVolumeId? TryGetFinalFileIdAndVolumeId(string path)
        {
            SafeFileHandle handle = null;
            var openResult = FileUtilities.TryOpenDirectory(
                path,
                FileDesiredAccess.None,
                FileShare.Read | FileShare.Write | FileShare.Delete,
                // Don't open with FileFlagsAndAttributes.FileFlagOpenReparsePoint because we want to get to the final file id and volume id.
                FileFlagsAndAttributes.FileFlagBackupSemantics,
                out handle);

            if (!openResult.Succeeded)
            {
                return null;
            }

            using (handle)
            {
                return FileUtilities.TryGetFileIdAndVolumeIdByHandle(handle);
            }
        }

        /// <summary>
        /// Result of junction root validation.
        /// </summary>
        private enum JunctionRootValidationResult
        {
            /// <summary>
            /// Junction root unchanged.
            /// </summary>
            Unchanged,

            /// <summary>
            /// Junction root changed.
            /// </summary>
            Changed,

            /// <summary>
            /// Junction root not found.
            /// </summary>
            NotFound
        }
    }

    /// <summary>
    /// Allows opening a batch of files based on their <see cref="FileId"/> and volume serial number.
    /// </summary>
    /// <remarks>
    /// Unlike the <see cref="VolumeMap"/> upon which it operates, this class is not thread-safe.
    /// This class is disposable since it holds handles to volume root paths. At most, it holds one handle to each volume.
    /// </remarks>
    public sealed class FileAccessor : IDisposable
    {
        private Dictionary<ulong, SafeFileHandle> m_volumeRootHandles = new Dictionary<ulong, SafeFileHandle>();
        private readonly VolumeMap m_map;

        internal FileAccessor(VolumeMap map)
        {
            Contract.Requires(map != null);
            m_map = map;
            Disposed = false;
        }

        /// <summary>
        /// Error reasons for <see cref="FileAccessor.TryOpenFileById"/>
        /// </summary>
        public enum OpenFileByIdResult : byte
        {
            /// <summary>
            /// Opened a handle.
            /// </summary>
            Succeeded = 0,

            /// <summary>
            /// The containing volume could not be opened.
            /// </summary>
            FailedToOpenVolume = 1,

            /// <summary>
            /// The given file ID does not exist on the volume.
            /// </summary>
            FailedToFindFile = 2,

            /// <summary>
            /// The file ID exists on the volume but could not be opened
            /// (due to permissions, a sharing violation, a pending deletion, etc.)
            /// </summary>
            FailedToAccessExistentFile = 3,
        }

        /// <summary>
        /// Tries to open a handle to the given file as identified by a (<paramref name="volumeSerial"/>, <paramref name="fileId"/>) pair.
        /// If the result is <see cref="OpenFileByIdResult.Succeeded"/>, <paramref name="fileHandle"/> has been set to a valid handle.
        /// </summary>
        /// <remarks>
        /// This method is not thread safe (see <see cref="FileAccessor"/> remarks).
        /// </remarks>
        public OpenFileByIdResult TryOpenFileById(
            ulong volumeSerial,
            FileId fileId,
            FileDesiredAccess desiredAccess,
            FileShare shareMode,
            FileFlagsAndAttributes flagsAndAttributes,
            out SafeFileHandle fileHandle)
        {
            Contract.Requires(!Disposed);
            Contract.Ensures((Contract.Result<OpenFileByIdResult>() == OpenFileByIdResult.Succeeded) == (Contract.ValueAtReturn(out fileHandle) != null));
            Contract.Ensures((Contract.Result<OpenFileByIdResult>() != OpenFileByIdResult.Succeeded) || !Contract.ValueAtReturn(out fileHandle).IsInvalid);

            SafeFileHandle volumeRootHandle = TryGetVolumeRoot(volumeSerial);

            if (volumeRootHandle == null)
            {
                fileHandle = null;
                return OpenFileByIdResult.FailedToOpenVolume;
            }

            OpenFileResult openResult = FileUtilities.TryOpenFileById(
                volumeRootHandle,
                fileId,
                desiredAccess,
                shareMode,
                flagsAndAttributes,
                out fileHandle);

            if (!openResult.Succeeded)
            {
                Contract.Assert(fileHandle == null);

                if (openResult.Status.IsNonexistent())
                {
                    return OpenFileByIdResult.FailedToFindFile;
                }
                else
                {
                    return OpenFileByIdResult.FailedToAccessExistentFile;
                }
            }

            return OpenFileByIdResult.Succeeded;
        }

        /// <summary>
        /// Indicates if this instance has been disposed via <see cref="Dispose"/>.
        /// </summary>
        /// <remarks>
        /// Needs to be public for contract preconditions.
        /// </remarks>
        public bool Disposed { get; private set; }

        /// <summary>
        /// Closes any handles held by this instance.
        /// </summary>
        public void Dispose()
        {
            Contract.Ensures(Disposed);

            if (Disposed)
            {
                return;
            }

            foreach (SafeFileHandle handle in m_volumeRootHandles.Values)
            {
                handle.Dispose();
            }

            m_volumeRootHandles = null;

            Disposed = true;
        }

        /// <summary>
        /// Creates a volume root handle or retrieves an existing one.
        /// </summary>
        /// <remarks>
        /// This is the un-synchronized get-or-add operation resulting in <see cref="FileAccessor"/>
        /// not being thread-safe.
        /// </remarks>
        private SafeFileHandle TryGetVolumeRoot(ulong volumeSerial)
        {
            SafeFileHandle volumeRootHandle;
            if (!m_volumeRootHandles.TryGetValue(volumeSerial, out volumeRootHandle))
            {
                VolumeGuidPath volumeRootPath = m_map.TryGetVolumePathBySerial(volumeSerial);
                if (!volumeRootPath.IsValid)
                {
                    return null;
                }

                if (
                    !FileUtilities.TryOpenDirectory(
                        volumeRootPath.Path,
                        FileShare.ReadWrite | FileShare.Delete,
                        out volumeRootHandle).Succeeded)
                {
                    Contract.Assert(volumeRootHandle == null);
                    return null;
                }

                m_volumeRootHandles.Add(volumeSerial, volumeRootHandle);
            }

            return volumeRootHandle;
        }
    }

    /// <summary>
    /// Allows opening a batch of volumes (devices) based on their volume serial numbers.
    /// </summary>
    /// <remarks>
    /// Unlike the <see cref="VolumeMap"/> upon which it operates, this class is not thread-safe.
    /// This class is disposable since it holds volume handles. At most, it holds one handle to each volume.
    /// Accessing volume handles is a privileged operation; attempting to open volume handles will likely fail if not elevated.
    /// </remarks>
    public sealed class VolumeAccessor : IDisposable
    {
        private Dictionary<ulong, SafeFileHandle> m_volumeHandles = new Dictionary<ulong, SafeFileHandle>();
        private readonly VolumeMap m_map;

        internal VolumeAccessor(VolumeMap map)
        {
            Contract.Requires(map != null);
            m_map = map;
            Disposed = false;
        }

        /// <summary>
        /// Creates a volume root handle or retrieves an existing one.
        /// </summary>
        /// <remarks>
        /// The returned handle should not be disposed.
        /// </remarks>
        public SafeFileHandle TryGetVolumeHandle(SafeFileHandle handleOnVolume)
        {
            return TryGetVolumeHandle(FileUtilities.GetVolumeSerialNumberByHandle(handleOnVolume));
        }

        /// <summary>
        /// Creates a volume root handle or retrieves an existing one.
        /// </summary>
        /// <remarks>
        /// This is the un-synchronized get-or-add operation resulting in <see cref="VolumeAccessor"/>
        /// not being thread-safe.
        /// The returned handle should not be disposed.
        /// </remarks>
        private SafeFileHandle TryGetVolumeHandle(ulong volumeSerial)
        {
            SafeFileHandle volumeHandle;
            if (!m_volumeHandles.TryGetValue(volumeSerial, out volumeHandle))
            {
                VolumeGuidPath volumeRootPath = m_map.TryGetVolumePathBySerial(volumeSerial);
                if (!volumeRootPath.IsValid)
                {
                    return null;
                }

                OpenFileResult openResult = FileUtilities.TryCreateOrOpenFile(
                    volumeRootPath.GetDevicePath(),
                    FileDesiredAccess.GenericRead,
                    FileShare.ReadWrite | FileShare.Delete,
                    FileMode.Open,
                    FileFlagsAndAttributes.None,
                    out volumeHandle);

                if (!openResult.Succeeded)
                {
                    Contract.Assert(volumeHandle == null);
                    return null;
                }

                m_volumeHandles.Add(volumeSerial, volumeHandle);
            }

            return volumeHandle;
        }

        /// <summary>
        /// Indicates if this instance has been disposed via <see cref="Dispose"/>.
        /// </summary>
        /// <remarks>
        /// Needs to be public for contract preconditions.
        /// </remarks>
        public bool Disposed { get; private set; }

        /// <summary>
        /// Closes any handles held by this instance.
        /// </summary>
        public void Dispose()
        {
            Contract.Ensures(Disposed);

            if (Disposed)
            {
                return;
            }

            foreach (SafeFileHandle handle in m_volumeHandles.Values)
            {
                handle.Dispose();
            }

            m_volumeHandles = null;

            Disposed = true;
        }
    }
}
