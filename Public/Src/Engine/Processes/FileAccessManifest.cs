// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BuildXL.Native.IO;
using BuildXL.Native.Processes;
using BuildXL.Utilities;
using JetBrains.Annotations;

namespace BuildXL.Processes
{
    /// <summary>
    /// Describes the set of files that a process may access, and the kind of operations that are permitted
    /// for each file.
    /// </summary>
    public sealed class FileAccessManifest
    {
        private readonly Dictionary<StringId, NormalizedPathString> m_normalizedFragments = new Dictionary<StringId, NormalizedPathString>();
        private readonly PathTable m_pathTable;

        /// <summary>
        /// Represents the invalid scope.
        /// </summary>
        private readonly Node m_rootNode;

        /// <summary>
        /// File access manifest flags.
        /// </summary>
        private FileAccessManifestFlag m_fileAccessManifestFlag;

        /// <summary>
        /// Name of semaphore for message count.
        /// </summary>
        private string m_messageCountSemaphoreName;

        /// <summary>
        /// Sealed manifest tree.
        /// </summary>
        private byte[] m_sealedManifestTreeBlock;

        /// <summary>
        /// Creates an empty instance.
        /// </summary>
        public FileAccessManifest(PathTable pathTable, DirectoryTranslator translateDirectories = null)
        {
            Contract.Requires(pathTable != null);

            m_pathTable = pathTable;
            m_rootNode = Node.CreateRootNode();
            DirectoryTranslator = translateDirectories;

            // We are pedantic by default. It's otherwise easy to silently do nothing in e.g. tests.
            FailUnexpectedFileAccesses = true;
            ReportUnexpectedFileAccesses = true;
            ReportFileAccesses = false;
            MonitorChildProcesses = true;
            MonitorNtCreateFile = false;
            MonitorZwCreateOpenQueryFile = false;
            ReportProcessArgs = false;
            ForceReadOnlyForRequestedReadWrite = false;
            IgnoreReparsePoints = false;
            IgnorePreloadedDlls = true; // TODO: Change this when customers onboard the feature.
            DisableDetours = false;
            IgnoreZwRenameFileInformation = false;
            IgnoreZwOtherFileInformation = false;
            IgnoreNonCreateFileReparsePoints = false;
            IgnoreSetFileInformationByHandle = false;
            NormalizeReadTimestamps = true;
            UseLargeNtClosePreallocatedList = false;
            UseExtraThreadToDrainNtClose = true;
            LogProcessData = false;
            IgnoreGetFinalPathNameByHandle = true;
            LogProcessDetouringStatus = false;
            HardExitOnErrorInDetours = false;
            CheckDetoursMessageCount = false;
            InternalDetoursErrorNotificationFile = string.Empty;
            QBuildIntegrated = false;
            PipId = 0L;
            EnforceAccessPoliciesOnDirectoryCreation = false;
        }

        private bool GetFlag(FileAccessManifestFlag flag) => (m_fileAccessManifestFlag & flag) != 0;

        /// <summary>
        /// Gets file access manifest flag.
        /// </summary>
        internal FileAccessManifestFlag Flag => m_fileAccessManifestFlag;

        private void SetFlag(FileAccessManifestFlag flag, bool value)
        {
            if (value)
            {
                m_fileAccessManifestFlag |= flag;
            }
            else
            {
                m_fileAccessManifestFlag &= ~flag;
            }
        }

        /// <summary>
        /// Flag indicating if the manifest tree block is sealed.
        /// </summary>
        public bool IsManifestTreeBlockSealed => m_sealedManifestTreeBlock != null;

        /// <summary>
        /// If true, then the detoured file access functions will write diagnostic messages
        /// to stderr when access to files is prevented.
        /// </summary>
        public bool EnableDiagnosticMessages
        {
            get => GetFlag(FileAccessManifestFlag.DiagnosticMessagesEnabled);
            set => SetFlag(FileAccessManifestFlag.DiagnosticMessagesEnabled, value);
        }

        /// <summary>
        /// If true, then detoured file access functions will treat invalid file access as an
        /// error, and will return an error code to the caller.
        /// If false, then detoured file access functions will treat invalid file access as a
        /// warning, but will still allow the file access to occur.
        /// </summary>
        public bool FailUnexpectedFileAccesses
        {
            get => GetFlag(FileAccessManifestFlag.FailUnexpectedFileAccesses);
            set => SetFlag(FileAccessManifestFlag.FailUnexpectedFileAccesses, value);
        }

        /// <summary>
        /// If true, the detoured process won't be downgrading CreateDirectory call to read-only probe
        /// in cases when the directory already exists.
        /// </summary>
        public bool EnforceAccessPoliciesOnDirectoryCreation
        {
            get => GetFlag(FileAccessManifestFlag.EnforceAccessPoliciesOnDirectoryCreation);
            set => SetFlag(FileAccessManifestFlag.EnforceAccessPoliciesOnDirectoryCreation, value);
        }

        /// <summary>
        /// If true, then the detoured process will execute the Win32 DebugBreak() function
        /// when the process attempts to access a file that is outside of its permitted scope.
        /// </summary>
        public bool BreakOnUnexpectedAccess
        {
            get => GetFlag(FileAccessManifestFlag.BreakOnAccessDenied);
            set => SetFlag(FileAccessManifestFlag.BreakOnAccessDenied, value);
        }

        /// <summary>
        /// If true, then all file accesses will be recorded
        /// </summary>
        public bool ReportFileAccesses
        {
            get => GetFlag(FileAccessManifestFlag.ReportFileAccesses);
            set => SetFlag(FileAccessManifestFlag.ReportFileAccesses, value);
        }

        /// <summary>
        /// If true, then all unexpected file accesses will be recorded
        /// </summary>
        public bool ReportUnexpectedFileAccesses
        {
            get => GetFlag(FileAccessManifestFlag.ReportUnexpectedFileAccesses);
            set => SetFlag(FileAccessManifestFlag.ReportUnexpectedFileAccesses, value);
        }

        /// <summary>
        /// If true, then nested processes will be monitored just like the main process
        /// </summary>
        public bool MonitorChildProcesses
        {
            get => GetFlag(FileAccessManifestFlag.MonitorChildProcesses);
            set => SetFlag(FileAccessManifestFlag.MonitorChildProcesses, value);
        }

        /// <summary>
        /// Monitor files opened for read by NtCreateFile
        /// TODO: This should be a temporary hack until we fix all places broken by the NtCreateFile monitoring
        /// </summary>
        public bool MonitorNtCreateFile
        {
            get => GetFlag(FileAccessManifestFlag.MonitorNtCreateFile);
            set => SetFlag(FileAccessManifestFlag.MonitorNtCreateFile, value);
        }

        /// <summary>
        /// Monitor files opened for read by ZwCreateOpenQueryFile
        /// </summary>
        public bool MonitorZwCreateOpenQueryFile
        {
            get => GetFlag(FileAccessManifestFlag.MonitorZwCreateOpenQueryFile);
            set => SetFlag(FileAccessManifestFlag.MonitorZwCreateOpenQueryFile, value);
        }

