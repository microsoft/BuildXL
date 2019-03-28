// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

'use strict';
Object.defineProperty(exports, "__esModule", { value: true });
const vscode_1 = require("vscode");
// This must be the first statement otherwise modules might got loaded with
// the wrong locale.
const nls = require("vscode-nls");
nls.config({ locale: vscode_1.env.language });
const vscode_jsonrpc_1 = require("vscode-jsonrpc");
/**
 * Contains the functionality for writing crucial information to the output window.
 */
var FindReferenceNotification;
(function (FindReferenceNotification) {
    /**
     * The notification type (the method name, the parameter type, and return type) used
     * to configure the JSON RPC layer.
     */
    FindReferenceNotification.type = new vscode_jsonrpc_1.NotificationType('dscript/findReferenceProgress');
    /**
     * The status bar item that is created to show the find reference progress.
     */
    var findReferencesStatusBarItem = undefined;
    /**
     * The handler called by the JSON RPC layer to process the notification.
     */
    function handler(params) {
        if (findReferencesStatusBarItem === undefined) {
            findReferencesStatusBarItem = vscode_1.window.createStatusBarItem(vscode_1.StatusBarAlignment.Left);
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
            setTimeout(() => {
                findReferencesStatusBarItem.hide();
                findReferencesStatusBarItem.dispose();
                findReferencesStatusBarItem = undefined;
            }, 5000);
        }
        else {
            findReferencesStatusBarItem.text = `Found ${params.numberOfReferences} references in ${params.numberOfProcessedSpecs}/${params.totalNumberOfSpecs} files by ${params.pendingDurationInMs}ms.`;
        }
    }
    FindReferenceNotification.handler = handler;
})(FindReferenceNotification = exports.FindReferenceNotification || (exports.FindReferenceNotification = {}));
//# sourceMappingURL=findReferenceNotification.js.map
