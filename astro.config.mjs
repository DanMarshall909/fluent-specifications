import { defineConfig } from 'astro/config';

export default defineConfig({
  site: 'https://fluent-spec.danmarshall.dev',
  outDir: './docs',
  trailingSlash: 'always',
  build: {
    assets: '_astro',
  },
  markdown: {
    shikiConfig: {
      theme: 'github-dark',
    },
  },
});
