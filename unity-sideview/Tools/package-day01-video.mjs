// Local iteration: reuse this user's existing packaged host, update tracked services,
// and isolate the new playtest's saves. Credentials never enter the repository.
import fs from 'node:fs';
import path from 'node:path';
import {execFileSync} from 'node:child_process';
import {randomUUID} from 'node:crypto';
import {fileURLToPath} from 'node:url';
const repo=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../..');
const [appArg,sourceArg]=process.argv.slice(2);
if(!appArg||!sourceArg)throw Error('Usage: node Tools/package-day01-video.mjs <built.app> <existing ProjectZHost>');
const app=fs.realpathSync(appArg),source=fs.realpathSync(sourceArg);
if(!app.endsWith('.app')||app.startsWith(repo+path.sep)||!fs.existsSync(path.join(source,'manifest.json')))throw Error('Invalid local package paths');
const host=path.join(app,'Contents/Resources/ProjectZHost');
fs.rmSync(host,{recursive:true,force:true});
fs.cpSync(source,host,{recursive:true,verbatimSymlinks:true});
const tracked=execFileSync('git',['ls-files','-z','unity-sideview/Tools/MemoryLab','unity-sideview/Tools/live-service','unity-sideview/Tools/team-model'],{cwd:repo,encoding:'utf8'}).split('\0').filter(Boolean);
for(const file of tracked){
 const relative=path.relative('unity-sideview/Tools',file);
 if(relative.split(path.sep).some(s=>s.startsWith('.')||['tests','test-results','verification','node_modules'].includes(s))||/\.(?:env|log|sqlite|key|pem|p12)$/.test(relative))continue;
 if(relative.startsWith('team-model/')&&!['client.mjs','common.mjs','adapter.mjs'].includes(path.basename(relative)))continue;
 const destination=path.join(host,'Tools',relative);fs.mkdirSync(path.dirname(destination),{recursive:true});fs.copyFileSync(path.join(repo,file),destination);
}
for(const name of ['launch.mjs','service-runner.mjs','config.mjs'])fs.copyFileSync(path.join(repo,'unity-sideview/Tools/PortableHost',name),path.join(host,name));
const plist=path.join(app,'Contents/Info.plist'),pb='/usr/libexec/PlistBuddy';
const player=execFileSync(pb,['-c','Print :CFBundleExecutable',plist],{encoding:'utf8'}).trim();
const manifest=JSON.parse(fs.readFileSync(path.join(host,'manifest.json')));
if(player!=='ProjectZLauncher')manifest.playerExecutable=player;
manifest.packageId=randomUUID();manifest.sourceRevision=execFileSync('git',['rev-parse','HEAD'],{cwd:repo,encoding:'utf8'}).trim();manifest.createdAt=new Date().toISOString();
fs.writeFileSync(path.join(host,'manifest.json'),JSON.stringify(manifest,null,2)+'\n',{mode:0o600});
const launcher=path.join(app,'Contents/MacOS/ProjectZLauncher');
fs.writeFileSync(launcher,'#!/bin/sh\nset -eu\nbase="$(CDPATH= cd -- "$(dirname -- "$0")/../Resources/ProjectZHost" && pwd)"\nexport Z_PORTABLE_DATA_DIR="${Z_PORTABLE_DATA_DIR:-$HOME/Library/Application Support/Project Z Day01 Video}"\nexec "$base/Runtime/node" "$base/launch.mjs" "$@"\n',{mode:0o755});fs.chmodSync(launcher,0o755);
execFileSync(pb,['-c','Set :CFBundleExecutable ProjectZLauncher',plist]);
execFileSync('/usr/bin/codesign',['--force','--deep','--sign','-',app],{stdio:'pipe'});
execFileSync('/usr/bin/codesign',['--verify','--deep','--strict',app],{stdio:'pipe'});
console.log('Local playtest packaged; source '+manifest.sourceRevision);
