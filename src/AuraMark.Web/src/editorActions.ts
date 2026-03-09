import type { Editor } from '@milkdown/core';
import type { DocumentPayload } from './documentPayload';
import type { HostCommand } from './ipc';
import { callCommand } from '@milkdown/utils';
import {
  createCodeBlockCommand,
  insertHrCommand,
  insertImageCommand,
  toggleEmphasisCommand,
  toggleInlineCodeCommand,
  toggleLinkCommand,
  toggleStrongCommand,
  turnIntoTextCommand,
  wrapInBlockquoteCommand,
  wrapInBulletListCommand,
  wrapInHeadingCommand,
  wrapInOrderedListCommand,
} from '@milkdown/preset-commonmark';
import { insertTableCommand, toggleStrikethroughCommand } from '@milkdown/preset-gfm';
import { TextSelection } from '@milkdown/prose/state';
import type { EditorView } from '@milkdown/prose/view';

type EditorActionArgs = Record<string, unknown>;

export type EditorActionRequest = {
  id: string;
  args?: EditorActionArgs;
};

type EditorActionState = {
  enabled: boolean;
  active: boolean;
  shortcut: string;
};

type EditorActionSnapshot = {
  actions: Record<string, EditorActionState>;
};

type EditorActionEnvironment = {
  getEditor: () => Editor | null;
  getEditorView: () => EditorView | null;
  getCurrentDocument: () => DocumentPayload;
  getCurrentMarkdown: () => string;
  getUiLanguage: () => string;
  isSourceMode: () => boolean;
  isInputFrozen: () => boolean;
  sourceEditor: HTMLTextAreaElement;
  commitSourceEditorMutation: (inputType: string, mutate: () => void) => void;
  applyRemoteDocument: (document: DocumentPayload, fromSourceToggle?: boolean) => Promise<void>;
  parseRawDocument: (rawMarkdown: string) => DocumentPayload;
  reportHistoryState: () => void;
  sendActionStateChanged: (snapshot: EditorActionSnapshot) => void;
};

type ActionStateContext = {
  sourceMode: boolean;
  inputFrozen: boolean;
  view: EditorView | null;
  language: string;
};

type EditorActionDefinition = {
  id: string;
  shortcut: string;
  isSupportedInRich: boolean;
  isSupportedInSource: boolean;
  runRich?: (env: EditorActionEnvironment, args?: EditorActionArgs) => Promise<boolean> | boolean;
  runSource?: (env: EditorActionEnvironment, args?: EditorActionArgs) => boolean;
  getActive?: (env: EditorActionEnvironment, ctx: ActionStateContext, args?: EditorActionArgs) => boolean;
};

