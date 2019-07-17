// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;

namespace BuildXL.Cache.ContentStore.Vfs.Provider
{
    using Utils = Microsoft.Windows.ProjFS.Utils;

    /// <summary>
    /// Represents an active filesystem enumeration of a directory with cursor.
    /// </summary>
    internal class ActiveEnumeration
    {
        private readonly IEnumerable<VfsNode> fileInfos;
        private IEnumerator<VfsNode> fileInfoEnumerator;
        private string filterString = null;

        public ActiveEnumeration(IReadOnlyList<VfsNode> fileInfos)
        {
            this.fileInfos = fileInfos;
            ResetEnumerator();
            MoveNext();
        }

        /// <summary>
        /// true if Current refers to an element in the enumeration, false if Current is past the end of the collection
        /// </summary>
        public bool IsCurrentValid { get; private set; }

        /// <summary>
        /// Gets the element in the collection at the current position of the enumerator
        /// </summary>
        public VfsNode Current
        {
            get { return fileInfoEnumerator.Current; }
        }

        /// <summary>
        /// Resets the enumerator and advances it to the first ProjectedFileInfo in the enumeration
        /// </summary>
        /// <param name="filter">Filter string to save.  Can be null.</param>
        public void RestartEnumeration(
            string filter)
        {
            ResetEnumerator();
            IsCurrentValid = fileInfoEnumerator.MoveNext();
            SaveFilter(filter);
        }

        /// <summary>
        /// Advances the enumerator to the next element of the collection (that is being projected).   
        /// If a filter string is set, MoveNext will advance to the next entry that matches the filter.
        /// </summary>
        /// <returns>
        /// true if the enumerator was successfully advanced to the next element; false if the enumerator has passed the end of the collection
        /// </returns>
        public bool MoveNext()
        {
            IsCurrentValid = fileInfoEnumerator.MoveNext();
            while (IsCurrentValid && IsCurrentHidden())
            {
                IsCurrentValid = fileInfoEnumerator.MoveNext();
            }

            return IsCurrentValid;
        }

        /// <summary>
        /// Attempts to save the filter string for this enumeration.  When setting a filter string, if Current is valid
        /// and does not match the specified filter, the enumerator will be advanced until an element is found that
        /// matches the filter (or the end of the collection is reached).
        /// </summary>
        /// <param name="filter">Filter string to save.  Can be null.</param>
        /// <returns> True if the filter string was saved.  False if the filter string was not saved (because a filter string
        /// was previously saved).
        /// </returns>
        /// <remarks>
        /// Per MSDN (https://msdn.microsoft.com/en-us/library/windows/hardware/ff567047(v=vs.85).aspx, the filter string
        /// specified in the first call to ZwQueryDirectoryFile will be used for all subsequent calls for the handle (and
        /// the string specified in subsequent calls should be ignored)
        /// </remarks>
        public bool TrySaveFilterString(
            string filter)
        {
            if (filterString == null)
            {
                SaveFilter(filter);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Returns the current filter string or null if no filter string has been saved
        /// </summary>
        /// <returns>The current filter string or null if no filter string has been saved</returns>
        public string GetFilterString()
        {
            return filterString;
        }

        private static bool FileNameMatchesFilter(
            string name,
            string filter)
        {
            if (string.IsNullOrEmpty(filter))
            {
                return true;
            }

            if (filter == "*")
            {
                return true;
            }

            return Utils.IsFileNameMatch(name, filter);
        }

        private void SaveFilter(
            string filter)
        {
            if (string.IsNullOrEmpty(filter))
            {
                filterString = string.Empty;
            }
            else
            {
                filterString = filter;
                if (IsCurrentValid && IsCurrentHidden())
                {
                    MoveNext();
                }
            }
        }

        private bool IsCurrentHidden()
        {
            return !FileNameMatchesFilter(Current.Name, GetFilterString());
        }

        private void ResetEnumerator()
        {
            fileInfoEnumerator = fileInfos.GetEnumerator();
        }
    }
}


