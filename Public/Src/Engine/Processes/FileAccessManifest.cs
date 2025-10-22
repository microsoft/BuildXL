// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Instrumentation.Common;

#nullable enable

namespace BuildXL.Processes
{
    /// <summary>
    /// Describes the set of files that a process may access, and the kind of operations that are permitted
    /// for each file.
    /// </summary>
    public sealed class FileAccessManifest
    {
        private readonly Dictionary<StringId, NormalizedPathString> m_normalizedFragments = new();

        /// <summary>
        /// Represents the invalid scope.
        /// </summary>
        private Node m_rootNode;

        /// <summary>
        /// File access manifest flags.
        /// </summary>
        private FileAccessManifestFlag m_fileAccessManifestFlag;

        /// <summary>
        /// Extra file access manifest flags.
        /// </summary>
        private FileAccessManifestExtraFlag m_fileAccessManifestExtraFlag;

        /// <summary>
        /// Name of semaphore for counting successfully sent messages.
        /// </summary>
        private string? m_messageSentCountSemaphoreName;

        /// <summary>
        /// Name of semaphore for counting messages sent and received.
        /// </summary>
        private string? m_messageCountSemaphoreName;

        /// <summary>
        /// Sealed manifest tree.
        /// </summary>
        private byte[]? m_sealedManifestTreeBlock;

        /// <summary>
        /// Creates an empty instance.
        /// </summary>
        public FileAccessManifest(
            PathTable pathTable,
            DirectoryTranslator? translateDirectories = null,
            IReadOnlyCollection<BreakawayChildProcess>? childProcessesToBreakawayFromSandbox = null)
        {
            PathTable = pathTable;
            m_rootNode = Node.CreateRootNode();
            DirectoryTranslator = translateDirectories;
            ChildProcessesToBreakawayFromSandbox = childProcessesToBreakawayFromSandbox;

            // We are pedantic by default. It's otherwise easy to silently do nothing in e.g. tests.
            FailUnexpectedFileAccesses = true;
            ReportUnexpectedFileAccesses = true;
            ReportFileAccesses = false;
            MonitorChildProcesses = true;
            MonitorNtCreateFile = true;
            MonitorZwCreateOpenQueryFile = true;
            ReportProcessArgs = false;
            ForceReadOnlyForRequestedReadWrite = false;
            IgnoreReparsePoints = false;
            IgnoreFullReparsePointResolving = true; // TODO: Change this when customers onboard the feature.
            IgnoreUntrackedPathsInFullReparsePointResolving = false;
            IgnorePreloadedDlls = false;
            DisableDetours = false;
            IgnoreZwRenameFileInformation = false;
            IgnoreZwOtherFileInformation = false;
            IgnoreNonCreateFileReparsePoints = false;
            IgnoreSetFileInformationByHandle = false;
            NormalizeReadTimestamps = true;
            UseLargeNtClosePreallocatedList = false;
            UseExtraThreadToDrainNtClose = true;
            LogProcessData = false;
            LogProcessDetouringStatus = false;
            HardExitOnErrorInDetours = false;
            CheckDetoursMessageCount = false;
            InternalDetoursErrorNotificationFile = string.Empty;
            UseLargeEnumerationBuffer = false;
            PipId = 0L;
            EnforceAccessPoliciesOnDirectoryCreation = false;
            IgnoreCreateProcessReport = false;
            ProbeDirectorySymlinkAsDirectory = false;
            ExplicitlyReportDirectoryProbes = false;
            PreserveFileSharingBehaviour = false;
            EnableLinuxPTraceSandbox = false;
            EnableLinuxSandboxLogging = false;
            AlwaysRemoteInjectDetoursFrom32BitProcess = false;
            UnconditionallyEnableLinuxPTraceSandbox = false;
            IgnoreDeviceIoControlGetReparsePoint = true;
            IgnoreGetFinalPathNameByHandle = true;
        }

        private bool GetFlag(FileAccessManifestFlag flag) => (m_fileAccessManifestFlag & flag) != 0;

        private bool GetExtraFlag(FileAccessManifestExtraFlag flag) => (m_fileAccessManifestExtraFlag & flag) != 0;

        /// <summary>
        /// Gets file access manifest flag.
        /// </summary>
        internal FileAccessManifestFlag Flag => m_fileAccessManifestFlag;

        internal FileAccessManifestExtraFlag ExtraFlag => m_fileAccessManifestExtraFlag;

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

        private void SetExtraFlag(FileAccessManifestExtraFlag flag, bool value)
        {
            if (value)
            {
                m_fileAccessManifestExtraFlag |= flag;
            }
            else
            {
                m_fileAccessManifestExtraFlag &= ~flag;
            }
        }

        /// <summary>
        /// Flag indicating if the manifest tree block is sealed.
        /// </summary>
        public bool IsManifestTreeBlockSealed => m_sealedManifestTreeBlock is not null;

        /// <summary>
        /// Flag indicating if the manifest tree is available
        /// </summary>
        /// <remarks>
        /// The regular deserialization of the manifest leaves only a byte representation of the node tree, that is 
        /// good enough for most purposes. However, in some cases we need to perform a full deserialization
        /// </remarks>
        public bool IsManifestTreeHydrated => !IsManifestTreeBlockSealed || m_rootNode.Children is not null;

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
        /// If true, ignore report from CreateProcess.
        /// </summary>
        public bool IgnoreCreateProcessReport
        {
            get => GetFlag(FileAccessManifestFlag.IgnoreCreateProcessReport);
            set => SetFlag(FileAccessManifestFlag.IgnoreCreateProcessReport, value);
        }

