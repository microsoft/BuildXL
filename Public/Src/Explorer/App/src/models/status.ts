// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

const maxNumberOfMessages = 8192;

export interface Status {
    lastMessage: LoggedMessage | undefined,
    messages: LoggedMessage[],
}

export const status : Status = {
    lastMessage: undefined,
    messages: [],
}

logMessage({
    source: "app",
    status: "info",
    message: "App started succesfully",
});

export interface Message {
    /** Wether this comes from a local server, cloudbuild server or this app */
    source: string,
    
    /** level of the event. error, warning and info will be displayed in the status bar, verbose goes to a a more detailed log */
    status: "info" | "verbose" | "warning" | "error",

    /** User facing message */
    message: string,
}

export interface LoggedMessage extends Message {
    /** when the event was logged */
    time: Date,
}


export function logLocalError(message: string) : string {
    logMessage({
        source: "app",
        status: "error",
        message: message,
    });

    return message;
}
export function logLocalInfo(message: string) : void {
    logMessage({
        source: "app",
        status: "info",
        message: message,
    });
}

export function logMessage(message: Message) {
    const msg : LoggedMessage = Object.assign({
        time: new Date(),
    }, message);

    status.messages.push(msg);
    if (status.messages.length > maxNumberOfMessages) {
        status.messages.shift(); // drop the first one
    }

    status.lastMessage = msg;

    var event = new CustomEvent<LoggedMessage>("bxp-messageLogged", {detail: msg });
    window.dispatchEvent(event);

}
