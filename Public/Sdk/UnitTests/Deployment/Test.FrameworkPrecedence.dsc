// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

import {Assert, Testing} from "Sdk.Testing";
import {Transformer} from "Sdk.Transformers";
import * as Deployment from "Sdk.Deployment";
import * as Managed from "Sdk.Managed.Shared";

namespace Sdk.DeploymentTests {
    const coreFile = f`core/Library.dll`;
    const frameworkFile = f`framework/Library.dll`;
    const otherFrameworkFile = f`otherFramework/Library.dll`;
    const packageFile = f`package/Library.dll`;
    const otherPackageFile = f`otherPackage/Library.dll`;
    const frameworkBinary = Managed.Factory.createBinaryFromFiles(frameworkFile).override<Managed.Binary>({ preferDeployment: true });
    const otherFrameworkBinary = Managed.Factory.createBinaryFromFiles(otherFrameworkFile).override<Managed.Binary>({ preferDeployment: true });

    function flatten(files: Deployment.DeployableItem[]): Deployment.FlattenedResult {
        return Deployment.flatten(
            { contents: files },
            Managed.Deployment.preferRuntimeFiles(
                Set.create(coreFile),
                Deployment.Diagnostics.handleDuplicateFileDeployment));
    }

    @@Testing.unitTest()
    export function frameworkWinsOverPackageInEitherOrder() {
        Assert.areEqual(frameworkFile, flatten([frameworkBinary, packageFile]).flattenedFiles.get(r`Library.dll`).file);
        Assert.areEqual(frameworkFile, flatten([packageFile, frameworkBinary]).flattenedFiles.get(r`Library.dll`).file);
    }

    @@Testing.unitTest()
    export function coreRuntimeWinsOverFrameworkInEitherOrder() {
        Assert.areEqual(coreFile, flatten([coreFile, frameworkBinary, packageFile]).flattenedFiles.get(r`Library.dll`).file);
        Assert.areEqual(coreFile, flatten([packageFile, frameworkBinary, coreFile]).flattenedFiles.get(r`Library.dll`).file);
    }

    @@Testing.unitTest()
    export function rediscoveringSameFilePreservesFrameworkPreference() {
        Assert.areEqual(frameworkFile, flatten([frameworkFile, frameworkBinary, packageFile]).flattenedFiles.get(r`Library.dll`).file);
        Assert.areEqual(frameworkFile, flatten([frameworkBinary, frameworkFile, packageFile]).flattenedFiles.get(r`Library.dll`).file);
    }

    @@Testing.unitTest()
    export function ordinaryPackageConflictsStillFail() {
        Testing.expectFailure(
            () => { flatten([packageFile, otherPackageFile]); },
            "Error trying to deploy both file");
    }

    @@Testing.unitTest()
    export function frameworkConflictsStillFail() {
        Testing.expectFailure(
            () => { flatten([frameworkBinary, otherFrameworkBinary]); },
            "Error trying to deploy both file");
    }

    @@Testing.unitTest()
    export function frameworkFilesFollowTransitiveReferencesAndExclusions() {
        const excludedBinary = Managed.Factory.createBinaryFromFiles(f`framework/Excluded.dll`);
        const dependency: Managed.Assembly = {
            name: a`Dependency`,
            targetFramework: "net10.0",
            runtime: Managed.Factory.createBinaryFromFiles(f`assembly/Dependency.dll`),
            references: [frameworkBinary, excludedBinary],
            deploy: Managed.Deployment.flattenAssembly,
        };
        const assemblyBinary = Managed.Factory.createBinaryFromFiles(f`assembly/Test.dll`);
        const assembly: Managed.Assembly = {
            name: a`Test`,
            targetFramework: "net10.0",
            compile: assemblyBinary,
            runtime: assemblyBinary,
            references: [dependency],
            runtimeContentToSkip: [excludedBinary],
            deploy: Managed.Deployment.flattenAssembly,
        };

        const result = Deployment.flatten({ contents: [packageFile, assembly] });
        Assert.areEqual(frameworkFile, result.flattenedFiles.get(r`Library.dll`).file);
        Assert.areEqual(undefined, result.flattenedFiles.get(r`Excluded.dll`));
    }

