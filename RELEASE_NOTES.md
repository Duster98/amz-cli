# AMZ CLI 3.2.7

This release includes all 3.2.6 features.

## Product image viewer

- Product details now open a full-screen image viewer when the main image is
  clicked or tapped.
- The viewer keeps every seller image in a thumbnail strip and supports
  previous/next navigation with buttons, arrow keys, or horizontal swipe.
- Zoom from 1x to 4x with the mouse wheel, trackpad, pinch, or the zoom
  controls; drag to pan when zoomed. A click or tap toggles a 2.5x zoom at the
  pointer.
- On desktop with a mouse, hovering the image shows a magnifier lens and a
  2.5x detail panel over the purchase column; it closes when the pointer leaves
  the image.
- The hover panel now keeps the magnified canvas inside its stage, with no
  few-pixel spill into the footer.
- The viewer traps focus, restores it on close, respects reduced motion, and
  works in light and dark themes.
- No changes to the parser, API, or CLI output.
