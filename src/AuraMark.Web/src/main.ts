import './style.css';
import 'prismjs/themes/prism.css';
import { Editor, defaultValueCtx, editorViewCtx, prosePluginsCtx, rootCtx } from '@milkdown/core';
import { listener, listenerCtx } from '@milkdown/plugin-listener';
import { commonmark } from '@milkdown/preset-commonmark';
import { gfm } from '@milkdown/preset-gfm';
import { nord } from '@milkdown/theme-nord';
import { history, redo, redoDepth, undo, undoDepth } from 'prosemirror-history';
import { HostCommand, IpcErrorPayload, WebMessagePayload, onHostMessage, sendToHost } from './ipc';

const host = document.getElementById('app');
if (!host) {
  throw new Error('Missing #app root');
}

const sourceHistoryLimit = 200;
const sourceHistoryGroupWindowMs = 1000;

const shell = document.createElement('div');
shell.className = 'editor-shell';
host.appendChild(shell);

const titleEl = document.createElement('div');
titleEl.className = 'doc-title';
shell.appendChild(titleEl);

const titleTextEl = document.createElement('span');
titleTextEl.textContent = 'Untitled.md';
titleEl.appendChild(titleTextEl);

const dotEl = document.createElement('span');
dotEl.className = 'doc-status-dot';
titleEl.appendChild(dotEl);

const statusBar = document.createElement('div');
statusBar.className = 'render-status';
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
let savedMarkdown = '';
let applySequence = 0;
let renderQueue: Promise<void> = Promise.resolve();
let lastReportedActiveIndex = -1;
let activeHeadingRafId = 0;
let clickFloorIndex = -1;
let prevScrollTop = 0;
let sourceUndoStack: string[] = [];
let sourceRedoStack: string[] = [];
let sourceLastEditAt = 0;
let sourceLastEditKind = '';
let pendingSourceInputType = '';

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

const sendHistoryState = (canUndo: boolean, canRedo: boolean) => {
  sendToHost({
    type: 'Command',
    content: JSON.stringify({ name: 'HistoryStateChanged', canUndo, canRedo }),
    timestamp: Date.now(),
  });
};

const updateDirtyDot = () => {
  const dirty = currentMarkdown !== savedMarkdown;
  dotEl.className = 'doc-status-dot' + (dirty ? ' state-dirty' : '');
  dotEl.title = dirty ? 'Unsaved changes' : '';
};

const setStatus = (_text: string) => {
  // no-op; dot is driven by content comparison
};

const trimHistoryStack = (stack: string[]) => {
  if (stack.length <= sourceHistoryLimit) {
    return;
  }

  stack.splice(0, stack.length - sourceHistoryLimit);
};

const normalizeSourceInputType = (inputType: string) => {
  if (!inputType) {
    return 'other';
  }

  if (inputType.startsWith('insertComposition') || inputType.startsWith('insertText')) {
    return 'insert';
  }

  if (inputType === 'insertLineBreak' || inputType === 'insertParagraph') {
    return 'linebreak';
  }

  if (inputType.startsWith('deleteContent')) {
    return 'delete';
  }

  if (inputType === 'insertFromPaste' || inputType === 'insertFromDrop') {
    return 'paste';
  }

  return inputType;
};

const getEditorView = () => {
  if (!editor) {
    return null;
  }

  try {
    return editor.action((ctx) => ctx.get(editorViewCtx));
  } catch {
    return null;
  }
};

const reportHistoryState = () => {
  if (inputFrozen) {
    sendHistoryState(false, false);
    return;
  }

  if (sourceMode) {
    sendHistoryState(sourceUndoStack.length > 0, sourceRedoStack.length > 0);
    return;
  }

  const view = getEditorView();
  if (!view) {
    sendHistoryState(false, false);
    return;
  }

  sendHistoryState(undoDepth(view.state) > 0, redoDepth(view.state) > 0);
};

const resetSourceHistory = () => {
  sourceUndoStack = [];
  sourceRedoStack = [];
  sourceLastEditAt = 0;
  sourceLastEditKind = '';
  pendingSourceInputType = '';
  reportHistoryState();
};

const pushSourceUndoSnapshot = (previousMarkdown: string, inputType: string) => {
  if (previousMarkdown === sourceEditor.value) {
    pendingSourceInputType = '';
    return;
  }

  const now = Date.now();
  const kind = normalizeSourceInputType(inputType);
  const canGroup =
    sourceUndoStack.length > 0 &&
    kind === sourceLastEditKind &&
    now - sourceLastEditAt <= sourceHistoryGroupWindowMs &&
    (kind === 'insert' || kind === 'delete');

  if (sourceUndoStack.length === 0) {
    sourceUndoStack.push(previousMarkdown);
  } else if (!canGroup && sourceUndoStack[sourceUndoStack.length - 1] !== previousMarkdown) {
    sourceUndoStack.push(previousMarkdown);
  }

  trimHistoryStack(sourceUndoStack);
  sourceRedoStack = [];
  sourceLastEditAt = now;
  sourceLastEditKind = kind;
  pendingSourceInputType = '';
};

