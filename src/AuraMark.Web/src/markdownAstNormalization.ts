type MarkdownNode = {
  type?: string;
  value?: string;
  spread?: boolean;
  children?: MarkdownNode[];
  [key: string]: unknown;
};

const isPlaceholderBreakHtml = (node: MarkdownNode) => {
  if (node.type !== 'html' || typeof node.value !== 'string') {
    return false;
  }

  const normalized = node.value.trim().toLowerCase();
  return normalized === '<br />' || normalized === '<br/>' || normalized === '<br>';
};

const normalizeNode = (node: MarkdownNode, parent: MarkdownNode | null) => {
  if ((node.type === 'list' || node.type === 'listItem') && typeof node.spread === 'boolean') {
    node.spread = false;
  }

  if (node.type === 'text' && typeof node.value === 'string') {
    node.value = node.value.replace(/\u00a0+$/gu, '');
  }

  if (node.type === 'listItem' && Array.isArray(node.children) && node.children.length === 1) {
    const onlyChild = node.children[0];
    if (isPlaceholderBreakHtml(onlyChild)) {
      node.children = [{ type: 'paragraph', children: [] }];
    }
  }

  if (parent?.type === 'paragraph' && node.type === 'html' && isPlaceholderBreakHtml(node)) {
    node.type = 'text';
    node.value = '';
  }

  if (!Array.isArray(node.children)) {
    return;
  }

  for (const child of node.children) {
    normalizeNode(child, node);
  }
};

export const createMarkdownAstNormalizationPlugin = () => {
  return (tree: MarkdownNode) => {
    normalizeNode(tree, null);
  };
};
