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
var OutputTracer;
(function (OutputTracer) {
    /**
     * The notification type (the method name, the parameter type, and return type) used
     * to configure the JSON RPC layer.
     */
    OutputTracer.type = new vscode_jsonrpc_1.NotificationType('dscript/outputTrace');
    let currentClient;
    /** Stores the current client in the global state for handling notification from the language server. */
    function setUpTracer(client) {
        currentClient = client;
    }
    OutputTracer.setUpTracer = setUpTracer;
    /**
     * The handler called by the JSON RPC layer to process the notification.
     */
    function handler(params) {
        if (currentClient && params.level !== undefined && params.message !== undefined) {
            switch (params.level) {
                case 4 /* Informational */:
                case 0 /* LogAlways */:
                case 5 /* Verbose */:
                    currentClient.info(params.message);
                    break;
                case 1 /* Critical */:
                case 2 /* Error */:
                    currentClient.error(params.message);
                    break;
                case 3 /* Warning */:
                    currentClient.warn(params.message);
                    break;
            }
        }
    }
    OutputTracer.handler = handler;
})(OutputTracer = exports.OutputTracer || (exports.OutputTracer = {}));
//# sourceMappingURL=outputTracer.js.map