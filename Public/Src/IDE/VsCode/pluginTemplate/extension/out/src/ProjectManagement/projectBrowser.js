// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

'use strict';
Object.defineProperty(exports, "__esModule", { value: true });
// This must be the first statement otherwise modules might got loaded with
// the wrong locale.
const vscode_1 = require("vscode");
const nls = require("vscode-nls");
nls.config({ locale: vscode_1.env.language });
const workspaceLoadingNotification_1 = require("../notifications/workspaceLoadingNotification");
const path = require("path");
const modulesForWorkspaceRequest_1 = require("./modulesForWorkspaceRequest");
const specsForModuleParams_1 = require("./specsForModuleParams");
const addSourceFileConfigurationNotification_1 = require("./addSourceFileConfigurationNotification");
const addSourceFileRequest_1 = require("./addSourceFileRequest");
const addProjectRequest_1 = require("./addProjectRequest");
const assert = require("assert");
var languageClient;
var extensionContext;
var createdProjectBrowser = false;
// A map from the BuildXL module configuration path to the tree item
// representing a BuildXL Module. Can eventually be used
// to reveal the BuildXL module an opened file is located in.
// This cannot be done yet due to bug: https://github.com/Microsoft/vscode/issues/30288
var modulePathToDominoModuleMap;
/**
 * Represents a BuildXL spec file in the project browser tree.
 */
class DominoSpecFileTreeItem {
    constructor(specDescriptor, showShortName) {
        this._specDescriptor = specDescriptor;
        this._showShortName = showShortName;
    }
    /**
     * Returns a URI for the spec file that can be opened in the text editor.
     */
    uri() {
        // The files we get back from the language server need to have
        // the schema appened to them in order VSCode to understand them.
        return vscode_1.Uri.parse("file:///" + this._specDescriptor.fileName);
    }
    /**
     * Returns a "tree item" representing a BuildXL spec file for the project browser.
     */
    getTreeItem() {
        return {
            command: {
                arguments: [this, undefined],
                title: "Open",
                tooltip: "Open",
                command: 'dscript.openSpecData'
            },
            contextValue: "dscript.spec",
            label: this._showShortName ? path.basename(this._specDescriptor.fileName) : this._specDescriptor.fileName,
            collapsibleState: vscode_1.TreeItemCollapsibleState.None
        };
    }
    /**
     * BuildXL spec files currently have no childern.
     */
    getChildren() {
        return [];
    }
}
exports.DominoSpecFileTreeItem = DominoSpecFileTreeItem;
/**
 * Used to represent a solution like view based on directory
 * hierachy.
 */
