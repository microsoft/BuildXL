// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using static BuildXL.Utilities.FormattableStringEx;

#nullable enable

namespace BuildXL.Native.Processes.Windows
{
    /// <summary>
    /// Container related methods for setting up virtualization filters
    /// </summary>
    public partial class ProcessUtilitiesWin
    {
        private const string s_containerDescription = "<container></container>";
        private static Lazy<IntPtr> s_containerDescriptionHandle = new (() => CreateCommonDefaultContainerDescription());

        // Win32 errors
        private const int ERROR_SERVICE_ALREADY_RUNNING = 0x00000420;
        private const int ERROR_ALREADY_EXISTS = 0x000000B7;

        // HResult errors
        private const int ERROR_FLT_INSTANCE_NOT_FOUND = unchecked ((int)0x801F0015);

        // This is to work around a problem in the WCI driver. Setting up the driver sporadically fails, but a retry seems to mitigate the problem
        // considerably.
        private const int s_wciRetries = 5;

        /// <summary><see cref="ProcessUtilities.IsWciAndBindFiltersAvailable()"/></summary>
        public bool IsWciAndBindFiltersAvailable()
        {
            // We depend on the RS6 DLLs to be present. So let's check they are there and have the right version.
            // Observe we don't check for RS6 explicitly, hoping that downlevel installations may also work at some point
            try
            {
                if (!Version.TryParse(FileVersionInfo.GetVersionInfo(NativeContainerUtilities.WciDllLocation).ProductVersion, out var wciVersion) || 
                    wciVersion < NativeContainerUtilities.MinimumRequiredVersion)
                {
                    return false;
                }

                if (!Version.TryParse(FileVersionInfo.GetVersionInfo(NativeContainerUtilities.BindDllLocation).ProductVersion, out var bindVersion) || 
                    bindVersion < NativeContainerUtilities.MinimumRequiredVersion)
                {
                    return false;
                }
            }
            catch(FileNotFoundException)
            {
                return false;
            }

            // Now let's check if the required drivers can actually be loaded. This will also check that the required elevation is present.
            var result = LoadFilterWithPrivilege(NativeContainerUtilities.WciDriverName);
            if (!LoadFilterSuccessful(result))
            {
                return false;
            }

            result = LoadFilterWithPrivilege(NativeContainerUtilities.BindDriverName);
            return LoadFilterSuccessful(result);
        }

        private static int LoadFilterWithPrivilege(string filterName)
        {
            try
            {
                NativeContainerUtilities.RtlImpersonateSelf(NativeContainerUtilities.SecurityImpersonationLevel.SecurityImpersonation, 0, out _);
                NativeContainerUtilities.RtlAdjustPrivilege(NativeContainerUtilities.Priviledge.SE_LOAD_DRIVER_PRIVILEGE, true, true, out _);

                var result = NativeContainerUtilities.FilterLoad(filterName);
                return result;
            }
            finally
            {
                NativeContainerUtilities.RevertToSelf();
            }
        }

        /// <summary><see cref="ProcessUtilities.AttachContainerToJobObject(IntPtr, IReadOnlyDictionary{ExpandedAbsolutePath, IReadOnlyList{ExpandedAbsolutePath}}, bool, IEnumerable{string}, NativeContainerUtilities.BfSetupFilterFlags, Action{IntPtr, ICollection{string}}, out IEnumerable{string})"/></summary>
        public void AttachContainerToJobObject(
            IntPtr hJob,
            IReadOnlyDictionary<ExpandedAbsolutePath, IReadOnlyList<ExpandedAbsolutePath>> redirectedDirToSourceDirsMap,
            bool enableWciFilter,
            IEnumerable<string> bindFltExclusions,
            NativeContainerUtilities.BfSetupFilterFlags bindFltFlags,
            Action<IntPtr, ICollection<string>>? customJobObjectCustomization,
            out IEnumerable<string> warnings)
        {
            try
            {
                // Activate container functionality on the job object. Used by both WCI and Bind.
                if (NativeContainerUtilities.WcCreateContainer(hJob, s_containerDescriptionHandle.Value, isServerSilo: false) != 0)
                {
                    throw new NativeWin32Exception(Marshal.GetLastWin32Error(), I($"Unable to create a container for job object {hJob}."));
                }

                var warningList = new List<string>();
                if (customJobObjectCustomization is null)
                {
                    ConfigureContainer(hJob, redirectedDirToSourceDirsMap, enableWciFilter, warningList, bindFltExclusions, bindFltFlags);
                }
                else
                {
                    customJobObjectCustomization(hJob, warningList);
                }

                warnings = warningList;
            }
            catch(NativeWin32Exception ex)
            {
                throw new BuildXLException("Unable to create container.", ex);
            }
        }

