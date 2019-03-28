// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

'use strict';

// This must be the first statement otherwise modules might got loaded with
// the wrong locale.
import * as nls from 'vscode-nls';
import { env, ExtensionContext, workspace, window } from 'vscode';

nls.config({ locale: env.language });
import { NotificationType, LanguageClient, ParameterInformation } from 'vscode-languageclient';

import * as path from 'path';
import * as fs from 'fs';

/**
 * The parameters for the "dscript/sourceFileConfiguration" notification.
 */
interface AddSourceFileConfiguration
{
    /**
     *  The property name (such as "sources") to add the source file to.
     */
    propertyName : string;

    /**
     * The function name whose argument interface contains the property.
     * referenced by propertyName
     */
    functionName: string;

    /**
     * The position of the argument in the function call that is of the
     * type referenced by propertyTypeName
     */
    argumentPosition : number;

    /**
     *  The type of the argument specified to the function (such as "Arguments") 
     */
    argumentTypeName : string;

    /**
     * The module for which the property specified in propertyName belongs to (such as "Build.Wdg.Native.Tools.StaticLibrary")
     */
    argumentTypeModuleName : string;
}

/**
* The parameters for the "dscript/sourceFileConfiguration" notification.
*/
interface AddSourceFileConfigurationParams
{
    /**
     *  The set of configurations needed for adding a source file
     * 
     * Each configuration can be different. For example, adding a source file to a DLL
     * can be different than adding a source file to a static link library.
     */
    configurations: AddSourceFileConfiguration[];
}

/**
*  Create the JSON-RPC request object for retrieving the specs present in a module in the DScript workspace.
*/
const AddSourceFileConfigurationNotification = new NotificationType<AddSourceFileConfigurationParams, any>("dscript/sourceFileConfiguration");

export function sendAddSourceFileConfigurationToLanguageServer(languageClient : LanguageClient, extensionContext: ExtensionContext) {
    // Send the add source notification to the language server
    const userOverridePath = path.join(extensionContext.storagePath, "addSourceFileConfiguration.json");
    const addSourceConfugrationFile = fs.existsSync(userOverridePath) ? userOverridePath : extensionContext.asAbsolutePath("ProjectManagement\\addSourceFileConfiguration.json");

    let addSourceConfugrationParams: AddSourceFileConfigurationParams;
    try {
        addSourceConfugrationParams = <AddSourceFileConfigurationParams>require(addSourceConfugrationFile);
    } catch {
        window.showErrorMessage(`The add source configuration file '${addSourceConfugrationFile}' cannot be parsed.` )
    }

    languageClient.sendNotification(AddSourceFileConfigurationNotification, addSourceConfugrationParams);
}