        /// <summary>
        /// If true, force read only for requested read-write access so long as the tool is allowed to read.
        /// </summary>
        public bool ForceReadOnlyForRequestedReadWrite
        {
            get => GetFlag(FileAccessManifestFlag.ForceReadOnlyForRequestedReadWrite);
            set => SetFlag(FileAccessManifestFlag.ForceReadOnlyForRequestedReadWrite, value);
        }

        /// <summary>
        /// If true, allows detouring the ZwRenameFileInformation API.
        /// </summary>
        public bool IgnoreZwRenameFileInformation
        {
            get => GetFlag(FileAccessManifestFlag.IgnoreZwRenameFileInformation);
            set => SetFlag(FileAccessManifestFlag.IgnoreZwRenameFileInformation, value);
        }

        /// <summary>
        /// If true, allows detouring the ZwOtherFileInformation API.
        /// </summary>
        public bool IgnoreZwOtherFileInformation
        {
            get => GetFlag(FileAccessManifestFlag.IgnoreZwOtherFileInformation);
            set => SetFlag(FileAccessManifestFlag.IgnoreZwOtherFileInformation, value);
        }

        /// <summary>
        /// If true, allows following symlinks for Non CreateFile APIs.
        /// </summary>
        public bool IgnoreNonCreateFileReparsePoints
        {
            get => GetFlag(FileAccessManifestFlag.IgnoreNonCreateFileReparsePoints);
            set => SetFlag(FileAccessManifestFlag.IgnoreNonCreateFileReparsePoints, value);
        }

        /// <summary>
        /// If true, allows detouring the SetFileInformationByHandle API.
        /// </summary>
        public bool IgnoreSetFileInformationByHandle
        {
            get => GetFlag(FileAccessManifestFlag.IgnoreSetFileInformationByHandle);
            set => SetFlag(FileAccessManifestFlag.IgnoreSetFileInformationByHandle, value);
        }

        /// <summary>
        /// If true, allows following reparse points without enforcing any file access rules.
        /// </summary>
        public bool IgnoreReparsePoints
        {
            get => GetFlag(FileAccessManifestFlag.IgnoreReparsePoints);
            set => SetFlag(FileAccessManifestFlag.IgnoreReparsePoints, value);
        }

        /// <summary>
        /// If true, allows enforcing file access rules for preloaded (statically) loaded Dlls.
        /// </summary>
        public bool IgnorePreloadedDlls
        {
            get => GetFlag(FileAccessManifestFlag.IgnorePreloadedDlls);
            set => SetFlag(FileAccessManifestFlag.IgnorePreloadedDlls, value);
        }

        /// <summary>
        /// If true, disables detouring any file access APIs, which will lead to incorrect builds.
        /// </summary>
        public bool DisableDetours
        {
            get => GetFlag(FileAccessManifestFlag.DisableDetours);
            set => SetFlag(FileAccessManifestFlag.DisableDetours, value);
        }

        /// <summary>
        /// If true, ignore certain files that are accessed as a side-effect of code coverage collection
        /// </summary>
        public bool IgnoreCodeCoverage
        {
            get => GetFlag(FileAccessManifestFlag.IgnoreCodeCoverage);
            set => SetFlag(FileAccessManifestFlag.IgnoreCodeCoverage, value);
        }

        /// <summary>
        /// If true, the command line arguments of all processes (including child processes) launched by the pip will be reported
        /// </summary>
        public bool ReportProcessArgs
        {
            get => GetFlag(FileAccessManifestFlag.ReportProcessArgs);
            set => SetFlag(FileAccessManifestFlag.ReportProcessArgs, value);
        }

        /// <summary>
        /// If true, all file reads will get a consistent timestamp. When false, actual timestamps will be allowed to flow
        /// through, as long as they are newer than the static <see cref="WellKnownTimestamps.NewInputTimestamp"/> used for enforcing rewrite order.
        /// </summary>
        public bool NormalizeReadTimestamps
        {
            get => GetFlag(FileAccessManifestFlag.NormalizeReadTimestamps);
            set => SetFlag(FileAccessManifestFlag.NormalizeReadTimestamps, value);
        }

        /// <summary>
        /// Whether BuildXL will use larger NtClose preallocated list.
        /// </summary>
        public bool UseLargeNtClosePreallocatedList
        {
            get => GetFlag(FileAccessManifestFlag.UseLargeNtClosePreallocatedList);
            set => SetFlag(FileAccessManifestFlag.UseLargeNtClosePreallocatedList, value);
        }

        /// <summary>
        /// Whether BuildXL will use extra thread to drain NtClose handle List or clean the cache directly.
        /// </summary>
        public bool UseExtraThreadToDrainNtClose
        {
            get => GetFlag(FileAccessManifestFlag.UseExtraThreadToDrainNtClose);
            set => SetFlag(FileAccessManifestFlag.UseExtraThreadToDrainNtClose, value);
        }

        /// <summary>
        /// Determines whether BuildXL will collect proccess execution data, kernel/user mode time and IO counts.
        /// </summary>
        public bool LogProcessData
        {
            get => GetFlag(FileAccessManifestFlag.LogProcessData);
            set => SetFlag(FileAccessManifestFlag.LogProcessData, value);
        }

        /// <summary>
        /// If true, don't detour the GetFinalPathNameByHandle API.
        /// </summary>
        public bool IgnoreGetFinalPathNameByHandle
        {
            get => GetFlag(FileAccessManifestFlag.IgnoreGetFinalPathNameByHandle);
            set => SetFlag(FileAccessManifestFlag.IgnoreGetFinalPathNameByHandle, value);
        }

        /// <summary>
        /// If true it enables logging of Detouring status.
        /// </summary>
        public bool LogProcessDetouringStatus
        {
            get => GetFlag(FileAccessManifestFlag.LogProcessDetouringStatus);
            set => SetFlag(FileAccessManifestFlag.LogProcessDetouringStatus, value);
        }

        /// <summary>
        /// If true it enables hard exiting on error with special exit code.
        /// </summary>
        public bool HardExitOnErrorInDetours
        {
            get => GetFlag(FileAccessManifestFlag.HardExitOnErrorInDetours);
            set => SetFlag(FileAccessManifestFlag.HardExitOnErrorInDetours, value);
        }

        /// <summary>
        /// If true it enables logging of Detouring status.
        /// </summary>
        public bool CheckDetoursMessageCount
        {
            get => GetFlag(FileAccessManifestFlag.CheckDetoursMessageCount);
            set => SetFlag(FileAccessManifestFlag.CheckDetoursMessageCount, value);
        }

        /// <summary>
        /// True when the sandbox is integrated in QBuild. False otherwise.
        /// </summary>
        /// <remarks>Note: this is only an option that is set programmatically. Not controlled by a command line option.</remarks>
        public bool QBuildIntegrated
        {
            get => GetFlag(FileAccessManifestFlag.QBuildIntegrated);
            set => SetFlag(FileAccessManifestFlag.QBuildIntegrated, value);
        }

        /// <summary>
        /// A location for a file where Detours to log failure messages.
        /// </summary>
        public string InternalDetoursErrorNotificationFile { get; set; }

        /// <summary>
        /// The semaphore that keeps count to the sent and received messages.
        /// </summary>
        public System.Threading.Semaphore MessageCountSemaphore { get; private set; }

