// Only the fixed game protocol is allowed; query strings can never supply a destination.
export function gameReturnUrl(search) {
  const query = new URLSearchParams(search);
  const session = query.get('returnSession') || '';
  const matching = query.get('from') === 'unity' && /^[a-f0-9]{32}$/.test(session);
  return 'projectz-live://return' + (matching ? '?session=' + session : '');
}

export function mountGameReturn({window, button, shutdown, showNotice}) {
  let stopped = false;
  const url = gameReturnUrl(window.location.search);
  const requestReturn = () => {
    if (!stopped) { stopped = true; shutdown(); }
    showNotice(requestReturn);
    // Keep navigation in the click gesture so browsers can ask to open the installed game.
    try { window.location.href = url; } catch { /* The notice retains a retry and manual fallback. */ }
  };
  button.setAttribute('aria-label', '返回 Unity 游戏');
  const label = button.querySelector('.back-label');
  if (label) label.textContent = '返回游戏';
  button.onclick = requestReturn;
  return {requestReturn};
}
