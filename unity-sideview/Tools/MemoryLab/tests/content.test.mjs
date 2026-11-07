import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { Catalog } from "../lib/catalog.mjs";
const resource=name=>JSON.parse(readFileSync(new URL(`../../../Assets/Seabreeze/Resources/Memory/${name}.json`,import.meta.url)));
test("Unity navigation anchors have unique identities and are real Skill map places",()=>{
  const {anchors}=resource("locations"),catalog=new Catalog();
  assert.equal(new Set(anchors.map(a=>a.id)).size,anchors.length,"Duplicate anchors produce inconsistent position bindings");
  for(const a of anchors){assert(catalog.map.places[a.id],a.id);assert(["Street","Cafe","Lighthouse"].includes(a.place));assert(Number.isFinite(a.x));}
});
test("Unity task UI uses exactly the backend task definitions",()=>{
  const c=new Catalog();
  assert.deepEqual(resource("task-catalog").tasks,c.tasks);
});
