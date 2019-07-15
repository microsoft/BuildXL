// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Tool, Transformer} from "Sdk.Transformers";

import {Shared, PlatformDependentQualifier} from "Sdk.Native.Shared";

import * as Cl   from "Sdk.Native.Tools.Cl";
import * as Lib  from "Sdk.Native.Tools.Lib";
import * as Midl from "Sdk.Native.Tools.Midl";

export declare const qualifier: PlatformDependentQualifier;

export const defaultLinkArguments = <Arguments>{
    sources: [],
    resources: [],
    libraries: [],
    exports: [],
    ignoreWarnings: [],
    driverType: DriverTypes.none,
    mergeSections: Map.empty<string, string>(),
    additionalLibraryDirectories: [],
    midlCommandLineOptions: [],
    functionPaddingMinimumSpace: -1,
    includeBindableImportAddressTableInImage: true,
    appContainer: false,
    clrImageType: ClrImageType.unspecified,
    clrUnmanagedCodeCheck: false,
    ignoreIdlAttributes: false,
    requiredSignatureCheck: false,
    setAdministratorPermissions: false,
    useFrameworkAssembly: true,
    noDllEntryPoint: false,
    produceProfiler: false,
    verbose: Verbose.disabled,
    windowsMetadata: WindowsMetadata.disabled,
    suppressStartupBanner: true,
    enablePdbRpc: true,
    enableVCFeature: true,
    enableOptimizedDebugInformation: true,
    enableOptimizedDebugInformationPlus: true,
    enableTypeProp: true,
    enableSpecDevirt: true,
    enableVectorization: true,
};

/**
 * Runner for the tool:LINK.EXE
 *
 * It links Common Object File Format (COFF) object files and libraries to create an executable (.exe) file or a dynamic-link library (DLL).
 */
@@Tool.runner("link.exe")
@@public
export function evaluate(args: Arguments): Result {
    Contract.requires(args.outputFile !== undefined, "output file must not be undefined");

    Contract.assert(!(args.linkTimeCodeGeneration === LinkTimeCodeGenerationOption.optimize ||
                            args.linkTimeCodeGeneration === LinkTimeCodeGenerationOption.update), "PGD...");

    args = defaultLinkArguments.override<Arguments>(args);

    let outDir = Context.getNewOutputDirectory("link");
    let tempDir = Context.getTempDirectory("tmplink");

    let outFile = outDir.combine(args.outputFile);

    let midlProxyOutFiles = args.midlCommandLineOptions.mapMany(m => m.proxyFile      ? [outDir.combine(m.proxyFile)]   : []);
    let midlHeaderOutFiles = args.midlCommandLineOptions.mapMany(m => m.headerFile    ? [outDir.combine(m.headerFile)]  : []);
    let midlIidOutFiles = args.midlCommandLineOptions.mapMany(m => m.iidFile          ? [outDir.combine(m.iidFile)]     : []);
    let midlDllOutFiles = args.midlCommandLineOptions.mapMany(m => m.dllDataFile      ? [outDir.combine(m.dllDataFile)] : []);
    let midlIncludeDirs = args.midlCommandLineOptions.mapMany(m => m.includeDirectory ? [m.includeDirectory] : []);

    let idlOutFile = args.idlFile && outDir.combine(args.idlFile);
    let tlbOutFile = args.tlbFile && outDir.combine(args.tlbFile);
    let winMetaOutFile = args.windowsMetadataFile && outDir.combine(args.windowsMetadataFile);
    let mapOutFile = args.mapFile && outDir.combine(args.mapFile);

    // The linker will generate a PDB if /PROFILE is specified, even if /DEBUG is not specified.
    let generatePdb = args.generateDebugInformation || args.produceProfiler;
    let pdbOutFile = generatePdb
        ? (args.programDatabaseFile ? outDir.combine(args.programDatabaseFile) : outFile.changeExtension(".pdb"))
        : undefined;
    // The linker will only generate a stripped PDB if it also generates a regular PDB.
    let strippedPdbOutFile = (generatePdb && args.createStrippedPDB)
        ? outFile.changeExtension(".stripped.pdb")
        : undefined;

    let manifestOutFile = (args.manifestMode === ManifestMode.sideBySide)
        ? outFile.changeExtension(outFile.name.extension + ".manifest")
        : undefined;
    let pgdOutFile = (args.linkTimeCodeGeneration === LinkTimeCodeGenerationOption.instrument)
        ? (args.outputProfileGuidedInstrumentationName ? outDir.combine(args.outputProfileGuidedInstrumentationName) : outFile.changeExtension(".pgd"))
        : undefined;
    let impLibOutFile = ((args.projectType === LinkProjectType.dynamicLinkLibrary) && (args.importLibrary === undefined)) ?
        ( args.importLibraryName ? outDir.combine(args.importLibraryName) : outFile.changeExtension(".lib") ) :
        undefined;
    let expOutFile = impLibOutFile ? impLibOutFile.changeExtension(".exp") : undefined;

    let exportStrings = (args.projectType === LinkProjectType.executable || args.projectType === LinkProjectType.dynamicLinkLibrary) && !args.importLibrary
        ? (args.exports || []).map(e => 
            e.name + (e.ordinal ? ",@" + e.ordinal.toString() : "") +
                    (e.hideName ? ",NONAME" : "") + 
                    (e.symbolType === ExportSymbolType.dataType ? ",DATA" : ""))
        : [];

    let cmdArgs: Argument[] = [
        Cmd.options("/DELAYLOAD:", args.delayLoadDlls),

        Cmd.option("@", Artifact.input(args.responseFile)),

        Cmd.startUsingResponseFile(false),

        ...optionsToCmdLineArgs(args),

        // output artifacts
        Cmd.option("/OUT:", Artifact.output(outFile)),
        Cmd.option("/IDLOUT:", Artifact.output(idlOutFile)),
        Cmd.option("/TLBOUT:", Artifact.output(tlbOutFile)),
        Cmd.option("/WINMDFILE:", Artifact.output(winMetaOutFile)),
        Cmd.options("/MIDL:", args.midlCommandLineOptions.map(m => m.midlArgument)),

        Cmd.flag("/MAP", args.createMap),
        Cmd.option("/MAP:", Artifact.output(mapOutFile)),
        Cmd.option("/PDB:", Artifact.output(pdbOutFile)),
        Cmd.option("/PDBSTRIPPED:", Artifact.output(strippedPdbOutFile)),
        Cmd.option("/IMPLIB:", Artifact.output(impLibOutFile)),
        Cmd.option("/PGD:", Artifact.output(pgdOutFile)),
        Cmd.options("/EXPORT:", exportStrings),

        Cmd.args(Artifact.outputs(midlProxyOutFiles)),
        Cmd.args(Artifact.outputs(midlHeaderOutFiles)),
        Cmd.args(Artifact.outputs(midlIidOutFiles)),
        Cmd.args(Artifact.outputs(midlDllOutFiles)),

        Cmd.args(Artifact.inputs(midlIncludeDirs)),
        // inputs
        ...getInputCmdLineArgs(args),
    ];

    let libSearchPaths = args.libraries.mapDefined(item => typeof item !== "File" ? item as StaticDirectory : undefined);
    let msPdbSrvEndPointUniqueId = Context.getMount("ObjectRoot").path.getRelative(outDir.path).toPathAtoms().map(p => p.toString()).join("|");

    let result = Transformer.execute({
        tool: args.tool || importFrom("VisualCpp").linkTool,
        workingDirectory: outDir,
        tags: args.tags,
        arguments: cmdArgs,
        dependencies: libSearchPaths,
        implicitOutputs: compact([
            manifestOutFile,
            iff(idlOutFile && !tlbOutFile, outFile.changeExtension(".tlb")),
            iff(args.createMap,            outFile.changeExtension(".map")),
            iff(expOutFile !== undefined,  expOutFile),
        ]),
        environmentVariables: [
            {
                name: "LIB",
                value: libSearchPaths.mapDefined(d => d.path),
                separator: ";"
            },
            {
                // The MsVc tools spawn mspdbsvr.exe child processes. They tend to be reused by multiple link or cl invocations.
                // This process aids in multi threaded compilation and caching of some information. Unfortunately the problem is that 
                // this process performs IO operations on behalf of the parent process. Therefore the sandbox safety cannot be guarnateed anymore
                // because it escapes the safety of the process tree that our sandbox encapsulates. This environment variable allows
                // us to control the creation of a single instance per invicatoin by passing a unique id for this invocation. The easiest
                // stable way to get a unique id is to use the unique output directoyr created.
                name: "_MSPDBSRV_ENDPOINT_",
                value: msPdbSrvEndPointUniqueId,
            }
        ],
        allowedSurvivingChildProcessNames: [
            "mspdbsrv.exe",
            "VCTIP.exe",
            "conhost.exe",
        ]
    });

    let midlOutput = <Midl.Result>{
        header: midlHeaderOutFiles.length > 0 
            ? Transformer.sealDirectory({
                root: d`${midlHeaderOutFiles[0].parent}`, 
                files: midlHeaderOutFiles.map(File.fromPath)
            }) 
            : undefined,
        proxy: result.getOutputFile(firstIfExists(midlProxyOutFiles)),
        dllData: result.getOutputFile(firstIfExists(midlDllOutFiles)),
        iid: result.getOutputFile(firstIfExists(midlIidOutFiles)),
        typeLibrary: result.getOutputFile(tlbOutFile)
    };

    return <Result>{
        binaryFile: result.getOutputFile(outFile),
        debugFile: result.getOutputFile(pdbOutFile),
        strippedDebugFile: result.getOutputFile(strippedPdbOutFile),
        importLibrary: args.importLibrary
            ? args.importLibrary.binaryFile
            : (args.projectType === LinkProjectType.dynamicLinkLibrary ? result.getOutputFile(impLibOutFile) : undefined),
        profileGuidedOptimizationDataFile: result.getOutputFile(pgdOutFile),
        mapFile: result.getOutputFile(mapOutFile),
        windowsMetadataFile: result.getOutputFile(winMetaOutFile),
        nameIdlOutput: result.getOutputFile(idlOutFile),
        midlOutput: midlOutput
    };
}