        /// <summary>
        /// Directory translator.
        /// </summary>
        public DirectoryTranslator DirectoryTranslator { get; }

        /// <summary>
        /// The pip's unique id.
        /// Right now it is the Pip.SemiStableHash.
        /// </summary>
        public long PipId { get; set; }

        /// <summary>
        /// Sets message count semaphore.
        /// </summary>
        public bool SetMessageCountSemaphore(string semaphoreName)
        {
            Contract.Requires(!string.IsNullOrEmpty(semaphoreName));

            UnsetMessageCountSemaphore();
            m_messageCountSemaphoreName = semaphoreName;
            MessageCountSemaphore = new System.Threading.Semaphore(0, int.MaxValue, semaphoreName, out bool newlyCreated);

            return newlyCreated;
        }

        /// <summary>
        /// Unset message count semaphore.
        /// </summary>
        public void UnsetMessageCountSemaphore()
        {
            if (MessageCountSemaphore == null)
            {
                Contract.Assert(m_messageCountSemaphoreName == null);
                return;
            }

            MessageCountSemaphore.Dispose();
            MessageCountSemaphore = null;
            m_messageCountSemaphoreName = null;
        }

        /// <summary>
        /// Optional child substitute shim execution information.
        /// </summary>
        /// <remarks>
        /// Internal since this is set as a side effect of initializing a FileAccessManifest
        /// in <see cref="SandboxedProcessInfo"/>.
        /// </remarks>
        [CanBeNull]
        public SubstituteProcessExecutionInfo SubstituteProcessExecutionInfo { get; set; }

        /// <summary>
        /// Adds a policy to an entire scope
        /// </summary>
        public void AddScope(AbsolutePath path, FileAccessPolicy mask, FileAccessPolicy values)
        {
            Contract.Requires(!IsManifestTreeBlockSealed);

            if (!path.IsValid)
            {
                // Note that AbsolutePath.Invalid is allowable (representing the root scope).
                m_rootNode.ApplyRootScope(new FileAccessScope(mask, values));
                return;
            }

            m_rootNode.AddConeWithScope(this, path, new FileAccessScope(mask, values));
        }

        /// <summary>
        /// Adds a policy to an individual path (without implying any policy to the scope the path may define)
        /// </summary>
        public void AddPath(AbsolutePath path, FileAccessPolicy mask, FileAccessPolicy values, Usn? expectedUsn = null)
        {
            Contract.Requires(!IsManifestTreeBlockSealed);
            Contract.Requires(path != AbsolutePath.Invalid);

            m_rootNode.AddNodeWithScope(this, path, new FileAccessScope(mask, values), expectedUsn ?? ReportedFileAccess.NoUsn);
        }

        private void WriteAnyBuildShimBlock(BinaryWriter writer)
        {
#if DEBUG
            writer.Write(0xABCDEF04); // "ABCDEF04"
#endif

            if (SubstituteProcessExecutionInfo == null)
            {
                writer.Write((uint)0);  // ShimAllProcesses false value.

                // Emit a zero-length substituteProcessExecShimPath when substitution is turned off.
                WriteChars(writer, null);
                return;
            }

            writer.Write(SubstituteProcessExecutionInfo.ShimAllProcesses ? (uint)1 : (uint)0);
            WriteChars(writer, SubstituteProcessExecutionInfo.SubstituteProcessExecutionShimPath.ToString(m_pathTable));
            writer.Write((uint)SubstituteProcessExecutionInfo.ShimProcessMatches.Count);

            if (SubstituteProcessExecutionInfo.ShimProcessMatches.Count > 0)
            {
                foreach (ShimProcessMatch match in SubstituteProcessExecutionInfo.ShimProcessMatches)
                {
                    WriteChars(writer, match.ProcessName.ToString(m_pathTable.StringTable));
                    WriteChars(writer, match.ArgumentMatch.IsValid ? match.ArgumentMatch.ToString(m_pathTable.StringTable) : null);
                }
            }
        }
        
        // See unmanaged decoder at DetoursHelpers.cpp :: CreateStringFromWriteChars()
        private static void WriteChars(BinaryWriter writer, string str)
        {
            var strLen = (uint)(string.IsNullOrEmpty(str) ? 0 : str.Length);
            writer.Write(strLen);
            for (var i = 0; i < strLen; i++)
            {
                writer.Write((char)str[i]);
            }
        }

        private static string ReadChars(BinaryReader reader)
        {
            uint length = reader.ReadUInt32();
            if (length == 0)
            {
                // TODO: Make ReadChars and WriteChars symmetric.
                // Note that there's asymmetry between WriteChars and ReadChars.
                // WriteChars has existed for long time and is used to serialize string to Detours.
                // ReadChars is introduced as a way to reuse WriteChars when BuildXL has to serialize/deserialize
                // manifest to files. We can make WriteChars and ReadChars symmetric, as well as, optimize the encoding.
                // However, such changes entails changing Detours code.
                return null;
            }

            char[] chars = new char[length];

            for (int i = 0; i < chars.Length; ++i)
            {
                chars[i] = reader.ReadChar();
            }

            return new string(chars);
        }

        private static void WriteInjectionTimeoutBlock(BinaryWriter writer, uint timeoutInMins)
        {
            writer.Write((uint)timeoutInMins);
        }

        private const uint ErrorDumpLocationCheckedCode = 0xABCDEF03;
        private const uint TranslationPathStringCheckedCode = 0xABCDEF02;
        private const uint FlagsCheckedCode = 0xF1A6B10C; // Flag block
        private const uint PipIdCheckedCode = 0xF1A6B10E;

        [SuppressMessage("Microsoft.Globalization", "CA1308:NormalizeStringsToUppercase", Justification = "Architecture strings are USASCII")]
        private static void WriteErrorDumpLocation(BinaryWriter writer, string internalDetoursErrorNotificationFile)
        {
#if DEBUG
            writer.Write(ErrorDumpLocationCheckedCode);
#endif
            WriteChars(writer, internalDetoursErrorNotificationFile);
        }

        private static string ReadErrorDumpLocation(BinaryReader reader)
        {
#if DEBUG
            uint code = reader.ReadUInt32();
            Contract.Assert(ErrorDumpLocationCheckedCode == code);
#endif
            return ReadChars(reader);
        }

        [SuppressMessage("Microsoft.Globalization", "CA1308:NormalizeStringsToUppercase", Justification = "Architecture strings are USASCII")]
        private static void WriteTranslationPathStrings(BinaryWriter writer, DirectoryTranslator translatePaths)
        {
#if DEBUG
            writer.Write(TranslationPathStringCheckedCode);
#endif

            // Write the number of translation paths.
            uint translatePathLen = (uint)(translatePaths?.Count ?? 0);
            writer.Write(translatePathLen);

            if (translatePathLen > 0)
            {
                Contract.Assert(translatePaths != null);

                foreach (DirectoryTranslator.Translation translation in translatePaths.Translations)
                {
                    WriteChars(writer, translation.SourcePath);
                    WriteChars(writer, translation.TargetPath);
                }
            }
        }

