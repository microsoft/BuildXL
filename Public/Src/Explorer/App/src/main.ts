// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

// Modules to control application life and create native browser window
import * as electron from "electron";

// Keep a global reference of the window object, if you don't, the window will
// be closed automatically when the JavaScript object is garbage collected.
let mainWindow: electron.BrowserWindow | undefined;

const menuTemplate = [
    {
        label: 'Debug',
        submenu: [
            {
                label: 'Open F12 Dev-Tools',
                click: () => {
                    if (mainWindow) {
                        mainWindow.webContents.openDevTools();
                    }
                },
            },
            {
                label: 'RefreshPage',
                click: () => {
                    if (mainWindow) {
                        mainWindow.reload();
                    }
                },
            },
            {
                label: 'ReloadApp',
                click: () => {
                    if (mainWindow) {
                        mainWindow.loadURL(`file://${__dirname}/App.html`);
                    }
                },
            },
        ],
    },
];

const mainMenu = electron.Menu.buildFromTemplate(menuTemplate);
electron.Menu.setApplicationMenu(mainMenu);

function createWindow() {

    // Create the browser window.
    mainWindow = new electron.BrowserWindow({
        width: 1600,
        height: 1080,
    });

    const session = electron.session.fromPartition('persist:name');
    session.clearCache(() => { });
    session.clearStorageData({}, (data: any) => { });

    mainWindow.loadURL(`file://${__dirname}/App.html`);

    var contents = mainWindow.webContents;

    // Handle navigation and zoom handlers
    contents.on('before-input-event', (event, input) => {
        switch (input.key) {
            case "F5":
                if (input.type == "keyDown") {
                    if (mainWindow) {
                        mainWindow.reload();
                    }
                }
                break;
            case "F12":
                if (input.type == "keyDown") {
                    contents.toggleDevTools();
                }
                break;
            case "ArrowLeft":
                if (input.alt && input.type == "keyUp" && contents.canGoBack()) {
                    contents.goBack();
                }
                break;
            case "ArrowRight":
                if (input.alt && input.type == "keyUp" && contents.canGoForward()) {
                    contents.goForward();
                }
                break;
            case "-":
                if (input.control && input.type == "keyDown") {
                    contents.getZoomLevel(level => contents.setZoomLevel(level - 1));
                }
                break;
            case "=":
                if (input.control && input.type == "keyDown") {
                    contents.getZoomLevel(level => contents.setZoomLevel(level + 1));
                }
                break;
            case "0":
                if (input.control && input.type == "keyDown") {
                    contents.setZoomLevel(0);
                }
                break;
        }
    });

    // Hookup global app events for back/forward to handle mouse buttons and keyboard shortcuts
    mainWindow.on('app-command', (e, cmd) => {
        switch (cmd) {
            case "browser-backward":
                if (contents.canGoBack()) {
                    contents.goBack()
                }
                break;
            case "browser-forward":
                if (contents.canGoForward()) {
                    contents.goForward()
                }
                break;
        }
    });

    // Emitted when the window is closed.
    mainWindow.on('closed', () => {
        // Dereference the window object, usually you would store windows
        // in an array if your app supports multi windows, this is the time
        // when you should delete the corresponding element.
        if (mainWindow) {
            mainWindow = undefined;
        }
    });
}

// This method will be called when Electron has finished
// initialization and is ready to create browser windows.
// Some APIs can only be used after this event occurs.
electron.app.on('ready', createWindow);

// Quit when all windows are closed.
electron.app.on('window-all-closed', () => {
    // On OS X it is common for applications and their menu bar
    // to stay active until the user quits explicitly with Cmd + Q
    if (process.platform !== 'darwin') {
        electron.app.quit();
    }
});

electron.app.on('activate', () => {
    // On OS X it's common to re-create a window in the app when the
    // dock icon is clicked and there are no other windows open.
    if (mainWindow === null) {
        createWindow();
    }
});

