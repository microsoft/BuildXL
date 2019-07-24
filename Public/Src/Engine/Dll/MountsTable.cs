// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Reflection;
using BuildXL.Engine.Tracing;
using BuildXL.Native.IO;
using BuildXL.Pips;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;
using JetBrains.Annotations;
using MacPaths = BuildXL.Interop.MacOS.IO;

namespace BuildXL.Engine
{
    /// <summary>
    /// Contains all the mounts state for runtime and evaluation purposes
    /// </summary>
    public sealed class MountsTable : BuildXL.FrontEnd.Sdk.MountsTableAbstraction
    {
        /// <summary>
        /// Mount path expander.
        /// </summary>
        public readonly MountPathExpander MountPathExpander;

        private readonly LoggingContext m_loggingContext;
        private readonly BuildXLContext m_context;

        // State during construction of the mounts table. Null after call to CompleteInitialization;
        private readonly ConcurrentDictionary<AbsolutePath, IMount> m_mountMapBuilder;
        private readonly ConcurrentDictionary<string, IMount> m_mountsByName;
        private readonly ConcurrentDictionary<IMount, LocationData> m_mountLocationsByMount;
        private readonly ConcurrentDictionary<IMount, PathAtom> m_mountPathIdentifiersByMount;
        private readonly ConcurrentDictionary<AbsolutePath, IMount> m_alternativeRoots;

        private readonly MountsTable m_parent;

        private bool m_mountAdditionError;

        /// <summary>
        /// Private constructor. Please use CreateAndRegister factory method.
        /// </summary>
        private MountsTable(LoggingContext loggingContext, BuildXLContext context, MountPathExpander mountPathExpander = null)
        {
            m_parent = null;
            m_loggingContext = loggingContext;
            m_context = context;
            MountPathExpander = mountPathExpander ?? new MountPathExpander(context.PathTable);

            m_mountMapBuilder = new ConcurrentDictionary<AbsolutePath, IMount>();
            m_mountsByName = new ConcurrentDictionary<string, IMount>(StringComparer.OrdinalIgnoreCase);
            m_mountPathIdentifiersByMount = new ConcurrentDictionary<IMount, PathAtom>();
            m_mountLocationsByMount = new ConcurrentDictionary<IMount, LocationData>();
            m_alternativeRoots = new ConcurrentDictionary<AbsolutePath, IMount>();
        }

        /// <summary>
        /// Private constructor used for creating module specific mount tables
        /// </summary>
        private MountsTable(MountsTable parent, ModuleId moduleId)
            : this(parent.m_loggingContext, parent.m_context)
        {
            m_parent = parent;
            MountPathExpander = parent.MountPathExpander.CreateModuleMountExpander(moduleId);
        }

