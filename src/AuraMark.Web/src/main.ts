import './style.css';
import { Editor, rootCtx, defaultValueCtx } from '@milkdown/core';
import { commonmark } from '@milkdown/preset-commonmark';
import { nord } from '@milkdown/theme-nord';
import { listener, listenerCtx } from '@milkdown/plugin-listener';
import { sendToHost, onHostMessage, WebMessagePayload } from './ipc';

const host = document.getElementById('app')!;
const shell = document.createElement('div');
shell.className = 'editor-shell';
host.appendChild(shell);

let initialized = false;

const makeEditor = async (markdown: string) => {
  shell.innerHTML = '';
  const ed = await Editor.make()
    .config((ctx) => {
      ctx.set(rootCtx, shell);
      ctx.set(defaultValueCtx, markdown);
    })
    .use(nord)
    .use(commonmark)
    .use(listener)
    .create();

  const l = ed.ctx.get(listenerCtx);
  l.markdownUpdated((_ctx, md) => {
    sendToHost({ type: 'Update', content: md, timestamp: Date.now() });
  });

  return ed;
};

// Boot editor quickly with placeholder
makeEditor('# AuraMark\n\nLoading...');

// Init from host
onHostMessage((payload: WebMessagePayload) => {
  if (payload.type === 'Init' && !initialized) {
    initialized = true;
    makeEditor(payload.content || '');
  }
});