function getInputCmdLineArgs(args: Arguments): Argument[] {
    Contract.assert(args.assemblyResourceFileEntry === undefined, "unimplemented assemblyResourceFileEntry because we don't have suffixes");
    Contract.assert(args.imageBaseFileEntry === undefined, "unimplemented imageBaseFileEntry because we don't have suffixes");
    Contract.assert(!(args.linkTimeCodeGeneration === LinkTimeCodeGenerationOption.optimize ||
                            args.linkTimeCodeGeneration === LinkTimeCodeGenerationOption.update), "pgd input files not implemented");
    
    let libraries = args.libraries.mapDefined(item => typeof item === "File" ? item as File : undefined);

    return [
        Cmd.option("/ORDER:@", Artifact.input(args.order)),
        Cmd.option("/STUB:", Artifact.input(args.stubProgram)),
        Cmd.option("/DEF:", Artifact.input(args.moduleDefinitionFile),
            args.projectType === LinkProjectType.dynamicLinkLibrary && args.importLibrary === undefined),
        Cmd.args(Artifact.inputs(args.sources.mapMany(s => s.binary && s.binary.object ? [s.binary.object] : []))),
        Cmd.args(Artifact.inputs(args.resources)),
        Cmd.args(Artifact.inputs(libraries)),
        Cmd.argument(args.projectType === LinkProjectType.dynamicLinkLibrary ? Artifact.input(args.importLibrary && args.importLibrary.exportFile) : undefined)
    ];
}

function memorySizesToCmdLineFlag(reserveSize: number, commitSize: number): string {
    Contract.requires((reserveSize !== undefined) || (commitSize === undefined),
        "commitSize specified but not reserveSize");
    
    if (reserveSize === undefined) {
        return undefined;
    }
    else if (commitSize === undefined) {
        return reserveSize.toString();
    }
    else {
        return reserveSize.toString() + "," + commitSize.toString();
    }
}

