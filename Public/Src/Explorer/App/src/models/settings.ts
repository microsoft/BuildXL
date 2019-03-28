// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as electron from 'electron';
import * as path from 'path';
import * as fs from 'fs';

/**
 * Stores all the user settings for the app
 */
export interface SettingsData {
    
    /** The theme of the UI to render */
    theme: ThemeStyle;

    /** Wether all server request should be routed to a presumed already running server */
    useDevServer: boolean;
    
    /** The hostname of the dev server */
    devServerName: string;
    
    /** The port of the dev server */
    devServerPort: number;

    /** 
     * The maximum number of servers that are allowed to be active at one point when not using a DevServer. 
     * It this threshold is exceeded, i.e. it needs to launch another backend server.
     */
    maxServers: number;

    /** 
     * The server port to start new servers on.
     */
    startServerPort: number;
}

export type ThemeStyle = "light" | "dark" | "hack";

const defaultSettings : SettingsData = {
    theme: "dark",
    useDevServer: false,
    devServerName: "localhost",
    devServerPort: 5000,
    maxServers: 3,
    startServerPort: 5550,
};

const settingsFolder = electron.remote.app.getPath("userData")
const settingsFile = path.join(settingsFolder, "buildxlviewer.json")

export function load() {
    try {
        if (!fs.existsSync(settingsFile)) {
            current = defaultSettings;
            return;
        }

        let buffer = fs.readFileSync(settingsFile, {encoding: "utf8"});
        let loadedSettings = <SettingsData>JSON.parse(buffer);
        current = Object.assign(defaultSettings, loadedSettings)
    }
    catch (error) {
        console.error("Failed to load settings file: " + error);
        current = defaultSettings;
    }
}

export function save() {
    try {
        if (!fs.existsSync(settingsFolder)) {
            fs.mkdirSync(settingsFolder);
        }

        let content = JSON.stringify(current, null, 4)
        fs.writeFileSync(settingsFile, content, {encoding: "utf8"});
    }    
    catch (error) {
        console.error("Failed to save settings file: " + error);
    }
}

export function update<K extends keyof SettingsData>(newSettings: Pick<SettingsData, K> | SettingsData)
{
    current = Object.assign(current, newSettings);
    
    var event = new CustomEvent("bxp-settingsUpdated", { detail: current });
    window.dispatchEvent(event);

    save();
}

export let current : SettingsData;
load();
