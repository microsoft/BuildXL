// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Transformer {
    /**
     * A detailed definition of a tool.
     */
    @@public
    export interface ToolDefinition extends ToolDefinitionOptions {
        /** The file with the actual executable. */
        exe: File;
    }

    @@public
    export interface ToolDefinitionOptions {

        /** Common description that will be used for all pips. */
        description?: string;

        /** List of nested tools used by current executable. */
        nestedTools?: ToolDefinition[];

        /** 
         * The files that are runtime dependencies for this executable.
         * 
         * Unlike "untrackedFiles", BuildXL is tracking if these files change.
         */
        runtimeDependencies?: File[];

        /** 
         * The directories that are runtime dependencies for this executable.
         * The executable may access any files under these directories.
         * 
         * Unlike "untrackedDirectoryScopes", BuildXL is tracking if these files change.
         */
        runtimeDirectoryDependencies?: StaticDirectory[];

        /** Runtime environment for the executable.  */
        runtimeEnvironment?: RuntimeEnvironment; 

        /** This tool needs a temporary directory. */
        prepareTempDirectory?: boolean;

        /** True if the executable depends on Windows directories. This signals that accesses to the Windows Directories should be allowed. */
        dependsOnWindowsDirectories?: boolean;

        /** 
          * True if the executable depends on directories that comprise the current host OS. 
          * For instance on windows this signals that accesses to the Windows Directories %WINDIR% should be allowed. 
          */
        dependsOnCurrentHostOSDirectories?: boolean;

        /** 
         * True if the executable depends on the per-user AppData directory. Setting this to true means that AppData
         * will be an untracked directory scope and the specific location of AppData will not be included in the pip's fingerpint. 
         */
        dependsOnAppDataDirectory?: boolean;

        /** Files to which read and write accesses should not be tracked. */
        untrackedFiles?: File[];

        /** Directories to which accesses should not be tracked (however, accesses to all nested files and subdirectories are tracked). */
        untrackedDirectories?: Directory[];

        /** Directories in which nested file and directory accesses should not be tracked. */
        untrackedDirectoryScopes?: Directory[];

        /** Provides a hard timeout after which the Process will be marked as failure due to timeout and terminated. */
        timeoutInMilliseconds?: number;

        /** 
         * Sets an interval value that indicates after which time BuildXL will issue a warnings that the process is running longer
         * than anticipated */
        warningTimeoutInMilliseconds?: number;
        
        // TODO: unimplemented
        /*
            producesPathIndependentOutputs?: boolean;
            // tags?: string[]; moved to RunnerArguments
        */
    }
    
    /**
     * Specifies specific settings that should be used when launching a CLR application. These settings are implemented
     * by setting specific environment variables for the launched process. This can be used to run an application under
     * a specific CLR or a CLR that is xcopy installed on a machine.
     */
    @@public
    export interface ClrConfig {
        /** Path to the installation root of the CLR. COMPLUS_InstallRoot will be set to this path. */
        installRoot?: Path;

        /** The version of the CLR. COMPLUS_InstallRoot will be set to this value. */
        version?: string;

        /** Value for COMPLUS_NoGuiFromShim environment variable. */
        guiFromShim?: boolean;

        /** Value for COMPLUS_DbgJitDebugLaunchSetting environment variable. */
        dbgJitDebugLaunchSetting?: boolean;
        
        /**
         * Force all apps to use the checked in CLR, despite <supportedRuntime> elements in the config.
         * We need this to deal with tools that specify supportedRuntime v4.0 or v4.0.30319 since we can
         * only specify one runtime version (COMPLUS_Version).  COMPLUS_OnlyUseLatestClr will be set to "1"
         * if this value is true.
         */
        onlyUseLatestClr?: boolean;

        /** Default version of the CLR.  COMPLUS_DefaultVersion will be set to this value. */
        defaultVersion?: string;
    }

    /** 
     * Information about the runtime environment of a process. 
     */
    @@public
    export interface RuntimeEnvironment {
        /** Minimum supported OS version. No requirement when not set. */
        minimumOSVersion?: Version;

        /** Maximum supported OS version. Unbounded when not set. */
        maximumOSVersion?: Version;

        /** Minimum required CLR version. No requirement when not set. */
        minimumClrVersion?: Version;

        /** Maximum supported CLR version. Unbounded when not set. */
        maximumClrVersion?: Version;

        /**
         * Overrides the default CLR that would be used when launching a process.
         * Specifying this value will cause various environment variables to be set
         * which will cause a specific version of the CLR to be used.
         */
        clrOverride?: ClrConfig;
    }
       
    /**
     * Version information for an assembly, OS, or CLR. Corresponds to System.Version as described at:
     * http://msdn.microsoft.com/en-us/library/System.Version(v=vs.110).aspx
     */
    @@public
    export interface Version {
        /** The build number. */
        buildNumber?: number; 

        /** The major version number. Must be non-negative. */
        major: number; 

        /** The minor version number. Must be non-negative. */
        minor: number;

        /** The revision number. */
        revision?: number;
    }
}