function optionsToCmdLineArgs(args: Arguments): Argument[] {
    Contract.requires(!(args.profileGuidedOptimizationData && args.profileGuidedOptimizationData.length > 0 &&
                            args.linkTimeCodeGeneration !== LinkTimeCodeGenerationOption.optimize &&
                            args.linkTimeCodeGeneration !== LinkTimeCodeGenerationOption.update),
                            "profileGuidedOptimizationData specified but not linkTimeCodeGeneration");
    Contract.requires(!args.outputProfileGuidedInstrumentationName || args.linkTimeCodeGeneration === LinkTimeCodeGenerationOption.instrument,
                            "profileGuidedInstrumentation specified without linkTimeCodeGeneration option set to 'instrument'");
    Contract.requires((args.stackReserveSize !== undefined) || (args.stackCommitSize === undefined),
        "stackCommitSize specified but not stackReserveSize");
    Contract.requires((args.heapReserveSize !== undefined) || (args.heapCommitSize === undefined),
        "heapCommitSize specified but not heapReserveSize");

    // TODO: unimplemented stuff
    Contract.assert(!(args.imageBase !== undefined && args.imageBase !== -1), "'imageBase' option not implemented yet"); //TODO

    return [
        // include
        Cmd.options("/INCLUDE:", args.forcedSymbolReferences),
        Cmd.options("/LIBPATH:", Artifact.inputs(args.additionalLibraryDirectories)),
        Cmd.flag("/NODEFAULTLIB", args.ignoreAllDefaultLibraries),
        Cmd.options("/NODEFAULTLIB:", args.ignoreSpecificLibraries),

        // target
        Cmd.option("/MACHINE:", Shared.enumConstToUpperCase(args.targetMachine)),
        Cmd.option("/OSVERSION:", args.osVersion),
        Cmd.flag("/KERNEL", args.kernelMode),

        // errors and warnings
        Cmd.option("/IGNORE:", args.ignoreWarnings && Cmd.join(",", args.ignoreWarnings)),
        Cmd.flag("/WX", args.treatWarningAsError),
        Cmd.option("/ERRORREPORT:", Shared.enumConstToUpperCase(args.errorReportSetting)),

        // optimization
        Cmd.option("/OPT:", Shared.optBoolToFlags(args.optimizeReferences, "REF", "NOREF")),
        Cmd.option("/OPT:", Shared.optBoolToFlags(args.enableCOMDATFolding, "ICF", "NOICF")),
        Cmd.flag("/PDBCOMPRESS", args.compressPdb),

        // debug
        Cmd.option("/ASSEMBLYDEBUG", Shared.optBoolToFlags(args.assemblyDebug, "", ":DISABLE")),
        Cmd.options("/NATVIS:", Artifact.inputs(args.nativeVisualizerFiles)),
        Cmd.flag("/Verbose", args.verbose === Verbose.enabled),
        Cmd.option("/Verbose:", Shared.enumConstToUpperCase(args.verbose, Verbose.enabled, Verbose.disabled)),

        // image property
        Cmd.option("/SAFESEH", Shared.optBoolToFlags(args.imageHasSafeExceptionHandlers, "", ":NO")),
        Cmd.option("/DYNAMICBASE", Shared.optBoolToFlags(args.randomizedBaseAddress, "", ":NO")),
        Cmd.option("/CLRIMAGETYPE", Shared.enumConstToUpperCase(args.clrImageType, ClrImageType.unspecified)),
        Cmd.option("/NXCOMPAT", Shared.optBoolToFlags(args.compatibleWithDataExecutionPrevention, "", ":NO")),
        Cmd.options("/MERGE:", args.mergeSections.forEach(kvp => kvp[0] + "=" + kvp[1])),
        Cmd.flag("/DELAY:NOBIND", args.includeBindableImportAddressTableInImage === false),
        //TODO: args.imageBase and args.imageSize

        // security
        Cmd.flag("/INTEGRITYCHECK", args.requiredSignatureCheck),
        Cmd.flag("/MANIFESTUAC", args.manifestUAC === true),
        Cmd.option("/MANIFESTUAC:level=", args.uacLevel === undefined
                                            ? undefined
                                            : args.uacLevel.toString()),
        Cmd.option("/MANIFESTUAC:uiAccess=", args.uacUiAccess === undefined
                                                ? undefined
                                                : args.uacUiAccess.toString()),

        // miscellaneous
        Cmd.flag("/NOLOGO", args.suppressStartupBanner),
        Cmd.options("/SECTION:", mapSectionsToStrings(args.sections)),
        Cmd.argument("/INCREMENTAL:NO"),
        Cmd.flag("/AppContainer", args.appContainer),
        Cmd.flag("/IGNOREIDL", args.ignoreIdlAttributes),
        Cmd.option("/GUARD", Shared.optBoolToFlags(args.cfGuard, ":CF", ":NO")),
        Cmd.flag("/NOOPTIDATA", args.noOptiData),
        Cmd.flag("/RELEASE", args.setChecksum),
        Cmd.option("/VERSION:", args.version),
        Cmd.flag("/FUNCTIONPADMIN", args.functionPaddingMinimumSpace === 0),
        Cmd.option("/FUNCTIONPADMIN:", args.functionPaddingMinimumSpace, args.functionPaddingMinimumSpace > 0),
        Cmd.option("/ENTRY:", args.entryPoint),
        Cmd.option("/SUBSYSTEM:", subsystemToCmdLineFlag(args.subsystem)),
        Cmd.flag("/DELAY:UNLOAD", args.supportExplicitDllUnloading),
        Cmd.option("/ALLOWISOLATION", Shared.optBoolToFlags(args.allowIsolation, "", ":NO")),
        Cmd.option("/ALLOWBIND", Shared.optBoolToFlags(args.allowBind, "", ":NO")),
        Cmd.option("/CLRSUPPORTLASTERROR", Shared.mapEnumConst(args.clrSupportLastError,
            [ClrSupportLastError.enabled, ""],
            [ClrSupportLastError.disabled, ":NO"],
            [ClrSupportLastError.systemDLL, ":SYSTEMDLL"])),
        Cmd.option("/CLRTHREADATTRIBUTE:", Shared.enumConstToUpperCase(args.clrThreadAttribute)),
        Cmd.option("/HEAP:", memorySizesToCmdLineFlag(args.heapReserveSize, args.heapCommitSize)),
        Cmd.option("/STACK:", memorySizesToCmdLineFlag(args.stackReserveSize, args.stackCommitSize)),
        Cmd.option("/TSAWARE", Shared.optBoolToFlags(args.terminalServerAware, "", ":NO")),

        // signing
        Cmd.option("/DELAYSIGN", Shared.optBoolToFlags(args.delaySign, "", ":NO")),
        Cmd.option("/KEYFILE:", Artifact.input(args.keyFile)),
        Cmd.option("/KEYCONTAINER:", Artifact.input(args.keyContainer)),

        // link
        Cmd.flag("/NOENTRY", args.noDllEntryPoint),
        Cmd.flag("/CLRUNMANAGEDCODECHECK", args.clrUnmanagedCodeCheck),
        Cmd.option("/TLBID:", args.typeLibraryResourceId),
        Cmd.flag("/FORCE:MULTIPLE", args.allowMultipleDefinitions),
        Cmd.flag("/FORCE:UNRESOLVED", args.allowUnresolvedReferences),
        Cmd.flag("/FORCE:PGOREPRO", args.allowMismatchedPgoData),
        Cmd.flag("/DLL", args.projectType === LinkProjectType.dynamicLinkLibrary),
        Cmd.option("/SWAPRUN:", Shared.enumConstToUpperCase(args.swapLocation)),
        Cmd.flag("/LARGEADDRESSAWARE", args.largeAddressAware),

        // output information
        (args.driverType !== DriverTypes.none ?
            Cmd.option("/DRIVER", 
                (args.driverType === DriverTypes.driver ? 
                    "" :
                    ":" + 
                    (Shared.mapEnumFlags(args.driverType,
                        [DriverTypes.wdm, "WDM"],
                        [DriverTypes.uniprocessor, "UPONLY"]) || []).join(","))) :
            undefined),
        Cmd.flag("/PROFILE", args.produceProfiler),
        Cmd.flag("/NOASSEMBLY", args.useFrameworkAssembly === false),
        Cmd.option("/BASE:", args.baseAddress),
        Cmd.option("/FIXED", Shared.optBoolToFlags(args.fixedBaseAddress, "", ":NO")),
        Cmd.argument(Shared.mapEnumConst(args.manifestMode,
            [ManifestMode.disabled, "/MANIFEST:NO"],
            [ManifestMode.embed, "/MANIFEST:EMBED"],
            [ManifestMode.sideBySide, "/MANIFEST"])),
        Cmd.option("/WINMD", Shared.mapEnumConst(args.windowsMetadata,
            [WindowsMetadata.enabled, ""],
            [WindowsMetadata.noGeneration, ":NO"],
            [WindowsMetadata.only, ":ONLY"])),
        Cmd.option("/FILEALIGN:", args.fileAlign && args.fileAlign !== 0 ? "0x" + args.fileAlign.toString(16) : undefined),
        Cmd.option("/PDBALTPATH:", args.programDatabaseAlternatePath),
        Cmd.option("/ASSEMBLYDEBUG", Shared.optBoolToFlags(args.allowIsolation, "", ":DISABLE")),

        // instrumentation
        Cmd.option("/LTCG", Shared.mapEnumConst(args.linkTimeCodeGeneration,
            [LinkTimeCodeGenerationOption.instrument, ":PGINSTRUMENT"],
            [LinkTimeCodeGenerationOption.optimize, ":PGOPTIMIZE"],
            [LinkTimeCodeGenerationOption.update, ":PGUPDATE"])),
        Cmd.flag("/LTCG", args.linkTimeCodeGeneration === LinkTimeCodeGenerationOption.use),
        Cmd.option("/LTCG", Shared.optBoolToFlags(args.linkStatus, ":STATUS", ":NOSTATUS")),
        Cmd.flag("/DEBUG", args.generateDebugInformation),

        // undocumented
        iff(args.returnFlowGuard !== ReturnFlowGuardType.disabled,
            Cmd.option("/GUARD:", Shared.mapEnumConst(args.returnFlowGuard,
                [ReturnFlowGuardType.enabled, "rf"],
                [ReturnFlowGuardType.instrumentation,   "rfinstr"],
                [ReturnFlowGuardType.strict,  "rfstrict"]))),
        Cmd.flag("/pdbrpc:no", args.enablePdbRpc === false),
        Cmd.flag("/novcfeature", args.enableVCFeature === false),
        Cmd.flag("/d2:-Zi", args.enableOptimizedDebugInformation === false),
        Cmd.flag("/d2:-Zi+", args.enableOptimizedDebugInformationPlus === false),
        Cmd.flag("/d2:-TypeProp-", args.enableTypeProp === false),
        Cmd.flag("/d2:-SpecDevirt-", args.enableSpecDevirt === false),
        Cmd.flag("/d2:-Qvec-", args.enableVectorization === false),
        Cmd.flag("/d2:-guardcfgfuncptr", args.callGuardCheckFunctionThruControlFlowGuardFunctionPointer),
        Cmd.flag("/d2guardcfgdispatch", args.enableDispatchModeForControlFlowGuard),
        Cmd.flag("/d2guardlongjmp", args.enableGuardForLongJump),
        Cmd.option("/PAGESIZE:", args.pageSize, args.pageSize !== 0),
        (args.debugTypes !== DebugTypes.none ? 
            Cmd.option("/DEBUGTYPE:", (Shared.mapEnumFlags(args.debugTypes,
                [DebugTypes.microsoftFormat, "cv"],
                [DebugTypes.fixup, "fixup"],
                [DebugTypes.pdata, "pdata"]) || []).join(",")) :
            undefined),

        // alternative names
        Cmd.options("/ALTERNATENAME:", (args.alternateNames || []).map(kvp => kvp.name + "=" + kvp.value))
    ];
}

