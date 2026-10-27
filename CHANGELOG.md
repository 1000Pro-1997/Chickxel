# Changes

When releasing a new version, add a `## vX.Y.Z (date)` section at the top. This English file is the original; write the same section in Korean in `CHANGELOG.ko.md` with the same heading (`## vX.Y.Z (date)`). Inside a section, give each change a one-line summary subheading (`### Subheading`) followed directly by two to five short bullet points (`- Sentence`). The release notes (English, then Korean) and the app's update window (Korean for the Korean UI, English for every other UI language) read that version's section from these two files.

## v1.14.40 (2026-10-27)
### Screenshots in the user guide
- The homepage guide and the in-app guide (F1) now show screenshots of the real app on nearly every page, including right-click menus, dropdowns and settings popups.
- What each page explains is marked on the image with a red box and a red number, and the text and the list under each image refer to the same numbers (①②…).
- One set of English screenshots is used for both the Korean and English guides. Click an image on the homepage to open it at full size.
- Eight pages also show a looping recording of the action (number fields, document tabs, selecting and editing in the timeline, tags, layer effects, transform mode and canvas size mode).

## v1.14.39 (2026-10-27)
### Guide screenshots
- Prepared screenshots of the real app for nearly every page of the user guide: 148 images each in Korean and English, using the wolf-ear adventurer as the example art. They will be placed into the homepage and in-app (F1) guide next.
- The images are produced by a script that runs the actual UI without a screen, so they can be refreshed whenever the UI changes. Pages that need motion are listed as recording requests.

## v1.14.38 (2026-10-27)
### Wider hue-weight search for Pixelate
- OKLCh hue weight now accepts 0–1000 in Pixelate settings; the default remains 2. Very large values make same-hue palette colors win almost regardless of lightness or chroma.
- Auto Optimize's Color Distance option tests both endpoints and common low and high values such as 1, 2, 5, 20, and 100 when using OKLCh.

### Enter no longer applies Pixelate
- Pressing Enter in the Pixelate dialog does not apply the selected result; use Apply or Add to New Tab explicitly.
- Closing the enlarged preview does not restore Enter as an Apply shortcut.

### Fixed scoring in a separate optimizer window
- The slider button opens a separate Auto Optimize Settings window for search parameters, budget, trial history, and score weights.
- Choose a persistent scoring color distance independently of the palette conversion distance. Every candidate uses that same scoring distance for major color shift, even while optimization changes conversion settings.

### Pixel sizes in Pixelate preview headers
- The source view, result preview, and enlarged preview headers now show image sizes in pixels, such as "Result 1/1 · 49×98px" and "Source · 389×771px".
- The source side of each preview shows the size of that frame's area in the original image.

### Gradient color-space selection
- Tool and effect gradients now offer RGB, HSL, OKLab, and OKLCH interpolation; new gradients default to OKLCH.
- Existing settings and projects without a saved color space keep their RGB appearance. Steps and Bayer dithering still use the original stop colors.

### Reusable gradient presets
- Right-click the gradient bar in tool or effect settings to save or apply a preset. Starter gradients include Rainbow, Repeating Rainbow, Sunset, Ocean, and Transparent Fade.
- Tools > Presets… opens a tabbed Presets window where you can preview, edit, rename, favorite, or delete gradient presets; each preset shows its gradient under its name there and in the right-click menu. Favorites appear first; the catalog persists separately from projects.

### Auto Optimize presets
- Save several named Auto Optimize presets, each with its own search items, budget, and fixed scoring criteria. Check the ones to run, favorite, duplicate, rename, or delete them from the optimizer settings window or the Optimizer tab of Tools > Presets….
- Auto Optimize runs every checked preset in turn and shows one result per preset, titled with the preset name, so you can pick the one you like. Ordinary candidates are scored with the first checked preset; older settings migrate into one Default preset.
- Built-in presets: OKLCH1000, OKLab, Quick check, Faithful to source, Clean pixels, and Same hue family (OKLCH 20). Existing preset lists gain the missing ones once. Built-in preset names follow the UI language (English when there is no translation).
- Gradient presets add Fire, Forest, and Grayscale. Preferences > Presets restores deleted or edited built-in presets for auto optimize and gradients, keeping presets you made; the built-in defaults live in the app, so deleting presets never loses them.

### Save or copy Pixelate and AI chat images
- Right-click the source image or a candidate tile in Pixelate to save it as PNG or copy it to the clipboard.
- The source saves as currently shown (with or without the background removed); a candidate saves its first frame.
- Images in the AI chat (drawn results, canvas snapshots, attached images) offer the same menu, and a single click opens a larger view.

## v1.14.37 (2026-10-26)
### Pixelate added to key features
- The guide's Key features now include a Pixelate chapter with two pages: turning AI-generated art or any image into pixel art, and removing or adding colors in the result.
- The homepage, the in-app guide (F1) and the README show both pages with new demo videos.

