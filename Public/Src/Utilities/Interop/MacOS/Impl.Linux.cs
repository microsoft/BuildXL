// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using static BuildXL.Interop.Libraries;
using static BuildXL.Interop.Unix.Constants;
using static BuildXL.Interop.Unix.IO;
using static BuildXL.Interop.Unix.Impl_Common;
using static BuildXL.Interop.Unix.Memory;
using static BuildXL.Interop.Unix.Process;
using static BuildXL.Interop.Unix.Processor;

namespace BuildXL.Interop.Unix
{
    /// <summary>
    /// The IO class for Linux-specific operations
    /// </summary>
    internal static class Impl_Linux
    {
        /// <summary>
        /// Version of __fxstatat syscalls to use.
        /// </summary>
        private const int __Ver = 1;

        private const string ProcPath = "/proc";
        private const string ProcStatPath = "/stat";
        private const string ProcMemInfoPath = "/meminfo";
        private const string ProcStatusPath = "/status";
        private const string ProcIoPath = "/io";

        private static long TicksPerSecond;

        /// <summary>Convert a number of "jiffies", or ticks, to a TimeSpan.</summary>
        /// <param name="ticks">The number of ticks.</param>
        /// <returns>The equivalent TimeSpan.</returns>
        internal static TimeSpan TicksToTimeSpan(double ticks)
        {
            long ticksPerSecond = Volatile.Read(ref TicksPerSecond);
            if (ticksPerSecond == 0)
            {
                // Look up the number of ticks per second in the system's configuration, then use that to convert to a TimeSpan
                ticksPerSecond = sysconf((int)Sysconf_Flags._SC_CLK_TCK);
                Volatile.Write(ref TicksPerSecond, ticksPerSecond);
            }

            return TimeSpan.FromSeconds(ticks / (double)ticksPerSecond);
        }

        private static readonly Lazy<DriveInfo[]> s_sortedDrives
            = new Lazy<DriveInfo[]>(() => DriveInfo.GetDrives().OrderBy(di => di.Name).Reverse().ToArray());

        /// <summary>
        /// Linux specific implementation of <see cref="IO.GetFileSystemType"/>
        /// </summary>
        internal static int GetFileSystemType(SafeFileHandle fd, StringBuilder fsTypeName, long bufferSize)
        {
            var path = ToPath(fd);
            if (path == null)
            {
                return ERROR;
            }

            return Try(() =>
                {
                    var di = new DriveInfo(path);
                    fsTypeName.Append(di.DriveFormat);
                    return 0;
                },
                ERROR);
        }

        /// <summary>
        /// Linux specific implementation of <see cref="IO.StatFileDescriptor"/>
        /// </summary>
        public static int StatFileDescriptor(SafeFileHandle fd, ref StatBuffer statBuf)
        {
            return StatFile(ToInt(fd), string.Empty, followSymlink: false, ref statBuf);
        }

        /// <summary>
        /// Linux specific implementation of <see cref="IO.StatFile"/>
        /// </summary>
        internal static int StatFile(string path, bool followSymlink, ref StatBuffer statBuf)
        {
            Contract.Requires(!string.IsNullOrEmpty(path));
            return StatFile(AT_FDCWD, path, followSymlink, ref statBuf);
        }

        private static int StatFile(int fd, string path, bool followSymlink, ref StatBuffer statBuf)
        {
            // using 'fstatat' instead of the newer 'statx' because Ubuntu 18.04 doesn't have iit
            var buf = new stat_buf();
            int result = StatFile(fd, path, followSymlink, ref buf);
            if (result != 0)
            {
                return ERROR;
            }
            else
            {
                Translate(buf, ref statBuf);
                return 0;
            }
        }

