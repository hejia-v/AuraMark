import type { Ctx } from '@milkdown/ctx';
import type { Node as ProseNode } from '@milkdown/prose/model';
import type { Decoration, EditorView, NodeView, NodeViewConstructor } from '@milkdown/prose/view';

import { prismConfig } from '@milkdown/plugin-prism';
import { codeBlockSchema } from '@milkdown/preset-commonmark';
import { $view } from '@milkdown/utils';
import { refractor } from 'refractor';
import type { Refractor } from 'refractor/core';
import bash from 'refractor/bash';
import c from 'refractor/c';
import cpp from 'refractor/cpp';
import csharp from 'refractor/csharp';
import css from 'refractor/css';
import diff from 'refractor/diff';
import go from 'refractor/go';
import graphql from 'refractor/graphql';
import java from 'refractor/java';
import javascript from 'refractor/javascript';
import json from 'refractor/json';
import jsx from 'refractor/jsx';
import markdown from 'refractor/markdown';
import markup from 'refractor/markup';
import php from 'refractor/php';
import powershell from 'refractor/powershell';
import python from 'refractor/python';
import rust from 'refractor/rust';
import sql from 'refractor/sql';
import tsx from 'refractor/tsx';
import typescript from 'refractor/typescript';
import yaml from 'refractor/yaml';

type LanguageOption = {
  label: string;
  value: string;
  aliases?: string[];
};

type RefractorSyntax = {
  (target: Refractor): void;
  aliases?: string[];
  displayName?: string;
};

type CopyState = 'copied' | 'error' | 'idle';

const CODE_BLOCK_LOCALE_EVENT = 'auramark:code-block-locale-change';

const SVG_NS = 'http://www.w3.org/2000/svg';

const createSvg = (pathD: string, viewBox = '0 0 24 24'): SVGSVGElement => {
  const svg = document.createElementNS(SVG_NS, 'svg');
  svg.setAttribute('viewBox', viewBox);
  svg.setAttribute('fill', 'none');
  svg.setAttribute('stroke', 'currentColor');
  svg.setAttribute('stroke-width', '2');
  svg.setAttribute('stroke-linecap', 'round');
  svg.setAttribute('stroke-linejoin', 'round');
  svg.classList.add('am-code-block-icon');

  const path = document.createElementNS(SVG_NS, 'path');
  path.setAttribute('d', pathD);
  svg.appendChild(path);
  return svg;
};

const createCopyIcon = (): SVGSVGElement => {
  const svg = document.createElementNS(SVG_NS, 'svg');
  svg.setAttribute('viewBox', '0 0 24 24');
  svg.setAttribute('fill', 'none');
  svg.setAttribute('stroke', 'currentColor');
  svg.setAttribute('stroke-width', '2');
  svg.setAttribute('stroke-linecap', 'round');
  svg.setAttribute('stroke-linejoin', 'round');
  svg.classList.add('am-code-block-icon');
  // clipboard rect
  const rect = document.createElementNS(SVG_NS, 'rect');
  rect.setAttribute('x', '9');
  rect.setAttribute('y', '9');
  rect.setAttribute('width', '13');
  rect.setAttribute('height', '13');
  rect.setAttribute('rx', '2');
  svg.appendChild(rect);
  // back rect
  const path = document.createElementNS(SVG_NS, 'path');
  path.setAttribute('d', 'M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1');
  svg.appendChild(path);
  return svg;
};

const ICON_CHECK_D = 'M20 6L9 17l-5-5';
const ICON_X_D = 'M18 6L6 18M6 6l12 12';
const ICON_CODE_D = 'M16 18l6-6-6-6M8 6l-6 6 6 6';

