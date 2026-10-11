import { defineConfig } from 'vitepress'
import llmstxt from 'vitepress-plugin-llms'
import { withMermaid } from "vitepress-plugin-mermaid"

export default withMermaid(defineConfig({
  title: 'Foundatio Lucene',
  description: 'Dynamic Lucene-style query capabilities for .NET with Entity Framework and Elasticsearch support',
  base: '/',
  ignoreDeadLinks: false,
  markdown: {
    lineNumbers: false
  },
  vite: {
    plugins: [
      llmstxt({
        ignoreFiles: ['node_modules/**', '.vitepress/**']
      })
    ]
  },
  head: [
    ['link', { rel: 'icon', href: 'https://raw.githubusercontent.com/FoundatioFx/Foundatio/main/media/foundatio-icon.png', type: 'image/png' }],
    ['meta', { name: 'theme-color', content: '#3c8772' }]
  ],
  themeConfig: {
    logo: {
      light: 'https://raw.githubusercontent.com/FoundatioFx/Foundatio/master/media/foundatio.svg',
      dark: 'https://raw.githubusercontent.com/FoundatioFx/Foundatio/master/media/foundatio-dark-bg.svg'
    },
    siteTitle: 'Lucene',
    nav: [
      { text: 'Guide', link: '/guide/what-is-foundatio-lucene' },
      { text: 'GitHub', link: 'https://github.com/FoundatioFx/Foundatio.Lucene' }
    ],
    sidebar: {
      '/guide/': [
        {
          text: 'Introduction',
          items: [
            { text: 'What is Foundatio.Lucene?', link: '/guide/what-is-foundatio-lucene' },
            { text: 'Getting Started', link: '/guide/getting-started' },
            { text: 'Migrating from Foundatio.Parsers', link: '/guide/migrating-from-parsers' }
          ]
        },
        {
          text: 'Core Concepts',
          items: [
            { text: 'Query Syntax', link: '/guide/query-syntax' },
            { text: 'Sorting and Aggregations', link: '/guide/sorting-and-aggregations' },
            { text: 'Field Mapping', link: '/guide/field-mapping' },
            { text: 'Validation', link: '/guide/validation' },
            { text: 'Configuration', link: '/guide/configuration' }
          ]
        },
        {
          text: 'Integrations',
          items: [
            { text: 'Elasticsearch', link: '/guide/elasticsearch' },
            { text: 'Elasticsearch Mappings', link: '/guide/elasticsearch-mappings' },
            { text: 'Entity Framework', link: '/guide/entity-framework' }
          ]
        },
        {
          text: 'Advanced Topics',
          items: [
            { text: 'Visitors', link: '/guide/visitors' },
            { text: 'Custom Visitors', link: '/guide/custom-visitors' },
            { text: 'Date Math', link: '/guide/date-math' },
            { text: 'Security', link: '/guide/security' },
            { text: 'Performance', link: '/guide/performance' }
          ]
        }
      ]
    },
    socialLinks: [
      { icon: 'github', link: 'https://github.com/FoundatioFx/Foundatio.Lucene' },
      { icon: 'discord', link: 'https://discord.gg/6HxgFCx' }
    ],
    footer: {
      message: 'Released under the Apache 2.0 License.',
      copyright: 'Copyright © 2025 Foundatio'
    },
    editLink: {
      pattern: 'https://github.com/FoundatioFx/Foundatio.Lucene/edit/main/docs/:path'
    },
    search: {
      provider: 'local'
    }
  }
}))