        private static DirectoryTranslator ReadTranslationPathStrings(BinaryReader reader)
        {
#if DEBUG
            uint code = reader.ReadUInt32();
            Contract.Assert(TranslationPathStringCheckedCode == code);
#endif

            uint length = reader.ReadUInt32();

            if (length == 0)
            {
                return null;
            }

            DirectoryTranslator directoryTranslator = new DirectoryTranslator();

            for (int i = 0; i < length; ++i)
            {
                string source = ReadChars(reader);
                string target = ReadChars(reader);
                directoryTranslator.AddTranslation(source, target);
            }

            directoryTranslator.Seal();

            return directoryTranslator;
        }

        [SuppressMessage("Microsoft.Naming", "CA2204:LiteralsShouldBeSpelledCorrectly")]
        private static void WriteDebugFlagBlock(BinaryWriter writer, ref bool debugFlagsMatch)
        {
            debugFlagsMatch = true;
#if DEBUG
            // "debug 1 (on)"
            writer.Write((uint)0xDB600001);
            if (!ProcessUtilities.IsNativeInDebugConfiguration())
            {
                BuildXL.Processes.Tracing.Logger.Log.PipInvalidDetoursDebugFlag1(BuildXL.Utilities.Tracing.Events.StaticContext);
                debugFlagsMatch = false;
            }
#else
            // "debug 0 (off)"
            writer.Write((uint)0xDB600000);
            if (ProcessUtilities.IsNativeInDebugConfiguration())
            {
                BuildXL.Processes.Tracing.Logger.Log.PipInvalidDetoursDebugFlag2(BuildXL.Utilities.Tracing.Events.StaticContext);
                debugFlagsMatch = false;
            }
#endif
        }

        private static void WriteFlagsBlock(BinaryWriter writer, FileAccessManifestFlag flags)
        {
#if DEBUG
            writer.Write(FlagsCheckedCode);
#endif

            writer.Write((uint)flags);
        }

        private static FileAccessManifestFlag ReadFlagsBlock(BinaryReader reader)
        {
#if DEBUG
            uint code = reader.ReadUInt32();
            Contract.Assert(FlagsCheckedCode == code);
#endif

            return (FileAccessManifestFlag)reader.ReadUInt32();
        }

        // Allow for future expansions with 64 more flags (in case they become needed))
        private static void WriteExtraFlagsBlock(BinaryWriter writer, FileAccessManifestExtraFlag extraFlags)
        {
#if DEBUG
            writer.Write((uint)0xF1A6B10D); // "extra flags block"
#endif
            writer.Write((uint)extraFlags);
        }

        private static void WritePipId(BinaryWriter writer, long pipId)
        {
#if DEBUG
            writer.Write(PipIdCheckedCode);
            writer.Write(0); // Padding. Needed to keep the data properly aligned for the C/C++ compiler.
#endif
            // The PipId is needed for reporting purposes on non Windows OSs. Write it here.
            writer.Write(pipId);
        }

        private static long ReadPipId(BinaryReader reader)
        {
#if DEBUG
            uint code = reader.ReadUInt32();
            Contract.Assert(PipIdCheckedCode == code);

            int zero = reader.ReadInt32();
            Contract.Assert(0 == zero);
#endif
            return reader.ReadInt64();
        }

        private static void WriteReportBlock(BinaryWriter writer, FileAccessSetup setup)
        {
#if DEBUG
            writer.Write((uint)0xFEEDF00D); // "feed food"
#endif
            // since the number of bytes in this block is always going to be even, use the bottom bit
            // to indicate whether it is a handle number (1) or a path (0)
            if (!string.IsNullOrEmpty(setup.ReportPath))
            {
                if (setup.ReportPath[0] == '#')
                {
                    uint size = sizeof(int); // there will be a 4 byte handle value following

                    string handleString = setup.ReportPath.Substring(1);

                    int handleValue32bit;
                    bool parseSuccess = int.TryParse(handleString, NumberStyles.Integer, CultureInfo.InvariantCulture, out handleValue32bit);
                    if (!parseSuccess)
                    {
                        throw Contract.AssertFailure(
                            "Win64 file handles are supposed to be signed 32-bit integers; if they were not, we'd have a problem monitoring 32-bit processes from 64-bit.");
                    }

                    size |= 0x01; // set bottom bit to indicate that the value is an integer

                    writer.Write((uint)size);
                    writer.Write((int)handleValue32bit);
                }
                else
                {
                    var encoding = OperatingSystemHelper.IsUnixOS ? Encoding.UTF8 : Encoding.Unicode;
                    var report = new PaddedByteString(encoding, setup.ReportPath);
                    var length = report.Length;
                    Contract.Assume(length >= 0);
                    Contract.Assume((length & 0x01) == 0);
                    writer.Write((uint)length);
                    report.Serialize(writer);
                }
            }
            else
            {
                // if there was no report line, we still have the report block, so write 0 for the size
                writer.Write(0U);
            }
        }

        private static void WriteDllBlock(BinaryWriter writer, FileAccessSetup setup)
        {
#if DEBUG
            writer.Write((uint)0xD11B10CC); // "dll block"
#endif

            // order has to match order in DetoursServices.cpp / ParseFileAccessManifest.
            // $Note(bxl-team): These dll strings MUST be ASCII because IMAGE_EXPORT_DIRECTORY used by Detours only supports ASCII.
            PaddedByteString[] dlls =
            {
                new PaddedByteString(Encoding.ASCII, setup.DllNameX86),
                new PaddedByteString(Encoding.ASCII, setup.DllNameX64),
            };

            uint totalSize = 0;
            foreach (PaddedByteString dll in dlls)
            {
                totalSize = checked(totalSize + (uint)dll.Length);
            }

            writer.Write(totalSize);
            uint dllCount = (uint)dlls.Length;
            writer.Write(dllCount);

            uint offset = 0;
            foreach (PaddedByteString dll in dlls)
            {
                writer.Write(offset);
                offset = checked(offset + (uint)dll.Length);
            }

            foreach (PaddedByteString dll in dlls)
            {
                dll.Serialize(writer);
            }
        }

        private void WriteManifestTreeBlock(BinaryWriter writer)
        {
            if (m_sealedManifestTreeBlock != null)
            {
                writer.Write(m_sealedManifestTreeBlock);
            }
            else
            {
                m_rootNode.InternalSerialize(default(NormalizedPathString), writer);
            }
        }

        /// <summary>
        /// Creates, as an Unicode encoded byte array, an expanded textual representation of the manifest, as consumed by the
        /// native detour implementation
        /// </summary>
        [SuppressMessage("Microsoft.Usage", "CA2202:Do not dispose objects multiple times")]
        public ArraySegment<byte> GetPayloadBytes(FileAccessSetup setup, MemoryStream stream, uint timeoutMins, ref bool debugFlagsMatch)
        {
            Contract.Requires(setup != null);
            Contract.Requires(stream != null);
            stream.Position = 0;
            using (var writer = new BinaryWriter(stream, Encoding.Unicode, true))
            {
                WriteDebugFlagBlock(writer, ref debugFlagsMatch);
                WriteInjectionTimeoutBlock(writer, timeoutMins);
                WriteTranslationPathStrings(writer, DirectoryTranslator);
                WriteErrorDumpLocation(writer, InternalDetoursErrorNotificationFile);
                WriteFlagsBlock(writer, m_fileAccessManifestFlag);
                WriteExtraFlagsBlock(writer, FileAccessManifestExtraFlag.None);
                WritePipId(writer, PipId);
                WriteReportBlock(writer, setup);
                WriteDllBlock(writer, setup);
                WriteAnyBuildShimBlock(writer);
                WriteManifestTreeBlock(writer);

                return new ArraySegment<byte>(stream.GetBuffer(), 0, (int)stream.Position);
            }
        }