        /// <summary>
        /// pathname, dirfd, and flags to identify the target file in one of the following ways:
        ///
        /// An absolute pathname
        ///    If pathname begins with a slash, then it is an absolute pathname that identifies the target file.  In this
        ///    case, dirfd is ignored.
        ///
        /// A relative pathname
        ///    If pathname is a string that begins with a character other than a slash and dirfd is AT_FDCWD, then
        ///    pathname is a relative pathname that is interpreted relative to the process's current working directory.
        ///
        /// A directory-relative pathname
        ///    If  pathname  is  a  string that begins with a character other than a slash and dirfd is a file descriptor
        ///    that refers to a directory, then pathname is a relative pathname that is interpreted relative to the
        ///    directory referred to by dirfd.
        /// </summary>
        private static int StatFile(int dirfd, string pathname, bool followSymlink, ref stat_buf buf)
        {
            Contract.Requires(pathname != null);

            int flags = 0
                | (!followSymlink                 ? AT_SYMLINK_NOFOLLOW : 0)
                | (string.IsNullOrEmpty(pathname) ? AT_EMPTY_PATH : 0);

            int result;
            while (
                (result = fstatat(__Ver, dirfd, pathname, ref buf, flags)) < 0 &&
                Marshal.GetLastWin32Error() == (int)Errno.EINTR);
            return result;
        }

        /// <summary>
        /// Linux specific implementation of <see cref="IO.SafeReadLink"/>
        /// </summary>
        internal static long SafeReadLink(string link, StringBuilder buffer, long bufferSize)
        {
            var resultLength = readlink(link, buffer, bufferSize);
            if (resultLength < 0) return ERROR;
            buffer.Length = (int)resultLength;
            return resultLength;
        }

        /// <summary>
        /// Linux specific implementation of <see cref="IO.Open"/>
        /// </summary>
        internal static SafeFileHandle Open(string pathname, OpenFlags flags, FilePermissions permissions)
        {
            int result;
            while (
                (result = open(pathname, Translate(flags), permissions)) < 0 &&
                Marshal.GetLastWin32Error() == (int)Errno.EINTR);
            return new SafeFileHandle(new IntPtr(result), ownsHandle: true);
        }

        /// <summary>
        /// Linux specific implementation of <see cref="IO.GetFilePermissionsForFilePath"/>
        /// </summary>
        internal static int GetFilePermissionsForFilePath(string path, bool followSymlink)
        {
            var stat = new stat_buf();
            int errorCode = StatFile(AT_FDCWD, path, followSymlink, ref stat);
            return errorCode == 0 ? (int)stat.st_mode : ERROR;
        }

        /// <summary>
        /// Linux specific implementation of <see cref="IO.SetFilePermissionsForFilePath"/>
        /// </summary>
        internal static int SetFilePermissionsForFilePath(string path, FilePermissions permissions, bool followSymlink)
        {
            if (!followSymlink && IsSymlink(path))
            {
                // Permissions do not apply to symlinks on Linux systems (only on BSD systems).
                return 0;
            }

            return chmod(path, permissions);
        }

        /// <summary>
        /// Linux specific implementation of <see cref="IO.SetTimeStampsForFilePath"/>
        /// </summary>
        /// <remarks>
        /// Only atime and mtime are settable
        /// </remarks>
        internal static int SetTimeStampsForFilePath(string path, bool followSymlink, StatBuffer buf)
        {
            int flags = followSymlink ? 0 : AT_SYMLINK_NOFOLLOW;
            var atime = new Timespec { Tv_sec = buf.TimeLastAccess,       Tv_nsec = buf.TimeNSecLastAccess };
            var mtime = new Timespec { Tv_sec = buf.TimeLastModification, Tv_nsec = buf.TimeNSecLastModification };
            return utimensat(AT_FDCWD, path, new[] { atime, mtime }, flags);
        }

        private static ulong ExtractValueFromProcLine(string line)
        {
            return line != null && ulong.TryParse(line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)[1], out var val) ? val : 0;
        }

        /// <summary>
        /// Linux specific implementation of <see cref="Memory.GetRamUsageInfo"/> \
        /// </summary>
        internal static int GetRamUsageInfo(ref RamUsageInfo buffer)
        {
            try
            {
                string[] lines = System.IO.File.ReadAllLines($"{ProcPath}{ProcMemInfoPath}");
                string memTotalLine = lines.FirstOrDefault(line => line.StartsWith("MemTotal:"));
                string memAvailableLine = lines.FirstOrDefault(line => line.StartsWith("MemAvailable:"));
                buffer.TotalBytes = ExtractValueFromProcLine(memTotalLine) * 1024;
                buffer.FreeBytes = ExtractValueFromProcLine(memAvailableLine) * 1024;
                return 0;
            }
            #pragma warning disable
            catch (Exception)
            {
                return ERROR;
            }
            #pragma warning restore
        }