        /// <summary>
        /// If true, probe directory symlink (or junction) as directory.
        /// </summary>
        public bool ProbeDirectorySymlinkAsDirectory
        {
            get => GetFlag(FileAccessManifestFlag.ProbeDirectorySymlinkAsDirectory);
            set => SetFlag(FileAccessManifestFlag.ProbeDirectorySymlinkAsDirectory, value);
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
        /// If true, allows following reparse points without enforcing any file access rules for their embedded targets.
        /// </summary>
        public bool IgnoreReparsePoints
        {
            get => GetFlag(FileAccessManifestFlag.IgnoreReparsePoints);
            set => SetFlag(FileAccessManifestFlag.IgnoreReparsePoints, value);
        }
        
        /// <summary>
        /// Initially BuildXL did not fully resolve file access paths which contained reparse points - especially paths with multiple embedded 
        /// junctions and/or directory symbolic links were not handled correctly, nor were the semantics for symbolic link reporting on Windows 
        /// on par with our Unix implementations. This behavior was mostly incomplete due to performance concerns, but with a resolver cache in
        /// the Detours layer we can slowly migrate partners to using full reparse point resolving. If no resolving at all should happen at all, 
        /// users can still pass 'IgnoreReparsePoints'.
        /// <remarks>If true, paths with reparse points are not fully resolved nor reported and the legacy behavior is honored.</remarks>
        /// </summary>
        public bool IgnoreFullReparsePointResolving
        {
            get => GetFlag(FileAccessManifestFlag.IgnoreFullReparsePointResolving);
            set => SetFlag(FileAccessManifestFlag.IgnoreFullReparsePointResolving, value);
        }

        /// <summary>
        /// If true, untracked paths, when symlinks, are not fully resolved nor reported.
        /// </summary>
        public bool IgnoreUntrackedPathsInFullReparsePointResolving
        {
            get => GetExtraFlag(FileAccessManifestExtraFlag.IgnoreUntrackedPathsInFullReparsePointResolving);
            set => SetExtraFlag(FileAccessManifestExtraFlag.IgnoreUntrackedPathsInFullReparsePointResolving, value);
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
        /// Previously used to provide some specific handling for QuickBuild builds when QuickBuild started to use BuildXL sandbox.
        /// </summary>
        [Obsolete("Deprecated")]
        public bool QBuildIntegrated
        {
            get;
            set;
        }

        /// <summary>
        /// Use large buffer for directory enumeration using NtQueryDirectoryFile or FindFirstFileEx.
        /// </summary>
        /// <remarks>
        /// Due to bug in ProjFs, directory enumerations by NtQueryDirectoryFile and FindFirstFileEx requires a large
        /// buffer for filling in enumeration results; otherwise directory enumerations can be incomplete.
        /// 
        /// BUG: https://microsoft.visualstudio.com/OS/_workitems/edit/38539442
        /// TODO: Deprecate this flag once the bug is resolved.
        /// </remarks>
        public bool UseLargeEnumerationBuffer
        {
            get => GetFlag(FileAccessManifestFlag.UseLargeEnumerationBuffer);
            set => SetFlag(FileAccessManifestFlag.UseLargeEnumerationBuffer, value);
        }

        /// <summary>
        /// When enabled, directory accesses will be explcitily reported by detours.
        /// </summary>
        public bool ExplicitlyReportDirectoryProbes
        {
            get => GetExtraFlag(FileAccessManifestExtraFlag.ExplicitlyReportDirectoryProbes);
            set => SetExtraFlag(FileAccessManifestExtraFlag.ExplicitlyReportDirectoryProbes, value);
        }

        /// <summary>
        /// When enabled, FILE_SHARE_DELETE will not be set automatically for CreateFile/NtCreateFile calls in Detours
        /// </summary>
        public bool PreserveFileSharingBehaviour
        {
            get => GetExtraFlag(FileAccessManifestExtraFlag.PreserveFileSharingBehaviour);
            set => SetExtraFlag(FileAccessManifestExtraFlag.PreserveFileSharingBehaviour, value);
        }

        /// <summary>
        /// When enabled, the Linux sandbox will use PTrace for binaries that are statically linked
        /// </summary>
        public bool EnableLinuxPTraceSandbox
        {
            get => GetExtraFlag(FileAccessManifestExtraFlag.EnableLinuxPTraceSandbox);
            set => SetExtraFlag(FileAccessManifestExtraFlag.EnableLinuxPTraceSandbox, value);
        }

        /// <summary>
        /// When enabled, the Linux sandbox will emit debug messages as access reports
        /// </summary>
        public bool EnableLinuxSandboxLogging
        {
            get => GetExtraFlag(FileAccessManifestExtraFlag.EnableLinuxSandboxLogging);
            set => SetExtraFlag(FileAccessManifestExtraFlag.EnableLinuxSandboxLogging, value);
        }

        /// <summary>
        /// When true, always use remote detours injection when launching processes from a 32-bit process.
        /// </summary>
        public bool AlwaysRemoteInjectDetoursFrom32BitProcess
        {
            get => GetExtraFlag(FileAccessManifestExtraFlag.AlwaysRemoteInjectDetoursFrom32BitProcess);
            set => SetExtraFlag(FileAccessManifestExtraFlag.AlwaysRemoteInjectDetoursFrom32BitProcess, value);
        }

        /// <summary>
        /// When enabled, the Linux sandbox will use PTrace for all binaries (regardless whether they are statically linked)
        /// </summary>
        /// <remarks>
        /// For testing purposes.
        /// Enabling this option implies enabling <see cref="EnableLinuxPTraceSandbox"/>
        /// </remarks>
        public bool UnconditionallyEnableLinuxPTraceSandbox
        {
            get => GetExtraFlag(FileAccessManifestExtraFlag.UnconditionallyEnableLinuxPTraceSandbox);
            set
            {
                SetExtraFlag(FileAccessManifestExtraFlag.UnconditionallyEnableLinuxPTraceSandbox, value);
                if (value)
                {
                    SetExtraFlag(FileAccessManifestExtraFlag.EnableLinuxPTraceSandbox, value);
                }
            }
        }

        /// <summary>
        /// When enabled, DeviceIoControl (case FSCTL_GET_REPARSE_POINT) is detoured 
        /// </summary>
        public bool IgnoreDeviceIoControlGetReparsePoint
        {
            get => GetExtraFlag(FileAccessManifestExtraFlag.IgnoreDeviceIoControlGetReparsePoint);
            set => SetExtraFlag(FileAccessManifestExtraFlag.IgnoreDeviceIoControlGetReparsePoint, value);
        }

        /// <summary>
        /// A location for a file where Detours to log failure messages.
        /// </summary>
        public string? InternalDetoursErrorNotificationFile { get; set; }

        /// <summary>
        /// The semaphore that keeps count the successful sent messages.
        /// </summary>
        public INamedSemaphore? MessageSentCountSemaphore { get; private set; }

        /// <summary>
        /// The semaphore that keeps count to the sent and received messages.
        /// </summary>
        public INamedSemaphore? MessageCountSemaphore { get; private set; }

        /// <summary>
        /// Directory translator.
        /// </summary>
        public DirectoryTranslator? DirectoryTranslator { get; set; }

        /// <summary>
        /// The pip's unique id.
        /// Right now it is the Pip.SemiStableHash.
        /// </summary>
        public long PipId { get; set; }

        /// <summary>
        /// List of child processes that will break away from the sandbox
        /// </summary>
        public IReadOnlyCollection<BreakawayChildProcess>? ChildProcessesToBreakawayFromSandbox { get; set; }

        /// <summary>
        /// Whether there is any configured child process that can breakaway from the sandbox
        /// </summary>
        public bool ProcessesCanBreakaway => ChildProcessesToBreakawayFromSandbox?.Any() == true;

        /// <summary>
        /// Sets message count semaphore.
        /// </summary>
        public bool SetMessageCountSemaphore(string semaphoreName, out string? errorMessage)
        {
            Contract.Requires(semaphoreName.Length > 0);

            UnsetMessageCountSemaphore();

            string messageCountSemaphoreName = OperatingSystemHelper.IsWindowsOS ? semaphoreName + "_1" : semaphoreName;
            var maybeSemaphore = SemaphoreFactory.CreateNew(messageCountSemaphoreName, 0, int.MaxValue);

            if (!maybeSemaphore.Succeeded)
            {
                errorMessage = maybeSemaphore.Failure?.DescribeIncludingInnerFailures();
                return false;
            }

            MessageCountSemaphore = maybeSemaphore.Result;
            m_messageCountSemaphoreName = messageCountSemaphoreName;

            if (OperatingSystemHelper.IsWindowsOS)
            {
                string messageSentCountSemaphoreName = semaphoreName + "_2";
                var maybeMessageSentSemaphore = SemaphoreFactory.CreateNew(messageSentCountSemaphoreName, 0, int.MaxValue);
                if (!maybeMessageSentSemaphore.Succeeded)
                {
                    errorMessage = maybeSemaphore.Failure?.DescribeIncludingInnerFailures();
                    return false; 
                }

                MessageSentCountSemaphore = maybeMessageSentSemaphore.Result;
                m_messageSentCountSemaphoreName = messageSentCountSemaphoreName;
            }
            errorMessage = null;
            return true;
        }

        /// <summary>
        /// Unset message count semaphore.
        /// </summary>
        public void UnsetMessageCountSemaphore()
        {
            if (MessageCountSemaphore is not null)
            {
                Contract.Assert(m_messageCountSemaphoreName is not null);
                MessageCountSemaphore.Dispose();
                MessageCountSemaphore = null;
                m_messageCountSemaphoreName = null;
            }

            if (MessageSentCountSemaphore is not null)
            {
                Contract.Assert(m_messageSentCountSemaphoreName is not null);
                MessageSentCountSemaphore.Dispose();
                MessageSentCountSemaphore = null;
                m_messageSentCountSemaphoreName = null;
            }
        }

        /// <summary>
        /// Optional child substitute shim execution information.
        /// </summary>
        /// <remarks>
        /// Internal since this is set as a side effect of initializing a FileAccessManifest
        /// in <see cref="SandboxedProcessInfo"/>.
        /// </remarks>
        public SubstituteProcessExecutionInfo? SubstituteProcessExecutionInfo { get; set; }

        /// <nodoc/>
        public PathTable PathTable { get; }

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

        /// <summary> 
        /// Looking up a policy for a path is not allowed before this property becomes true
        /// (which happens once the FAM is serialized)
        /// </summary>
        public bool IsFinalized => m_rootNode.IsPolicyFinalized;

        /// <summary>
        /// Finds the manifest path (the 'closest' configured path policy) for a given path. 
        /// </summary>
        /// <remarks>
        /// When no manifest path is found, the returned manifest path is <see cref="AbsolutePath.Invalid"/>. 
        /// The node policy is always set (and will contain the policy of the root node if no explicit 
        /// manifest path is found)
        /// 
        /// CAUTION
        ///       When the manifest is obtained from a deserialization, it will have a new empty path table.
        ///       This method only works if the given path is obtained from the same path table that the original
        ///       file access manifest holds before being serialized.
        /// </remarks>
        public bool TryFindManifestPathFor(AbsolutePath path, out AbsolutePath manifestPath, out FileAccessPolicy nodePolicy)
        {
            Contract.Requires(path.IsValid);

            // The manifest could be the result of a deserialization process, so the tree node may not be available. Make sure it is 
            // hydrated before we try to do anything with it
            HydrateTreeNodeIfNeeded();

            Contract.Assert(m_rootNode.IsPolicyFinalized);

            var resultNode = m_rootNode.FindNodeFor(this, path);
            nodePolicy = resultNode.NodePolicy;

            if (resultNode == m_rootNode)
            {
                manifestPath = AbsolutePath.Invalid;
                return false;
            }

            manifestPath = resultNode.PathId;

            Contract.Assert(manifestPath.IsValid);
            return true;
        }

        // See unmanaged decoder at DetoursHelpers.cpp :: CreateStringFromWriteChars()
        private static void WriteChars(BinaryWriter writer, string? str)
        {
            var strLen = (uint)(string.IsNullOrEmpty(str) ? 0 : str!.Length);
            writer.Write(strLen);
            for (var i = 0; i < strLen; i++)
            {
                writer.Write(str![i]);
            }
        }

        private void WritePath(BinaryWriter writer, AbsolutePath path) => WriteChars(writer, path.IsValid ? path.ToString(PathTable) : null);

        private void WritePathAtom(BinaryWriter writer, PathAtom pathAtom) => WriteChars(writer, pathAtom.IsValid ? pathAtom.ToString(PathTable.StringTable) : null);

        private static string? ReadChars(BinaryReader reader)
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
            writer.Write(timeoutInMins);
        }

        private static class CheckedCode
        {
            // CODESYNC: DataTypes.h.
            public const uint TranslationPathString         = 0xABCDEF02;
            public const uint ErrorDumpLocation             = 0xABCDEF03;
            public const uint SubstituteProcessShim         = 0xABCDEF04;
            public const uint ChildProcessesBreakAwayString = 0xABCDEF05;
            public const uint Flags                         = 0xF1A6B10C;
            public const uint PipId                         = 0xF1A6B10E;
            public const uint DebugOn                       = 0xDB600001;
            public const uint DebugOff                      = 0xDB600000;
            public const uint ExtraFlags                    = 0xF1A6B10D;
            public const uint ReportBlock                   = 0xFEEDF00D; // feed food.
            public const uint DllBlock                      = 0xD11B10CC;

            public static void EnsureRead(BinaryReader reader, uint checkCode)
            {
                uint code = reader.ReadUInt32();
                Contract.Assert(code == checkCode);
            }
        }

        [SuppressMessage("Microsoft.Globalization", "CA1308:NormalizeStringsToUppercase", Justification = "Architecture strings are USASCII")]
        private static void WriteErrorDumpLocation(BinaryWriter writer, string? internalDetoursErrorNotificationFile)
        {
#if DEBUG
            writer.Write(CheckedCode.ErrorDumpLocation);
#endif
            WriteChars(writer, internalDetoursErrorNotificationFile);
        }

        private static string? ReadErrorDumpLocation(BinaryReader reader)
        {
#if DEBUG
            CheckedCode.EnsureRead(reader, CheckedCode.ErrorDumpLocation);
#endif
            return ReadChars(reader);
        }

        [SuppressMessage("Microsoft.Globalization", "CA1308:NormalizeStringsToUppercase", Justification = "Architecture strings are USASCII")]
        private static void WriteTranslationPathStrings(BinaryWriter writer, DirectoryTranslator? translatePaths)
        {
#if DEBUG
            writer.Write(CheckedCode.TranslationPathString);
#endif

            // Write the number of translation paths.
            uint translatePathLen = (uint)(translatePaths?.Count ?? 0);
            writer.Write(translatePathLen);

            if (translatePathLen > 0)
            {
                foreach (DirectoryTranslator.Translation translation in translatePaths!.Translations)
                {
                    WriteChars(writer, translation.SourcePath);
                    WriteChars(writer, translation.TargetPath);
                }
            }
        }

        private static DirectoryTranslator? ReadTranslationPathStrings(BinaryReader reader)
        {
#if DEBUG
            CheckedCode.EnsureRead(reader, CheckedCode.TranslationPathString);
#endif

            uint length = reader.ReadUInt32();
            if (length == 0)
            {
                return null;
            }

            var directoryTranslator = new DirectoryTranslator();

            for (int i = 0; i < length; ++i)
            {
                string? source = ReadChars(reader);
                string? target = ReadChars(reader);
                directoryTranslator.AddTranslation(source, target);
            }

            directoryTranslator.Seal();

            return directoryTranslator;
        }

        // CODESYNC: FileAccessManifestParser.cpp :: init
        // CODESYNC: Public/Src/Sandbox/Common/FileAccessManifest.cc FileAccessManifest::ParseFileAccessManifest
        private static void WriteChildProcessesToBreakAwayFromSandbox(
            BinaryWriter writer,
            IReadOnlyCollection<BreakawayChildProcess>? breakawayChildProcesses)
        {
#if DEBUG
            writer.Write(CheckedCode.ChildProcessesBreakAwayString);
#endif

            // Write the number of breakaway child processes.
            uint numBreakawayChildProcesses = (uint)(breakawayChildProcesses?.Count ?? 0);
            writer.Write(numBreakawayChildProcesses);

            if (numBreakawayChildProcesses > 0)
            {
                foreach (BreakawayChildProcess breakawayChildProcess in breakawayChildProcesses!)
                {
                    (string processName, string? requiredCommandLineArgsSubstring, bool commandLineArgsSubstringContainmentIgnoreCase) = breakawayChildProcess;
                    WriteChars(writer, processName);
                    WriteChars(writer, requiredCommandLineArgsSubstring);
                    writer.Write(commandLineArgsSubstringContainmentIgnoreCase);
                }
            }
        }

        private static IReadOnlyCollection<BreakawayChildProcess>? ReadChildProcessesToBreakAwayFromSandbox(BinaryReader reader)
        {
#if DEBUG
            CheckedCode.EnsureRead(reader, CheckedCode.ChildProcessesBreakAwayString);
#endif

            uint numBreakawayChildProcesses = reader.ReadUInt32();
            if (numBreakawayChildProcesses == 0)
            {
                return null;
            }

            var breakawayChildProcesses = new HashSet<BreakawayChildProcess>((int)numBreakawayChildProcesses);
            for (int i = 0; i < numBreakawayChildProcesses; ++i)
            {
                breakawayChildProcesses.Add(new BreakawayChildProcess(ReadChars(reader)!, ReadChars(reader), reader.ReadBoolean()));
            }

            return breakawayChildProcesses;
        }

        [SuppressMessage("Microsoft.Naming", "CA2204:LiteralsShouldBeSpelledCorrectly")]
        private static void WriteDebugFlagBlock(BinaryWriter writer, ref bool debugFlagsMatch)
        {
            debugFlagsMatch = true;
#if DEBUG
            // "debug 1 (on)"
            writer.Write(CheckedCode.DebugOn);
            if (!ProcessUtilities.IsNativeInDebugConfiguration())
            {
                debugFlagsMatch = false;
            }
#else
            // "debug 0 (off)"
            writer.Write(CheckedCode.DebugOff);
            if (ProcessUtilities.IsNativeInDebugConfiguration())
            {
                debugFlagsMatch = false;
            }
#endif
        }

        private static void WriteFlagsBlock(BinaryWriter writer, FileAccessManifestFlag flags)
        {
#if DEBUG
            writer.Write(CheckedCode.Flags);
#endif

            writer.Write((uint)flags);
        }

        private static FileAccessManifestFlag ReadFlagsBlock(BinaryReader reader)
        {
#if DEBUG
            CheckedCode.EnsureRead(reader, CheckedCode.Flags);
#endif

            return (FileAccessManifestFlag)reader.ReadUInt32();
        }

        // Allow for future expansions with 64 more flags (in case they become needed))
        private static void WriteExtraFlagsBlock(BinaryWriter writer, FileAccessManifestExtraFlag extraFlags)
        {
#if DEBUG
            writer.Write(CheckedCode.ExtraFlags);
#endif
            writer.Write((uint)extraFlags);
        }

        private static FileAccessManifestExtraFlag ReadExtraFlagsBlock(BinaryReader reader)
        {
#if DEBUG
            CheckedCode.EnsureRead(reader, CheckedCode.ExtraFlags);
#endif

            return (FileAccessManifestExtraFlag)reader.ReadUInt32();
        }


        private static void WritePipId(BinaryWriter writer, long pipId)
        {
#if DEBUG
            writer.Write(CheckedCode.PipId);
            writer.Write(0); // Padding. Needed to keep the data properly aligned for the C/C++ compiler.
#endif
            // The PipId is needed for reporting purposes on non Windows OSs. Write it here.
            writer.Write(pipId);
        }

        private static long ReadPipId(BinaryReader reader)
        {
#if DEBUG
            CheckedCode.EnsureRead(reader, CheckedCode.PipId);

            int zero = reader.ReadInt32();
            Contract.Assert(0 == zero);
#endif
            return reader.ReadInt64();
        }

        private static void WriteReportBlock(BinaryWriter writer, FileAccessSetup setup)
        {
#if DEBUG
            writer.Write(CheckedCode.ReportBlock);
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

                    writer.Write(size);
                    writer.Write(handleValue32bit);
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
            writer.Write(CheckedCode.DllBlock);
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

        private void WriteSubstituteProcessShimBlock(BinaryWriter writer)
        {
#if DEBUG
            writer.Write(CheckedCode.SubstituteProcessShim);
#endif

            if (SubstituteProcessExecutionInfo is null)
            {
                writer.Write(0U);  // ShimAllProcesses false value.

                // Emit a zero-length substituteProcessExecShimPath when substitution is turned off.
                WriteChars(writer, null);
                return;
            }

            writer.Write(SubstituteProcessExecutionInfo.ShimAllProcesses ? 1U : 0U);
            WritePath(writer, SubstituteProcessExecutionInfo.SubstituteProcessExecutionShimPath);
            WritePath(writer, SubstituteProcessExecutionInfo.SubstituteProcessExecutionPluginDll32Path);
            WritePath(writer, SubstituteProcessExecutionInfo.SubstituteProcessExecutionPluginDll64Path);

            writer.Write((uint)SubstituteProcessExecutionInfo.ShimProcessMatches.Count);

            if (SubstituteProcessExecutionInfo.ShimProcessMatches.Count > 0)
            {
                foreach (ShimProcessMatch match in SubstituteProcessExecutionInfo.ShimProcessMatches)
                {
                    WritePathAtom(writer, match.ProcessName);
                    WritePathAtom(writer, match.ArgumentMatch);
                }
            }
        }

        private void WriteManifestTreeBlock(BinaryWriter writer)
        {
            if (m_sealedManifestTreeBlock is not null)
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
        public ArraySegment<byte> GetPayloadBytes(LoggingContext loggingContext, FileAccessSetup setup, MemoryStream stream, uint timeoutMins, ref bool debugFlagsMatch)
        {
            stream.Position = 0;
            using (var writer = new BinaryWriter(stream, Encoding.Unicode, true))
            {
                WriteDebugFlagBlock(writer, ref debugFlagsMatch);
                if (!debugFlagsMatch)
                {
#if DEBUG
                    Tracing.Logger.Log.PipInvalidDetoursDebugFlag1(loggingContext);
#else
                    Tracing.Logger.Log.PipInvalidDetoursDebugFlag2(loggingContext);
#endif
                }
                WriteInjectionTimeoutBlock(writer, timeoutMins);
                WriteChildProcessesToBreakAwayFromSandbox(writer, ChildProcessesToBreakawayFromSandbox);
                WriteTranslationPathStrings(writer, DirectoryTranslator);
                WriteErrorDumpLocation(writer, InternalDetoursErrorNotificationFile);
                WriteFlagsBlock(writer, m_fileAccessManifestFlag);
                WriteExtraFlagsBlock(writer, m_fileAccessManifestExtraFlag);
                WritePipId(writer, PipId);
                WriteReportBlock(writer, setup);
                WriteDllBlock(writer, setup);
                WriteSubstituteProcessShimBlock(writer);
                WriteManifestTreeBlock(writer);

                return new ArraySegment<byte>(stream.GetBuffer(), 0, (int)stream.Position);
            }
        }

        /// <summary>
        /// Explicitly releases most of its memory.
        /// </summary>
        internal void Release()
        {
            m_normalizedFragments?.Clear();
            var workList = new Stack<Node>();
            workList.Push(m_rootNode);
            while (workList.Count > 0)
            {
                var node = workList.Pop();
                if (node is null)
                {
                    continue;
                }
                if (node.Children is not null)
                {
                    foreach (var child in node.Children)
                    {
                        workList.Push(child);
                    }
                }
                node.ReleaseChildren();
            }
        }

        /// <summary>
        /// Serializes this manifest.
        /// </summary>
        public void Serialize(Stream stream)
        {
            using var writer = new BinaryWriter(stream, Encoding.Unicode, true);
            WriteChildProcessesToBreakAwayFromSandbox(writer, ChildProcessesToBreakawayFromSandbox);
            WriteTranslationPathStrings(writer, DirectoryTranslator);
            WriteErrorDumpLocation(writer, InternalDetoursErrorNotificationFile);
            WriteFlagsBlock(writer, m_fileAccessManifestFlag);
            WriteExtraFlagsBlock(writer, m_fileAccessManifestExtraFlag);
            WritePipId(writer, PipId);
            WriteChars(writer, m_messageCountSemaphoreName);

            if (OperatingSystemHelper.IsWindowsOS)
            {
                WriteChars(writer, m_messageSentCountSemaphoreName);
            }

            // The manifest tree block has to be serialized the last.
            WriteManifestTreeBlock(writer);
        }

        /// <summary>
        /// Deserialize an instance of <see cref="FileAccessManifest"/> from stream.
        /// </summary>
        /// <remarks>
        /// This method does not perform a full fidelity deserialization, since the node tree is left as a byte array, available
        /// via <see cref="GetManifestTreeBytes"/>. To get the full fidelity manifest back, call HydrateTreeNode.
        /// </remarks>
        public static FileAccessManifest Deserialize(Stream stream)
        {
            using var reader = new BinaryReader(stream, Encoding.Unicode, true);
            IReadOnlyCollection<BreakawayChildProcess>? childProcessesToBreakAwayFromSandbox = ReadChildProcessesToBreakAwayFromSandbox(reader);
            DirectoryTranslator? directoryTranslator = ReadTranslationPathStrings(reader);
            string? internalDetoursErrorNotificationFile = ReadErrorDumpLocation(reader);
            FileAccessManifestFlag fileAccessManifestFlag = ReadFlagsBlock(reader);
            FileAccessManifestExtraFlag fileAccessManifestExtraFlag = ReadExtraFlagsBlock(reader);
            long pipId = ReadPipId(reader);
            string? messageCountSemaphoreName = ReadChars(reader);
            string? messageSentCountSemaphoreName = OperatingSystemHelper.IsWindowsOS ? ReadChars(reader) : null;

            byte[] sealedManifestTreeBlock;

            // TODO: Check perf. a) if this is a good buffer size, b) if the buffers should be pooled (now they are just allocated and thrown away)
            using (var ms = new MemoryStream(4096))
            {
                stream.CopyTo(ms);
                sealedManifestTreeBlock = ms.ToArray();
            }

            return new FileAccessManifest(new PathTable(), directoryTranslator, childProcessesToBreakAwayFromSandbox)
            {
                InternalDetoursErrorNotificationFile = internalDetoursErrorNotificationFile,
                PipId = pipId,
                m_fileAccessManifestFlag = fileAccessManifestFlag,
                m_fileAccessManifestExtraFlag = fileAccessManifestExtraFlag,
                m_sealedManifestTreeBlock = sealedManifestTreeBlock,
                m_messageCountSemaphoreName = messageCountSemaphoreName,
                m_messageSentCountSemaphoreName = messageSentCountSemaphoreName,
            };
        }

        private void HydrateTreeNodeIfNeeded()
        {
            if (IsManifestTreeHydrated)
            {
                return;
            }
            
            using (var stream = new MemoryStream(m_sealedManifestTreeBlock!))
            using (var reader = new BinaryReader(stream, Encoding.Unicode, leaveOpen: true))
            {
                (m_rootNode, _) = Node.InternalDeserialize(reader);
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
                Contract.Assert(m_sealedManifestTreeBlock is not null);
                return m_sealedManifestTreeBlock;
            }

            // start with 4 KB of memory (one page), which will expand as necessary
            // stream will be disposed by the BinaryWriter when it goes out of scope
            using var stream = new MemoryStream(4096);
            using var writer = new BinaryWriter(stream, Encoding.Unicode, true);
            m_rootNode.Serialize(writer);
            var bytes = stream.ToArray();
            return bytes;
        }

        /// <summary>
        /// Get an enumeration of lines representing the manifest tree.
        /// </summary>
        /// <returns>The line-by-line string representation of the manifest (formatted as a pre-order tree).</returns>
        public IEnumerable<string> Describe()
        {
            return m_rootNode.Describe();
        }

        // CODESYNC: DataTypes.h
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
            IgnoreCreateProcessReport = 0x2000000,
            UseLargeEnumerationBuffer = 0x4000000,
            IgnorePreloadedDlls = 0x8000000,
            EnforceAccessPoliciesOnDirectoryCreation = 0x10000000,
            ProbeDirectorySymlinkAsDirectory = 0x20000000,
            IgnoreFullReparsePointResolving = 0x40000000
        }

        // CODESYNC: DataTypes.h
        [Flags]
        internal enum FileAccessManifestExtraFlag
        {
            NoneExtra = 0,
            ExplicitlyReportDirectoryProbes = 0x1,
            PreserveFileSharingBehaviour = 0x2,
            EnableLinuxPTraceSandbox = 0x4,
            EnableLinuxSandboxLogging = 0x8,
            AlwaysRemoteInjectDetoursFrom32BitProcess = 0x10,
            UnconditionallyEnableLinuxPTraceSandbox = 0x20,
            IgnoreDeviceIoControlGetReparsePoint = 0x40,
            IgnoreUntrackedPathsInFullReparsePointResolving = 0x80,
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

            public static readonly PaddedByteString Invalid = default;

            public bool IsValid => m_value is not null;

            public void Serialize(BinaryWriter writer)
            {
                Contract.Requires(IsValid);

                using (PooledObjectWrapper<byte[]> wrapper = Pools.GetByteArray(Length))
                {
                    byte[] bytes = wrapper.Instance;
                    m_encoding.GetBytes(m_value, 0, m_value.Length, bytes, 0);
                    writer.Write(bytes, 0, Length);
                }
            }

            public PaddedByteString(Encoding encoding, string str)
            {
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
                HashCode = ProcessUtilities.NormalizeAndHashPath(path, out Bytes);
            }

            public NormalizedPathString(byte[] utf16EncodedBytes, int hashCode)
            {
                Bytes = utf16EncodedBytes;
                HashCode = hashCode;
            }

            public bool IsValid => Bytes is not null;

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

            public static byte[] DeserializeBytes(BinaryReader reader)
            {
                // This is a UTF16-byte encoded array, terminating with a null character
                // That means two consecutive 0-bytes representing null
                var bytes = new List<byte>();
                
                bytes.Add(reader.ReadByte());
                bytes.Add(reader.ReadByte());

                bool fourByteAligned = false;

                while (bytes[bytes.Count - 1] != 0 || bytes[bytes.Count - 2] != 0)
                {
                    bytes.Add(reader.ReadByte());
                    bytes.Add(reader.ReadByte());
                    fourByteAligned = !fourByteAligned;
                }

                // The encoded byte array is 4-byte aligned, and padding is there if needed
                if (!fourByteAligned)
                {
                    var pad = reader.ReadUInt16();
                    Contract.Assert(pad == 0);
                }

                return bytes.ToArray();
            }


            public override string ToString()
            {
                return IsValid ? Encoding.Unicode.GetString(Bytes, 0, Bytes.Length - sizeof(char)) : "<invalid>";
            }

            public override bool Equals(object? obj)
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
            private Dictionary<NormalizedPathString, Node>? m_children;

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
            internal IEnumerable<Node>? Children => m_children?.Values;

            /// <summary>
            /// Releases all children nodes, allowing all that memory to be reclaimed by the garbage collector.
            /// </summary>
            internal void ReleaseChildren()
            {
                m_children?.Clear();
            }

            /// <summary>
            /// The path ID as understood by the owning path table.
            /// </summary>
            public AbsolutePath PathId => m_pathId;

            /// <summary>
            /// If not <code>NoUsn</code>, check that when file is successfully opened, its initial USN is the expected USN.
            /// </summary>
            public Usn ExpectedUsn { get; private set; }

            /// <summary>
            /// The policy for the cone defined by the current node.
            /// </summary>
            public FileAccessPolicy ConePolicy => m_conePolicy;

            private void FinalizeConePolicy(FileAccessPolicy conePolicy)
            {
                m_conePolicy = conePolicy;
                m_isPolicyFinalized = true;
            }

            /// <summary>
            /// The final policy for the current node
            /// </summary>
            public FileAccessPolicy NodePolicy => m_nodePolicy;

            private void FinalizeNodePolicy(FileAccessPolicy nodePolicy)
            {
                m_nodePolicy = nodePolicy;
                m_isPolicyFinalized = true;
            }

            /// <summary>
            /// Use this to determine whether the policy has been finalized. All Nodes should have
            /// their policies finalized before serialization.
            /// </summary>
            internal bool IsPolicyFinalized => m_isPolicyFinalized;

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
                Contract.Requires(path.IsValid);

                Node leaf = AddPath(owner, path);
                leaf.ApplyNodeFileAccess(scope, expectedUsn);
            }

            /// <summary>
            /// Returns the lowest node in the node tree that contains the given path
            /// </summary>
            public Node FindNodeFor(FileAccessManifest owner, AbsolutePath pathToLookUp)
            {
                Contract.Requires(pathToLookUp.IsValid);

                if (PathId == pathToLookUp)
                {
                    return this;
                }

                var container = pathToLookUp.GetParent(owner.PathTable);
                var node = container.IsValid ? 
                    FindNodeFor(owner, container) : 
                    this;

                if (node.TryGetChild(owner, pathToLookUp, out var result, out _))
                {
                    return result;
                }

                return node;
            }

            private Node GetOrCreateChild(FileAccessManifest owner, AbsolutePath path)
            {
                if (TryGetChild(owner, path, out Node? child, out NormalizedPathString normalizedFragment))
                {
                    return child;
                }

                m_children ??= new Dictionary<NormalizedPathString, Node>();

                child = new Node(path);
                m_children.Add(normalizedFragment, child);

                return child;
            }

            private bool TryGetChild(FileAccessManifest owner, AbsolutePath path, [NotNullWhen(true)] out Node? node, out NormalizedPathString normalizedFragment)
            {
                Contract.Requires(path.IsValid);

                StringId fragment = path.GetName(owner.PathTable).StringId;

                // We cache normalized fragments to avoid excessive memory allocations;
                // in particular, we can avoid the allocation associated with creating a new NormalizedPathString
                // for all fragments recurring in nested path prefixes.
                if (!owner.m_normalizedFragments.TryGetValue(fragment, out normalizedFragment))
                {
                    owner.m_normalizedFragments.Add(
                        fragment,
                        normalizedFragment = new NormalizedPathString(owner.PathTable.StringTable.GetString(fragment)));
                }

                node = null;
                if (m_children?.TryGetValue(normalizedFragment, out node) == true)
                {
                    return true;
                }

                return false;
            }

            // CODESYNC: DataTypes.h
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
                Contract.Requires(path.IsValid);

                var container = path.GetParent(owner.PathTable);
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

                if (m_children is not null)
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

            /// <nodoc/>
            public static (Node, NormalizedPathString) InternalDeserialize(BinaryReader reader)
            {
#if DEBUG
                uint foodCafe = reader.ReadUInt32(); // "food cafe"
                Contract.Assert((uint)0xF00DCAFE == foodCafe);
#endif
                uint normalizedFragmentHash = reader.ReadUInt32();
                uint conePolicy = reader.ReadUInt32();
                uint nodePolicy = reader.ReadUInt32();
                uint pathIdValue = reader.ReadUInt32();
                ulong expectedUsnValue = reader.ReadUInt64();
                uint bucketCount = reader.ReadUInt32();

                int childrenCount = 0;
                for (int i = 0; i < bucketCount; i++)
                {
                    uint offset = reader.ReadUInt32();
                    // An offset with 0 means no child was stored there
                    if (offset != 0)
                    {
                        childrenCount++;
                    }
                }

                unchecked
                {
                    var normalizedPathString = new NormalizedPathString(NormalizedPathString.DeserializeBytes(reader), (int)normalizedFragmentHash);

                    var node = new Node(new AbsolutePath((int)pathIdValue));
                    node.m_conePolicy = (FileAccessPolicy)conePolicy;
                    node.m_nodePolicy = (FileAccessPolicy)nodePolicy;
                    node.ExpectedUsn = new Usn(expectedUsnValue);
                    node.m_isPolicyFinalized = true;

                    if (childrenCount > 0)
                    {
                        node.m_children = new Dictionary<NormalizedPathString, Node>(childrenCount);
                    }

                    for (int i = 0; i < childrenCount; i++)
                    {
                        (Node child, NormalizedPathString childNormalizedPathString) = InternalDeserialize(reader);
                        node.m_children![childNormalizedPathString] = child;
                    }

                    return (node, normalizedPathString);
                }
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

                    var childCount = (uint)(m_children is null ? 0 : m_children.Count);

                    // The children will be added to a hash-table.
                    // As it is known that hash-table performance starts to degrade with load factors > 0.7,
                    // we size our hash-table appropriately.
                    var bucketCount = childCount == 0 ? 0 : checked((uint)(childCount / 0.7));
                    Contract.Assert((bucketCount == 0) == (childCount == 0));
                    writer.Write(bucketCount);

                    long offsetsStart = 0;
                    if (m_children is not null)
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

                    if (m_children is not null)
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
                        using var wrapper = Pools.GetStringBuilder();
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
                            var expectedUsn = new Usn(reader.ReadUInt64());

                            int hashtableCount = reader.ReadInt32();
                            var absoluteChildStarts = new List<long>();
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

                            if (partialPath.Length > 0)
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

        // CODESYNC: DetoursServices.h :: BreakawayChildProcess struct
        /// <summary>
        /// A container for a breakaway child process.
        /// </summary>
        /// <param name="ProcessName">The breakaway child process name.</param>
        /// <param name="RequiredCommandLineArgsSubstring">Optionally, the substring that the command line arguments to <paramref name="ProcessName"/> must contain for it to breakaway.</param>
        /// <param name="CommandLineArgsSubstringContainmentIgnoreCase">Whether to ignore case when checking if the command line arguments
        /// to <paramref name="ProcessName"/> contain <paramref name="RequiredCommandLineArgsSubstring"/> (if <paramref name="RequiredCommandLineArgsSubstring"/> is not <see langword="null"/>).
        /// Defaults to <see langword="false"/>.</param>
        /// <remarks>
        /// This record is conceptually equivalent to IBreakawayChildProcess defined in the Configuration dll. The reason for defining it here again is that this Process dll cannot have 
        /// extra dependencies (see BuildXL.Processes.dsc for details).
        /// </remarks>
        public record BreakawayChildProcess(string ProcessName, string? RequiredCommandLineArgsSubstring = null, bool CommandLineArgsSubstringContainmentIgnoreCase = false);

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
                    Tuple.Create((short)FileAccessPolicy.AllowReadIfNonexistent, "ReadIfNonexistent"),
                    Tuple.Create((short)FileAccessPolicy.AllowCreateDirectory, "CreateDirectory"),
                    Tuple.Create((short)FileAccessPolicy.AllowSymlinkCreation, "CreateSymlink"),
                    Tuple.Create((short)FileAccessPolicy.AllowRealInputTimestamps, "RealInputTimestamps"),
                    Tuple.Create((short)FileAccessPolicy.OverrideAllowWriteForExistingFiles, "OverrideAllowWriteForExistingFiles"),
                    Tuple.Create((short)FileAccessPolicy.TreatDirectorySymlinkAsDirectory, "DirectorySymlinkAsDirectory"),
                    Tuple.Create((short)FileAccessPolicy.EnableFullReparsePointParsing, "EnableFullReparsePointParsing"),
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
                    if (pair.Item1 != 0 && (i & pair.Item1) == pair.Item1
                        || pair.Item1 == 0 && i == 0)
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
