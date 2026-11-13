export class AppError extends Error {
  constructor(message, status = 400) {
    super(message);
    this.status = status;
  }
}
export const PROVIDERS = {
  deepseek: {
    name: "DeepSeek",
    baseUrl: "https://api.deepseek.com",
    model: "deepseek-flash",
  },
  ark: {
    name: "火山方舟",
    baseUrl: "https://ark.cn-beijing.volces.com/api/v3",
    model: "",
  },
};
export const INTERNAL_ARK = {
  baseUrl: "https://ark-cn-beijing.bytedance.net/api/v3",
  model: "ep-20260624162511-sjh5k",
};
export const DEFAULT_CONFIG = {
  provider: "deepseek",
  baseUrl: PROVIDERS.deepseek.baseUrl,
  model: PROVIDERS.deepseek.model,
  apiType: "chat",
  temperature: 0.6,
  topK: 6,
  jsonMode: true,
  thinking: "disabled",
};

function migrateConfig(saved) {
  if (!saved.model && !saved.provider) return { ...DEFAULT_CONFIG };
  const provider =
    saved.provider ||
    (saved.baseUrl?.includes("api.deepseek.com") ? "deepseek" : "ark");
  return { ...DEFAULT_CONFIG, ...PROVIDERS[provider], ...saved, provider };
}
const credentialScope = (config) =>
  `${config.provider}:${new URL(config.baseUrl).origin}`;

