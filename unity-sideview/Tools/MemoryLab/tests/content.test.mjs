import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { Catalog } from "../lib/catalog.mjs";
const resource=name=>JSON.parse(readFileSync(new URL(`../../../Assets/Seabreeze/Resources/Memory/${name}.json`,import.meta.url)));
test("Unity navigation anchors have unique identities and are real Skill map places",()=>{
  const {anchors}=resource("locations"),catalog=new Catalog();
  assert.equal(new Set(anchors.map(a=>a.id)).size,anchors.length,"Duplicate anchors produce inconsistent position bindings");
  const map=readFileSync(new URL('../../../Assets/Seabreeze/Resources/Content/IslandMap.asset',import.meta.url),'utf8');
  const scenes=new Set([...map.matchAll(/mapId: (\w+)/g)].map(m=>m[1]));
  for(const a of anchors){assert(catalog.map.places[a.id],a.id);assert(scenes.has(a.place),a.place);assert(Number.isFinite(a.x));}
  assert.equal(anchors.find(a=>a.id==='dock').place,'Harbor');
  assert.equal(anchors.find(a=>a.id==='flower_shop').place,'FlowerShop');
});
test("Unity task UI uses exactly the backend task definitions",()=>{
  const c=new Catalog();
  assert.deepEqual(resource("task-catalog").tasks,c.tasks);
});