class DirectoryTreeItem {
    constructor(directoryName) {
        this._directoryName = directoryName;
        this._retrieveSpecs = true;
    }
    // Creates or retrieves a DirectoryTreeItem that represents
    // the passed in single directory name (path-atom)
    getOrAddSingleChild(directoryName) {
        assert.equal(directoryName.includes(path.sep), false);
        // If this is the first time through, allocate the children
        if (this._childDirectories === undefined) {
            this._childDirectories = new Map();
        }
        const directoryNameForMap = directoryName.toLowerCase();
        // Create a new item if one does not exist for this directory.
        if (!this._childDirectories.has(directoryNameForMap)) {
            const newDirectoryTreeItem = new DirectoryTreeItem(directoryName);
            this._childDirectories.set(directoryNameForMap, newDirectoryTreeItem);
            // Let the tree view know that we've added a child.
            // This will cause the tree-view to re-acquire the childern.
            dominoDirectoryTreeEventEmitter.fire(this);
            return newDirectoryTreeItem;
        }
        return this._childDirectories.get(directoryNameForMap);
    }
    /**
     * Creates a hierarchy of DirectoryTreeItem's based on the specified path.
     * @returns A directory tree item representing the last portion of the passed in path.
     * @param directoryName The path to split apart and create a hierarchy of DirectoryTreeItems
     */
    getOrAddChild(directoryName) {
        const paths = directoryName.split(path.sep);
        let nextDirectoryTreeItem = this;
        paths.forEach((singlePath, index) => {
            nextDirectoryTreeItem = nextDirectoryTreeItem.getOrAddSingleChild(singlePath);
        });
        return nextDirectoryTreeItem;
    }
    /**
     * Associates a module descriptor with this directory tree item.
     * @param mod The module to associate with this directory item.
     */
    addModule(mod) {
        if (this._modules === undefined) {
            this._modules = [];
        }
        this._modules.push(mod);
        // Let the tree view know that we've added a child.
        // This will cause the tree-view to re-acquire the childern.
        dominoDirectoryTreeEventEmitter.fire(this);
    }
    /**
     * Associates the spcified project spec file with this directory item.
     * @param projectSpec The project spec file to associate with this directory item.
     */
    addProjectSpec(projectSpec) {
        if (this._projectSpecs === undefined) {
            this._projectSpecs = [];
        }
        this._projectSpecs.push(projectSpec);
        dominoDirectoryTreeEventEmitter.fire(this);
    }
    /**
     * Returns the tree item to the tree-view for this directory tree item.
     */
    getTreeItem() {
        return {
            // Set a context value used by the context menu contribution so
            // it knows this is a project directory.
            contextValue: "dscript.projectDirectory",
            // The label is simply the directory name
            label: this._directoryName,
            // The collapisble state has to check multiple things as it can have
            // multiple children. It needs to check for directories, project specs
            // and modules.
            collapsibleState: ((this._childDirectories && this._childDirectories.size !== 0) ||
                (this._projectSpecs && this._projectSpecs.length !== 0) ||
                (this._modules && this._modules.length !== 0)) ? vscode_1.TreeItemCollapsibleState.Collapsed : vscode_1.TreeItemCollapsibleState.None
        };
    }
    /**
     * Returns the children known to this directory.
     */
    getChildren() {
        const directoryChildren = [];
        // Add our child directories, sorted by name.
        if (this._childDirectories && this._childDirectories.size !== 0) {
            directoryChildren.push(...this._childDirectories.values());
            directoryChildren.sort((a, b) => {
                return a._directoryName.localeCompare(b._directoryName);
            });
        }
        // Add our modules, sorted by module name.
        const moduleChildren = [];
        if (this._modules && this._modules.length !== 0) {
            this._modules.forEach((mod) => {
                moduleChildren.push(new DominoModuleTreeItem(mod, dominoDirectoryTreeEventEmitter));
            });
            moduleChildren.sort((a, b) => {
                return a.descriptor().name.localeCompare(b.descriptor().name);
            });
        }
        // Add our project specs.
        const specChildren = [];
        // If we have not asked the language server for the specs, then do so know.
        if (this._modules && this._modules.length !== 0 && this._retrieveSpecs) {
            this._retrieveSpecs = false;
            this._modules.forEach((mod) => {
                languageClient.sendRequest(specsForModuleParams_1.SpecsForModulesRequest, { moduleDescriptor: mod }).then((specs) => {
                    specs.specs.forEach((spec, index) => {
                        const directoryTreeItem = directoryTreeItems.getOrAddChild(path.dirname(spec.fileName));
                        directoryTreeItem.addProjectSpec(spec);
                    });
                });
            });
        }
        else {
            if (this._projectSpecs && this._projectSpecs.length !== 0) {
                this._projectSpecs.forEach((spec) => {
                    specChildren.push(new DominoSpecFileTreeItem(spec, true));
                });
                specChildren.sort((a, b) => {
                    return a.uri().fsPath.localeCompare(b.uri().fsPath);
                });
            }
        }
        // Finally return the child array which is a combination of all three types.
        return [...directoryChildren, ...moduleChildren, ...specChildren];
    }
}
// The root directory tree item.
const directoryTreeItems = new DirectoryTreeItem(undefined);
/**
 * Represents a BuildXL module in the project broweser tree.
 */