        /// <summary>
        /// Factory method that creates a new MountsTable and registers the global values into the given environment.
        /// </summary>
        [SuppressMessage("Microsoft.Naming", "CA2204:LiteralsShouldBeSpelledCorrectly")]
        public static MountsTable CreateAndRegister(
            LoggingContext loggingContext,
            BuildXLContext context,
            IConfiguration configuration,
            [CanBeNull] IReadOnlyDictionary<string, string> properties)
        {
            Contract.Requires(context != null);
            Contract.Requires(configuration != null);
            Contract.Requires(loggingContext != null);

            ILayoutConfiguration layout = configuration.Layout;
            IReadOnlyList<IResolverSettings> resolverSettings = configuration.Resolvers;

            var table = new MountsTable(loggingContext, context);

            // If any resolver settings allows for a writable source directory, then the source directory is writable
            var writableSourceRoot = resolverSettings.Any(resolverSetting => resolverSetting.AllowWritableSourceDirectory);

            table.AddStaticMount("BuildEnginePath", layout.BuildEngineDirectory, isWriteable: false);
            table.AddStaticMount("SourceRoot", layout.SourceDirectory, isWriteable: writableSourceRoot);
            table.AddStaticMount("ObjectRoot", layout.ObjectDirectory, isWriteable: true, isScrubbable: true);
            table.AddStaticMount(
                "LogsDirectory", 
                configuration.Logging.RedirectedLogsDirectory.IsValid 
                ? configuration.Logging.RedirectedLogsDirectory 
                : configuration.Logging.LogsDirectory, 
                isWriteable: true, 
                allowCreateDirectory: true);

            if (layout.FrontEndDirectory.IsValid)
            {
                // This location is used only for storing nuget packages and generated specs so far.
                table.AddStaticMount("FrontEnd", layout.FrontEndDirectory, isWriteable: true, isScrubbable: false);
            }
            if (layout.TempDirectory.IsValid)
            {
                table.AddStaticMount("TempRoot", layout.TempDirectory, isWriteable: true, isScrubbable: true);
            }

            // Cross Plat supported MountPoints
            table.AddStaticSystemMount("ProgramData", Environment.SpecialFolder.CommonApplicationData);
            table.AddStaticSystemMount("ProgramFiles", Environment.SpecialFolder.ProgramFiles, trackSourceFileChanges: true);
            table.AddStaticSystemMount("System", Environment.SpecialFolder.System);
            if (!layout.RedirectedUserProfileJunctionRoot.IsValid)
            {
                table.AddStaticSystemMount("UserProfile", Environment.SpecialFolder.UserProfile);
                table.AddStaticSystemMount("AppData", Environment.SpecialFolder.ApplicationData, allowCreateDirectory : true);
                table.AddStaticSystemMount("LocalAppData", Environment.SpecialFolder.LocalApplicationData, allowCreateDirectory: true);
            }
            else
            {
                // User profile is redirected; need to use the paths specified in the env block.
                Contract.Assert(properties != null);
                RegisterRedirectedMount(context, properties, table, "UserProfile");
                RegisterRedirectedMount(context, properties, table, "AppData", allowCreateDirectory: true);
                RegisterRedirectedMount(context, properties, table, "LocalAppData", allowCreateDirectory: true);
            }

            if (!OperatingSystemHelper.IsUnixOS)
            {
                // Add system mounts that are Windows Only
                table.AddStaticSystemMount("Windows", Environment.SpecialFolder.Windows);
                table.AddStaticSystemMount("ProgramFilesX86", Environment.SpecialFolder.ProgramFilesX86, trackSourceFileChanges: true);
                table.AddStaticSystemMount("CommonProgramFiles", Environment.SpecialFolder.CommonProgramFiles, trackSourceFileChanges: true);
                table.AddStaticSystemMount("CommonProgramFilesX86", Environment.SpecialFolder.CommonProgramFilesX86, trackSourceFileChanges: true);
                
                if (!layout.RedirectedUserProfileJunctionRoot.IsValid)
                {
                    table.AddStaticSystemMount("InternetCache", Environment.SpecialFolder.InternetCache);
                    table.AddStaticSystemMount("InternetHistory", Environment.SpecialFolder.History);
                    table.AddStaticSystemMount("INetCookies", Environment.SpecialFolder.Cookies, allowCreateDirectory: true);
                    table.AddStaticSystemMount("LocalLow", FileUtilities.KnownFolderLocalLow);
                }
                else
                {
                    // User profile is redirected; need to use the paths specified in the env block.
                    Contract.Assert(properties != null);
                    RegisterRedirectedMount(context, properties, table, "InternetCache");
                    RegisterRedirectedMount(context, properties, table, "InternetHistory");
                    RegisterRedirectedMount(context, properties, table, "INetCookies", allowCreateDirectory: true);
                    RegisterRedirectedMount(context, properties, table, "LocalLow");
                }
            }
            else
            {
                table.AddStaticSystemMount("Applications", MacPaths.Applications, trackSourceFileChanges: true);
                table.AddStaticSystemMount("Bin", MacPaths.Bin, trackSourceFileChanges: true);
                table.AddStaticSystemMount("UsrBin", MacPaths.UsrBin, trackSourceFileChanges: true);
                table.AddStaticSystemMount("UsrInclude", MacPaths.UsrInclude, trackSourceFileChanges: true);
                table.AddStaticSystemMount("UsrLib", MacPaths.UsrLib, trackSourceFileChanges: true);
                table.AddStaticSystemMount("Library", MacPaths.Library, trackSourceFileChanges: true);
                table.AddStaticSystemMount("UserProvisioning", MacPaths.UserProvisioning, trackSourceFileChanges: true);
            }

            return table;
        }