const languageOptions: LanguageOption[] = [
  { value: '', label: 'Plain text', aliases: ['text', 'plain', 'plaintext', 'txt'] },
  { value: 'bash', label: 'Bash', aliases: ['sh', 'shell'] },
  { value: 'c', label: 'C' },
  { value: 'cpp', label: 'C++', aliases: ['c++'] },
  { value: 'csharp', label: 'C#', aliases: ['cs', 'dotnet'] },
  { value: 'css', label: 'CSS' },
  { value: 'diff', label: 'Diff' },
  { value: 'go', label: 'Go' },
  { value: 'graphql', label: 'GraphQL', aliases: ['gql'] },
  { value: 'html', label: 'HTML', aliases: ['markup'] },
  { value: 'java', label: 'Java' },
  { value: 'javascript', label: 'JavaScript', aliases: ['js'] },
  { value: 'json', label: 'JSON' },
  { value: 'jsx', label: 'React JSX' },
  { value: 'markdown', label: 'Markdown', aliases: ['md'] },
  { value: 'php', label: 'PHP' },
  { value: 'powershell', label: 'PowerShell', aliases: ['ps1'] },
  { value: 'python', label: 'Python', aliases: ['py'] },
  { value: 'rust', label: 'Rust', aliases: ['rs'] },
  { value: 'sql', label: 'SQL' },
  { value: 'tsx', label: 'React TSX' },
  { value: 'typescript', label: 'TypeScript', aliases: ['ts'] },
  { value: 'xml', label: 'XML' },
  { value: 'yaml', label: 'YAML', aliases: ['yml'] },
];

const registeredSyntaxes: RefractorSyntax[] = [
  bash,
  c,
  cpp,
  csharp,
  css,
  diff,
  go,
  graphql,
  java,
  javascript,
  json,
  jsx,
  markdown,
  markup,
  php,
  powershell,
  python,
  rust,
  sql,
  tsx,
  typescript,
  yaml,
];

const labelByValue = new Map(languageOptions.map((option) => [option.value, option.label]));

const aliasToValue = new Map<string, string>();

for (const option of languageOptions) {
  aliasToValue.set(option.value.toLowerCase(), option.value);
  for (const alias of option.aliases ?? []) {
    aliasToValue.set(alias.toLowerCase(), option.value);
  }
}

const copyText = async (text: string) => {
  try {
    if (navigator.clipboard?.writeText) {
      await navigator.clipboard.writeText(text);
      return true;
    }
  } catch {
    // fall through to execCommand fallback
  }

  const textarea = document.createElement('textarea');
  textarea.value = text;
  textarea.setAttribute('readonly', 'true');
  textarea.style.position = 'fixed';
  textarea.style.left = '-9999px';
  textarea.style.top = '0';
  document.body.appendChild(textarea);
  textarea.select();

  try {
    return document.execCommand('copy');
  } finally {
    textarea.remove();
  }
};

const isChinese = (language: string) => language.toLowerCase().startsWith('zh');

const getStrings = (language: string) => {
  if (isChinese(language)) {
    return {
      changeLanguage: '切换代码语言',
      copy: '复制',
      copied: '已复制',
      copyFailed: '复制失败',
      plainText: '纯文本',
    };
  }

  return {
    changeLanguage: 'Change code language',
    copy: 'Copy',
    copied: 'Copied',
    copyFailed: 'Copy failed',
    plainText: 'Plain text',
  };
};

const ensureRegistered = (target: Refractor, syntax: RefractorSyntax) => {
  const name = syntax.displayName;
  if (name && target.registered(name)) {
    return;
  }

  target.register(syntax);
};

const configureRefractor = (target: Refractor) => {
  for (const syntax of registeredSyntaxes) {
    ensureRegistered(target, syntax);
  }

  return target;
};

const normalizeLanguage = (value: string) => {
  const trimmed = value.trim().toLowerCase();
  if (!trimmed) {
    return '';
  }

  return aliasToValue.get(trimmed) ?? trimmed;
};

const titleCase = (value: string) =>
  value
    .split(/[-_\s]+/)
    .filter(Boolean)
    .map((part) => part.slice(0, 1).toUpperCase() + part.slice(1))
    .join(' ');

const getLanguageLabel = (value: string, uiLanguage: string) => {
  if (!value) {
    return getStrings(uiLanguage).plainText;
  }

  return labelByValue.get(value) ?? titleCase(value);
};

const createLineNumberFragment = (count: number) => {
  const fragment = document.createDocumentFragment();
  for (let index = 1; index <= count; index++) {
    const line = document.createElement('span');
    line.className = 'am-code-block-line-number';
    line.textContent = String(index);
    fragment.appendChild(line);
  }

  return fragment;
};

const getLineCount = (code: string) => {
  if (code.length === 0) {
    return 1;
  }

  return code.split('\n').length;
};

