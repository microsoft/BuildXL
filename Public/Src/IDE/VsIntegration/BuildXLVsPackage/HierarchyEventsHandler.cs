// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.Security;
using System.Text;
using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell.Interop;
using VSLangProj;

namespace BuildXL.VsPackage
{
    /// <summary>
    /// Main handler that listens to ItemAdded and ItemRemoved events
    /// </summary>
    public sealed class HierarchyEventsHandler : IVsHierarchyEvents
    {
        private readonly IVsHierarchy m_hierarchy;

        /// <summary>
        /// Creates an instance of this class
        /// </summary>
        public HierarchyEventsHandler(IVsHierarchy hierarchy)
        {
            Contract.Requires(hierarchy != null);

            m_hierarchy = hierarchy;
        }

        /// <summary>
        /// Gets invoked when a new item is added to hierarchy
        /// </summary>
        /// <param name="itemidParent">Parent id of the item added</param>
        /// <param name="itemidSiblingPrev">Sibling id of the item added</param>
        /// <param name="itemidAdded">ID of the item added</param>
        /// <returns>Returns the status of the execution</returns>
        [SuppressMessage("Microsoft.Performance", "CA1800:DoNotCastUnnecessarily")]
        public int OnItemAdded(uint itemidParent, uint itemidSiblingPrev, uint itemidAdded)
        {
            object itemAdded;
            int retValue = m_hierarchy.GetProperty(itemidAdded, (int)__VSHPROPID.VSHPROPID_ExtObject, out itemAdded);
            if (retValue != VSConstants.S_OK)
            {
                return retValue;
            }

            var item = itemAdded as ProjectItem;
            if (item != null)
            {
                return ProjectItemAdded(item);
            }

            if (itemAdded is Reference)
            {
                // Handled from a different event handler.
                return VSConstants.S_OK;
            }

            SpecUtilities.ShowMessage(string.Format(CultureInfo.InvariantCulture, "Unknown item type: {0}", itemAdded.ToString()));
            return VSConstants.S_OK;
        }

        /// <summary>
        /// Gets invoked when an item is deleted
        /// </summary>
        /// <param name="itemid">ID of the item that is deleted</param>
        /// <returns>Returns the status of the execution</returns>
        [SuppressMessage("Microsoft.Performance", "CA1800:DoNotCastUnnecessarily")]
        public int OnItemDeleted(uint itemid)
        {
            object itemRemoved;
            int retValue = m_hierarchy.GetProperty(itemid, (int)__VSHPROPID.VSHPROPID_ExtObject, out itemRemoved);
            if (retValue != VSConstants.S_OK)
            {
                return retValue;
            }

            var item = itemRemoved as ProjectItem;
            if (item != null)
            {
                return ProjectItemRemoved(item);
            }

            if (itemRemoved is Reference)
            {
                // Handled from a different event handler.
                return VSConstants.S_OK;
            }

            SpecUtilities.ShowMessage(string.Format(CultureInfo.InvariantCulture, "Unknown item type: {0}", itemRemoved.ToString()));
            return VSConstants.S_OK;
        }

        /// <inheritdoc/>
        public int OnItemsAppended(uint itemidParent)
        {
            return VSConstants.E_NOTIMPL;
        }

        /// <inheritdoc/>
        public int OnPropertyChanged(uint itemid, int propid, uint flags)
        {
            return VSConstants.E_NOTIMPL;
        }

        /// <inheritdoc/>
        public int OnInvalidateIcon(IntPtr hicon)
        {
            return VSConstants.E_NOTIMPL;
        }

        /// <inheritdoc/>
        public int OnInvalidateItems(uint itemidParent)
        {
            return VSConstants.E_NOTIMPL;
        }

        #region project items