function compact<T>(arr: T[]): T[]        { return arr.filter(a => a !== undefined); }
function iff<T>(cond: boolean, val: T): T { return cond ? val : undefined; }

function firstIfExists<T>(arr: T[]): T {
    return (arr === undefined || arr.length === 0) ? undefined : arr[0];
}

function subsystemToCmdLineFlag(subsystem: Shared.Subsystem): string {
    if (subsystem === undefined) return undefined;
    let majorMinor = (subsystem.major !== undefined || subsystem.minor !== undefined)
        ? "," + subsystem.major + "." + Shared.prepend("0", 2, subsystem.minor.toString())
        : "";
    return subsystemTypeToStr(subsystem.subsystemType) + majorMinor;
}

function subsystemTypeToStr(v: Shared.SubsystemType): string {
    if (v === undefined) return undefined;
    switch (v) {
        case Shared.SubsystemType.bootApplication:      return "BOOT_APPLICATION";
        case Shared.SubsystemType.console:              return "CONSOLE";
        case Shared.SubsystemType.efiApplication:       return "EFI_APPLICATION";
        case Shared.SubsystemType.efiBootServiceDriver: return "EFI_BOOT_SERVICE_DRIVE";
        case Shared.SubsystemType.efiRom:               return "EFI_ROM";
        case Shared.SubsystemType.efiRuntimeDriver:     return "EFI_RUNTIME_DRIVE";
        case Shared.SubsystemType.native:               return "NATIVE";
        case Shared.SubsystemType.posix:                return "POSIX";
        case Shared.SubsystemType.windows:              return "WINDOWS";
        case Shared.SubsystemType.windowsCe:            return "WINDOWSCE";
    }
}

function mapSectionsToStrings(sections: Section[]): string[] {
    if (sections === undefined) return undefined;
    return sections.map(section => {
        let sectionAlignment = (section.alignment && section.alignment >= 0)
            ? ",/ALIGN=" + section.alignment.toString()
            : "";
        return section.name + "," + sectionAttributesToStr(section.attributes) + sectionAlignment;
    });
}

function getSectionAttributeStr(attributeChar: string, value: boolean): string {
    if (value === undefined) {
        return "";
    }
    else if (value) {
        return attributeChar;
    }
    else {
        return "!" + attributeChar;
    }
}

function sectionAttributesToStr(attributes: SectionAttributes): string {
    return getSectionAttributeStr("D", attributes.discardable) +
        getSectionAttributeStr("E", attributes.execute) +
        getSectionAttributeStr("K", attributes.cacheable) +
        getSectionAttributeStr("P", attributes.pageable) +
        getSectionAttributeStr("R", attributes.read) +
        getSectionAttributeStr("S", attributes.shared) +
        getSectionAttributeStr("W", attributes.write);
}

/**
    * Used to embed a resource in an assembly
    */
@@public
export interface AssemblyResourceFileEntry {
    /** The managed resource you want embed in this assembly */
    file: File;

    /** The name for the resource. */
    name?: string;

    /** Set Visibility of file to private */
    privateFileVisibility: boolean;
}

/**
    * Specifies the Type of CLR Image
    */
@@public
export const enum ClrImageType {
    /** Type:IJW */
    @@Tool.option("/CLRIMAGETYPE:IJW")
    ijw,
    /** Type:PURE */
    @@Tool.option("/CLRIMAGETYPE:PURE")
    pure,
    /** Type:SAFE */
    @@Tool.option("/CLRIMAGETYPE:SAFE")
    safe,
    /** Type:SAFE32BITPREFERRED */
    @@Tool.option("/CLRIMAGETYPE:SAFE32BITPREFERRED")
    safe32BitPreferred,
    /** Use default Image Types. */
    unspecified
}

/**
    * Specifies which error codes from P/Invoke calls are preserved (all, none or only
    * those coming from system DLLs).
    */
@@public
export const enum ClrSupportLastError {
    @@Tool.option("/CLRSUPPORTLASTERROR")
    enabled,
    @@Tool.option("/CLRSUPPORTLASTERROR:NO")
    disabled,
    @@Tool.option("/CLRSUPPORTLASTERROR:SYSTEMDLL")
    systemDLL
}

/**
    * Specifies the threading attribute for the entry point of a CLR program.
    */
@@public
export const enum ClrThreadAttribute {
    @@Tool.option("/CLRTHREADATTRIBUTE:NONE")
    none,
    @@Tool.option("/CLRTHREADATTRIBUTE:MTA")
    mta,
    @@Tool.option("/CLRTHREADATTRIBUTE:STA")
    sta
}

/**
    * DebugType specification to pass to link.exe
    */
@@Tool.option("/DEBUGTYPE", { supportsMultipleValues: true, valueSeparator: "," })
@@public
export const enum DebugTypes {
    /** /debugtype will not be set */
    none = 0x00,
    /** This option creates Microsoft Format debugging information. */
    @@Tool.option("/DEBUGTYPE:cv")
    microsoftFormat = 0x01,
    @@Tool.option("/DEBUGTYPE:fixup")
    fixup = 0x02,
    @@Tool.option("/DEBUGTYPE:pdata")
    pdata = 0x04
}

/**
    * DriverType specification to pass to link.exe
    */
@@Tool.option("/DRIVER", { optionSeparationMode: Tool.OptionSeparationMode.supported, supportsMultipleValues: true, valueSeparator: ",", optionSeparator: ":"})
@@public
export const enum DriverTypes {
    /** The target is not a driver. */
    none,
    /** The target is a driver. */
    @@Tool.option("/DRIVER")
    driver,
    /** The target is a WDM driver. */
    @@Tool.option("/DRIVER:WDM")
    wdm,
    /** The target is a driver for uniprocessor systems only. */
    @@Tool.option("/DRIVER:UPONLY")
    uniprocessor
}

/**
    * Explicit export specification for a function or a data item.
    */
@@public
export interface ExportSymbol {
    /** If Ordinal is a value in the range of 1 through 65,535 it is possible to exclude the string name in the export table. */
    hideName?: boolean;

    /**
     * The entry name to export.
     * This becomes the public name of the function or data item in the import library.
     */
    name: string;

    /**
     * The index into the exports table in the range of 1 through 65,535.
     * A value of zero means that no ordinal value is explicitly set.  It is possible to export a symbol with both an name and a value, or exclusively by ordinal.
     */
    ordinal?: number;

    /**
     * If the Name references a data item, then this must be ExportSymbolType.Data.
     * The data item in the client program must be declared using extern __declspec(dllimport).
     */
    symbolType?: ExportSymbolType;
}

/**
    * When exporting a symbol via /EXPORT, it is possible to specify a function or an exported data item.
    */
@@public
export const enum ExportSymbolType {
    /** The export symbol is a data item. */
    dataType,
    /** The export symbol is a function. */
    functionType
}

/**
    * An entry in a file specifying image base addresses and sizes
    */
@@public
export interface ImageBaseFileEntry {
    /** The file containing the list of image base addresses and sizes */
    file: File;