        private static IEnumerable<int> GetChildren(int processId)
        {
            IEnumerable<int> childPids = Enumerable.Empty<int>();
            try
            {
                var proc = System.Diagnostics.Process.GetProcessById(processId);
                foreach (ProcessThread thread in proc.Threads)
                {
                    var contents = File.ReadAllText($"{ProcPath}/{proc.Id}/task/{thread.Id}/children");
                    var ids = contents.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(pid => Int32.Parse(pid));
                    childPids = childPids.Concat(ids);
                }
            }
#pragma warning disable
            catch (Exception) { }
#pragma warning restore

            return childPids;
        }

        /// <summary>
        /// Gets resource consumption data for a specific process, throws if the underlying ProcFS structures are not present or malformed.
        /// </summary>
        private static ProcessResourceUsage CreateProcessResourceUsageForPid(int pid)
        {
            var firstLine = File.ReadAllLines($"{ProcPath}/{pid}/{ProcStatPath}").FirstOrDefault();
            var splits = firstLine.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToArray();
            var utime = ((ulong)TicksToTimeSpan(double.Parse(splits[13])).Ticks) * 100UL;
            var stime = ((ulong)TicksToTimeSpan(double.Parse(splits[14])).Ticks) * 100UL;

            string[] lines = System.IO.File.ReadAllLines($"{ProcPath}/{pid}/{ProcIoPath}");
            string readOps = lines.FirstOrDefault(line => line.StartsWith("syscr:"));
            string bytesRead = lines.FirstOrDefault(line => line.StartsWith("read_bytes:"));
            string writeOps = lines.FirstOrDefault(line => line.StartsWith("syscw:"));
            string bytesWritten = lines.FirstOrDefault(line => line.StartsWith("write_bytes:"));

            lines = System.IO.File.ReadAllLines($"{ProcPath}/{pid}/{ProcStatusPath}");
            string workingSetSize = lines.FirstOrDefault(line => line.StartsWith("VmRSS:"));
            string peakWorkingSetSize = lines.FirstOrDefault(line => line.StartsWith("VmHWM:"));

            return new ProcessResourceUsage()
            {
                UserTimeNs = utime,
                SystemTimeNs = stime,
                DiskReadOps = ExtractValueFromProcLine(readOps),
                DiskBytesRead = ExtractValueFromProcLine(bytesRead),
                DiskWriteOps = ExtractValueFromProcLine(writeOps),
                DiskBytesWritten = ExtractValueFromProcLine(bytesWritten),
                WorkingSetSize = ExtractValueFromProcLine(workingSetSize) * 1024,
                PeakWorkingSetSize = ExtractValueFromProcLine(peakWorkingSetSize) * 1024,
                NumberOfChildProcesses = 0,
            };
        }

        private static IEnumerable<ProcessResourceUsage> GetResourceUsagesForProcessTree(int processId, bool includeChildren)
        {
            var stack = new Stack<int>();
            stack.Push(processId);

            while (stack.Any())
            {
                var next = stack.Pop();

                var resourceUsage = CreateProcessResourceUsageForPid(next);
                if (includeChildren)
                {
                    var children = GetChildren(next);
                    resourceUsage.NumberOfChildProcesses = children.Count();
                    foreach (var child in children)
                    {
                        stack.Push(child);
                    }
                }

                yield return resourceUsage;
            }

            yield break;
        }

        internal static int GetProcessResourceUsage(int pid, ref ProcessResourceUsage buffer, long bufferSize, bool includeChildProcesses)
        {
            try
            {
                var resourceUsage = GetResourceUsagesForProcessTree(pid, includeChildProcesses);
                buffer.UserTimeNs = resourceUsage.Aggregate(0UL, (acc, usage) => acc + usage.UserTimeNs);
                buffer.SystemTimeNs = resourceUsage.Aggregate(0UL, (acc, usage) => acc + usage.SystemTimeNs);
                buffer.WorkingSetSize = resourceUsage.Aggregate(0UL, (acc, usage) => acc + usage.WorkingSetSize);
                buffer.PeakWorkingSetSize = resourceUsage.Aggregate(0UL, (acc, usage) => acc + usage.PeakWorkingSetSize);
                buffer.DiskReadOps = resourceUsage.Aggregate(0UL, (acc, usage) => acc + usage.DiskReadOps);
                buffer.DiskBytesRead = resourceUsage.Aggregate(0UL, (acc, usage) => acc + usage.DiskBytesRead);
                buffer.DiskWriteOps = resourceUsage.Aggregate(0UL, (acc, usage) => acc + usage.DiskWriteOps);
                buffer.DiskBytesWritten = resourceUsage.Aggregate(0UL, (acc, usage) => acc + usage.DiskBytesWritten);
                buffer.NumberOfChildProcesses = resourceUsage.Aggregate(0, (acc, usage) => acc + usage.NumberOfChildProcesses);

                return 0;
            }
#pragma warning disable
            catch (Exception)
            {
                return ERROR;
            }
#pragma warning restore
        }

