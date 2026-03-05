import { defineConfig } from 'vitepress'
import { withMermaid } from 'vitepress-plugin-mermaid'

export default withMermaid(
  defineConfig({
    title: 'Modulus',
    description: 'A CLI tool and library suite for scaffolding .NET modular monolith solutions',

    head: [
      ['link', { rel: 'icon', type: 'image/x-icon', href: '/Modulus/favicon.ico' }],
      ['link', { rel: 'icon', type: 'image/png', sizes: '32x32', href: '/Modulus/favicon-32x32.png' }],
      ['link', { rel: 'icon', type: 'image/png', sizes: '16x16', href: '/Modulus/favicon-16x16.png' }],
      ['link', { rel: 'apple-touch-icon', sizes: '180x180', href: '/Modulus/apple-touch-icon.png' }],
      ['meta', { property: 'og:title', content: 'Modulus' }],
      ['meta', { property: 'og:description', content: 'Scaffold production-ready modular monoliths in seconds' }],
      ['meta', { property: 'og:image', content: 'https://adamwyatt34.github.io/Modulus/og-image.png' }],
      ['meta', { property: 'og:type', content: 'website' }],
      ['meta', { name: 'twitter:card', content: 'summary_large_image' }],
    ],

    base: '/Modulus/',

    lastUpdated: true,

    themeConfig: {
      logo: '/logo.svg',

      nav: [
        { text: 'Guide', link: '/getting-started/' },
        { text: 'Architecture', link: '/architecture/' },
        { text: 'CLI', link: '/cli/' },
        {
          text: 'API',
          items: [
            { text: 'Mediator', link: '/mediator/' },
            { text: 'Messaging', link: '/messaging/' },
            { text: 'Source Generators', link: '/generators/' },
            { text: 'Analyzers', link: '/analyzers/' },
          ]
        },
        { text: 'Recipes', link: '/recipes/' },
        {
          text: 'v1.1.0',
          items: [
            { text: 'Changelog', link: 'https://github.com/adamwyatt34/Modulus/blob/main/CHANGELOG.md' },
            { text: 'NuGet', link: 'https://www.nuget.org/packages/Modulus.Cli' },
          ]
        }
      ],

      sidebar: {
        '/getting-started/': [
          {
            text: 'Getting Started',
            items: [
              { text: 'Prerequisites & Installation', link: '/getting-started/' },
              { text: 'Your First Solution', link: '/getting-started/first-solution' },
            ]
          }
        ],
        '/architecture/': [
          {
            text: 'Architecture',
            items: [
              { text: 'Overview', link: '/architecture/' },
              { text: 'Module Anatomy', link: '/architecture/module-anatomy' },
              { text: 'Building Blocks', link: '/architecture/building-blocks' },
              { text: 'Extracting to Microservices', link: '/architecture/extraction' },
            ]
          }
        ],
        '/mediator/': [
          {
            text: 'Mediator',
            items: [
              { text: 'Overview', link: '/mediator/' },
              { text: 'Commands & Queries', link: '/mediator/commands-queries' },
              { text: 'Result Pattern', link: '/mediator/result-pattern' },
              { text: 'Pipeline Behaviors', link: '/mediator/pipeline-behaviors' },
              { text: 'Domain Events', link: '/mediator/domain-events' },
              { text: 'Streaming Queries', link: '/mediator/streaming' },
            ]
          }
        ],
        '/generators/': [
          {
            text: 'Source Generators',
            items: [
              { text: 'Overview', link: '/generators/' },
              { text: 'Strongly Typed IDs', link: '/generators/strongly-typed-ids' },
              { text: 'Handler Registration', link: '/generators/handler-registration' },
              { text: 'Module Auto-Discovery', link: '/generators/module-discovery' },
            ]
          }
        ],
        '/analyzers/': [
          {
            text: 'Analyzers',
            items: [
              { text: 'Overview', link: '/analyzers/' },
              { text: 'Rule Reference', link: '/analyzers/rules' },
              { text: 'Configuration', link: '/analyzers/configuration' },
            ]
          }
        ],
        '/messaging/': [
          {
            text: 'Messaging',
            items: [
              { text: 'Overview', link: '/messaging/' },
              { text: 'Integration Events', link: '/messaging/integration-events' },
              { text: 'Message Bus', link: '/messaging/message-bus' },
              { text: 'Transports', link: '/messaging/transports' },
              { text: 'Outbox Pattern', link: '/messaging/outbox-pattern' },
              { text: 'Inbox Pattern', link: '/messaging/inbox-pattern' },
            ]
          }
        ],
        '/aspire/': [
          {
            text: 'Aspire Integration',
            items: [
              { text: 'Overview', link: '/aspire/' },
            ]
          }
        ],
        '/cli/': [
          {
            text: 'CLI Reference',
            items: [
              { text: 'Overview', link: '/cli/' },
              { text: 'modulus init', link: '/cli/init' },
              { text: 'modulus add-module', link: '/cli/add-module' },
              { text: 'modulus list-modules', link: '/cli/list-modules' },
              { text: 'modulus add-entity', link: '/cli/add-entity' },
              { text: 'modulus add-command', link: '/cli/add-command' },
              { text: 'modulus add-query', link: '/cli/add-query' },
              { text: 'modulus add-endpoint', link: '/cli/add-endpoint' },
              { text: 'modulus version', link: '/cli/version' },
            ]
          }
        ],
        '/testing/': [
          {
            text: 'Testing',
            items: [
              { text: 'Overview', link: '/testing/' },
              { text: 'Architecture Tests', link: '/testing/architecture-tests' },
              { text: 'Unit Testing', link: '/testing/unit-testing' },
              { text: 'Integration Testing', link: '/testing/integration-testing' },
            ]
          }
        ],
        '/recipes/': [
          {
            text: 'Recipes',
            items: [
              { text: 'Overview', link: '/recipes/' },
              { text: 'Authentication', link: '/recipes/authentication' },
              { text: 'Caching', link: '/recipes/caching' },
              { text: 'Strongly Typed IDs', link: '/recipes/strongly-typed-ids' },
              { text: 'Sagas', link: '/recipes/sagas' },
              { text: 'API Versioning', link: '/recipes/api-versioning' },
              { text: 'Health Checks', link: '/recipes/health-checks' },
            ]
          }
        ],
        '/contributing/': [
          {
            text: 'Contributing',
            items: [
              { text: 'Guide', link: '/contributing/' },
            ]
          }
        ],
      },

      editLink: {
        pattern: 'https://github.com/adamwyatt34/Modulus/edit/main/docs/:path',
        text: 'Edit this page on GitHub'
      },

      socialLinks: [
        { icon: 'github', link: 'https://github.com/adamwyatt34/Modulus' }
      ],

      search: {
        provider: 'local'
      },

      footer: {
        message: 'Released under the MIT License.',
        copyright: 'Copyright 2026 Adam Wyatt'
      },

      outline: {
        level: [2, 3]
      }
    },

    markdown: {
      lineNumbers: true
    },

    mermaid: {},
  })
)