class EnhancedCodeBlockView implements NodeView {
  dom: HTMLDivElement;
  contentDOM: HTMLElement;

  private node: ProseNode;
  private readonly view: EditorView;
  private readonly getPos: (() => number | undefined) | boolean;
  private readonly getUiLanguage: () => string;
  private readonly toolbar: HTMLDivElement;
  private readonly selectWrap: HTMLDivElement;
  private readonly select: HTMLSelectElement;
  private readonly copyButton: HTMLButtonElement;
  private readonly gutter: HTMLDivElement;
  private readonly pre: HTMLPreElement;
  private readonly code: HTMLElement;
  private copyResetTimer = 0;
  private copyState: CopyState = 'idle';
  private lineCount = 0;
  private readonly localeListener: () => void;

  constructor(node: ProseNode, view: EditorView, getPos: (() => number | undefined) | boolean, getUiLanguage: () => string) {
    this.node = node;
    this.view = view;
    this.getPos = getPos;
    this.getUiLanguage = getUiLanguage;

    this.dom = document.createElement('div');
    this.dom.className = 'am-code-block';

    this.toolbar = document.createElement('div');
    this.toolbar.className = 'am-code-block-toolbar';
    this.dom.appendChild(this.toolbar);

    const langGroup = document.createElement('div');
    langGroup.className = 'am-code-block-lang-group';
    this.toolbar.appendChild(langGroup);

    const langIcon = createSvg(ICON_CODE_D);
    langIcon.classList.add('am-code-block-lang-icon');
    langGroup.appendChild(langIcon);

    this.selectWrap = document.createElement('div');
    this.selectWrap.className = 'am-code-block-select-wrap';
    langGroup.appendChild(this.selectWrap);

    this.select = document.createElement('select');
    this.select.className = 'am-code-block-select';
    this.selectWrap.appendChild(this.select);

    const body = document.createElement('div');
    body.className = 'am-code-block-body';
    this.dom.appendChild(body);

    this.copyButton = document.createElement('button');
    this.copyButton.type = 'button';
    this.copyButton.className = 'am-code-block-copy';
    this.copyButton.appendChild(createCopyIcon());
    this.dom.appendChild(this.copyButton);

    this.gutter = document.createElement('div');
    this.gutter.className = 'am-code-block-gutter';
    this.gutter.setAttribute('aria-hidden', 'true');
    body.appendChild(this.gutter);

    this.pre = document.createElement('pre');
    this.pre.className = 'am-code-block-pre';
    body.appendChild(this.pre);

    this.code = document.createElement('code');
    this.code.className = 'am-code-block-code';
    this.pre.appendChild(this.code);
    this.contentDOM = this.code;

    this.select.addEventListener('change', this.handleLanguageChange);
    this.copyButton.addEventListener('click', this.handleCopy);

    this.localeListener = () => {
      this.applyLocale();
      this.syncLanguageOptions(this.node.attrs.language as string);
    };
    window.addEventListener(CODE_BLOCK_LOCALE_EVENT, this.localeListener);

    this.applyNode(node);
    this.applyLocale();
  }

  update(node: ProseNode) {
    if (node.type !== this.node.type) {
      return false;
    }

    this.applyNode(node);
    return true;
  }

  stopEvent(event: Event) {
    const target = event.target;
    if (!(target instanceof HTMLElement) && !(target instanceof SVGElement)) {
      return false;
    }

    return target.closest('.am-code-block-toolbar') !== null || target.closest('.am-code-block-copy') !== null;
  }

  ignoreMutation(mutation: MutationRecord | { type: 'selection'; target: Node }) {
    if (mutation.type === 'selection') {
      return false;
    }

    return !this.code.contains(mutation.target);
  }

  destroy() {
    this.select.removeEventListener('change', this.handleLanguageChange);
    this.copyButton.removeEventListener('click', this.handleCopy);
    window.removeEventListener(CODE_BLOCK_LOCALE_EVENT, this.localeListener);
    if (this.copyResetTimer) {
      window.clearTimeout(this.copyResetTimer);
    }
  }

  private readonly handleLanguageChange = () => {
    if (typeof this.getPos !== 'function') {
      return;
    }

    const pos = this.getPos();
    if (typeof pos !== 'number') {
      return;
    }

    const nextLanguage = this.select.value;
    this.view.dispatch(this.view.state.tr.setNodeAttribute(pos, 'language', nextLanguage));
    this.view.focus();
  };

