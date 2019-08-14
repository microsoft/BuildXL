// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.Reflection;
using System.Text;
using BuildXL.Native.IO;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Processes
{
    /// <summary>
    /// Information about attempted file access
    /// </summary>
    public readonly struct ReportedFileAccess : IEquatable<ReportedFileAccess>
    {
        /// <summary>
        /// Prefix for reads
        /// </summary>
        public const string ReadDescriptionPrefix = " R  ";

        /// <summary>
        /// Prefix for writes
        /// </summary>
        public const string WriteDescriptionPrefix = " W  ";

        /// <summary>
        /// Magic number indicating that no USN was/could be obtained.
        /// </summary>
        public static readonly Usn NoUsn = new Usn(0xFFFFFFFFFFFFFFFF);

        /// <summary>
        /// An action to take on a file or device that exists or does not exist.
        /// </summary>
        public readonly CreationDisposition CreationDisposition;

        /// <summary>
        /// The requested access to the file or device
        /// </summary>
        public readonly DesiredAccess DesiredAccess;

        /// <summary>
        /// Last-error code
        /// </summary>
        public readonly uint Error;

        /// <summary>
        /// USN number. Note that 0 is the default USN number (e.g. when no journal is present),
        /// and the <code>NoUsn</code> value indicates the absence of a USN number.
        /// </summary>
        /// <remarks>
        /// This value is only set (to a value different than <code>NoUsn</code>) when
        /// the requested policy had the <code>ReportUsnAfterOpen</code> flag
        /// or had an expected USN set.
        /// </remarks>
        public readonly Usn Usn;

        /// <summary>
        /// The file or device attributes and flags
        /// </summary>
        [SuppressMessage("Microsoft.Naming", "CA1726:UsePreferredTerms", MessageId = "Flags")]
        public readonly FlagsAndAttributes FlagsAndAttributes;

        /// <summary>
        /// Full path that was accessed. If this path is equivalent to <see cref="ManifestPath"/>, it is null.
        /// If it is not equivalent to <see cref="ManifestPath"/>, then this path is outside of the path table altogether
        /// (manifest absolute path is invalid) or refers to a descendant (i.e., the manifest path id was used in a scope rule).
        /// </summary>
        [SuppressMessage("Microsoft.Naming", "CA1721:PropertyNamesShouldNotMatchGetMethods")]
        public readonly string Path;

        /// <summary>
        /// Path as given in the file access manifest. This path may have been echoed as part of an exact match (<see cref="Path"/> is then null)
        /// or as part of a scope (<see cref="Path"/> is a descendant).
        /// </summary>
        public readonly AbsolutePath ManifestPath;

        /// <summary>
        /// The process which caused the reported file access
        /// </summary>
        public readonly ReportedProcess Process;

        /// <summary>
        /// The requested sharing mode of the file or device.
        /// </summary>
        public readonly ShareMode ShareMode;

        /// <summary>
        /// Whether the file access was allowed or denied
        /// </summary>
        public readonly FileAccessStatus Status;

        /// <summary>
        /// What method was used for determining the <see cref="FileAccessStatus"/>
        /// </summary>
        public readonly FileAccessStatusMethod Method;

        // The following fields are a byte-wide and should be kept
        // together at the end of the structure to minimize padding.

        /// <summary>
        /// Level of access requested by this file operation (e.g. CreateFile can request read, write or both).
        /// </summary>
        public readonly RequestedAccess RequestedAccess;

        /// <summary>
        /// The operation that caused the reported file access
        /// </summary>
        public readonly ReportedFileOperation Operation;

        /// <summary>
        /// If true, the file access was marked for explicit reporting (with <see cref="FileAccessPolicy.ReportAccess"/>),
        /// possibly in a containing scope.
        /// </summary>
        public readonly bool ExplicitlyReported;

        /// <summary>
        /// Enumerate pattern
        /// </summary>
        public readonly string EnumeratePattern;

        /// <summary>
        /// Creates an instance from an absolute path
        /// </summary>
        [SuppressMessage("Microsoft.Naming", "CA1726:UsePreferredTerms", MessageId = "flags")]
        public ReportedFileAccess(
            ReportedFileOperation operation,
            ReportedProcess process,
            RequestedAccess requestedAccess,
            FileAccessStatus status,
            bool explicitlyReported,
            uint error,
            Usn usn,
            DesiredAccess desiredAccess,
            ShareMode shareMode,
            CreationDisposition creationDisposition,
            FlagsAndAttributes flagsAndAttributes,
            AbsolutePath manifestPath,
            string path,
            string enumeratePatttern,
            FileAccessStatusMethod fileAccessStatusMethod = FileAccessStatusMethod.PolicyBased)
        {
            Contract.Requires(process != null);
            Operation = operation;
            Process = process;
            RequestedAccess = requestedAccess;
            Status = status;
            ExplicitlyReported = explicitlyReported;
            Error = error;
            Usn = usn;
            DesiredAccess = desiredAccess;
            ShareMode = shareMode;
            CreationDisposition = creationDisposition;
            FlagsAndAttributes = flagsAndAttributes;
            ManifestPath = manifestPath;
            Path = path;
            EnumeratePattern = enumeratePatttern;
            Method = fileAccessStatusMethod;
        }

        /// <summary>
        /// Error code returned when probing for an absent file.
        /// </summary>
        public const int ERROR_FILE_NOT_FOUND = 0x2;

        /// <summary>
        /// Error code returned when probing for an absent path.
        /// </summary>
        public const int ERROR_PATH_NOT_FOUND = 0x3;

        /// <summary>
        /// Indicates if this file access was to a path that did not exist.
        /// </summary>
        /// <remarks>
        /// Below we simply compare the error code with the values of ERROR_PATH_NOT_FOUND and ERROR_PATH_NOT_FOUND.
        /// The particular error depends on if the final or non-final component is missing.
        /// http://msdn.microsoft.com/en-us/library/windows/desktop/ms681382(v=vs.85).aspx
        /// </remarks>
        public bool IsNonexistent => Error == ERROR_PATH_NOT_FOUND || Error == ERROR_FILE_NOT_FOUND;

        /// <inherit />
        public bool Equals(ReportedFileAccess other)
        {
            return ManifestPath == other.ManifestPath &&
                   string.Equals(Path, other.Path, StringComparison.OrdinalIgnoreCase) &&
                   RequestedAccess == other.RequestedAccess &&
                   Status == other.Status &&
                   Process == other.Process &&
                   Error == other.Error &&
                   Usn == other.Usn &&
                   ExplicitlyReported == other.ExplicitlyReported &&
                   DesiredAccess == other.DesiredAccess &&
                   ShareMode == other.ShareMode &&
                   CreationDisposition == other.CreationDisposition &&
                   FlagsAndAttributes == other.FlagsAndAttributes &&
                   string.Equals(EnumeratePattern, other.EnumeratePattern, StringComparison.OrdinalIgnoreCase) &&
                   Method == other.Method;
        }

        /// <summary>
        /// Creates a short description of the operation and path. The following is the summary for writing bar.txt and
        /// reading bar2.txt:
        ///
        /// W c:\foo\bar.txt
        /// R c:\foo\bar2.txt
        /// </summary>
        public string ShortDescribe(PathTable pathTable)
        {
            return (IsWriteViolation ? WriteDescriptionPrefix : ReadDescriptionPrefix) + GetPath(pathTable);
        }

        /// <summary>
        /// Determines whether the current violation is a write violation
        /// </summary>
        public bool IsWriteViolation => (RequestedAccess & RequestedAccess.Write) != 0;

        /// <summary>
        /// Describes the operation that cause this reported file access, including all parameter value, except the path
        /// </summary>
        public string Describe()
        {
            using (PooledObjectWrapper<StringBuilder> wrapper = Pools.GetStringBuilder())
            {
                StringBuilder sb = wrapper.Instance;
                sb.Append('[');
                sb.Append(Process.Path);
                sb.Append(':');
                sb.Append(Process.ProcessId);
                sb.Append(']');

                if (RequestedAccess != RequestedAccess.None)
                {
                    sb.AppendFormat("({0:G})", RequestedAccess);
                }

                sb.Append(' ');

                switch (Operation)
                {
                    case ReportedFileOperation.ZwCreateFile:
                    case ReportedFileOperation.ZwOpenFile:
                    case ReportedFileOperation.NtCreateFile:
                    case ReportedFileOperation.CreateFile:
                    case ReportedFileOperation.Unknown:
                        {
                            sb.Append(Operation.ToString());
                            sb.Append("(..., ");
                            UInt32FlagsFormatter<DesiredAccess>.Append(sb, (uint)DesiredAccess);
                            sb.Append(", ");
                            UInt32FlagsFormatter<ShareMode>.Append(sb, (uint)ShareMode);
                            sb.Append(", , ");
                            UInt32EnumFormatter<CreationDisposition>.Append(sb, (uint)CreationDisposition);
                            sb.Append(", ");
                            UInt32FlagsFormatter<FlagsAndAttributes>.Append(sb, (uint)FlagsAndAttributes);
                            sb.Append(")");
                            break;
                        }

                    case ReportedFileOperation.CopyFileSource:
                        {
                            sb.Append("CopyFile([Source], ...)");
                            break;
                        }

                    case ReportedFileOperation.CopyFileDestination:
                        {
                            sb.Append("CopyFile(..., [Destination])");
                            break;
                        }

                    case ReportedFileOperation.CreateHardLinkSource:
                        {
                            sb.Append("CreateHardLink(..., [ExistingFile)");
                            break;
                        }

                    case ReportedFileOperation.CreateHardLinkDestination:
                        {
                            sb.Append("CreateHardLink([NewLink], ...)");
                            break;
                        }

                    case ReportedFileOperation.MoveFileSource:
                        {
                            sb.Append("MoveFile([Source], ...)");
                            break;
                        }

                    case ReportedFileOperation.MoveFileDestination:
                        {
                            sb.Append("MoveFile(..., [Destination])");
                            break;
                        }

                    case ReportedFileOperation.SetFileInformationByHandleSource:
                        {
                            sb.Append("SetFileInformationByHandle([Source], ...)");
                            break;
                        }

                    case ReportedFileOperation.SetFileInformationByHandleDest:
                        {
                            sb.Append("SetFileInformationByHandle(..., [Destination])");
                            break;
                        }

                    case ReportedFileOperation.ZwSetRenameInformationFileSource:
                        {
                            sb.Append("ZwSetRenameInformationFile([Source], ...)");
                            break;
                        }

                    case ReportedFileOperation.ZwSetRenameInformationFileDest:
                        {
                            sb.Append("ZwSetRenameInformationFile(..., [Destination])");
                            break;
                        }

                    case ReportedFileOperation.ZwSetFileNameInformationFileSource:
                        {
                            sb.Append("ZwSetFileNameInformationFile([Source], ...)");
                            break;
                        }

                    case ReportedFileOperation.ZwSetFileNameInformationFileDest:
                        {
                            sb.Append("ZwSetFileNameInformationFile(..., [Destination])");
                            break;
                        }

                    case ReportedFileOperation.MoveFileWithProgressSource:
                        {
                            sb.Append("MoveFileWithProgress([Source]...)");
                            break;
                        }

                    case ReportedFileOperation.MoveFileWithProgressDest:
                        {
                            sb.Append("MoveFileWithProgress([Dest]...)");
                            break;
                        }

                    case ReportedFileOperation.FindFirstFileEx:
                        {
                            sb.Append("FindFirstFileEx(...)");
                            if (RequestedAccess == RequestedAccess.Enumerate)
                            {
                                sb.Append(", ");
                                sb.Append("Enumerate Pattern:" + EnumeratePattern);
                            }

                            break;
                        }

                    default:
                        {
                            sb.Append(Enum.GetName(typeof(ReportedFileOperation), Operation)).Append("(...)");
                            break;
                        }
                }

                if (Error != 0)
                {
                    sb.AppendFormat(CultureInfo.InvariantCulture, " => (0x{0:X8}) ", Error);
                    sb.Append(NativeWin32Exception.GetFormattedMessageForNativeErrorCode(unchecked((int)Error)));
                }

                if (Usn != NoUsn)
                {
                    sb.AppendFormat(CultureInfo.InvariantCulture, " (USN 0x{0:X8}) ", Usn);
                }

                // If the status was Denied, don't include it in the description,
                // because an access that was denied by manifest may have been
                // allowed in practice (no failure injection), and the message
                // would be confusing.
                if (Status != FileAccessStatus.Denied)
                {
                    // Other modes are interesting and should be logged
                    sb.Append(" => ");
                    sb.Append(Status);
                }

                return sb.ToString();
            }
        }

        /// <summary>
        /// Gets the fully expanded path
        /// </summary>
        public string GetPath(PathTable pathTable)
        {
            return Path ?? ManifestPath.ToString(pathTable);
        }

        /// <summary>
        /// Checks if this is a special device type of path for which we should not report a warning.
        /// Make it a verbose message, so it appears in the log (for diagnosability if there are problems with such access).
        /// </summary>
        /// <returns>true if the Path reperesents a special path. Otherwise false.</returns>
        [SuppressMessage("Microsoft.Globalization", "CA1304")]
        [SuppressMessage("Microsoft.Globalization", "CA1307")]
        private bool IsSpecialDevicePath()
        {
            bool ret = false;
            if (Path != null)
            {
                // Add more special device paths here if needed.
                if (Path.ToLower().StartsWith("\\\\.\\pipe"))
                {
                    ret = true;
                }
            }

            return ret;
        }

        /// <summary>
        /// Checks if the path contains whildcard characters, for which we should not report a warning.
        /// Make it a verbose message, so it appears in the log (for diagnosability if there are problems with such access).
        /// </summary>
        /// <returns>true if the Path contains whildcard characters. Otherwise false.</returns>
        /// <remarks>
        /// We can get access message to such file if an app is probbing fot existence of files with wildcard characters.
        /// </remarks>
        [SuppressMessage("Microsoft.Globalization", "CA1304")]
        [SuppressMessage("Microsoft.Globalization", "CA1307")]
        public static bool DoesPathContainsWildcards(string path)
        {
            bool ret = false;

            if (path != null)
            {
                // Get the last part of the file name
                int lastSlash = path.LastIndexOf('\\');
                string lastComponent = lastSlash != -1 ? path.Substring(lastSlash) : path;
                if (lastComponent != null)
                {
                    // Add more special device paths here if needed.
                    if (lastComponent.Contains("?") ||
                        lastComponent.Contains("*"))
                    {
                        ret = true;
                    }
                }
            }

            return ret;
        }

        /// <summary>
        /// Attempts to parse the full path accessed to an <see cref="AbsolutePath"/>.
        /// When this succeeds, the returned path is equivalent to <see cref="GetPath"/>.
        /// In the event of parse failure that is not attributable to <c>ERROR_INVALID_NAME</c>,
        /// an event is logged to attribute the unknown path to the reporting <paramref name="pip"/>.
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
        public bool TryParseAbsolutePath(PipExecutionContext context, Process pip, out AbsolutePath parsedPath)
        {
            Contract.Requires(context != null);
            Contract.Requires(pip != null);
            Contract.Ensures(Contract.ValueAtReturn(out parsedPath).IsValid == Contract.Result<bool>());

            const int ErrorInvalidName = 0x7B; // ERROR_INVALID_NAME

            if (Path == null)
            {
                parsedPath = ManifestPath;
                return parsedPath.IsValid;
            }
            else
            {
                // Here we try to parse the path, but may fail gracefully. Sometimes tools try to open invalid paths.
                // For example, 'for /R dir %f in (*) do echo %f' in cmd may have GetFileAttributesEx("dir\*") called.
                bool parsed = AbsolutePath.TryCreate(context.PathTable, Path, out parsedPath);
                if (!parsed)
                {
                    if (Error != ErrorInvalidName)
                    {
                        // If this is opening a special (device type) path, just report it as a verbose message, so we don't lose it completely.
                        if (IsSpecialDevicePath())
                        {
                            BuildXL.Processes.Tracing.Logger.Log.PipProcessIgnoringPathOfSpecialDeviceFileAccess(
                                Events.StaticContext,
                                pip.SemiStableHash,
                                pip.GetDescription(context),
                                Describe(),
                                Path);
                        }
                        else if (DoesPathContainsWildcards(Path))
                        {
                            BuildXL.Processes.Tracing.Logger.Log.PipProcessIgnoringPathWithWildcardsFileAccess(
                                Events.StaticContext,
                                pip.SemiStableHash,
                                pip.GetDescription(context),
                                Describe(),
                                Path);
                        }
                        else
                        {
                            BuildXL.Processes.Tracing.Logger.Log.PipProcessFailedToParsePathOfFileAccess(
                                Events.StaticContext,
                                pip.SemiStableHash,
                                pip.GetDescription(context),
                                Describe(),
                                Path);
                        }
                    }
                }

                return parsed;
            }
        }

        /// <summary>
        /// Creates an instance from an absolute path.
        /// </summary>
        [SuppressMessage("Microsoft.Naming", "CA1726:UsePreferredTerms", MessageId = "flags")]
        public static ReportedFileAccess Create(
            ReportedFileOperation operation,
            ReportedProcess process,
            RequestedAccess requestedAccess,
            FileAccessStatus status,
            bool explicitlyReported,
            uint error,
            Usn usn,
            DesiredAccess desiredAccess,
            ShareMode shareMode,
            CreationDisposition creationDisposition,
            FlagsAndAttributes flagsAndAttributes,
            AbsolutePath path,
            string enumeratePattern = null)
        {
            Contract.Requires(process != null);

            return new ReportedFileAccess(
                operation,
                process,
                requestedAccess,
                status,
                explicitlyReported,
                error,
                usn,
                desiredAccess,
                shareMode,
                creationDisposition,
                flagsAndAttributes,
                path,
                null,
                enumeratePattern);
        }

        /// <summary>
        /// Creates an instance from a full path, trying to look up a matching absolute path from the path table
        /// </summary>
        [SuppressMessage("Microsoft.Naming", "CA1726:UsePreferredTerms", MessageId = "flags")]
        public static ReportedFileAccess Create(
            ReportedFileOperation operation,
            ReportedProcess process,
            RequestedAccess requestedAccess,
            FileAccessStatus status,
            bool explicitlyReported,
            uint error,
            Usn usn,
            DesiredAccess desiredAccess,
            ShareMode shareMode,
            CreationDisposition creationDisposition,
            FlagsAndAttributes flagsAndAttributes,
            PathTable pathTable,
            string path,
            string enumeratePattern = null)
        {
            Contract.Requires(process != null);
            Contract.Requires(pathTable != null);
            Contract.Requires(path != null);

            AbsolutePath absolutePath;
            if (AbsolutePath.TryGet(pathTable, (StringSegment)path, out absolutePath))
            {
                return new ReportedFileAccess(
                    operation,
                    process,
                    requestedAccess,
                    status,
                    explicitlyReported,
                    error,
                    usn,
                    desiredAccess,
                    shareMode,
                    creationDisposition,
                    flagsAndAttributes,
                    absolutePath,
                    null,
                    enumeratePattern);
            }

            return new ReportedFileAccess(
                operation,
                process,
                requestedAccess,
                status,
                explicitlyReported,
                error,
                usn,
                desiredAccess,
                shareMode,
                creationDisposition,
                flagsAndAttributes,
                AbsolutePath.Invalid,
                path,
                enumeratePattern);
        }

        /// <nodoc />
        public void Serialize(
            BuildXLWriter writer,
            Dictionary<ReportedProcess, int> processMap,
            Action<BuildXLWriter, AbsolutePath> writePath)
        {
            writer.Write((byte)Operation);

            if (processMap != null && processMap.TryGetValue(Process, out int index))
            {
                writer.WriteCompact(index);
            }
            else
            {
                Process.Serialize(writer);
            }

            writer.WriteCompact((int)RequestedAccess);
            writer.WriteCompact((int)Status);
            writer.Write(ExplicitlyReported);
            writer.Write(Error);
            writer.Write(Usn.Value);
            writer.Write((uint)DesiredAccess);
            writer.Write((uint)ShareMode);
            writer.Write((uint)CreationDisposition);
            writer.Write((uint)FlagsAndAttributes);

            if (writePath != null)
            {
                writePath(writer, ManifestPath);
            }
            else
            {
                writer.Write(ManifestPath);
            }

            writer.WriteNullableString(Path);
            writer.WriteNullableString(EnumeratePattern);
            writer.Write((byte)Method);
        }

        /// <nodoc />
        public static ReportedFileAccess Deserialize(
            BuildXLReader reader, 
            IReadOnlyList<ReportedProcess> processes, 
            Func<BuildXLReader, AbsolutePath> readPath)
        {
            return new ReportedFileAccess(
                operation: (ReportedFileOperation)reader.ReadByte(),
                process: processes != null ? processes[reader.ReadInt32Compact()] : ReportedProcess.Deserialize(reader),
                requestedAccess: (RequestedAccess)reader.ReadInt32Compact(),
                status: (FileAccessStatus)reader.ReadInt32Compact(),
                explicitlyReported: reader.ReadBoolean(),
                error: reader.ReadUInt32(),
                // In general if process is executed externally, e.g., in VM, the obtained USN cannot be translated to the host.
                // However, for our low-privilege build, we are going to map the host volumes to the VM, and thus the USN
                // can still be used.
                usn: new Usn(reader.ReadUInt64()),
                desiredAccess: (DesiredAccess)reader.ReadUInt32(),
                shareMode: (ShareMode)reader.ReadUInt32(),
                creationDisposition: (CreationDisposition)reader.ReadUInt32(),
                flagsAndAttributes: (FlagsAndAttributes)reader.ReadUInt32(),
                manifestPath: readPath != null ? readPath(reader) : reader.ReadAbsolutePath(),
                path: reader.ReadNullableString(),
                enumeratePatttern: reader.ReadNullableString(),
                fileAccessStatusMethod: (FileAccessStatusMethod)reader.ReadByte());
        }

        /// <inherit />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inherit />
        public override int GetHashCode()
        {
            unchecked
            {
                return HashCodeHelper.Combine(
                    string.IsNullOrEmpty(Path) ? ManifestPath.GetHashCode() : StringComparer.OrdinalIgnoreCase.GetHashCode(Path),
                    string.IsNullOrEmpty(EnumeratePattern) ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(EnumeratePattern),
                    Process != null ? (int)Process.ProcessId : 0,
                    (int)RequestedAccess,
                    (int)Status,
                    (int)Error,
                    Usn.GetHashCode(),
                    (int)DesiredAccess,
                    (int)ShareMode,
                    (int)CreationDisposition,
                    (int)FlagsAndAttributes);
            }
        }

        /// <summary>
        /// Checks whether two file access violations are the same.
        /// </summary>
        public static bool operator ==(ReportedFileAccess left, ReportedFileAccess right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Checks whether two file access violations are different.
        /// </summary>
        public static bool operator !=(ReportedFileAccess left, ReportedFileAccess right)
        {
            return !left.Equals(right);
        }

        /// <summary>
        /// Gets the name of enums by value.
        /// </summary>
        private static class UInt32EnumFormatter<TEnum>
        {
            public static void Append(StringBuilder sb, uint value)
            {
                Contract.Assume(typeof(TEnum).GetTypeInfo().IsEnum);
                Contract.Assume(typeof(TEnum).GetTypeInfo().GetEnumUnderlyingType() == typeof(uint));
                sb.Append((TEnum)(object)value);
            }
        }

        /// <summary>
        /// Fast flags enum formatter that separates bits with '|'
        /// </summary>
        private static class UInt32FlagsFormatter<TEnum>
        {
            private static readonly string[] s_names = GetNames();

            [SuppressMessage("Microsoft.Performance", "CA1810:InitializeReferenceTypeStaticFieldsInline")]
            static UInt32FlagsFormatter()
            {
                Contract.Assume(typeof(TEnum).GetTypeInfo().IsEnum);
                Contract.Assume(typeof(TEnum).GetTypeInfo().GetEnumUnderlyingType() == typeof(uint));
            }

            private static string[] GetNames()
            {
                var names = new string[32];
                int k = 1;
                for (int j = 0; j < 32; j++, k <<= 1)
                {
                    names[j] = unchecked(((TEnum)(object)(uint)k).ToString());
                }

                return names;
            }

            public static void Append(StringBuilder sb, uint value)
            {
                var i = unchecked((int)value);
                bool first = true;
                int k = 1;
                for (int j = 0; j < 32; j++, k <<= 1)
                {
                    if ((i & k) != 0)
                    {
                        if (first)
                        {
                            first = false;
                        }
                        else
                        {
                            sb.Append('|');
                        }

                        sb.Append(s_names[j]);
                    }
                }

                if (first)
                {
                    sb.Append(0);
                }
            }
        }
    }
}
