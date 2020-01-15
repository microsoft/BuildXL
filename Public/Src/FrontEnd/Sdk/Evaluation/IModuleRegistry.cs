// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.FrontEnd.Sdk.Evaluation
{
    /// <nodoc />
    public interface IModuleRegistry
    {
        // TODO: This is temporarily a marker interface to abstract away the Runtime model common to FrontEnds and DScript syntax.

        /// <nodoc />
        void AddUninstantiatedModuleInfo(IUninstantiatedModuleInfo moduleInfo);
    }
}
