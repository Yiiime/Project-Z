import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {spawn} from 'node:child_process';
import {once} from 'node:events';
import {readSourceConfig, selectConfig, hostEnvironment} from './config.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const delay = ms => new Promise(r => setTimeout(r, ms));
test('export copies only game credentials and preserves quoted values', t => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'z-config-'));
  t.after(() => fs.rmSync(dir, {recursive:true, force:true}));
  fs.writeFileSync(path.join(dir, '.env.local'), 'DEEPSEEK_API_KEY="demo#value"\nFFMPEG_PATH=/private/host/path\nAWS_SECRET_ACCESS_KEY=unrelated\nVOLC_TTS_API_KEY=demo-voice # comment\n');
  assert.deepEqual(readSourceConfig(dir), {DEEPSEEK_API_KEY:'demo#value', VOLC_TTS_API_KEY:'demo-voice'});
  assert.throws(() => selectConfig({VOLC_TTS_API_KEY:'bad\nvalue'}));
});
test('runtime ignores host model, path, network exposure and team-login overrides', () => {
  const result = hostEnvironment({DEEPSEEK_API_KEY:'bundled-demo'}, {host:'/moved/app/host', tools:'/local/tools', runtime:'/local/data', session:'/local/run', memoryPort:12345, livePort:12346}, {HOME:'/user', Z_TEAM_MODEL_CONFIG:'/old/config', GAME_MODEL_API_KEY:'external', FFMPEG_PATH:'/old/binary', NODE_OPTIONS:'--import=untrusted', Z_LIVE_OFFICE:'1', HTTPS_PROXY:'untrusted'});
  assert.equal(result.GAME_MODEL_API_KEY, 'bundled-demo');
  assert.equal(result.Z_TEAM_MODEL_CONFIG, '/moved/app/host/Config/team-disabled.json');
  assert.equal(result.NODE_OPTIONS, undefined);
  assert.equal(result.Z_LIVE_OFFICE, undefined);
  assert.equal(result.HTTPS_PROXY, undefined);
  assert.ok(result.FFMPEG_PATH.startsWith('/moved/app/host/'));
});
function fixture(t) {
  const base = fs.mkdtempSync(path.join(os.tmpdir(), 'z-portable-'));
  t.after(() => fs.rmSync(base, {recursive:true, force:true}));
  const host = path.join(base, '移动后的 app/Contents/Resources/ProjectZHost');
  const data = path.join(base, 'fresh user');
  for (const dir of ['Config', 'Tools/MemoryLab', 'Tools/live-service/server']) fs.mkdirSync(path.join(host, dir), {recursive:true});
  for (const file of ['launch.mjs', 'config.mjs', 'service-runner.mjs']) fs.copyFileSync(path.join(here, file), path.join(host, file));
  fs.writeFileSync(path.join(host, 'manifest.json'), JSON.stringify({packageId:'test-release', sourceRevision:'test', playerExecutable:'unused'}));
  fs.writeFileSync(path.join(host, 'Config/providers.json'), JSON.stringify({DEEPSEEK_API_KEY:'demo'}));
  fs.writeFileSync(path.join(host, 'Tools/MemoryLab/server.mjs'), `import http from 'node:http'; import fs from 'node:fs'; fs.writeFileSync('scratch', 'writable'); http.createServer((q,r)=>{r.setHeader('Content-Type','application/json');r.end(JSON.stringify({configured:true}));}).listen(Number(process.env.Z_MEMORY_PORT),'127.0.0.1');`);
  fs.writeFileSync(path.join(host, 'Tools/live-service/server/index.mjs'), `import http from 'node:http'; import fs from 'node:fs'; const port=Number(process.env.Z_UNITY_PORT); http.createServer((q,r)=>{r.setHeader('Content-Type','application/json');r.end(JSON.stringify({ok:true}));}).listen(port,'127.0.0.1',()=>fs.writeFileSync(process.env.Z_LIVE_CONNECTION_FILE,JSON.stringify({baseUrl:'http://127.0.0.1:'+port,token:'test-token'})));`);
  function launch(mode = '--check') {
    const child = spawn(process.execPath, [path.join(host, 'launch.mjs'), mode], {env:{...process.env, Z_PORTABLE_DATA_DIR:data}, stdio:['ignore','pipe','pipe']});
    let output=''; for (const s of [child.stdout, child.stderr]) s.on('data', b => output += b);
    const done = once(child, 'exit').then(([code,signal]) => ({code,signal,output}));
    t.after(() => { if (child.exitCode === null && child.signalCode === null) child.kill('SIGTERM'); });
    return {child,done};
  }
  return {host,data,launch};
}
function alive(pid) { try { process.kill(pid, 0); return true; } catch { return false; } }
async function ready(data) {
  for (let i=0;i<100;i++) { try { return JSON.parse(fs.readFileSync(path.join(data, 'latest-run.json'))); } catch {} await delay(50); }
  throw Error('Fixture failed to start.');
}
test('relocated package uses writable private service copy and cleans up on exit', async t => {
  const f=fixture(t), run=f.launch();
  assert.equal((await run.done).code, 0);
  const state=await ready(f.data);
  assert.ok(state.services.every(pid=>!alive(pid)));
  assert.ok(!fs.existsSync(path.join(f.host, 'Tools/MemoryLab/scratch')));
  assert.ok(fs.existsSync(path.join(f.data, 'releases/test-release/Tools/MemoryLab/scratch')));
  assert.ok(!fs.existsSync(path.join(f.data,'launcher.lock')));
  assert.equal((await f.launch().done).code, 0, 'a second clean launch works');
});
test('concurrent launch preserves running game services; terminating launcher stops them', async t => {
  const f=fixture(t), first=f.launch('--serve'), state=await ready(f.data);
  assert.equal((await f.launch().done).code, 1);
  assert.ok(state.services.every(alive));
  first.child.kill('SIGTERM'); await first.done;
  assert.ok(state.services.every(pid=>!alive(pid)));
});
test('services terminate even when the launcher is killed unexpectedly', async t => {
  const f=fixture(t), first=f.launch('--serve'), state=await ready(f.data);
  first.child.kill('SIGKILL'); await first.done;
  for(let i=0;i<100&&state.services.some(alive);i++) await delay(30);
  assert.ok(state.services.every(pid=>!alive(pid)));
  assert.equal((await f.launch().done).code, 0, 'stale lock recovers without killing foreign processes');
});
