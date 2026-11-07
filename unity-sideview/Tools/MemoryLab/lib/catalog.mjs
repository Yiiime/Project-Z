import { readFileSync } from "node:fs";
import { resolve, dirname, sep } from "node:path";
import { fileURLToPath } from "node:url";
export const PROJECT = resolve(
  dirname(fileURLToPath(import.meta.url)),
  "../content/current",
);
export const clone = (v) => structuredClone(v);
export function readJson(path) {
  return JSON.parse(readFileSync(path, "utf8"));
}
export class Catalog {
  constructor(root = PROJECT) {
    this.root = resolve(root);
    this.registry = readJson(this.path("actor-registry.json"));
    this.map = readJson(this.path(this.registry.public_map));
    this.contract = readJson(this.path(this.registry.contract_schema));
    this.actors = new Map(this.registry.actors.map((a) => [a.actor_id, a]));
    this.tasks = readJson(this.path(this.registry.task_catalog)).tasks;
    this.graph = Object.fromEntries(
      Object.keys(this.map.places).map((p) => [p, []]),
    );
    for (const [a, b] of this.map.walking_edges) {
      this.graph[a].push(b);
      this.graph[b].push(a);
    }
    this.prompts = new Map();
  }
  path(relative) {
    const p = resolve(this.root, relative);
    if (!p.startsWith(this.root + sep)) throw Error("资源路径超出项目");
    return p;
  }
  actor(id) {
    const a = this.actors.get(id);
    if (!a) throw Error("未知角色");
    return a;
  }
  place(id) {
    return {
      id,
      ...(this.map.places[id] || { name: id, description: "宿主提供的地点" }),
    };
  }
  identity(id) {
    return {
      id,
      ...this.map.public_actors[id],
      ...Object.fromEntries(
        ["workplace", "home", "usual_place"].map((k) => [
          k + "_name",
          this.map.public_actors[id][k]
            ? this.place(this.map.public_actors[id][k]).name
            : null,
        ]),
      ),
    };
  }
  sharedWorld() {
    return [
      "# 岛上公开身份与地理（开局就知道）",
      ...this.registry.actors.map((a) => {
        const p = this.identity(a.actor_id);
        return `${p.name}：${p.occupation}。${p.workplace_name ? "工作地点：" + p.workplace_name + "。" : ""}${p.home_name ? "住在" + p.home_name + "。" : ""}${p.public_note}`;
      }),
      ...this.map.layout,
      this.map.navigation_boundary,
      "常驻地点不是实时行踪；公开资料不包括别人幕后身份、私聊或当前计划。",
    ].join("\n");
  }
  profile(id) {
    return readFileSync(this.path(this.actor(id).profile), "utf8");
  }
  prompt(id) {
    if (!this.prompts.has(id))
      this.prompts.set(
        id,
        [
          ...this.registry.common_documents.map((p) =>
            readFileSync(this.path(p), "utf8"),
          ),
          readFileSync(this.path(this.actor(id).profile), "utf8"),
          this.sharedWorld(),
        ].join("\n\n"),
      );
    return this.prompts.get(id);
  }
  route(from, to, graph = this.graph) {
    if (from === to) return [from];
    const q = [[from]],
      seen = new Set([from]);
    while (q.length) {
      const path = q.shift();
      for (const next of graph[path.at(-1)] || []) {
        if (seen.has(next)) continue;
        const route = [...path, next];
        if (next === to) return route;
        seen.add(next);
        q.push(route);
      }
    }
    return null;
  }
  mapContext(scene, query = "", graph = this.graph) {
    return {
      current_place: this.place(scene),
      exits: (graph[scene] || []).map((x) => this.place(x)),
      asked_routes: Object.entries(this.map.places)
        .filter(([, p]) =>
          [p.name, ...(p.aliases || [])].some((a) =>
            query.toLowerCase().includes(a.toLowerCase()),
          ),
        )
        .slice(0, 6)
        .map(([destination, p]) => {
          const path = this.route(scene, destination, graph);
          return {
            destination,
            destination_name: p.name,
            status: path ? "known_route" : "host_navigation_required",
            path: path || [],
            via: (path || []).map((x) => this.place(x).name),
          };
        }),
      boundary: "路线候选不是已走到，入口、门禁和临时封路由引擎确认。",
    };
  }
}
