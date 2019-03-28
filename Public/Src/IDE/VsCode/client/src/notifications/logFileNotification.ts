// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

'use strict';

import { window, env, Uri } from 'vscode';
import { NotificationType } from 'vscode-jsonrpc';

/**
 * Contains the functionality for receiving the log file location
 * sent from the language server over the JSON RPC back channel.
 * 
 * CODESYNC: This must be kept in sync with the language server implementation:
 * public\src\ide\languageserver\jsonrpc\logfilelocationparams.cs
 */
export namespace LogFileLocationNotification {
    var logFileLocation : Uri = undefined;

    interface LogFileLocationParams {
        file: string;
    }

    export const type: NotificationType<LogFileLocationParams, void> = new NotificationType(`dscript/logFileLocation`);

    export function handler(params: LogFileLocationParams) : void {
        logFileLocation = Uri.file(params.file);
    }

    export function tryOpenLogFile() : boolean {
        if (logFileLocation !== undefined) {
            window.showTextDocument(logFileLocation);
            return true;
        }

        return false;
    }
}

