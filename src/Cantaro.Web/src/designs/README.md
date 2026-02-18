# Cantaro UI Designs

Current routes:

- `/1` Sidebar Dashboard
- `/2` Liquid Glass Morphism (reference visual style)
- `/3` Pulse Mosaic (redone)
- `/4` Ribbon Control Deck (redone)
- `/5` Masonry Feed baseline
- `/6` Panorama Command Deck
- `/7` Kanban Studio
- `/8` Orbit Studio
- `/9` Magazine Rails

## Direction

- Shared visual language follows Design 2: glass cards, gradients, soft blur, and rich hover states.
- New iterations keep the Design 1 sidebar structure for quick navigation and persistent context.
- Layouts vary aggressively in composition: rails, kanban lanes, radial canvas, staggered mosaics, and asymmetric bento grids.

## Extensibility

All post-`/5` designs use shared primitives in `LayoutShell.tsx` and `platformCatalog`, making it easy to add future platforms without reworking each layout from scratch.