## v1.14.36 (2026-10-26)
### AI preflight status and account controls
- Opening AI first checks the Gpixel MCP server directly with `initialize` and `tools/list`, plus Claude Code and Codex CLI paths, versions and sign-in states, without sending a model request.
- The CLI MCP row reports what a usage-free check can prove: Claude's chat MCP settings are created, and Codex reads the Gpixel MCP entry. The live CLI connection is still verified on the first message; real CLI failures show the exit code and error output.
- The status window can install or update a CLI, sign in or out, select an executable, and recheck everything. AI > Open AI Status and Preferences > AI Status open it directly.
- "Don't show again" hides the preflight only until the app version, detected CLI file, or custom CLI path changes.

## v1.14.35 (2026-10-26)
### Local cell boundaries on small sprites
- Local correction now adjusts cell boundaries at 4–5 px, which previously fell below its 5 px cutoff; the source overlay shows the resulting per-band bends.
- The minimum average cell size is adjustable from 1 to 16 source pixels (default 4); lower settings may fit blurred-edge noise. The selected result shows whether correction was applied, skipped, unchanged or off.
- The optimizer's “Try local correction” checkbox now searches this threshold along with on/off, shift and band width; it is distinct from the actual Pixel grid setting.

### Wider correction search and matched source-cell mark
- Local boundary shift now reaches 99% of a cell, band width reaches 32 cells, and the optimizer tries these limits alongside the 1–16 px minimum cell size.
- Clicking a source cell in the enlarged view outlines that exact cell in blue as well as the matching result pixel; the source is not recolored.

### Dither chosen by how colors blend to the eye
- Bayer dithering mixes the two palette colors in linear light, as neighboring pixels blend on screen, and judges the blended color in OKLab; a half-and-half black and white pattern now stands for the gray it actually looks like.
- A pattern is used only when the blend looks closer to the source than the nearest single color by more than the pattern's visibility (lightness differences count more than hue differences); colors within a just-noticeable difference of a palette color are not dithered.
- Distant colors are no longer scattered into nearly identical skin colors.

### Better cell-color search and sharp-boundary scoring
- Cell sampling adds center-weighted mean and dominant-color weighted mean; the latter averages within one similar-color group instead of blending skin and dark clothing.
- Candidate generation and auto optimization can compare both modes alongside the four existing modes, without changing saved mode numbers.
- Fidelity now adds the structure lost inside each cell (how much its source pixels spread) to the color difference seen after blurring source and result the way the eye does (S-CIELAB style), so averaging across a sharp boundary is penalized while a dither that blends into the source color counts as a match.
- The optimizer skips color-snap-step trials for methods that do not use that setting, reserving its trial budget for relevant combinations.

### Local grid follows stronger continuous color boundaries
- Within each stripe, local correction weighs sharp OKLab edges and continuous runs more than faint parallel texture or isolated short noise, without changing global scale detection.
- Cell boundaries still obey maximum shift and 70–130% cell width; two sharp boundaries inside one cell cannot both be represented.

## v1.14.34 (2026-10-26)
### Movement test moved into View
- The F5 movement test remains available for testing top-down movement and platformer jumps with the current animation.
- Its single-command Test menu is removed; open it from View instead.

### Transform and move several cels at once
- With several cels selected in the timeline (a cel range, frame headers = those frames on the current layer, layer names = all frames of those layers), Ctrl+T now transforms all of them with the same rotation, scale and move. The handle box surrounds the drawings of every selected cel, the current cel moves on the canvas, and the others change when you apply.
- Applying is one undo step for all cels and Esc cancels them all. Empty cels are skipped, cels on locked layers are skipped with a note in the status bar, part cels are transformed in their own space around the same canvas point, and pixels moved outside the canvas are cut on apply.
- Dragging with the Move tool (without a selection) or nudging with the arrow keys moves all selected cels by the same amount in one undo step.

### Reflection can face left and right (walls)
- The Reflection layer effect's **Direction** now offers **Left (wall)** and **Right (wall)** next to Down (floor/water) and Up (ceiling): the visible image is flipped horizontally and placed beside its left or right edge, offset by the gap.
- All settings work sideways: the length setting is renamed **Width** (a % of the image width), the opacity fades from the column nearest the image to the far column, and **Ripple** shifts each column vertically instead of each row horizontally. Dither fade and tint work as before.
- Canvas expansion, part atlas sprite size and clipping account for side reflections, the effect list shows "Reflection · Left · …", and presets and the AI tools use the direction names Down, Up, Left and Right.
- Older files and presets open unchanged: their up/down reflections keep their direction.

### Document tabs and split view
- Every open document now has a tab above the canvas (file name, red dot for unsaved changes, full path on hover). Clicking a file in the file browser opens it in a new tab; a document that's already open (even in the other pane) isn't opened twice — its tab is selected and focused. Open, recent files, New project and AI tools open new tabs too.
- Switching tabs keeps each tab's zoom, position, frame, layer and selection; the timeline, preview, panels, undo history, title and status bar follow. Close with X, middle click or Ctrl+W (unsaved documents ask Save / Don't save / Cancel), **+** creates a new project, drag tabs to reorder, Ctrl+Tab / Ctrl+Shift+Tab cycle.
- **Move to the other pane** (tab right-click, the button at the right end of the strip, View menu, or dragging a tab to the right edge of the canvas) moves the tab into a second pane with its own tab strip; a pane's only tab isn't moved. The focused pane drives the timeline and panels, and closing a pane's last tab removes the split.
- Open tabs, the split and the active tabs are restored on the next start (missing files and never-saved documents are skipped).
- Closing the final tab shows the start screen below the tab strip; the **+** button stays visible for creating a new project.

