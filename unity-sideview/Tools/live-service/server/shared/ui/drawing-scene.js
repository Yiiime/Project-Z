// Shared declarative vector format. Never accepts SVG/HTML, URLs or executable code.
export const DRAWING_LIMITS = {width:480,height:360,maxElements:64,maxCommands:24,maxStages:3};
const drawingFail=()=>{throw Error('invalid_drawing');};
const drawingKeys=(v,allowed)=>{if(!v||typeof v!=='object'||Array.isArray(v)||Object.keys(v).some(k=>!allowed.includes(k)))drawingFail();};
export function drawingText(v,max=600){if(typeof v!=='string'||!v.trim()||v.length>max||/[\u0000-\u001f\u007f<>]/.test(v))drawingFail();return v.trim();}
const drawingId=v=>typeof v==='string'&&/^[a-zA-Z][a-zA-Z0-9_-]{0,47}$/.test(v);
const drawingColor=v=>v==='none'||typeof v==='string'&&/^#[0-9a-fA-F]{6}$/.test(v);
function drawingElement(e,stages){
  drawingKeys(e,['id','label','stage','stroke','fill','strokeWidth','commands']);
  if(!drawingId(e.id)||!Number.isInteger(e.stage)||e.stage<0||e.stage>=stages||!drawingColor(e.stroke)||!drawingColor(e.fill)||e.stroke==='none'&&e.fill==='none'||!Number.isFinite(e.strokeWidth)||e.strokeWidth<1||e.strokeWidth>8) drawingFail();
  drawingText(e.label,60);
  if(!Array.isArray(e.commands)||!e.commands.length||e.commands.length>DRAWING_LIMITS.maxCommands||e.commands[0][0]!=='M')drawingFail();
  let visible=false;
  for(const c of e.commands){
    const lengths={M:3,L:3,Q:5,C:7,Z:1};
    if(!Array.isArray(c)||!Object.hasOwn(lengths,c[0])||c.length!==lengths[c[0]])drawingFail();
    for(let i=1;i<c.length;i++)if(!Number.isFinite(c[i])||c[i]<0||c[i]>(i%2?480:360))drawingFail();
    if(['L','Q','C'].includes(c[0]))visible=true;
  }
  if(!visible)drawingFail();
  return structuredClone(e);
}
export function validateDrawingScene(raw){
  drawingKeys(raw,['title','stages','elements']);drawingText(raw.title,100);
  if(!Array.isArray(raw.stages)||raw.stages.length<1||raw.stages.length>3||!Array.isArray(raw.elements)||!raw.elements.length||raw.elements.length>64)drawingFail();
  raw.stages.forEach(s=>drawingText(s,30));
  const elements=raw.elements.map(e=>drawingElement(e,raw.stages.length));
  if(new Set(elements.map(e=>e.id)).size!==elements.length)drawingFail();
  const result={title:raw.title,stages:[...raw.stages],elements};
  if(JSON.stringify(result).length>26000)drawingFail();
  return result;
}
export function applyDrawingPatch(base,patch,targetIds){
  base=validateDrawingScene(base);drawingKeys(patch,['upserts','removeIds','title']);if(patch.title!==undefined)drawingText(patch.title,100);
  const original=new Map(base.elements.map(e=>[e.id,e])),targets=new Set(targetIds);
  if(!Array.isArray(targetIds)||!targetIds.length||targetIds.length>16||targets.size!==targetIds.length||targetIds.some(id=>!original.has(id)))drawingFail();
  if(!Array.isArray(patch.upserts)||!Array.isArray(patch.removeIds)||patch.upserts.length>32||patch.removeIds.length>16||!patch.upserts.length&&!patch.removeIds.length)drawingFail();
  const upserts=patch.upserts.map(e=>drawingElement(e,base.stages.length)),ids=upserts.map(e=>e.id);
  if(new Set(ids).size!==ids.length||new Set(patch.removeIds).size!==patch.removeIds.length)drawingFail();
  for(const id of patch.removeIds)if(!targets.has(id)||ids.includes(id))drawingFail();
  for(const e of upserts)if(original.has(e.id)&&!targets.has(e.id))drawingFail();
  // Replace in-place to preserve stacking order. New objects are appended.
  const replacements=new Map(upserts.map(e=>[e.id,e]));
  const elements=base.elements.filter(e=>!patch.removeIds.includes(e.id)).map(e=>replacements.get(e.id)||e);
  elements.push(...upserts.filter(e=>!original.has(e.id)));
  if(JSON.stringify(elements)===JSON.stringify(base.elements))drawingFail();
  // Keep original stage indices and stacking order; edited scenes display once.
  const result={title:patch.title||base.title,stages:base.stages,elements};
  if(!elements.length||elements.length>64||JSON.stringify(result).length>26000)drawingFail();
  return result;
}
export function renderDrawingScene(scene,stage){
  // Both API replies and local edits pass validators before entering the renderer.
  if(!Number.isInteger(stage)||stage<0||stage>=scene.stages.length)drawingFail();
  const escape=v=>String(v).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&apos;'}[c]));
  const paths=scene.elements.filter(e=>e.stage<=stage).map(e=>`<path data-object-id="${escape(e.id)}" d="${e.commands.map(c=>c.join(' ')).join(' ')}" stroke="${e.stroke}" fill="${e.fill}" stroke-width="${e.strokeWidth}" stroke-linecap="round" stroke-linejoin="round"><title>${escape(e.label)}</title></path>`).join('');
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 480 360" role="img" aria-label="${escape(scene.title)}">${paths}</svg>`;
}
