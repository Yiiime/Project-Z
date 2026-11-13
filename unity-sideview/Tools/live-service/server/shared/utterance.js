import {EXPRESSION_CATALOG} from './expression-catalog.js';

export const DELIVERY_MAX_LENGTH = 160;
export function expressionKeys(actorId, activePortrait = null) {
  const actor = Object.hasOwn(EXPRESSION_CATALOG.actors, actorId) ? EXPRESSION_CATALOG.actors[actorId] : null;
  if (!actor) return [];
  return [...(actor.expressionsByActivePortrait?.[activePortrait] || actor.expressions)];
}
export function validExpression(actorId, value, activePortrait = null) {
  return typeof value === 'string' && expressionKeys(actorId, activePortrait).includes(value);
}
export function deliveryText(value) {
  if (typeof value !== 'string' || !value.trim() || value.length > DELIVERY_MAX_LENGTH || /[\u0000-\u001f\u007f]|<\/?[a-z][^>]*>|```/i.test(value)) throw Error('invalid_delivery');
  return value.trim();
}
// New model responses are strict. Only historical packets and authored local
// lines may omit all three fields; no aliases or dialogue-keyword inference.
export function utteranceMetadata(actorId, value, {allowLegacy = false, activePortrait = null} = {}) {
  if (!expressionKeys(actorId).length) throw Error('invalid_actor_id');
  const missing = ['actorId', 'delivery', 'expression'].every(key => value[key] === undefined);
  if (allowLegacy && missing) return {actorId, delivery:'自然平静，按日常交谈的语速说。', expression:'neutral'};
  if (value.actorId !== actorId) throw Error('actor_id_mismatch');
  if (!validExpression(actorId, value.expression, activePortrait)) throw Error('invalid_expression');
  return {actorId, delivery:deliveryText(value.delivery), expression:value.expression};
}

export function utterancePrompt(actorIds) {
  const actors = Object.fromEntries(actorIds.map(id => [id, expressionKeys(id)]));
  return `\n【台词表达协议】每条实际发言与台词在同一次生成中输出 actorId、delivery、expression。actorId 必须等于本次发言者的稳定ID，可用表情严格按角色：${JSON.stringify(actors)}。不输出别名、跨角色表情或模型内部动画阶段。expression 根据最终台词、本人情绪和上下文自主选择，neutral 是自然中性，不按关键词触发。delivery 是不超过160字的简短发声指导，描述说这句话时可听见的表达方式：根据人设、关系、已提供的记忆和当前对话自主决定情绪与语气，必要时加语速、音量、停顿。允许复杂或克制的情绪，也允许自然平静；不强制每句变化，不机械模仿对方情绪。delivery 只包含发声指导，不写剧情分析、推理过程、身体或纯视觉动作。speech/text 只包含实际说出口的内容，不写括号动作、语气标注或旁白。delivery、expression 与正文一致但不是彼此的机械映射，不把表情当成 info.emotion 或世界事实。原有内部 speech 字段就是对外的 text，不重复输出 text。没有发言的 wait/skip/inspect 不生成发声指导。`;
}