        /// <summary>
        /// Serializes this manifest.
        /// </summary>
        public void Serialize(Stream stream)
        {
            Contract.Requires(stream != null);

            using (var writer = new BinaryWriter(stream, Encoding.Unicode, true))
            {
                WriteTranslationPathStrings(writer, DirectoryTranslator);
                WriteErrorDumpLocation(writer, InternalDetoursErrorNotificationFile);
                WriteFlagsBlock(writer, m_fileAccessManifestFlag);
                WritePipId(writer, PipId);
                WriteChars(writer, m_messageCountSemaphoreName);

                // The manifest tree block has to be serialized the last.
                WriteManifestTreeBlock(writer);
            }
        }

        /// <summary>
        /// Deserialize an instance of <see cref="FileAccessManifest"/> from stream.
        /// </summary>
        public static FileAccessManifest Deserialize(Stream stream)
        {
            Contract.Requires(stream != null);

            using (var reader = new BinaryReader(stream, Encoding.Unicode, true))
            {
                DirectoryTranslator directoryTranslator = ReadTranslationPathStrings(reader);
                string internalDetoursErrorNotificationFile = ReadErrorDumpLocation(reader);
                FileAccessManifestFlag fileAccessManifestFlag = ReadFlagsBlock(reader);
                long pipId = ReadPipId(reader);
                string messageCountSemaphoreName = ReadChars(reader);

                byte[] sealedManifestTreeBlock;

                // TODO: Check perf. a) if this is a good buffer size, b) if the buffers should be pooled (now they are just allocated and thrown away)
                using (MemoryStream ms = new MemoryStream(4096))
                {
                    stream.CopyTo(ms);
                    sealedManifestTreeBlock = ms.ToArray();
                }

                return new FileAccessManifest(new PathTable(), directoryTranslator)
                {
                    InternalDetoursErrorNotificationFile = internalDetoursErrorNotificationFile,
                    PipId = pipId,
                    m_fileAccessManifestFlag = fileAccessManifestFlag,
                    m_sealedManifestTreeBlock = sealedManifestTreeBlock,
                    m_messageCountSemaphoreName = messageCountSemaphoreName
                };
            }
        }

        /// <summary>
        /// Return the raw byte array encoding of the tree-structured FileAccessManifest.
        /// </summary>
        /// <remarks>
        /// This method is only used for testing.
        /// </remarks>
        [SuppressMessage("Microsoft.Usage", "CA2202:Do not dispose objects multiple times")]
        public byte[] GetManifestTreeBytes()
        {
            if (IsManifestTreeBlockSealed)
            {
                Contract.Assert(m_sealedManifestTreeBlock != null);
                return m_sealedManifestTreeBlock;
            }

            // start with 4 KB of memory (one page), which will expand as necessary
            // stream will be disposed by the BinaryWriter when it goes out of scope
            using (var stream = new MemoryStream(4096))
            {
                using (var writer = new BinaryWriter(stream, Encoding.Unicode, true))
                {
                    m_rootNode.Serialize(writer);
                    var bytes = stream.ToArray();
                    return bytes;
                }
            }
        }

        /// <summary>
        /// Get an enumeration of lines representing the manifest tree.
        /// </summary>
        /// <returns>The line-by-line string representation of the manifest (formatted as a pre-order tree).</returns>
        public IEnumerable<string> Describe()
        {
            return m_rootNode.Describe();
        }

        // Keep this in sync with the C++ version declared in DataTypes.h
        [Flags]
        internal enum FileAccessManifestFlag
        {
            None = 0,
            BreakOnAccessDenied = 0x1,
            FailUnexpectedFileAccesses = 0x2,
            DiagnosticMessagesEnabled = 0x4,
            ReportFileAccesses = 0x8,
            ReportUnexpectedFileAccesses = 0x10,
            MonitorNtCreateFile = 0x20,
            MonitorChildProcesses = 0x40,
            IgnoreCodeCoverage = 0x80,
            ReportProcessArgs = 0x100,
            ForceReadOnlyForRequestedReadWrite = 0x200,
            IgnoreReparsePoints = 0x400,
            NormalizeReadTimestamps = 0x800,
            IgnoreZwRenameFileInformation = 0x1000,
            IgnoreSetFileInformationByHandle = 0x2000,
            UseLargeNtClosePreallocatedList = 0x4000,
            UseExtraThreadToDrainNtClose = 0x8000,
            DisableDetours = 0x10000,
            LogProcessData = 0x20000,
            IgnoreGetFinalPathNameByHandle = 0x40000,
            LogProcessDetouringStatus = 0x80000,
            HardExitOnErrorInDetours = 0x100000,
            CheckDetoursMessageCount = 0x200000,
            IgnoreZwOtherFileInformation = 0x400000,
            MonitorZwCreateOpenQueryFile = 0x800000,
            IgnoreNonCreateFileReparsePoints = 0x1000000,
            QBuildIntegrated = 0x4000000,
            IgnorePreloadedDlls = 0x8000000,
            EnforceAccessPoliciesOnDirectoryCreation = 0x10000000
        }

        // Keep this in sync with the C++ version declared in DataTypes.h
        [Flags]
        private enum FileAccessManifestExtraFlag
        {
            None = 0,
        }

        private readonly struct FileAccessScope
        {
            public readonly FileAccessPolicy Mask;
            public readonly FileAccessPolicy Values;

            public FileAccessScope(FileAccessPolicy mask, FileAccessPolicy values)
            {
                Mask = mask;
                Values = values;
            }
        }

        /// <summary>
        /// A class to convert a string into a zero-terminated, padded encoded byte array.
        /// </summary>
        private readonly struct PaddedByteString
        {
            private readonly Encoding m_encoding;
            private readonly string m_value;
            public readonly int Length;

            public static readonly PaddedByteString Invalid = default(PaddedByteString);

            public bool IsValid
            {
                get { return m_value != null; }
            }

            public void Serialize(BinaryWriter writer)
            {
                Contract.Requires(IsValid);
                Contract.Requires(writer != null);

                using (PooledObjectWrapper<byte[]> wrapper = Pools.GetByteArray(Length))
                {
                    byte[] bytes = wrapper.Instance;
                    m_encoding.GetBytes(m_value, 0, m_value.Length, bytes, 0);
                    writer.Write(bytes, 0, Length);
                }
            }

            public PaddedByteString(Encoding encoding, string str)
            {
                Contract.Requires(encoding != null);
                Contract.Requires(str != null);

                Length = checked(encoding.GetByteCount(str) + 4) & ~3;
                m_value = str;
                m_encoding = encoding;

                Contract.Assert(Length % 4 == 0); // make sure that the bytes are aligned at a 4-byte boundary
            }
        }

