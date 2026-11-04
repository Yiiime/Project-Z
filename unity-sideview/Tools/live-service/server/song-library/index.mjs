import fs from 'node:fs';
import path from 'node:path';
import {SongLibrary} from './library.mjs';
import {CatalogSource} from './sources.mjs';

// The source reference and the generated performance have separate evidence.
export class LiveSongLibrary {
  constructor({root,directory,singing,catalogFile=process.env.Z_SONG_CATALOG}) {
    this.singing=singing;this.root=root;
    this.library=new SongLibrary({directory,sources:catalogFile?[new CatalogSource({id:'configured',file:catalogFile})]:[]});
  }
  sync() {
    const store=this.singing.read(),library=this.library;
    for(const asset of store.assets) {
      if(!asset.approved)continue;
      const p=asset.plan,s=p.segment;
      if(!['first_line','verse','chorus','bridge','full','time_range','lyric_start'].includes(s.kind))continue;
      const recording=library.register({title:p.title,artist:p.artist||'歌手待确认',version:p.version||'已验收唱段（参考录音待绑定）'});
      const provenance='singing-service:'+asset.id;
      if(library.db.prepare('SELECT id FROM assets WHERE provenance=?').get(provenance))continue;
      const segment=s.kind==='time_range'?{kind:s.kind,startMs:s.startMs,endMs:s.endMs}:s.kind==='lyric_start'?{kind:s.kind,text:s.lyricStart}:{kind:s.kind,ordinal:s.ordinal};
      const item=library.addAsset(recording.id,{file:path.join(this.singing.directory,'assets',asset.sha256+'.wav'),segment,voice:asset.voice,provenance});
      if(asset.review.source==='seed-review.json')library.importLegacyReview(item.id,JSON.parse(fs.readFileSync(path.join(this.root,'seed-review.json'),'utf8')));
      else library.reviewAsset(item.id,{approved:true,reviewer:asset.review.reviewer,note:asset.review.note,checks:asset.review.checks});
    }
  }
  async lookup(query) {
    this.sync();const result=await this.library.lookup(query);
    return {...result,states:result.candidates.map(candidate=>this.library.state(candidate.id,query.segment||{kind:'chorus',ordinal:1},this.singing.read().voice))};
  }
  close(){this.library.close();}
}
