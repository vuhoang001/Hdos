// @ts-check
// Sidebar layout theo Diátaxis (4 quadrant) + ADR riêng.
// Mỗi quadrant có "intent" rõ ràng cho người đọc:
//   - Tutorials   → học, có deliverable cụ thể
//   - How-to      → giải task cụ thể
//   - Reference   → tra cứu khô, đầy đủ
//   - Explanation → hiểu vì sao
//   - ADR         → quyết định kiến trúc đã chốt

/** @type {import('@docusaurus/plugin-content-docs').SidebarsConfig} */
const sidebars = {
  mainSidebar: [
    'intro',
    {
      type: 'category',
      label: '🚀 Tutorials',
      link: { type: 'generated-index', title: 'Tutorials', description: 'Học từ con số 0 — mỗi bài có sản phẩm cuối cụ thể.' },
      collapsed: false,
      items: [
        'tutorials/setup-project',
        'tutorials/add-first-feature',
      ],
    },
    {
      type: 'category',
      label: '🛠️ How-to Guides',
      link: { type: 'generated-index', title: 'How-to Guides', description: 'Cách giải quyết một task cụ thể trong dự án.' },
      collapsed: false,
      items: [
        'how-to/add-authentication',
        'how-to/add-rest-endpoint',
        'how-to/create-migration',
        'how-to/debug-401',
        'how-to/setup-cicd',
        'how-to/add-new-service',
      ],
    },
    {
      type: 'category',
      label: '📖 Reference',
      link: { type: 'generated-index', title: 'Reference', description: 'Tra cứu — đầy đủ, khô, không kể chuyện.' },
      collapsed: false,
      items: [
        'reference/api-overview',
        'reference/folder-structure',
        'reference/ports-and-config',
        'reference/cicd-architecture',
        'reference/glossary',
      ],
    },
    {
      type: 'category',
      label: '💡 Explanation',
      link: { type: 'generated-index', title: 'Explanation', description: 'Hiểu vì sao thiết kế thế này.' },
      collapsed: false,
      items: [
        'explanation/why-clean-architecture',
        'explanation/domain-vs-integration-events',
        'explanation/jwt-security-model',
        'explanation/request-flow',
        {
          type: 'category',
          label: 'C4 Diagrams',
          link: { type: 'generated-index', title: 'C4 Model', description: 'Sơ đồ kiến trúc theo C4 model — Context / Container / Component.' },
          collapsed: false,
          items: [
            'explanation/c4/context',
            'explanation/c4/container',
            'explanation/c4/component-auth',
          ],
        },
      ],
    },
  ],

  adrSidebar: [
    {
      type: 'category',
      label: '📐 ADR — Architecture Decision Records',
      link: { type: 'doc', id: 'adr/index' },
      collapsed: false,
      items: [
        'adr/template',
        'adr/0001-record-architecture-decisions',
        'adr/0002-split-rest-grpc-ports',
      ],
    },
  ],
};

export default sidebars;