const orderedListRegex = /^\s*\d+[.)]\s+/;
const unorderedListRegex = /^\s*[-+*]\s+/;
const taskListRegex = /^\s*[-+*]\s+\[(?: |x|X)\]\s+/;
const quoteRegex = /^\s*>\s+/;
const headingRegex = /^\s{0,3}(#{1,6})\s+/;

export const parseEditorActionRequest = (content: string | undefined): EditorActionRequest | null => {
  if (!content) {
    return null;
  }

  try {
    const parsed = JSON.parse(content) as EditorActionRequest;
    if (!parsed || typeof parsed.id !== 'string') {
      return null;
    }

    return parsed;
  } catch {
    return null;
  }
};

export const createEditorActionController = (env: EditorActionEnvironment) => {
  const focusRichEditor = () => {
    const view = env.getEditorView();
    if (!view) {
      return null;
    }

    view.focus();
    view.dom.focus();
    return view;
  };

  const reportStates = () => {
    const ctx: ActionStateContext = {
      sourceMode: env.isSourceMode(),
      inputFrozen: env.isInputFrozen(),
      view: env.getEditorView(),
      language: env.getUiLanguage(),
    };

    const actions = Object.fromEntries(
      actionDefinitions.map((definition) => {
        const enabled =
          !ctx.inputFrozen &&
          (ctx.sourceMode ? definition.isSupportedInSource : definition.isSupportedInRich);
        const active = enabled && definition.getActive ? definition.getActive(env, ctx) : false;
        return [definition.id, { enabled, active, shortcut: definition.shortcut }];
      }),
    );

    const headingEnabled =
      !ctx.inputFrozen &&
      (ctx.sourceMode
        ? actionMap.get('paragraph.heading')?.isSupportedInSource
        : actionMap.get('paragraph.heading')?.isSupportedInRich);
    for (let level = 1; level <= 6; level++) {
      actions[`paragraph.heading.${level}`] = {
        enabled: Boolean(headingEnabled),
        active: Boolean(actionMap.get('paragraph.heading')?.getActive?.(env, ctx, { level })),
        shortcut: `Ctrl+${level}`,
      };
    }

    env.sendActionStateChanged({ actions });
  };

  const execute = async (request: EditorActionRequest) => {
    const definition = actionMap.get(request.id);
    if (!definition || env.isInputFrozen()) {
      reportStates();
      return false;
    }

    const handled = env.isSourceMode()
      ? definition.isSupportedInSource && definition.runSource
        ? definition.runSource(env, request.args)
        : false
      : definition.isSupportedInRich && definition.runRich
        ? (focusRichEditor(), await definition.runRich(env, request.args))
        : false;

    queueMicrotask(() => {
      env.reportHistoryState();
      reportStates();
    });

    return handled;
  };

  return { execute, reportStates };
};

const runRichCommand =
  (commandKey: string) =>
  (env: EditorActionEnvironment) => {
    const editor = env.getEditor();
    if (!editor) {
      return false;
    }

    return editor.action(callCommand(commandKey));
  };

const runRichInsertTable = (env: EditorActionEnvironment) => {
  const editor = env.getEditor();
  if (!editor) {
    return false;
  }

  return editor.action(callCommand(insertTableCommand.key, { row: 3, col: 3 }));
};

const runRichParagraph = (env: EditorActionEnvironment) => {
  const editor = env.getEditor();
  if (!editor) {
    return false;
  }

  return editor.action(callCommand(turnIntoTextCommand.key));
};

const getHeadingLevelArg = (args?: EditorActionArgs) => {
  const level = Number(args?.level ?? 1);
  return Number.isInteger(level) ? Math.min(6, Math.max(1, level)) : 1;
};

const runRichHeading = (env: EditorActionEnvironment, args?: EditorActionArgs) => {
  const editor = env.getEditor();
  if (!editor) {
    return false;
  }

  return editor.action(callCommand(wrapInHeadingCommand.key, getHeadingLevelArg(args)));
};

const runRichIncreaseHeading = (env: EditorActionEnvironment) => {
  const level = getCurrentHeadingLevel(env.getEditorView());
  return runRichHeading(env, { level: Math.min(6, Math.max(1, level + 1 || 1)) });
};

const runRichDecreaseHeading = (env: EditorActionEnvironment) => {
  const view = env.getEditorView();
  const level = getCurrentHeadingLevel(view);
  if (level <= 1) {
    return runRichParagraph(env);
  }

  return runRichHeading(env, { level: level - 1 });
};

const runRichLink = (env: EditorActionEnvironment) => {
  const editor = env.getEditor();
  if (!editor) {
    return false;
  }

  return editor.action(callCommand(toggleLinkCommand.key, { href: 'https://example.com' }));
};

const runRichImage = (env: EditorActionEnvironment) => {
  const editor = env.getEditor();
  if (!editor) {
    return false;
  }

  return editor.action(
    callCommand(insertImageCommand.key, {
      src: 'path/to/image.png',
      alt: 'alt text',
      title: 'alt text',
    }),
  );
};

const runRichClearFormatting = (env: EditorActionEnvironment) => {
  const view = env.getEditorView();
  if (!view) {
    return false;
  }

  const { state } = view;
  const { from, to } = state.selection;
  if (from === to) {
    const transaction = state.tr.setStoredMarks([]);
    view.dispatch(transaction);
    return true;
  }

  const markNames = ['strong', 'emphasis', 'inlineCode', 'strike_through', 'link'];
  let transaction = state.tr;
  for (const markName of markNames) {
    const markType = state.schema.marks[markName];
    if (markType) {
      transaction = transaction.removeMark(from, to, markType);
    }
  }

  if (!transaction.docChanged && transaction.storedMarks == null) {
    return false;
  }

  view.dispatch(transaction);
  return true;
};

const hasAncestorNode = (view: EditorView | null, nodeName: string) => {
  if (!view) {
    return false;
  }

  const { $from } = view.state.selection;
  for (let depth = $from.depth; depth >= 0; depth--) {
    if ($from.node(depth).type.name === nodeName) {
      return true;
    }
  }

  return false;
};

const getCurrentHeadingLevel = (view: EditorView | null) => {
  if (!view) {
    return 0;
  }

  const { $from } = view.state.selection;
  for (let depth = $from.depth; depth >= 0; depth--) {
    const node = $from.node(depth);
    if (node.type.name === 'heading') {
      return Number(node.attrs.level ?? 0);
    }
  }

  return 0;
};

const hasMark = (view: EditorView | null, markName: string) => {
  if (!view) {
    return false;
  }

  const { state } = view;
  const markType = state.schema.marks[markName];
  if (!markType) {
    return false;
  }

  const { from, to, empty } = state.selection;
  if (empty) {
    const marks = state.storedMarks ?? state.selection.$from.marks();
    return marks.some((mark) => mark.type === markType);
  }

  let found = false;
  state.doc.nodesBetween(from, to, (node) => {
    if (found || !node.isText) {
      return;
    }

    if (markType.isInSet(node.marks)) {
      found = true;
    }
  });

  return found;
};

const isParagraphActive = (env: EditorActionEnvironment, ctx: ActionStateContext) => {
  if (ctx.sourceMode) {
    const lines = getSelectedLines(env.sourceEditor);
    return lines.some((line) => line.trim().length > 0) &&
      lines.every((line) => !headingRegex.test(line) && !quoteRegex.test(line) && !orderedListRegex.test(line) && !unorderedListRegex.test(line));
  }

  return !hasAncestorNode(ctx.view, 'heading') &&
    !hasAncestorNode(ctx.view, 'blockquote') &&
    !hasAncestorNode(ctx.view, 'ordered_list') &&
    !hasAncestorNode(ctx.view, 'bullet_list') &&
    !hasAncestorNode(ctx.view, 'code_block');
};

const isHeadingActive = (env: EditorActionEnvironment, ctx: ActionStateContext, args?: EditorActionArgs) => {
  const expectedLevel = getHeadingLevelArg(args);
  if (ctx.sourceMode) {
    return getSelectedLines(env.sourceEditor)
      .filter((line) => line.trim().length > 0)
      .every((line) => (headingRegex.exec(line)?.[1].length ?? 0) === expectedLevel);
  }

  return getCurrentHeadingLevel(ctx.view) === expectedLevel;
};

const getSelectedLines = (textarea: HTMLTextAreaElement) => {
  const { lineStart, lineEnd } = getLineSelection(textarea.value, textarea.selectionStart, textarea.selectionEnd);
  return textarea.value.slice(lineStart, lineEnd).split('\n');
};

const isCurrentLineMatch = (textarea: HTMLTextAreaElement, regex: RegExp) => {
  const { lineStart, lineEnd } = getLineSelection(textarea.value, textarea.selectionStart, textarea.selectionStart);
  const line = textarea.value.slice(lineStart, lineEnd);
  return regex.test(line);
};

const isWrappedSelection = (textarea: HTMLTextAreaElement, prefix: string, suffix: string) => {
  const { selectionStart, selectionEnd, value } = textarea;
  if (selectionStart === selectionEnd) {
    return false;
  }

  return (
    selectionStart >= prefix.length &&
    selectionEnd + suffix.length <= value.length &&
    value.slice(selectionStart - prefix.length, selectionStart) === prefix &&
    value.slice(selectionEnd, selectionEnd + suffix.length) === suffix
  );
};

const runSourceParagraph = (env: EditorActionEnvironment) =>
  mutateSelectedLines(env, 'paragraphParagraph', (lines) =>
    lines.map((line) => {
      const leading = line.match(/^\s*/)?.[0] ?? '';
      const body = line.trimStart()
        .replace(headingRegex, '')
        .replace(quoteRegex, '')
        .replace(taskListRegex, '')
        .replace(orderedListRegex, '')
        .replace(unorderedListRegex, '');
      return body.length > 0 ? `${leading}${body}` : line;
    }),
  );

const runSourceHeading = (env: EditorActionEnvironment, args?: EditorActionArgs) =>
  mutateSelectedLines(env, 'paragraphHeading', (lines) => {
    const level = getHeadingLevelArg(args);
    const prefix = `${'#'.repeat(level)} `;
    return lines.map((line) => {
      if (!line.trim()) {
        return line;
      }

      const body = line.trimStart()
        .replace(headingRegex, '')
        .replace(quoteRegex, '');
      return `${prefix}${body}`;
    });
  });

const runSourceIncreaseHeading = (env: EditorActionEnvironment) =>
  mutateSelectedLines(env, 'paragraphHeadingIncrease', (lines) =>
    lines.map((line) => {
      const match = headingRegex.exec(line);
      if (!match) {
        return `# ${line.trim()}`;
      }

      const nextLevel = Math.min(6, match[1].length + 1);
      return `${'#'.repeat(nextLevel)} ${line.trimStart().replace(headingRegex, '')}`;
    }),
  );

const runSourceDecreaseHeading = (env: EditorActionEnvironment) =>
  mutateSelectedLines(env, 'paragraphHeadingDecrease', (lines) =>
    lines.map((line) => {
      const match = headingRegex.exec(line);
      if (!match) {
        return line;
      }

      const nextLevel = match[1].length - 1;
      const body = line.trimStart().replace(headingRegex, '');
      return nextLevel <= 0 ? body : `${'#'.repeat(nextLevel)} ${body}`;
    }),
  );

const runSourceToggleQuote = (env: EditorActionEnvironment) =>
  mutateSelectedLines(env, 'paragraphQuote', (lines) => {
    const shouldRemove = lines.filter((line) => line.trim().length > 0).every((line) => quoteRegex.test(line));
    return lines.map((line) => {
      if (!line.trim()) {
        return line;
      }

      return shouldRemove ? line.replace(quoteRegex, '') : `> ${line}`;
    });
  });

const runSourceOrderedList = (env: EditorActionEnvironment) =>
  mutateSelectedLines(env, 'paragraphOrderedList', (lines) =>
    lines.map((line, index) => {
      if (!line.trim()) {
        return line;
      }

      const body = stripListMarkers(line);
      return `${index + 1}. ${body}`;
    }),
  );

const runSourceUnorderedList = (env: EditorActionEnvironment) =>
  mutateSelectedLines(env, 'paragraphUnorderedList', (lines) =>
    lines.map((line) => (line.trim() ? `- ${stripListMarkers(line)}` : line)),
  );

const runSourceTaskList = (env: EditorActionEnvironment) =>
  mutateSelectedLines(env, 'paragraphTaskList', (lines) =>
    lines.map((line) => (line.trim() ? `- [ ] ${stripListMarkers(line)}` : line)),
  );

const runSourceCodeFence = (env: EditorActionEnvironment) =>
  wrapSelection(env, 'paragraphCodeFence', '\n```text\n', '\n```\n', 'code');

const runSourceMathBlock = (env: EditorActionEnvironment) =>
  wrapSelection(env, 'paragraphMathBlock', '\n$$\n', '\n$$\n', 'math');

const runSourceTable = (env: EditorActionEnvironment) =>
  replaceSelection(env, 'paragraphTable', '| Column 1 | Column 2 | Column 3 |\n| --- | --- | --- |\n| Value 1 | Value 2 | Value 3 |');

const runSourceFootnote = (env: EditorActionEnvironment) => {
  const textarea = env.sourceEditor;
  const selectedText = textarea.value.slice(textarea.selectionStart, textarea.selectionEnd) || 'footnote';
  const nextIndex = findNextFootnoteIndex(env.getCurrentMarkdown());
  const marker = `[^${nextIndex}]`;
  const nextMarkdown = `${textarea.value.slice(0, textarea.selectionStart)}${selectedText}${marker}${textarea.value.slice(textarea.selectionEnd)}\n\n[^${nextIndex}]: note`;

  env.commitSourceEditorMutation('paragraphFootnote', () => {
    textarea.value = nextMarkdown;
    const markerStart = nextMarkdown.indexOf(marker);
    textarea.selectionStart = markerStart;
    textarea.selectionEnd = markerStart + marker.length;
  });
  return true;
};

const runSourceHorizontalRule = (env: EditorActionEnvironment) =>
  replaceSelection(env, 'paragraphHorizontalRule', '\n\n---\n\n');

const runSourceBold = (env: EditorActionEnvironment) => wrapSelection(env, 'formatBold', '**', '**', 'bold');
const runSourceItalic = (env: EditorActionEnvironment) => wrapSelection(env, 'formatItalic', '*', '*', 'italic');
const runSourceUnderline = (env: EditorActionEnvironment) => wrapSelection(env, 'formatUnderline', '<u>', '</u>', 'underlined');
const runSourceStrikethrough = (env: EditorActionEnvironment) => wrapSelection(env, 'formatStrike', '~~', '~~', 'struck');
const runSourceInlineCode = (env: EditorActionEnvironment) => wrapSelection(env, 'formatInlineCode', '`', '`', 'code');
const runSourceInlineMath = (env: EditorActionEnvironment) => wrapSelection(env, 'formatInlineMath', '$', '$', 'x');
const runSourceHighlight = (env: EditorActionEnvironment) => wrapSelection(env, 'formatHighlight', '<mark>', '</mark>', 'highlight');
const runSourceSuperscript = (env: EditorActionEnvironment) => wrapSelection(env, 'formatSuperscript', '<sup>', '</sup>', 'sup');
const runSourceSubscript = (env: EditorActionEnvironment) => wrapSelection(env, 'formatSubscript', '<sub>', '</sub>', 'sub');

const runSourceLink = (env: EditorActionEnvironment) => {
  const textarea = env.sourceEditor;
  const selectedText = textarea.value.slice(textarea.selectionStart, textarea.selectionEnd) || 'link text';
  return replaceSelection(env, 'formatLink', `[${selectedText}](https://example.com)`, selectedText.length + 3, selectedText.length + 22);
};

const runSourceImage = (env: EditorActionEnvironment) =>
  replaceSelection(env, 'formatImage', '![alt text](path/to/image.png)', 2, 10);

const runSourceClearFormatting = (env: EditorActionEnvironment) => {
  const textarea = env.sourceEditor;
  const { selectionStart, selectionEnd } = textarea;
  if (selectionStart === selectionEnd) {
    return false;
  }

  const selectedText = textarea.value.slice(selectionStart, selectionEnd)
    .replace(/^\*\*(.*)\*\*$/s, '$1')
    .replace(/^\*(.*)\*$/s, '$1')
    .replace(/^~~(.*)~~$/s, '$1')
    .replace(/^`(.*)`$/s, '$1')
    .replace(/^\$(.*)\$$/s, '$1')
    .replace(/^<u>(.*)<\/u>$/s, '$1')
    .replace(/^<mark>(.*)<\/mark>$/s, '$1')
    .replace(/^<sup>(.*)<\/sup>$/s, '$1')
    .replace(/^<sub>(.*)<\/sub>$/s, '$1')
    .replace(/^\[(.*)\]\((.*)\)$/s, '$1')
    .replace(/^!\[(.*)\]\((.*)\)$/s, '$1');

  return replaceSelection(env, 'formatClear', selectedText);
};

const wrapSelection = (
  env: EditorActionEnvironment,
  inputType: string,
  prefix: string,
  suffix: string,
  placeholder: string,
) => {
  const textarea = env.sourceEditor;
  const selectedText = textarea.value.slice(textarea.selectionStart, textarea.selectionEnd) || placeholder;
  const start = prefix.length;
  return replaceSelection(env, inputType, `${prefix}${selectedText}${suffix}`, start, start + selectedText.length);
};

const replaceSelection = (
  env: EditorActionEnvironment,
  inputType: string,
  replacement: string,
  selectionStartOffset?: number,
  selectionEndOffset?: number,
) => {
  const textarea = env.sourceEditor;
  env.commitSourceEditorMutation(inputType, () => {
    const start = textarea.selectionStart;
    const end = textarea.selectionEnd;
    textarea.value = `${textarea.value.slice(0, start)}${replacement}${textarea.value.slice(end)}`;
    const nextSelectionStart = start + (selectionStartOffset ?? replacement.length);
    const nextSelectionEnd = start + (selectionEndOffset ?? replacement.length);
    textarea.selectionStart = nextSelectionStart;
    textarea.selectionEnd = nextSelectionEnd;
  });
  return true;
};

const mutateSelectedLines = (
  env: EditorActionEnvironment,
  inputType: string,
  mutateLines: (lines: string[]) => string[],
) => {
  const textarea = env.sourceEditor;
  env.commitSourceEditorMutation(inputType, () => {
    const selection = getLineSelection(textarea.value, textarea.selectionStart, textarea.selectionEnd);
    const currentLines = textarea.value.slice(selection.lineStart, selection.lineEnd).split('\n');
    const nextBlock = mutateLines(currentLines).join('\n');
    textarea.value = `${textarea.value.slice(0, selection.lineStart)}${nextBlock}${textarea.value.slice(selection.lineEnd)}`;
    textarea.selectionStart = selection.lineStart;
    textarea.selectionEnd = selection.lineStart + nextBlock.length;
  });
  return true;
};

const stripListMarkers = (line: string) =>
  line.trimStart()
    .replace(taskListRegex, '')
    .replace(orderedListRegex, '')
    .replace(unorderedListRegex, '')
    .replace(quoteRegex, '')
    .replace(headingRegex, '');

const getLineSelection = (value: string, start: number, end: number) => {
  const lineStart = value.lastIndexOf('\n', Math.max(0, start - 1)) + 1;
  const nextBreak = value.indexOf('\n', end);
  const lineEnd = nextBreak === -1 ? value.length : nextBreak;
  return { lineStart, lineEnd };
};

const findNextFootnoteIndex = (markdown: string) => {
  const matches = [...markdown.matchAll(/\[\^(\d+)\]/g)];
  const max = matches.reduce((current, match) => Math.max(current, Number(match[1] ?? 0)), 0);
  return max + 1;
};

const actionDefinitions: EditorActionDefinition[] = [
  { id: 'paragraph.paragraph', shortcut: 'Ctrl+0', isSupportedInRich: true, isSupportedInSource: true, runRich: runRichParagraph, runSource: runSourceParagraph, getActive: isParagraphActive },
  { id: 'paragraph.heading', shortcut: 'Ctrl+1', isSupportedInRich: true, isSupportedInSource: true, runRich: runRichHeading, runSource: runSourceHeading, getActive: isHeadingActive },
  { id: 'paragraph.heading.increase', shortcut: 'Ctrl+Alt+]', isSupportedInRich: true, isSupportedInSource: true, runRich: runRichIncreaseHeading, runSource: runSourceIncreaseHeading, getActive: (_env, ctx) => hasAncestorNode(ctx.view, 'heading') },
  { id: 'paragraph.heading.decrease', shortcut: 'Ctrl+Alt+[', isSupportedInRich: true, isSupportedInSource: true, runRich: runRichDecreaseHeading, runSource: runSourceDecreaseHeading, getActive: (_env, ctx) => hasAncestorNode(ctx.view, 'heading') },
  { id: 'paragraph.quote', shortcut: 'Ctrl+Alt+Q', isSupportedInRich: true, isSupportedInSource: true, runRich: runRichCommand(wrapInBlockquoteCommand.key), runSource: runSourceToggleQuote, getActive: (env, ctx) => hasAncestorNode(ctx.view, 'blockquote') || isCurrentLineMatch(env.sourceEditor, quoteRegex) },
  { id: 'paragraph.ordered-list', shortcut: 'Ctrl+Alt+7', isSupportedInRich: true, isSupportedInSource: true, runRich: runRichCommand(wrapInOrderedListCommand.key), runSource: runSourceOrderedList, getActive: (env, ctx) => hasAncestorNode(ctx.view, 'ordered_list') || isCurrentLineMatch(env.sourceEditor, orderedListRegex) },
  { id: 'paragraph.unordered-list', shortcut: 'Ctrl+Alt+8', isSupportedInRich: true, isSupportedInSource: true, runRich: runRichCommand(wrapInBulletListCommand.key), runSource: runSourceUnorderedList, getActive: (env, ctx) => hasAncestorNode(ctx.view, 'bullet_list') || isCurrentLineMatch(env.sourceEditor, unorderedListRegex) },
  { id: 'paragraph.task-list', shortcut: 'Ctrl+Alt+9', isSupportedInRich: false, isSupportedInSource: true, runSource: runSourceTaskList, getActive: (env) => isCurrentLineMatch(env.sourceEditor, taskListRegex) },
  { id: 'paragraph.code-fence', shortcut: 'Ctrl+Shift+K', isSupportedInRich: true, isSupportedInSource: true, runRich: runRichCommand(createCodeBlockCommand.key), runSource: runSourceCodeFence, getActive: (_env, ctx) => hasAncestorNode(ctx.view, 'code_block') },
  { id: 'paragraph.math-block', shortcut: 'Ctrl+Alt+M', isSupportedInRich: false, isSupportedInSource: true, runSource: runSourceMathBlock },
  { id: 'paragraph.table', shortcut: 'Ctrl+Alt+T', isSupportedInRich: true, isSupportedInSource: true, runRich: runRichInsertTable, runSource: runSourceTable, getActive: (_env, ctx) => hasAncestorNode(ctx.view, 'table') },
  { id: 'paragraph.footnote', shortcut: 'Ctrl+Alt+F', isSupportedInRich: false, isSupportedInSource: true, runSource: runSourceFootnote },
  { id: 'paragraph.horizontal-rule', shortcut: 'Ctrl+Alt+H', isSupportedInRich: true, isSupportedInSource: true, runRich: runRichCommand(insertHrCommand.key), runSource: runSourceHorizontalRule },
  { id: 'format.bold', shortcut: 'Ctrl+B', isSupportedInRich: true, isSupportedInSource: true, runRich: runRichCommand(toggleStrongCommand.key), runSource: runSourceBold, getActive: (env, ctx) => hasMark(ctx.view, 'strong') || isWrappedSelection(env.sourceEditor, '**', '**') },
  { id: 'format.italic', shortcut: 'Ctrl+I', isSupportedInRich: true, isSupportedInSource: true, runRich: runRichCommand(toggleEmphasisCommand.key), runSource: runSourceItalic, getActive: (env, ctx) => hasMark(ctx.view, 'emphasis') || isWrappedSelection(env.sourceEditor, '*', '*') },
  { id: 'format.underline', shortcut: 'Ctrl+U', isSupportedInRich: false, isSupportedInSource: true, runSource: runSourceUnderline, getActive: (env) => isWrappedSelection(env.sourceEditor, '<u>', '</u>') },
  { id: 'format.strikethrough', shortcut: 'Ctrl+Alt+S', isSupportedInRich: true, isSupportedInSource: true, runRich: runRichCommand(toggleStrikethroughCommand.key), runSource: runSourceStrikethrough, getActive: (env, ctx) => hasMark(ctx.view, 'strike_through') || isWrappedSelection(env.sourceEditor, '~~', '~~') },
  { id: 'format.inline-code', shortcut: 'Ctrl+Shift+`', isSupportedInRich: true, isSupportedInSource: true, runRich: runRichCommand(toggleInlineCodeCommand.key), runSource: runSourceInlineCode, getActive: (env, ctx) => hasMark(ctx.view, 'inlineCode') || isWrappedSelection(env.sourceEditor, '`', '`') },
  { id: 'format.inline-math', shortcut: 'Ctrl+Alt+K', isSupportedInRich: false, isSupportedInSource: true, runSource: runSourceInlineMath, getActive: (env) => isWrappedSelection(env.sourceEditor, '$', '$') },
  { id: 'format.link', shortcut: 'Ctrl+K', isSupportedInRich: true, isSupportedInSource: true, runRich: runRichLink, runSource: runSourceLink, getActive: (_env, ctx) => hasMark(ctx.view, 'link') },
  { id: 'format.image', shortcut: 'Ctrl+Shift+I', isSupportedInRich: true, isSupportedInSource: true, runRich: runRichImage, runSource: runSourceImage },
  { id: 'format.highlight', shortcut: 'Ctrl+Shift+H', isSupportedInRich: false, isSupportedInSource: true, runSource: runSourceHighlight, getActive: (env) => isWrappedSelection(env.sourceEditor, '<mark>', '</mark>') },
  { id: 'format.superscript', shortcut: 'Ctrl+.', isSupportedInRich: false, isSupportedInSource: true, runSource: runSourceSuperscript, getActive: (env) => isWrappedSelection(env.sourceEditor, '<sup>', '</sup>') },
  { id: 'format.subscript', shortcut: 'Ctrl+,', isSupportedInRich: false, isSupportedInSource: true, runSource: runSourceSubscript, getActive: (env) => isWrappedSelection(env.sourceEditor, '<sub>', '</sub>') },
  { id: 'format.clear', shortcut: 'Ctrl+\\', isSupportedInRich: true, isSupportedInSource: true, runRich: runRichClearFormatting, runSource: runSourceClearFormatting },
];

const actionMap = new Map(actionDefinitions.map((action) => [action.id, action]));