        private readonly struct NormalizedPathString : IEquatable<NormalizedPathString>
        {
            public readonly byte[] Bytes; // UTF16-encoded, includes terminating null character
            public readonly int HashCode;

            public NormalizedPathString(string path)
            {
                Contract.Requires(path != null);

                HashCode = ProcessUtilities.NormalizeAndHashPath(path, out Bytes);
            }

            public bool IsValid
            {
                get { return Bytes != null; }
            }

            public override int GetHashCode()
            {
                return HashCode;
            }

            public bool Equals(NormalizedPathString other)
            {
                return
                    HashCode == other.HashCode &&
                    ProcessUtilities.AreBuffersEqual(Bytes, other.Bytes);
            }

            public void Serialize(BinaryWriter writer)
            {
                writer.Write(Bytes, 0, Bytes.Length);

                // padding, so that we are always 4-byte aligned
                var mod = Bytes.Length & 0x3; // Bytes.Length % 4
                if (mod != 0)
                {
                    while (mod++ < 4)
                    {
                        writer.Write((byte)0);
                    }
                }
            }

            public override string ToString()
            {
                return IsValid ? Encoding.Unicode.GetString(Bytes, 0, Bytes.Length - sizeof(char)) : "<invalid>";
            }

            public override bool Equals(object obj)
            {
                return obj is NormalizedPathString && Equals((NormalizedPathString)obj);
            }

            public static bool operator ==(NormalizedPathString left, NormalizedPathString right)
            {
                return left.Equals(right);
            }

            public static bool operator !=(NormalizedPathString left, NormalizedPathString right)
            {
                return !(left == right);
            }
        }

        /// <summary>
        /// A node for a partial path and associated policy.
        /// </summary>
        private sealed class Node
        {
            /// <summary>
            /// Holds the children of this Node.
            /// </summary>
            private Dictionary<NormalizedPathString, Node> m_children;

            /// <summary>
            /// Policy for the cone defined by this node
            /// </summary>
            private FileAccessPolicy m_conePolicy;

            /// <summary>
            /// Policy for the specific node, with no implications to the scope that this node defines
            /// </summary>
            private FileAccessPolicy m_nodePolicy;

            private bool m_isPolicyFinalized;

            private readonly AbsolutePath m_pathId;

            /// <summary>
            /// The scope policy for the current cone (rooted at this node), to be converted into a policy when the
            /// tree is finalized.
            /// </summary>
            internal FileAccessScope ConeScope { get; private set; }

            /// <summary>
            /// The scope policy for the current node in particular, which gets combined into the node local policy
            /// when the tree is finalized.
            /// </summary>
            internal FileAccessScope NodeScope { get; private set; }

            /// <summary>
            /// Returns children nodes.
            /// </summary>
            internal IEnumerable<Node> Children => m_children?.Values;

            /// <summary>
            /// The path ID as understood by the owning path table.
            /// </summary>
            public AbsolutePath PathId
            {
                get { return m_pathId; }
            }

            /// <summary>
            /// If not <code>NoUsn</code>, check that when file is successfully opened, its initial USN is the expected USN.
            /// </summary>
            public Usn ExpectedUsn { get; private set; }

            /// <summary>
            /// The policy for the cone defined by the current node.
            /// </summary>
            public FileAccessPolicy ConePolicy
            {
                get
                {
                    return m_conePolicy;
                }
            }

            private void FinalizeConePolicy(FileAccessPolicy conePolicy)
            {
                m_conePolicy = conePolicy;
                m_isPolicyFinalized = true;
            }

            /// <summary>
            /// The final policy for the current node
            /// </summary>
            public FileAccessPolicy NodePolicy
            {
                get
                {
                    return m_nodePolicy;
                }
            }

            private void FinalizeNodePolicy(FileAccessPolicy nodePolicy)
            {
                m_nodePolicy = nodePolicy;
                m_isPolicyFinalized = true;
            }

            /// <summary>
            /// Use this to determine whether the policy has been finalized. All Nodes should have
            /// their policies finalized before serialization.
            /// </summary>
            private bool IsPolicyFinalized
            {
                get { return m_isPolicyFinalized; }
            }

            /// <summary>
            /// Construct a default Node. The value of the default node will be updated adding a scope for {Invalid}.
            /// </summary>
            public Node(AbsolutePath path)
            {
                ConeScope = new FileAccessScope(FileAccessPolicy.MaskNothing, FileAccessPolicy.Deny);
                NodeScope = new FileAccessScope(FileAccessPolicy.MaskNothing, FileAccessPolicy.Deny);
                ExpectedUsn = ReportedFileAccess.NoUsn;
                m_pathId = path;
            }

            /// <summary>
            /// Return a default instance of a Node with the default scope policy active.
            /// </summary>
            /// <returns>A default-scope root node instance.</returns>
            public static Node CreateRootNode()
            {
                var root = new Node(AbsolutePath.Invalid);
                return root;
            }

            /// <summary>
            /// A special case for adding the root scope for {Invalid} paths.
            /// </summary>
            /// <param name="scope">The root scope policy.</param>
            internal void ApplyRootScope(FileAccessScope scope)
            {
                Contract.Requires(!PathId.IsValid);
                ApplyConeFileAccess(scope);
            }

            /// <summary>
            /// Adds a cone to the tree with the given FileAccessScope.
            /// </summary>
            /// <param name="owner">The owning manifest.</param>
            /// <param name="coneRoot">The root of the cone, to be processed recursively.</param>
            /// <param name="scope">The scope policy for the Node represented by the path.</param>
            public void AddConeWithScope(FileAccessManifest owner, AbsolutePath coneRoot, FileAccessScope scope)
            {
                Contract.Requires(owner != null);
                Contract.Requires(coneRoot.IsValid);

                Node leaf = AddPath(owner, coneRoot);
                leaf.ApplyConeFileAccess(scope);
            }

            /// <summary>
            /// Add a file path to the tree with the given FileAccessPolicy.
            /// </summary>
            /// <param name="owner">The owning manifest.</param>
            /// <param name="path">The absolute path to the file, to be processed non-recursively.</param>
            /// <param name="scope">The policy for the file represented by the path.</param>
            /// <param name="expectedUsn">If not <code>NoUsn</code>, check that when is successfully opened, its initial USN is the expected USN.</param>
            public void AddNodeWithScope(FileAccessManifest owner, AbsolutePath path, FileAccessScope scope, Usn expectedUsn)
            {
                Contract.Requires(owner != null);
                Contract.Requires(path.IsValid);

                Node leaf = AddPath(owner, path);
                leaf.ApplyNodeFileAccess(scope, expectedUsn);
            }