### Inserted images start in transform mode
- Applying the Pixelate window as a new layer now enters transform mode right away, holding the whole uncropped result: move or resize it and press Enter (parts outside the canvas are cut only then), or Esc to keep it as inserted. Multi-frame results are inserted as before.
- The Pixelate window has a new **Add to new tab** button before **Apply** that creates a document exactly the size of the selected result in a new tab; "new document" outputs (including the AI chat's) open in new tabs.

### Built-in example sprites turned off for now
- The built-in example sprites in AI references are hidden by default and are no longer used as search results or as style references for image generation, because their quality is low.
- Turn them back on with **Show built-in examples** in **Edit > Preferences… > AI references**. Your own references are unaffected.

### AI chat can generate an image and pixelate it
- A new **Drawing settings** button (sliders icon) next to the **?** help button opens a side panel like the help panel. **Drawing mode** offers **Generate image → pixelate** (default) and **Draw pixels directly** (the previous behavior), and the panel shows the Codex CLI status. Only the selected mode's options appear; hover an option or its info icon for the longer explanation.
- In generate mode the AI calls the new `generate_pixel_art` tool: Gpixel runs your own Codex CLI (`codex exec`, Codex image generation, your ChatGPT login, no API key) to make a high-quality image, then converts it with the AI image pixelator (background removal, auto-optimize with the Pixelate window's items and budget — by default everything except the outline-fit mode, 2000 trials / 120 s, shown as "Optimizing conversion… pass p · trial n/N · best x" — shared palette, 1-px outline) and inserts the top result into a new layer (one undo step with the AI's turn) or a new document; a failed (empty) top result is not inserted and the AI gets an error. Each image takes about 30–90 seconds, and unless you mention a background it's drawn on a plain transparent one.
- Options are saved: result-size correction (off by default; when off the image is converted at the pixel grid found in it, like the Pixelate window's Auto result size, and a size you mention in the chat is only a rough scale hint for the generation; when on the result is fitted to the result size — 32, 48, 64, Custom — or the size given in the chat), remove background, auto-optimize, insert as layer or document, style reference (Auto picks a pixel-art example from your AI references) and default frame counts per action (idle 4, walk 6, run 6, attack 6, jump 6, hurt 3, death 5).
- Animations such as "make a walking animation" are generated in one go as a single sprite sheet: one image draws every frame of the same character side by side (a 2-row grid from 7 frames) at the same scale and baseline ("Generating sprite sheet (4 frames, Codex)…"). The sheet is split by empty columns and rows, parting sprites that touch or overlap at their thinnest column until the frame count matches (or as a grid if that still gives the wrong count — hands and feet crossing a cell line stay with their own sprite instead of leaving fragments in the next frame), and the frames are bottom-aligned on one canvas and palette. **Generation** under **Animation** switches to **One image per frame** (the previous behavior, with a red warning that the look and size often change between frames). A result card shows the frames and a **View conversion settings** button that reopens the **Pixelate AI image** window with the sheet and split settings used.
- Generated sprite sheets are converted at their original size (up to 4096 px on the long side) instead of being halved above 1600 px: halving a 2172 px sheet also halved its pixel blocks (7 → 3.5 px), which turned off local cell correction and coarsened the outline measurement, so the cell lines in the source view missed the blocks (default conversion score 57.4 → 66.5 on a 6-frame chibi mage sheet; one conversion takes about 1.6× as long).
- Claude can't create images, so the Claude tab always draws pixels directly (a red alert icon explains this); ask it to "let Codex do it" to generate that drawing with Codex. The chat help has a new **Generate image → pixelate** tool group.

### AI chat can draw with palette colors only
- A new **Use palette colors only** checkbox under **Draw pixels directly** in the chat's **Drawing settings** (off by default, saved) makes every color the AI paints with the pixel tools snap to the nearest color of the active palette, using the same color distance as the Pixelate tool.
- Fully transparent erasing still works; semi-transparent colors become the nearest opaque palette color. The tool result tells the AI which colors were changed, and each message lists the palette colors (up to 64) so the AI uses them from the start.
- It works in both the Claude and Codex tabs and doesn't affect generated images or layer effects.

### Images in the AI chat input become inline chips
- Pasting an image (Ctrl/Cmd+V — a copied picture or a copied image file), dropping image files on the input, **Image…** and **Attach canvas** now insert an image chip at the cursor instead of a row of images above the input, like Claude Code's input. A chip is a small square thumbnail on a checkerboard (pixel art scaled pixel-exact) and an accent-colored "#N" in a rounded accent border, sized to the text line. Text can go before and after chips.
- A chip acts as one unit: Backspace right after it or Delete right before it removes it and its attachment, arrow keys skip over it, and the remaining chips are renumbered. Hover a chip for a thumbnail preview; click it to view it large with a **Remove attachment** button.
- Sent messages show chips with the same look in the same places (hover to preview, click to view large). The CLI gets the images in chip order and the text says `[Image #1]`, `[Image #2]`… where the chips were, so the AI knows which image you mean.
- Reopened saved chats show the chips from the saved 64 px thumbnails (older chats keep their thumbnails above the text), and editing the last message puts its chips and images back in the input.

### Long Codex messages no longer fail on Windows
- With Codex CLI installed through npm, Gpixel started it through the `codex.cmd` shim, which runs through cmd.exe and its 8191-character command-line limit. Longer prompts with a resume id, model and attached images ended with "Codex process exited (code 1)" and garbled text.
- Gpixel now starts the native `codex.exe` from the npm package directly (or `node codex.js` when there is no native binary), which allows about 32,000 characters and passes arguments unchanged. This covers the Codex chat, image generation and login/feature checks.
- If a command line is still over the limit, the CLI isn't started and a clear "command line is too long (N characters, limit M)" error is shown.
- CLI error output that isn't valid UTF-8 is read in the Windows console code page, so cmd and Windows messages are no longer garbled (Claude chat errors too).

### Turn AI-made pixel-look images into clean pixel art
- New **AI > Pixelate AI image…**, **Edit > Pixelate current layer…** and file browser right-click **Pixelate AI image…** clean up AI images with uneven pixel sizes, blurred edges, JPEG noise and too many colors, including sprite sheets with several frames. From the AI menu the window opens empty: drop an image on it, paste one with Ctrl+V (an image or a copied image file), or **Browse…** (starting in the file browser's folder); **Replace source** or another drop/paste swaps the image and starts over. **Output size** in the bar under the source fits the result to the width × height you enter once **Auto** is off (Auto, the default, shows the selected result's size in grey), with a link button to keep the aspect; it's the same setting as step 4, Output size.
- Step-by-step settings (in a left column that starts collapsed to a thin bar so the preview and candidates get the room, and slides open with one click) cover background removal, frame splitting, the pixel grid (even when pixels vary 7–9 px, and for blocks enlarged up to 256 px; pixel size from outline thickness, color edges or **Block edges** — strong color edges with paper-grain texture ignored, whose cell lines follow those edges — and **Auto** picks whichever lines up best with the source's strong edges beyond chance, preferring a 2× or 3× value that fits about as well, showing all three values and why; clean pixel art keeps its exact integer scale, and 1-px art stays 1×), output size, cell color, palette (OKLab k-means, existing palettes, Auto color count, dither; **Original colors** by default take each palette color from the most-used actual cell color instead of the group average, so blurred edges don't turn a white robe grey, with an **Edge cell weight** for edge and blended cells), cleanup and frame consistency. **Local cell correction** (on by default) re-fits the cell boundaries band by band to the block edges found there, for images drawn half a cell off in places; the source view draws the bent boundaries as they are. The palette's **Color distance** (**OKLCh hue-weighted** by default, CIEDE2000 or OKLab) decides which palette color each cell, pinned color and excluded-color replacement takes, and **Keep distinct hues** (on by default) adds or swaps in a small but clearly different hue — such as apricot ear shading that used to turn gold with 12 colors; **Prefer shade family** (on by default) maps a cell to the nearest-lightness shade of a palette color with a similar hue and chroma and keeps dithering from mixing in another hue, so deep apricot hand shading no longer turns gold or gets gold dots; all three are optimizer items, can be turned off, and earlier saved settings load with the new defaults.
- **Fit outline to 1 px** (cleanup) makes outlines drawn several pixels thick 1 px: **By shrinking** (default) sets the pixel size to the outer outline thickness and scales the sprite down, **Thin outline only** keeps the grid's pixel size and peels dark layers doubled inward without breaking the line or touching the eyes, and **Redraw as vector** traces the center line of the source outline and redraws it as a 1-px pixel-perfect line (adjustable simplify tolerance; small inner shapes such as eyes stay). Candidates try the modes side by side. By shrinking keeps the cell at the measured outline thickness so the outline band is exactly one cell (a larger value may win only when it clearly fits the edges better), and the 1-px outline score is measured on the final pixels with black outlines counted even when the outline darkness is lowered.
- Several candidates are built in the background and scored 0–100 (fidelity measured inside cells, 1-px outline, orphan pixels, outline gaps, colors, frame consistency, grid fit, major color shift, **Block repetition** — high when the pixel size is a fraction of the real pixel so the result is still an enlarged image); the best one is preselected and the score weights can be changed, and a warning appears when the result's long side exceeds 256 px. **Auto optimize** starts with one click, and with **Auto optimize on load** (on by default, in its settings) once by itself after the first candidates of a newly loaded source (the button becomes **Cancel** while it runs) and shows only a live best-score graph with the final score, a one-line summary and a small **Revert**; the settings button next to it holds the checked items (by default everything that applies except the outline fit mode, so your chosen mode keeps its 1-px outline; **Select all / Clear all** and **Defaults** leave the outline fit mode as you chose it; with By shrinking the pixel size stays within ±15% of the outline thickness, measured at the default outline darkness so restarting from an optimized result with a brighter outline darkness doesn't shift the range; the pixel size tries all three estimates and their 2×, 3×, ½ and ⅓, never going below 0.75× of the starting size and estimate without evidence, and never moving off an exact multiple of clean art), the budget (2000 trials / 120 s by default) and the trial history. It shows the top 10 as candidates and loads the best into the settings. When a search pass ends with no further improvement, it starts another pass from the best result (also starting from the most different top result not yet used as a start and a small fixed nudge of the best, never re-running a setting); a pass that gains less than 0.1 points doesn't end the search right away — up to three more passes start from other top results and wider nudges, so optimizing an already optimized result no longer stops after its first pass — all within the one budget; the graph marks each new pass with a faint vertical line and the summary reads "3 passes · a → b · n trials · t s · reason". The same image and settings always give the same result when the trial budget, not the time limit, ends the run. The large preview shows the chosen candidate next to the source with the cell boundaries actually used, frame controls and a frame strip; left-click the result or the source for an enlarged view (both panes zoom and pan together, grid toggle, color swatches and frame strip, play or step frames, Esc or click outside to close), where left-clicking a result pixel highlights its color swatch, the pixel and its same-color regions (HEX and coordinates in the status line) and left-clicking a source cell does the same for the result color that cell became ("Source cell #… → result #…"). Color swatches and result pixels share one rule — Left-click: show where used · Right-click: exclude/restore: right-clicking a swatch, or a pixel in the large preview's result or the enlarged result pane (the color shown there), excludes the color from the current result only, without rebuilding (only its pixels change, to the remaining color nearest their source cell, so the count drops by exactly one; right-click again to restore, **Restore all**), and left-clicking a swatch highlights where it's used (for an excluded swatch, where it used to be). The **+** at the end of the swatches picks a source cell's color as a **pinned color** (eyedropper on the source views, whose borders pulse in the accent color while picking; Shift+click keeps picking, Esc or right-click stops) that every palette keeps — it counts toward the color count, is never excluded, and cells nearer to it than to other colors take it (right-click a pinned swatch to unpin, **Clear all**). White sticker borders (**Remove sticker border**, also an optimizer item) and blended background fringes are removed automatically, and **Keep background (one color)** keeps a solid background flattened.
- Apply as a new layer (one undo step) or a new document, optionally saving the colors as a palette; presets and the last settings are remembered. Experimentally, the AI chat can open afterwards with the original attached and a touch-up prompt filled in, or rank the top candidates (nothing is sent automatically).
- A result that is empty, nearly empty, single-colored or under 8×8 in every frame (a wrongly detected pixel size) is marked failed: it scores 0, sorts last, is never preselected or picked by auto optimize, and the window says why. With the auto grid it is redone with the next-best estimate, and the auto pixel size is capped at 1/5 of a frame's shorter side, so frame spacing in a sprite sheet is no longer taken for a pixel (a 6-frame walk sheet used to give an empty 3×3 result).

### Discord button on the website's front page
- The website's front page now has a **Discord** button next to **Download** and **Guide** (Korean and English pages).

### Right-click menu on the preview
- Right-click the image in the **Preview** panel to **Copy image** (the frame you see, with transparency, without the preview background) or **Copy enlarged (×4)** with sharp pixels for pasting into chats such as Discord.
- **Save frame…** saves the shown frame as a PNG (named `<document>_<frame number>.png`), and **Save as GIF…** opens the **Export as animated GIF** window.
- While playing, the frame shown when you right-click is used, so you don't need to pause. The items are disabled when no document is open.

### Website item and Discord logo in the Help menu
- New **Help > Website** item (globe icon, right below **Guide…**) opens the Gpixel website in your browser — the Korean page for the Korean UI, the English page (`…/GPixel/en/`) otherwise.
- **Help > Report a bug or idea (Discord)…** now shows the Discord logo, and its icon and label use Discord's blurple color.
- The Discord buttons in **About Gpixel**, the error window and the AI chat help also show the blurple Discord logo instead of the speech-bubble icon.

### Outline effect can shade from neighboring pixels
- The **Outline** effect has a new **Color mode**: **Fixed color** (as before) or **Shade from neighbors**.
- In shade mode each line pixel takes the color of the sprite pixel it touches and makes it darker or lighter by **Shade** −100 to +100% (default −40%), so the line next to blue is dark blue and next to grey is dark grey instead of one black line. Hue is kept; only lightness changes.
- Outside lines use the touching pixel (straight neighbors first, then the most common color); inside lines shade the edge pixel itself. Thicker lines carry the same color outward, and direction, inside/outside, thickness, hide original and clipping keep working.
- The effect list shows it as, for example, "shade −40%". Older files and effect presets open with a fixed color; AI effect tools get the new `mode` and `amount` settings.

### Open images, animations and videos
- **File > Open (.gpixel · image · GIF · video)…** (Ctrl+O, several files at once, each in its own tab) now also opens JPG, BMP, GIF, WebP, ICO, WBMP and animated PNG as a new document: canvas = image size, one layer named after the file, one frame per image frame with the file's durations (0–10 ms become 100 ms like browsers), transparency kept. GIF disposal and APNG dispose/blend are composited correctly, photos follow their EXIF rotation, and over 300 frames asks Open all / First 300 / Cancel.
- Videos (MP4, WebM, MOV, MKV, AVI, …) and AVIF/HEIC images are decoded with **ffmpeg** (not bundled, never downloaded). An **Import video** window sets the range, frames per second (default: the video's, at most 12), max frames (default 120) and size (original, or fit the long side — smooth or nearest), with an estimate of frames, size and memory; a progress window can cancel. If ffmpeg isn't found, a message links to ffmpeg.org; set its location in the new **Edit > Preferences… > Video (ffmpeg)** tab.
- Dropping files anywhere on the window outside the canvas (file list, start screen, tab strip, panels) opens each in a new tab. On the canvas, images still become new layers, and animated images and videos (after the import window) fill frames on one layer.
- The file browser lists these images and videos too (first-frame thumbnails; videos get a film badge and an ffmpeg thumbnail when available) and opens them the same way.

## v1.14.33 (2026-10-24)
### Selected frames outlined as a whole column
- Clicking a frame header now draws a blue outline around the whole column, from the header down to the last layer row, like the green outline during playback.
- Adjacent selected frames share one outline.

### AI chat help opens in a side panel
- The question mark button next to **New chat** now widens the chat window smoothly to the right and shows the help in a panel beside the chat, so the conversation stays visible and usable.
- The button stays pressed while the panel is open; press it again or the **X** on the panel to close it, and the window returns to its original width.
- If the wider window would go past the right edge of the screen, it moves left and moves back when you close the panel. A maximized window keeps its size and the chat area gets narrower instead.
- Clicking an example request fills the message box and keeps the panel open. The remembered window size excludes the panel, and the panel starts closed next time.

### Select and copy text in your own messages
- You can drag to select text in your own message bubbles and copy it with Ctrl/⌘+C, like AI replies.
- Dragging can start anywhere inside the bubble, including its padding, and it works in reopened past chats too.

## v1.14.32 (2026-10-24)
### Discord link is now a public invite
- The Discord link for bug reports and ideas is now a public invite anyone can join (<https://discord.gg/Avw7aUVxqr>).
- The app's **Help > Report a bug or idea (Discord)…** menu and the Discord buttons in the error window and AI chat help open this link.
- The links on the website, in the guide and in the README changed too.

### Changes and release notes also in Korean and English
- Changes are written in both English (`CHANGELOG.md`) and Korean (`CHANGELOG.ko.md`).
- Release notes show English first, then Korean.
- The update window shows the changes in Korean for the Korean UI and in English for every other UI language (English and languages added from CSV).
- Only older versions without an English section (v1.14.22 and earlier) appear in Korean.

### English is now the main changelog
- `CHANGELOG.md` is now the English changelog, and the Korean one moved to `CHANGELOG.ko.md`.

### Cleaner changes in the update window
- Each version's changes appear as bold item titles with indented bullet lists.
- Bold, code and links are formatted too, and clicking a link opens the browser.

### Drop images onto the canvas to add them as new layers
- Drag PNG, JPG, BMP, GIF or WebP files from Explorer/Finder or the file browser onto the canvas and each file becomes a new layer (named after the file) above the current layer. With a part or group selected, it goes above that group, like Add layer.
- The image is placed with the pixel you dropped on at its center, in the current frame, with its colors unchanged. An image larger than the canvas is centered on the canvas and you're told it was cut.
- An animated GIF goes into one layer starting at the current frame, and missing frames are added at the end with the GIF's frame durations. The whole drop is a single undo step.
- While dragging, the canvas edge lights up and a dashed outline shows where the image will go. You can also drag a file out of the file browser list and drop it on the canvas.
- Dropping a `.gpixel` opens it, and with no document open a `.gpixel` or PNG opens like File > Open.

## v1.14.31 (2026-10-24)
### AI chat replies rendered as Markdown
- `**bold**`, `*italic*`, `` `code` ``, headings (`#` to `###`), bulleted and numbered lists (nested by indentation) and `>` quotes are formatted, and the markup characters are hidden.
- Code blocks appear in a monospace box and copy as is. Links (`[text](url)` and bare URLs, http and https only) open in the browser when clicked.
- Bold works even when Korean text follows right after it (as in `**'숲의 모험가'**는`), while text such as `snake_case` or `2*3*4` and unpaired markers stay as plain text.
- Replies are formatted while they stream in (an unclosed `**` stays as is until it closes), and the same applies when you reopen a saved chat.
- You can still select and copy text as before (without markup), and your own messages are shown as typed.

### References the AI looks up before drawing
- In the **AI references** window (menu **AI > References…**), add images (`.png`, animated `.gif`, `.gpixel`) and notes (`.md`, `.txt`) with **Add files…** (drag and drop also works) or **Add current canvas**, and give them a title, description, tags and kind. Filter by search or kind, and view the preview (animations play) and palette.
- Write your own style rules (`rules.md`) with **Edit rules**. The built-in **Gpixel default rules**, which can't be deleted, and default examples (slime, sword, character, slime bounce, grass tile) are included.
- In every chat the AI reads the rules, looks up references before drawing something new to follow their proportions and palette, and checks the result against the rules when done. Ask things like "draw a 32px bat like the slime in my references". The chat help has a new **References** group with example requests.
- Saying "add this to my references" makes the AI add the current canvas. **Caption with AI** only puts a request to describe references without a description into the input box; nothing is sent until you press send.
- References live in `<settings folder>/Gpixel/References` (change the folder or hide the default examples in **Edit > Preferences… > AI references**). Files you put in the folder outside the app are picked up too, and nothing is uploaded anywhere.

### Guide chapters no longer have a cover page
- The **Guide** (website and F1 window) no longer has a cover page for each chapter (the "In this chapter" list).
- Clicking a chapter name in the contents expands it and opens its first page directly. The chapter's introduction appears at the top of that first page.
- The previous and next buttons move between pages, and old chapter addresses (`guide.html#chapter`) open the chapter's first page.

### Shorter README
- The repository README is trimmed down to the website link, feature GIFs and downloads.
- Detailed feature descriptions moved to `FEATURES.md`, and development notes such as building, releasing and translating moved to `DEVELOPMENT.md`.

### Discord for bug reports and ideas
- There is now a **Discord** for bug reports, annoyances and ideas. GitHub Issues are still welcome.
- Open it from the app's **Help > Report a bug or idea (Discord)…**, the Discord button in **About Gpixel**, or the button at the bottom of the AI chat help.
- **Report on Discord** in the error window opens it too (please attach the log file).
- The website's top and bottom menus, the guide's **Getting started > Feedback** and the README link to it as well.

## v1.14.30 (2026-10-24)
### OK button in Palette web (Lospec)
- The **Palette web (Lospec)** window has an **OK** button next to **Close** at the bottom.
- It applies the selected palette like **Use as palette** and closes the window.
- If you cancel the confirmation to overwrite the current palette, the window stays open.

### Hide original option for the Outline effect
- The **Outline** effect has a new **Hide original (outline only)** setting. When on, the image the outline traces is erased to transparent, leaving only the line (the outer ring for outside outlines, the edge ring for inside outlines).
- Outside the effect's frame range the image stays as is.
- Effects clipped to this outline (such as a gradient color overlay) apply only to the line, and other effects below apply to the result with the original erased.
- Older files and effect presets open with it off.

### Favorites in Palette web (Lospec)
- The **Palette web (Lospec)** window has **Favorites**. Add and remove them with the star on list rows and next to the selected palette's name.
- Favorite palettes, including their details and example images, are kept on this computer (`<settings folder>/Gpixel/Lospec`), so you can view them, use them as a palette, add them, save them as .hex and press OK **offline**.
- Choosing **Favorites** in the sort box shows only favorites, most recently added first, filtered by color count and tag or name.
- If Lospec can't be reached, the **View favorites** button next to the error takes you straight there. The last sort you chose is kept the next time you open the window.

### Layer effects through AI chat
- AI chat can add **layer effects** and change or remove their settings, frame ranges, on/off state, order and clipping (image pixels stay untouched). Ask things like "give the character a black outline", "white hit flash on frames 3-4" or "make it enter with an appear effect".
- Saved **custom effects** can be added by name too.
- On request it bakes effects in or expands the canvas to fit effects that go beyond it.
- Each change is one undo step (grouped per reply), and effects on locked layers are left alone. The chat help (question mark button) has a new **Layer effects** group with example requests.

### Delete or edit the last AI chat message
- Clicking the pencil (edit) or trash (delete) icon under the last message removes it and the replies, tool lines and result images after it from the chat.
- Edit puts the removed text (and the images attached that time, if still there) back into the input box so you can fix and resend it. Clicking repeatedly steps back one turn at a time to the beginning, and it works in reopened saved chats too.
- If that turn changed the drawing and it can still be undone, it asks **Also undo the drawing changes made in this exchange?** (Undo / Keep drawing / Cancel).
- After deleting, the next message goes to a new AI session along with a summary of the remaining chat, so the AI doesn't remember what was deleted.
- It's unavailable while a reply is coming in; stopped turns can be deleted.

### Top buttons no longer cut off in a narrow chat window
- Fixed the **New chat** and **question mark** buttons at the top being cut off when the chat window is narrow.
- The model box shrinks instead (long names are shortened) so the buttons are always visible, and the window can't get narrower than that.

## v1.14.29 (2026-10-24)
### Wider next layer row buttons
- In the timeline's **next layer row**, the plus button covers the whole layer name cell and the copy button covers the whole frame area (first to last frame).
- You can click anywhere on the strip, and the copy icon repeats every 10 frames.

## v1.14.28 (2026-10-24)
### Next layer row in the timeline
- The timeline has a **next layer row** below the last layer row (like the next frame cell).
- The plus icon in the name cell adds an empty layer at the bottom, and the copy icon in the first frame cell adds a copy of the bottom layer (including parts, for groups) at the bottom (one undo step; the new layer is selected).
- The copy icon in the next frame cell now works only on layer rows, and clicking the empty area below the last row no longer adds a frame.

### Saved AI chats and past chats list
- AI chats are now **saved** (separately for Claude and Codex, after each reply).
- Pressing **New chat** shows the **past chats list**. Click a chat to show it again, and sending continues that chat. Typing in the input box and sending from the list starts a new chat.
- Use the star icon to favorite a chat (pinned to the top, never deleted automatically) and the trash icon to delete it.
- Up to 20 chats are kept per AI; beyond that, the oldest non-favorite chats are deleted first.
- When you reopen the chat window or restart the app, each tab opens the chat (or list) you last viewed.

### AI chat help (question mark button)
- The **question mark button** next to **New chat** shows what the AI can do (by tool group).
- It also has example requests that go into the input box when clicked, and tips.

### Current layer only is now Current file only
- **Current layer only** in chat is now **Current file only**.
- When on, the AI edits only the currently open image (it doesn't open or create other files).
- Within it, the AI can create new layers, frames, groups, parts and skins and organize layers.

### AI leaves locked layers alone
- The AI now always leaves **locked layers** alone (including parts of locked groups).
- Besides pixels, it refuses with a reason to change their name, visibility, opacity, part position and size, switch skins or outfits, delete frames that have drawing on that layer, or resize the canvas.

### GPT-6-Astra as the default Codex model
- In the Codex model list, **GPT-6-Astra** is marked **(recommended)** and is the default model on first use.
- Settings that never picked a model also switch to GPT-6-Astra once. If you then choose "Default (Codex settings)", it stays that way.

## v1.14.27 (2026-10-24)
### Feature GIFs play in the Guide window
- Feature GIFs play right away in the **Guide** window (offline).
- To see them larger, click **Watch larger on the website** below.
- When the search term appears in a GIF's description, the GIF gets a yellow border and is scrolled into view.

## v1.14.26 (2026-10-24)
### In-app Guide window (F1)
- **F1** (**Help > Guide…**) opens the same **Guide** as the website inside the app.
- Pick chapters and pages in the contents on the left.
- Typing in the search box at the top finds results instantly offline, with a result list and matches highlighted in yellow in the text (move between results with Up/Down and open with Enter).
- Videos link to the website.

### Shortcut list moved to the Help menu
- The **Shortcut list** now opens from the **Help** menu (no default shortcut).
- Any key you assigned to the shortcut list in the Shortcuts tab of Preferences still works.

## v1.14.25 (2026-10-24)
### Gradient change merged into Color overlay
- Removed the **Gradient change** effect and moved its features into the gradient of **Color overlay**.
- The new gradient mode **Whole sprite, one color (over time)** changes the whole image through the stop colors over the effect range.
- Moving gradients can set **Speed** and **Ping-pong**.
- Gradient change in older projects and effect presets becomes Color overlay when loaded. Color shifts look the same; radial rotation becomes a circular (dynamic) gradient that keeps the rotation direction but turns only once over the effect range (angles other than 360° and repeat counts are approximated).

### Undo for palette changes
- Palette changes (adding, deleting, sorting and loading colors, renaming and reordering, adding and deleting palettes, and so on) can now be **undone and redone**.
- They share one history with drawing edits, so Ctrl+Z undoes whatever you did last.
- They are kept only while the app is open (collapsing and expanding are not recorded).

## v1.14.24 (2026-10-24)
### Restart right after changing the language
- Changing the display language, or importing a CSV for the current language and confirming Preferences, now also shows a **Restart now** button.
- It closes the app as usual (asking about unsaved documents; canceling leaves everything open) and starts it again in the new language.

## v1.14.23 (2026-10-24)
### Random variation and fly distance for Explode
- The Explode effect's **Scatter** setting is now **Random variation**.
- **Fly distance** can be set separately for X and Y from 0 to 128px.
- A single distance from older projects applies to both axes, and gravity still applies even when the Y distance is 0.

### English UI
- Added an **English** UI.
- On first launch the app picks the language that matches your computer, or starts in English if none matches.
- **Language** is now at the top of **Edit > Preferences…**. A newly chosen display language applies after restarting.

### Make your own language with CSV
- You can make your own language with **Export translation CSV…** and **Add language from CSV…**.
- Add a language code column (for example `ja`) and the language name in the `#name` row to the exported CSV, translate it and import it, and it appears in the list; importing the same language code overwrites it. Empty cells show in English.
- Editing and importing the `ko` or `en` columns overrides the built-in text.
- Deleting the CSV from the folder opened by **Open language folder** restores the original text.

## Older versions
Versions before v1.14.23 are described in Korean only, in [CHANGELOG.ko.md](CHANGELOG.ko.md).
