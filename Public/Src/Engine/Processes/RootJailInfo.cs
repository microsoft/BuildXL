// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;

namespace BuildXL.Processes
{
    /// <summary>
    /// Info for root jail.
    /// </summary>
    public readonly struct RootJailInfo
    {
        /// <summary>Root directory</summary>
        public string RootJail { get; }

        /// <summary>User id to set for the <c>--userspec</c> switch to <c>chroot</c></summary>
        public int? UserId { get; }

        /// <summary>Group id to set for the <c>--userspec</c> switch to <c>chroot</c></summary>
        public int? GroupId { get; }

        /// <summary>
        /// This option provides a way to override <see cref="SandboxKind"/> and turn off sandboxing even
        /// when <see cref="SandboxKind"/> says otherwise.
        /// </summary>
        /// <remarks>
        /// The reason for this weird setup is the fact that the root jail options are only honored by 
        /// (select) sandboxed process wrappers.  Setting <see cref="SandboxKind"/> to None would indeed
        /// turn off sandboxing but it would also force execution with <see cref="UnsandboxedProcess"/>
        /// which does not support running processes inside a root jail.
        /// </remarks>
        public bool DisableSandboxing { get; }

        /// <summary>
        /// An option to disable reporting dynamically loaded shared libraries.
        /// Provided because enabling auditing may add considerable overhead.
        /// </summary>
        public bool DisableAuditing { get; }

        /// <summary>
        /// Program to use to enter root jail. Defaults to <c>sudo chroot</c>, which requires NOPASSWD sudo privileges.
        /// </summary>
        public (string program, string[] args) RootJailProgram { get; init; } = ("/usr/bin/sudo", new[] { "/usr/sbin/chroot" });

        /// <nodoc />
        public RootJailInfo(string rootJail, int? userId = null, int? groupId = null, bool disableSandboxing = false, bool disableAuditing = false)
        {
            Contract.RequiresNotNull(rootJail);
            RootJail = rootJail;
            UserId = userId;
            GroupId = groupId;
            DisableSandboxing = disableSandboxing;
            DisableAuditing = disableAuditing;
        }

        /// <nodoc />
        public void Serialize(BuildXLWriter writer)
        {
            writer.Write(RootJail);
            writer.Write(UserId, static (w, v) => w.WriteCompact(v));
            writer.Write(GroupId, static (w, v) => w.WriteCompact(v));
            writer.Write(DisableSandboxing);
            writer.Write(DisableAuditing);
            writer.Write(RootJailProgram.program);
            writer.Write(RootJailProgram.args, static (w, a) => w.Write(a));
        }

        /// <nodoc />
        public static RootJailInfo Deserialize(BuildXLReader reader)
        {
            return new RootJailInfo(
                rootJail: reader.ReadString(),
                userId: reader.ReadNullableStruct(static r => r.ReadInt32Compact()),
                groupId: reader.ReadNullableStruct(static r => r.ReadInt32Compact()),
                disableSandboxing: reader.ReadBoolean(),
                disableAuditing: reader.ReadBoolean())
            {
                RootJailProgram = (reader.ReadString(), reader.ReadArray(static r => r.ReadString())),
            };
        }
    }

    /// <summary>
    /// Extension methods for <see cref="RootJailInfo"/>.
    /// </summary>
    public static class RootJailInfoExtensions
    {
        /// <summary>
        /// If <see cref="RootJailInfo.RootJail"/> is set:
        ///    if <paramref name="path"/> is relative to <see cref="RootJailInfo.RootJail"/> returns an absolute path which
        ///    when accessed from the root jail resolves to path at location <paramref name="path"/>; otherwise throws.
        ///
        /// If <see cref="RootJailInfo.RootJail"/> is null:
        ///    returns <paramref name="path"/>
        /// </summary>
        public static string ToPathInsideRootJail(this RootJailInfo? @this, string path)
        {
            string rootJailDir = @this?.RootJail;
            if (rootJailDir == null)
            {
                return path;
            }

            if (!path.StartsWith(rootJailDir.TrimEnd('/') + '/'))
            {
                throw new BuildXLException($"Root jail dir '{rootJailDir}' must be a parent directory of path '{path}'");
            }

            var jailRelativePath = path.Substring(rootJailDir.Length);
            return jailRelativePath[0] == '/'
                ? jailRelativePath
                : "/" + jailRelativePath;
        }

        /// <summary>
        /// If <see cref="RootJailInfo.RootJail" /> is not null, copies <paramref name="file"/> into the root of that directory
        /// and returns the absolute path to that file as if the filesystem root were <see cref="RootJailInfo.RootJail" />;
        /// otherwise, returns <paramref name="file"/>.
        /// </summary>
        public static string CopyToRootJailIfNeeded(this RootJailInfo? @this, string file)
        {
            string rootJailDir = @this?.RootJail;
            if (rootJailDir == null)
            {
                return file;
            }

            var basename = Path.GetFileName(file);
            File.Copy(file, Path.Combine(rootJailDir, basename));
            return "/" + basename;
        }
    }
}
