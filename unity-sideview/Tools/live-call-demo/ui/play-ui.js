import {cardBackArt, tarotArt} from './card-art.js';

export function mountPlayUI({engine, toast, showDialog, closeDialog, autonomous=false}) {
  const $ = name => document.getElementById(name);
  let lastTarot = '', lastDrawing = '', lastQueue = '', latest = engine.getState();
  const formShell = '<p class="dialog-eyebrow">Z’S LITTLE STUDIO</p>';
  function tarotDialog() {
    showDialog(formShell + '<h2 class="dialog-title">这一轮，谁来选牌？</h2><p class="dialog-description">谁提问，谁选牌。选定后锁定，翻开的牌会留在本轮记录里。</p><form id="tarot-form"><div class="play-mode"><label><input type="radio" name="origin" value="host" checked> 看 Z 自己选</label><label><input type="radio" name="origin" value="viewer"> 我来问一个</label></div><label class="play-field" id="question-field" hidden>你的问题<input class="dialog-input" id="tarot-question" maxlength="120" placeholder="例如：开始新计划时，我可以先关注什么？"></label><label class="play-field">本轮牌阵<select id="tarot-spread"><option value="single">单张 · 三选一，即选即翻</option><option value="three">三张 · 分三次选，选齐一起翻</option></select></label><p class="demo-note">本地演示牌组 · 仅正位 · 娱乐与自我思考</p><button class="dialog-primary" type="submit">开始这一轮 →</button></form><button class="dialog-secondary" id="tarot-history">查看过往牌桌</button>', () => {
      $('tarot-history').onclick = historyDialog;
      $('tarot-form').onchange = () => {
        const viewer = $('tarot-form').elements.origin.value === 'viewer';
        $('question-field').hidden = !viewer; $('tarot-question').required = viewer;
      };
      $('tarot-form').onsubmit = event => {
        event.preventDefault();
        const origin = $('tarot-form').elements.origin.value;
        if (engine.requestTarot({origin, question: $('tarot-question').value, spread: $('tarot-spread').value})) {
          closeDialog(); if (latest.play.queue.length) toast('已排队，当前互动结束后开始');
        } else toast('请填写问题，并先恢复直播');
      };
    });
  }
  function historyDialog() {
    showDialog(formShell + '<h2 class="dialog-title">过往牌桌</h2><div id="tarot-history-list"></div>', () => {
      const rounds = [...latest.play.history, ...(latest.play.tarot?.cards.length ? [latest.play.tarot] : [])].reverse();
      if (!rounds.length) { $('tarot-history-list').textContent = '还没有翻开的牌。'; return; }
      for (const round of rounds) {
        const section = document.createElement('section'); section.className = 'past-reading';
        const title = document.createElement('h3'), question = document.createElement('p');
        title.textContent = `${round.owner} · ${round.phase === 'cancelled' ? '已结束' : '本轮记录'}`;
        question.textContent = round.question; section.append(title, question);
        for (const card of round.cards) {
          const line = document.createElement('p'); line.textContent = `${card.position}：${card.title} · 正位。${card.meaning}`; section.append(line);
        }
        if (!round.cards.length) { const line = document.createElement('p'); line.textContent = '本轮未翻牌。'; section.append(line); }
        $('tarot-history-list').append(section);
      }
    });
  }
  function drawingDialog() {
    showDialog(formShell + '<h2 class="dialog-title">点一幅小小的画</h2><p class="dialog-description">先选一个演示主题，看看 Z 的画板如何随进度出现。画好后还能换一个小装饰。</p><form id="drawing-form"><label class="play-field">画什么<select id="drawing-subject"><option value="cat">一只小猫</option><option value="rabbit">一只小兔</option></select></label><label class="play-field">戴什么<select id="drawing-accessory"><option value="scarf">围巾</option><option value="bow">蝴蝶结</option></select></label><p class="demo-note">预制笔画回放 · 单色简笔画 · 暂支持这两种主题与装饰，后续接入更多绘画内容。</p><button class="dialog-primary" type="submit">请 Z 画一幅 →</button></form>', () => {
      $('drawing-form').onsubmit = event => {
        event.preventDefault();
        if (engine.requestDrawing({subject: $('drawing-subject').value, accessory: $('drawing-accessory').value})) {
          closeDialog(); if (latest.play.queue.length) toast('已排队，当前互动结束后开始');
        } else toast('请先恢复直播，再开始画画');
      };
    });
  }
  if(!autonomous){
  $('today-start').onclick = tarotDialog;
  $('drawing-start').onclick = drawingDialog;
  $('program').onclick = () => showDialog(formShell + '<h2 class="dialog-title">今晚，在小屋玩什么？</h2><p class="dialog-description">陪 Z 翻一张牌，亲手为自己的问题选牌，或看看一幅简笔画慢慢成形。</p><button class="dialog-primary" id="program-tarot">塔罗 · Z 自选 / 我来选</button><button class="dialog-secondary" id="program-drawing">绘画 · 看看 Z 的画板</button><button class="dialog-secondary" id="program-letter">月光来信</button>', () => {
    $('program-tarot').onclick = tarotDialog; $('program-drawing').onclick = drawingDialog;
    $('program-letter').onclick = () => {closeDialog(); $('lottery-trigger').click();};
  });
  }
  for (const kind of ['tarot', 'drawing']) {
    $(`${kind}-hide`).onclick = () => engine.setPanel(kind, false);
    $(`${kind}-chip`).onclick = () => {
      if (!engine.setPanel(kind, true)) toast('先完成当前玩法，再复看这份记录');
    };
    $(`${kind}-finish`).onclick = () => {
      if (latest.play.active === kind) engine.finishActivity();
      else engine.setPanel(kind, false);
    };
    $(`${kind}-cancel`).onclick = () => engine.finishActivity(true);
  }
  $('tarot-pause').onclick = () => engine.pauseTarot();
  $('tarot-feedback').onclick = () => {
    $('chat-input').placeholder = latest.play.tarot?.origin === 'host' ? '说说你对 Z 这个问题的想法……' : '补充你的情况，或告诉 Z 哪里不贴合……'; $('chat-input').focus();
  };
  $('drawing-edit').onclick = () => {
    if(latest.play.drawing.mode==='generated_vector'){
      showDialog(formShell+'<h2 class="dialog-title">想改哪里？</h2><form id="drawing-edit-form"><input class="dialog-input" id="drawing-edit-request" maxlength="110" required placeholder="例如：把围巾改成蓝色，其他保持不变"><button class="dialog-primary" type="submit">告诉 Z →</button></form>',()=>{
        $('drawing-edit-form').onsubmit=event=>{event.preventDefault();const text=$('drawing-edit-request').value.trim();if(text&&engine.sendMessage('修改这幅画：'+text))closeDialog();};
      });return;
    }
    if (!engine.editDrawing(latest.play.drawing.accessory === 'scarf' ? 'bow' : 'scarf')) toast('先完成当前玩法，再修改画作');
  };
  $('drawing-enlarge').onclick = () => {
    showDialog(formShell + '<h2 class="dialog-title" id="art-title"></h2><div class="art-large" id="art-large"></div><p class="demo-note">当前画板上的草图</p>', () => {
      $('art-title').textContent = latest.play.drawing.title;
      $('art-large').innerHTML = latest.play.drawing.svg;
    });
  };
  function render(state) {
    latest = state;
    const p = state.play, t = p.tarot, d = p.drawing;
    const busy = Boolean(state.lottery?.open);
    const tarotVisible = Boolean(t?.open && !busy && (!p.active || p.active === 'tarot'));
    const drawingVisible = Boolean(d?.open && !busy && (!p.active || p.active === 'drawing'));
    $('tarot-panel').hidden = !tarotVisible;
    $('drawing-panel').hidden = !drawingVisible;
    document.querySelector('.stage-column').classList.toggle('has-play', tarotVisible || drawingVisible);
    $('live-stage').classList.toggle('play-visible', tarotVisible || drawingVisible);
    $('tarot-chip').hidden = !t || tarotVisible;
    $('drawing-chip').hidden = !d || drawingVisible;
    if (t) $('tarot-chip').textContent = `☾ ${t.phase === 'completed' ? '本轮塔罗 · 复看' : t.phase === 'cancelled' ? '已结束的塔罗' : '回到选牌'}`;
    if (d) $('drawing-chip').textContent = `✎ ${['ready', 'completed'].includes(d.phase) ? '画好了 · 点开看看' : d.phase === 'cancelled' ? '已结束的画作' : d.phase==='failed'?'绘画未完成 · 查看':'正在画 · 查看进度'}`;
    const queueKey = JSON.stringify(p.queue);
    if (queueKey !== lastQueue) {
      lastQueue = queueKey; $('play-queue').replaceChildren();
      p.queue.forEach((item, index) => {
        const button = document.createElement('button');
        button.textContent = `${index + 1}. ${item.kind === 'tarot' ? '塔罗' : item.kind === 'drawing' ? '绘画' : '月光来信'} ×`;
        button.title = '移出等待队列'; button.onclick = () => engine.removeQueued(index); $('play-queue').append(button);
      });
    }
    if (t && tarotVisible) {
      $('tarot-owner').textContent = `${t.owner}的问题 · ${t.spread === 'three' ? '三张牌阵' : '单张牌阵'}`;
      $('tarot-question-text').textContent = t.question;
      const labels = {preparing: '准备牌桌', candidates_ready: '摆好牌背', awaiting_selection: t.origin === 'host' ? 'Z 正在选牌 · 你可以旁观' : '轮到你选牌', selection_locked: '已选定 · 不可改选', revealing: '正在翻牌', interpreting: 'Z 正在解读', completed: '解读完成 · 可以接着聊', cancelled: '本轮已结束'};
      $('tarot-status').textContent = t.paused ? '本轮暂停 · 选择会保留' : labels[t.phase];
      $('tarot-count').textContent = `${t.slots.length} / ${t.spread === 'three' ? 3 : 1}`;
      const key = JSON.stringify([t.id, t.phase, t.round, t.paused, state.paused, t.selected, t.cards, t.activeIndex]);
      if (key !== lastTarot) {
        lastTarot = key;
        $('tarot-slots').replaceChildren();
        const names = t.spread === 'three' ? ['当前处境', '留意因素', '可尝试行动'] : ['本次关注的角度'];
        names.forEach((name, index) => {
          const slot = document.createElement('span'); slot.className = t.slots[index] ? 'filled' : '';
          slot.textContent = `${t.slots[index] ? '✓' : '○'} ${name}`; $('tarot-slots').append(slot);
        });
        $('tarot-cards').replaceChildren();
        if (t.cards.length) t.cards.forEach((card, index) => {
          const button = document.createElement('button'); button.className = `play-card revealed${t.activeIndex === index ? ' reading' : ''}`;
          button.innerHTML = `<span class="play-card-art">${tarotArt(card.name)}</span><b></b><small></small>`;
          button.querySelector('b').textContent = `${card.title} · 正位`;
          button.querySelector('small').textContent = card.position;
          button.setAttribute('aria-label', `查看${card.position}：${card.title}正位`);
          button.onclick = () => {
            showDialog(formShell + '<h2 class="dialog-title" id="play-card-title"></h2><p class="dialog-description" id="play-card-meaning"></p><p class="result-advice" id="play-card-advice"></p>', () => {
              $('play-card-title').textContent = `${card.position} · ${card.title}`;
              $('play-card-meaning').textContent = card.meaning; $('play-card-advice').textContent = card.advice;
            });
          };
          $('tarot-cards').append(button);
        });
        else t.candidates.forEach((candidate, index) => {
          const button = document.createElement('button'); button.className = `play-card${t.selected === candidate.id ? ' selected' : ''}`;
          button.innerHTML = `<span class="play-card-art">${cardBackArt('fortune', 0)}</span><b>${'ABC'[index]}</b><small></small>`;
          button.querySelector('small').textContent = t.selected === candidate.id ? '✓ 已选定' : `${index === 1 ? '中间' : candidate.label + '边'}`;
          button.disabled = t.origin !== 'viewer' || t.phase !== 'awaiting_selection' || t.paused || state.paused;
          button.setAttribute('aria-label', `选择${'ABC'[index]}，${index === 1 ? '中间' : candidate.label + '边'}的牌`);
          button.onclick = () => {
            if (!engine.chooseTarot(t.id, t.round, candidate.id)) toast('这次选择已锁定或已过期');
          };
          $('tarot-cards').append(button);
        });
      }
      $('tarot-pause').hidden = t.origin !== 'viewer' || !['awaiting_selection', 'candidates_ready'].includes(t.phase);
      $('tarot-pause').textContent = t.paused ? '继续选牌' : '先暂停';
      $('tarot-finish').hidden = !['completed', 'cancelled'].includes(t.phase);
      $('tarot-cancel').hidden = p.active !== 'tarot' || ['completed', 'cancelled'].includes(t.phase);
      $('tarot-feedback').textContent = t.origin === 'host' ? '聊聊 Z 的问题' : '补充我的情况';
      $('tarot-feedback').hidden = !['completed', 'interpreting'].includes(t.phase) || p.active !== 'tarot';
      $('tarot-note').textContent = t.cards.length ? '点牌可复看 · 补充背景沿用原牌' : t.spread === 'three' ? '每轮三选一，选齐三张一起翻开' : '只翻选中的一张，其余牌不揭示';
      if (t.phase === 'candidates_ready') requestAnimationFrame(() => engine.candidatesDisplayed(t.id, t.round));
      if (t.phase === 'revealing' && $('tarot-cards').querySelectorAll('svg').length === t.cards.length)
        requestAnimationFrame(() => engine.tarotDisplayed(t.id));
    }
    if (d && drawingVisible) {
      $('drawing-title').textContent = d.title;
      const generated=d.mode==='generated_vector';
      $('drawing-owner').textContent = `来自 ${d.owner} · ${generated?'简笔草图':'绘制回放'}`;
      $('drawing-status').textContent = d.phase === 'editing' ? '修改中 · 暂留修改前画面' : d.phase === 'cancelled' ? '本次绘画已结束' : d.phase === 'ready' ? '完成图正在展示' : ['轮廓已画出', '正在补充特征', '已完成'][d.stage] || '正在构思';
      if(generated)$('drawing-status').textContent=d.phase==='failed'?d.reason:d.phase==='editing'?'正在修改 · 当前保留上一版':d.phase==='preparing'?'正在构思并生成画面':d.phase==='cancelled'?'本次绘画已结束':d.phase==='completed'?'草图已展示 · 可以告诉 Z 想调整的地方':`${d.stages[d.stage]||'准备画面'} · 已生成笔画回放`;
      const key = `${d.id}:${d.revision}:${d.stage}:${d.phase}`;
      if (key !== lastDrawing) {
        lastDrawing = key; $('drawing-canvas').innerHTML = d.svg || '<span>画板正在准备中</span>';
        if(generated){$('drawing-steps').replaceChildren();for(const [i,label] of d.stages.entries()){const step=document.createElement('span');step.textContent=`${i+1} ${label}`;$('drawing-steps').append(step);}}
      }
      for (const [index, el] of [...$('drawing-steps').children].entries()) {
        el.classList.toggle('done', d.stage >= index); el.classList.toggle('current', d.stage === index);
      }
      $('drawing-finish').hidden = !['completed', 'cancelled','failed'].includes(d.phase);
      $('drawing-cancel').hidden = p.active !== 'drawing' || ['completed', 'cancelled'].includes(d.phase);
      $('drawing-edit').hidden = generated?!d.scene||!['ready','drawing','completed','failed'].includes(d.phase):d.stage < 1 || d.phase === 'editing' || d.phase === 'cancelled';
      $('drawing-edit').textContent = generated?'修改这幅画':d.accessory === 'scarf' ? '换成蝴蝶结' : '换成围巾';
      $('drawing-enlarge').disabled = !d.svg;
      if (!['editing','failed','cancelled'].includes(d.phase) && $('drawing-canvas').querySelector('svg')) requestAnimationFrame(() => {
        const svg=$('drawing-canvas').querySelector('svg');if(!svg||$('drawing-panel').hidden||state.paused)return;
        const bounds=svg.getBBox();if(bounds.width>0||bounds.height>0)engine.drawingDisplayed(d.id, d.revision, d.stage);
      });
    }
  }
  engine.addEventListener('state', event => render(event.detail));
  render(latest);
}
