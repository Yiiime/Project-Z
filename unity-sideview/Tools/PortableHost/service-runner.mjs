// IPC closes even if the launcher crashes: never leave an orphan service behind.
import {pathToFileURL} from 'node:url';
process.on('disconnect', () => process.exit(0));
process.argv.splice(1, 1);
await import(pathToFileURL(process.argv[1]).href);
