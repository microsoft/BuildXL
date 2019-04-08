// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Settings from "../models/settings";
import * as Status from "../models/status";
import * as LocalBuilds from "../models/localBuilds";

import * as fs from "fs";
import * as cp from "child_process";
import * as path from "path";
import * as findFreePort from "find-free-port";
import * as request from "request-promise-native";

// $TODO: We have to make this https but this requires some investigation on how to get a key setup without user intervention.
const defaultProtocol = "http://";

class Connection {

    private _kind : "devServer" | "local";
    private _process: cp.ChildProcess | undefined;

    public host: string;
    public port: number;
    public engineVersion: string | undefined;
    public engineBinFolder: string | undefined;
    public lastPing: Date;

    public constructor(kind: "devServer" | "local", host: string, port: number, engineVersion?: string, engineBinFolder?: string) {
        if (port < 80 || port >= 65535) {
            throw "Unexpected port number: " + port;
        }
        if (host.indexOf("/") >= 0 || host.indexOf("\\") >= 0 || host.indexOf(":") >= 0) {
            throw "Unexpected characters in host name: " + host;
        }

        this._kind = kind;
        this.host = host;
        this.port = port;
        this.engineVersion = engineVersion;
        this.engineBinFolder = engineBinFolder;
    }

    public isLive() : boolean {
        switch (this._kind) {
            case "devServer":
                return true;
            case "local":
                return this._process !== undefined;
            default:
                throw "Unsupported kind";
        }
    }

    public terminate() {
        if (this._process) {
            this._process.kill();
            this._process = undefined;
        }
    }

    private getUrlBase() : string {
        return defaultProtocol + this.host + ":" + this.port;
    }

    public async sendRequest<T>(url: string) : Promise<T> {
        let requstUrl = this.getUrlBase() + url;

        let result = await request.get(
            requstUrl, 
            {
                port: this.port,
                json: true,
            });

        this.lastPing = new Date();

        return result;
    }

    public async launch() : Promise<void> {
        
        // $TODO: Handle mac binary names
        let launchPath = path.join(this.engineBinFolder || ".", "tools", "bxp-server", "bxp-server.exe");
        if (!fs.existsSync(launchPath)) {
            throw Status.logLocalError("Error luanching server. Did not find file: " + launchPath);
        }
        
        Status.logLocalInfo("Launching local server at port " + this.port);
        let process = await cp.spawn(
            launchPath,
            [this.getUrlBase()],
            {
                // $TODO: make the processes detached. For this upon boot we need to scan the box for servers so the can be reused
                // between build explorer invocations. For now we just let them be killed when the explorer is killed.
                //detached: true,
                windowsHide: true,
            });
        
        // Allow the server to live longer than the viewer, it will
        // self terminate if not used within a certain timeframe.
        process.unref();
        
        const source = `${this.host}:${this.port}`;
        process.stdout.on('data', (data) => {
            Status.logMessage({source: source, status: "info", message: `${data}`});
          });
          
        process.stderr.on('data', (data) => {
            Status.logMessage({source: source, status: "error", message: `${data}`});
        });

        process.on('close', code => {
            if (code === 0) {
                Status.logMessage({source: source, status: "error", message: `Server self-terminated after being inactive for too long`});
            } else {
                Status.logMessage({source: source, status: "error", message: `Server unexpectedly terminated with exit code: ${code}`});
            }

            // mark as not connected anymore
            this._process = undefined;
        })

        var testResult = await this.sendRequest<TestResult>("/test");

        Status.logLocalInfo("Succesfully launched local server for version " + testResult.version);

        this.engineVersion = testResult.version;
        this._process = process;
    }
}

interface TestResult {
    status: string,
    version: string
}


let devConnection : Connection = new Connection("devServer", Settings.current.devServerName, Settings.current.devServerPort);
window.addEventListener("bxp-settingsUpdated", (e : CustomEvent<Settings.SettingsData>) => {
    if (devConnection.host !== e.detail.devServerName || devConnection.port !== e.detail.devServerPort) {
        devConnection = new Connection("devServer", e.detail.devServerName, e.detail.devServerPort);
    }
});

const connections = new Set<Connection>();
const builds = new Map<string, Connection>();


export async function getConnection(sessionId: string) : Promise<Connection> {
    let settings = Settings.current;
    if (settings.useDevServer) {
        return devConnection;
    }

    // Check if this build has a registred connection;
    let connection = builds.get(sessionId);
    if (connection) {
        if (connection && connection.isLive()) {
            return connection;
        }
        else {
            // Removing the cache as the connection is no longer alive.
            builds.delete(sessionId);
            connections.delete(connection);
        }
    }

    const buildDetails = LocalBuilds.tryGetBuildDetails(sessionId);
    if (!buildDetails) {
        throw Status.logLocalError("Failed to find build details to launch server.");
    }

    // First find a server with the same BuildXL binary folder
    for (let connection of connections.values()) {
        if (connection.engineBinFolder === buildDetails.engineBinFolder) {
            if (!connection.isLive()) {
                connection.launch();
            }
            builds.set(sessionId, connection);
            return connection;
        }
    }

    // Second try to find a server with the same BuildXL version
    for (let connection of connections.values()) {
        if (connection.engineVersion === buildDetails.engineVersion && connection.isLive()) {
            builds.set(sessionId, connection);
            return connection;
        }
    }
    
    // Launch a new server
    if (connections.size >= Settings.current.maxServers) {
        // We don't have enough space to launch a server, free one up.
        pruneOneConnections();
    }

    connection = await launchNewServer(buildDetails);
    if (connection) {
        builds.set(sessionId, connection);
        connections.add(connection);
        return connection;
    }

    throw "Error Launching new server";
}

async function launchNewServer(buildDetails: LocalBuilds.BuildDetails) : Promise<Connection> {
    let startPort = Settings.current.startServerPort;
    let port : number = await findFreePort(startPort, startPort + 100);

    let connection = new Connection("local", "localhost", port, buildDetails.engineVersion, buildDetails.engineBinFolder);
    await connection.launch();
    return connection;
}

function pruneOneConnections() {
    // First find a disconnected slot;
    for (var kv of connections)
    {
        if (!kv.isLive()) {
            // Removed item
            connections.delete(kv);
            return;
        }
    }

    let oldestConnection : Connection | undefined = undefined;
    for (var connection of connections.values())
    {
        if (!oldestConnection || connection.lastPing < oldestConnection.lastPing) {
            oldestConnection = connection;
        }
    }

    if (oldestConnection) {
        oldestConnection.terminate();
        connections.delete(oldestConnection);
    }
}
