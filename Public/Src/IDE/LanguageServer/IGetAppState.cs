// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using JetBrains.Annotations;

namespace BuildXL.Ide.LanguageServer
{
    /// <summary>
    /// Used py providers to attempt to get the current application state
    /// </summary>
    /// <remarks>
    /// JSON-RPC providers can only be added as a target once and there is currently no
    /// remove method once an RPC target has been added.
    /// 
    /// So, after a provider is initialized and added as a target, if the user chooses to
    /// reload the BuildXL workspace, the appstate is reinitialized (that is nulled out and re-assigned.
    /// 
    /// Since we cannot re-create the provider after it has been handed to JSON-RPC layer,
    /// we need a way for the provider to retrieve the appstate before use.
    /// 
    /// This interface is implemented by the app and blocks on the workspace loading.
    /// 
    /// The provider is reponsible for checking for null before use.
    /// </remarks>
    [CanBeNull]
    public delegate AppState GetAppState();
}