            private Node GetOrCreateChild(FileAccessManifest owner, AbsolutePath path)
            {
                Contract.Requires(owner != null);
                Contract.Requires(path.IsValid);
                Contract.Ensures(Contract.Result<Node>() != null);

                StringId fragment = path.GetName(owner.m_pathTable).StringId;

                // We cache normalized fragments to avoid excessive memory allocations;
                // in particular, we can avoid the allocation associated with creating a new NormalizedPathString
                // for all fragments recurring in nested path prefixes.
                NormalizedPathString normalizedFragment;
                if (!owner.m_normalizedFragments.TryGetValue(fragment, out normalizedFragment))
                {
                    owner.m_normalizedFragments.Add(
                        fragment,
                        normalizedFragment = new NormalizedPathString(owner.m_pathTable.StringTable.GetString(fragment)));
                }

                Node child;
                if (m_children == null)
                {
                    m_children = new Dictionary<NormalizedPathString, Node>();
                }
                else if (m_children.TryGetValue(normalizedFragment, out child))
                {
                    Contract.Assume(child != null);
                    return child;
                }

                child = new Node(path);
                m_children.Add(normalizedFragment, child);
                return child;
            }

            // Keep this in sync with the C++ version declared in DataTypes.h
            [Flags]
            private enum FileAccessBucketOffsetFlag : uint
            {
                ChainStart = 0x01,
                ChainContinuation = 0x02,
                ChainMask = 0x03,
            }

            /// <summary>
            /// Add a path to the tree.
            /// </summary>
            /// <remarks>
            /// For each section of a path (directory for internal nodes, and files for leaves), if a node
            /// doesn't already exist, create a node using the scope of the directory above. These scopes
            /// will be applied when determining the final policy, so we rely on the fact that applying a scope
            /// is an idempotent operation. See lemma, below.
            /// If the comparison reaches the end of this Node's path, move to the children and try to find a match.
            /// If part of this Node's path (in terms of path atoms) is matched, but part diverges, we should split the node at the
            /// point that it diverges, and create a child node for the old path's policy, and for the new path's policy, rooted at
            /// a new node which represents everything in common since the divergence.
            /// </remarks>
            /// <param name="owner">The owning manifest.</param>
            /// <param name="path">The path to add to the tree.</param>
            /// <returns>The Node corresponding to the "leaf" of the path (which may or may not be a leaf of this tree).</returns>
            private Node AddPath(FileAccessManifest owner, AbsolutePath path)
            {
                Contract.Requires(owner != null);
                Contract.Requires(path.IsValid);
                Contract.Ensures(Contract.Result<Node>() != null);

                var container = path.GetParent(owner.m_pathTable);
                var node = container.IsValid ? AddPath(owner, container) : this;
                return node.GetOrCreateChild(owner, path);
            }

            /// <summary>
            /// Visit all nodes in the tree and finalize their respective policies using the parent policy
            /// as a reference.
            /// </summary>
            /// <remarks>
            /// If it is a leaf node and the policy is valid, use the policy that is specified for that node,
            /// otherwise recursively compute the policy from the root down to the leaves.
            /// </remarks>
            /// <param name="parentPolicy">
            /// The finalized policy from a parent.
            /// The default value is FileAccessPolicy.Deny because we need to start with nothing to get
            /// accurate results.
            /// </param>
            internal void FinalizePolicies(FileAccessPolicy parentPolicy = FileAccessPolicy.Deny)
            {
                if (IsPolicyFinalized)
                {
                    return; // don't repeat work
                }

                FinalizeConePolicy((parentPolicy & ConeScope.Mask) | ConeScope.Values);

                // The node policy is computed by composing the cone scope with the node scope
                FinalizeNodePolicy((ConePolicy & NodeScope.Mask) | NodeScope.Values);

                if (m_children != null)
                {
                    foreach (var child in m_children)
                    {
                        child.Value.FinalizePolicies(ConePolicy);
                    }
                }
            }

            private void ApplyConeFileAccess(FileAccessScope apply)
            {
                ConeScope = new FileAccessScope(mask: apply.Mask & ConeScope.Mask, values: apply.Values | ConeScope.Values);
            }

            private void ApplyNodeFileAccess(FileAccessScope apply, Usn expectedUsn)
            {
                Contract.Assume(ExpectedUsn == ReportedFileAccess.NoUsn || expectedUsn == ExpectedUsn, "Conflicting USNs set");

                NodeScope = new FileAccessScope(mask: apply.Mask & NodeScope.Mask, values: apply.Values | NodeScope.Values);
                ExpectedUsn = expectedUsn;
            }

            public void InternalSerialize(NormalizedPathString normalizedFragment, BinaryWriter writer)
            {
                // always finalize when serializing -- note that this prevents adding additional scopes as before
                if (!IsPolicyFinalized)
                {
                    FinalizePolicies();
                }

                unchecked
                {
                    long start = writer.BaseStream.Position;
#if DEBUG
                    writer.Write((uint)0xF00DCAFE); // "food cafe"
#endif
                    writer.Write((uint)normalizedFragment.HashCode);
                    writer.Write((uint)ConePolicy);
                    writer.Write((uint)NodePolicy);
                    writer.Write((uint)PathId.Value.Value);
                    writer.Write((ulong)ExpectedUsn.Value);

                    var childCount = (uint)(m_children == null ? 0 : m_children.Count);

                    // The children will be added to a hash-table.
                    // As it is known that hash-table performance starts to degrade with load factors > 0.7,
                    // we size our hash-table appropriately.
                    var bucketCount = childCount == 0 ? 0 : checked((uint)(childCount / 0.7));
                    Contract.Assert((bucketCount == 0) == (childCount == 0));
                    writer.Write(bucketCount);

                    long offsetsStart = 0;
                    if (m_children != null)
                    {
                        offsetsStart = writer.BaseStream.Position;
                        for (var i = 0; i < bucketCount; i++)
                        {
                            // to be patched up later
                            writer.Write(0U);
                        }
                    }

                    if (normalizedFragment.IsValid)
                    {
                        normalizedFragment.Serialize(writer);
                    }
                    else
                    {
                        writer.Write(0U);
                    }

                    if (m_children != null)
                    {
                        // We are now building a simple hash-table with linear chaining for collisions.
                        // The lowest two bits of each record may encoding information about whether a collision chain starts at that point, or continues.
                        uint[] offsets = new uint[bucketCount];
                        foreach (var child in m_children)
                        {
                            var hash = unchecked((uint)child.Key.HashCode);
                            var index = hash % bucketCount;

                            // collision?
                            if (offsets[index] != 0)
                            {
                                offsets[index] |= (uint)FileAccessBucketOffsetFlag.ChainStart;
                                index = (index + 1) % bucketCount;

                                // collision?
                                while (offsets[index] != 0)
                                {
                                    offsets[index] |= (uint)FileAccessBucketOffsetFlag.ChainContinuation;
                                    index = (index + 1) % bucketCount;
                                }
                            }

                            var offset = checked((uint)(writer.BaseStream.Position - start));
                            Contract.Assume((offset & (uint)FileAccessBucketOffsetFlag.ChainMask) == 0);
                            offsets[index] = offset;
                            child.Value.InternalSerialize(child.Key, writer);
                        }

                        long endPosition = writer.BaseStream.Position;
                        writer.BaseStream.Seek(offsetsStart, SeekOrigin.Begin);
                        for (var i = 0; i < offsets.Length; i++)
                        {
                            writer.Write(offsets[i]);
                        }

                        writer.BaseStream.Seek(endPosition, SeekOrigin.Begin);
                    }
                }
            }

