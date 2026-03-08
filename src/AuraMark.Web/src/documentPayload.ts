import { parseDocument as parseYamlDocument, stringify as stringifyYaml } from 'yaml';

export type MetadataEntry = {
  key: string;
  kind: 'scalar' | 'list' | 'object';
  displayText?: string;
  items?: string[];
  structuredText?: string;
};

export type DocumentPayload = {
  rawMarkdown: string;
  frontMatterRaw: string;
  bodyMarkdown: string;
  metadata: MetadataEntry[];
};

const frontMatterRegex = /^---[ \t]*\r?\n([\s\S]*?)\r?\n---[ \t]*(?:\r?\n|$)/;

const isScalar = (value: unknown): value is string | number | boolean | null => {
  return value === null || ['string', 'number', 'boolean'].includes(typeof value);
};

const isPlainRecord = (value: unknown): value is Record<string, unknown> => {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
};

const toScalarText = (value: string | number | boolean | null) => {
  if (value === null) {
    return 'null';
  }

  return String(value);
};

const toStructuredText = (value: unknown) => {
  const serialized = stringifyYaml(value).trim();
  return serialized.length > 0 ? serialized : 'null';
};

const createMetadataEntry = (key: string, value: unknown): MetadataEntry => {
  if (isScalar(value)) {
    return {
      key,
      kind: 'scalar',
      displayText: toScalarText(value),
    };
  }

  if (Array.isArray(value) && value.every(isScalar)) {
    return {
      key,
      kind: 'list',
      items: value.map((item) => toScalarText(item)),
    };
  }

  return {
    key,
    kind: 'object',
    structuredText: toStructuredText(value),
  };
};

const normalizeMetadataEntry = (entry: Partial<MetadataEntry>): MetadataEntry | null => {
  if (typeof entry.key !== 'string' || entry.key.length === 0) {
    return null;
  }

  if (entry.kind === 'list') {
    return {
      key: entry.key,
      kind: 'list',
      items: Array.isArray(entry.items) ? entry.items.filter((item): item is string => typeof item === 'string') : [],
    };
  }

  if (entry.kind === 'object') {
    return {
      key: entry.key,
      kind: 'object',
      structuredText: typeof entry.structuredText === 'string' ? entry.structuredText : 'null',
    };
  }

  return {
    key: entry.key,
    kind: 'scalar',
    displayText: typeof entry.displayText === 'string' ? entry.displayText : '',
  };
};

export const composeRawMarkdown = (frontMatterRaw: string, bodyMarkdown: string) => {
  return `${frontMatterRaw || ''}${bodyMarkdown || ''}`;
};

export const createRawDocument = (rawMarkdown: string): DocumentPayload => ({
  rawMarkdown,
  frontMatterRaw: '',
  bodyMarkdown: rawMarkdown,
  metadata: [],
});

export const normalizeDocumentPayload = (value: Partial<DocumentPayload> | null | undefined): DocumentPayload => {
  if (!value) {
    return createRawDocument('');
  }

  const frontMatterRaw = typeof value.frontMatterRaw === 'string' ? value.frontMatterRaw : '';
  const bodyMarkdown =
    typeof value.bodyMarkdown === 'string'
      ? value.bodyMarkdown
      : typeof value.rawMarkdown === 'string'
        ? value.rawMarkdown
        : '';
  const rawMarkdown =
    typeof value.rawMarkdown === 'string' && value.rawMarkdown.length > 0
      ? value.rawMarkdown
      : composeRawMarkdown(frontMatterRaw, bodyMarkdown);
  const metadata = Array.isArray(value.metadata)
    ? value.metadata
        .map((entry) => normalizeMetadataEntry(entry))
        .filter((entry): entry is MetadataEntry => entry !== null)
    : [];

  return {
    rawMarkdown,
    frontMatterRaw,
    bodyMarkdown,
    metadata,
  };
};

export const parseRawDocument = (rawMarkdown: string): DocumentPayload => {
  const safeRawMarkdown = rawMarkdown || '';
  const match = safeRawMarkdown.match(frontMatterRegex);
  if (!match) {
    return createRawDocument(safeRawMarkdown);
  }

  const yamlSource = match[1] ?? '';
  const yamlDocument = parseYamlDocument(yamlSource);
  if (yamlDocument.errors.length > 0) {
    return createRawDocument(safeRawMarkdown);
  }

  const data = yamlDocument.toJS();
  if (data !== null && !isPlainRecord(data)) {
    return createRawDocument(safeRawMarkdown);
  }

  return {
    rawMarkdown: safeRawMarkdown,
    frontMatterRaw: match[0],
    bodyMarkdown: safeRawMarkdown.slice(match[0].length),
    metadata: Object.entries(data ?? {}).map(([key, value]) => createMetadataEntry(key, value)),
  };
};

export const parseIncomingDocument = (content: string): DocumentPayload => {
  if (!content) {
    return createRawDocument('');
  }

  try {
    const parsed = JSON.parse(content) as Partial<DocumentPayload>;
    if (typeof parsed === 'object' && parsed && 'rawMarkdown' in parsed && 'bodyMarkdown' in parsed) {
      return normalizeDocumentPayload(parsed);
    }
  } catch {
    // Fall back to plain markdown content.
  }

  return parseRawDocument(content);
};
