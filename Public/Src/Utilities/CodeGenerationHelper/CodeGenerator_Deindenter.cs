// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Utilities.CodeGenerationHelper
{
    public partial class CodeGenerator
    {
        private sealed class Deindenter : IDisposable
        {
            private readonly CodeGenerator m_parent;

            public Deindenter(CodeGenerator parent)
            {
                Contract.Requires(parent != null);
                m_parent = parent;
            }

            public void Dispose()
            {
                m_parent.m_indentLevel--;
            }
        }
    }
}
