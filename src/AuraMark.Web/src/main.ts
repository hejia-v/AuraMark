import './style.css';
import 'prismjs/themes/prism.css';
import { Editor, rootCtx, defaultValueCtx } from '@milkdown/core';
import { commonmark } from '@milkdown/preset-commonmark';
import { gfm } from '@milkdown/preset-gfm';
import { nord } from '@milkdown/theme-nord';
import { listener, listenerCtx } from '@milkdown/plugin-listener';
import { sendToHost, onHostMessage, WebMessagePayload, HostCommand, IpcErrorPayload } from './ipc';

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
let applySequence = 0;
let renderQueue: Promise<void> = Promise.resolve();

window.addEventListener('error', (event) => {
  sendToHost({
    type: 'Error',
    content: `window error: ${event.message}`,
    timestamp: Date.now(),
  });
});
window.addEventListener('unhandledrejection', (event) => {
  sendToHost({
    type: 'Error',
    content: `unhandled rejection: ${String(event.reason)}`,
    timestamp: Date.now(),
  });
});

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

const renderEditor = async (markdown: string, sequence: number) => {
  renderQueue = renderQueue
    .catch(() => undefined)
    .then(async () => {
      if (sequence !== applySequence) {
        return;
      }

      if (editor) {
        const previousEditor = editor;
        editor = null;
        if (typeof (previousEditor as any).destroy === 'function') {
          await (previousEditor as any).destroy(true);
        }
      }

      if (sequence !== applySequence) {
        return;
      }

      milkRoot.innerHTML = '';
      const nextEditor = await Editor.make()
        .config((ctx) => {
          ctx.set(rootCtx, milkRoot);
          ctx.set(defaultValueCtx, markdown);
        })
        .use(nord)
        .use(commonmark)
        .use(gfm)
        .use(listener)
        .create();

      if (sequence !== applySequence) {
        if (typeof (nextEditor as any).destroy === 'function') {
          await (nextEditor as any).destroy(true);
        }
        return;
      }

      editor = nextEditor;
      const l = nextEditor.ctx.get(listenerCtx);
      l.markdownUpdated((_ctx, markdownText) => {
        currentMarkdown = markdownText;
        sourceEditor.value = markdownText;
        if (suppressOutbound || inputFrozen) {
          return;
        }

        sendUpdate(markdownText);
      });
    });

  await renderQueue;
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
  const sequence = ++applySequence;
  suppressOutbound = true;
  try {
    currentMarkdown = markdown;
    sourceEditor.value = markdown;
    setStatus('Rendering...');

    if (!sourceMode || fromSourceToggle) {
      await renderEditor(markdown, sequence);
    }

    if (sequence !== applySequence) {
      return;
    }

    setStatus(inputFrozen ? 'Synced (input frozen)' : 'Editing');
    sendAck('Rendered');
  } finally {
    if (sequence === applySequence) {
      suppressOutbound = false;
    }
  }
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

const parseErrorPayload = (content: string): IpcErrorPayload | null => {
  try {
    const parsed = JSON.parse(content) as IpcErrorPayload;
    if (!parsed || typeof parsed.code !== 'string' || typeof parsed.message !== 'string') {
      return null;
    }

    return parsed;
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

window.addEventListener('beforeunload', () => {
  editor = null;
});

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
    return;
  }

  if (payload.type === 'Error') {
    const parsed = parseErrorPayload(payload.content);
    if (parsed) {
      setStatus(`${parsed.code}: ${parsed.message}`);
    } else {
      setStatus(payload.content || 'Error');
    }
  }
});

setStatus('Waiting host init...');
sendAck('Ready');