        /// <summary>
        /// Handles added items
        /// </summary>
        /// <param name="item">Project item that is added</param>
        /// <returns>Status of the update</returns>
        private int ProjectItemAdded(ProjectItem item)
        {
            if (!SpecUtilities.IsAValidProjectItem(item))
            {
                return VSConstants.S_OK;
            }

            try
            {
                string specFile, value, relativePath;

                if (!TryGetSpecFileAndSrcRelativePath(item, out specFile, out value, out relativePath))
                {
                    return VSConstants.S_OK;
                }

                // No specific action needed to be taken if a folder is added
                if (string.Equals(SpecUtilities.PhysicalFolderKind, item.Kind, StringComparison.OrdinalIgnoreCase))
                {
                    AddProjectDirectory(item);
                    return VSConstants.S_OK;
                }

                // Adding element to the specification file if its csharp file
                // TODO: check about BuildXL spec file
                string extension = Path.GetExtension(relativePath);
                Contract.Assume(extension != null);
                if (extension.Equals(Constants.CSharp, StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(Constants.CPP, StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(Constants.DominoSpec, StringComparison.OrdinalIgnoreCase))
                {
                    SpecUtilities.AddSourceElement(specFile, value, relativePath);
                    return VSConstants.S_OK;
                }

                // Adding config file
                if (extension.Equals(Constants.Config, StringComparison.OrdinalIgnoreCase))
                {
                    SpecUtilities.AddAppConfig(specFile, value, relativePath);
                    return VSConstants.S_OK;
                }

                // Adding resource file
                if (extension.Equals(Constants.Resource, StringComparison.OrdinalIgnoreCase))
                {
                    SpecUtilities.AddEmbeddedResource(specFile, value, relativePath);
                    return VSConstants.S_OK;
                }

                // Adding resource file with linked contents
                if (extension.Equals(Constants.Png, StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(Constants.Js, StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(Constants.Html, StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(Constants.Htm, StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(Constants.Css, StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(Constants.Ico, StringComparison.OrdinalIgnoreCase))
                {
                    SpecUtilities.AddEmbeddedResourceLinkedContent(specFile, value, relativePath);
                    return VSConstants.S_OK;
                }
            }
            catch (BuildXLVsException de)
            {
                SpecUtilities.ShowException(de.Message, de.StackTrace);
            }
            catch (Exception ex)
            {
                // Log exception and rethrow it
#pragma warning disable EPC12 // Suspicious exception handling: only Message property is observed in exception block.
                SpecUtilities.ShowException(ex.Message, ex.StackTrace);
#pragma warning restore EPC12 // Suspicious exception handling: only Message property is observed in exception block.
                throw;
            }

            return VSConstants.S_OK;
        }

        /// <summary>
        /// Adds a complete directory
        /// </summary>
        /// <param name="item">The item that needs to be added</param>
        private void AddProjectDirectory(ProjectItem item)
        {
            foreach (ProjectItem subItem in item.ProjectItems)
            {
                if (string.Equals(SpecUtilities.PhysicalFolderKind, subItem.Kind, StringComparison.OrdinalIgnoreCase))
                {
                    AddProjectDirectory(subItem);
                    continue;
                }

                ProjectItemAdded(subItem);
            }
        }

        /// <summary>
        /// Handles removal of items
        /// </summary>
        /// <param name="item">Item that is removed</param>
        /// <returns>Status of the update</returns>
        private int ProjectItemRemoved(ProjectItem item)
        {
            if (!SpecUtilities.IsAValidProjectItem(item))
            {
                return VSConstants.S_OK;
            }

            try
            {
                string specFile, value, relativePath;
                if (!TryGetSpecFileAndSrcRelativePath(item, out specFile, out value, out relativePath))
                {
                    return VSConstants.S_OK;
                }

                // Check if its a folder
                if (string.Equals(SpecUtilities.PhysicalFolderKind, item.Kind, StringComparison.OrdinalIgnoreCase))
                {
                    RemoveProjectDirectory(item);
                    return VSConstants.S_OK;
                }

                // Removing element to the specification file if its csharp file
                string extension = Path.GetExtension(relativePath);
                Contract.Assume(extension != null);

                // TODO: check about BuildXL spec file
                if (extension.Equals(Constants.CSharp, StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(Constants.CPP, StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(Constants.DominoSpec, StringComparison.OrdinalIgnoreCase))
                {
                    SpecUtilities.RemoveSourceElement(specFile, value, relativePath);
                }

                // Removing config file
                if (extension.Equals(Constants.Config, StringComparison.OrdinalIgnoreCase))
                {
                    SpecUtilities.RemoveAppConfig(specFile, value, relativePath);
                    return VSConstants.S_OK;
                }

                // Removing resource file
                if (extension.Equals(Constants.Resource, StringComparison.OrdinalIgnoreCase))
                {
                    SpecUtilities.RemoveEmbeddedResource(specFile, value, relativePath);
                    return VSConstants.S_OK;
                }

                // Removing resource file with linked contents
                if (extension.Equals(Constants.Png, StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(Constants.Js, StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(Constants.Html, StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(Constants.Htm, StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(Constants.Css, StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(Constants.Ico, StringComparison.OrdinalIgnoreCase))
                {
                    SpecUtilities.RemoveEmbeddedResourceLinkedContent(specFile, value, relativePath);
                    return VSConstants.S_OK;
                }
            }
            catch (BuildXLVsException de)
            {
                SpecUtilities.ShowException(de.Message, de.StackTrace);
            }
            catch (Exception ex)
            {
                // Log exception and rethrow it
#pragma warning disable EPC12 // Suspicious exception handling: only Message property is observed in exception block.
                SpecUtilities.ShowException(ex.Message, ex.StackTrace);
#pragma warning restore EPC12 // Suspicious exception handling: only Message property is observed in exception block.
                throw;
            }

            return VSConstants.S_OK;
        }

        /// <summary>
        /// Removes a complete directory
        /// </summary>
        /// <param name="item">The item that needs to be removed</param>
        private void RemoveProjectDirectory(ProjectItem item)
        {
            foreach (ProjectItem subItem in item.ProjectItems)
            {
                if (string.Equals(SpecUtilities.PhysicalFolderKind, subItem.Kind, StringComparison.OrdinalIgnoreCase))
                {
                    RemoveProjectDirectory(subItem);
                    continue;
                }

                ProjectItemRemoved(subItem);
            }
        }

        /// <summary>
        /// Returns specification file and relative path of the item
        /// </summary>
        /// <param name="item">The project item</param>
        /// <param name="specFile">The specification file</param>
        /// <param name="value">The name of the value corresponding to this project</param>
        /// <param name="relativePath">The relative path of the project item</param>
        /// <returns>Returns the status</returns>
        private static bool TryGetSpecFileAndSrcRelativePath(ProjectItem item, out string specFile, out string value, out string relativePath)
        {
            if (!SpecUtilities.TryGetSpecFileFromProject(item.ContainingProject.FileName, out specFile, out value))
            {
                Debug.WriteLine(string.Format(CultureInfo.CurrentCulture, "BuildXL specification file not found"));
                relativePath = null;
                return false;
            }

            // Get the relative path of the item
            var tempSpecFile = specFile;
            var specFileDir = HandleRecoverableIOException(
                    () => Path.GetDirectoryName(tempSpecFile),
                    ex =>
                    {
                        throw new BuildXLVsException(string.Format(CultureInfo.InvariantCulture, "Directory name cannot be retrieved from {0}: {1}", tempSpecFile, ex.Message));
                    });

            relativePath = RelativePath(item.FileNames[0], specFileDir);
            return true;
        }

        /// <summary>
        /// Returns relative path for a given absolute path
        /// </summary>
        /// <param name="absolutePath">The absolute path</param>
        /// <param name="relativeTo">Other path from which the relative path needs to be computed</param>
        /// <returns>The relative path</returns>
        private static string RelativePath(string absolutePath, string relativeTo)
        {
            var fullAbsolutePath = HandleRecoverableIOException(
                    () => Path.GetFullPath(absolutePath),
                    ex =>
                    {
                        throw new BuildXLVsException(string.Format(CultureInfo.InvariantCulture, "The value {0} cannot be represented as a full path: {1}", absolutePath, ex.Message));
                    });
            string[] absoluteDirectories = fullAbsolutePath.Split('\\');

            var fullRelativePath = HandleRecoverableIOException(
                    () => Path.GetFullPath(relativeTo),
                    ex =>
                    {
                        throw new BuildXLVsException(string.Format(CultureInfo.InvariantCulture, "The value {0} cannot be represented as a full path: {1}", relativeTo, ex.Message));
                    });
            string[] relativeDirectories = fullRelativePath.Split('\\');

            // Get the shortest of the two paths
            int length = absoluteDirectories.Length < relativeDirectories.Length ? absoluteDirectories.Length : relativeDirectories.Length;

            // Use to determine where in the loop we exited
            int lastCommonRoot = -1;
            int index;

            // Find common root
            for (index = 0; index < length; index++)
            {
                if (absoluteDirectories[index] != relativeDirectories[index])
                {
                    break;
                }

                lastCommonRoot = index;
            }

            // If we didn't find a common prefix then throw
            if (lastCommonRoot == -1)
            {
                return absolutePath;
            }

            // Build up the relative path
            StringBuilder relativePath = new StringBuilder();

            // Add on the ..
            for (index = lastCommonRoot + 1; index < relativeDirectories.Length; index++)
            {
                if (relativeDirectories[index].Length > 0)
                {
                    relativePath.Append(@"..\");
                }
            }

            // Add on the folders
            for (index = lastCommonRoot + 1; index < absoluteDirectories.Length - 1; index++)
            {
                relativePath.Append(absoluteDirectories[index] + "\\");
            }

            relativePath.Append(absoluteDirectories[absoluteDirectories.Length - 1]);
            return relativePath.ToString();
        }

        #endregion project items

        /// <summary>
        /// Invokes taskProducer, and handles any raised <see cref="IOException" /> or <see cref="UnauthorizedAccessException" />
        /// by calling <paramref name="handler" />. The caught exception is re-thrown unless the handler itself throws. The typical
        /// use case is to wrap the <see cref="IOException" /> or <see cref="UnauthorizedAccessException" /> in a
        /// <see cref="BuildXLVsException" />.
        /// These exceptions are commonly thrown from I/O functions in the BCL, and represent recoverable external errors.
        /// </summary>
        private static T HandleRecoverableIOException<T>(Func<T> func, Action<Exception> handler)
        {
            Contract.Requires(func != null);
            Contract.Requires(handler != null);

            // We re-throw in the catch blocks (versus capturing and re-throwing) to avoid stomping on the call stack, etc.).
            try
            {
                return func();
            }
            catch (UnauthorizedAccessException ex)
            {
                handler(ex);
                throw;
            }
            catch (SecurityException ex)
            {
                handler(ex);
                throw;
            }
            catch (IOException ex)
            {
                handler(ex);
                throw;
            }
            catch (Win32Exception ex)
            {
                handler(ex);
                throw;
            }
        }
    }
}