        private static void RegisterRedirectedMount(
            BuildXLContext context, 
            IReadOnlyDictionary<string, string> properties, 
            MountsTable table, 
            string mountName, 
            string envVariable = null, 
            bool allowCreateDirectory = false)
        {
            envVariable = envVariable ?? mountName.ToUpperInvariant();

            if (!properties.TryGetValue(envVariable, out string redirectedPath))
            {
                Contract.Assert(false, $"Failed to register a redirected mount ('{mountName}') using a path defined by '{envVariable}' environment variable. The variable was not specified.");
            }

            table.AddStaticSystemMount(mountName, redirectedPath, allowCreateDirectory);

            // We don't need to add the real path of redirected mount into alternative roots.
            // Internally inside BuildXL itself, all accesses to user-related paths should go through the redirected paths by querying the mount table,
            // e.g. in DScript, one should use Context.getMount(...) to get user-related paths.
            // For any executed tool, if the tool accesses a user-related path, then the directory translation will translate that path into the redirected one.
            // We also don't want to add the real paths to the mount table because those paths will also go to the path expander that is part of the cached graph.
            // If the graph is used across builds, then the real user-related paths are not guaranteed to be the same.
        }

        /// <summary>
        /// Gets all mounts.
        /// </summary>
        public IEnumerable<IMount> AllMounts => m_mountPathIdentifiersByMount.Keys;

        /// <summary>
        /// Gets mappings from name to mount.
        /// </summary>
        public IReadOnlyDictionary<string, IMount> MountsByName => m_mountsByName;

        /// <summary>
        /// Adds a resolved mount as found by parsing the configuration file.
        /// </summary>
        public void AddResolvedMount(IMount mount, LocationData? mountLocation = null)
        {
            var location = mountLocation ?? mount.Location;

            if (!ValidateMount(mount, location))
            {
                m_mountAdditionError = true;
                return;
            }

            if (!VerifyMountAgainstParentMounts(mount))
            {
                m_mountAdditionError = true;
                return;
            }

            if (m_mountPathIdentifiersByMount.TryAdd(mount, mount.Name))
            {
                string mountName = mount.Name.ToString(m_context.StringTable);
                m_mountsByName[mountName] = mount;
                m_mountMapBuilder[mount.Path] = mount;
            }
        }

        private bool ValidateMount(IMount mount, LocationData location)
        {
            // Mount can be invalid if it is not specified properly in the DScript configuration.
            // For example, one can specify 'undefined' for mount's name or path. The undefined value gets
            // translated into invalid value by ConfigurationConverter.
            if (!mount.Name.IsValid)
            {
                Events.LogWithProvenance(
                       m_loggingContext,
                       Logger.Log.MountHasInvalidName,
                       m_context.PathTable,
                       location);
                return false;
            }

            if (!mount.Path.IsValid)
            {
                Events.LogWithProvenance(
                       m_loggingContext,
                       Logger.Log.MountHasInvalidPath,
                       m_context.PathTable,
                       location,
                       mount.Name.ToString(m_context.StringTable));
                return false;
            }

            return true;
        }

