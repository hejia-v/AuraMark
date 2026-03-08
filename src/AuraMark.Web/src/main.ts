import './style.css';
import 'prismjs/themes/prism.css';
import { Editor, defaultValueCtx, editorViewCtx, prosePluginsCtx, rootCtx } from '@milkdown/core';
import { listener, listenerCtx } from '@milkdown/plugin-listener';
import { prism } from '@milkdown/plugin-prism';
import { commonmark } from '@milkdown/preset-commonmark';
import { gfm } from '@milkdown/preset-gfm';
import { nord } from '@milkdown/theme-nord';
import { history, redo, redoDepth, undo, undoDepth } from 'prosemirror-history';
import { createCodeBlockView, configureCodeBlockPrism, notifyCodeBlockLocaleChange } from './codeBlockEnhancements';
import { DocumentPayload, composeRawMarkdown, createRawDocument, parseIncomingDocument, parseRawDocument } from './documentPayload';
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

const transitionIndicatorEl = document.createElement('span');
transitionIndicatorEl.className = 'doc-transition-indicator';
titleEl.appendChild(transitionIndicatorEl);

const transitionDotsEl = document.createElement('span');
transitionDotsEl.className = 'doc-transition-dots';
transitionIndicatorEl.appendChild(transitionDotsEl);

for (let i = 0; i < 3; i++) {
  const dot = document.createElement('span');
  dot.className = 'doc-transition-dot';
  transitionDotsEl.appendChild(dot);
}

const transitionLabelEl = document.createElement('span');
transitionLabelEl.className = 'doc-transition-label';
transitionIndicatorEl.appendChild(transitionLabelEl);

const statusBar = document.createElement('div');
statusBar.className = 'render-status';
shell.appendChild(statusBar);

const stage = document.createElement('div');
stage.className = 'editor-stage';
shell.appendChild(stage);

const richViewport = document.createElement('div');
richViewport.className = 'rich-viewport';
stage.appendChild(richViewport);

type RichDocumentFrame = {
  container: HTMLDivElement;
  metadataPanel: HTMLDivElement;
  editorRoot: HTMLDivElement;
};

const disableWritingAssistance = (element: HTMLElement) => {
  element.spellcheck = false;
  element.setAttribute('spellcheck', 'false');
  element.setAttribute('autocapitalize', 'off');
  element.setAttribute('autocorrect', 'off');
  element.setAttribute('data-gramm', 'false');
  element.setAttribute('data-gramm_editor', 'false');
};

const createRichFrame = (): RichDocumentFrame => {
  const container = document.createElement('div');
  container.className = 'milk-root';
  disableWritingAssistance(container);

  const metadataPanel = document.createElement('div');
  metadataPanel.className = 'metadata-panel';
  metadataPanel.hidden = true;
  container.appendChild(metadataPanel);

  const editorRoot = document.createElement('div');
  editorRoot.className = 'milk-body-root';
  disableWritingAssistance(editorRoot);
  container.appendChild(editorRoot);

  return { container, metadataPanel, editorRoot };
};

let activeFrame = createRichFrame();
let activeMilkRoot = activeFrame.container;
activeMilkRoot.classList.add('is-active');
richViewport.appendChild(activeMilkRoot);

const sourceEditor = document.createElement('textarea');
sourceEditor.className = 'source-editor';
disableWritingAssistance(sourceEditor);
sourceEditor.setAttribute('aria-label', 'Markdown source');
stage.appendChild(sourceEditor);

let editor: Editor | null = null;
let initialized = false;
let sourceMode = false;
let inputFrozen = false;
let suppressOutbound = false;
let currentMarkdown = '';
let savedMarkdown = '';
let currentDocument: DocumentPayload = createRawDocument('');
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
let uiLanguage = 'en-US';
let transitionTimer = 0;
let transitionMode: 'opening' | 'switching' = 'opening';

