// This file runs beside the packaged .app and its MemoryLab directory.
import { spawn, execFileSync } from "node:child_process";
import { mkdirSync, mkdtempSync } from "node:fs";
import { homedir, tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { createServer } from "./MemoryLab/server.mjs";

const root = dirname(fileURLToPath(import.meta.url));
const bundle = join(root, "ProjectZSevenMemory.app", "Contents");
const executable = execFileSync("/usr/libexec/PlistBuddy",
  ["-c", "Print :CFBundleExecutable", join(bundle, "Info.plist")], {encoding: "utf8"}).trim();
const args = process.argv.slice(2);
const duo = args.includes("-litingZPortraitPreview");
const session = mkdtempSync(join(tmpdir(), duo ? "project-liting-z-dialogue-" : "project-z-dialogue-"));
const saveRoot = process.env.Z_MEMORY_SAVE_ROOT || join(session, "save");
mkdirSync(saveRoot, {recursive: true});
const configPath = process.env.Z_MEMORY_CONFIG ||
  join(homedir(), "Library", "Application Support", "Project Z", "MemoryLab", "model.json");
const service = createServer({dbPath: join(session, "memory.sqlite"), configPath});
await new Promise((resolve, reject) => {
  service.server.once("error", reject);
  service.server.listen(0, "127.0.0.1", resolve);
});
const url = "http://127.0.0.1:" + service.server.address().port;
if (!service.status().configured) {
  console.log("首次试玩请先在浏览器配置聊天模型：" + url + "/settings");
  spawn("/usr/bin/open", [url + "/settings"], {stdio: "ignore"}).unref();
}
const previewArgs = duo ? [] : ["-zPortraitPreview"];
const player = spawn(join(bundle, "MacOS", executable), [...previewArgs, "-memoryService", url,
  "-screen-fullscreen", "0", "-screen-width", "1440", "-screen-height", "900",
  "-logFile", join(session, "player.log"), ...args],
  {env: {...process.env, Z_MEMORY_SAVE_ROOT: saveRoot}, stdio: "inherit"});
console.log("试玩日志：" + join(session, "player.log"));
for (const signal of ["SIGHUP", "SIGINT", "SIGTERM"]) {
  process.once(signal, () => player.kill("SIGTERM"));
}
const result = await new Promise((resolve, reject) => {
  player.once("error", reject);
  player.once("exit", (code, signal) => resolve(code ?? (signal ? 1 : 0)));
});
service.server.close();
process.exit(result);