            public void Serialize(BinaryWriter writer)
            {
                // always finalize when serializing -- note that this prevents adding additional scopes as before
                if (!IsPolicyFinalized)
                {
                    FinalizePolicies();
                }

                InternalSerialize(default(NormalizedPathString), writer);
            }

            private static string ReadUnicodeString(BinaryReader reader, List<byte> buffer)
            {
                buffer.Clear();
                while (true)
                {
                    var b1 = reader.ReadByte();
                    var b2 = reader.ReadByte();
                    if ((b1 | b2) == 0)
                    {
                        return Encoding.Unicode.GetString(buffer.ToArray());
                    }

                    buffer.Add(b1);
                    buffer.Add(b2);
                }
            }

            private static Stack<T> CreateStack<T>(T initialValue)
            {
                var q = new Stack<T>();
                q.Push(initialValue);
                return q;
            }

            /// <summary>
            /// Describe the content of this node, and all nested nodes.
            /// </summary>
            /// <remarks>
            /// The description is derived by re-parsed a generated binary blob,
            /// as that faithfully represents the information that is actually used by the monitored process.
            /// </remarks>
            [SuppressMessage("Microsoft.Usage", "CA2202:Do not dispose objects multiple times")]
            public IEnumerable<string> Describe()
            {
                // start with 4 KB of memory (one page), which will expand as necessary
                // stream will be disposed by the BinaryWriter when it goes out of scope
                using (var stream = new MemoryStream(4096))
                {
                    using (var writer = new BinaryWriter(stream, Encoding.Unicode, true))
                    {
                        Serialize(writer);
                    }

                    using (var reader = new BinaryReader(stream, Encoding.Unicode, true))
                    {
                        var stack = CreateStack(new { Start = 0L, Level = 0, Path = string.Empty });
                        var buffer = new List<byte>();
                        using (var wrapper = Pools.GetStringBuilder())
                        {
                            var str = wrapper.Instance;
                            while (stack.Count > 0)
                            {
                                var item = stack.Pop();
                                reader.BaseStream.Seek(item.Start, SeekOrigin.Begin);

                                str.Clear();
                                for (int i = 0; i < item.Level; i++)
                                {
                                    str.Append("  ");
                                }

#if DEBUG
                                var tag = unchecked((uint)reader.ReadInt32());
                                Contract.Assume(tag == 0xF00DCAFE);
#endif

                                var hash = reader.ReadUInt32();
                                var conePolicy = (short)reader.ReadUInt32();
                                var nodePolicy = (short)reader.ReadUInt32();

                                var pathId = reader.ReadUInt32();
                                Usn expectedUsn = new Usn(reader.ReadUInt64());

                                int hashtableCount = reader.ReadInt32();
                                List<long> absoluteChildStarts = new List<long>();
                                for (int i = 0; i < hashtableCount; i++)
                                {
                                    uint childOffset = reader.ReadUInt32();
                                    if (childOffset != 0)
                                    {
                                        absoluteChildStarts.Add(item.Start + (childOffset & ~0x3));
                                    }
                                }

                                string partialPath = ReadUnicodeString(reader, buffer);
                                string fullPath = Path.Combine(item.Path, partialPath);

                                if (hashtableCount != 0)
                                {
                                    // this is a directory
                                    fullPath += Path.DirectorySeparatorChar;
                                }

                                str.Append('[').Append(partialPath).Append(']');

                                if (pathId != 0)
                                {
                                    if (hashtableCount != 0)
                                    {
                                        str.Append(" <Scope>");
                                    }
                                    else if (!partialPath.Contains('.'))
                                    {
                                        str.Append(" <Scope>");
                                    }
                                    else
                                    {
                                        str.Append(" <Path>");
                                    }

                                    str.Append(" PathID:").AppendFormat(CultureInfo.InvariantCulture, "0x{0:X8}", pathId);
                                }

                                str.Append(" Cone Policy:").Append(conePolicy);
                                str.Append(" (");
                                FileAccessPolicyFormatter.Append(str, conePolicy);
                                str.Append(')');

                                str.Append(" Node Policy:").Append(nodePolicy);
                                str.Append(" (");
                                FileAccessPolicyFormatter.Append(str, nodePolicy);
                                str.Append(')');

                                if (expectedUsn != ReportedFileAccess.NoUsn)
                                {
                                    str.AppendFormat(" ExpectedUsn:{0}", expectedUsn);
                                }

                                if (string.IsNullOrEmpty(partialPath))
                                {
                                    str.Append(" {Root Scope}");
                                }
                                else
                                {
                                    str.Append(" '").Append(fullPath).Append('\'');
                                }

                                yield return str.ToString();

                                for (int i = absoluteChildStarts.Count - 1; i >= 0; i--)
                                {
                                    stack.Push(new { Start = absoluteChildStarts[i], Level = item.Level + 1, Path = fullPath });
                                }
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Formatter for FileAccessPolicy flags in a compact way for more readable output messages.
        /// </summary>
        /// <remarks>
        /// Unfortunately the automatic formatter does not do a good job at this because the enum
        /// contains duplicate names for the same values and has explicit named value for zero, which
        /// ends up getting appended to every section of the string separated by '|'.
        /// Perhaps moving the mask flags out of the enum would help with this.
        /// This was adapted from BuildXL.Processes.ReportedFileAccess.UInt32EnumFormatter.
        /// </remarks>
        private static class FileAccessPolicyFormatter
        {
            private static readonly List<Tuple<short, string>> s_names = GetNames();

            private static List<Tuple<short, string>> GetNames()
            {
                var names = new List<Tuple<short, string>>
                            {
                                Tuple.Create((short)FileAccessPolicy.Deny, "Deny"),
                                Tuple.Create((short)FileAccessPolicy.AllowRead, "Read"),
                                Tuple.Create((short)FileAccessPolicy.AllowWrite, "Write"),
                                Tuple.Create((short)FileAccessPolicy.AllowSymlinkCreation, "CreateSymlink"),
                                Tuple.Create((short)FileAccessPolicy.AllowReadIfNonexistent, "ReadIfNonexistent"),
                                Tuple.Create((short)FileAccessPolicy.AllowCreateDirectory, "CreateDirectory"),
                                Tuple.Create((short)FileAccessPolicy.ReportAccess, "ReportAccess"),

                                // Note that composite values must appear before their parts.
                                Tuple.Create((short)FileAccessPolicy.ReportAccessIfExistent, "ReportAccessIfExistent"),
                                Tuple.Create(
                                    (short)FileAccessPolicy.ReportAccessIfNonexistent,
                                    "ReportAccessIfNonExistent"),
                                Tuple.Create(
                                    (short)FileAccessPolicy.ReportDirectoryEnumerationAccess,
                                    "ReportDirectoryEnumerationAccess")
                            };

                return names;
            }

            public static void Append(StringBuilder sb, short value)
            {
                short i = value;

                if (value == 0)
                {
                    sb.Append(s_names[value]);
                    return;
                }

                bool first = true;
                foreach (Tuple<short, string> pair in s_names)
                {
                    if ((i & pair.Item1) == pair.Item1)
                    {
                        if (first)
                        {
                            first = false;
                        }
                        else
                        {
                            sb.Append('|');
                        }

                        sb.Append(pair.Item2);

                        i = (short)(i & ~pair.Item1);
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
