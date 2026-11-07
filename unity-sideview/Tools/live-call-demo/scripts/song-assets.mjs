import fs from 'node:fs/promises';
import path from 'node:path';
import {createHash} from 'node:crypto';
import {TALENT_CATALOG,playableSongs} from '../ui/talent-config.js';

export async function buildSongAssets(root,catalog=TALENT_CATALOG){
  if(!Array.isArray(catalog.songs)||catalog.songs.length>30)throw Error('Song catalog limit exceeded');
  const assets={},ids=new Set();let total=0;
  for(const song of catalog.songs){
    if(typeof song.id!=='string'||!/^[a-zA-Z0-9_-]{1,80}$/.test(song.id)||ids.has(song.id))throw Error('Invalid or duplicate song id');
    ids.add(song.id);
    if(!playableSongs(catalog).includes(song))throw Error('Song must have approved voice mapping and review: '+song.id);
    for(const field of ['title','version','section','reviewRef'])if(typeof song[field]!=='string'||!song[field].trim()||song[field].length>120)throw Error('Missing song metadata: '+field);
    if(song.title.length+song.section.length>150)throw Error('Song title too long');
    const directory=path.join(root,'ui/audio/songs');
    const realDirectory=await fs.realpath(directory);
    if(realDirectory!==directory)throw Error('Song asset directory must not be a symlink');
    const filename=path.join(directory,path.basename(song.src));
    const stat=await fs.lstat(filename);if(!stat.isFile()||stat.isSymbolicLink()||stat.size>12*1024*1024)throw Error('Invalid song asset');
    const bytes=await fs.readFile(filename),hash=createHash('sha256').update(bytes).digest('hex');
    if(song.sha256!==hash)throw Error('Song changed since review: '+song.id);
    total+=bytes.length;if(total>32*1024*1024)throw Error('Song library asset budget exceeded');
    assets[song.src]={type:song.src.endsWith('.mp3')?'audio/mpeg':'audio/wav',body:bytes.toString('base64'),size:bytes.length,etag:'"'+hash+'"'};
  }
  return {assets,total};
}