const applySourceHistoryMarkdown = (markdown: string) => {
  currentMarkdown = markdown;
  sourceEditor.value = markdown;
  sourceEditor.focus();
  sourceEditor.selectionStart = markdown.length;
  sourceEditor.selectionEnd = markdown.length;
  sendUpdate(markdown);
  updateDirtyDot();
  reportHistoryState();
};

const performSourceUndo = () => {
  if (inputFrozen || sourceUndoStack.length === 0) {
    reportHistoryState();
    return false;
  }

  const nextMarkdown = sourceUndoStack.pop();
  if (typeof nextMarkdown !== 'string') {
    reportHistoryState();
    return false;
  }

  if (sourceRedoStack[sourceRedoStack.length - 1] !== currentMarkdown) {
    sourceRedoStack.push(currentMarkdown);
    trimHistoryStack(sourceRedoStack);
  }

  sourceLastEditAt = 0;
  sourceLastEditKind = '';
  applySourceHistoryMarkdown(nextMarkdown);
  return true;
};

const performSourceRedo = () => {
  if (inputFrozen || sourceRedoStack.length === 0) {
    reportHistoryState();
    return false;
  }

  const nextMarkdown = sourceRedoStack.pop();
  if (typeof nextMarkdown !== 'string') {
    reportHistoryState();
    return false;
  }

  if (sourceUndoStack[sourceUndoStack.length - 1] !== currentMarkdown) {
    sourceUndoStack.push(currentMarkdown);
    trimHistoryStack(sourceUndoStack);
  }

  sourceLastEditAt = 0;
  sourceLastEditKind = '';
  applySourceHistoryMarkdown(nextMarkdown);
  return true;
};

const performRichHistoryCommand = (command: typeof undo) => {
  if (inputFrozen) {
    reportHistoryState();
    return false;
  }

  const view = getEditorView();
  if (!view) {
    reportHistoryState();
    return false;
  }

  const handled = command(view.state, view.dispatch);
  queueMicrotask(() => {
    reportHistoryState();
  });
  return handled;
};

const performUndo = () => {
  return sourceMode ? performSourceUndo() : performRichHistoryCommand(undo);
};

const performRedo = () => {
  return sourceMode ? performSourceRedo() : performRichHistoryCommand(redo);
};

const commitSourceEditorMutation = (inputType: string, mutate: () => void) => {
  if (inputFrozen) {
    reportHistoryState();
    return;
  }

  const previousMarkdown = currentMarkdown;
  mutate();
  pendingSourceInputType = inputType;
  pushSourceUndoSnapshot(previousMarkdown, inputType);
  currentMarkdown = sourceEditor.value;
  sendUpdate(currentMarkdown);
  updateDirtyDot();
  reportHistoryState();
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
        if (typeof (previousEditor as { destroy?: (clearPlugins?: boolean) => Promise<unknown> }).destroy === 'function') {
          await previousEditor.destroy(true);
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
          ctx.update(prosePluginsCtx, (plugins) => [...plugins, history()]);
        })
        .use(nord)
        .use(commonmark)
        .use(gfm)
        .use(listener)
        .create();

      if (sequence !== applySequence) {
        if (typeof (nextEditor as { destroy?: (clearPlugins?: boolean) => Promise<unknown> }).destroy === 'function') {
          await nextEditor.destroy(true);
        }
        return;
      }

      editor = nextEditor;
      const listeners = nextEditor.ctx.get(listenerCtx);
      listeners.markdownUpdated((_ctx, markdownText) => {
        currentMarkdown = markdownText;
        sourceEditor.value = markdownText;
        updateDirtyDot();
        reportHistoryState();

        if (suppressOutbound || inputFrozen) {
          return;
        }

        sendUpdate(markdownText);
      });

      reportActiveHeading(computeActiveHeading());
      reportHistoryState();
    });

  await renderQueue;
};

const setSourceMode = (enabled: boolean) => {
  sourceMode = enabled;
  stage.classList.toggle('is-source-mode', sourceMode);
  if (sourceMode) {
    sourceEditor.value = currentMarkdown;
    sourceEditor.focus();
    resetSourceHistory();
    reportActiveHeading(-1);
  } else {
    void applyRemoteMarkdown(sourceEditor.value, true);
  }
};

const setInputFrozen = (frozen: boolean) => {
  inputFrozen = frozen;
  shell.classList.toggle('is-frozen', frozen);
  sourceEditor.readOnly = frozen;
  reportHistoryState();
};

