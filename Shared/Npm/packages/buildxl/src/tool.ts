import * as downloadHelper from './downloadHelper';
import * as os from 'os';
import * as util from 'util';
import * as path from 'path';
import * as childProcess from 'child_process';

async function runAsync(tool: string) : Promise<void> {
    const folder = await downloadHelper.ensureTool();
    const exeExtension = os.type() === 'NT_Windows' ? '.exe' : '';

    const toolPath = path.join(folder, tool + exeExtension);
    const [/*nodePath*/, /*scriptPath*/, ...args] = process.argv;

    await util.promisify(childProcess.spawn)(toolPath, args, {
        stdio: 'inherit',
        cwd: process.cwd(),
        env: process.env
    });
}

export function run(tool: string) : void {
    runAsync(tool)
        .catch(err => {
            console.error(`Error: ${err}`);
            process.exit(1);
        });
}