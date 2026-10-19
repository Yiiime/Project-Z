extends RefCounted
## A browser-owned text field avoids Godot Web's hidden IME/contenteditable path.
## This source is a resource dependency, so Web exports need no external JS file.
const SOURCE := """
(() => {
  if (window.MoonZNativeInput) return;
  window.MoonZNativeInput = {
    create(id, callback) {
      const input = document.createElement('input');
      input.id = id;
      input.type = 'text';
      input.maxLength = 180;
      input.autocomplete = 'off';
      input.autocapitalize = 'off';
      input.spellcheck = false;
      input.setAttribute('aria-label', '弹幕输入');
      input.setAttribute('enterkeyhint', 'send');
      Object.assign(input.style, {
        position: 'fixed', display: 'none', zIndex: '1000', boxSizing: 'border-box',
        margin: '0', minWidth: '0', outline: 'none', border: '1px solid transparent',
        borderRadius: '4px', background: 'transparent', color: '#f2f3f5',
        fontFamily: '"Noto Sans SC", "PingFang SC", "Microsoft YaHei", sans-serif',
        fontWeight: '450', caretColor: '#c5d9ec'
      });
      document.body.appendChild(input);
      let composing = false, destroyed = false, geometry = null, compositionEnded = 0;
      const emit = kind => {
        if (!destroyed) callback(kind, input.value, composing);
      };
      input.addEventListener('compositionstart', () => { composing = true; emit('input'); });
      input.addEventListener('compositionend', () => {
        composing = false; compositionEnded = performance.now(); emit('input');
      });
      input.addEventListener('input', event => {
        if (event.isComposing) composing = true;
        emit('input');
      });
      input.addEventListener('blur', () => {
        input.style.borderColor = 'transparent';
        if (composing) { composing = false; emit('input'); }
      });
      input.addEventListener('focus', () => { input.style.borderColor = 'rgba(197,217,236,.45)'; });
      input.addEventListener('keydown', event => {
        event.stopPropagation();
        if (event.key === 'Enter') {
          if (composing || event.isComposing || event.keyCode === 229 || performance.now() - compositionEnded < 70) return;
          event.preventDefault();
          if (!event.repeat) emit('submit');
        } else if (event.key === 'Escape' && !composing && !event.isComposing) {
          event.preventDefault(); emit('escape');
        }
      });
      input.addEventListener('keyup', event => event.stopPropagation());
      const layout = () => {
        if (!geometry || destroyed) return;
        const canvas = document.getElementById('canvas') || document.querySelector('canvas');
        if (!canvas) { input.style.display = 'none'; return; }
        const r = canvas.getBoundingClientRect();
        const [x, y, w, h, vw, vh, visible, editable, placeholder] = geometry;
        const scale = Math.min(r.width / vw, r.height / vh);
        const left = r.left + (r.width - vw * scale) / 2 + x * scale;
        const top = r.top + (r.height - vh * scale) / 2 + y * scale;
        const shown = visible && scale > 0;
        if (!shown && document.activeElement === input) input.blur();
        input.style.display = shown ? 'block' : 'none';
        input.style.left = left + 'px'; input.style.top = top + 'px';
        input.style.width = (w * scale) + 'px'; input.style.height = (h * scale) + 'px';
        input.style.padding = '0 ' + (16 * scale) + 'px';
        input.style.fontSize = (17 * scale) + 'px';
        input.style.borderRadius = (4 * scale) + 'px';
        input.disabled = !editable;
        input.placeholder = placeholder;
      };
      window.addEventListener('resize', layout);
      window.addEventListener('scroll', layout, true);
      document.addEventListener('fullscreenchange', layout);
      return {
        update(x, y, w, h, vw, vh, visible, editable, placeholder) {
          geometry = [x, y, w, h, vw, vh, visible, editable, placeholder]; layout();
        },
        setValue(value) { if (!composing && input.value !== value) input.value = value; },
        focus() {
          if (input.style.display !== 'none' && !input.disabled) {
            input.focus({preventScroll: true}); input.setSelectionRange(input.value.length, input.value.length);
          }
        },
        destroy() {
          destroyed = true;
          window.removeEventListener('resize', layout);
          window.removeEventListener('scroll', layout, true);
          document.removeEventListener('fullscreenchange', layout);
          input.remove(); callback = null; geometry = null;
        }
      };
    }
  };
})();
"""

var _callback: JavaScriptObject
var _handle: JavaScriptObject


func mount(callback: Callable, id: String) -> bool:
	if not OS.has_feature("web"):
		return false
	JavaScriptBridge.eval(SOURCE, true)
	var factory := JavaScriptBridge.get_interface("MoonZNativeInput")
	if factory == null:
		return false
	_callback = JavaScriptBridge.create_callback(callback)
	_handle = factory.create(id, _callback)
	return _handle != null


func update(rect: Rect2, visible: bool, editable: bool, placeholder: String) -> void:
	if _handle != null:
		_handle.update(rect.position.x, rect.position.y, rect.size.x, rect.size.y, 1600.0, 900.0, visible, editable, placeholder)


func set_value(value: String) -> void:
	if _handle != null:
		_handle.setValue(value)


func focus() -> void:
	if _handle != null:
		_handle.focus()


func destroy() -> void:
	if _handle != null:
		_handle.destroy()
	_handle = null
	_callback = null
