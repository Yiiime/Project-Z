import { ModelProvider, AppError, DEFAULT_CONFIG } from "./provider.mjs";

export class TeamModelProvider extends ModelProvider {
  constructor(store, team) {
    super(store, { fetchImpl: team.modelFetch("npc", async () => { throw Error("Unexpected model endpoint"); }) });
    this.team = team;
    this.keys.clear();
    this.config = { ...DEFAULT_CONFIG, ...team.config.profiles.npc };
  }
  get key() { return this.team ? (this.team.session() ? "team-managed" : "") : super.key; }
  get name() { return this.team ? "团队模型" : super.name; }
  set(input) {
    if (!this.team) return super.set(input);
    throw new AppError("当前由团队统一提供模型，请使用 GitHub 登录", 403);
  }
  publicConfig() { return { ...super.publicConfig(), ...(this.team ? { team: this.team.publicStatus() } : {}) }; }
  async call(body, options) {
    try { return await super.call(body, options); }
    catch (error) {
      if (!this.team.session()) throw new AppError("请在模型连接页重新登录有项目权限的 GitHub 账号", 401);
      throw error;
    }
  }
}
