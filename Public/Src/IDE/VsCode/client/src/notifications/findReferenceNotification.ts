// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

'use strict';

import { window, env, Uri, StatusBarItem, StatusBarAlignment } from 'vscode';

// This must be the first statement otherwise modules might got loaded with
// the wrong locale.
import * as nls from 'vscode-nls';
nls.config({ locale: env.language });

import { NotificationType } from 'vscode-jsonrpc';
import { LanguageClient } from 'vscode-languageclient';

/**
 * Contains the functionality for writing crucial information to the output window.
 */
export namespace FindReferenceNotification {
    /**
     * The find reference notification progress.
     * If the numbewrOfProcessedSpecs is equals to totalNumberOfSpecs, then the operation is completed.
    */
    export interface FindReferenceProgressParams {
        /** Total number of references found in so far. */
        numberOfReferences: number;
        /** Time spent for the find references operation so far. */
        pendingDurationInMs: number;
        /** Number of processed specs. */
        numberOfProcessedSpecs: number;
        /** Total number of specs. */
        totalNumberOfSpecs: number;
    }

    /**
     * The notification type (the method name, the parameter type, and return type) used
     * to configure the JSON RPC layer.
     */
    export const type : NotificationType<FindReferenceProgressParams, void> = new NotificationType('dscript/findReferenceProgress');

    /**
     * The status bar item that is created to show the find reference progress.
     */
    var findReferencesStatusBarItem : StatusBarItem = undefined;

    /**
     * The handler called by the JSON RPC layer to process the notification.
     */
    export function handler(params: FindReferenceProgressParams) : void {
        if (findReferencesStatusBarItem === undefined) {
            findReferencesStatusBarItem = window.createStatusBarItem(StatusBarAlignment.Left);
            findReferencesStatusBarItem.show();
        }
        
        if (params.numberOfProcessedSpecs === params.totalNumberOfSpecs) {
            if (params.numberOfProcessedSpecs === 0) {
                // the operation is cancelled.
                findReferencesStatusBarItem.text = `Find references is cancelled.`;
            }
            else {
                findReferencesStatusBarItem.text = `Found ${params.numberOfReferences} references in ${params.totalNumberOfSpecs} files by ${params.pendingDurationInMs}ms.`;
            }

            // Show that the operations is completed and hide the status bar after some time.
            setTimeout( () => {
                findReferencesStatusBarItem.hide();
                findReferencesStatusBarItem.dispose();
                findReferencesStatusBarItem = undefined;
            }, 5000);
        }
        else {
            findReferencesStatusBarItem.text = `Found ${params.numberOfReferences} references in ${params.numberOfProcessedSpecs}/${params.totalNumberOfSpecs} files by ${params.pendingDurationInMs}ms.`
        }
    }
}