const localize = (key: 'markdownSource' | 'unsavedChanges') => {
  const chinese = uiLanguage.toLowerCase().startsWith('zh');
  if (key === 'markdownSource') {
    return chinese ? 'Markdown 源码' : 'Markdown source';
  }

  return chinese ? '存在未保存的更改' : 'Unsaved changes';
};

const applyLanguage = (language: string) => {
  uiLanguage = language || 'en-US';
  document.documentElement.lang = uiLanguage;
  sourceEditor.setAttribute('aria-label', localize('markdownSource'));
  updateDirtyDot();
  updateTransitionCopy();
  notifyCodeBlockLocaleChange();
};

const getTransitionLabel = () => {
  const chinese = uiLanguage.toLowerCase().startsWith('zh');
  if (transitionMode === 'opening') {
    return chinese ? '\u6b63\u5728\u6253\u5f00\u6587\u6863' : 'Opening document';
  }

  return chinese ? '\u6b63\u5728\u5207\u6362\u6587\u6863' : 'Switching document';
};

const updateTransitionCopy = () => {
  transitionLabelEl.textContent = getTransitionLabel();
};

const setTransitionState = (active: boolean, mode: 'opening' | 'switching') => {
  transitionMode = mode;
  updateTransitionCopy();
  stage.classList.toggle('is-busy', active);

  if (active) {
    if (transitionTimer) {
      return;
    }

    transitionTimer = window.setTimeout(() => {
      transitionTimer = 0;
      shell.classList.add('is-transitioning');
    }, 90);
    return;
  }

  if (transitionTimer) {
    window.clearTimeout(transitionTimer);
    transitionTimer = 0;
  }

  shell.classList.remove('is-transitioning');
};

const afterNextPaint = () =>
  new Promise<void>((resolve) => {
    requestAnimationFrame(() => {
      requestAnimationFrame(() => {
        resolve();
      });
    });
  });

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
  dotEl.title = dirty ? localize('unsavedChanges') : '';
};

const setCurrentDocument = (document: DocumentPayload) => {
  currentDocument = document;
  currentMarkdown = document.rawMarkdown;
};

const updateCurrentDocumentBody = (bodyMarkdown: string) => {
  const nextDocument: DocumentPayload = {
    ...currentDocument,
    bodyMarkdown,
    rawMarkdown: composeRawMarkdown(currentDocument.frontMatterRaw, bodyMarkdown),
  };
  setCurrentDocument(nextDocument);
  return nextDocument;
};

const renderMetadataPanel = (panel: HTMLDivElement, metadata: DocumentPayload['metadata']) => {
  panel.replaceChildren();
  panel.hidden = metadata.length === 0;
  if (metadata.length === 0) {
    return;
  }

  for (const entry of metadata) {
    const row = document.createElement('div');
    row.className = 'metadata-row';

    const key = document.createElement('div');
    key.className = 'metadata-key';
    key.textContent = entry.key;
    row.appendChild(key);

    const value = document.createElement('div');
    value.className = 'metadata-value';

    if (entry.kind === 'list') {
      const chips = document.createElement('div');
      chips.className = 'metadata-chips';
      for (const item of entry.items ?? []) {
        const chip = document.createElement('span');
        chip.className = 'metadata-chip';
        chip.textContent = item;
        chips.appendChild(chip);
      }
      value.appendChild(chips);
    } else if (entry.kind === 'object') {
      const structured = document.createElement('pre');
      structured.className = 'metadata-structured';
      structured.textContent = entry.structuredText ?? 'null';
      value.appendChild(structured);
    } else {
      const text = document.createElement('span');
      text.className = 'metadata-text';
      text.textContent = entry.displayText ?? '';
      value.appendChild(text);
    }

    row.appendChild(value);
    panel.appendChild(row);
  }
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
  const document = parseRawDocument(markdown);
  setCurrentDocument(document);
  sourceEditor.value = document.rawMarkdown;
  sourceEditor.focus();
  sourceEditor.selectionStart = document.rawMarkdown.length;
  sourceEditor.selectionEnd = document.rawMarkdown.length;
  sendUpdate(document.rawMarkdown);
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
  setCurrentDocument(parseRawDocument(sourceEditor.value));
  sendUpdate(currentDocument.rawMarkdown);
  updateDirtyDot();
  reportHistoryState();
};

