import fs from 'node:fs';
import path from 'node:path';
import {randomUUID} from 'node:crypto';

// Publish both services together. An Editor starting Play concurrently sees either complete version.
export function writeEditorConnection(project, connection) {
  const directory = path.join(project, 'UserSettings/ZLive');
  fs.mkdirSync(directory, {recursive: true, mode: 0o700});
  const file = path.join(directory, 'local-connection.json');
  const temporary = file + '.' + randomUUID();
  try {
    fs.writeFileSync(temporary, JSON.stringify(connection), {flag: 'wx', mode: 0o600});
    fs.renameSync(temporary, file);
  } finally {
    fs.rmSync(temporary, {force: true});
  }
  return file;
}
