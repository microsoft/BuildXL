// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Engine.Tracing;
using BuildXL.Storage.ChangeTracking;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Engine
{
    /// <summary>
    /// Class representing graph input artifact changes resulting from journal scan.
    /// </summary>
    internal class GraphInputArtifactChanges : IFileChangeTrackingObserver
    {
        /// <summary>
        /// Paths that possibly have changed.
        /// </summary>
        public HashSet<string> PossiblyChangedPaths { get; private set; }

        /// <summary>
        /// Directories that have changed since they were enumerated in the previous build.
        /// </summary>
        public HashSet<string> ChangedDirs { get; private set; }

        /// <summary>
        /// Checks if input files or directories have changed.
        /// </summary>
        public bool HaveNoChanges => PossiblyChangedPaths != null && PossiblyChangedPaths.Count == 0 && ChangedDirs != null && ChangedDirs.Count == 0;

        private readonly LoggingContext m_loggingContext;

        /// <summary>
        /// Creates an instance of <see cref="GraphInputArtifactChanges"/>.
        /// </summary>
        public GraphInputArtifactChanges(LoggingContext loggingContext)
        {
            Contract.Requires(loggingContext != null);
            m_loggingContext = loggingContext;
        }

        /// <inheritdoc />
        public void OnNext(ChangedPathInfo value)
        {
            if ((value.PathChanges & PathChanges.MembershipChanged) != 0)
            {
                ChangedDirs.Add(value.Path);
            }
            else
            {
                PossiblyChangedPaths.Add(value.Path);
            }
        }

        /// <inheritdoc />
        public void OnNext(ChangedFileIdInfo value)
        {
        }

        /// <inheritdoc />
        void IObserver<ChangedFileIdInfo>.OnError(Exception error)
        {
        }

        /// <inheritdoc />
        void IObserver<ChangedFileIdInfo>.OnCompleted()
        {
        }

        /// <inheritdoc />
        void IObserver<ChangedPathInfo>.OnError(Exception error)
        {
        }

        /// <inheritdoc />
        void IObserver<ChangedPathInfo>.OnCompleted()
        {
        }

        /// <inheritdoc />
        public void OnInit()
        {
            ChangedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            PossiblyChangedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        /// <inheritdoc />
        public void OnCompleted(ScanningJournalResult result)
        {
            Log(result);

            if (!result.Succeeded)
            {
                ChangedDirs = null;
                PossiblyChangedPaths = null;
            }
        }

        private void Log(ScanningJournalResult result)
        {
            if (PossiblyChangedPaths != null && ChangedDirs != null)
            {
                if (PossiblyChangedPaths.Count > 0 || ChangedDirs.Count > 0)
                {
                    string path = PossiblyChangedPaths.Count > 0 ? PossiblyChangedPaths.First() : string.Empty;
                    string directory = ChangedDirs.Count > 0 ? ChangedDirs.First() : string.Empty;

                    Logger.Log.JournalDetectedInputChanges(m_loggingContext, path, directory);
                }
                else if (result.Succeeded)
                {
                    Logger.Log.JournalDetectedNoInputChanges(m_loggingContext);
                }
            }
        }
    }
}