const renderEditor = async (document: DocumentPayload, sequence: number) => {
  renderQueue = renderQueue
    .catch(() => undefined)
    .then(async () => {
      if (sequence !== applySequence) {
        return;
      }

      const previousEditor = editor;
      const previousFrame = activeFrame;
      const nextFrame = createRichFrame();
      renderMetadataPanel(nextFrame.metadataPanel, document.metadata);
      nextFrame.container.classList.add('is-staged');
      richViewport.appendChild(nextFrame.container);

      const nextEditor = await Editor.make()
        .config((ctx) => {
          ctx.set(rootCtx, nextFrame.editorRoot);
          ctx.set(defaultValueCtx, document.bodyMarkdown);
          ctx.update(prosePluginsCtx, (plugins) => [...plugins, history()]);
        })
        .use(nord)
        .use(commonmark)
        .use(gfm)
        .use(prism)
        .config((ctx) => {
          configureCodeBlockPrism(ctx);
        })
        .use(createCodeBlockView(() => uiLanguage))
        .use(listener)
        .create();

      disableWritingAssistance(nextEditor.action((ctx) => ctx.get(editorViewCtx)).dom);

      if (sequence !== applySequence) {
        if (typeof (nextEditor as { destroy?: (clearPlugins?: boolean) => Promise<unknown> }).destroy === 'function') {
          await nextEditor.destroy(true);
        }
        nextFrame.container.remove();
        return;
      }

      const listeners = nextEditor.ctx.get(listenerCtx);
      listeners.markdownUpdated((_ctx, markdownText) => {
        const nextDocument = updateCurrentDocumentBody(markdownText);
        sourceEditor.value = nextDocument.rawMarkdown;
        updateDirtyDot();
        reportHistoryState();

        if (suppressOutbound || inputFrozen) {
          return;
        }

        sendUpdate(nextDocument.rawMarkdown);
      });

      if (sequence !== applySequence) {
        if (typeof (nextEditor as { destroy?: (clearPlugins?: boolean) => Promise<unknown> }).destroy === 'function') {
          await nextEditor.destroy(true);
        }
        nextFrame.container.remove();
        return;
      }

      bindMilkRootInteractions(nextFrame.container);
      nextFrame.container.classList.remove('is-staged');
      nextFrame.container.classList.add('is-entering');
      previousFrame.container.classList.add('is-leaving');
      await afterNextPaint();
      if (sequence !== applySequence) {
        if (typeof (nextEditor as { destroy?: (clearPlugins?: boolean) => Promise<unknown> }).destroy === 'function') {
          await nextEditor.destroy(true);
        }
        nextFrame.container.remove();
        return;
      }
      editor = nextEditor;
      activeFrame = nextFrame;
      activeMilkRoot = nextFrame.container;
      prevScrollTop = 0;
      nextFrame.container.classList.add('is-active');
      previousFrame.container.classList.add('is-hidden');
      await new Promise((resolve) => window.setTimeout(resolve, 170));
      previousFrame.container.remove();
      if (typeof (previousEditor as { destroy?: (clearPlugins?: boolean) => Promise<unknown> } | null)?.destroy === 'function') {
        await previousEditor.destroy(true);
      }

      reportActiveHeading(computeActiveHeading());
      reportHistoryState();
    });

  await renderQueue;
};

const setSourceMode = (enabled: boolean) => {
  sourceMode = enabled;
  stage.classList.toggle('is-source-mode', sourceMode);
  if (sourceMode) {
    sourceEditor.value = currentDocument.rawMarkdown;
    sourceEditor.focus();
    resetSourceHistory();
    reportActiveHeading(-1);
  } else {
    void applyRemoteDocument(parseRawDocument(sourceEditor.value), true);
  }
};

