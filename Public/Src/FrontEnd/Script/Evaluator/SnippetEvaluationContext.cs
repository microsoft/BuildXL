// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Tracing;

namespace BuildXL.FrontEnd.Script.Evaluator
{
    /// <summary>
    /// Provides a context that is useful for evaluating a snippet of code in debugging mode.
    /// The only purpose of this class is to encapsulate in a disposable way the modifications that a context needs
    /// to be suitable for using when evaluating an expression in debugger mode. Disposing the object sets the passed context
    /// back to its original shape
    /// </summary>
    /// <remarks>
    /// This class assumes a context instance is not accessed in a multithreaded environment and that a context instance is wrapped at most once
    /// by an instance of this class
    /// </remarks>
    public sealed class SnippetEvaluationContext : IDisposable
    {
        private Context m_context;
        private readonly bool m_originalSkipDecorator;

        /// <summary>
        /// The provided context is modified so the given logger is used and the decorator is always skipped
        /// </summary>
        public SnippetEvaluationContext(Context context, Logger logger)
        {
            Contract.Requires(context.LoggerOverride == null);

            m_context = context;

            m_originalSkipDecorator = context.SkipDecorator;

            m_context.LoggerOverride = logger;
            m_context.SkipDecorator = true;
        }

        /// <nodoc/>
        public Context GetContextForSnippetEvaluation()
        {
            return m_context;
        }

        /// <summary>
        /// Sets the passed context back to its original state
        /// </summary>
        public void Dispose()
        {
            if (m_context != null)
            {
                m_context.LoggerOverride = null;
                m_context.SkipDecorator = m_originalSkipDecorator;
            }

            m_context = null;
        }
    }
}
