import { fromMarkdown } from 'mdast-util-from-markdown';
import { gfm } from 'micromark-extension-gfm';
import { gfmFromMarkdown } from 'mdast-util-gfm';

type MarkdownPoint = {
  offset?: number | null;
};

type MarkdownPosition = {
  start?: MarkdownPoint | null;
  end?: MarkdownPoint | null;
};

type MarkdownNode = {
  type?: string;
  value?: string;
  position?: MarkdownPosition | null;
  children?: MarkdownNode[];
  [key: string]: unknown;
};

type TextLeaf = {
  path: string;
  value: string;
  startOffset: number;
  endOffset: number;
};

type TextReplacement = {
  startOffset: number;
  endOffset: number;
  value: string;
};

const splitMarkdownLines = (markdown: string) => markdown.split(/\r\n|\n|\r/);

const detectLineEnding = (markdown: string) => {
  const match = markdown.match(/\r\n|\n|\r/);
  return match?.[0] ?? '\n';
};

const normalizeComparableLine = (line: string) => {
  let next = line.replace(/\u00a0+$/gu, '').replace(/\s+$/u, '');
  next = next.replace(/^(\s*)[*+-](?=\s|$)/u, '$1-');
  next = next.replace(/^(\s*)\d+[.)](?=\s|$)/u, '$11.');
  next = next.replace(/^(\s*(?:[-+*]|\d+[.)]))\s+<br\s*\/?>\s*$/iu, '$1');
  return next;
};

const buildLcsMatches = (
  left: string[],
  right: string[],
  areEqual: (leftValue: string, rightValue: string) => boolean,
) => {
  const lengths = Array.from({ length: left.length + 1 }, () => Array<number>(right.length + 1).fill(0));

  for (let leftIndex = left.length - 1; leftIndex >= 0; leftIndex--) {
    for (let rightIndex = right.length - 1; rightIndex >= 0; rightIndex--) {
      lengths[leftIndex][rightIndex] = areEqual(left[leftIndex], right[rightIndex])
        ? lengths[leftIndex + 1][rightIndex + 1] + 1
        : Math.max(lengths[leftIndex + 1][rightIndex], lengths[leftIndex][rightIndex + 1]);
    }
  }

  const matches: Array<[number, number]> = [];
  let leftIndex = 0;
  let rightIndex = 0;
  while (leftIndex < left.length && rightIndex < right.length) {
    if (areEqual(left[leftIndex], right[rightIndex])) {
      matches.push([leftIndex, rightIndex]);
      leftIndex++;
      rightIndex++;
      continue;
    }

    if (lengths[leftIndex + 1][rightIndex] >= lengths[leftIndex][rightIndex + 1]) {
      leftIndex++;
    } else {
      rightIndex++;
    }
  }

  return matches;
};

const parseMarkdown = (markdown: string): MarkdownNode => {
  return fromMarkdown(markdown, {
    extensions: [gfm()],
    mdastExtensions: [gfmFromMarkdown()],
  }) as MarkdownNode;
};

const stripPositions = (value: unknown): unknown => {
  if (Array.isArray(value)) {
    return value.map((item) => stripPositions(item));
  }

  if (!value || typeof value !== 'object') {
    return value;
  }

  const node = value as MarkdownNode;
  const result: Record<string, unknown> = {};
  const keys = Object.keys(node).sort();
  for (const key of keys) {
    if (key === 'position' || key === 'spread' || key === 'start') {
      continue;
    }

    if (key === 'value' && node.type === 'text') {
      result[key] = '__TEXT__';
      continue;
    }

    result[key] = stripPositions(node[key]);
  }

  return result;
};

const collectTextLeaves = (node: MarkdownNode, path: number[] = [], leaves: TextLeaf[] = []) => {
  if (node.type === 'text') {
    const startOffset = node.position?.start?.offset;
    const endOffset = node.position?.end?.offset;
    if (typeof startOffset === 'number' && typeof endOffset === 'number') {
      leaves.push({
        path: path.join('.'),
        value: typeof node.value === 'string' ? node.value : '',
        startOffset,
        endOffset,
      });
    }

    return leaves;
  }

  if (!Array.isArray(node.children)) {
    return leaves;
  }

  node.children.forEach((child, index) => {
    collectTextLeaves(child, [...path, index], leaves);
  });
  return leaves;
};

const buildPatchedMarkdown = (markdown: string, replacements: TextReplacement[]) => {
  let next = markdown;
  for (const replacement of replacements.sort((a, b) => b.startOffset - a.startOffset)) {
    next =
      next.slice(0, replacement.startOffset) +
      replacement.value +
      next.slice(replacement.endOffset);
  }

  return next;
};