  private readonly handleCopy = async () => {
    const copied = await copyText(this.node.textContent);
    this.setCopyState(copied ? 'copied' : 'error');
  };

  private applyLocale() {
    const strings = getStrings(this.getUiLanguage());
    this.select.setAttribute('aria-label', strings.changeLanguage);
    this.select.title = strings.changeLanguage;

    if (this.copyState === 'copied') {
      this.copyButton.replaceChildren(createSvg(ICON_CHECK_D));
      this.copyButton.title = strings.copied;
      return;
    }

    if (this.copyState === 'error') {
      this.copyButton.replaceChildren(createSvg(ICON_X_D));
      this.copyButton.title = strings.copyFailed;
      return;
    }

    this.copyButton.replaceChildren(createCopyIcon());
    this.copyButton.title = strings.copy;
  }

  private applyNode(node: ProseNode) {
    this.node = node;
    const rawLanguage = typeof node.attrs.language === 'string' ? node.attrs.language : '';
    const normalizedLanguage = normalizeLanguage(rawLanguage);
    const languageLabel = getLanguageLabel(normalizedLanguage, this.getUiLanguage());

    this.dom.dataset.language = normalizedLanguage || 'plain-text';
    this.pre.dataset.language = languageLabel;
    this.code.dataset.language = normalizedLanguage || 'plain-text';
    this.code.className = normalizedLanguage
      ? `am-code-block-code language-${normalizedLanguage}`
      : 'am-code-block-code';

    this.syncLanguageOptions(rawLanguage);
    this.select.value = normalizedLanguage;
    this.updateLineNumbers(node.textContent);
  }

  private syncLanguageOptions(rawLanguage: string) {
    const normalizedLanguage = normalizeLanguage(rawLanguage);
    const strings = getStrings(this.getUiLanguage());
    const options = languageOptions.map((option) => ({
      ...option,
      label: option.value ? option.label : strings.plainText,
    }));

    if (normalizedLanguage && !options.some((option) => option.value === normalizedLanguage)) {
      options.push({
        value: normalizedLanguage,
        label: getLanguageLabel(normalizedLanguage, this.getUiLanguage()),
      });
    }

    const currentSignature = options.map((option) => `${option.value}:${option.label}`).join('|');
    if (this.select.dataset.signature === currentSignature) {
      return;
    }

    this.select.dataset.signature = currentSignature;
    this.select.replaceChildren();

    for (const option of options) {
      const optionElement = document.createElement('option');
      optionElement.value = option.value;
      optionElement.textContent = option.label;
      this.select.appendChild(optionElement);
    }
  }

  private updateLineNumbers(code: string) {
    const nextLineCount = getLineCount(code);
    if (nextLineCount === this.lineCount) {
      return;
    }

    this.lineCount = nextLineCount;
    this.gutter.replaceChildren(createLineNumberFragment(nextLineCount));
  }

  private setCopyState(state: CopyState) {
    this.copyState = state;
    this.copyButton.classList.toggle('is-copied', state === 'copied');
    this.copyButton.classList.toggle('is-error', state === 'error');
    this.applyLocale();

    if (this.copyResetTimer) {
      window.clearTimeout(this.copyResetTimer);
    }

    if (state !== 'idle') {
      this.copyResetTimer = window.setTimeout(() => {
        this.copyState = 'idle';
        this.copyButton.classList.remove('is-copied', 'is-error');
        this.applyLocale();
        this.copyResetTimer = 0;
      }, 1600);
    }
  }
}

export const configureCodeBlockPrism = (ctx: Ctx) => {
  ctx.set(prismConfig.key, {
    configureRefractor: (target) => configureRefractor(target),
  });
};

export const createCodeBlockView = (getUiLanguage: () => string) =>
  $view(codeBlockSchema.node, (): NodeViewConstructor => {
    return (node, view, getPos, _decorations: readonly Decoration[], _innerDecorations): NodeView => {
      return new EnhancedCodeBlockView(node, view, getPos, getUiLanguage);
    };
  });

export const notifyCodeBlockLocaleChange = () => {
  window.dispatchEvent(new Event(CODE_BLOCK_LOCALE_EVENT));
};

configureRefractor(refractor);
