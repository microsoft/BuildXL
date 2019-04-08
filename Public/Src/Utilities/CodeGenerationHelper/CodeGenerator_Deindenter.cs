// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
