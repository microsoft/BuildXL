// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using BuildXL.Utilities;
using JetBrains.Annotations;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Defines a named root
    /// </summary>
    public interface IEngineConfiguration
    {
        /// <summary>
        /// Default pip filter to use for builds. Any command filter specified on the command line will override this.
        /// </summary>
        /// <remarks>
        /// /filter:&lt;deps&gt;&lt;filter&gt;          Specifies a filter expression (short form: /f)
        /// &lt;deps&gt;                          Specifies additional pips to run based on dependency information of pips matched in filter. May be: empty (all dependencies), "+"
        /// (dependencies and dependents), or "-" (dirty dependencies)
        /// &lt;filter&gt;                        Expression form &lt;negation&gt;&lt;filterType&gt;='&lt;filterArgument&gt;'or &lt;negation&gt;(&lt;filter&gt;&lt;binaryOperator&gt;&lt;filter
        /// &gt;) where &lt;negation&gt; is the "~" character to negate the expression, and &lt;binaryOperator&gt; is either "and" or "or"
        /// Types of filters:
        ///
        /// id                              Filters by a pip's id as shown in the viewer.
        /// output                          Filters pips by the output files they create. The value may be: 'path' to match a file, 'path\'. to match files within a directory, 'path\*' to
        /// match files in a directory and recursive directories, 'Mount[MountName]' to match files under a mount, or '*\fileName' to
        /// match files with a specific name no matter where they are. 'path' may be an absolute or relative path. 'fileName' must be a fileName and may not contain any directory separator
        /// characters.
        /// spec                            Filters by the spec that caused a pip to be included in the graph. The value may be: 'path' to match a file, 'path\'. to match files within a
        /// directory, 'path\*' to match files in a directory and recursive directories, 'Mount[MountName]' to match files under a mount,
        /// or '*\fileName' to match files with a specific name no matter where they are. 'path' may be an absolute or relative path. 'fileName' must be a fileName and may not contain any
        /// directory separator characters.
        /// tag                             Filters by a tag. Case sensitive.
        /// value                           Filters by value name. Case sensitive.
        /// Filtering examples:
        /// /f:~(tag='test')              Selects all pips not marked with the 'test' tag, including their dependencies
        /// /f:+spec='src\utilities\*'    Selects all pips originating from spec files within \src\utilities and all subdirectories, including, their dependencies and dependents.
        /// /f:(tag='csc.exe'and~(tag='test'))
        /// Selects all pips marked with tag 'csc.exe' and not marked with tag 'test'. Runs them and their dependencies
        /// </remarks>
        [CanBeNull]
        string DefaultFilter { get; }

        /// <summary>
        /// Specifies a drive mapping applied during this build. Paths under specified letters will be mapped to the corresponding paths at the system level for the build process and the
        /// tools launched as a part of the build. (short form: /rm)
        /// </summary>
        [NotNull]
        IReadOnlyDictionary<string, AbsolutePath> RootMap { get; }

        /// <summary>
        /// Use hard links to de-dupe build output with the cache.
        /// </summary>
        bool UseHardlinks { get; }

        /// <summary>
        /// Disable sharing of the same ConHost by all the executing pips.
        /// </summary>
        bool DisableConHostSharing { get; }

        /// <summary>
        /// Scans volume change journals to determine spec file changes for graph reuse check. Defaults to enabled.
        /// </summary>
        bool ScanChangeJournal { get; }

        /// <summary>
        /// Time limit for scanning each volume change journal. Set to -1 for no limit.
        /// </summary>
        int ScanChangeJournalTimeLimitInSec { get; }

        /// <summary>
        /// Specifies the phase until which BuildXL runs. Allowed values are None (no phase is run), Parse (run until parsing is done), Evaluate (run until value evaluation is done), Schedule
        /// (run until scheduling is done), Execute (run until execution is done). Default isExecute.
        /// </summary>
        EnginePhases Phase { get; }

        /// <summary>
        /// Deletes output files that would have been produced by the build. Pips will not be executed.
        /// </summary>
        bool CleanOnly { get; }

        /// <summary>
        /// Before executing, scrubs (deletes) files and directories not marked as inputs or outputs of the current build. Only applies to mounts marked as Scrubbable. This includes the object directory but none others by default.
        /// </summary>
        bool Scrub { get; }

        /// <summary>
        /// Before executing, scrubs (deletes) files and directories not marked as inputs or outputs of the current build in the specified directories.
        /// </summary>
        [CanBeNull]
        IReadOnlyList<AbsolutePath> ScrubDirectories { get; }

        /// <summary>
        /// Directories to eagerly populate with symlinks
        /// </summary>
        IReadOnlyList<AbsolutePath> PopulateSymlinkDirectories { get; }

        /// <summary>
        /// Directories under the object directory root will get shortened to avoid too long path names. Defaults to 64 characters for relative output directories.
        /// </summary>
        /// <remarks>
        /// The value should be greater than 0.
        /// WIP: switch to an attribute: [PropertyContract("value > 0")]
        /// </remarks>
        // Bug #170768 tracks fixing running unittests from VS and restoring this back to 64
        int MaxRelativeOutputDirectoryLength { get; }

        /// <summary>
        /// Cleans per pip temp directories after the pip successfully exits to save disk space. Defaults to enabled.
        /// </summary>
        bool CleanTempDirectories { get; }

        /// <summary>
        /// Reuse engine state between client sessions
        /// </summary>
        bool ReuseEngineState { get; }

        /// <summary>
        /// Build lock wait time between lock attempts (in seconds).
        /// </summary>
        int BuildLockPollingIntervalSec { get; }

        /// <summary>
        /// Build lock total wait time (in minutes).
        /// </summary>
        int BuildLockWaitTimeoutMins { get; }

        /// <summary>
        /// Whether this build is explicitly requesting convergence with remote caches. This will disable features that
        /// may interrupt this.
        /// </summary>
        /// <remarks>
        /// TODO: Not customer facing at this time. Intentionally not including this in help text
        /// </remarks>
        bool Converge { get; }

        /// <summary>
        /// Allows users to specify directory translations.
        /// </summary>
        [CanBeNull]
        IReadOnlyList<TranslateDirectoryData> DirectoriesToTranslate { get; }

        /// <summary>
        /// Whether the main log file should contain statistics for the engine
        /// </summary>
        bool LogStatistics { get; }

        /// <summary>
        /// Compresses the graph files during serialization and uncompresses during reloading
        /// </summary>
        bool CompressGraphFiles { get; }

        /// <summary>
        /// Initialization mode for file change tracker.
        /// </summary>
        FileChangeTrackerInitializationMode FileChangeTrackerInitializationMode { get; }

        /// <summary>
        /// Whether to track the builds in a textfile in the user folder.
        /// </summary>
        bool TrackBuildsInUserFolder { get; }

        /// <summary>
        /// Whether or not to use file content table.
        /// </summary>
        bool? UseFileContentTable { get; }
    }
}
