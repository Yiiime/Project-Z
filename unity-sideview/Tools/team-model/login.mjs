import { spawn } from "node:child_process";
import { loadTeamClient } from "./client.mjs";

const team = loadTeamClient();
if (!team) { console.log("团队模型服务尚未启用。"); process.exit(0); }
if (process.argv.includes("--logout")) { await team.logout(); console.log("已退出团队模型。"); process.exit(0); }
if (team.session()) {
  try { const value = await team.verify(); console.log(`团队模型已连接：${value.user.login}`); process.exit(0); }
  catch (error) { if (![401, 403].includes(error.status)) { console.error(error.message); process.exit(1); } }
}
try {
  const login = await team.startLogin();
  console.log(`请用有项目权限的 GitHub 账号打开 ${login.verificationUri}\n一次性登录代码：${login.userCode}`);
  const command = process.platform === "darwin" ? "open" : process.platform === "win32" ? "explorer.exe" : "xdg-open";
  const child = spawn(command, [login.verificationUri], { stdio: "ignore" });
  child.on("error", () => {}); child.unref();
  let interval = login.interval;
  while (Date.now() < login.expires) {
    await new Promise(resolve => setTimeout(resolve, interval * 1000));
    const value = await team.pollLogin();
    if (!value.pending) { console.log(`团队模型已连接：${value.user.login}`); process.exit(0); }
    interval = value.interval || interval;
  }
  throw Error("登录已过期，请重新运行。");
} catch (error) { console.error(error.message); process.exitCode = 1; }
