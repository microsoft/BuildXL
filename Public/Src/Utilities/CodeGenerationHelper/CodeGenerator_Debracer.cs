// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Utilities.CodeGenerationHelper
{
    public partial class CodeGenerator
    {
        private sealed class Debracer : IDisposable
        {
            private readonly CodeGenerator m_parent;

            public Debracer(CodeGenerator parent)
            {
                Contract.Requires(parent != null);

                m_parent = parent;
            }

            public void Dispose()
            {
                m_parent.m_indentLevel--;
                m_parent.Format("{0}", "}\n");
            }
        }

        private sealed class DebracerStatement : IDisposable
        {
            private readonly string m_close;
            private readonly CodeGenerator m_parent;

            public DebracerStatement(CodeGenerator parent, string close)
            {
                Contract.Requires(parent != null);
                Contract.Requires(!string.IsNullOrEmpty(close));

                m_parent = parent;
                m_close = close;
            }

            public void Dispose()
            {
                m_parent.m_indentLevel--;
                m_parent.Format("{0}{1}{2}", "}", m_close, "\n");
            }
        }

        private sealed class EndWrappedPreprocessor : IDisposable
        {
            private readonly string m_close;
            private readonly CodeGenerator m_parent;

            public EndWrappedPreprocessor(CodeGenerator parent, string close)
            {
                Contract.Requires(parent != null);
                Contract.Requires(!string.IsNullOrEmpty(close));

                m_parent = parent;
                m_close = close;
            }

            public void Dispose()
            {
                m_parent.Format("#{0}{1}", m_close, "\n");
            }
        }
    }
}
