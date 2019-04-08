// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Storage.ChangeJournalService;

namespace BuildXL.Storage
{
    /// <summary>
    /// Class representing state of journal.
    /// </summary>
    public class JournalState
    {
        /// <summary>
        /// Disabled journal.
        /// </summary>
        public static readonly JournalState DisabledJournal = new JournalState(null, null);

        /// <summary>
        /// Volume map.
        /// </summary>
        public readonly VolumeMap VolumeMap;

        /// <summary>
        /// Journal accessor.
        /// </summary>
        public readonly IChangeJournalAccessor Journal;

        /// <summary>
        /// Checks if this instance of <see cref="JournalState"/> has a disabled journal.
        /// </summary>
        public bool IsDisabled => this == DisabledJournal;

        /// <summary>
        /// Checks if this instance of <see cref="JournalState"/> has an enabled journal.
        /// </summary>
        public bool IsEnabled => !IsDisabled;

        private JournalState(VolumeMap volumeMap, IChangeJournalAccessor journal)
        {
            Journal = journal;
            VolumeMap = volumeMap;
        }

        /// <summary>
        /// Creates a state with enabled journal.
        /// </summary>
        public static JournalState CreateEnabledJournal(VolumeMap volumeMap, IChangeJournalAccessor journal)
        {
            Contract.Requires(volumeMap != null);
            Contract.Requires(journal != null);

            return new JournalState(volumeMap, journal);
        }
    }
}
