import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';

const configuredBase = process.env.RUNE_DOCS_BASE ?? '/Rune/';
const base = configuredBase.endsWith('/') ? configuredBase : `${configuredBase}/`;

export default defineConfig({
  site: 'https://jedrek0429.github.io',
  base,
  integrations: [
    starlight({
      title: 'Rune',
      description: 'Documentation for Rune, its bot interface, and Rune.Api.',
      customCss: ['./src/styles/custom.css'],
      head: [
        {
          tag: 'link',
          attrs: {
            rel: 'icon',
            type: 'image/png',
            href: `${base}favicon.png`,
          },
        },
      ],
      components: {
        PageTitle: './src/components/PageTitle.astro',
        Footer: './src/components/Footer.astro',
      },
      social: [
        {
          icon: 'github',
          label: 'GitHub',
          href: 'https://github.com/jedrek0429/Rune',
        },
      ],
      sidebar: [
        {
          label: 'Articles',
          items: [
            { label: 'Quick Start', link: '/guides/quick-start/' },
            { label: 'How Rune works', link: '/guides/architecture/' },
            { label: 'Security model', link: '/guides/security/' },
          ],
        },
        {
          label: 'Bot Reference',
          items: [
            { label: 'Overview', link: '/bot/generated/' },
            {
              label: 'Commands',
              items: [
                { autogenerate: { directory: 'bot/generated/commands' } },
              ],
            },
          ],
        },
        {
          label: 'API Reference',
          items: [
            { label: 'Overview', link: '/api/generated/' },
            { label: 'Events', link: '/api/generated/events/' },
            {
              label: 'Types',
              items: [
                { autogenerate: { directory: 'api/generated/types' } },
              ],
            },
          ],
        },
      ],
    }),
  ],
});
