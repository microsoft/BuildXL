// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Sdk
{
    /// <summary>
    /// The interface the BuildXL engine uses to create the frontend.
    /// </summary>
    /// <remarks>
    /// This is yet another factory (c) that is responsible for creating entire front-end.
    /// Even that front end already has a lot of factories, all of them are too low level and are not exposed to the engine.
    /// Originally the app itself was responsible for front end construction but that prevented us from releasing all
    /// fron end related objects once they don't needed by the engine.
    /// With this factory the engine itself can control the lifetime of the front-end and can release it right after the evaluation phase.
    /// </remarks>
    public interface IFrontEndControllerFactory
    {
        /// <nodoc />
        IFrontEndController Create(PathTable pathTable, SymbolTable symbolTable);
    }

    /// <summary>
    /// Simple factory that returns a given controller instance.
    /// </summary>
    /// <remarks>
    /// Primarily used by tests and other non critical tools.
    /// </remarks>
    public sealed class LambdaBasedFrontEndControllerFactory : IFrontEndControllerFactory
    {
        private readonly Func<PathTable, SymbolTable, IFrontEndController> m_createController;

        /// <nodoc />
        public LambdaBasedFrontEndControllerFactory(Func<PathTable, SymbolTable, IFrontEndController> createController)
        {
            m_createController = createController;
        }

        /// <inheritdoc />
        public IFrontEndController Create(PathTable pathTable, SymbolTable symbolTable) => m_createController(pathTable, symbolTable);
    }
}
