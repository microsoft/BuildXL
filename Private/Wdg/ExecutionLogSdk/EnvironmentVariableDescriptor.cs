// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using BuildXL.Utilities;

namespace Tool.ExecutionLogSdk
{
    /// <summary>
    /// Represents an environment variable that was present in the running environment of one or more pips during the build.
    /// </summary>
    [SuppressMessage("Microsoft.Naming", "CA1710:IdentifiersShouldHaveCorrectSuffix", Justification = "This class should not be called a Collection")]
    public sealed class EnvironmentVariableDescriptor
    {
        #region Private properties

        /// <summary>
        /// Used to convert a StringId to a string
        /// </summary>
        private StringTable m_stringTable;
        #endregion

        #region Internal properties

        /// <summary>
        /// Internal collection of pips that use (or at least can "see" the value of) the environment variable
        /// </summary>
        internal readonly ConcurrentHashSet<PipDescriptor> ReferencingPipsHashset = new ConcurrentHashSet<PipDescriptor>();

        /// <summary>
        /// Signals that the environment variable is a pass through variable and has no value.
        /// </summary>
        internal bool IsPassThroughEnvironmentVariable { get; set; }
        #endregion

        #region Public properties

        /// <summary>
        /// Signals that the environment variable is a pass through variable and has no value.
        /// </summary>
        public bool IsPassThrough
        {
            get { return IsPassThroughEnvironmentVariable; }
        }

        /// <summary>
        /// The name of the environment variable.
        /// </summary>
        public string Name => m_stringTable.IdToString(NameId);

        /// <summary>
        /// The StringId of the Name
        /// </summary>
        public StringId NameId { get; }

        /// <summary>
        /// The pips that use (or at least can "see" the value of) the environment variable in their execution process.
        /// </summary>
        public IReadOnlyCollection<PipDescriptor> ReferencingPips { get { return ReferencingPipsHashset; } }
        #endregion

        #region Internal methods

        /// <summary>
        /// Internal constructor
        /// </summary>
        /// <param name="nameId">The StringId of the name of the environment variable that this object instance describes</param>
        /// <param name="stringTable">Used to convert a StringId to a string</param>
        internal EnvironmentVariableDescriptor(StringId nameId, StringTable stringTable)
        {
            NameId = nameId;
            m_stringTable = stringTable;
            IsPassThroughEnvironmentVariable = false;
        }
        #endregion

    }
}
