// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace TypeScript.Net.Types
{
    /// <summary>
    /// Factory method that gets a content of the file.
    /// </summary>
    /// <remarks>
    /// <see cref="ISourceFile.Text"/> property does not necesserily keep a source file content.
    /// Right after the parsing is done, the file content is eligible for GC and the <see cref="ISourceFile"/> just holds
    /// a reference to the <see cref="ITextSourceProvider"/> to reread the content for an error case.
    /// </remarks>
    public interface ITextSourceProvider
    {
        /// <summary>
        /// Gets the <see cref="TextSource"/>.
        /// </summary>
        TextSource ReadTextSource();
    }

    /// <summary>
    /// Simple implementation of the <see cref="ITextSourceProvider"/> that returns a given <see cref="TextSource"/>.
    /// </summary>
    internal sealed class TextSourceProvider : ITextSourceProvider
    {
        private readonly TextSource m_textSource;

        public TextSourceProvider(TextSource textSource)
        {
            m_textSource = textSource;
        }

        public TextSource ReadTextSource()
        {
            return m_textSource;
        }
    }
}
