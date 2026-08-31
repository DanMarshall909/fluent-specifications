import { readFileSync } from 'node:fs';

const parameterHints = JSON.parse(
  readFileSync(
    new URL('../src/generated/parameter-hints.json', import.meta.url),
    'utf8',
  ),
);

function readSymbol(rawMetadata = '') {
  const match = rawMetadata.match(/(?:^|\s)symbol=(?:"([^"]+)"|'([^']+)'|([^\s]+))/);
  return match?.[1] ?? match?.[2] ?? match?.[3];
}

export function parameterHintTransformer() {
  return {
    name: 'fluent-specifications:parameter-hints',
    preprocess(_code, options) {
      const symbol = readSymbol(options.meta?.__raw);
      const hints = parameterHints[symbol] ?? [];
      if (hints.length === 0) return;

      options.decorations = [
        ...(options.decorations ?? []),
        ...hints.map((hint) => ({
          start: hint.offset,
          end: hint.offset,
          alwaysWrap: true,
          properties: {
            class: 'parameter-hint',
            'data-parameter-hint': `${hint.name}:`,
            'aria-hidden': 'true',
          },
        })),
      ];
    },
  };
}