    @@Testing.unitTest()
    export function frameworkFilesInRuntimeContentArePreferred() {
        const assembly: Managed.Assembly = {
            name: a`Test`,
            targetFramework: "net10.0",
            runtime: Managed.Factory.createBinaryFromFiles(f`assembly/Test.dll`),
            references: [],
            runtimeContent: { contents: [frameworkBinary] },
            deploy: Managed.Deployment.flattenAssembly,
        };

        const result = Deployment.flatten({ contents: [packageFile, assembly] });
        Assert.areEqual(frameworkFile, result.flattenedFiles.get(r`Library.dll`).file);
    }

    @@Testing.unitTest()
    export function frameworkPackageWinsThroughPackageDependencies() {
        const bundledFile = f`framework/runtimes/test/lib/net10.0/Library.dll`;
        const contents = Transformer.sealDirectory({ root: d`framework`, files: [bundledFile] });
        const reference = Managed.Factory.createNugetPackage("Framework.Ref", "1.0", contents, [], []);
        const framework = Managed.Factory.createFrameworkPackage(reference, reference, a`test`, a`net10.0`);
        const dependency = Managed.Factory.createNugetPackage(
            "Package", "1.0", contents, [],
            [Managed.Factory.createBinaryFromFiles(packageFile)],
            [framework]);

        const result = Deployment.flatten({ contents: [dependency] });
        Assert.areEqual(bundledFile, result.flattenedFiles.get(r`Library.dll`).file);
    }

    function preferredAssembly(binary: Managed.Binary): Managed.Assembly {
        return {
            name: a`Preferred`,
            targetFramework: "net10.0",
            runtime: binary,
            references: [],
            preferDeployment: true,
            deploy: Managed.Deployment.flattenAssembly,
        };
    }

    function assemblyWithDependencies(): Managed.Assembly {
        const binary = Managed.Factory.createBinaryFromFiles(frameworkFile, f`framework/Library.pdb`, f`framework/Library.xml`);
        const dependency = Managed.Factory.createBinaryFromFiles(f`package/Dependency.dll`);
        return preferredAssembly(binary).override<Managed.Assembly>({
            references: [dependency],
            runtimeContent: { contents: [f`content/Content.dll`] },
            runtimeConfigFiles: [f`framework/Preferred.config`],
        });
    }

    @@Testing.unitTest()
    export function assemblyPreferenceAppliesOnlyToOwnRuntimeFiles() {
        const assembly = assemblyWithDependencies();

        for (let items of [[packageFile, assembly], [assembly, packageFile]]) {
            const result = flatten(items);
            Assert.areEqual(frameworkFile, result.flattenedFiles.get(r`Library.dll`).file);
            Assert.areEqual(true, result.flattenedFiles.get(r`Library.pdb`).preferDeployment);
            Assert.areEqual(true, result.flattenedFiles.get(r`Library.xml`).preferDeployment);
            Assert.areEqual(undefined, result.flattenedFiles.get(r`Dependency.dll`).preferDeployment);
            Assert.areEqual(undefined, result.flattenedFiles.get(r`Content.dll`).preferDeployment);
            Assert.areEqual(undefined, result.flattenedFiles.get(r`Preferred.config`).preferDeployment);
        }

        Testing.expectFailure(
            () => { flatten([f`other/Dependency.dll`, assemblyWithDependencies()]); },
            "Error trying to deploy both file");
        Testing.expectFailure(
            () => { flatten([assemblyWithDependencies(), preferredAssembly(Managed.Factory.createBinaryFromFiles(otherPackageFile))]); },
            "Error trying to deploy both file");

        const executable = f`framework/Preferred.exe`;
        const nativeAssembly = assembly.override<Managed.Assembly>({ nativeExecutable: executable });
        Assert.areEqual(executable, flatten([f`package/Preferred.exe`, nativeAssembly]).flattenedFiles.get(r`Preferred.exe`).file);
    }

