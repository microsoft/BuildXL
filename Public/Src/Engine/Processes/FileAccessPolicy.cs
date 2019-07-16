// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;

namespace BuildXL.Processes
{
    // Keep this in sync with the C++ version declared in DataTypes.h

    /// <summary>
    /// Flags indicating whether a file may be read or written
    /// </summary>
    [SuppressMessage("Microsoft.Design", "CA1008:EnumsShouldHaveZeroValue", Justification = "We have 'Deny'.")]
    [SuppressMessage("Microsoft.Naming", "CA1714:FlagsEnumsShouldHavePluralNames", Justification = "A policy comprises many things.")]
    [SuppressMessage("Microsoft.Usage", "CA2217:DoNotMarkEnumsWithFlags", Justification = "Having MaskNothing is nice.")]
    [Flags]
    public enum FileAccessPolicy : ushort
    {
        /// <summary>
        /// Don't allow anything
        /// </summary>
        Deny = 0x00,

        /// <summary>
        /// Allow reading
        /// </summary>
        AllowRead = 0x01,

        /// <summary>
        /// Allow writing
        /// </summary>
        AllowWrite = 0x02,

        /// <summary>
        /// Allow attempts to open for reading or probe attributes, but then fail if the file exists.
        /// </summary>
        AllowReadIfNonexistent = 0x04,

        /// <summary>
        /// Allows directories to be created
        /// </summary>
        AllowCreateDirectory = 0x08,

        /// <summary>
        /// If set, then we will report attempts to access files under this scope such that the file exists.
        /// to the access report file.  BuildXL uses this information to discover dynamic dependencies, such as #include
        /// files.
        /// </summary>
        ReportAccessIfExistent = 0x10,

        /// <summary>
        /// If set, then we will report the USN just after a file open operation for files under this scope
        /// to the access report file.  BuildXL uses this information to make sure that the same file version that's hashed that's actually read by a process.
        /// </summary>
        ReportUsnAfterOpen = 0x20,

        /// <summary>
        /// If set, then we will report attempts to access files under this scope that fail due to the path or file being absent.
        /// BuildXL uses this information to discover dynamic anti-dependencies, such as those on an #include search path, sneaky loader search paths, etc.
        /// </summary>
        ReportAccessIfNonexistent = 0x40,

        /// <summary>
        /// If set, then we will report attempts to enumerate directories under this scope
        /// BuildXL uses this information to discover dynamic anti-dependencies/directory enumerations, such as those on an #include search path, sneaky loader search paths, etc.
        /// </summary>
        ReportDirectoryEnumerationAccess = 0x80,

        /// <summary>
        /// Allows creation of a symlink.
        /// </summary>
        AllowSymlinkCreation = 0x100,

        /// <summary>
        /// Allows the real timestamps for input files to be seen under this scope. 
        /// </summary>
        /// <remarks>
        /// BuildXL always exposes the same consistent timestamp <see cref="WellKnownTimestamps.NewInputTimestamp"/> for input files to consuming pips unless
        /// this flag is specified
        /// </remarks>
        AllowRealInputTimestamps = 0x200,

        /// <summary>
        /// Override writes allowed by policy based on file existence checks. 
        /// </summary>
        /// <remarks>
        /// Used entirely in the context of shared opaques, where the whole cone under the opaque root is write-allowed by policy (except known inputs).
        /// This policy makes sure that writes on undeclared inputs that fall under the write-allowed cone are flagged as DFAs.
        /// The way to dermine undeclared inputs is based on file existence: if a pip tries to write into a file - allowed by policy - but
        /// that was not created by the pip (i.e. the file was there before the first write attempted by this pip), then it is a write on an undeclared input
        /// Observe that sandboxing never blocks in this case, denying the access is surfaced as a DFA after the write happened.
        /// </remarks>
        OverrideAllowWriteForExistingFiles = 0x400,

        /// <summary>
        /// If set, then we will report attempts to access files under this scope, whether they exist or not (combination of <see cref="ReportAccessIfExistent"/>
        /// and <see cref="ReportAccessIfNonexistent"/>).
        /// </summary>
        ReportAccess = ReportAccessIfNonexistent | ReportAccessIfExistent | ReportDirectoryEnumerationAccess,

        /// <summary>
        /// Allow reading, even if the file doesn't exist.
        /// </summary>
        AllowReadAlways = AllowRead | AllowReadIfNonexistent,

        /// <summary>
        /// Allow both reading and writing, even if the file doesn't exist.
        /// </summary>
        AllowAllButSymlinkCreation = AllowRead | AllowReadIfNonexistent | AllowWrite | AllowCreateDirectory,

        /// <summary>
        /// Allow both reading and writing, even if the file doesn't exist.
        /// </summary>
        AllowAll = AllowRead | AllowReadIfNonexistent | AllowWrite | AllowCreateDirectory | AllowSymlinkCreation,

        /// <summary>
        /// This is a scope mask which denies all operations (and removes <see cref="ReportAccess"/>).
        /// </summary>
        MaskAll = 0,

        /// <summary>
        /// This is a scope mask which does not limit any operations (including <see cref="ReportAccess"/>).
        /// </summary>
        MaskNothing = 0xFFFF,
    }
}