    /** An identifier for an individual entry in the file */
    key: string;
}

/**
    * Arguments for running Link transformer
    */
//@@toolOptionDefaults({ optionSeparationMode: OptionSeparationMode.required, optionSeparator: ":" })
@@public
export interface Arguments extends Transformer.RunnerArguments {
    /** Specifies a path that the linker will search. */
    @@Tool.option("/LIBPATH")
    additionalLibraryDirectories?: StaticDirectory[];

    /** If false, indicates to Bind.exe that the linked image should not be bound. */
    @@Tool.option("/ALLOWBIND[|:NO]")
    allowBind?: boolean;

    /** If true, causes the operating system to do manifest lookups and loads.
     *  If false, indicates that DLLs are loaded as if there was no manifest.
     *  Default is true. */
    @@Tool.option("/ALLOWISOLATION[|:NO]")
    allowIsolation?: boolean;

    /** Link even if profile-guided optimization data doesn't match the objects being linked. */
    @@Tool.option("/FORCE:PGOREPRO")
    allowMismatchedPgoData?: boolean;

    /** Link even if there are multiple definitions of the same symbol. */
    @@Tool.option("/FORCE:MULTIPLE")
    allowMultipleDefinitions?: boolean;

    /** Link even if there are unresolved symbol references. */
    @@Tool.option("/FORCE:UNRESOLVED")
    allowUnresolvedReferences?: boolean;

    /** Specifies a list of one or more alternative names used by the linker. */
    @@Tool.option("/ALTERNATENAME")
    alternateNames?: Shared.AlternateName[];

    /** Indicates whether the app must be run in the appcontainer process-isolation environment */
    @@Tool.option("/APPCONTAINER[:NO]")
    appContainer?: boolean;

    /** If true, emits the DebuggableAttribute attribute together with debug information
     *  tracking and disables JIT optimizations.
     *  If false, emits the DebuggableAttribute attribute but disables debug information
     *  tracking and enables JIT optimizations.
     *  Default is false. */
    @@Tool.option("/ASSEMBLYDEBUG[|:DISABLE]")
    assemblyDebug?: boolean;

    /** Used to embed a resource in an assembly */
    @@Tool.option("/ASSEMBLYRESOURCE")
    assemblyResourceFileEntry?: AssemblyResourceFileEntry;

    /** Sets a base address for the program or DLL being built. Specify {address[,size] | @filename,key}. */
    @@Tool.option("/BASE:")
    baseAddress?: string;

    /**
     * Call guard check function through CFG function pointer instead of calling the guard check function.
     * Warning: this is an internal-use, undocumented option.  Use at your own risk.
     */
    @@Tool.option("/d2:-guardcfgfuncptr")
    callGuardCheckFunctionThruControlFlowGuardFunctionPointer?: boolean;

    /** Specifies support for Control Flow Guard checks in the executable image. */
    @@Tool.option("/GUARD:[CF|NO]")
    cfGuard?: boolean;

    /** Specifies Type of CLR Image */
    @@Tool.option("/CLRIMAGETYPE")
    clrImageType?: ClrImageType;

    /**
     * Specifies which error codes from P/Invoke calls are preserved (all, none or only
     * those coming from system DLLs).
     */
    clrSupportLastError?: ClrSupportLastError;

    /**
    * Specifies the threading attribute for the entry point of a CLR program.
    */
    clrThreadAttribute?: ClrThreadAttribute;

    /** Specifies whether the linker will apply the SuppressUnmanagedCodeSecurity attribute to linker-generated PInvoke stubs that call from managed code into native DLLs. */
    @@Tool.option("/CLRUNMANAGEDCODECHECK")
    clrUnmanagedCodeCheck?: boolean;

    /** Specifies whether the image is compatible with data execution prevention. */
    @@Tool.option("/NXCOMPAT[|:NO]", { optionSeparationMode: Tool.OptionSeparationMode.supported })
    compatibleWithDataExecutionPrevention?: boolean;

    /** Specifies whether to compress the pdb */
    @@Tool.option("/PDBCOMPRESS")
    compressPdb?: boolean;

    /** Specifies to create a map or not */
    @@Tool.option("/MAP")
    createMap?: boolean;

    /**
     * Specifies whether to create a second program database (PDB) file that omits symbols that you do
     * not want to distribute to your customers.
     */
    createStrippedPDB?: boolean;

    /** Specifies the types of debug data included in the PDB. */
    debugTypes?: DebugTypes;

    /**
     * Specify DLL(s) to be loaded on demand instead of EXE load time.
     * One /DELAYLOAD command line option is emitted for each element of the DelayLoadDll list.
     * This option causes the named DLL(s) to be loaded only on the first call by the program to a function
     * in the DLL. /DELAYLOAD is supported by Delayimp.lib, although you may provide your own delay-load
     * helper functions.
     * DelayLoadDll is declared as an array of strings because the named DLLs are not part of the
     * linked executable.
     */
    @@Tool.option("/DELAYLOAD")
    delayLoadDlls?: String[];

    /** If true, partially signs an assembly. By default, the value is false. */
    @@Tool.option("/DELAYSIGN[|:NO]")
    delaySign?: boolean;

    /** Specifies the type of driver being built */
    driverType?: DriverTypes;

    /**
     * Specified if redundant COMDAT symbols are removed from the linker output.
     */
    @@Tool.option("/OPT[:ICF|:NOICF]")
    enableCOMDATFolding?: boolean;

    /**
     * Enable/Disable optimized debug information
     * Warning: this is an internal-use, undocumented option.  Use at your own risk.
     */
    @@Tool.option("/d2:-Zi", { negateOption: true })
    enableOptimizedDebugInformation?: boolean;

    /**
     * Enable/Disable optimized debug information
     * ///
     * Warning: this is an internal-use, undocumented option.  Use at your own risk.
     */
    @@Tool.option("/d2:-Zi+", { negateOption: true })
    enableOptimizedDebugInformationPlus?: boolean;

    /**
     * Undocumented option /pdbrpc
     * Warning: this is an internal-use, undocumented option.  Use at your own risk.
     */
    @@Tool.option("/pdbrpc:no", { negateOption: true })
    enablePdbRpc?: boolean;

    /**
     * Disable speculative devirtualization
     * ///
     * Warning: this is an internal-use, undocumented option.  Use at your own risk.
     */
    @@Tool.option("/d2:-SpecDevirt-", { negateOption: true })
    enableSpecDevirt?: boolean;

    /**
     * Disable type-based speculative devirtualization.
     * ///
     * Warning: this is an internal-use, undocumented option.  Use at your own risk.
     */
    @@Tool.option("/d2:-TypeProp-", { negateOption: true })
    enableTypeProp?: boolean;

    /**
     * Undocumented option /novcfeature
     * Warning: this is an internal-use, undocumented option.  Use at your own risk.
     */
    @@Tool.option("/novcfeature", { negateOption: true })
    enableVCFeature?: boolean;

    /**
     * Disable automatic vectorization
     * ///
     * Warning: this is an internal-use, undocumented option.  Use at your own risk.
     */
    @@Tool.option("/d2:-Qvec-", { negateOption: true })
    enableVectorization?: boolean;

    /**
     * Enables a �dispatch-mode� for Control Flow Guard checks, dispatching to target instead of returning to caller.
     * This will become enabled by default with the /guard:cf flag in the next Visual Studio update (for AMD64).
     */
    @@Tool.option("/d2:-guardcfgdispatch")
    enableDispatchModeForControlFlowGuard?: boolean;

    /**
     * Enables additional checks on longjmp by telling the compiler to track setjmp return addresses.
     * Warning: this is an internal-use, undocumented option.  Use at your own risk.
     */
    @@Tool.option("/d2:-guardlongjmp")
    enableGuardForLongJump?: boolean;