        /// <summary><see cref="ProcessUtilities.TryCleanUpContainer"/></summary>
        public bool TryCleanUpContainer(
            IntPtr hJob,
            Action<IntPtr, ICollection<string>>? customJobObjectCleanup,
            out IEnumerable<string> errors)
        {
            var cleanUpErrors = new List<string>();

            customJobObjectCleanup?.Invoke(hJob, cleanUpErrors);

            var result = NativeContainerUtilities.WcCleanupContainer(hJob, null);
            if (result != 0)
            {
                cleanUpErrors.Add(I($"Cannot clean up container for job object {hJob}. Details: {NativeWin32Exception.GetFormattedMessageForNativeErrorCode(result)}"));
            }

            errors = cleanUpErrors;
            return cleanUpErrors.Count == 0;
        }

        private static IntPtr CreateCommonDefaultContainerDescription()
        {
            if (NativeContainerUtilities.WcCreateDescriptionFromXml(s_containerDescription, out IntPtr descriptionHandle) != 0)
            {
                throw new NativeWin32Exception(Marshal.GetLastWin32Error(), I($"Unable to create a common container description."));
            }

            return descriptionHandle;
        }

        private static bool LoadFilterSuccessful(int win32Error)
        {
            // The result of trying to load the filter should be either S_OK or the filter should be there/already running
            return win32Error == 0 || win32Error == ExceptionUtilities.HResultFromWin32(ERROR_ALREADY_EXISTS) || win32Error == ExceptionUtilities.HResultFromWin32(ERROR_SERVICE_ALREADY_RUNNING);
        }

        private static void ConfigureContainer(
            IntPtr hJob,
            IReadOnlyDictionary<ExpandedAbsolutePath, IReadOnlyList<ExpandedAbsolutePath>> redirectedDirToSourceDirsMap,
            bool enableWciFilter,
            List<string> wciRetries,
            IEnumerable<string> bindFltExclusions,
            NativeContainerUtilities.BfSetupFilterFlags bindFltFlags)
        {
            // USE_CURRENT_SILO_MAPPING has to be passed when WCI and Bind are configured for the same silo (job object).
            if (enableWciFilter)
            {
                bindFltFlags |= NativeContainerUtilities.BfSetupFilterFlags.BINDFLT_FLAG_USE_CURRENT_SILO_MAPPING;
            }

            foreach (var kvp in redirectedDirToSourceDirsMap)
            {
                IReadOnlyCollection<ExpandedAbsolutePath> sourcePaths = kvp.Value;
                string destinationPath = kvp.Key.ExpandedPath;

                if (enableWciFilter)
                {
                    ConfigureWciFilter(hJob, sourcePaths, destinationPath, wciRetries);
                }

                // Configure only those exclusions that fit under any of the source paths.
                IEnumerable<string> sourceSpecificExclusions = bindFltExclusions
                    .Where(e => sourcePaths.Any(s => e.StartsWith(s.ExpandedPath, StringComparison.OrdinalIgnoreCase)));

                ConfigureBindFilter(hJob, sourcePaths, destinationPath, sourceSpecificExclusions, bindFltFlags);
            }
        }

