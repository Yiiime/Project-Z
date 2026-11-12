import fs from 'node:fs';
import {fileURLToPath} from 'node:url';
import {createHash} from 'node:crypto';
import {Catalog} from '../../MemoryLab/lib/catalog.mjs';
const catalog = new Catalog();
const shared = fileURLToPath(new URL('../server/shared/', import.meta.url));
const publicWorld = catalog.sharedWorld();
const actors = [...catalog.actors.values()];
const profiles = Object.fromEntries(actors.map(a=>[a.actor_id,{
  profile:catalog.profile(a.actor_id),
  initial_relationships:catalog.registry.initial_relationships[a.actor_id]||{},
}]));
// Only public geography/occupations go in the module imported by UI.
const roster=actors.filter(a=>a.actor_id!=='z').map(a=>{
 const p=catalog.identity(a.actor_id);
 return {npc_id:a.actor_id,display_name:a.name,occupation:p.occupation,
  voice:'按本人语言习惯，简短自然地接话',motive:a.daily_goals.find(g=>g.id.endsWith('_social')).label,
  personal_detail:p.public_note,approaches:['接住现场某个人刚说的具体小事','从自己的日常或工作说一个相关看法']};
});
const marker='// Direct attention to different parts';
const current=fs.readFileSync(shared+'audience-roster.js','utf8');
const turn=current.slice(current.indexOf(marker)).replace(/const recipients=\{[^;]+;/,
 "const recipients={liyuan:'host',liting:'host',axing:'peer',zhengyi:'player',xiaoxiaoqing:'host',linxu:'player',zhou_bobo:'peer'};");
if(!turn.includes('export function speakingTurn'))throw Error('Missing compatible audience turn adapter');
const publicSource='// Generated PUBLIC roster from MemoryLab; never put private profiles here.\nexport const NPCS = '+JSON.stringify(roster,null,2)+';\n'+turn;
const world=fs.readFileSync(catalog.path('moonlife-actors/references/world.md'),'utf8');
const zPersona=[world,profiles.z.profile,publicWorld].join('\n\n');
const meta={id:'z',pack_version:catalog.registry.pack_version,persona_revision:catalog.registry.persona_revision,source:'MemoryLab/content/current',sha256:createHash('sha256').update(JSON.stringify({profiles,publicWorld})).digest('hex')};
const outputs={
 [shared+'audience-roster.js']:publicSource,
 [shared+'worker/island-roster.js']:'// Generated SERVER-ONLY material; one actor per model request.\nexport const ISLAND_PERSONAS = '+JSON.stringify(profiles,null,2)+';\nexport const ISLAND_PUBLIC_WORLD = '+JSON.stringify(publicWorld)+';\n',
 [shared+'worker/z-persona.js']:'// Generated from the current island persona. Run node scripts/build-character-content.mjs.\nexport const Z_PERSONA = '+JSON.stringify(zPersona)+';\nexport const Z_CURRENT_WORLD = Object.freeze({day:"engine_required",time:"engine_required",scene:"live_room"});\nexport const Z_PERSONA_META = Object.freeze('+JSON.stringify(meta)+');\n',
};
for(const [path,value] of Object.entries(outputs)){
 if(process.argv.includes('--check')){if(!fs.existsSync(path)||fs.readFileSync(path,'utf8')!==value)throw Error('Stale generated character file: '+path);}
 else fs.writeFileSync(path,value);
}
console.log('Current Z host and seven island viewers: '+(process.argv.includes('--check')?'verified':'generated'));
