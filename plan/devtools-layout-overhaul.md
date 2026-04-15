# DevTools Layout Overhaul

## Goals

- Reduce width consumed by nested detail panes.
- Stop showing secondary surfaces all at once on every page.
- Reuse one compact inspector pattern across tree, resource, style, binding, and memory details.
- Keep the primary workflow visible while moving secondary diagnostics into tabs.
- Improve narrow-window behavior without creating separate per-page designs.

## Reference Principles

- Prefer a 2-pane default over permanent 3-pane layouts.
- Treat extra diagnostics as contextual tabs, not always-visible regions.
- Keep the primary grid or tree visible; move metadata, box model, value sources, preview, and route views into tabs.
- Reduce default information density in property grids.
- Trim shell padding and allow header/footer content to scroll horizontally instead of forcing larger layouts.

## Implementation Plan

### 1. Shared compact inspector pattern

- Replace nested `property grid + details splitter` layouts with tabbed inspectors.
- Use `Properties` as the primary tab.
- Move selected-property chrome, metadata, layout tools, and value sources into secondary tabs.
- Apply the same structure to:
  - `ControlDetailsView`
  - `ResourceValueDetailsView`
  - `StyleValueDetailsView`
  - `BindingObjectDetailsView`
  - `MemoryObjectDetailsView`

### 2. Property grid density

- Remove low-value default columns from the shared property grid builder.
- Keep frequent scanning focused on property name and value.
- Surface extra metadata in the selected-property editor and value-source tabs instead of wide always-visible columns.

### 3. Page-level consolidation

- Keep one primary pane per page and move secondary surfaces into tabs.
- Refactor:
  - `ResourcesPageView` to `Resources` + `Inspector`
  - `StylesPageView` to `Entries` + `Inspector`
  - `BindingsPageView` to `Facts` + `Object Inspector`
  - `MemoryPageView` to `Snapshot`, `Tracked Objects`, and `Inspector`
  - `AssetsPageView` to `Assets` + `Preview`
  - `EventsPageView` to `Log` + `Route`

### 4. Shell compaction

- Reduce shell and page padding tokens.
- Slightly reduce title sizing and inspector section padding.
- Allow global header controls and footer status to scroll horizontally in narrow windows instead of expanding the layout.

### 5. Verification

- Build the solution after the refactor.
- Fix any compile or XAML issues introduced by the layout changes.

## Expected Outcome

- Tree pages stop paying for a nested right-side splitter.
- Detail panes stop reserving large fixed widths for cards and side metadata.
- Multi-surface pages stop stacking list, facts, preview, or route panels permanently.
- The shell remains usable in narrower windows with fewer hard minimum-width collisions.