    const preferredPackageContents = Transformer.sealDirectory({ root: d`framework`, files: [frameworkFile] });

    function packageWithDependencies(): Managed.ManagedNugetPackage {
        const binary = Managed.Factory.createBinaryFromFiles(frameworkFile, f`framework/Library.pdb`, f`framework/Library.xml`);
        const dependency = Managed.Factory.createNugetPackage(
            "Dependency", "1.0", preferredPackageContents, [], [Managed.Factory.createBinaryFromFiles(f`package/Dependency.dll`)]);
        return Managed.Factory.createNugetPackage("Preferred", "1.0", preferredPackageContents, [], [binary], [dependency])
            .override<Managed.ManagedNugetPackage>({
                preferDeployment: true,
                runtimeContent: { contents: [f`content/Content.dll`] },
            });
    }

    @@Testing.unitTest()
    export function packagePreferenceAppliesOnlyToOwnRuntimeFiles() {
        const preferredPackage = packageWithDependencies();

        for (let items of [[packageFile, preferredPackage], [preferredPackage, packageFile]]) {
            const result = flatten(items);
            Assert.areEqual(frameworkFile, result.flattenedFiles.get(r`Library.dll`).file);
            Assert.areEqual(true, result.flattenedFiles.get(r`Library.pdb`).preferDeployment);
            Assert.areEqual(true, result.flattenedFiles.get(r`Library.xml`).preferDeployment);
            Assert.areEqual(undefined, result.flattenedFiles.get(r`Dependency.dll`).preferDeployment);
            Assert.areEqual(undefined, result.flattenedFiles.get(r`Content.dll`).preferDeployment);
        }

        Testing.expectFailure(
            () => { flatten([f`other/Dependency.dll`, packageWithDependencies()]); },
            "Error trying to deploy both file");
        Testing.expectFailure(
            () => {
                const preferredPackage = packageWithDependencies();
                const otherPackage = preferredPackage.override<Managed.ManagedNugetPackage>({
                    runtime: [Managed.Factory.createBinaryFromFiles(otherPackageFile)],
                });
                flatten([preferredPackage, otherPackage]);
            },
            "Error trying to deploy both file");
    }

    @@Testing.unitTest()
    export function containerPreferencePreservesExclusionsAndRediscovery() {
        const contents = Transformer.sealDirectory({ root: d`framework`, files: [frameworkFile] });
        const binary = Managed.Factory.createBinaryFromFiles(frameworkFile, f`framework/Library.pdb`, f`framework/Library.xml`);
        const preferredPackage = Managed.Factory.createNugetPackage("Preferred", "1.0", contents, [], [binary])
            .override<Managed.ManagedNugetPackage>({ preferDeployment: true });

        for (let container of [preferredAssembly(binary), preferredPackage]) {
            const result = flatten([binary, container, packageFile]);
            Assert.areEqual(frameworkFile, result.flattenedFiles.get(r`Library.dll`).file);
            Assert.areEqual(true, result.flattenedFiles.get(r`Library.pdb`).preferDeployment);
            Assert.areEqual(true, result.flattenedFiles.get(r`Library.xml`).preferDeployment);

            const excluded = Deployment.flatten({ contents: [container] }, undefined, { excludedDeployableItems: [binary] });
            Assert.areEqual(0, excluded.flattenedFiles.count());

            const options: Managed.Deployment.FlattenOptions = { skipPdb: true, skipXml: true };
            const skipped = Deployment.flatten({ contents: [binary, container, packageFile] }, undefined, options);
            Assert.areEqual(frameworkFile, skipped.flattenedFiles.get(r`Library.dll`).file);
            Assert.areEqual(undefined, skipped.flattenedFiles.get(r`Library.pdb`));
            Assert.areEqual(undefined, skipped.flattenedFiles.get(r`Library.xml`));
        }
    }
}