    /** Specifies the starting address (entry point) for an .exe file or DLL. */
    @@Tool.option("/ENTRY")
    entryPoint?: string;

    /** Specifies whether to report internal linker errors to Microsoft. */
    @@Tool.option("/errorReport")
    errorReportSetting?: Shared.ErrorReport;

    /**
     * Specifies set of functions/data from the program to export so that other programs can call/use them.
     * This is a command line equivalent of EXPORTS statement in module definition file (.def).
     */
    @@Tool.option("/EXPORT")
    exports?: ExportSymbol[];

    /**
     * If non-zero, we insert /FILEALIGN:[number] into the command line.
     * /FILEALIGN specifies the alignment of sections in your output file. Sections are blocks of contiguous memory in a
     * Portable Executable (PE) file that contains either code or data. /FILEALIGN lets you link your application with a
     * nonstandard alignment. Each section is aligned on a boundary that is a multiple of the /FILEALIGN value. There is
     * no fixed default. By specifying the section size, you can change the size of the output file. Modifying section
     * size may be useful for programs that will run on smaller devices.
     * Warning: this is an internal-use, undocumented option.  Use at your own risk.
     */
    @@Tool.option("/FILEALIGN")
    fileAlign?: number;

    /** Creates a program that can be loaded only at its preferred base address */
    @@Tool.option("/FIXED", { optionSeparationMode: Tool.OptionSeparationMode.supported })
    fixedBaseAddress?: boolean;

    /** Specifies the forced symbol references. */
    @@Tool.option("/INCLUDE")
    forcedSymbolReferences?: string[];

    /** Specifies the amount of padding to add to the beginning of each function. */
    @@Tool.option("/FUNCTIONPADMIN", { optionSeparationMode: Tool.OptionSeparationMode.supported })
    functionPaddingMinimumSpace?: number;

    /** If true, creates debugging information for the .exe file or DLL. */
    @@Tool.option("/DEBUG")
    generateDebugInformation?: boolean;

    /** Specifies the amount of physical memory on the heap to allocate at a time. */
    //TODO: figure out toolOption form
    heapCommitSize?: number;

    /** Specifies the total heap allocation in virtual memory. */
    //TODO: figure out toolOption form
    heapReserveSize?: number;

    /** Specifies the name of the .idl file and other MIDL output files. */
    @@Tool.option("/IDLOUT")
    idlFile?: PathAtom;

    /** Tells the linker to remove all default libraries from the list of libraries it searches when resolving external references. */
    @@Tool.option("/NODEFAULTLIB")
    ignoreAllDefaultLibraries?: boolean;

    /** Specifies that any IDL attributes in source code should not be processed into an .idl file. */
    @@Tool.option("/IGNOREIDL")
    ignoreIdlAttributes?: boolean;

    /** Tells the linker to remove one or more specific libraries from the list of libraries it searches when resolving external references. */
    @@Tool.option("/NODEFAULTLIB")
    ignoreSpecificLibraries?: PathAtom[];

    /** This option directs LINK to ignore the specified warnings. All arguments must be numbers between 4000 and 4999 inclusive. */
    @@Tool.option("/IGNORE", { supportsMultipleValues: true, multipleValueSeparator: ',' })
    ignoreWarnings?: number[];

    /** Specifies the base address of the image. */
    @@Tool.option("/BASE", { supportsMultipleValues: true, multipleValueSeparator: ',' })
    imageBase?: number;

    /**
     * Points to a file that contains the image base address and maximum image size.
     * The key is used to look up an entry in the data file that specifies the
     * image's intended base address and maximum size.
     * If this option is provided, ImageBase and ImageSize should not be set.
     */
    @@Tool.option("/BASE:@filename,key")
    imageBaseFileEntry?: ImageBaseFileEntry;

    /**
     * Specifies whether the linker will only produce an image if it can also produce a table of the image's safe exception handlers.
     * See http://msdn.microsoft.com/en-us/library/9a89h429.aspx
     */
    @@Tool.option("/SAFESEH", { optionSeparationMode: Tool.OptionSeparationMode.supported })
    imageHasSafeExceptionHandlers?: boolean;

    /**
     * Specifies the maximum permissible image size.
     * The linker will issue a warning if the generated image is larger than the size
     * specified here.
     */
    @@Tool.option("/BASE", { supportsMultipleValues: true, multipleValueSeparator: ',' })
    imageSize?: number;

    /**
     * When the Import Library and Export File are built in a separate step,
     * LinkRunner needs to include the Export File in it's input files.
     */
    importLibrary?: Lib.Result;

    /** A user-specified import library name that replaces the default library name. */
    @@Tool.option("/IMPLIB")
    importLibraryName?: PathAtom;

    /**
     * Tell linker to include a bindable IAT in the final image.
     * In the context of DELAYLOAD, if the DLL is bound, the DELAYLOAD helper function
     * will attempt to use the information in the IAT instead of calling GetProcAddress.
     * The linker option /DELAY:NOBIND circumvents this behavior so that the linker will
     * ignore the values in the IAT.
     * /DELAY:NOBIND is only emitted if IncludeBindableImportAddressTableInImage is false.
     */
    @@Tool.option("/DELAY:NOBIND", { negateOption: true })
    includeBindableImportAddressTableInImage?: boolean;

    /** Indicates that the target is a kernel-mode binary */
    @@Tool.option("/KERNEL")
    kernelMode?: boolean;

    /** Container that contains the key for a signed assembly. */
    @@Tool.option("/KEYCONTAINER:")
    keyContainer?: File;

    /** Specifies a file that contains the key for a signed assembly. */
    @@Tool.option("/KEYFILE:")
    keyFile?: File;

    /**
     * If true, we insert /LARGEADDRESSAWARE into the command line.
     * The /LARGEADDRESSAWARE option tells the linker that the application can handle addresses larger than 2 gigabytes.
     * In the 64-bit compilers, this option is enabled by default. In the 32-bit compilers, /LARGEADDRESSAWARE:NO is enabled
     * if /LARGEADDRESSAWARE is not otherwise specified on the linker line.
     */
    @@Tool.option("/LARGEADDRESSAWARE")
    largeAddressAware?: boolean;

    /**
     * This argument allows you to pass two things:
     * File: the runner can take concrete libraries and reference them.
     * StaticDirectory: the runner can take a sealed directory with a bunch of libraries. This is searched when an #import is used.
     * When a static directory is used it is added to the LIB environment variable
     * SET LIB=
     * http://msdn.microsoft.com/en-us/library/aa270757(v=vs.60).aspx
     */
    libraries?: (File | StaticDirectory)[];

    /**
     * If true, specifies that the linker is to display a progress
     * indicator that shows what percentage of the link is complete.
     */
    @@Tool.option("/LTCG:{STATUS|NOSTATUS}")
    linkStatus?: boolean;

    /**
     * Specifies value enabling link time code generation of objects compiled with /GL (Whole Program Optimization).
     * Depending on this value decides whether ProfileGuidedOptimizationData or OutputProfileGuidedInstrumentationName are used.
     */
    linkTimeCodeGeneration?: LinkTimeCodeGenerationOption;

    /** Creates a mapfile with given name. */
    mapFile?: PathAtom;

    /** Specifies how the manifest should be generated (embedded, not at all, or by in a file on the side) */
    manifestMode?: ManifestMode;

    /** Specifies whether User Account Control (UAC) information is embedded in the program manifest. */
    @@Tool.option("/MANIFESTUAC")
    manifestUAC?: boolean;

    /**
     * Combines sections in an image. Data will be passed to the command line in the form:
     * /MERGE:[key]=[value]
     */
    @@Tool.option("/MERGE")
    mergeSections?: Map<string, string>;

