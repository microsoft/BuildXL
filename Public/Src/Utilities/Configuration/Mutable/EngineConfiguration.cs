// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <nodoc />
    public sealed class EngineConfiguration : IEngineConfiguration
    {
        /// <nodoc />
        public EngineConfiguration()
        {
            RootMap = new Dictionary<string, AbsolutePath>();
            UseHardlinks = true;
            ScanChangeJournal = true;
            ScanChangeJournalTimeLimitInSec = 30;
            DisableConHostSharing = false;
            Phase = EnginePhases.Execute;
            MaxRelativeOutputDirectoryLength = 64;
            CleanTempDirectories = true;
            DefaultFilter = null;
            ReuseEngineState = true;
            BuildLockPollingIntervalSec = 5;
            BuildLockWaitTimeoutMins = 0;
            DirectoriesToTranslate = new List<TranslateDirectoryData>();
            ScrubDirectories = new List<AbsolutePath>();
            PopulateSymlinkDirectories = new List<AbsolutePath>();
            CompressGraphFiles = false;
            FileChangeTrackerInitializationMode = FileChangeTrackerInitializationMode.ResumeExisting;
            LogStatistics = true;
            TrackBuildsInUserFolder = true;
            UseFileContentTable = default;
        }

        /// <nodoc />
        public EngineConfiguration(IEngineConfiguration template, PathRemapper pathRemapper)
        {
            Contract.Assume(template != null);
            Contract.Assume(pathRemapper != null);

            DefaultFilter = template.DefaultFilter;
            RootMap = new Dictionary<string, AbsolutePath>();
            foreach (var kv in template.RootMap)
            {
                RootMap.Add(kv.Key, pathRemapper.Remap(kv.Value));
            }

            UseHardlinks = template.UseHardlinks;
            ScanChangeJournal = template.ScanChangeJournal;
            ScanChangeJournalTimeLimitInSec = template.ScanChangeJournalTimeLimitInSec;
            DisableConHostSharing = template.DisableConHostSharing;
            Phase = template.Phase;
            CleanOnly = template.CleanOnly;
            Scrub = template.Scrub;
            MaxRelativeOutputDirectoryLength = template.MaxRelativeOutputDirectoryLength;
            CleanTempDirectories = template.CleanTempDirectories;
            ReuseEngineState = template.ReuseEngineState;
            BuildLockPollingIntervalSec = template.BuildLockPollingIntervalSec;
            BuildLockWaitTimeoutMins = template.BuildLockWaitTimeoutMins;
            Converge = template.Converge;
            DirectoriesToTranslate =
                template.DirectoriesToTranslate.Select(
                    d => new TranslateDirectoryData(d.RawUserOption, pathRemapper.Remap(d.FromPath), pathRemapper.Remap(d.ToPath))).ToList();
            ScrubDirectories = pathRemapper.Remap(template.ScrubDirectories);
            PopulateSymlinkDirectories = pathRemapper.Remap(template.PopulateSymlinkDirectories);
            CompressGraphFiles = template.CompressGraphFiles;
            FileChangeTrackerInitializationMode = template.FileChangeTrackerInitializationMode;
            LogStatistics = template.LogStatistics;
            TrackBuildsInUserFolder = template.TrackBuildsInUserFolder;
            UseFileContentTable = template.UseFileContentTable;
        }

        /// <inheritdoc />
        public string DefaultFilter { get; set; }

        /// <nodoc />
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public Dictionary<string, AbsolutePath> RootMap { get; set; }

        /// <inheritdoc />
        IReadOnlyDictionary<string, AbsolutePath> IEngineConfiguration.RootMap => RootMap;

        /// <inheritdoc />
        public bool UseHardlinks { get; set; }

        /// <inheritdoc />
        public bool DisableConHostSharing { get; set; }

        /// <inheritdoc />
        public bool ScanChangeJournal { get; set; }

        /// <inheritdoc />
        public int ScanChangeJournalTimeLimitInSec { get; set; }

        /// <inheritdoc />
        public EnginePhases Phase { get; set; }

        /// <inheritdoc />
        public bool CleanOnly { get; set; }

        /// <inheritdoc />
        public bool Scrub { get; set; }

        /// <nodoc />
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public List<AbsolutePath> ScrubDirectories { get; set; }

        /// <inheritdoc />
        IReadOnlyList<AbsolutePath> IEngineConfiguration.ScrubDirectories => ScrubDirectories;

        /// <inheritdoc />
        public int MaxRelativeOutputDirectoryLength { get; set; }

        /// <inheritdoc />
        public bool CleanTempDirectories { get; set; }

        /// <inheritdoc />
        public bool ReuseEngineState { get; set; }

        /// <inheritdoc />
        public int BuildLockPollingIntervalSec { get; set; }

        /// <inheritdoc />
        public int BuildLockWaitTimeoutMins { get; set; }

        /// <inheritdoc />
        public bool Converge { get; set; }

        /// <nodoc />
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public List<TranslateDirectoryData> DirectoriesToTranslate { get; set; }

        /// <inheritdoc />
        IReadOnlyList<TranslateDirectoryData> IEngineConfiguration.DirectoriesToTranslate => DirectoriesToTranslate;

        /// <nodoc />
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public List<AbsolutePath> PopulateSymlinkDirectories { get; set; }

        /// <inheritdoc />
        IReadOnlyList<AbsolutePath> IEngineConfiguration.PopulateSymlinkDirectories => PopulateSymlinkDirectories;

        /// <inheritdoc />
        public bool CompressGraphFiles { get; set; }

        /// <inheritdoc />
        public FileChangeTrackerInitializationMode FileChangeTrackerInitializationMode { get; set; }

        /// <inheritdoc />
        public bool LogStatistics { get; set; }

        /// <inheritdoc />
        public bool TrackBuildsInUserFolder { get; set; }

        /// <inheritdoc />
        public bool? UseFileContentTable { get; set; }
    }
}
