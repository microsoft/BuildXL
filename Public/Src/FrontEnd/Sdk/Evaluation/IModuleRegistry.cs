// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
