import './style.css';
import 'prismjs/themes/prism.css';
import { Editor, rootCtx, defaultValueCtx } from '@milkdown/core';
import { commonmark } from '@milkdown/preset-commonmark';
import { gfm } from '@milkdown/preset-gfm';
import { nord } from '@milkdown/theme-nord';
import { listener, listenerCtx } from '@milkdown/plugin-listener';
import { prism } from '@milkdown/plugin-prism';
import { sendToHost, onHostMessage, WebMessagePayload, HostCommand } from './ipc';

const host = document.getElementById('app');
if (!host) {
  throw new Error('Missing #app root');
}

const shell = document.createElement('div');
shell.className = 'editor-shell';
host.appendChild(shell);

const statusBar = document.createElement('div');
statusBar.className = 'render-status';
statusBar.textContent = 'Loading...';
shell.appendChild(statusBar);

const stage = document.createElement('div');
stage.className = 'editor-stage';
shell.appendChild(stage);

const milkRoot = document.createElement('div');
milkRoot.className = 'milk-root';
stage.appendChild(milkRoot);

const sourceEditor = document.createElement('textarea');
sourceEditor.className = 'source-editor';
sourceEditor.spellcheck = false;
sourceEditor.setAttribute('aria-label', 'Markdown source');
stage.appendChild(sourceEditor);

let editor: Editor | null = null;
let initialized = false;
let sourceMode = false;
let inputFrozen = false;
let suppressOutbound = false;
let currentMarkdown = '';

const sendAck = (text: string) => {
  sendToHost({
    type: 'Ack',
    content: text,
    timestamp: Date.now(),
  });
};

const sendUpdate = (markdown: string) => {
  sendToHost({
    type: 'Update',
    content: markdown,
    timestamp: Date.now(),
  });
};

const setStatus = (text: string) => {
  statusBar.textContent = text;
};

const renderEditor = async (markdown: string) => {
  if (editor && typeof (editor as any).destroy === 'function') {
    await (editor as any).destroy(true);
  }

  milkRoot.innerHTML = '';
  editor = await Editor.make()
    .config((ctx) => {
      ctx.set(rootCtx, milkRoot);
      ctx.set(defaultValueCtx, markdown);
    })
    .use(nord)
    .use(commonmark)
    .use(gfm)
    .use(prism)
    .use(listener)
    .create();

  const l = editor.ctx.get(listenerCtx);
  l.markdownUpdated((_ctx, markdownText) => {
    currentMarkdown = markdownText;
    sourceEditor.value = markdownText;
    if (suppressOutbound || inputFrozen) {
      return;
    }

    sendUpdate(markdownText);
    ensureCodeCopyButtons();
  });

  ensureCodeCopyButtons();
};

const setSourceMode = (enabled: boolean) => {
  sourceMode = enabled;
  stage.classList.toggle('is-source-mode', sourceMode);
  if (sourceMode) {
    sourceEditor.value = currentMarkdown;
    sourceEditor.focus();
  } else {
    void applyRemoteMarkdown(sourceEditor.value, true);
  }
};

const setInputFrozen = (frozen: boolean) => {
  inputFrozen = frozen;
  shell.classList.toggle('is-frozen', frozen);
  sourceEditor.readOnly = frozen;
};

const applyRemoteMarkdown = async (markdown: string, fromSourceToggle = false) => {
  suppressOutbound = true;
  currentMarkdown = markdown;
  sourceEditor.value = markdown;
  setStatus('Rendering...');

  if (!sourceMode || fromSourceToggle) {
    await renderEditor(markdown);
  }

  setStatus(inputFrozen ? 'Synced (input frozen)' : 'Editing');
  suppressOutbound = false;
  sendAck('Rendered');
};

const parseCommand = (content: string): HostCommand | null => {
  try {
    const cmd = JSON.parse(content) as HostCommand;
    if (!cmd || typeof cmd.name !== 'string') {
      return null;
    }
    return cmd;
  } catch {
    return null;
  }
};

const scrollToHeading = (index: number | undefined) => {
  if (typeof index !== 'number' || index < 0) {
    return;
  }

  const headings = milkRoot.querySelectorAll('h1, h2, h3, h4, h5, h6');
  const target = headings[index] as HTMLElement | undefined;
  if (!target) {
    return;
  }

  target.scrollIntoView({ behavior: 'smooth', block: 'start' });
};

const insertAtCursor = (textarea: HTMLTextAreaElement, insertion: string) => {
  const start = textarea.selectionStart;
  const end = textarea.selectionEnd;
  const before = textarea.value.slice(0, start);
  const after = textarea.value.slice(end);
  textarea.value = `${before}${insertion}${after}`;
  const nextPos = start + insertion.length;
  textarea.selectionStart = nextPos;
  textarea.selectionEnd = nextPos;
};

