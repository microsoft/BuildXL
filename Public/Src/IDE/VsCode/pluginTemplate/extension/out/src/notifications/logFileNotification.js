// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

'use strict';
Object.defineProperty(exports, "__esModule", { value: true });
const vscode_1 = require("vscode");
const vscode_jsonrpc_1 = require("vscode-jsonrpc");
/**
 * Contains the functionality for receiving the log file location
 * sent from the language server over the JSON RPC back channel.
 *
 * CODESYNC: This must be kept in sync with the language server implementation:
 * public\src\ide\languageserver\jsonrpc\logfilelocationparams.cs
 */
var LogFileLocationNotification;
(function (LogFileLocationNotification) {
    var logFileLocation = undefined;
    LogFileLocationNotification.type = new vscode_jsonrpc_1.NotificationType(`dscript/logFileLocation`);
    function handler(params) {
        logFileLocation = vscode_1.Uri.file(params.file);
    }
    LogFileLocationNotification.handler = handler;
    function tryOpenLogFile() {
        if (logFileLocation !== undefined) {
            vscode_1.window.showTextDocument(logFileLocation);
            return true;
        }
        return false;
    }
    LogFileLocationNotification.tryOpenLogFile = tryOpenLogFile;
})(LogFileLocationNotification = exports.LogFileLocationNotification || (exports.LogFileLocationNotification = {}));
//# sourceMappingURL=logFileNotification.js.map