    /**
     * Specify MIDL Command Line Options.
     * TODO:  This should be an input file?
     */
    @@Tool.option("/MIDL")
    midlCommandLineOptions?: LinkMidlArgument[];

    /** Specifies the name of a module definition file. */
    @@Tool.option("/DEF")
    moduleDefinitionFile?: File;

    /** Specifies native visualizer (.natvis) files to be included in the PDB. */
    nativeVisualizerFiles?: File[];

    /** TODO: Add documentation */
    @@Tool.option("/NOOPTIDATA")
    noOptiData?: boolean;

    /**
     * Enables elimination of functions or data that are never referenced.
     * /OPT:[REF|NOREF]
     */
    @@Tool.option("/OPT:[REF|NOREF]")
    optimizeReferences?: boolean;

    /** Specifies a text file that defines the linking order for COMDAT functions. */
    @@Tool.option("/ORDER")
    order?: File;

    /** Specifies the OS version written in Image Optional Header */
    @@Tool.option("/OSVERSION")
    osVersion?: string;

    /** Overrides the default name of the program that the linker creates. */
    @@Tool.option("/OUT")
    outputFile?: PathAtom;

    /**
     * Specifies the database that will be created by profile-guided instrumentation.
     * Depending on the value of LinkTimeCodeGeneration, the argument /PGD either specifies an input or an output. In this case it is an output.
     */
    @@Tool.option("/PGD")
    outputProfileGuidedInstrumentationName?: PathAtom;

    /**
     * Set the memory page size assumed by the image.
     * ///
     * Warning: this is an internal-use, undocumented option.  Use at your own risk.
     */
    @@Tool.option("/PAGESIZE", { optionSeparationMode: Tool.OptionSeparationMode.supported, optionSeparator: ':' })
    pageSize?: number;

    /** Specifies to Produce an output file that can be used with the Performance Tools profiler. */
    @@Tool.option("/PROFILE")
    produceProfiler?: boolean;

    /**
     * Specifies the databases that will be consumed by profile-guided optimization.
     * Depending on the value of LinkTimeCodeGeneration, the argument /PGD either specifies an input or an output. In this case it is an input.
     */
    @@Tool.option("/PGD")
    profileGuidedOptimizationData?: File[];

    /**
     * Provides an alternate PDB path, which can include environment variables that the linker will expand.
     * This option provides an alternate location for the Program Database (.pdb) file, replacing the location the linker
     * linker would normally record in the completed binary. This enables you to provide a path that is independent
     * file structure of the build computer.  This is useful, if you want to put the .pdb file on a network share, for example.
     */
    @@Tool.option("/PDBALTPATH")
    programDatabaseAlternatePath?: string;

    /** Specifies a name for the program database (PDB) that the linker creates if  is set. */
    @@Tool.option("/PDB")
    programDatabaseFile?: PathAtom;

    /** Specifies the project type to get proper link runner behavior. */
    @@Tool.option("/DLL")
    projectType?: LinkProjectType;

    /**
     * Specifies a value controls whether to generate an executable image that can be randomly rebased at load time by using
     * the address space layout randomization (ASLR) feature of Windows Vista.
     */
    @@Tool.option("/DYNAMICBASE", { optionSeparationMode: Tool.OptionSeparationMode.supported })
    randomizedBaseAddress?: boolean;

    /**
     * Specifies the return flow guard type
     */
    returnFlowGuard?: ReturnFlowGuardType;

    /** Specifies that the module requires a signature check at load time. */
    @@Tool.option("/INTEGRITYCHECK")
    requiredSignatureCheck?: boolean;

    /** Prevents the linker from registering an entry point for the DLL */
    @@Tool.option("/NOENTRYPOINT")
    noDllEntryPoint?: boolean;

    /** List of resource files (.res) */
    resources?: File[];

    /**
     * Add a response file with a set of common defines.
     * This response file will be added at the start of the command line.
     */
    @@Tool.option("@{responseFile}")
    responseFile?: File;

    /** Changes the attributes of a section, overriding the attributes set when the .obj file for the section was compiled. */
    @@Tool.option("/SECTION", { supportsMultipleValues: true, multipleValueSeparator: ',' })
    sections?: Section[];

    /** Specifies whether to set the Checksum in the header of an .exe file. */
    @@Tool.option("/RELEASE")
    setChecksum?: boolean;

    /**
     * List of ICompilationOutput object to be given to link.exe
     * Common extensions are .obj .lib
     */
    sources?: Cl.CompilationOutput[];

    /** Specifies a value indicating the total stack allocation size in physical memory. */
    @@Tool.option("/STACK", { supportsMultipleValues: true, multipleValueSeparator: ',' })
    stackCommitSize?: number;

    /** Specifies the total stack allocation size in virtual memory. */
    @@Tool.option("/STACK", { supportsMultipleValues: true, multipleValueSeparator: ',' })
    stackReserveSize?: number;

    /** Specifies to a MS-DOS stub program to a Win32 program. */
    @@Tool.option("/STUB")
    stubProgram?: File;

    /** Specifies the environment for the executable. */
    @@Tool.option("/SUBSYSTEM")
    subsystem?: Shared.Subsystem;

    /**
     * Declares support for the specific unloading of a DLL.
     * If you do not specify this option, any call to FUnloadDelayLoadedDLL will fail.
     */
    @@Tool.option("/DELAY:UNLOAD")
    supportExplicitDllUnloading?: boolean;

    /** Suppress copyright message */
    @@Tool.option("/nologo")
    suppressStartupBanner?: boolean;

    /** Specifies where to load linked output to for a Swap File */
    @@Tool.option("/SWAPRUN")
    swapLocation?: SwapLocation;

    /** Specifies the target platform for the program or DLL. */
    @@Tool.option("/MACHINE")
    targetMachine?: Machine;

    /**
     * The /TSAWARE option sets a flag in the IMAGE_OPTIONAL_HEADER DllCharacteristics field in the program image's optional header.
     * When this flag is set, Terminal Server will not make certain changes to the application.
     */
    @@Tool.option("/TSAWARE", { optionSeparationMode: Tool.OptionSeparationMode.supported })
    terminalServerAware?: boolean;

    /** Specifies the name and extension of the .tlb file */
    @@Tool.option("/TLBOUT")
    tlbFile?: PathAtom;

    /** Treats all linker warnings as errors. */
    @@Tool.option("/WX")
    treatWarningAsError?: boolean;

    /** Specifies the resource ID of the linker-generated type library. */
    @@Tool.option("/TLBID")
    typeLibraryResourceId?: number;

    /**
     * Specifies the requested execution level for the application when it is run under with User Account Control.
     */
    uacLevel?: UacLevel;

    /**
     * If true, the application bypasses user interface protection levels and drives input to
     * higher-permission windows on the desktop; otherwise, false.
     */
    @@Tool.option("/MANIFEST:uiAccess=[true|false]")
    uacUiAccess?: boolean;

    /** Suppresses the creation of a .NET Framework assembly */
    @@Tool.option("/NOASSEMBLY", { negateOption: true })
    useFrameworkAssembly?: boolean;

    /** Specifies verbose output */
    @@Tool.option("/VERBOSE")
    verbose?: Verbose;

    /** Specifies the version number in the header of the .exe or .dll file. */
    @@Tool.option("/VERSION:major[.minor]")
    version?: string;

    /** Enables generation of a Windows Runtime Metadata file. */
    @@Tool.option("/WINMD", { optionSeparationMode: Tool.OptionSeparationMode.supported })
    windowsMetadata?: WindowsMetadata;

    /** Specifies the file name for the Windows Runtime Metadata (.winmd) output file */
    @@Tool.option("/WINMDFILE")
    windowsMetadataFile?: PathAtom;
}

/**
 * Link defined MIDL Argument
 */
@@public
export interface LinkMidlArgument {
    /** The name of the dlldata file for a proxy DLL. */
    dllDataFile: PathAtom;