const insertCodeBlock = () => {
  const snippet = '\n```text\n\n```\n';
  if (sourceMode) {
    insertAtCursor(sourceEditor, snippet);
    currentMarkdown = sourceEditor.value;
    if (!inputFrozen) {
      sendUpdate(currentMarkdown);
    }
    return;
  }

  const merged = `${currentMarkdown}${snippet}`;
  void applyRemoteMarkdown(merged);
  if (!inputFrozen) {
    sendUpdate(merged);
  }
};

const handleHostCommand = (command: HostCommand) => {
  switch (command.name) {
    case 'ReplaceAll':
      void applyRemoteMarkdown(command.content ?? '');
      return;
    case 'E2eSetMarkdown': {
      const markdown = command.content ?? '';
      void applyRemoteMarkdown(markdown);
      if (!inputFrozen) {
        sendUpdate(markdown);
      }
      return;
    }
    case 'ToggleSourceMode':
      setSourceMode(!sourceMode);
      return;
    case 'FreezeInput':
      setInputFrozen(true);
      return;
    case 'ResumeInput':
      setInputFrozen(false);
      return;
    case 'ScrollToHeading':
      scrollToHeading(command.index);
      return;
    case 'SetImmersive':
      shell.classList.toggle('is-immersive', Boolean(command.value));
      return;
    case 'InsertCodeBlock':
      insertCodeBlock();
      return;
    case 'ToggleSidebar':
      sendToHost({
        type: 'Command',
        content: JSON.stringify({ name: 'ToggleSidebar' }),
        timestamp: Date.now(),
      });
      return;
  }
};

const ensureCodeCopyButtons = () => {
  const blocks = milkRoot.querySelectorAll('pre');
  blocks.forEach((preElement) => {
    const pre = preElement as HTMLElement;
    if (pre.querySelector('.code-copy-button')) {
      return;
    }

    const code = pre.querySelector('code');
    if (!code) {
      return;
    }

    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'code-copy-button';
    button.textContent = 'Copy';
    button.addEventListener('click', async () => {
      try {
        await navigator.clipboard.writeText(code.textContent ?? '');
        button.textContent = 'Copied';
        setTimeout(() => {
          button.textContent = 'Copy';
        }, 1200);
      } catch {
        button.textContent = 'Error';
        setTimeout(() => {
          button.textContent = 'Copy';
        }, 1200);
      }
    });

    pre.appendChild(button);
  });
};

const observer = new MutationObserver(() => {
  ensureCodeCopyButtons();
});
observer.observe(milkRoot, { childList: true, subtree: true });

sourceEditor.addEventListener('input', () => {
  if (inputFrozen) {
    return;
  }

  currentMarkdown = sourceEditor.value;
  sendUpdate(currentMarkdown);
});

window.addEventListener('keydown', (ev) => {
  if (ev.ctrlKey && ev.shiftKey && ev.key.toLowerCase() === 'k') {
    ev.preventDefault();
    insertCodeBlock();
    return;
  }

  if (!sourceMode) {
    return;
  }

  if (ev.ctrlKey && !ev.shiftKey && ev.key.toLowerCase() === 'b') {
    ev.preventDefault();
    insertAtCursor(sourceEditor, '**bold**');
    currentMarkdown = sourceEditor.value;
    sendUpdate(currentMarkdown);
    return;
  }

  if (ev.ctrlKey && !ev.shiftKey && ev.key.toLowerCase() === 'i') {
    ev.preventDefault();
    insertAtCursor(sourceEditor, '*italic*');
    currentMarkdown = sourceEditor.value;
    sendUpdate(currentMarkdown);
  }
});

onHostMessage((payload: WebMessagePayload) => {
  if (payload.type === 'Init') {
    initialized = true;
    void applyRemoteMarkdown(payload.content || '');
    return;
  }

  if (payload.type === 'Command') {
    const command = parseCommand(payload.content);
    if (command) {
      handleHostCommand(command);
    }
    return;
  }

  if (payload.type === 'Ack' && payload.content === 'Saved') {
    setStatus('Saved');
    setTimeout(() => {
      if (!inputFrozen) {
        setStatus('Editing');
      }
    }, 300);
  }
});

void renderEditor('# AuraMark\n\nLoading...');
setStatus('Waiting host init...');
sendAck('Ready');

if (!initialized) {
  sendUpdate('# AuraMark\n\n');
}
