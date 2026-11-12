let session;
export async function status() {
  session = await fetch('/api/status').then(r => r.json());
  if (session.error) throw new Error(session.error);
  return session;
}
export async function api(path, options = {}) {
  session ||= await status();
  const response = await fetch(path, { ...options, headers: { 'Content-Type': 'application/json', 'X-Ending-Token': session.token, ...options.headers } });
  const result = await response.json();
  if (!response.ok) throw new Error(result.error || '操作未完成');
  return result;
}
export const $ = id => document.getElementById(id);
export function node(tag, text, className) { const el = document.createElement(tag); if (text != null) el.textContent = text; if (className) el.className = className; return el; }
export function download(value, name) {
  const url = URL.createObjectURL(new Blob([JSON.stringify(value, null, 2)], { type: 'application/json' }));
  const a = node('a'); a.href = url; a.download = name; a.click(); setTimeout(() => URL.revokeObjectURL(url), 1000);
}