const applyRemoteMarkdown = async (markdown: string, fromSourceToggle = false) => {
  const sequence = ++applySequence;
  lastReportedActiveIndex = -1;
  clickFloorIndex = -1;
  suppressOutbound = true;
  try {
    currentMarkdown = markdown;
    savedMarkdown = markdown;
    sourceEditor.value = markdown;
    updateDirtyDot();

    if (!sourceMode || fromSourceToggle) {
      await renderEditor(markdown, sequence);
    }

    if (sequence !== applySequence) {
      return;
    }

    sourceLastEditAt = 0;
    sourceLastEditKind = '';
    pendingSourceInputType = '';
    if (sourceMode) {
      resetSourceHistory();
    } else {
      reportHistoryState();
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

const computeActiveHeadingAtY = (y: number): number => {
  const headings = milkRoot.querySelectorAll('h1, h2, h3, h4, h5, h6');
  if (headings.length === 0) {
    return -1;
  }

  let activeIndex = -1;
  for (let i = 0; i < headings.length; i++) {
    const el = headings[i] as HTMLElement;
    if (el.offsetTop <= y) {
      activeIndex = i;
    } else {
      break;
    }
  }
  return activeIndex;
};

const computeActiveHeading = (): number => {
  return computeActiveHeadingAtY(milkRoot.scrollTop + 10);
};

const reportActiveHeading = (index: number) => {
  if (index === lastReportedActiveIndex) {
    return;
  }

  lastReportedActiveIndex = index;
  sendToHost({
    type: 'Command',
    content: JSON.stringify({ name: 'ActiveHeadingChanged', index }),
    timestamp: Date.now(),
  });
};

milkRoot.addEventListener('scroll', () => {
  if (activeHeadingRafId) {
    return;
  }

  activeHeadingRafId = requestAnimationFrame(() => {
    activeHeadingRafId = 0;
    const currentScrollTop = milkRoot.scrollTop;
    const scrollingDown = currentScrollTop >= prevScrollTop;
    prevScrollTop = currentScrollTop;

    const scrollIndex = computeActiveHeading();
    if (clickFloorIndex >= 0) {
      if (!scrollingDown || scrollIndex >= clickFloorIndex) {
        clickFloorIndex = -1;
      }
    }

    reportActiveHeading(Math.max(scrollIndex, clickFloorIndex));
  });
});

milkRoot.addEventListener('click', (event: MouseEvent) => {
  const rect = milkRoot.getBoundingClientRect();
  const clickY = event.clientY - rect.top + milkRoot.scrollTop;
  const index = computeActiveHeadingAtY(clickY);
  clickFloorIndex = index;
  reportActiveHeading(index);
});

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
    commitSourceEditorMutation('insertCodeBlock', () => {
      insertAtCursor(sourceEditor, snippet);
    });
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
    case 'Undo':
      performUndo();
      return;
    case 'Redo':
      performRedo();
      return;
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
    case 'SetTitle':
      titleTextEl.textContent = command.content ?? 'Untitled.md';
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

sourceEditor.addEventListener('beforeinput', (event) => {
  const inputEvent = event as InputEvent;
  pendingSourceInputType = inputEvent.inputType ?? '';

  if (!sourceMode || inputFrozen) {
    return;
  }

  if (pendingSourceInputType === 'historyUndo') {
    event.preventDefault();
    performSourceUndo();
    return;
  }

  if (pendingSourceInputType === 'historyRedo') {
    event.preventDefault();
    performSourceRedo();
  }
});

sourceEditor.addEventListener('input', () => {
  if (inputFrozen) {
    pendingSourceInputType = '';
    return;
  }

  const previousMarkdown = currentMarkdown;
  pushSourceUndoSnapshot(previousMarkdown, pendingSourceInputType);
  currentMarkdown = sourceEditor.value;
  sendUpdate(currentMarkdown);
  updateDirtyDot();
  reportHistoryState();
});

window.addEventListener('keydown', (event) => {
  if (event.ctrlKey && !event.altKey && event.key.toLowerCase() === 'z') {
    event.preventDefault();
    if (event.shiftKey) {
      performRedo();
    } else {
      performUndo();
    }
    return;
  }

  if (event.ctrlKey && !event.altKey && event.key.toLowerCase() === 'y') {
    event.preventDefault();
    performRedo();
    return;
  }

  if (event.ctrlKey && event.shiftKey && event.key.toLowerCase() === 'k') {
    event.preventDefault();
    insertCodeBlock();
    return;
  }

  if (!sourceMode) {
    return;
  }

  if (event.ctrlKey && !event.shiftKey && event.key.toLowerCase() === 'b') {
    event.preventDefault();
    commitSourceEditorMutation('formatBold', () => {
      insertAtCursor(sourceEditor, '**bold**');
    });
    return;
  }

  if (event.ctrlKey && !event.shiftKey && event.key.toLowerCase() === 'i') {
    event.preventDefault();
    commitSourceEditorMutation('formatItalic', () => {
      insertAtCursor(sourceEditor, '*italic*');
    });
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
    savedMarkdown = currentMarkdown;
    updateDirtyDot();
    reportHistoryState();
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