        /// <summary>
        /// Linux specific implementation of <see cref="Memory.GetMemoryPressureLevel"/>
        /// </summary>
        internal static int GetMemoryPressureLevel(ref PressureLevel level)
        {
            // there is no memory pressure level on Linux
            level = PressureLevel.Normal;
            return 0;
        }

        /// <summary>
        /// Linux specific implementation of <see cref="Processor.GetCpuLoadInfo"/>
        /// </summary>
        internal static int GetCpuLoadInfo(ref CpuLoadInfo buffer, long bufferSize)
        {
            try
            {
                var firstLine = File.ReadAllLines($"{ProcPath}{ProcStatPath}").FirstOrDefault();
                if (string.IsNullOrEmpty(firstLine)) return ERROR;
                var splits = firstLine.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToArray();

                return ulong.TryParse(splits[1], out buffer.UserTime) &&
                       ulong.TryParse(splits[3], out buffer.SystemTime) &&
                       ulong.TryParse(splits[4], out buffer.IdleTime)
                     ? 0
                     : ERROR;
            }
            #pragma warning disable
            catch (Exception)
            {
                return ERROR;
            }
            #pragma warning restore
        }

        // CODESYNC: NormalizeAndHashPath in StringOperations.cpp
        // TODO: there is no reason for this hash computation to be done in native StringOperations.cpp
        private const uint Fnv1Prime32 = 16777619;
        private const uint Fnv1Basis32 = 2166136261;
        private static uint _Fold(uint hash, byte value)
        {
            unchecked { return (hash * Fnv1Prime32) ^ (uint)value; }
        }
        private static uint Fold(uint hash, uint value)
        {
            unchecked { return _Fold(_Fold(hash, (byte)value), (byte)(((uint)value) >> 8)); }
        }

        internal static int NormalizePathAndReturnHash(byte[] pPath, byte[] normalizedPath)
        {
            Contract.Requires(pPath.Length == normalizedPath.Length);
            unchecked
            {
                uint hash = Fnv1Basis32;
                int i = 0;
                for (; i < pPath.Length && pPath[i] != 0; i++)
                {
                    normalizedPath[i] = pPath[i];
                    hash = Fold(hash, normalizedPath[i]);
                }

                Contract.Assert(i < normalizedPath.Length);
                normalizedPath[i] = 0;
                return (int)hash;
            }
        }

        internal static string GetMountNameForPath(string path)
        {
            return s_sortedDrives.Value.FirstOrDefault(di => path.StartsWith(di.Name))?.Name;
        }

        [DllImport(Libraries.LibC, SetLastError = true)]
        unsafe internal static extern int lsetxattr(
            [MarshalAs(UnmanagedType.LPStr)] string path,
            [MarshalAs(UnmanagedType.LPStr)] string name,
            void *value,
            ulong size,
            int flags);

        [DllImport(Libraries.LibC, SetLastError = true)]
        internal static extern long lgetxattr(
            [MarshalAs(UnmanagedType.LPStr)] string path,
            [MarshalAs(UnmanagedType.LPStr)] string name,
            ref long value,
            ulong size,
            int flags);

        private static bool IsSymlink(string path)
        {
            var buf = new stat_buf();
            return
                fstatat(__Ver, AT_FDCWD, path, ref buf, AT_SYMLINK_NOFOLLOW) == 0 &&
                (buf.st_mode & (ushort)FilePermissions.S_IFLNK) != 0;
        }

        private static string ToPath(SafeFileHandle fd)
        {
            var path = new StringBuilder(MaxPathLength);
            return SafeReadLink($"{ProcPath}/self/fd/{ToInt(fd)}", path, path.Capacity) >= 0
                ? path.ToString()
                : null;
        }

