/**
 * Unity 表演契约，供运行时 prompt 和测试共用。
 * 本模块只维护标签语义与组合规则；角色事实、记忆策略和剧情状态仍由运行时 Skill 负责。
 */

export const EXPRESSION_LABELS = Object.freeze([
  'neutral',
  'smile',
  'surprise',
  'angry',
  'troubled',
  'blush',
  'cry',
  'bawl',
  'sigh',
  'gloom',
]);

export const GESTURE_LABELS = Object.freeze(['none', 'wave', 'explain', 'point']);
export const GAZE_LABELS = Object.freeze(['player', 'away']);

export const PERFORMANCE_SELECTION_RULES = Object.freeze({
  expressions: Object.freeze({
    neutral: '没有明显情绪，或台词需要收束时使用。',
    smile: '真诚开心、温暖或鼓励时使用；普通礼貌回复不必强行微笑。',
    surprise: '出现意外、突然发现或明显惊讶时使用。',
    angry: '明确不满或边界被触碰时使用；不要为了推进对话强行发火。',
    troubled: '担忧、困惑、犹豫或一时拿不准时使用。',
    blush: '害羞、被夸后不好意思或暧昧窘迫时使用。',
    cry: '受伤或难过、出现克制哭泣和眼泪时使用。',
    bawl: '高强度、失控式大哭时使用；不要把普通难过升级成大哭。',
    sigh: '无奈、疲惫、释然或认命般叹气时使用；不是哭泣标签。',
    gloom: '阴沉、幻灭或冷淡的黑化反应时使用；不是普通悲伤或生气。',
  }),
  gestures: Object.freeze({
    none: '没有必要的手势时使用，默认优先选择。',
    wave: '打招呼、告别或轻快回应时使用。',
    explain: '解释、澄清或拆解观点时使用。',
    point: '需要指向当前提到的对象或方向时使用。',
  }),
  composition: Object.freeze([
    '每段最多选择一个 expression 和一个 gesture。',
    'expression 与 gesture 独立选择；任意合法表情都可以搭配任意合法手势。',
    '不要因为有台词就自动挥手；没有明确手势意图时使用 none。',
    '标签只表达当前台词的短时表现意图，不是持久情绪状态。',
  ]),
});

function formatRules(title, rules) {
  return [title, ...Object.entries(rules).map(([label, meaning]) => `- ${label}：${meaning}`)];
}

export const PERFORMANCE_SELECTION_RULES_TEXT = [
  ...PERFORMANCE_SELECTION_RULES.composition,
  ...formatRules('expression 选择语义：', PERFORMANCE_SELECTION_RULES.expressions),
  ...formatRules('gesture 选择语义：', PERFORMANCE_SELECTION_RULES.gestures),
].join('\n');

export function buildPerformanceSelectionPrompt() {
  return PERFORMANCE_SELECTION_RULES_TEXT;
}
