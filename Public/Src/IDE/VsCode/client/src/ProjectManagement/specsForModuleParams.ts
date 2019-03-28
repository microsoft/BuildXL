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
import { ModuleDescriptor } from './modulesForWorkspaceRequest'

/**
 * Represents the parameters for the "dscript/specsForModule" request.
 */
export interface RequestSpecsForModuleParams {
    /**
     * The module identifier to request spec files for.
     */
    moduleDescriptor: ModuleDescriptor;
}

/**
 * Represents a spec file in the BuildXL workspace.
 */
export interface SpecDescriptor {
    /**
     * The identifier of the BuildXL spec file.
     */
    id: number,

    /**
     * The file name for the spec file.
     */
    fileName: string
}

/**
 * Represents the results of the "dscript/specsForModule" JSON-RPC request.
 */
export interface SpecsFromModuleResult {
    /**
     * The array of BuildXL spec files present in a module.
     */
    specs: SpecDescriptor[];
};

/**
 *  Create the JSON-RPC request object for retrieving the specs presint in a module in the DScript workspace.
 */
export const SpecsForModulesRequest = new RequestType<RequestSpecsForModuleParams, SpecsFromModuleResult, any, any>("dscript/specsForModule");
