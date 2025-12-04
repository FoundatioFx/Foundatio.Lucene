# Foundatio.Lucene Documentation

This folder contains the VitePress v2.0 documentation for Foundatio.Lucene.

## Development

To run the documentation site locally:

```bash
cd docs
npm install
npm run dev
```

The documentation will be available at `http://localhost:5173/`

## Building

To build the documentation for production:

```bash
npm run build
```

The built site will be in the `docs/.vitepress/dist` directory.

## Configuration

VitePress configuration is in `.vitepress/config.ts` with:

- Navigation structure
- Theme customization
- Search configuration
- Build optimization

## Contributing

When updating the documentation:

1. **Test locally** - Always run `npm run dev` to verify changes
2. **Check navigation** - Ensure new pages are added to the sidebar
3. **Validate links** - Check internal links work correctly
4. **Update examples** - Keep examples practical and realistic

## Writing Guidelines

- Use clear, concise language
- Include practical examples for every concept
- Provide both basic and advanced usage patterns
- Add "Next Steps" sections to guide readers
