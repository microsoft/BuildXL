// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

'use strict';

// This must be the first statement otherwise modules might got loaded with
// the wrong locale.
import { TextEdit, Range,  QuickPickItem, SnippetString, Command, EventEmitter, TreeItem, TreeDataProvider, commands, window, env, workspace, ExtensionContext, Uri, TreeItemCollapsibleState, WorkspaceEdit} from 'vscode';
import * as nls from 'vscode-nls';
nls.config({ locale: env.language });

import { WorkspaceLoadingNotification } from '../notifications/workspaceLoadingNotification';
import { TextEdit as VSCodeRpcTextEdit, RequestType, LanguageClient, LanguageClientOptions, ServerOptions, TransportKind, NotificationType } from 'vscode-languageclient';
import * as path from 'path';
import * as fs from 'fs';

import { ModuleDescriptor, ModulesForWorkspaceParams, ModulesForWorkspaceRequest, ModulesForWorkspaceResult } from './modulesForWorkspaceRequest';
import { SpecDescriptor, RequestSpecsForModuleParams, SpecsForModulesRequest, SpecsFromModuleResult } from './specsForModuleParams';
import { sendAddSourceFileConfigurationToLanguageServer } from './addSourceFileConfigurationNotification';
import { configureAddSourceFile } from './addSourceFileRequest';
import { configureAddProject } from './addProjectRequest';
import * as assert from 'assert';

var languageClient : LanguageClient;
var extensionContext: ExtensionContext;
var createdProjectBrowser : boolean = false;

// A map from the BuildXL module configuration path to the tree item
// representing a BuildXL Module. Can eventually be used
// to reveal the BuildXL module an opened file is located in.
// This cannot be done yet due to bug: https://github.com/Microsoft/vscode/issues/30288
var modulePathToDominoModuleMap: Map<string, DominoModuleTreeItem>;    

/**
 * Represents a node in the project browser.
 */
interface DominoProjectBaseTreeItem {
    /**
     * Returns a a item to insert into the project browser tree.
     */
    getTreeItem(): TreeItem;

    /**
     * Returns the children for this node.
     */
    getChildren(): DominoProjectBaseTreeItem[];
}

/**
 * Represents a BuildXL spec file in the project browser tree.
 */
export class DominoSpecFileTreeItem implements DominoProjectBaseTreeItem {
    constructor(specDescriptor: SpecDescriptor, showShortName: boolean) {
        this._specDescriptor = specDescriptor;
        this._showShortName = showShortName;
    }

    // The spec descriptor describing this spec file returned by the language server.
    private _specDescriptor : SpecDescriptor;

    // Indicated whether to use the short-name for the label (used in the solution\directory view)
    private _showShortName: boolean;

    /**
     * Returns a URI for the spec file that can be opened in the text editor.
     */
    public uri() : Uri {
        // The files we get back from the language server need to have
        // the schema appened to them in order VSCode to understand them.
        return Uri.parse("file:///" + this._specDescriptor.fileName);
    }

    /**
     * Returns a "tree item" representing a BuildXL spec file for the project browser.
     */
    public getTreeItem() : TreeItem {
        return {
            command: {
                arguments: [this, undefined],
                title:  "Open",
                tooltip: "Open",
                command: 'dscript.openSpecData'
            },
            contextValue: "dscript.spec",
            label: this._showShortName? path.basename(this._specDescriptor.fileName) : this._specDescriptor.fileName,
            collapsibleState: TreeItemCollapsibleState.None
        };
    }

    /**
     * BuildXL spec files currently have no childern.
     */
    public getChildren() : DominoProjectBaseTreeItem[] {
        return [];
    }
}

/**
 * Used to represent a solution like view based on directory
 * hierachy.
 */
class DirectoryTreeItem implements DominoProjectBaseTreeItem {
    constructor(directoryName: string) {
        this._directoryName = directoryName;
        this._retrieveSpecs = true;
    }

    // The single directory name (path-atom) that this item represents
    private _directoryName: string;

    // Indicates whether we have asked the language server
    // for the specs for a module.
    private _retrieveSpecs : boolean;

    // The list of project specs in this directory
    private _projectSpecs: SpecDescriptor[];

    // The list of modules in this directory.
    private _modules: ModuleDescriptor[];

    // A map from a single directory name (path-atom)
    // to the DirectoryTreeItem that represents it.
    private _childDirectories: Map<string, DirectoryTreeItem>;

