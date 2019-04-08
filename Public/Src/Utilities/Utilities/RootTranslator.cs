// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Utilities
{
    /// <summary>
    /// Allows expansion of paths with a specified string used to expand roots.
    /// </summary>
    /// <remarks>
    /// These expansions are used on storing contents to cache when CASaS is enabled.
    /// </remarks>
    public sealed class RootTranslator
    {
        private readonly DirectoryTranslator m_translator;

        /// <summary>
        /// Constructor.
        /// </summary>
        public RootTranslator(DirectoryTranslator translator)
        {
            m_translator = translator;
        }

        /// <summary>
        /// Constructor.
        /// </summary>
        public RootTranslator()
        {
            m_translator = new DirectoryTranslator();
        }

        /// <summary>
        /// Seals the translator and allows translations
        /// </summary>
        public void Seal()
        {
            m_translator.Seal();
        }

        /// <summary>
        /// Adds a root translation
        /// </summary>
        public void AddTranslation(string sourcePath, string targetPath)
        {
            m_translator.AddTranslation(sourcePath, targetPath);
        }

        /// <summary>
        /// Adds a root translation
        /// </summary>
        public void AddTranslation(ExpandedAbsolutePath sourcePath, ExpandedAbsolutePath targetPath)
        {
            AddTranslation(sourcePath.ExpandedPath, targetPath.ExpandedPath);
        }

        /// <summary>
        /// Translate the path based on the added translations
        /// </summary>
        public string Translate(string path)
        {
            return m_translator.Translate(path);
        }
    }
}
