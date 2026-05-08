// @ts-check
// Docusaurus config for Hdos docs (Diátaxis + C4 + ADR)
// https://docusaurus.io/docs/api/docusaurus-config

import { themes as prismThemes } from 'prism-react-renderer';

/** @type {import('@docusaurus/types').Config} */
const config = {
  title: 'Hdos Docs',
  tagline: 'Microservices .NET 8 — Clean Architecture, CQRS, RabbitMQ, gRPC',
  favicon: 'img/favicon.ico',

  url: 'https://hdos.example.com',
  baseUrl: '/',

  organizationName: 'hdos',
  projectName: 'hdos-docs',

  onBrokenLinks: 'warn',

  // Mermaid để render C4 diagrams
  markdown: {
    mermaid: true,
    hooks: {
      onBrokenMarkdownLinks: 'warn',
    },
  },
  themes: [
    '@docusaurus/theme-mermaid',
    [
      '@easyops-cn/docusaurus-search-local',
      /** @type {import('@easyops-cn/docusaurus-search-local').PluginOptions} */
      ({
        hashed: true,
        language: ['en', 'vi'],   // Vietnamese tokenizer
        indexDocs: true,
        indexBlog: false,
        indexPages: false,
        docsRouteBasePath: '/',   // khớp routeBasePath ở presets.docs
        highlightSearchTermsOnTargetPage: true,
        searchResultLimits: 10,
        searchResultContextMaxLength: 80,
        // explicitSearchResultPath: true,
      }),
    ],
  ],

  i18n: {
    defaultLocale: 'vi',
    locales: ['vi'],
  },

  presets: [
    [
      'classic',
      /** @type {import('@docusaurus/preset-classic').Options} */
      ({
        docs: {
          sidebarPath: './sidebars.js',
          routeBasePath: '/',           // docs là homepage, không có blog/landing
          editUrl:
            'https://github.com/your-org/hdos/edit/main/docs-site/',
          showLastUpdateTime: true,
          showLastUpdateAuthor: true,
          numberPrefixParser: false,    // GIỮ "0001-" trong slug (cho ADR)
        },
        blog: false,                    // tắt blog
        theme: {
          customCss: './src/css/custom.css',
        },
      }),
    ],
  ],

  themeConfig:
    /** @type {import('@docusaurus/preset-classic').ThemeConfig} */
    ({
      image: 'img/social-card.png',
      colorMode: {
        defaultMode: 'light',
        respectPrefersColorScheme: true,
      },
      mermaid: {
        theme: { light: 'neutral', dark: 'dark' },
      },
      navbar: {
        title: 'Hdos',
        logo: {
          alt: 'Hdos Logo',
          src: 'img/logo.svg',
        },
        items: [
          {
            type: 'docSidebar',
            sidebarId: 'mainSidebar',
            position: 'left',
            label: 'Tài liệu',
          },
          {
            type: 'docSidebar',
            sidebarId: 'adrSidebar',
            position: 'left',
            label: 'ADR',
          },
          {
            href: 'https://github.com/your-org/hdos',
            label: 'GitHub',
            position: 'right',
          },
        ],
      },
      footer: {
        style: 'dark',
        links: [
          {
            title: 'Tài liệu',
            items: [
              { label: 'Tutorials', to: '/tutorials/setup-project' },
              { label: 'How-to', to: '/how-to/add-authentication' },
              { label: 'Reference', to: '/reference/api-overview' },
              { label: 'Explanation', to: '/explanation/why-clean-architecture' },
            ],
          },
          {
            title: 'Decisions',
            items: [
              { label: 'ADR Index', to: '/adr/' },
              { label: 'Glossary', to: '/glossary' },
            ],
          },
          {
            title: 'External',
            items: [
              { label: 'Diátaxis', href: 'https://diataxis.fr' },
              { label: 'C4 Model', href: 'https://c4model.com' },
              { label: 'ADR', href: 'https://adr.github.io' },
            ],
          },
        ],
        copyright: `Copyright © ${new Date().getFullYear()} Hdos.`,
      },
      prism: {
        theme: prismThemes.github,
        darkTheme: prismThemes.dracula,
        additionalLanguages: ['csharp', 'bash', 'json', 'yaml', 'protobuf'],
      },
      docs: {
        sidebar: {
          hideable: true,
          autoCollapseCategories: false,
        },
      },
    }),
};

export default config;