    // Creates or retrieves a DirectoryTreeItem that represents
    // the passed in single directory name (path-atom)
    protected getOrAddSingleChild(directoryName: string) : DirectoryTreeItem {
        
        assert.equal(directoryName.includes(path.sep), false);

        // If this is the first time through, allocate the children
        if (this._childDirectories === undefined) {
            this._childDirectories = new Map<string, DirectoryTreeItem>();
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
    public getOrAddChild(directoryName: string) : DirectoryTreeItem {
        const paths = directoryName.split(path.sep);
        let nextDirectoryTreeItem : DirectoryTreeItem = this;
        paths.forEach((singlePath, index) => {
            nextDirectoryTreeItem = nextDirectoryTreeItem.getOrAddSingleChild(singlePath);
        });

        return nextDirectoryTreeItem;
    }

    /**
     * Associates a module descriptor with this directory tree item.
     * @param mod The module to associate with this directory item.
     */
    public addModule(mod: ModuleDescriptor) {
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
    private addProjectSpec(projectSpec: SpecDescriptor) {
        if (this._projectSpecs === undefined) {
            this._projectSpecs = [];
        }

        this._projectSpecs.push(projectSpec);

        dominoDirectoryTreeEventEmitter.fire(this);
    }

    /**
     * Returns the tree item to the tree-view for this directory tree item.
     */
    public getTreeItem() : TreeItem {
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
                                (this._modules && this._modules.length !== 0) ) ? TreeItemCollapsibleState.Collapsed : TreeItemCollapsibleState.None
        };
    }

    /**
     * Returns the children known to this directory.
     */
    public getChildren() : DominoProjectBaseTreeItem[] {
        const directoryChildren: DominoProjectBaseTreeItem[] = [];

        // Add our child directories, sorted by name.
        if (this._childDirectories && this._childDirectories.size !== 0) {
            directoryChildren.push(...this._childDirectories.values());
            directoryChildren.sort((a, b) => {
                return (<DirectoryTreeItem>a)._directoryName.localeCompare((<DirectoryTreeItem>b)._directoryName);
            })
        }

        // Add our modules, sorted by module name.
        const moduleChildren: DominoProjectBaseTreeItem[] = [];
        if (this._modules && this._modules.length !== 0) {
            this._modules.forEach((mod) => {
                moduleChildren.push(new DominoModuleTreeItem(mod, dominoDirectoryTreeEventEmitter));
            });
            moduleChildren.sort((a, b) => {
                return (<DominoModuleTreeItem>a).descriptor().name.localeCompare((<DominoModuleTreeItem>b).descriptor().name);
            })
        }

        // Add our project specs.
        const specChildren: DominoProjectBaseTreeItem[] = [];

        // If we have not asked the language server for the specs, then do so know.
        if (this._modules && this._modules.length !== 0 && this._retrieveSpecs) {
            this._retrieveSpecs = false;
            this._modules.forEach((mod) => {
                languageClient.sendRequest(SpecsForModulesRequest, <RequestSpecsForModuleParams>{moduleDescriptor: mod}).then((specs : SpecsFromModuleResult) => {
                    specs.specs.forEach((spec, index) => {
                        const directoryTreeItem = directoryTreeItems.getOrAddChild(path.dirname(spec.fileName));
                        directoryTreeItem.addProjectSpec(spec);
                    });
                });
            });
        } else {
            if (this._projectSpecs && this._projectSpecs.length !== 0) {
                this._projectSpecs.forEach((spec) => {
                    specChildren.push(new DominoSpecFileTreeItem(spec, true));
                });
                specChildren.sort((a, b) => {
                    return (<DominoSpecFileTreeItem>a).uri().fsPath.localeCompare((<DominoSpecFileTreeItem>b).uri().fsPath);
                })
            }    
        }

        // Finally return the child array which is a combination of all three types.
        return [...directoryChildren, ...moduleChildren, ...specChildren];
    }
}

// The root directory tree item.
const directoryTreeItems :DirectoryTreeItem = new DirectoryTreeItem(undefined);

/**
 * Represents a BuildXL module in the project broweser tree.
 */
export class DominoModuleTreeItem implements DominoProjectBaseTreeItem {
    constructor(moduleDescriptor: ModuleDescriptor, eventEmiiter: EventEmitter<DominoProjectBaseTreeItem>) {
        this._moduleDescriptor = moduleDescriptor;
        this._collapseState = TreeItemCollapsibleState.Collapsed;
        this._eventEmiiter = eventEmiiter;
    }

    // The module description information returned from the language server.
    private _moduleDescriptor : ModuleDescriptor;
    private _children: DominoSpecFileTreeItem[];
    private _collapseState: TreeItemCollapsibleState;
    private _eventEmiiter: EventEmitter<DominoProjectBaseTreeItem>;

    /**
     * Returns a "tree item" representing a module  for the project browser.
     */
    public getTreeItem() : TreeItem {
        return {
            contextValue: "dscript.module",
            label: this._moduleDescriptor.name,
            collapsibleState: this._collapseState
        };
    }

    public expand() : void {
        // Note that this does not actually work due to 
        // https://github.com/Microsoft/vscode/issues/40179
        this._collapseState = TreeItemCollapsibleState.Expanded;
        this._eventEmiiter.fire(this);
    }