        private static void ConfigureWciFilter(IntPtr hJob, IReadOnlyCollection<ExpandedAbsolutePath> sourcePaths, string destinationPath, List<string> wciRetries)
        {
            var layerDescriptors = new NativeContainerUtilities.WC_LAYER_DESCRIPTOR[sourcePaths.Count];
            
            int i = 0;
            foreach (ExpandedAbsolutePath sourcePath in sourcePaths)
            {
                var layerDescriptor = new NativeContainerUtilities.WC_LAYER_DESCRIPTOR
                {
                    // By default we always set the layer so it inherits the descriptor. This 
                    // makes hardlinks (ACLed for deny-write) and exposed via copy-on-write writable
                    // The sparse flag is not set explicitly since we use the general sparse isolation mode
                    // which implies that each layer will be sparse
                    Flags = NativeContainerUtilities.LayerDescriptorFlags.InheritSecurity,
                    LayerId = NativeContainerUtilities.ToGuid(Guid.NewGuid()),
                    Path = sourcePath.ExpandedPath,
                };
                layerDescriptors[i] = layerDescriptor;
                i++;
            }

            // Set a WCI reparse point into the filesystem connecting the destination (container scratch dir)
            // to the topmost layer.
            var reparsePointData = new NativeContainerUtilities.WC_REPARSE_POINT_DATA
            {
                Flags = 0,
                LayerId = layerDescriptors[0].LayerId,
                NameLength = 0,
                Name = string.Empty
            };

            int hresult = NativeContainerUtilities.WciSetReparsePointData(
                destinationPath,
                ref reparsePointData,
                (UInt16)Marshal.OffsetOf(typeof(NativeContainerUtilities.WC_REPARSE_POINT_DATA), "Name"));
            if (hresult != 0)
            {
                throw new NativeWin32Exception(Marshal.GetLastWin32Error(), I($"Unable to setup the reparse point data for '{destinationPath}'."));
            }

            hresult = -1;
            // We try to setup the WCI filter on a retry loop to work around an existing issue
            // in the driver behavior, where the setup sometimes fails.
            var retries = s_wciRetries;
            while (hresult != 0 && retries > 0)
            {
                // Isolation is set to hard because we want the virtualized source path to be
                // completely isolated (i.e. with 'hard' we will get copy-on-write behavior and
                // tombstones when any deletion happens inside the container).
                // Set as sparse so all layers recursively merge without needing an explicit reparse point
                // for each of them.
                hresult = NativeContainerUtilities.WciSetupFilter(
                    hJob,
                    NativeContainerUtilities.WC_ISOLATION_MODE.IsolationModeSparseHard,
                    destinationPath,
                    layerDescriptors,
                    (uint)layerDescriptors.Length,
                    NativeContainerUtilities.WC_NESTING_MODE.WcNestingModeInner);

                retries--;
                if (hresult != 0)
                {
                    wciRetries.Add(I($"Error setting WCI filter for {hJob} from '{string.Join(Environment.NewLine, sourcePaths)}' to '{destinationPath}'. Retries left: {retries}. Details: {NativeWin32Exception.GetFormattedMessageForNativeErrorCode(hresult)}"));
                }
            }

            if (hresult != 0)
            {
                throw new NativeWin32Exception(hresult, I($"Unable to setup the WCI filter for source paths '{string.Join(Environment.NewLine, sourcePaths)}' to destination path '{destinationPath}'. Details: {NativeWin32Exception.GetFormattedMessageForNativeErrorCode(hresult)}"));
            }
        }

        private static void ConfigureBindFilter(
            IntPtr hJob,
            IReadOnlyCollection<ExpandedAbsolutePath> sourcePaths,
            string targetPath,
            IEnumerable<string> exclusionPaths,
            NativeContainerUtilities.BfSetupFilterFlags bindFltFlags)
        {
            string[] exclusionsForMapping = exclusionPaths.ToArray();
            foreach (ExpandedAbsolutePath sourcePath in sourcePaths)
            {
                var hresult = NativeContainerUtilities.BfSetupFilter(
                    hJob,
                    bindFltFlags,
                    sourcePath.ExpandedPath,
                    targetPath,
                    exclusionsForMapping,
                    (ulong) exclusionsForMapping.Length);

                if (hresult != 0)
                {
                    throw new NativeWin32Exception(Marshal.GetLastWin32Error(), I($"Unable to setup the Bind filter from '{sourcePath}' to '{targetPath}'."));
                }
            }
        }
    }
}