class DominoModuleTreeItem {
    constructor(moduleDescriptor, eventEmiiter) {
        this._moduleDescriptor = moduleDescriptor;
        this._collapseState = vscode_1.TreeItemCollapsibleState.Collapsed;
        this._eventEmiiter = eventEmiiter;
    }
    /**
     * Returns a "tree item" representing a module  for the project browser.
     */
    getTreeItem() {
        return {
            contextValue: "dscript.module",
            label: this._moduleDescriptor.name,
            collapsibleState: this._collapseState
        };
    }
    expand() {
        // Note that this does not actually work due to 
        // https://github.com/Microsoft/vscode/issues/40179
        this._collapseState = vscode_1.TreeItemCollapsibleState.Expanded;
        this._eventEmiiter.fire(this);
    }
    collapse() {
        this._collapseState = vscode_1.TreeItemCollapsibleState.Collapsed;
        this._eventEmiiter.fire(this);
    }
    /**
     * Returns the children of the BuildXL module, which, are currently spec files.
     */
    getChildren() {
        if (this._children === undefined) {
            this._children = [];
            // Capture the this pointer in a block variable so it can
            // be used after the sendRequest completes.
            let moduleData = this;
            languageClient.sendRequest(specsForModuleParams_1.SpecsForModulesRequest, { moduleDescriptor: this._moduleDescriptor }).then((specs) => {
                specs.specs.forEach((spec, index) => {
                    moduleData._children.push(new DominoSpecFileTreeItem(spec, false));
                });
                // Now, fire the change event to the tree so that it calls the getChildren again
                moduleData._eventEmiiter.fire(moduleData);
            });
        }
        return this._children;
    }
    /**
     * Returns the module descriptor for the item.
     */
    descriptor() {
        return this._moduleDescriptor;
    }
}
exports.DominoModuleTreeItem = DominoModuleTreeItem;
/**
 * The module list receieved from the langauge server
 */
var moduleTreeItems;
/**
 * The event emitter for the project browser tree.
 */
const dominoModuleTreeEventEmitter = new vscode_1.EventEmitter();
/**
 * Creates the tree item provider for the module browser tree.
 */
function createModuleTreeDataProvider() {
    return {
        getChildren: (element) => {
            // Undefined means return the root items.
            if (element === undefined) {
                return moduleTreeItems;
            }
            return element.getChildren();
        },
        getTreeItem: (element) => {
            return element.getTreeItem();
        },
        onDidChangeTreeData: dominoModuleTreeEventEmitter.event
    };
}
/**
 * The event emitter for the directory browser tree.
 */
const dominoDirectoryTreeEventEmitter = new vscode_1.EventEmitter();
/**
 * Creates the tree item provider for the directoy browser tree.
 */
function createDirectoryTreeDataProvider() {
    return {
        getChildren: (element) => {
            // Undefined means return the root items.
            if (element === undefined) {
                return directoryTreeItems.getChildren();
            }
            return element.getChildren();
        },
        getTreeItem: (element) => {
            return element.getTreeItem();
        },
        onDidChangeTreeData: dominoDirectoryTreeEventEmitter.event
    };
}
/**
 * Creates the BuildXL Project browser view
 * @param langClient The JSON-RPC language client, used to send requests to the language server
 * @param context The context the extennsion is running in, needed to register commands.
 */
