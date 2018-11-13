// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;
using BuildXL.Utilities;
using JetBrains.Annotations;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// White List entry
    /// </summary>
    public interface IUnsafeSandboxConfiguration
    {
        /// <summary>
        /// Whether to preserve the existing output file from a previous invocation of a process before invoking it.
        /// Preserving output files can be a source of non-determinism since the behavior of the process can change based
        /// on the state of the outputs.
        /// </summary>
        PreserveOutputsMode PreserveOutputs { get; }

        /// <summary>
        /// Whether to monitor file accesses of individual tools at all. Disabling monitoring results in an unsafe configuration (for diagnostic purposes only). Defaults to on.
        /// </summary>
        bool MonitorFileAccesses { get; }

        /// <summary>
        /// Whether to detour the ZwRenameFileInformation API. Not detouring ZwRenameFileInformation is an unsafe configuration. Defaults to off (i.e., detour the ZwRenameFileInformation API).
        /// </summary>
        bool IgnoreZwRenameFileInformation { get; }

        /// <summary>
        /// Whether to detour the ZwOtherFileInformation API. Not detouring ZwOtherFileInformation is an unsafe configuration. Defaults to on (i.e., don't detour the ZwOtherFileInformation API).
        /// </summary>
        bool IgnoreZwOtherFileInformation { get; }

        /// <summary>
        /// Whether to detour the follow symlinks for APIs different than CreateFile and NtCreate/OpenFile APIs. Not detouring ZwOtherFileInformation is an unsafe configuration. Defaults to on (i.e., don't follow symlinks for these APIs).
        /// </summary>
        bool IgnoreNonCreateFileReparsePoints { get; }

        /// <summary>
        /// Whether to detour the SetFileInformationByhandle API. Not detouring SetFileInformationByHandle is an unsafe configuration. Defaults to off (i.e., detour the SetFileInformationByHandle API).
        /// </summary>
        bool IgnoreSetFileInformationByHandle { get; }

        /// <summary>
        /// Whether to ignore reparse points. Ignoring reparse points is an unsafe configuration. Defaults to off (i.e., not ignoring reparse points).
        /// </summary>
        bool IgnoreReparsePoints { get; }

        /// <summary>
        /// Whether to ignore Dlls loaded before Detours was started. Ignoring the preloaded (statically loaded) dlls is an unsafe configuration. Defaults to on (i.e., ignoring preloaded Dlls).
        /// </summary>
        bool IgnorePreloadedDlls { get; }

        /// <summary>
        /// Whether to treat existing directory probes as enumerations. This could lead to cases of overbuilding. Defaults to on (i.e., existing directory probes are hanled as enumeration).
        /// TODO: temporarily making the default true until WDG sets the flags or let us remove the flag completely.
        /// </summary>
        /// <remarks>
        /// Overbuilding could happen when you have directory that is just being probed for existence, but it is treated as a directory enumeration.
        /// If a temp (irrelevant) directory or file is added to that directory, we are rebuilding the pips that declared a enumeration dependency on the directory.
        /// </remarks>
        bool ExistingDirectoryProbesAsEnumerations { get; }

        /// <summary>
        /// Monitor files opened for read by NtCreateFile
        /// </summary>
        bool MonitorNtCreateFile { get; }

        /// <summary>
        /// Monitor files opened for read by ZwCreateFile or ZwOpenFile
        /// </summary>
        bool MonitorZwCreateOpenQueryFile { get; }

        /// <summary>
        /// The kind of process sandbox to use
        /// </summary>
        SandboxKind SandboxKind { get; }

        /// <summary>
        /// When enabled, if a tool accesses a file that was not declared in the specification dependencies, it is treated as an error instead of a warning. Turning this
        /// option off results in an unsafe configuration (for diagnostic purposes only). Defaults to on.
        /// </summary>
        bool UnexpectedFileAccessesAreErrors { get; }

        /// <summary>
        /// Whether to detour the GetFinalPathNameByHandle API. Not detouring GetFinalPathNameByHandle is an unsafe configuration. Default to off (i.e., Detour the GetFinalPathNameByHandle API).
        /// </summary>
        bool IgnoreGetFinalPathNameByHandle { get; }

        /// <summary>
        /// Whether to flag writes under opaque directories (exclusive or shared) that make existing absent probes to become present probes.
        /// </summary>
        bool IgnoreDynamicWritesOnAbsentProbes { get; }

        // NOTE: if you add a property here, don't forget to update UnsafeSandboxConfigurationExtensions

        // NOTE: whenever unsafe options change, the fingerprint version needs to be bumped
    }

    /// <summary>
    /// Extension methods for <see cref="IUnsafeSandboxConfiguration"/>.
    /// </summary>
    public static class UnsafeSandboxConfigurationExtensions
    {
        /// <summary>
        /// Defaults that are consider "safe".
        /// </summary>
        public readonly static IUnsafeSandboxConfiguration SafeDefaults = Mutable.UnsafeSandboxConfiguration.SafeOptions;

        /// <summary>
        /// Returns whether sandboxing is disabled.
        /// </summary>
        public static bool DisableDetours(this IUnsafeSandboxConfiguration @this)
        {
            return @this.SandboxKind == SandboxKind.None;
        }

        /// <nodoc/>
        public static void Serialize(this IUnsafeSandboxConfiguration @this, BuildXLWriter writer)
        {
            writer.Write((byte)@this.SandboxKind);
            writer.Write(@this.ExistingDirectoryProbesAsEnumerations);
            writer.Write(@this.IgnoreGetFinalPathNameByHandle);
            writer.Write(@this.IgnoreNonCreateFileReparsePoints);
            writer.Write(@this.IgnoreReparsePoints);
            writer.Write(@this.IgnoreSetFileInformationByHandle);
            writer.Write(@this.IgnoreZwOtherFileInformation);
            writer.Write(@this.IgnoreZwRenameFileInformation);
            writer.Write(@this.MonitorFileAccesses);
            writer.Write(@this.MonitorNtCreateFile);
            writer.Write(@this.MonitorZwCreateOpenQueryFile);
            writer.Write((byte)@this.PreserveOutputs);
            writer.Write(@this.UnexpectedFileAccessesAreErrors);
            writer.Write(@this.IgnorePreloadedDlls);
            writer.Write(@this.IgnoreDynamicWritesOnAbsentProbes);
        }

        /// <nodoc/>
        public static IUnsafeSandboxConfiguration Deserialize(BuildXLReader reader)
        {
            return new Mutable.UnsafeSandboxConfiguration()
            {
                SandboxKind = (SandboxKind)reader.ReadByte(),
                ExistingDirectoryProbesAsEnumerations = reader.ReadBoolean(),
                IgnoreGetFinalPathNameByHandle = reader.ReadBoolean(),
                IgnoreNonCreateFileReparsePoints = reader.ReadBoolean(),
                IgnoreReparsePoints = reader.ReadBoolean(),
                IgnoreSetFileInformationByHandle = reader.ReadBoolean(),
                IgnoreZwOtherFileInformation = reader.ReadBoolean(),
                IgnoreZwRenameFileInformation = reader.ReadBoolean(),
                MonitorFileAccesses = reader.ReadBoolean(),
                MonitorNtCreateFile = reader.ReadBoolean(),
                MonitorZwCreateOpenQueryFile = reader.ReadBoolean(),
                PreserveOutputs = (PreserveOutputsMode)reader.ReadByte(),
                UnexpectedFileAccessesAreErrors = reader.ReadBoolean(),
                IgnorePreloadedDlls = reader.ReadBoolean(),
                IgnoreDynamicWritesOnAbsentProbes = reader.ReadBoolean(),
            };
        }

        /// <summary>
        /// Returns <code>true</code> if <paramref name="lhs"/> does not contain a single unsafe value that is not present in <paramref name="rhs"/>.
        /// </summary>
        public static bool IsAsSafeOrSaferThan(this IUnsafeSandboxConfiguration lhs, IUnsafeSandboxConfiguration rhs)
        {
            return IsAsSafeOrSafer(lhs.DisableDetours(), rhs.DisableDetours(), SafeDefaults.DisableDetours())
                && IsAsSafeOrSafer(lhs.ExistingDirectoryProbesAsEnumerations, rhs.ExistingDirectoryProbesAsEnumerations, SafeDefaults.ExistingDirectoryProbesAsEnumerations)
                && IsAsSafeOrSafer(lhs.IgnoreGetFinalPathNameByHandle, rhs.IgnoreGetFinalPathNameByHandle, SafeDefaults.IgnoreGetFinalPathNameByHandle)
                && IsAsSafeOrSafer(lhs.IgnoreNonCreateFileReparsePoints, rhs.IgnoreNonCreateFileReparsePoints, SafeDefaults.IgnoreNonCreateFileReparsePoints)
                && IsAsSafeOrSafer(lhs.IgnoreReparsePoints, rhs.IgnoreReparsePoints, SafeDefaults.IgnoreReparsePoints)
                && IsAsSafeOrSafer(lhs.IgnoreSetFileInformationByHandle, rhs.IgnoreSetFileInformationByHandle, SafeDefaults.IgnoreSetFileInformationByHandle)
                && IsAsSafeOrSafer(lhs.IgnoreZwOtherFileInformation, rhs.IgnoreZwOtherFileInformation, SafeDefaults.IgnoreZwOtherFileInformation)
                && IsAsSafeOrSafer(lhs.IgnoreZwRenameFileInformation, rhs.IgnoreZwRenameFileInformation, SafeDefaults.IgnoreZwRenameFileInformation)
                && IsAsSafeOrSafer(lhs.MonitorFileAccesses, rhs.MonitorFileAccesses, SafeDefaults.MonitorFileAccesses)
                && IsAsSafeOrSafer(lhs.MonitorNtCreateFile, rhs.MonitorNtCreateFile, SafeDefaults.MonitorNtCreateFile)
                && IsAsSafeOrSafer(lhs.MonitorZwCreateOpenQueryFile, rhs.MonitorZwCreateOpenQueryFile, SafeDefaults.MonitorZwCreateOpenQueryFile)
                && IsAsSafeOrSafer(lhs.UnexpectedFileAccessesAreErrors, rhs.UnexpectedFileAccessesAreErrors, SafeDefaults.UnexpectedFileAccessesAreErrors)
                // Where's PreserveOutputs? The sandbox configuration setting globally decides whether preserve outputs.
                // Whether the current run is as safe or safer also depends on whether preserve outputs is allowed for
                // the pip in question. Because that requires pip specific details, that is determined in UnsafeOptions
                && IsAsSafeOrSafer(lhs.IgnorePreloadedDlls, rhs.IgnorePreloadedDlls, SafeDefaults.IgnorePreloadedDlls)
                && IsAsSafeOrSafer(lhs.IgnoreDynamicWritesOnAbsentProbes, rhs.IgnoreDynamicWritesOnAbsentProbes, SafeDefaults.IgnoreDynamicWritesOnAbsentProbes);
        }

        private static bool IsAllowMissingOutputFileNamesSafer(IReadOnlyList<string> lhsValue, IReadOnlyList<string> rhsValue)
        {
            return lhsValue == null || lhsValue.Count == 0 || lhsValue.All(e => rhsValue.Contains(e));
        }

        private static bool IsAsSafeOrSafer<T>(T lhsValue, T rhsValue, T safeValue) where T: struct
        {
            return
                EqualityComparer<T>.Default.Equals(lhsValue, safeValue) ||
                EqualityComparer<T>.Default.Equals(lhsValue, rhsValue);
        }

        private static void WriteReadOnlyList(BuildXLWriter writer, IReadOnlyList<string> list)
        {
            var count = list != null ? list.Count : 0;
            writer.WriteCompact(count);
            for (int i = 0; i < count; i++)
            {
                writer.Write(list[i]);
            }
        }

        private static List<string> ReadReadOnlyList(BuildXLReader reader)
        {
            var count = reader.ReadInt32Compact();
            var result = new List<string>(count);
            for (int i = 0; i < count; i++)
            {
                result.Add(reader.ReadString());
            }

            return result;
        }
    }
}