    /** The name of the output header file. */
    headerFile: PathAtom;

    /** The name of the interface UUID file. */
    iidFile: PathAtom;

    /** The name of the include directory */
    includeDirectory: StaticDirectory;

    /** Midl Argument specified */
    midlArgument?: string;

    /** The name of the proxy file. */
    proxyFile: PathAtom;
}

/**
 * Output of running Link transformer
 */
@@public
export interface Result extends Shared.BuiltImage {
    /** Exports file */
    importLibrary: File;

    /** Map file */
    mapFile: File;

    /** Output from calling Midl */
    midlOutput?: Midl.Result;

    /** Named interface definition language (IDL) output */
    nameIdlOutput: File;

    /** Profile guided optimization database file */
    profileGuidedOptimizationDataFile: File;

    /** Stripped PDB information for BinaryFile */
    strippedDebugFile: File;

    /** WindowsMetaData File */
    windowsMetadataFile: File;
}

/** Controls some link runner behavior by specifying what type of project is being linked. */
@@public
export const enum LinkProjectType {
    /** The target is a driver. */
    driver,
    /** The target is a DLL. */
    @@Tool.option("/DLL")
    dynamicLinkLibrary,
    /** The target is a EXE. */
    executable
}

/** Link-time Code Generation option to pass to link.exe. */
@@Tool.option("/LTCG", { supportsMultipleValues: true, valueSeparator: ":" })
@@public
export const enum LinkTimeCodeGenerationOption {
    /** Specifies that the linker outputs a .pgd file in preparation for instrumented test runs on the application. */
    @@Tool.option("/LTCG:PGINSTRUMENT")
    instrument,
    /**
     * Specifies that the linker uses the profile data that is created after the instrumented binary is run 
     * to create an optimized image.
     */
    @@Tool.option("/LTCG:PGOPTIMIZE")
    optimize,
    /**
     * Enables a list of input files to be added or modified from what was specified in the :PGINSTRUMENT phase.
     * However, any new input files are not optimized by using profile-guided optimizations, and changed portions
     * of a modified input file that invalidate profile data that is collected during the instrumentation phase
     * for that code are not optimized by using profile-guided optimizations.
     */
    @@Tool.option("/LTCG:PGUPDATE")
    update,
    /** Tells the linker to call the compiler and perform whole-program optimization. */
    @@Tool.option("/LTCG")
    use
}

/** Machine specification to pass to link.exe. */
@@public
export const enum Machine {
    /** Target arm machines */
    @@Tool.option("/MACHINE:ARM")
    arm,
    /** Target arm64 machines */
    @@Tool.option("/MACHINE:ARM64")
    arm64,
    /** Target ebc machines */
    @@Tool.option("/MACHINE:EBC")
    ebc,
    /** Target ia64 machines */
    @@Tool.option("/MACHINE:IA64")
    ia64,
    /** Target mips machines */
    @@Tool.option("/MACHINE:MIPS")
    mips,
    /** Target mips16 machines */
    @@Tool.option("/MACHINE:MIPS16")
    mips16,
    /** Target mipsfpu machines */
    @@Tool.option("/MACHINE:MIPSFPU")
    mipsfpu,
    /** Target mipsfpu16 machines */
    @@Tool.option("/MACHINE:MIPSFPU16")
    mipsfpu16,
    /** Target sh4 machines */
    @@Tool.option("/MACHINE:SH4")
    sh4,
    /** Target thumb machines */
    @@Tool.option("/MACHINE:THUMB")
    thumb,
    /** Target x64 machines */
    @@Tool.option("/MACHINE:X64")
    x64,
    /** Target x86 machines */
    @@Tool.option("/MACHINE:X86")
    x86
}

/** Specifies how the manifest should be generated (embedded, not at all, or by in a file on the side) */
@@public
export const enum ManifestMode {
    @@Tool.option("/MANIFEST:EMBED")
    embed,
    @@Tool.option("/MANIFEST:NO")
    disabled,
    @@Tool.option("/MANIFEST")
    sideBySide
}

/** Specifies the return flow guard check */
@@public
export const enum ReturnFlowGuardType {
    /**
     * Disable the return flow guard checks
     */
    disabled,
    /**
     * Enable RFG instrumentation and also set the PE bit to enable enforcement (if the image is an EXE).
     */
    @@Tool.option("/GUARD:rf")
    enabled,
    /**
     * Enable RFG instrumentation for the binary but do not set the PE bit to enable enforcement.
     */
    @@Tool.option("/GUARD:rfinstr")
    instrumentation,
    /**
     * Set the PE bit to require strict RFG enforcement for the process (if the image is an EXE). This will require that all images loaded by the process enable RFG.
     */
    @@Tool.option("/GUARD:rfstrict")
    strict,
}

/** Changes the attributes of a section. */
@@public
export interface Section {
    /** The section alignment. Must be a power of 2. */
    alignment?: number;

    /** The attributes to apply to the section */
    attributes?: SectionAttributes;

    /** The section name */
    name: string;
}

/** Attribute flags for sections. */
@@public
export interface SectionAttributes {
    discardable?: boolean;
    execute?: boolean;
    cacheable?: boolean;
    pageable?: boolean;
    read?: boolean;
    shared?: boolean;
    write?: boolean;
}

/** Tells the operating system to copy the linker output to a swap file before it is run. */
@@public
export const enum SwapLocation {
    /** The operating system will copy the image on a removable disk to a page file and then load it */
    @@Tool.option("/SWAPRUN:CD")
    cD,
    /** The operating system will first copy the binary image from the network to a swap file and load it from there */
    @@Tool.option("/SWAPRUN:NET")
    net
}

/** Specifies the requested execution level for the application when it is run under with User Account Control. */
@@public
export const enum UacLevel {
    @@Tool.option("/MANIFESTUAC:level=asInvoker")
    asInvoker,
    @@Tool.option("/MANIFESTUAC:level=highestAvailable")
    highestAvailable,
    @@Tool.option("/MANIFESTUAC:level=requireAdministrator")
    requireAdministrator
}

/** Prints linker progress messages. */
@@public
export const enum Verbose {
    /** Displays details about the linking process */
    @@Tool.option("/VERBOSE")
    enabled,
    /** Non verbose */
    disabled,
    @@Tool.option("/VERBOSE:CLR")
    clr,
    /** Display information about linker activity that results from the use of /OPT:ICF. */
    @@Tool.option("/VERBOSE:ICF")
    icf,
    /** Displays information about the incremental link process. */
    @@Tool.option("/VERBOSE:INCR")
    incr,
    /** Displays progress messages that indicate just the libraries searched. */
    @@Tool.option("/VERBOSE:LIB")
    lib,
    /** Displays information about linker activity that results from the use of /OPT:REF. */
    @@Tool.option("/VERBOSE:REF")
    ref,
    /** Displays information about modules that are not compatible with safe exception handling when /SAFESEH is not specified. */
    @@Tool.option("/VERBOSE:SAFESEH")
    safeseh,
    /** Displays information about any library files that are unused when the image is created. */
    @@Tool.option("/VERBOSE:UNUSEDLIBS")
    unusedLibs
}

/** Enables generation of a Windows Runtime Metadata (.winmd) file. */
@@public
export const enum WindowsMetadata {
    /** No generation of a Windows Runtime Metadata File */
    disabled,
    /** The default setting for Windows Store apps. The linker generates both the binary executable file and the .winmd metadata file */
    @@Tool.option("/WINMD")
    enabled,
    /** The linker generates only the binary executable file, but not a .winmd file. */
    @@Tool.option("/WINMD:NO")
    noGeneration,
    /** The linker generates only the .winmd file, but not the binary executable file. */
    @@Tool.option("WINMD:ONLY")
    only
}
