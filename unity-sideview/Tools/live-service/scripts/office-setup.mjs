import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import {fileURLToPath} from 'node:url';
import {randomBytes,X509Certificate} from 'node:crypto';
import {execFileSync} from 'node:child_process';
const root=fileURLToPath(new URL('../',import.meta.url)),dir=path.join(root,'.runtime/office');
const host=process.argv[2];
const local=Object.values(os.networkInterfaces()).flat().some(x=>x.address===host&&!x.internal);
if(!local||! /^(10\.|192\.168\.|172\.(1[6-9]|2\d|3[01])\.)/.test(host||''))throw Error('Pass this Mac’s private LAN IPv4 address.');
fs.mkdirSync(dir,{recursive:true,mode:0o700});
const configFile=path.join(dir,'host.json');
let config=fs.existsSync(configFile)?JSON.parse(fs.readFileSync(configFile)):null;
if(config&&config.host!==host)throw Error('Address changed: stop the service, archive .runtime/office, then regenerate and rebuild client packages.');
if(!config){
 execFileSync('openssl',['req','-x509','-newkey','rsa:2048','-nodes','-days','365','-subj','/CN=Z Office Live','-keyout',path.join(dir,'key.pem'),'-out',path.join(dir,'cert.pem')],{stdio:'ignore'});
 fs.chmodSync(path.join(dir,'key.pem'),0o600);
 config={host,port:4181,tokens:Object.fromEntries(Array.from({length:6},(_,i)=>['player-'+(i+1),randomBytes(32).toString('hex')]))};
 fs.writeFileSync(configFile,JSON.stringify(config,null,2),{mode:0o600});
}
const pin=new X509Certificate(fs.readFileSync(path.join(dir,'cert.pem'))).fingerprint256.replaceAll(':','').toLowerCase();
for(const [owner,token] of Object.entries(config.tokens))fs.writeFileSync(path.join(dir,owner+'.json'),JSON.stringify({baseUrl:`https://${host}:${config.port}`,token,certificateSha256:pin}),{mode:0o600});
console.log('Office configuration ready. Six separate client configs in .runtime/office; no supplier keys included.');