const setInputFrozen = (frozen: boolean) => {
  inputFrozen = frozen;
  shell.classList.toggle('is-frozen', frozen);
  sourceEditor.readOnly = frozen;
  reportHistoryState();
};

const applyRemoteDocument = async (document: DocumentPayload, fromSourceToggle = false) => {
  const sequence = ++applySequence;
  const transitionState: 'opening' | 'switching' = editor ? 'switching' : 'opening';
  lastReportedActiveIndex = -1;
  clickFloorIndex = -1;
  suppressOutbound = true;
  setTransitionState(true, transitionState);
  try {
    setCurrentDocument(document);
    savedMarkdown = document.rawMarkdown;
    sourceEditor.value = document.rawMarkdown;
    updateDirtyDot();

    if (!sourceMode || fromSourceToggle) {
      await renderEditor(document, sequence);
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
      setTransitionState(false, transitionState);
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

  const headings = activeMilkRoot.querySelectorAll('h1, h2, h3, h4, h5, h6');
  const target = headings[index] as HTMLElement | undefined;
  if (!target) {
    return;
  }

  target.scrollIntoView({ behavior: 'smooth', block: 'start' });
};

const computeActiveHeadingAtY = (y: number): number => {
  const headings = activeMilkRoot.querySelectorAll('h1, h2, h3, h4, h5, h6');
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
  return computeActiveHeadingAtY(activeMilkRoot.scrollTop + 10);
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

const handleMilkRootScroll = (event: Event) => {
  if (event.currentTarget !== activeMilkRoot) {
    return;
  }

  if (activeHeadingRafId) {
    return;
  }

  activeHeadingRafId = requestAnimationFrame(() => {
    activeHeadingRafId = 0;
    const currentScrollTop = activeMilkRoot.scrollTop;
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
};

const handleMilkRootClick = (event: MouseEvent) => {
  if (event.currentTarget !== activeMilkRoot) {
    return;
  }

  const rect = activeMilkRoot.getBoundingClientRect();
  const clickY = event.clientY - rect.top + activeMilkRoot.scrollTop;
  const index = computeActiveHeadingAtY(clickY);
  clickFloorIndex = index;
  reportActiveHeading(index);
};

const bindMilkRootInteractions = (root: HTMLDivElement) => {
  root.addEventListener('scroll', handleMilkRootScroll);
  root.addEventListener('click', handleMilkRootClick);
};

bindMilkRootInteractions(activeMilkRoot);

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

  const mergedDocument: DocumentPayload = {
    ...currentDocument,
    bodyMarkdown: `${currentDocument.bodyMarkdown}${snippet}`,
    rawMarkdown: composeRawMarkdown(currentDocument.frontMatterRaw, `${currentDocument.bodyMarkdown}${snippet}`),
  };
  void applyRemoteDocument(mergedDocument);
  if (!inputFrozen) {
    sendUpdate(mergedDocument.rawMarkdown);
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
      void applyRemoteDocument(parseIncomingDocument(command.content ?? ''));
      return;
    case 'E2eSetMarkdown': {
      const markdown = command.content ?? '';
      const document = parseRawDocument(markdown);
      void applyRemoteDocument(document);
      if (!inputFrozen) {
        sendUpdate(document.rawMarkdown);
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
      updateTransitionCopy();
      return;
    case 'SetLanguage':
      applyLanguage(command.content ?? 'en-US');
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
  setCurrentDocument(parseRawDocument(sourceEditor.value));
  sendUpdate(currentDocument.rawMarkdown);
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
    void applyRemoteDocument(parseIncomingDocument(payload.content || ''));
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
    savedMarkdown = currentDocument.rawMarkdown;
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
applyLanguage(uiLanguage);
sendAck('Ready');