        private bool VerifyMountAgainstParentMounts(IMount childMount)
        {
            if (m_parent == null)
            {
                return true;
            }

            string mountName = childMount.Name.ToString(m_context.StringTable);
            IMount parentMount;
            if (m_parent.m_mountsByName.TryGetValue(mountName, out parentMount))
            {
                if (parentMount.Path != childMount.Path)
                {
                    LogRelatedMountError(
                        Logger.Log.ModuleMountsWithSameNameAsConfigMountsMustHaveSamePath,
                        childMount: childMount,
                        parentMount: parentMount);
                    return false;
                }
            }

            if (m_parent.m_mountMapBuilder.TryGetValue(childMount.Path, out parentMount))
            {
                if (!parentMount.Name.CaseInsensitiveEquals(m_context.StringTable, childMount.Name))
                {
                    LogRelatedMountError(
                        Logger.Log.ModuleMountsWithSamePathAsConfigMountsMustHaveSameName,
                        childMount: childMount,
                        parentMount: parentMount);
                    return false;
                }
            }

            var parentMountInfo = m_parent.MountPathExpander.GetSemanticPathInfo(childMount.Path);
            if (parentMountInfo.IsValid)
            {
                parentMount = m_parent.m_mountMapBuilder[parentMountInfo.Root];
                if (!parentMountInfo.IsWritable && childMount.IsWritable)
                {
                    LogRelatedMountError(
                        Logger.Log.NonWritableConfigMountsMayNotContainWritableModuleMounts,
                        childMount: childMount,
                        parentMount: parentMount);
                    return false;
                }

                if (!parentMountInfo.IsReadable && childMount.IsReadable)
                {
                    LogRelatedMountError(
                        Logger.Log.NonReadableConfigMountsMayNotContainReadableModuleMounts,
                        childMount: childMount,
                        parentMount: parentMount);
                    return false;
                }

                if (parentMountInfo.IsScrubbable && !childMount.IsScrubbable)
                {
                    LogRelatedMountError(
                        Logger.Log.ScrubbableMountsMayOnlyContainScrubbableMounts,
                        childMount: childMount,
                        parentMount: parentMount);
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Adds a system mount that is statically defined at the start of the build.
        /// </summary>
        private void AddStaticSystemMount(string name, Environment.SpecialFolder specialFolder, bool allowCreateDirectory = false, bool trackSourceFileChanges = false)
        {
            string folder = null;

            try
            {
                // Don't verify the path for the sake of performance and also because if the path is verified and doesn't
                // exist, an empty string will be returned. We want to unconditionally create the mount whether the backing
                // path exists or not.
                folder = SpecialFolderUtilities.GetFolderPath(specialFolder, Environment.SpecialFolderOption.DoNotVerify);
            }
            catch (ArgumentException)
            {
                Logger.Log.CouldNotAddSystemMount(m_loggingContext, name, folder);
                return;
            }

            AddStaticSystemMount(name, folder, allowCreateDirectory, trackSourceFileChanges);
        }

        /// <summary>
        /// Adds a system mount that is statically defined at the start fo the build
        /// </summary>
        private void AddStaticSystemMount(string name, Guid specialFolder, bool allowCreateDirectory = false, bool trackSourceFileChanges = false)
        {
            AddStaticSystemMount(name, FileUtilities.GetKnownFolderPath(specialFolder), allowCreateDirectory, trackSourceFileChanges);
        }

        /// <summary>
        /// Adds a system mount that is statically defined at the start fo the build
        /// </summary>
        private void AddStaticSystemMount(string name, string specialFolderPath, bool allowCreateDirectory = false, bool trackSourceFileChanges = false)
        {
            AbsolutePath folderPath;
            if (!AbsolutePath.TryCreate(m_context.PathTable, specialFolderPath, out folderPath))
            {
                Logger.Log.CouldNotAddSystemMount(m_loggingContext, name, specialFolderPath);
                return;
            }

            AddStaticMount(
                name,
                folderPath,
                isWriteable: false,
                isReadable: true,
                trackSourceFileChanges: trackSourceFileChanges,
                isSystem: true,
                allowCreateDirectory : allowCreateDirectory);
        }
        
        /// <summary>
        /// Adds a mount that is statically defined at the start fo the build
        /// </summary>
        private void AddStaticMount(
            string name,
            AbsolutePath path,
            bool isWriteable = true,
            bool isReadable = true,
            bool trackSourceFileChanges = true,
            bool isSystem = false,
            bool isScrubbable = false,
            bool allowCreateDirectory = false)
        {
            Contract.Requires(!string.IsNullOrEmpty(name));
            Contract.Requires(path.IsValid);

            var mount = new BuildXL.Utilities.Configuration.Mutable.Mount()
                        {
                            Name = PathAtom.Create(m_context.StringTable, name),
                            Path = path,
                            TrackSourceFileChanges = trackSourceFileChanges,
                            IsWritable = isWriteable,
                            IsReadable = isReadable,
                            IsSystem = isSystem,
                            IsScrubbable = isScrubbable,
                            AllowCreateDirectory = allowCreateDirectory
                        };

            var location = CreateToken(m_context);
            AddResolvedMount(mount, location);
        }

        /// <summary>
        /// Adds an alternative root that should be tokenized similarly to a mount defined by <paramref name="name"/>
        /// </summary>
        public void AddAlternativeRootToMount(string name, AbsolutePath root)
        {
            if (!m_mountsByName.TryGetValue(name, out var mount))
            {
                Contract.Assert(false, $"Tried adding an alternative root '{root.ToString(m_context.PathTable)}' to mount '{name}', however, this mount does not exist.");
            }

            if (m_alternativeRoots.TryGetValue(root, out var previouslyAssociatedMount))
            {
                Contract.Assert(false, $"Tried adding an alternative root '{root.ToString(m_context.PathTable)}' to mount '{name}', however, this root has already been associated with a mount '{previouslyAssociatedMount.Name.ToString(m_context.StringTable)}'.");
            }

            m_alternativeRoots.TryAdd(root, mount);
        }

        /// <summary>
        /// Finalizes the mount registration and deals with error reporting.
        /// </summary>
        public bool CompleteInitialization()
        {
            if (m_mountAdditionError)
            {
                return false;
            }

            if (!AddAndPerformNestingOperations())
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Helper to create a token with given text.
        /// </summary>
        private static LocationData CreateToken(BuildXLContext context)
        {
            return LocationData.Create(AbsolutePath.Create(context.PathTable, AssemblyHelper.GetAssemblyLocation(typeof(MountsTable).GetTypeInfo().Assembly)));
        }

        /// <summary>
        /// Populates the module specific mount expanders from modules in the module configurations
        /// </summary>
        /// <param name="modules">the module configurations</param>
        /// <param name="moduleMountTables">the module mounts tables</param>
        /// <returns>true if the operations were performed successfully</returns>
        public bool PopulateModuleMounts(IEnumerable<IModuleConfiguration> modules, out IDictionary<ModuleId, MountsTable> moduleMountTables)
        {
            moduleMountTables = new Dictionary<ModuleId, MountsTable>();
            bool success = true;
            foreach (var module in modules)
            {
                if (module.Mounts.Count != 0)
                {
                    MountsTable moduleMountsTable = new MountsTable(this, module.ModuleId);
                    foreach (var mount in module.Mounts)
                    {
                        moduleMountsTable.AddResolvedMount(mount);
                    }

                    moduleMountTables[module.ModuleId] = moduleMountsTable;
                    success &= moduleMountsTable.CompleteInitialization();
                }
            }

            if (!success)
            {
                moduleMountTables = null;
            }

            return success;
        }

        /// <summary>
        /// Some validations and operations require knowledge of whether a mount is nested under another mount.
        /// </summary>
        /// <returns>true if the operations were performed successfully</returns>
        private bool AddAndPerformNestingOperations()
        {
            bool success = true;

            // flip alternative roots -> mount map for easier lookup
            var alternativeRoots = m_alternativeRoots.ToMultiValueDictionary(kvp => kvp.Value, kvp => kvp.Key);

            // Compute the depth of each mount root so parent mounts can be visited first
            List<KeyValuePair<int, IMount>> mountsByRootDepth = new List<KeyValuePair<int, IMount>>();
            foreach (IMount mount in m_mountMapBuilder.Values)
            {
                int depth = 0;
                AbsolutePath path = mount.Path;
                while (path.IsValid)
                {
                    path = path.GetParent(m_context.PathTable);
                    depth++;
                }

                mountsByRootDepth.Add(new KeyValuePair<int, IMount>(depth, mount));
            }

            mountsByRootDepth.Sort((depthAndMount1, depthAndMount2) => depthAndMount1.Key.CompareTo(depthAndMount2.Key));

            foreach (var mountEntry in mountsByRootDepth)
            {
                var childMount = mountEntry.Value;
                var parentMountInfo = MountPathExpander.GetSemanticPathInfo(childMount.Path);
                if (parentMountInfo.IsValid)
                {
                    IMount parentMount;
                    if (!m_mountMapBuilder.TryGetValue(parentMountInfo.Root, out parentMount) && m_parent != null)
                    {
                        parentMount = m_parent.m_mountMapBuilder[parentMountInfo.Root];
                    }

                    if (parentMount.IsScrubbable && !childMount.IsScrubbable)
                    {
                        LogRelatedMountError(
                            Logger.Log.ScrubbableMountsMayOnlyContainScrubbableMounts,
                            childMount: childMount,
                            parentMount: parentMount);
                        success = false;
                    }
                }

                // TODO: Does not adding on success == false change behavior
                if (success)
                {
                    MountPathExpander.Add(m_context.PathTable, childMount);

                    if (alternativeRoots.TryGetValue(childMount, out var additionalRoots))
                    {
                        foreach (var root in additionalRoots)
                        {
                            MountPathExpander.AddWithExistingName(
                                m_context.PathTable,
                                new SemanticPathInfo(childMount.Name, root, childMount.TrackSourceFileChanges, childMount.IsReadable, childMount.IsWritable, childMount.IsSystem, childMount.IsScrubbable,  childMount.AllowCreateDirectory));
                        }
                    }
                }
            }

            return success;
        }

        private void LogRelatedMountError(RelatedMountLogEventError logEvent, IMount childMount, IMount parentMount)
        {
            logEvent(
                m_loggingContext,
                childMount.Location.Path.ToString(m_context.PathTable),
                childMount.Location.Line,
                childMount.Location.Position,
                childMount.Name.ToString(m_context.StringTable),
                childMount.Path.ToString(m_context.PathTable),
                parentMount.Name.ToString(m_context.StringTable),
                parentMount.Path.ToString(m_context.PathTable));

            Events.LogRelatedLocation(
                    m_loggingContext,
                    Logger.Log.ErrorRelatedLocation,
                    m_context.PathTable,
                    parentMount.Location,
                    childMount.Location);
        }

        private delegate void RelatedMountLogEventError(
            LoggingContext context,
            string file,
            int line,
            int column,
            string childMountName,
            string childMountRoot,
            string parentMountName,
            string parentMountRoot);

        /// <inheritdoc />
        public override BuildXL.FrontEnd.Sdk.TryGetMountResult TryGetMount(string name, ModuleId currentPackage, out IMount mount)
        {
            Contract.Requires(currentPackage.IsValid);

            if (string.IsNullOrEmpty(name))
            {
                mount = null;
                return BuildXL.FrontEnd.Sdk.TryGetMountResult.NameNullOrEmpty;
            }

            return m_mountsByName.TryGetValue(name, out mount)
                ? BuildXL.FrontEnd.Sdk.TryGetMountResult.Success
                : BuildXL.FrontEnd.Sdk.TryGetMountResult.NameNotFound;
        }

        /// <inheritdoc />
        public override IEnumerable<string> GetMountNames(ModuleId currentPackage)
        {
            return m_mountsByName.Keys;
        }
    }
}