export class ModelProvider {
  constructor(store, { allowTestEndpoint = false, fetchImpl = fetch } = {}) {
    this.store = store;
    this.fetch = fetchImpl;
    this.config = migrateConfig(store.settings());
    this.keys = new Map();
    if (process.env.DEEPSEEK_API_KEY) {
      this.config = {
        ...this.config,
        ...PROVIDERS.deepseek,
        provider: "deepseek",
        model: process.env.DEEPSEEK_MODEL || PROVIDERS.deepseek.model,
      };
      this.keys.set(credentialScope(this.config), process.env.DEEPSEEK_API_KEY);
    } else if (process.env.ARK_API_KEY) {
      this.config = {
        ...this.config,
        ...PROVIDERS.ark,
        provider: "ark",
        model:
          process.env.ARK_MODEL ||
          (this.config.provider === "ark" ? this.config.model : ""),
      };
      this.keys.set(credentialScope(this.config), process.env.ARK_API_KEY);
    }
    if (process.env.ARK_API_KEY && process.env.MEMORY_LAB_MODE === "team") {
      this.config = {
        ...this.config,
        provider: "ark",
        baseUrl: process.env.ARK_BASE_URL || INTERNAL_ARK.baseUrl,
        model: process.env.ARK_MODEL || INTERNAL_ARK.model,
        apiType: process.env.ARK_API_TYPE || "responses",
      };
      this.keys.set(credentialScope(this.config), process.env.ARK_API_KEY);
    }
    if (process.env.MEMORY_LAB_MODE === "team" && this.key)
      this.set({ ...this.config, apiKey: this.key });
    this.allowTestEndpoint = allowTestEndpoint;
    this.verified = false;
  }
  get key() {
    return this.keys.get(credentialScope(this.config)) || "";
  }
  get name() {
    return PROVIDERS[this.config.provider].name;
  }
  publicConfig() {
    return {
      ...this.config,
      providerName: this.name,
      hasKey: Boolean(this.key),
      configured: Boolean(this.key && this.config.model),
      verified: this.verified,
    };
  }
  set(input) {
    const provider = input.provider ?? this.config.provider;
    if (!Object.hasOwn(PROVIDERS, provider)) throw new AppError("服务商无效");
    const profile = PROVIDERS[provider];
    const baseUrl =
      input.baseUrl ??
      (provider === this.config.provider
        ? this.config.baseUrl
        : profile.baseUrl);
    let url;
    try {
      url = new URL(String(baseUrl));
    } catch {
      throw new AppError("Base URL 格式不正确");
    }
    const path = url.pathname.replace(/\/$/, "");
    const official =
      url.protocol === "https:" &&
      !url.port &&
      (provider === "ark"
        ? (/^ark\.[a-z0-9-]+\.volces\.com$/.test(url.hostname) ||
            url.hostname === "ark-cn-beijing.bytedance.net") &&
          path === "/api/v3"
        : url.hostname === "api.deepseek.com" && ["", "/v1"].includes(path));
    const localTest =
      this.allowTestEndpoint &&
      url.protocol === "http:" &&
      url.hostname === "127.0.0.1";
    if (
      (!official && !localTest) ||
      url.username ||
      url.password ||
      url.search ||
      url.hash
    )
      throw new AppError(`请使用${profile.name}官方 HTTPS 接口地址`);
    const model = String(
      input.model ??
        (provider === this.config.provider ? this.config.model : profile.model),
    ).trim();
    if (!model || model.length > 160 || !/^[a-zA-Z0-9._-]+$/.test(model))
      throw new AppError(
        provider === "ark"
          ? "请填写方舟 Model ID 或 ep- 开头的接入点 ID"
          : "请填写 DeepSeek 模型 ID",
      );
    const temperature = Number(input.temperature ?? this.config.temperature);
    const topK = Number(input.topK ?? this.config.topK);
    if (!Number.isFinite(temperature) || temperature < 0 || temperature > 1)
      throw new AppError("温度应在 0 到 1 之间");
    if (!Number.isInteger(topK) || topK < 1 || topK > 12)
      throw new AppError("召回条数应在 1 到 12 之间");
    const thinking = input.thinking ?? this.config.thinking;
    if (!["disabled", "default"].includes(thinking))
      throw new AppError("思考模式无效");
    const jsonMode = input.jsonMode ?? this.config.jsonMode;
    if (typeof jsonMode !== "boolean") throw new AppError("JSON 输出设置无效");
    const apiType =
      input.apiType ??
      (url.hostname === "ark-cn-beijing.bytedance.net"
        ? "responses"
        : provider === this.config.provider
          ? this.config.apiType
          : "chat");
    if (
      !["chat", "responses"].includes(apiType) ||
      (apiType === "responses" && provider !== "ark")
    )
      throw new AppError("此服务商不支持所选接口类型");
    const nextConfig = {
      provider,
      baseUrl: url.href.replace(/\/$/, ""),
      model,
      apiType,
      temperature,
      topK,
      thinking,
      jsonMode,
    };
    const scope = credentialScope(nextConfig),
      enteredKey = String(input.apiKey ?? "").trim();
    if (enteredKey && (enteredKey.length > 4096 || /\s/.test(enteredKey)))
      throw new AppError("API Key 格式不正确");
    const key = enteredKey || this.keys.get(scope);
    if (!key) throw new AppError(`请填写${profile.name} API Key`);
    this.keys.set(scope, key);
    this.config = nextConfig;
    this.verified = false;
    this.store.saveSettings(this.config);
    return this.publicConfig();
  }
  clearKey() {
    this.keys.delete(credentialScope(this.config));
    this.verified = false;
  }
  request(messages, { json = false, stream = false, maxTokens = 2048 } = {}) {
    if (this.config.apiType === "responses") {
      const body = {
        model: this.config.model,
        input: messages.map((m) => ({ role: m.role, content: m.content })),
        temperature: json ? 0.1 : this.config.temperature,
        max_output_tokens: maxTokens,
        stream,
        store: false,
      };
      if (this.config.thinking === "disabled")
        body.thinking = { type: "disabled" };
      if (json && this.config.jsonMode)
        body.text = { format: { type: "json_object" } };
      return body;
    }
    const body = {
      model: this.config.model,
      messages,
      temperature: json ? 0.1 : this.config.temperature,
      max_tokens: maxTokens,
      stream,
    };
    if (this.config.thinking === "disabled")
      body.thinking = { type: "disabled" };
    if (json && this.config.jsonMode)
      body.response_format = { type: "json_object" };
    if (stream && this.config.provider === "deepseek")
      body.stream_options = { include_usage: true };
    return body;
  }
  async call(body, { signal, onDelta } = {}) {
    if (!this.key || !this.config.model)
      throw new AppError(`请先连接${this.name}模型`, 422);
    const responses = this.config.apiType === "responses";
    const key = this.key,
      name = this.name,
      endpoint = `${this.config.baseUrl}/${responses ? "responses" : "chat/completions"}`;
    const combined = signal
      ? AbortSignal.any([signal, AbortSignal.timeout(120000)])
      : AbortSignal.timeout(120000);
    let response;
    try {
      response = await this.fetch(endpoint, {
        method: "POST",
        redirect: "error",
        signal: combined,
        headers: {
          Authorization: `Bearer ${key}`,
          "Content-Type": "application/json",
        },
        body: JSON.stringify(body),
      });
    } catch (error) {
      if (signal?.aborted) throw new AppError("操作已取消", 409);
      throw new AppError(
        combined.aborted
          ? `${name}请求超时，请重试或关闭深度思考`
          : `无法连接${name}，请检查网络和 Base URL`,
        502,
      );
    }
    if (!response.ok) {
      const errors = {
        401: "API Key 无效或已过期",
        402: `${name}余额不足，请检查账户余额`,
        403: "当前密钥无权调用此模型",
        404: "模型或接入点不存在，请核对 Model ID",
        429: `${name}请求限流，请稍后重试`,
        503: `${name}服务繁忙，请稍后重试`,
      };
      // Never reflect provider response bodies, which may contain request data or credentials.
      throw new AppError(
        errors[response.status] ||
          `${name}返回 HTTP ${response.status}，请核对模型、JSON 输出及思考模式设置`,
        502,
      );
    }
    let text = "",
      usage = null,
      finishReason = null;
    let completed = false;
    const responseText = (data) =>
      (Array.isArray(data.output) ? data.output : [])
        .filter((item) => item.type === "message")
        .flatMap((item) => (Array.isArray(item.content) ? item.content : []))
        .filter(
          (part) =>
            part.type === "output_text" && typeof part.text === "string",
        )
        .map((part) => part.text)
        .join("");
    function acceptResponse(data) {
      if (data.error || data.status === "failed")
        throw new AppError(`${name}生成失败，请核对模型和接口设置`, 502);
      if (data.status === "incomplete")
        throw new AppError(
          "模型回复未完成，请关闭深度思考或缩短输入后重试",
          502,
        );
      if (data.status !== "completed")
        throw new AppError("模型连接中断，回复未完成，请重试", 502);
      completed = true;
      usage = data.usage || usage;
      finishReason = "stop";
      const finalText = responseText(data);
      if (!text && finalText) {
        text = finalText;
        onDelta?.(finalText);
      } else if (finalText && finalText !== text)
        throw new AppError("流式回复与最终内容不一致，请重试", 502);
    }
    if (
      body.stream &&
      response.headers.get("content-type")?.includes("text/event-stream")
    ) {
      const reader = response.body.getReader(),
        decoder = new TextDecoder();
      let pending = "";
      function accept(line) {
        if (!line.startsWith("data:")) return;
        const data = line.slice(5).trim();
        if (!data || data === "[DONE]") return;
        let chunk;
        try {
          chunk = JSON.parse(data);
        } catch {
          throw new AppError(`${name}流式响应格式异常`, 502);
        }
        if (chunk.error)
          throw new AppError(`${name}在生成过程中返回错误，请重试`, 502);
        if (responses) {
          if (
            ["error", "response.failed", "response.incomplete"].includes(
              chunk.type,
            )
          )
            throw new AppError(
              "模型回复未完成，请核对接口设置或关闭深度思考后重试",
              502,
            );
          if (
            chunk.type === "response.output_text.delta" &&
            typeof chunk.delta === "string"
          ) {
            text += chunk.delta;
            onDelta?.(chunk.delta);
          }
          if (chunk.type === "response.completed")
            acceptResponse(chunk.response || {});
          return;
        }
        if (chunk.usage) usage = chunk.usage;
        const c = chunk.choices?.[0];
        if (c?.finish_reason) finishReason = c.finish_reason;
        const delta = c?.delta?.content;
        if (typeof delta === "string") {
          text += delta;
          onDelta?.(delta);
        }
      }
      try {
        while (true) {
          let part;
          try {
            part = await reader.read();
          } catch {
            throw new AppError(
              signal?.aborted
                ? "操作已取消"
                : combined.aborted
                  ? `${name}响应超时，请重试或关闭深度思考`
                  : `${name}连接中断，请重试`,
              signal?.aborted ? 409 : 502,
            );
          }
          const { done, value } = part;
          if (done) break;
          pending += decoder.decode(value, { stream: true });
          const lines = pending.split("\n");
          pending = lines.pop();
          lines.forEach((l) => accept(l.replace(/\r$/, "")));
          if (text.length > 40000 || pending.length > 1000000)
            throw new AppError("模型响应超过 Demo 限制", 502);
        }
        pending += decoder.decode();
        if (pending.trim()) accept(pending);
      } finally {
        reader.releaseLock();
      }
    } else {
      let data;
      try {
        data = await response.json();
      } catch {
        throw new AppError(`${name}响应不是合法 JSON`, 502);
      }
      if (responses) acceptResponse(data);
      else {
        text = data.choices?.[0]?.message?.content || "";
        usage = data.usage || null;
        finishReason = data.choices?.[0]?.finish_reason;
        if (typeof text === "string") onDelta?.(text);
      }
    }
    if (responses && !completed)
      throw new AppError("模型连接中断，回复未完成，请重试", 502);
    if (typeof text !== "string" || !text.trim())
      throw new AppError("模型没有返回文本，请检查模型类型和思考模式", 502);
    if (finishReason === "length")
      throw new AppError(
        "模型输出被长度上限截断，请关闭深度思考或缩短输入后重试",
        502,
      );
    if (finishReason && finishReason !== "stop")
      throw new AppError(
        `${name}未完成回复（${["content_filter", "tool_calls", "insufficient_system_resource", "aborted"].includes(finishReason) ? finishReason : "生成中断"}），请重试`,
        502,
      );
    this.verified = true;
    return { text, usage, finishReason };
  }
}
