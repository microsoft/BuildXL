// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
        public string RootJailProgram { get; init; } = "sudo chroot";

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
            writer.Write(UserId, (w, v) => w.WriteCompact(v));
            writer.Write(GroupId, (w, v) => w.WriteCompact(v));
            writer.Write(DisableSandboxing);
            writer.Write(DisableAuditing);
            writer.Write(RootJailProgram);
        }

        /// <nodoc />
        public static RootJailInfo Deserialize(BuildXLReader reader)
        {
            return new RootJailInfo(
                rootJail: reader.ReadString(),
                userId: reader.ReadNullableStruct(r => r.ReadInt32Compact()),
                groupId: reader.ReadNullableStruct(r => r.ReadInt32Compact()),
                disableSandboxing: reader.ReadBoolean(),
                disableAuditing: reader.ReadBoolean())
            {
                RootJailProgram = reader.ReadString(),
            };
        }
    }
}
