import { files, directories } from "test";

@@public
export const consumedFile = files.get(p`out/copied.txt`);

@@public
export const consumedSealedDirectory = directories.get(p`out`);

@@public
export const consumedBarrier = directories.get(p`out/opaque`);

const requiredExports = Contract.assert(
    consumedFile !== undefined && consumedSealedDirectory !== undefined && consumedBarrier !== undefined,
    "The required DirectGraph artifacts were not exported");
