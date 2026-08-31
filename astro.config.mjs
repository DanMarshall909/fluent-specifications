import { defineConfig } from 'astro/config';
import { parameterHintTransformer } from './tools/parameter-hint-transformer.mjs';

export default defineConfig({
  site: 'https://fluent-specifications.danmarshall.dev',
  outDir: './docs',
  trailingSlash: 'always',
  build: {
    assets: '_astro',
  },
  markdown: {
    shikiConfig: {
      theme: 'github-dark',
      transformers: [parameterHintTransformer()],
    },
  },
});
