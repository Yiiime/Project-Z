// Compatibility view for authenticated local diagnostics; one owner opens the DB.
export class LiveSongLibrary {
 constructor({singing}){this.singing=singing;this.library=singing.library;}
 async lookup(query){this.singing.syncLibrary();const result=await this.library.lookup(query);return {...result,states:result.candidates.map(c=>this.library.state(c.id,query.segment||{kind:'chorus',ordinal:1},this.singing.read().voice))};}
 close(){} // SingingService closes its own library.
}
