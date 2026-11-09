import fs from 'node:fs/promises';
import {createHash} from 'node:crypto';
import {HARU_ACTIONS,HARU_ACTION_LIBRARY} from '../ui/live2d-actions.js';
const moc=await fs.readFile(new URL('../ui/live2d/models/Haru/Haru.moc3',import.meta.url));
const payload={...HARU_ACTION_LIBRARY,modelSha256:createHash('sha256').update(moc).digest('hex'),actions:HARU_ACTIONS};
await fs.writeFile(new URL('../docs/haru-action-library.json',import.meta.url),JSON.stringify(payload,null,2)+'\n');
console.log(`Exported ${Object.keys(HARU_ACTIONS).length} action presets; calibration samples are explicitly unreviewed.`);
