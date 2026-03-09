const listItemStartRegex = /^(\s*)(?:[-+*]|\d+[.)])(?:\s|$)/;

const isBlankLine = (line: string) => line.trim().length === 0;

const isListRelatedLine = (line: string) => {
  if (listItemStartRegex.test(line)) {
    return true;
  }

  return /^\s{2,}\S/.test(line);
};

const shouldDropBlankLine = (previousLine: string | undefined, nextLine: string | undefined) => {
  if (!previousLine || !nextLine) {
    return false;
  }

  if (!isListRelatedLine(previousLine) || !isListRelatedLine(nextLine)) {
    return false;
  }

  return listItemStartRegex.test(previousLine) || listItemStartRegex.test(nextLine);
};

const collapseLooseListSpacing = (markdown: string) => {
  const lines = markdown.split(/\r?\n/);
  const result: string[] = [];

  for (let index = 0; index < lines.length; index++) {
    const line = lines[index];
    if (!isBlankLine(line)) {
      result.push(line);
      continue;
    }

    let previousLine: string | undefined;
    for (let i = result.length - 1; i >= 0; i--) {
      if (!isBlankLine(result[i])) {
        previousLine = result[i];
        break;
      }
    }

    let nextLine: string | undefined;
    for (let i = index + 1; i < lines.length; i++) {
      if (!isBlankLine(lines[i])) {
        nextLine = lines[i];
        break;
      }
    }

    if (shouldDropBlankLine(previousLine, nextLine)) {
      continue;
    }

    result.push(line);
  }

  return result.join('\n');
};

const headingLineRegex = /^\s{0,3}#{1,6}\s+\S/;

const collapseHeadingSpacing = (markdown: string) => {
  const lines = markdown.split(/\r?\n/);
  const result: string[] = [];

  for (let index = 0; index < lines.length; index++) {
    const line = lines[index];
    const previousLine = result[result.length - 1];
    const nextLine = lines[index + 1];

    if (isBlankLine(line) && headingLineRegex.test(nextLine ?? '')) {
      if (isBlankLine(previousLine ?? '')) {
        continue;
      }
    }

    if (headingLineRegex.test(previousLine ?? '') && isBlankLine(line)) {
      if (nextLine && !isBlankLine(nextLine)) {
        continue;
      }
    }

    result.push(line);
  }

  return result.join('\n');
};

const collapseParagraphListSpacing = (markdown: string) => {
  const lines = markdown.split(/\r?\n/);
  const result: string[] = [];

  for (let index = 0; index < lines.length; index++) {
    const line = lines[index];
    const previousLine = result[result.length - 1];
    const nextLine = lines[index + 1];

    if (
      isBlankLine(line) &&
      typeof previousLine === 'string' &&
      typeof nextLine === 'string' &&
      /[:：]\s*$/u.test(previousLine) &&
      listItemStartRegex.test(nextLine)
    ) {
      continue;
    }

    result.push(line);
  }

  return result.join('\n');
};

export const normalizeSerializedMarkdown = (markdown: string) => {
  let next = markdown;

  // Milkdown uses raw HTML `<br />` as a placeholder for empty list items.
  next = next.replace(/^(\s*(?:[-+*]|\d+[.)]))\s+<br\s*\/?>\s*$/gim, '$1');

  // Drop invisible NBSP placeholders at end of lines.
  next = next.replace(/\u00a0+(?=\r?$)/gim, '');

  // Prefer compact lists to avoid rewriting tight lists into loose ones.
  next = collapseLooseListSpacing(next);

  // Milkdown sometimes inserts presentation-only blank lines around headings.
  next = collapseHeadingSpacing(next);

  // Keep explanatory paragraph -> list transitions tight when the source was tight.
  next = collapseParagraphListSpacing(next);

  return next;
};