export const patchMarkdownTextPreservingLayout = (
  originalMarkdown: string,
  previousCanonicalMarkdown: string,
  nextCanonicalMarkdown: string,
) => {
  if (previousCanonicalMarkdown === nextCanonicalMarkdown) {
    return originalMarkdown;
  }

  try {
    const originalTree = parseMarkdown(originalMarkdown);
    const previousTree = parseMarkdown(previousCanonicalMarkdown);
    const nextTree = parseMarkdown(nextCanonicalMarkdown);

    const originalShape = JSON.stringify(stripPositions(originalTree));
    const previousShape = JSON.stringify(stripPositions(previousTree));
    const nextShape = JSON.stringify(stripPositions(nextTree));
    if (originalShape !== previousShape || previousShape !== nextShape) {
      return null;
    }

    const originalLeaves = collectTextLeaves(originalTree);
    const previousLeaves = collectTextLeaves(previousTree);
    const nextLeaves = collectTextLeaves(nextTree);
    if (originalLeaves.length !== previousLeaves.length || previousLeaves.length !== nextLeaves.length) {
      return null;
    }

    const replacements: TextReplacement[] = [];
    for (let i = 0; i < originalLeaves.length; i++) {
      const originalLeaf = originalLeaves[i];
      const previousLeaf = previousLeaves[i];
      const nextLeaf = nextLeaves[i];
      if (originalLeaf.path !== previousLeaf.path || previousLeaf.path !== nextLeaf.path) {
        return null;
      }

      if (previousLeaf.value === nextLeaf.value) {
        continue;
      }

      replacements.push({
        startOffset: originalLeaf.startOffset,
        endOffset: originalLeaf.endOffset,
        value: nextLeaf.value,
      });
    }

    if (replacements.length === 0) {
      return originalMarkdown;
    }

    return buildPatchedMarkdown(originalMarkdown, replacements);
  } catch {
    return null;
  }
};

export const patchMarkdownStructurePreservingLayout = (
  originalMarkdown: string,
  previousCanonicalMarkdown: string,
  nextCanonicalMarkdown: string,
) => {
  if (previousCanonicalMarkdown === nextCanonicalMarkdown) {
    return originalMarkdown;
  }

  const originalLines = splitMarkdownLines(originalMarkdown);
  const previousLines = splitMarkdownLines(previousCanonicalMarkdown);
  const nextLines = splitMarkdownLines(nextCanonicalMarkdown);
  const originalLineEnding = detectLineEnding(originalMarkdown);

  const previousToOriginalLine = Array<number>(previousLines.length).fill(-1);
  for (const [previousLineIndex, originalLineIndex] of buildLcsMatches(
    previousLines,
    originalLines,
    (previousLine, originalLine) => normalizeComparableLine(previousLine) === normalizeComparableLine(originalLine),
  )) {
    previousToOriginalLine[previousLineIndex] = originalLineIndex;
  }

  if (previousLines.length > 0 && previousToOriginalLine.every((lineIndex) => lineIndex < 0)) {
    return null;
  }

  const resultLines: string[] = [];
  let originalCursor = 0;
  let previousCursor = 0;
  let nextCursor = 0;

  for (const [previousLineIndex, nextLineIndex] of buildLcsMatches(
    previousLines,
    nextLines,
    (previousLine, nextLine) => previousLine === nextLine,
  )) {
    const originalLineIndex = previousToOriginalLine[previousLineIndex];
    if (originalLineIndex < 0 || originalLineIndex < originalCursor) {
      return null;
    }

    const previousChangedCount = previousLineIndex - previousCursor;
    if (previousChangedCount === 0) {
      resultLines.push(...originalLines.slice(originalCursor, originalLineIndex));
      resultLines.push(...nextLines.slice(nextCursor, nextLineIndex));
    } else {
      resultLines.push(...nextLines.slice(nextCursor, nextLineIndex));
    }

    resultLines.push(originalLines[originalLineIndex]);
    originalCursor = originalLineIndex + 1;
    previousCursor = previousLineIndex + 1;
    nextCursor = nextLineIndex + 1;
  }

  const remainingPreviousChangedCount = previousLines.length - previousCursor;
  if (remainingPreviousChangedCount === 0) {
    resultLines.push(...originalLines.slice(originalCursor));
    resultLines.push(...nextLines.slice(nextCursor));
  } else {
    resultLines.push(...nextLines.slice(nextCursor));
  }

  return resultLines.join(originalLineEnding);
};