function createDominoProjectBrowser(langClient, context) {
    // Read the configuration and see if we should do anything.
    const config = vscode_1.workspace.getConfiguration('dscript');
    vscode_1.commands.executeCommand('setContext', 'dscript.moduleBrowserEnabled', config.turnOnModuleBrowser);
    vscode_1.commands.executeCommand('setContext', 'dscript.solutionExplorerEnabled', config.turnOnSolutionExplorer);
    if (!config.turnOnModuleBrowser && !config.turnOnSolutionExplorer) {
        return;
    }
    // If we've already created ourselves, then just bail out.
    // This can happen if we get multiple calls because the
    // user is changing their configuration settings.
    if (createdProjectBrowser) {
        return;
    }
    createdProjectBrowser = true;
    languageClient = langClient;
    extensionContext = context;
    // Register the command to be able to open spec documents when they are clicked on.
    context.subscriptions.push(vscode_1.commands.registerCommand('dscript.openSpecData', (spec) => {
        var textDocument = vscode_1.workspace.openTextDocument(spec.uri()).then((document) => {
            vscode_1.window.showTextDocument(document);
        });
    }));
    // Register the add source file command.
    addSourceFileRequest_1.configureAddSourceFile(languageClient, context);
    // Register the add project command.
    addProjectRequest_1.configureAddProject(languageClient, context);
    // Register for the workspace loading event so we know when the workspace
    // has successfully been loaded so we can start filling out the project browser
    workspaceLoadingNotification_1.WorkspaceLoadingNotification.WorkspaceLoadingEvent.event((state) => {
        if (state == 2 /* Success */) {
            moduleTreeItems = [];
            modulePathToDominoModuleMap = new Map();
            // Send the add source notification to the language server
            addSourceFileConfigurationNotification_1.sendAddSourceFileConfigurationToLanguageServer(languageClient, context);
            languageClient.sendRequest(modulesForWorkspaceRequest_1.ModulesForWorkspaceRequest, { includeSpecialConfigurationModules: false }).then((m) => {
                m.modules.sort((a, b) => {
                    return a.name.localeCompare(b.name);
                });
                m.modules.forEach((mod, index) => {
                    const moduleConfigDirname = path.dirname(mod.configFilename);
                    // Update the project module browser
                    const dominoModuleTreeItem = new DominoModuleTreeItem(mod, dominoModuleTreeEventEmitter);
                    modulePathToDominoModuleMap.set(moduleConfigDirname.toLowerCase(), dominoModuleTreeItem);
                    moduleTreeItems.push(dominoModuleTreeItem);
                    // Update the project directory browser.
                    const moduleDirectoryItem = directoryTreeItems.getOrAddChild(moduleConfigDirname);
                    moduleDirectoryItem.addModule(mod);
                });
                dominoDirectoryTreeEventEmitter.fire();
                dominoModuleTreeEventEmitter.fire();
            });
        }
    });
    // Set up the DScript project module browser.
    vscode_1.window.registerTreeDataProvider("dscriptBuildScope", createModuleTreeDataProvider());
    // Set up the DScript project directory browser.
    vscode_1.window.registerTreeDataProvider("buildxlProjectDirectoryBrowser", createDirectoryTreeDataProvider());
    // Register for open document notifications so we can select the write module in the
    // tree view.
    vscode_1.workspace.onDidOpenTextDocument((document) => {
        // Note: Due to issue https://github.com/Microsoft/vscode/issues/30288 we cannot currently
        // do this.
        // I believe we would do something like this..
        // As the user opens different documents we would find the closest
        // module configuration file for the open document.
        // If we find it, we would select the document in the BuildXL project browser.
        // We should be able to optimize this loop.
        if (modulePathToDominoModuleMap) {
            let docDirName = path.dirname(document.fileName).toLowerCase();
            ;
            while (docDirName && docDirName.length > 0) {
                if (modulePathToDominoModuleMap.has(docDirName)) {
                    modulePathToDominoModuleMap.get(docDirName).expand();
                    break;
                }
                // When we walk all the way to the root, stop.
                const newDirName = path.dirname(docDirName).toLowerCase();
                if (newDirName === undefined || newDirName === docDirName) {
                    break;
                }
                docDirName = newDirName;
            }
        }
    });
}
exports.createDominoProjectBrowser = createDominoProjectBrowser;
//# sourceMappingURL=projectBrowser.js.map
