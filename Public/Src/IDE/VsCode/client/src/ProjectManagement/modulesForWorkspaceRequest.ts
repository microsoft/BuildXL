// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

'use strict';

// This must be the first statement otherwise modules might got loaded with
// the wrong locale.
import { env } from 'vscode';
import * as nls from 'vscode-nls';
nls.config({ locale: env.language });

import { WorkspaceLoadingNotification } from '../notifications/workspaceLoadingNotification';
import { RequestType, LanguageClient } from 'vscode-languageclient';

/**
 * Represents the parameters for the "dscript/modulesForWorkspace" JSON-RPC request, which, there are none.
 */
export interface ModulesForWorkspaceParams {
    includeSpecialConfigurationModules : boolean;
}

/**
 * Represents a DScript module.
 */
export interface ModuleDescriptor {
    /**
     *  The identifier of the module.
     */
    id: number,

    /** 
     * The name of the module.
     */
    name: string

    /**
     * The config file name for the module.
     */
    configFilename: string;

    /**
     * The version of the module.
     */
    veersion: string;
}

/**
 * Represents the results of the "dscript/modulesForWorkspace" JSON-RPC request.
 */
export interface ModulesForWorkspaceResult {
    /**
     * The array of modules present in the DScript workspace.
     */
    modules: ModuleDescriptor[];
};

/**
 *  Create the JSON-RPC request object for retrieving the modules present in the DScript workspace.
 */
export const ModulesForWorkspaceRequest = new RequestType<ModulesForWorkspaceParams, ModulesForWorkspaceResult, any, any>("dscript/modulesForWorkspace");