        private static O_Flags Translate(OpenFlags flags)
        {
            return Enum
                .GetValues(typeof(OpenFlags))
                .Cast<OpenFlags>()
                .Where(f => flags.HasFlag(f))
                .Aggregate(O_Flags.O_NONE, (acc, f) => acc | TranslateOne(f));
        }

        private static O_Flags TranslateOne(OpenFlags flag)
        {
            return flag switch
            {
                OpenFlags.O_RDONLY   => O_Flags.O_RDONLY,
                OpenFlags.O_WRONLY   => O_Flags.O_WRONLY,
                OpenFlags.O_RDWR     => O_Flags.O_RDWR,
                OpenFlags.O_NONBLOCK => O_Flags.O_NONBLOCK,
                OpenFlags.O_APPEND   => O_Flags.O_APPEND,
                OpenFlags.O_CREAT    => O_Flags.O_CREAT,
                OpenFlags.O_TRUNC    => O_Flags.O_TRUNC,
                OpenFlags.O_EXCL     => O_Flags.O_EXCL,
                OpenFlags.O_NOFOLLOW => O_Flags.O_NOFOLLOW | O_Flags.O_PATH,
                OpenFlags.O_SYMLINK  => O_Flags.O_NOFOLLOW | O_Flags.O_PATH,
                OpenFlags.O_CLOEXEC  => O_Flags.O_CLOEXEC,
                                   _ => O_Flags.O_NONE,
            };
        }

        private static uint Concat(params uint[] elems) => elems.Aggregate(0U, (a, e) => a*10+e);

        private static void Translate(stat_buf from, ref StatBuffer to)
        {
            to.DeviceID                 = (int)from.st_dev;
            to.InodeNumber              = from.st_ino;
            to.Mode                     = (ushort)from.st_mode;
            to.HardLinks                = (ushort)from.st_nlink;
            to.UserID                   = from.st_uid;
            to.GroupID                  = from.st_gid;
            to.Size                     = from.st_size;
            to.TimeLastAccess           = from.st_atime;
            to.TimeLastModification     = from.st_mtime;
            to.TimeLastStatusChange     = from.st_ctime;
            to.TimeCreation             = 0; // not available
            // even though EXT4 supports nanosecond precision, the kernel time is not
            // necessarily getting updated every 1ns so nsec values can still be quantized
            to.TimeNSecLastAccess       = from.st_atime_nsec;
            to.TimeNSecLastModification = from.st_mtime_nsec;
            to.TimeNSecLastStatusChange = from.st_ctime_nsec;
            to.TimeNSecCreation         = 0; // not available
        }

        private static T Try<T>(Func<T> action, T errorValue)
        {
            try
            {
                return action();
            }
            #pragma warning disable ERP022 // Unobserved exception in generic exception handler
            catch
            {
                return errorValue;
            }
            #pragma warning restore ERP022 // Unobserved exception in generic exception handler
        }

        #region P-invoke consts, structs, and other type definitions
        #pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

        private const int AT_FDCWD              = -100;
        private const int AT_SYMLINK_NOFOLLOW   = 0x100;
        private const int AT_EMPTY_PATH         = 0x1000;

        /// <summary>
        /// struct stat from stat.h
        /// </summary>
        /// <remarks>
        /// IMPORTANT: the explicitly specified size of 256 must match the value of 'sizeof(struct stat)' in C
        /// </remarks>
        [StructLayout(LayoutKind.Sequential, Size = 144)]
        private struct stat_buf
        {
            public  UInt64   st_dev;     // device
            public  UInt64   st_ino;     // inode
            public  UInt64   st_nlink;   // number of hard links
            public  UInt32   st_mode;    // protection
            public  UInt32   st_uid;     // user ID of owner
            public  UInt32   st_gid;     // group ID of owner
            private UInt32   _padding;   // padding for structure alignment
            public  UInt64   st_rdev;    // device type (if inode device)
            public  Int64    st_size;    // total size, in bytes
            public  Int64    st_blksize; // blocksize for filesystem I/O
            public  Int64    st_blocks;  // number of blocks allocated
            public  Int64    st_atime;   // time of last access
            public  Int64    st_atime_nsec; // Timespec.tv_nsec partner to st_atime
            public  Int64    st_mtime;   // time of last modification
            public  Int64    st_mtime_nsec; // Timespec.tv_nsec partner to st_mtime
            public  Int64    st_ctime;   // time of last status change
            public  Int64    st_ctime_nsec; // Timespec.tv_nsec partner to st_ctime
            /* More spare space here for future expansion (controlled by explicitly specifying struct size) */
        }

