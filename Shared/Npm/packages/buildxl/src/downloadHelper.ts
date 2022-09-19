import * as childProcess from 'child_process';
import * as fs from 'fs';
import * as fsExtra from 'fs-extra';
import * as os from 'os';
import * as path from 'path';
import * as stream from 'stream';
import * as util from 'util';
import fetch from 'node-fetch';

async function getVersion() : Promise<string> {
    const pkgJson = await fsExtra.readFile(path.join(__dirname, '..', 'package.json'), { encoding: 'utf8'});
    const pkg = JSON.parse(pkgJson);
    return pkg.version;
}

async function downloadNugetPackage(targetDir: string, packageName: string, version: string) : Promise<void> {
    const feed = "https://pkgs.dev.azure.com/ms/BuildXL/_packaging/BuildXL/nuget/v3/index.json";

    // Clear 
    try {
        await fs.promises.rmdir(targetDir, {recursive: true});
    } catch (err) {
        // Ignore folder not found error
        if ((err as {code?: unknown}).code !== 'ENOENT') {
            throw err;
        }
    }

    // Pull nuget.exe
    console.log('Downloading nuget tool.');
    const nugetTarget = path.join(targetDir, 'nuget.exe');
    await downloadFile("https://dist.nuget.org/win-x86-commandline/v5.7.0/nuget.exe", nugetTarget);
    
    // Let nuget pull the package
    console.log(`Downloading and extracting BuildXL package ${packageName} version ${version}.`);
    await util.promisify(childProcess.exec)(`${nugetTarget} install -OutputDirectory "${targetDir}" -Source "${feed}" -Version "${version}" ${packageName}`, {
        cwd: process.cwd(),
        env: process.env
    });
}

async function downloadFile(url: string, target: string) : Promise<void> {
    await fsExtra.mkdirp(path.dirname(target));
    const response = await fetch(url);
	if (response.ok) {
		return util.promisify(stream.pipeline)(response.body, fs.createWriteStream(target));
	}

	throw new Error(`unexpected response ${response.statusText} from url ${url}`);
}

export async function ensureTool() : Promise<string> {
    const architecture = os.arch();
    if (architecture !== 'x64') {
        throw new Error(`BuildXL is currently only supported on x64 operating systems. '${architecture}' is not yet supported.`);
    }

    let target : string = '';
    const type = os.type();
    switch (type) {
        case 'Darwin':
            throw new Error(`BuildXL does support MacOS, but there is no nuget.exe client for mac to download the bits.`);
        case 'Windows_NT':
            target = 'win-x64';
            break;
        default:
            throw new Error(`BuildXL does not yet support the target operating system: '${type}'.`);
    }

    const targetDir = path.join(__dirname, '..', 'downloaded', target);
    const packageName = "Microsoft.BuildXL." + target;
    const version = await getVersion();

    const markerFilePath = path.join(targetDir, 'marker.txt');

    if (!await fsExtra.pathExists(markerFilePath))
    {
        await downloadNugetPackage(targetDir, packageName, version);

        // write the marker file
        await fsExtra.writeFile(markerFilePath, new Date().toISOString());
    }

    // return path into the nuget package
    return path.join(targetDir, packageName + "." + version);
}
