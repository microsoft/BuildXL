// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using BuildXL.Utilities;

namespace Tool.ExecutionLogSdk
{
    /// <summary>
    /// Describes a single module.
    /// </summary>
    [SuppressMessage("Microsoft.Naming", "CA1710:IdentifiersShouldHaveCorrectSuffix", Justification = "This class should not be called a Collection")]
    public sealed class ModuleDescriptor
    {
        #region Private properties

        /// <summary>
        /// Used to convert the StringId and AbsolutePath values into strings
        /// </summary>
        private PipExecutionContext m_context;

        /// <summary>
        /// The StringId for the ModuleName
        /// </summary>
        private StringId m_moduleNameId;

        /// <summary>
        /// The AbsolutePath for the ModuleLocation
        /// </summary>
        private AbsolutePath m_moduleLocationId;
        #endregion

        #region Public properties

        /// <summary>
        /// The Id of the module.
        /// </summary>
        public int ModuleId { get; set; }

        /// <summary>
        /// The name of the module.
        /// </summary>
        public string ModuleName => m_moduleNameId.ToString(m_context.StringTable);

        /// <summary>
        /// The location of the module definition file.
        /// </summary>
        public string ModuleLocation => m_context.PathTable.AbsolutePathToString(m_moduleLocationId);
        #endregion

        #region Internal methods

        /// <summary>
        /// Internal constructor
        /// </summary>
        /// <param name="moduleId">The id of the module</param>
        /// <param name="moduleNameId">The StringId of the name of the module</param>
        /// <param name="moduleLocationId">The AbsolutePath of the location of the module definition file</param>
        /// <param name="context">Used to convert the StringId and AbsolutePath values into strings</param>
        internal ModuleDescriptor(int moduleId, StringId moduleNameId, AbsolutePath moduleLocationId, PipExecutionContext context)
        {
            ModuleId = moduleId;
            m_moduleNameId = moduleNameId;
            m_moduleLocationId = moduleLocationId;
            m_context = context;
        }
        #endregion

        public override string ToString()
        {
            return ModuleName;
        }
    }
}