        /// <summary>
        /// Flags for <see cref="open"/>
        /// </summary>
        [Flags]
        public enum O_Flags : int
        {
            O_RDONLY    = 0,     // open for reading only
            O_NONE      = 0,
            O_WRONLY    = 1,     // open for writing only
            O_RDWR      = 2,     // open for reading and writing
            O_CREAT     = 64,    // create file if it does not exist
            O_EXCL      = 128,   // error if O_CREAT and the file exists
            O_NOCCTY    = 256,
            O_TRUNC     = 512,   // truncate size to 0
            O_APPEND    = 1024,  // append on each write
            O_NONBLOCK  = 2048,  // do not block on open or for data to become available
            O_ASYNC     = 8192,
            O_DIRECT    = 16384,
            O_DIRECTORY = 65536,
            O_NOFOLLOW  = 131072,  // do not follow symlinks
            O_CLOEXEC   = 524288, // mark as close-on-exec
            O_SYNC      = 1052672,
            O_PATH      = 2097152, // allow open of symlinks
        }

        /// <summary>
        /// struct stat from stat.h
        /// </summary>
        /// <remarks>
        /// IMPORTANT: the explicitly specified size of 112 must match the value of 'sizeof(struct sysinfo)' in C
        /// </remarks>
        [StructLayout(LayoutKind.Sequential, Size = 112)]
        internal struct sysinfo_buf
        {
            public Int64 uptime;     /* Seconds since boot */
            public UInt64 load1, load2, load3; /* 1, 5, and 15 minute load averages */
            public UInt64 totalram;  /* Total usable main memory size */
            public UInt64 freeram;   /* Available memory size */
            public UInt64 sharedram; /* Amount of shared memory */
            public UInt64 bufferram; /* Memory used by buffers */
            public UInt64 totalswap; /* Total swap space size */
            public UInt64 freeswap;  /* Swap space still available */
            public UInt16 procs;    /* Number of current processes */
            public UInt64 totalhigh; /* Total high memory size */
            public UInt64 freehigh;  /* Available high memory size */
            public UInt32 mem_unit;   /* Memory unit size in bytes */
            /* Padding */
        };

        [Flags]
        internal enum Sysconf_Flags : int
        {
            _SC_CLK_TCK = 1,
            _SC_PAGESIZE = 2
        };

        #endregion

        #region P-invoke function definitions
        [DllImport(LibC, SetLastError = true)]
        private static extern int open(string pathname, O_Flags flags, FilePermissions permission);

        [DllImport(LibC, EntryPoint = "__fxstatat", SetLastError = true, CharSet = CharSet.Ansi)]
        private static extern int fstatat(int __ver, int fd, string pathname, ref stat_buf buff, int flags);

        [DllImport(LibC, SetLastError = true, CharSet = CharSet.Ansi)]
        private static extern int utimensat(int dirfd, string pathname, Timespec[] times, int flags);

        [DllImport(LibC, SetLastError = true, CharSet = CharSet.Ansi)]
        private static extern int sysinfo(ref sysinfo_buf buf);

        [DllImport(LibC, EntryPoint = "copy_file_range", SetLastError = true, CharSet = CharSet.Ansi)]
        internal static extern long copyfilerange(int fd_in, IntPtr off_in, int fd_out, IntPtr off_out, long len, uint flags);

        [DllImport(LibC, SetLastError = true, CharSet = CharSet.Ansi)]
        internal static extern long sendfile(int fd_out, int fd_in, IntPtr offset, long count);

        [DllImport(LibC, SetLastError = true, CharSet = CharSet.Ansi)]
        internal static extern int posix_fadvise(int fd, long offset, long len, int advice);

        [DllImport(LibC, SetLastError = true, CharSet = CharSet.Ansi)]
        internal static extern long sysconf(int name);

        #pragma warning restore CS1591 // Missing XML comment for publicly visible type or member

        #endregion
    }
}