    public collapse() : void {
        this._collapseState = TreeItemCollapsibleState.Collapsed;
        this._eventEmiiter.fire(this);
    }

    /**
     * Returns the children of the BuildXL module, which, are currently spec files.
     */
    public getChildren() :DominoProjectBaseTreeItem[] {
        if (this._children === undefined) {
            this._children = [];
            // Capture the this pointer in a block variable so it can
            // be used after the sendRequest completes.
            let moduleData = this;
            languageClient.sendRequest(SpecsForModulesRequest, <RequestSpecsForModuleParams>{ moduleDescriptor: this._moduleDescriptor}).then((specs : SpecsFromModuleResult) => {
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
    public descriptor() : ModuleDescriptor {
        return this._moduleDescriptor;
    }
}

/**
 * The module list receieved from the langauge server
 */
var moduleTreeItems: DominoModuleTreeItem[];

/**
 * The event emitter for the project browser tree.
 */
const dominoModuleTreeEventEmitter = new EventEmitter<DominoProjectBaseTreeItem>();

/**
 * Creates the tree item provider for the module browser tree.
 */
function createModuleTreeDataProvider() : TreeDataProvider<DominoProjectBaseTreeItem> {
    return {
        getChildren: (element: DominoProjectBaseTreeItem): DominoProjectBaseTreeItem[] => {
            // Undefined means return the root items.
            if (element === undefined) {
                return moduleTreeItems;                
            }

            return element.getChildren();
        },

        getTreeItem: (element: DominoProjectBaseTreeItem): TreeItem => {
            return element.getTreeItem();
        },

        onDidChangeTreeData: dominoModuleTreeEventEmitter.event
    };
}

/**
 * The event emitter for the directory browser tree.
 */
const dominoDirectoryTreeEventEmitter = new EventEmitter<DominoProjectBaseTreeItem>();

/**
 * Creates the tree item provider for the directoy browser tree.
 */
function createDirectoryTreeDataProvider() : TreeDataProvider<DominoProjectBaseTreeItem> {
    return {
        getChildren: (element: DominoProjectBaseTreeItem): DominoProjectBaseTreeItem[] => {
            // Undefined means return the root items.
            if (element === undefined) {
                return directoryTreeItems.getChildren();                
            }

            return element.getChildren();
        },

        getTreeItem: (element: DominoProjectBaseTreeItem): TreeItem => {
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
export function createDominoProjectBrowser(langClient : LanguageClient, context: ExtensionContext) {

    // Read the configuration and see if we should do anything.
    const config = workspace.getConfiguration('dscript');
    commands.executeCommand('setContext', 'dscript.moduleBrowserEnabled', config.turnOnModuleBrowser);
    commands.executeCommand('setContext', 'dscript.solutionExplorerEnabled', config.turnOnSolutionExplorer);

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
    context.subscriptions.push(commands.registerCommand('dscript.openSpecData',
        (spec: DominoSpecFileTreeItem) => {
            var textDocument = workspace.openTextDocument(spec.uri()).then((document) => {
                window.showTextDocument(document);
            });
        }
    ));
    
    // Register the add source file command.
    configureAddSourceFile(languageClient, context);

    // Register the add project command.
    configureAddProject(languageClient, context);

    // Register for the workspace loading event so we know when the workspace
    // has successfully been loaded so we can start filling out the project browser
    WorkspaceLoadingNotification.WorkspaceLoadingEvent.event((state) => {
        if (state == WorkspaceLoadingNotification.WorkspaceLoadingState.Success) {
            moduleTreeItems = [];
            modulePathToDominoModuleMap = new Map<string, DominoModuleTreeItem>();

            // Send the add source notification to the language server
            sendAddSourceFileConfigurationToLanguageServer(languageClient, context);

            languageClient.sendRequest(ModulesForWorkspaceRequest, <ModulesForWorkspaceParams>{includeSpecialConfigurationModules: false }).then((m : ModulesForWorkspaceResult) => {
                m.modules.sort((a,b) => {
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
            })    
        }
    });

    // Set up the BuildXL DScript project module browser.
    window.registerTreeDataProvider("dscriptBuildScope", createModuleTreeDataProvider());

    // Set up the BuildXL DScript project directory browser.
    window.registerTreeDataProvider("buildxlProjectDirectoryBrowser", createDirectoryTreeDataProvider());

    // Register for open document notifications so we can select the write module in the
    // tree view.
    workspace.onDidOpenTextDocument((document) => {
        // Note: Due to issue https://github.com/Microsoft/vscode/issues/30288 we cannot currently
        // do this.

        // I believe we would do something like this..
        // As the user opens different documents we would find the closest
        // module configuration file for the open document.
        // If we find it, we would select the document in the BuildXL project browser.
        // We should be able to optimize this loop.
        if (modulePathToDominoModuleMap) {
            let docDirName = path.dirname(document.fileName).toLowerCase();;
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
