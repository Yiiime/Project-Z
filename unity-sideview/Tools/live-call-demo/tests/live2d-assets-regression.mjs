import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import {buildLive2DAssets} from '../scripts/live2d-assets.mjs';

const root=await fs.mkdtemp(path.join(os.tmpdir(),'z-live2d-assets-'));
const directory=path.join(root,'ui/live2d');
try {
  assert.deepEqual(await buildLive2DAssets(root),{assets:{},total:0});
  await fs.mkdir(path.join(directory,'models/Haru'),{recursive:true});
  await fs.mkdir(path.join(directory,'licenses'),{recursive:true});
  const binary=Buffer.from([0,255,127,128,4]);
  await fs.writeFile(path.join(directory,'models/Haru/Haru.moc3'),binary);
  await fs.writeFile(path.join(directory,'models/Haru/Haru.model3.json'),'{}');
  await fs.writeFile(path.join(directory,'licenses/terms.html'),'<script>never execute</script>');
  await fs.writeFile(path.join(root,'.env.local'),'SECRET_MUST_NOT_BE_EXPOSED');
  const {assets,total}=await buildLive2DAssets(root);
  assert.equal(Object.keys(assets).length,3);
  assert.equal(total,37);
  const source=(await fs.readFile(new URL('../worker/public-assets.js',import.meta.url),'utf8')).replace('export function','function');
  const serve=new Function('PUBLIC_ASSETS',source+'\nreturn servePublicAsset;')(assets);
  const url='https://demo.invalid/live2d/models/Haru/Haru.moc3';
  let response=serve(new Request(url),new URL(url).pathname);
  assert.equal(response.status,200);
  assert.deepEqual(Buffer.from(await response.arrayBuffer()),binary);
  assert.equal(response.headers.get('content-type'),'application/octet-stream');
  assert.equal(response.headers.get('x-content-type-options'),'nosniff');
  assert.equal(response.headers.get('content-length'),'5');
  assert.equal(response.headers.get('cache-control'),'private, no-cache');
  const etag=response.headers.get('etag');
  response=serve(new Request(url,{method:'HEAD'}),new URL(url).pathname);
  assert.equal(response.status,200);assert.equal(await response.text(),'');
  response=serve(new Request(url,{headers:{'If-None-Match':etag}}),new URL(url).pathname);
  assert.equal(response.status,304);assert.equal(await response.text(),'');
  response=serve(new Request(url,{method:'POST'}),new URL(url).pathname);
  assert.equal(response.status,405);assert.equal(response.headers.get('allow'),'GET, HEAD');
  for(const missing of ['/live2d/.env.local','/live2d/../../.env.local','toString','__proto__','/live2d/missing.png']){
    assert.equal(serve(new Request(url),missing).status,404);
  }
  assert.match(serve(new Request(url),'/live2d/licenses/terms.html').headers.get('content-type'),/^text\/plain/);
  for(const invalid of ['.secret.json','notes.env','bad name.json']){
    await fs.writeFile(path.join(directory,invalid),'test');
    await assert.rejects(buildLive2DAssets(root));
    await fs.unlink(path.join(directory,invalid));
  }
  await fs.symlink(path.join(root,'.env.local'),path.join(directory,'symlink.json'));
  await assert.rejects(buildLive2DAssets(root),/symlinks/);
  await fs.unlink(path.join(directory,'symlink.json'));
  await fs.writeFile(path.join(directory,'large.png'),Buffer.alloc(8*1024*1024+1));
  await assert.rejects(buildLive2DAssets(root),/budget/);
  console.log('Live2D assets: binary GET, HEAD, conditional cache, method restriction, private-file isolation, filename/symlink/type and size limits PASS');
} finally { await fs.rm(root,{recursive:true,force:true}); }
