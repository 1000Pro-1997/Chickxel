# Chickxel default rules

## Outlines
- Outlines are 1 px. Use the darkest (hue-tinted) shade of the material instead of pure black.
- On the lit side the outline may be one step lighter or dropped (selective outline). Inner contour lines are lighter than the outer outline.
- Curves step evenly (segment lengths 1-2-3 or 3-2-1). Uneven runs read as bumpy (jaggies).
- Remove doubles (lines two pixels thick) and stray pixels in L-shaped corners.

## Light and shading
- One light direction (usually top-left), the same for the whole sprite and animation.
- 3–4 shade ramp per material: shadow → base → light (+ one highlight pixel).
- Hue-shift the ramp: darker shades cooler (toward blue/purple), lighter shades warmer (toward yellow); saturation peaks in the middle.
- Put shadows on the side away from the light and at the bottom. No pillow shading (bright center with dark ring around the edge).
- No orphan pixels (single pixels not connected to anything) except intended sparkles/highlights.

## Color
- Keep the palette small: 8–16 colors for a small sprite, 3–4 per material.
- Avoid near-duplicate colors; keep clear steps between shades. Dither only sparingly on large areas.
- Keep enough value contrast inside the silhouette to separate it from the background.

## Shape and proportions
- At small sizes (16–32 px) the silhouette alone should read: exaggerate head, weapon and key features.
- Keep proportions consistent within a set (e.g. a 16 px character's head is about half its height; a 32 px character is about 2–2.5 heads tall).
- Front-facing characters start symmetric; only the shading differs according to the light.
- Tiles must wrap seamlessly on all four edges (check by repeating them), with at most one eye-catching detail per tile.

## Animation
- Draw key poses first, then in-betweens. Walk cycles start from contact (foot touches down) and passing poses.
- Walk cycles of 4–8 frames: the body is lowest on contact frames and highest on passing frames.
- Show fast motion with one or two smear frames.
- Squash and stretch keep volume: wider means shorter, taller means narrower.
- Keep outline thickness, palette and light direction identical across frames. Typical frame duration 80–150 ms.
