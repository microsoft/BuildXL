// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using BuildXL.Pips;
using BuildXL.Utilities.Core;

namespace BuildXL.Scheduler.Artifacts
{
    /// <summary>
    /// Wrapper <see cref="SemanticPathExpander"/> which allows <see cref="FileContentManager"/> to adjust
    /// <see cref="SemanticPathInfo"/> before returning to caller
    /// </summary>
    internal class FileContentManagerSemanticPathExpander : SemanticPathExpander
    {
        private readonly SemanticPathExpander m_innerExpander;
        private readonly FileContentManager m_fileContentManager;

        public FileContentManagerSemanticPathExpander(FileContentManager fileContentManager, SemanticPathExpander innerExpander)
        {
            m_innerExpander = innerExpander;
            m_fileContentManager = fileContentManager;
        }

        /// <inheritdoc />
        public override SemanticPathInfo GetSemanticPathInfo(AbsolutePath path)
        {
            return m_fileContentManager.GetUpdatedSemanticPathInfo(m_innerExpander.GetSemanticPathInfo(path));
        }

        /// <inheritdoc />
        public override SemanticPathInfo GetSemanticPathInfo(string path)
        {
            return m_fileContentManager.GetUpdatedSemanticPathInfo(m_innerExpander.GetSemanticPathInfo(path));
        }

        /// <inheritdoc />
        public override SemanticPathExpander GetModuleExpander(ModuleId moduleId)
        {
            var moduleExpander = m_innerExpander.GetModuleExpander(moduleId);
            return moduleExpander == m_innerExpander ? this : new FileContentManagerSemanticPathExpander(m_fileContentManager, moduleExpander);
        }

        /// <inheritdoc />
        public override IEnumerable<AbsolutePath> GetWritableRoots()
        {
            return m_innerExpander.GetWritableRoots();
        }

        /// <inheritdoc />
        public override IEnumerable<AbsolutePath> GetSystemRoots()
        {
            return m_innerExpander.GetSystemRoots();
        }

        /// <inheritdoc />
        public override IEnumerable<AbsolutePath> GetPathsWithAllowedCreateDirectory()
        {
            return m_innerExpander.GetPathsWithAllowedCreateDirectory();
        }

        /// <inheritdoc />
        public override IEnumerable<SemanticPathInfo> GetSemanticPathInfos(Func<SemanticPathInfo, bool> filter)
        {
            return m_innerExpander.GetSemanticPathInfos(filter);
        }
    }
}
