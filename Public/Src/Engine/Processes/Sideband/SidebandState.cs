// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities.Core;

namespace BuildXL.Processes.Sideband
{
    /// <summary>
    /// Holds the information recorded in the sideband files, retrieved at the start of the build.
    /// </summary>
    public sealed class SidebandState
    {
        /// <summary>
        /// Mapping from pip semistable hash to the collection of paths recorded for that pip in the sideband files
        /// This value is only set when <see cref="ShouldPostponeDeletion"/> is true.
        /// </summary>
        public IReadOnlyDictionary<long, IReadOnlyCollection<AbsolutePath>> Entries { get; private init; }

        /// <nodoc />
        private SidebandState(bool shouldPostponeDeletion, 
            IReadOnlyDictionary<long, IReadOnlyCollection<AbsolutePath>> entries = null, 
            IReadOnlyList<string> extraneousSidebandFiles = null)
        {
            ShouldPostponeDeletion = shouldPostponeDeletion;
            Entries = entries;
            ExtraneousSidebandFiles = extraneousSidebandFiles;
        }

        /// <summary>
        /// Final decision about whether or not to postpone deletion of shared opaque outputs.
        /// </summary>
        /// <remarks>
        /// This value is set appropriately by <see cref="SidebandState.CreateForEagerDeletion"/> 
        /// and <see cref="SidebandState.CreateForLazyDeletion(IReadOnlyDictionary{long, IReadOnlyCollection{AbsolutePath}}, IReadOnlyList{string})"/>
        /// </remarks>
        public bool ShouldPostponeDeletion { get; private init; }

        /// <summary>
        /// List of sideband files that are present on disk but whose corresponding pips are not found in the pip graph.
        /// This value is only set when <see cref="ShouldPostponeDeletion"/> is true.
        /// </summary>
        public IReadOnlyList<string> ExtraneousSidebandFiles
        {
            get
            {
                Contract.Requires(ShouldPostponeDeletion);
                return m_extraneousSidebandFiles;
            }
            private init => m_extraneousSidebandFiles = value;
        }
        private IReadOnlyList<string> m_extraneousSidebandFiles;


        /// <nodoc />
        public static SidebandState CreateForLazyDeletion(IReadOnlyDictionary<long, IReadOnlyCollection<AbsolutePath>> entries, IReadOnlyList<string> extraneousFiles)
        {
            Contract.AssertNotNull(entries);
            Contract.AssertNotNull(extraneousFiles);
            return new SidebandState(shouldPostponeDeletion: true, entries, extraneousFiles);
        }

        /// <nodoc />
        public static SidebandState CreateForEagerDeletion()
        {
            return new SidebandState(shouldPostponeDeletion: false);
        }

        /// <nodoc />
        public IReadOnlyCollection<AbsolutePath> this[long pipId] 
        {
            get
            {
                Contract.Requires(ShouldPostponeDeletion);
                return Entries[pipId];
            }
        }
    }
}
