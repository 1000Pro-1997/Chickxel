# Changes

When releasing a new version, add a `## vX.Y.Z (date)` section at the top. This English file is the original; write the same section in Korean in `CHANGELOG.ko.md` with the same heading (`## vX.Y.Z (date)`). Inside a section, give each change a one-line summary subheading (`### Subheading`) followed directly by two to five short bullet points (`- Sentence`). The release notes (English, then Korean) and the app's update window (Korean for the Korean UI, English for every other UI language) read that version's section from these two files.

## v1.14.69 (2026-11-01)
### Script effects pick the layer they read from a dropdown
- New script setting type `layer`: the effects window shows a dropdown of the document's layers (top to bottom, as in the timeline), and the Effect Maker lists the preview scene's layers. A saved name that isn't in the document stays in the list marked missing.
- Scripts read the chosen layer with `ctx.layer("key")`, `ctx.layerWithEffects("key")` and `ctx.layerEffects("key")`, using the key of the `layer` setting; they return null (or an empty list) when nothing is chosen or the layer is missing.
- `ctx.layerBelow`, `ctx.layerAbove`, `ctx.layerByName`, their `WithEffects` versions, `ctx.layerEffectsByName` and `ctx.layerEffectsAt` are removed. Scripts that still call them stop with a "not a function" error until they are rewritten with a `layer` setting.
- The chosen layer's name is saved with the effect, so it survives saving, presets, copying effects and inserting from the Effect Maker.

### The Effect Maker AI adds useful settings on its own
- Before writing an effect, the AI decides which settings make it useful and adds them without being asked: the anchor (pivot, center or the source's bounds), direction, speed and easing, looping, range, offsets, colors and size, and a `layer` setting for effects that read another layer.
- It keeps them to a handful with defaults that look good on the preview chick, and names the settings it added in its reply. The effect-writing guide has the same rule.

### Uploaded previews can hide the preview scene's layers
- Effects with a preview scene now also upload the preview with the scene's layers hidden, with and without the effect, at the same size as the normal preview. The effect still reacts to the hidden layers; the website uses them for its "hide helper layers" toggle.
- Exported and published packages carry them as `previews/<name>.solo.png` and `previews/<name>.solo.original.png`. A custom preview image gets none, like the before-effect image.

## v1.14.68 (2026-11-01)
### Start a new project from the clipboard image
- When the clipboard holds an image, the New project window shows it with **Paste the clipboard image (W×H)**. Turning it on sets the canvas to the image size and puts the image on the first layer.
- You can still change the size (the image stays at the top left); starting with parts is off while pasting.

## v1.14.67 (2026-11-01)
### No red error line when Claude misnames a tool
- When Claude calls a Chickxel tool without its MCP prefix, Claude Code rejects it ("No such tool available") and Claude calls it again correctly. The AI chat and the Effect Maker AI no longer show that rejected call as a red error line; real tool errors still show.

### Saving over a file another program has open
- Saves that replace an existing file (Save frame as PNG and the other right-click saves, project save, exports) failed with "Access to the path is denied" when another program kept the file open, for example a sync app, Explorer's preview, an image viewer or a game engine. Chickxel now retries briefly and then writes into the existing file.
- If the file is read-only, or the other program doesn't allow changes at all, the message now says so instead of "Access to the path is denied"; the old file is left as it was.

### Temporary titles and free save names for new projects
- Right-click the tab of a never-saved new project and choose **Temporary title…** to name it. The tab and window title show it instead of "New project"; leave it empty to clear it.
- The first time you save a new project, the Save dialog suggests the temporary title as the file name (characters files can't use are removed), or `sprite` without one. If that name already exists in the folder the dialog opens in, it suggests `sprite_1.chickxel`, `sprite_2.chickxel`, and so on.
- When the AI chat draws into a new unsaved project without a temporary title, it gives it a short title in your language (`title` on `new_project`, new tool `set_document_title`).

### Effect Maker preview scene for effects that read other layers
- When a script reads other layers, the Effect Maker shows **Preview scene**: **Edit in main editor** opens the scene (layers around the preview chick, a `ground` layer to start) as a tab in the main editor, where you can draw with every tool; the Effect Maker preview follows your edits.
- The scene is saved with the effect and goes into exported and uploaded packages; Check, the Effect Maker AI and the uploaded preview image all render the effect in the scene.
- New script functions `ctx.layerByName(name)` and `ctx.layerByNameWithEffects(name)` read the nearest layer with that name (e.g. `"ground"`), aligned like `ctx.layerBelow()`.
- Effects that read other layers always carry the `layer-reference` tag; the Publish window keeps it and counts it toward the 8 tags.

### Effect Maker status button and compact preview controls
- The status box is now a button next to **Insert into layer**: green when everything is fine, yellow with warnings only, red with errors. Click it for the details; clicking an error line still jumps to the code.
- Play/pause and "show without effect" are icon buttons on one row with the frame count.

## v1.14.66 (2026-11-01)
### Preview plays the clicked tag, otherwise all frames
- Playing the preview loops only the tag you clicked in the timeline; with no tag selected it plays all frames, even when the current frame is inside a tag.
- Clicking the frame area outside a tag (frame header, cels, effect rows, empty tag row) clears the tag selection. Only the selected tag has a white outline.

### AI picks are remembered; model picker in the Effect Maker AI
- The AI chat and the Effect Maker AI save the AI you pick (Claude or Codex) and its model right away, so the next time either opens it starts with your last choice.
- The Effect Maker AI has a model list next to **Send**. Changing it applies from the next message and keeps the conversation; the AI chat and the Effect Maker AI share the same saved model per AI.

## v1.14.65 (2026-11-01)
### Effects follow gradients on color settings
- The Effect Maker rules (Rules window and AI guide) now recommend picking color settings per pixel with `ctx.color("key", x, y)` (lists: `ctx.colorAt`, `ctx.pairColor`), with a short example, so a color switched to a gradient varies across pixels. The AI does this by default and keeps a flat color only when an effect really needs one solid color.
- The Effect Maker check renders each color setting with a sample gradient (red to green to blue) and lists color settings that still draw one flat color when a gradient is chosen. These are recommendations: they don't change the check result or block upload, and the AI tools `check_effect_maker`, `write_script_effect` and `set_effect_maker_source` report them too.

### Package IDs are set automatically
- Publish Online no longer has a Package ID field. The ID is the package's own ID when the asset comes from a package you uploaded (installed, or a copy of one), otherwise "your-handle.package-name"; the same name updates the same package, and someone else's package uploaded by you becomes your own separate package.
- The top of the Package group says "Uploading as a new package" or "Update <name>: 1.0.0 → 1.0.1", checked online (it says so when it can't connect), and tells you when a package of yours with the same name is being updated. The suggested next version raises the patch number (1.0.0 → 1.0.1).
- The Asset Manager has a **Publish New Version…** button for single-asset packages you uploaded.

### Effect Maker languages for older scripts
- Scripts without `language` now show and edit the UI-language translation in Title and Description when one exists. The labels always show which language you are editing (or "Base (language not set)").
- The Languages window has a **Base language** picker at the top for the language of the title and description written in the source; it is guessed from the text when `language` is missing and written into the source when you click OK.

## v1.14.64 (2026-11-01)
### Publish one asset with languages, tags and a fixed license
- The Publish Online window uploads one asset as its own package; the included-assets checklist is gone. The suggested package ID is "handle.asset-name" (a short hash when the name has no English letters).
- Author ID and display name come from your signed-in Chickxel account and can't be edited. The website field was removed.
- Published packages use a fixed license: CC0 1.0 (anyone may copy, change and use without conditions), with the CC0 legal code as LICENSE.txt.
- **Languages…** sets the package name and short description per language; the Store, the Asset Manager and chickxel.com show them in the viewer's language (script effects are prefilled from their declaration translations). In the language windows, languages that already have text show in green with a check mark.
- Tags are entered as `#neon` chips: Space, Enter or comma adds a tag, X removes it, Backspace in the empty box removes the last one. The Effect Maker's Info group has the same chips, written to the declaration's `tags`.

### Effect Maker follows the UI language
- Title and Description show and edit the UI language: when it differs from the declaration's `language`, they edit that translation (empty if there is none), with the language shown next to the labels.
- Script declarations accept `tags` (up to 8 lowercase English tags) and `language` (the language of the name and description). New scripts start in the UI language and declare it. The Effect Maker AI and the rules guide always set `language`, `description` and 3-6 English tags.
- Official effect packages are now 1.0.1 (their declarations include `language: "ko"`).

## v1.14.63 (2026-11-01)
### Text effect and Text tool
- New **Text** layer effect draws text in one color with a bundled pixel font (Galmuri 7, 9, 11, 11 Bold, 14; SIL OFL 1.1). Settings: multi-line text, font, color (gradients work), position, letter and line spacing, alignment and **Typewriter** (letters appear one by one over the effect range).
- On an empty layer the effect works as a text layer: **Merge Down** or **Bake** turns it into pixels.
- New **Text** tool (T): click the canvas, type, and press Enter (Shift+Enter = new line, Esc = cancel) to add a text layer above the current layer in the primary color and the font chosen in tool settings. Click on existing text to edit it.
- The **Move** tool drags text layers (empty cel with a Text effect) by moving the text itself; the arrow keys nudge it. Each move is one undo step.
- The Dithering tool no longer has a default shortcut (T now selects the Text tool); you can still assign one in Preferences.

### Effects and presets, Store
- The effect type panel in the effects window now has **Effects** and **Presets** tabs. The Effects tab shows every single effect in one grid: the app's effects, followed by script effects you made and effects from installed packages. An **Open Store** button at the bottom opens the Asset Manager's Store filtered to effects. The New script / Open JS file group is gone; make effects in Tools > Effect Maker.
- Saved combinations of effects and values (custom effects/recipes) are now called **presets**. The Presets tab shows only presets and saves new ones with **Save Current Effects as Preset**.
- The Asset Manager's Browse tab is now **Store**, with an **Open on Website** button that opens the chickxel.com assets page (on the tab for the selected kind).
- Script effect color settings can be gradients like the built-in effects (`ctx.color` samples per pixel with the same math; custom bounds supported). New setting types `colorList` and `colorPairs` (add, remove and reorder items, each with its own gradient), `widget: "angle"` / `"point"` and `alpha: false`. Script API additions: a salt argument for `ctx.hash`, `ctx.bayer2`/`ctx.bayer4`, `ctx.hasGradient`, deferred effects (`deferred: true` draws after the other effects, like Afterimage) with `ctx.renderedFrameSource`, and a declared `easing` default. Preset parameters and the AI effect-settings tools work with script effect settings.
- The built-in effects are now official script effects: Outline, Shadow and the other 21 effects come as official packages (`chickxel.effects.<id>`) that the app installs by default and updates when it ships a newer version. They look exactly like before (pixel-identical JavaScript ports) and appear first in the Effects tab, with the common script settings panel. Disabled official effects stay disabled and deleted ones are not reinstalled; **Asset Manager > Restore default effects** brings them back.
- Projects, presets and packages with the old built-in effects open as the matching official script effects with identical pixels and are saved as script effects (older app versions open them as script effects too).
- AI tools list and add the installed script effects by id (for example `outline`); the old built-in type ids still work.
- Script effects can use `Float64Array`/`Int32Array`/`Uint8Array`, `ctx.phase`, `rgbToOklab`/`oklabToRgb`, `motion: false`, `clipsBySource`/`erasesSource`, and their limits (statements, time, memory) now grow with the image size, so effects finish on large canvases; the fast path has no image size cap.
- Projects, presets and packages that carry an earlier version of an official effect script keep running without asking after the official script changes (the app remembers every official script hash it has shipped). On first run the default effect packages install in the background, so the window does not freeze; they appear in the Effects tab when the install finishes, and Restore default effects no longer blocks the window either.
- On/off script settings show their name as the checkbox text, so long names no longer cut off the color and gradient rows. Official effect tiles no longer show the script marker, and packages with only official scripts no longer show the 'allow scripts' note. Package check: a script used by a rejected effect is no longer also reported as an unused script.

### Gradient Pencil and symmetry around the pivot
- Gradient Pencil tool: long-press the Pencil button or press Shift+B. Draws like the pencil (brush, pixel perfect, symmetry, selection, locked layers, right button = eraser), but each painted pixel's color comes from a gradient. In tool settings > Gradient, choose Whole stroke (on release the gradient spans the stroke from start to end by drawn length, with a live preview while drawing) or Repeat (restarts every N px). Pick a gradient preset with the Presets button, reset to black-to-white with Default, or edit the stops inline. Settings are remembered.
- Symmetry drawing has **Around pivot**: mirrors around lines through the pivot (the selected part's pivot, otherwise the current frame's) instead of the canvas center, for every tool that mirrors. The guide lines move with it; also in the Tools menu.
- While symmetry drawing is on, a grey ghost brush cursor shows at each mirrored position so you can see where the mirrored stroke will land.
- The eraser remembers its own brush shape and size, separate from the pencil (the other brush tools keep sharing the pencil's). Both are saved for the next start.
- The eraser has **Strength** (1-100%) and **Channels** (R, G, B) in tool settings. Below 100% each stroke makes pixels more transparent by that much (a pixel is reduced once per stroke; rub again to reduce what is left). With only some channels on, opacity stays and only those color channels are reduced (R alone removes red). The pencil's right-button eraser uses the same settings.

### Effect Maker window (script effects)
- **Tools > Effect Maker…** makes and edits script effects (JavaScript). On the left is the list of effects you made (**Show all** adds copies from packages and the script effects of installed packages) with the title and description fields below, in the middle the code, and on the right the same chick preview that is made when you publish (pick the chick by its picture, play, show without effect) and the status.
- **New script** starts from a short skeleton (one setting and a loop over the pixels). The title, description and preview chick fields write `name`, `description` and `previewChick` into the source's declaration, so one source file stays one effect in any app.
- **Check** runs the install-time source check and then the effect on every sample chick over the preview steps with the default settings, and lists run errors (click to jump to the line) and the slowest step. **Upload…** saves, runs the check and only then opens the publish window with just this effect selected (the preview is made automatically).
- The Preview setting values header has **Reset** (back to the source defaults) and **Set as default** (writes the current values as each setting's default in the source, one undo step).
- The preview can use 2-20 steps (**Frames** next to the play button, `previewFrames` in the declaration); the preview, the check and the uploaded preview use that count. An uploaded preview that would exceed the preview size limit merges pairs of frames (same loop length). A frame strip under the preview shows every step with its number; click one to pause on it.
- Script effects can have their title, description and setting labels in several languages (`translations` in the declaration, **Languages** in the Effect Maker). The effects window, the layer effect list, the Asset Manager and the Effect Maker show the names for the app's UI language. The AI always adds English translations when it writes names in another language.
- **Save** keeps it as a personal script effect (Ctrl+S) and **Insert into layer** adds it with the preview setting values to the end of the main window's current layer (one undo step). An effect opened from an installed package becomes a personal copy when you save it.
- The right-click menu of script tiles in the effects window's Effects tab and the details of personal script effects in the asset manager have **Edit in Effect Maker**.
- A **Rules** button next to "Info" shows the rules for writing an effect and the script API; **Copy** puts the guide (and the current source) on the clipboard to paste into another AI such as ChatGPT on the web.
- An **AI** button next to "Preview" opens an AI panel on the right of the window: describe the effect and the AI (Claude Code or Codex, same login as the AI chat) writes source that follows the rules into the editor and checks it with the preview and Check. Undo in the editor reverts its changes.
- The AI panel can look at the open drawing, its layers, effects and references but can't change them: the in-app MCP server gives the panel its own access key and blocks everything except read tools and editing the Effect Maker source. It can read the source of your other script effects (personal and from installed packages) as a reference ("like my X effect but ..."), and you can attach images as in the AI chat: image files, paste, drag and drop, the current canvas and the current preview frame.

### Script code editor: syntax colors, autocomplete, error underlines
- The Effect Maker and the script editor of the effects window share one code editor: JavaScript syntax colors, line numbers, wavy underlines at errors and description tooltips over names.
- Suggestions appear after `img.`, `ctx.`, `ctx.source.` and `Math.`, after `s.` (the declared setting keys), at key positions of the declaration and setting items, and inside `type: "`, `area: "`, `previewChick: "` and `ctx.color("` (also with Ctrl+Space). While you type a call, a parameter hint shows with the current argument in bold.

### AI makes and saves effects
- AI chat writes script effects for looks the built-in effects can't make (`write_script_effect` - with errors nothing changes and the line and column come back so it can fix them). It reads the API with `get_script_api` first.
- `preview_effects` shows frames of effects on the chick or the layer's own image, and `save_script_effect` saves a layer's script effect as a personal script effect and opens it in the Effect Maker. Publishing online is left to you.
- New AI tools `get_effect_maker_source`, `set_effect_maker_source` and `check_effect_maker` read, replace and check the Effect Maker's source while the window is open. `get_script_api` now returns the rules together with the API.
- New read-only AI tools `list_script_effects` and `get_script_effect_source` list your script effects and return one's source, settings and execution path.

### Script effects read other layers, center and 9-slice
- `ctx.layerBelow()`/`ctx.layerAbove()` (without effects) and `ctx.layerBelowWithEffects()`/`ctx.layerAboveWithEffects()` (with that frame's effects) return the layer directly below/above in the same group and level, aligned to `img` (same size and position), or null. Hidden layers are included. Layers that read each other with effects render deterministically without loops (inside that render, effects that read other layers with effects are skipped one level).
- `ctx.layerEffectsByName(name)` and `ctx.layerEffectsAt(offset)` return the names of the effects enabled on this frame on another layer, so a script can change its look depending on other layers.
- `ctx.center` (center of the effect source) and `ctx.slice` (9-slice border of the canvas or part, or null). `ctx.center` also runs on the fast path.
- Effects that read other layers re-render on the canvas, timeline, effects-window preview and export when a neighbor's pixels, effects, name, order or the border change.

### Effect recipes are not published
- Effects are published as script effects only. The publish window no longer lists effect recipes (combinations of built-in effects) and refuses them, and chickxel.com rejects packages that contain them. Saving recipes with the effects window's save-all-effects for use on your own computer works as before.
- Script declarations can have a short `description` (up to 200 characters); it fills the short description in the publish window.
- **Tools > Presets…** is gone; gradient presets are in the **Asset Manager** (the gradient editor's manage button still opens the presets window).

### Preview chicks and animated previews
- The save custom effect window lets you pick one of 20 preview chicks; script effects name one in the declaration, like `previewChick: "knight"`.
- Previews made for Publish Online and package export are animated PNGs of the effect over 10 steps on that chick (still effects stay one image), with the effect-free original included so chickxel.com can compare before and after.
- The official effect packages (Shiny Pickup, Outlined Drop Shadow, Hit Reaction) are now 1.0.1 with the new previews.

### Re-uploaded versions must be higher
- The publish window won't upload a version that is the same as or lower than the highest version already published (online list and this computer's records), and says why: published versions can't change.

## v1.14.62 (2026-11-01)
### Chickxel account, gallery uploads and asset publishing on chickxel.com
- **Help > Chickxel Account…** signs in with Google, GitHub or Discord in your browser and returns to Chickxel. The window shows your picture, name and handle, lets you change the name and handle, opens your gallery page and signs out. The session is kept in the settings folder (encrypted for your Windows user on Windows).
- **File > Upload to Gallery…** uploads the current frame as PNG or the whole animation as GIF (scale, title, description, tags) to the chickxel.com gallery, with progress and cancel, and opens the picture's page when done.
- **Publish Online…** in the asset manager now uploads the package with your account instead of saving a submission zip for a GitHub form. Your handle is the author ID and the first part of the package ID; problems the server finds are listed under the checks, and the package page opens after uploading. The chosen list preview becomes the first asset's preview.
- The website and the online asset list moved to https://chickxel.com (the list is read from `/api/catalog/index.json`). Packages uploaded by users show "Community" as their review status.
- Each account can store up to 50 MB on chickxel.com; uploads past that, or while the site storage is full, are refused with a message saying so.

### Website: gallery in the menu
- The website menu has **Gallery** (https://chickxel.com/gallery) instead of Key features. Signed-in visitors can upload pixel art there, browse effect and gradient packages under Assets, and like or dislike them.
- The headline now reads "A pixel art editor built for games."

## v1.14.61 (2026-11-01)
### Chickxel moves from MIT to a source-available license
- Chickxel is now under its own source-available license (`LICENSE`). Official releases stay free for anyone, including commercial work, and what you make with Chickxel is yours.
- The source stays public: you can read it, build it and change it for your own use, but you can't redistribute or sell the original or a modified version without permission.
- Contributions can be used and relicensed by the copyright holder. Everything published under MIT (releases up to v1.14.60) stays MIT.
- The About window, the website and `THIRD-PARTY-NOTICES.md` show the new license.

### Checked presets always have a place in the candidate row
- Every preset checked in the auto-optimize settings now shows a tile in the candidate row even before it has run (preset name, "Not run yet"), including before a source image is added. Checking or unchecking a preset adds or removes its tile right away.
- Right-click such a tile and choose **Run only this preset** to optimize just that one; the tile blinks while it runs and becomes its result.

### Preset bar: copy button and clearer colors
- A **Copy** button next to Delete saves the current values as "name copy 1" right after the open preset (then copy 2, 3…; copying a copy counts up from the original name).
- **Save** is now disabled when nothing changed, instead of turning green. **Delete** is red, and **Clean up** is a green icon-only button with its name in the tooltip.

### Gpixel is now Chickxel
- The app, its website and its GitHub repository are renamed to Chickxel: https://1000pro-1997.github.io/Chickxel/ and https://github.com/1000Pro-1997/Chickxel (old github.com links redirect).
- New projects are saved as `.chickxel` and asset packages as `.chickxel-asset`. `.gpixel` projects and `.gpixel-asset` packages still open, import and save as before; **Save As** suggests the `.chickxel` name. Effects saved with `gpixel.effect.` type IDs read as before.
- On first start the `Gpixel` settings folder (settings, installed assets, undo history, trash, chats, logs, and on Windows the Node.js added by the AI installer) moves to `Chickxel`. If an older version is still running, it is copied and the old folder stays.
- Downloads are now `Chickxel-windows-x64.zip` (`Chickxel.exe`) and `Chickxel-macos-*.zip` (`Chickxel.app`). Versions up to 1.14.60 can't find these in their update check, and their online asset list stops loading, so download this version manually once.
- Official catalog assets now have `chickxel.` IDs; ones already installed with `gpixel.` IDs keep working.

### Optimize the pixel grid first, and preview the cell split
- **3. Pixel grid** has a new **Optimize grid fit first** checkbox. When on, Clean up and Auto optimize first try only the grid values (pixel size, size jitter, pixel size source, local correction; offset X·Y for a manual grid) and keep the cell split with the best cell split score, then optimize the other steps on that grid with the normal score. The grid stage uses up to half of the trial and time budget when other steps are optimized too. The checkbox is saved with the preset.
- The cell split score looks only at the source and the cell boundaries, not at the palette or cleanup result. **Cell edge fit** is high when cell boundaries sit on the source's clear color edges and miss none (beyond what random placement would hit), and **Cell color purity** is high when each cell holds only similar colors.
- Next to it, **Preview cells** adds a "Preview cells" candidate, selects it and shows its cell split over the source (grid view on). With the checkbox on it runs only the grid stage; with it off it cleans up once with the values shown.
- Optimizing the grid now also tries **Allowed size jitter** and **Pixel size from**, which used to stay as set.
- While a tile is being optimized, clicking it shows the best result so far in the large preview, source grid and zoom view, and follows it as the best changes. Removing or pinning colors waits until it finishes; when it finishes, its result tile is selected.

### Color decision in the enlarged view
- Left-clicking a result pixel or a source cell in the enlarged view shows a **Color decision** row above the color swatches: how that pixel's color was chosen, step by step, with a swatch and HEX for each — the cell color (by the cell color method) → the palette → any later cleanup step that changed it (remove orphan pixels, fill 1px holes, thin outline, vector redraw and so on), pinned colors and excluded colors.
- Below it are the three palette colors closest to the cell color with their palette color distance and their lightness, chroma and hue difference from the cell color (OKLCh), the chosen one in bold — so you can read, for example, why a light gray cell came out closer to peach than to white (same lightness, only chroma added). When Prefer shade family or dithering picked a color other than the closest, the palette step's name says so.
- Hover a step's name to see the rule it uses; for example, removing orphan pixels takes the most common neighbor color without looking at the source cell color.

### Palette method "Distance grouping"
- **6. Palette** has a new method, **Distance grouping**: colors whose difference, measured with the **Color distance** below (the same distance cells use to pick a palette color), is within the **Group radius** (default 0.04) become one color, and the radius decides the color count.
- With CIEDE2000 as the color distance, colors that look alike are grouped. OKLCH grouping measures lightness, chroma and hue separately and ignores hue for grays, so a light gray could join a peach group of the same lightness and be painted peach. On the sample image, light neutral cells painted with a chromatic color went from 35 of 1845 to none.
- Optimizing the Palette step also tries this method and its radius.

### Auto drawing mode: generate first unless "Current file only"
- In Codex's Auto drawing mode, turning on **Current file only** in the chat draws directly on the current picture; otherwise image generation → pixelate is tried first. Request wording such as exact sizes or "change the hat color" no longer switches the mode.
- If generation fails (no Codex, generation error and so on), the AI draws the same request with pixel tools instead and then reviews the whole picture like a direct-drawing turn. Stopping the generation yourself does not trigger this.

### Keep the palette method while optimizing
- A **Don't optimize** checkbox next to the Palette step's method keeps the method as set when that step is optimized; its other values, such as the OKLCH grouping range, are still tried. The checkbox is saved with the preset.
- OKLCH1000 is no longer a built-in preset. If your list has it, it stays as one of your own presets with its saved values (so **Reset** removes it).
- Dithering has its own **Don't optimize** checkbox, on by default, so optimizing the Palette step no longer switches dithering on: a dither pattern scored well because its average color is close to the source, but zoomed in it showed checkerboards or stray colored dots. Built-in presets now start with no dithering, and existing built-in presets are switched to no dithering once on update.
- **Fidelity** in the score now compares cell by cell by default: each result pixel is compared only with its own source cell's color (the average of the cell's largest group of similar colors, center weighted) using the scoring color distance, so a stray colored dot or a dither pattern costs points in every cell instead of being blended away with its neighbors. **0. Optimize criteria** has the choice (**Cell by cell** / **Blended with neighbors**), the cell color (**Dominant group** / **Cell average**), the group range and **Center weighted**; they are saved with the preset. Scores are stricter than before.

### Pixelate candidate row shows presets, not ranks
- Loading a source now runs each checked preset once with its settings as they are and shows one tile per preset, marked "As set". The ranked 1st/2nd/3rd candidates and the **Candidates** settings (count and what to vary) are gone.
- **Clean up** with values you changed adds the new tile at the front of the row; rerunning an existing preset updates its tile in place.
- The tile that is running gets a slowly blinking yellow border. A new run's tile appears right away and blinks until its result fills it, showing the best result found so far as it improves (refreshed at most five times a second).
- While running, the **Cancel** button is red and stops only the preset that is running; its best result so far stays in its tile and the next preset starts. Right-click it and choose **Cancel all** to stop the remaining presets too.
- Right-clicking a frame in the source view offers **Copy frame N** and **Save frame N as PNG…** (the frame as shown, with the background removed if that view is on), above the whole-image copy and save. The source view's right-click menu did not open before; it does now.
- Hovering a candidate tile now shows its full name, how it ended and its score (for example "Optimized (72.4)"), colors and size, and the pixel size above the score breakdown.

### Pixelate candidates drive the settings panel
- Clicking a candidate tile loads its settings on the left, including its preset, Optimize checkboxes, optimize criteria and whether the output size is automatic. If you have edits that have not been run yet, you are asked whether to save them first.
- **Clean up** now reruns only the selected preset's tile and keeps the other candidates instead of replacing the row with ranked results. An unchanged saved preset behaves like **Rerun only this preset**.
- Running unsaved edits adds a separate "Name (modified)" tile and leaves the saved preset unchanged. A preset with no Optimize checkboxes runs once as set and is marked "As set".

### Native-resolution pixel art and image-specific grid values
- Pixel art saved at its own resolution (one dot = one pixel) or at 2×, with noise or JPEG artifacts pushing it past 256 colors, now gets 1 px or 2 px cells. Before, auto detection only tried 3 px and up and blurred such art into blocks several times too large.
- Presets no longer save **Add to auto pixel size** or the manual grid's start X/Y, which only fit the image they were measured on. Values written there by earlier optimizations (up to +45 px) are removed once on update, and reopening the window no longer carries them over to a new image.
- Pixel art with cells of about 3 px is no longer squashed into blocks (for example about 85×195 instead of 11×20). The color-edge estimate now counts the smallest 3 px candidate, and when choosing between estimates a value is only taken as a multiple of another up to 4×, not 10×.

### Auto Optimize Settings Main button and Reset, resizable color swatches
- The AI conversion preset is now chosen with a rounded **Main** button on each row; the chosen one is green.
- A red **Reset** button next to **Close settings** asks for confirmation, then restores the preset list, checkboxes, main preset, preset values (including values written by optimization) and **Auto optimize on load** to their first-install state. Presets you created are deleted.
- The color swatches in the preview and the enlarged view start at no more than half the panel height; drag the short grip above them up or down to resize (remembered separately for each view), or double-click it to restore half.

### AI pixel size and reference conversion
- When the user does not specify a pixel size, Codex chooses a logical square sprite size in English before generation, from the attached reference or the subject description. An explicitly requested size or enabled fixed-result setting takes precedence.
- The generation prompt starts with the selected size and repeats it in the style, composition and pixel-grid rules; sprite sheets apply that size per frame.
- Codex Auto also converts attached-image requests with an explicit size; the prompt preserves the reference subject and pose.

### Representative preset for AI conversion
- Select one representative preset in Auto Optimize Settings; Same hue family (OKLCH 20) is the initial choice, independent of the presets checked for batch optimization. If OKLCH1000 was your representative because it was the old default, it moves to Same hue family (OKLCH 20).
- AI-generated images use that preset's saved conversion settings, optimization sections, budget and scoring criteria; an explicit output-size target still takes precedence.

### AI chat prompts, choices, and notifications
- Image generation adds a collapsed card showing the request; expand it to inspect the exact prompt sent to Codex, including after reopening a chat.
- When the AI needs a decision, choose one of its options, type another answer, or cancel directly above the chat input. Cancelling never silently selects an option.
- If generated art is larger than the open canvas, choose to expand the canvas or position a crop in a preview before insertion. Cancelling leaves the document unchanged.
- AI questions and finished turns send system notifications on Windows and macOS when notifications are available.

## v1.14.60 (2026-10-30)
### Edit and apply sprite-sheet frames
- The source preview shows numbered frame boundaries. Drag a frame to move it or an edge or corner to resize it; the edited regions feed into the next pixel cleanup and can be saved in presets.
- A source image is split once when loaded or replaced. Changing the split mode or grid dimensions leaves the current frames in place until **Apply frame split** is clicked; dragging a frame boundary applies it immediately.

### Pixel grids and target-color scoring
- Pixel grids are now fitted independently for every frame, including fixed output sizes and scale factors. The separate per-frame detection checkbox is gone.
- Setting a target color count penalizes results with more colors than the target by how far they go over it. Next to the target, a new **Strict** checkbox (off by default) also penalizes results with fewer colors than the target; with it off, fewer colors are not penalized. An exact match has no color-count penalty; Auto still ignores color count in scoring.

### Auto optimization on source load
- Auto optimization after loading, pasting, dropping or replacing a source image is off by default, including when upgrading existing settings.
- Turn **Auto optimize on load** on to run it after the first candidates finish for future source images; the choice is remembered.

## v1.14.59 (2026-10-29)
### Chat images and message copying
- Click an image chip in a chat message to enlarge it and use the visible **Copy image** or **Save image** buttons; right-clicking the chip also offers those actions. Older conversations retain only their saved preview size.
- Sent image chips now prioritize image actions over the text-selection menu on right-click. **Copy image** in generated-image and chip menus now copies even when no status callback is installed, before the image view can be replaced.
- Every sent message now has a copy-text button below its bubble, including older messages. It copies the words without image-chip tokens; edit and delete remain available only on the latest message.

### One preset list for Clean Up as Pixels and Auto optimize
- Clean Up as Pixels presets and Auto optimize presets are now one list, pinned at the top of the settings with Save, Rename, Delete and a new Clean up button. Your old presets move to the end of the list.
- Each step (Background, Pixel grid, Cell color, Palette, Cleanup) has an Optimize checkbox, and the new "0. Optimize criteria" section holds the budget and scoring; both are saved with the preset.
- Choosing a preset or changing a value no longer re-runs the cleanup; only Clean up does. The Save button turns red when there are unsaved changes and green when everything is saved, and switching away from unsaved changes asks whether to save.
- The Auto optimize settings window now only picks which presets run, plus the history. The Auto optimize tab in Tools > Presets is gone.
- Optimizing writes the best values found into the preset that was optimized (Clean up: the chosen preset; Auto optimize: each checked preset), so you can turn Optimize off and fine-tune from there. If that preset is open, the settings on the left follow the best result live. Revert restores the presets as well.
- Clean up with Optimize on shows the run's trials as ranked candidates (#1 is the optimized result) instead of a single preset tile.
- Optimizing a step now tries everything in it: the Palette step switches freely between k-means, median cut, OKLCH grouping and keep original colors (and the existing palette you started from), and tunes OKLCH grouping's L, C and H ranges; the Cleanup step also tries the outline-fit modes. Settings that belong to one palette method change only in trials that use it.
- "0. Optimize criteria" has a Target colors setting: results with more colors than the target have their whole score multiplied by (1 − Color count weight × extra colors ÷ target), so with the default weight 0.5 and a target of 16, 32 colors lose half the score and 48 or more score 0; fewer colors are not penalized, and Auto (the default) leaves the color count out of the score.
- Keep original colors no longer fails on large sprite sheets (it counted colors through a one-row image wider than the 16384-pixel limit).
- The built-in presets on a fresh install (and after Restore defaults) are now the tuned set: each preset's step settings, per-step optimize, budget, scoring and target colors come from one data file shipped with the app.

### Fixed colors no longer rebuild the palette
- Adding a fixed color in Clean Up as Pixels now keeps the current result and only adds that color: pixels whose source color is closer to it than to their current color switch to it, and every other color stays exactly as it was.
- Candidate rankings, scores and optimized preset tiles stay in place; unpinning a color restores the previous pixels.

### OKLCH grouping palette method, now the default
- Clean Up as Pixels has a new palette method, OKLCH grouping: colors whose lightness, chroma and hue differences are all within adjustable limits (default L 0.1, C 0.1, H 20°) become one color. It is now the default palette method; "Fewer colors (16)" still uses k-means.
- Group centers are chosen greedily by how many pixels they cover; colors in reach of several centers join the nearest, and hue is ignored for near-gray colors. The limits decide the color count.

### Separate L, C and H weights for the scoring color distance
- With OKLCh hue-weighted as the "Major color shift" distance in "0. Optimize criteria", lightness, chroma and hue differences each have their own weight (default 1 · 1 · 2), saved with the preset.

### Clean Up as Pixels layout fixes
- Choosing a preset right after the update no longer shows it as unsaved: settings saved with a palette that the chosen method doesn't use now count as the same.
- "4. Output size" has an Auto checkbox linked to the Auto checkbox in the output size bar under the source.
- A long color swatch list scrolls once it would take more than half of the preview panel, and the swatch for a hovered color scrolls into view.
- "Keep distinct hues" puts Min area on its own line so it is no longer cut off.

## v1.14.58 (2026-10-28)
### AI status window stays dismissed after updates
- "Don't show again" in the AI status window now survives Gpixel updates and AI CLI self-updates; it reappears only when an AI CLI location or custom path changes.
- The "Confirm and open AI" button is sized to its label instead of being cut off.

### Auto-optimize shows preset results instead of rankings
- Starting auto-optimize in Clean Up as Pixels replaces the ranked candidates with one tile per optimization preset, marked Waiting or Optimizing with the best score so far, and fills each tile as its preset finishes.
- Adding or removing fixed colors while preset results are shown keeps the preset tiles: each result keeps its optimized settings and only the fixed colors change.
- Cancelling before any preset finishes brings the ranked candidates back.

## v1.14.57 (2026-10-28)
### Simpler homepage navigation
- Removed the GitHub link from the homepage header; the footer still links to the repository.
- The Korean header now labels the download link "다운로드" to match the main download button.

### Homepage feature descriptions updated
- The homepage now describes script effects, the asset manager and adjustable effect recipes, along with responsive rendering of heavy effects.
- The download section and ZIP links use consistent "다운로드" labels in Korean.

### AI tool calls show elapsed time
- Completed tool calls in the AI chat show their own duration at the end of the line, such as `get_image (1m 20s)`.
- Failed tool calls keep their error message and also show the duration; both are saved in the conversation history.

### Automatic AI drawing mode
- Automatic is now the default: new artwork without an exact size uses image generation and pixel conversion; exact pixel sizes and edits to the open artwork use direct pixel drawing.
- The two manual modes remain available. Previously saved "Generate" settings switch to Automatic once because old settings did not distinguish a default from a manual choice; a saved "Direct" mode is retained.

### Direct drawing reviews the complete picture
- For direct pixel drawing, the assistant plans against the user's written requirements and any attached images, including proportions and part placement, before drawing. Image generation is unchanged.
- After a successful drawing edit, the app starts one additional review pass with the full composited canvas attached. The assistant scores visual fidelity against the original requirements, corrects visible defects and reports remaining issues; the score is its visual judgment, not an objective similarity measurement.

## v1.14.56 (2026-10-28)
### App icon is now the plain chick
- The app, window, Dock and taskbar icon shows the default pixel chick without the palette or background, scaled on a consistent 16×16 pixel grid.
- The website's logo and favicon use the same picture.

## v1.14.55 (2026-10-28)
### Heavy effects no longer freeze the editor
- Layer effects on large images or many frames are computed on background threads, so the canvas, timeline, preview and effects window keep responding while you drag effect settings, draw or play.
- When a frame takes longer than 150 ms, it is covered with a translucent gray layer that shows the intermediate result as it is computed, with a spinning progress badge; the cover lifts once the whole frame is done. Light work still updates instantly without a cover.
- Each effect now splits its rows across CPU cores (about 3 to 5 times faster on 1024×1024 and larger images), afterimage ghosts are reused between neighbouring frames, and changing one effect value recomputes from that effect onward.
- Changing a layer's effects or visibility only recomputes results that include that layer; timeline thumbnails are kept at thumbnail size and cached results stay within a memory budget.
- The effects window draws every frame once in the background and shares it between the frame strip, the before/after preview and the canvas-expansion size, instead of redrawing all frames on every change.
- Pressing OK or Apply permanently in the effects window no longer freezes the window while it measures how far the effects reach or bakes them (1920×1080 with explode and 30 more frames: about 2 to 3 seconds). It now waits for the frames already being drawn in the background and shows the progress cover meanwhile.
- In the effects window, dragging a number box or the frame strip on a heavy image (all frames take over 50 ms to draw) no longer redraws every frame at each step: the previews keep the last result under the gray cover and everything is drawn once when you release. Dragging an effect bar's range in the timeline likewise only moves the bar and applies the new range on release. Confirming an effect or releasing a drag no longer redraws frames whose effects did not actually change.
- The effects window prepares the before/after pictures of every frame once after drawing, so playback only swaps pictures instead of cropping and converting the full image each frame.

### Delete frames from the effects window's frame strip
- The frame strip in the effects window ends with a dashed trash cell. Drag it left and the frames from there to the last one turn red; release to mark them for deletion (the first frame always stays). The marked frames are removed from the project when you press OK or Apply permanently, in the same undo step as the effects; effect ranges and tags shrink the same way as deleting frames in the timeline. Click the trash cell to bring back the frames you deleted last (one step). Extending frames after deleting (dragging a range past the last frame or raising the frame count) discards the deleted frames and adds new frames after the remaining ones.

### Long saves, exports and opens show a progress cover
- Saving with Ctrl+S, exporting, opening a project and creating a GIF run on a background thread. If one takes longer than 150 ms, the window is covered with a dark translucent layer that shows what is happening, a spinning icon and a percentage.
- Autosave no longer interrupts editing: it copies the document and writes the copy in the background, so you can keep drawing or dragging while it saves. Only very large documents (over about 768 MB of pixels) still show the cover during autosave.
- While the cover is up, editing, shortcuts, file drops and AI tool calls wait until the work finishes; closing the window closes it right after.
- Saving and exporting are much faster: cel and sheet images are compressed on several CPU cores, and export reuses afterimage ghosts between frames (1024×1024, 60 frames, heavy effects: save with export 7.1 s to 1.5 s, open 0.4 s to 0.07 s).
- Large exported PNGs (about 1450×1450 and up) have the same pixels but may differ by about 0.1% in file size, because they are compressed in pieces.

### Effects are saved with stable type IDs, and unknown effects are kept
- Layer effects are now saved with permanent type IDs (such as `gpixel.effect.outline`) and a settings object. The project format is now version 4, so earlier Gpixel versions cannot open projects saved by this version.
- Projects and custom effect presets saved by earlier versions open with the same effects and the same pixels; their old effect names are converted when read.
- An effect this version does not know (for example one added by a newer Gpixel) is no longer dropped when opening a project: the effects window lists it as an unknown effect that is not drawn and cannot be edited, and saving keeps it unchanged.
- AI tools list effect types with the same IDs, and every effect setting has an explicit default and range.

### Asset manager and local asset packages
- A new **Asset Manager** (Tools menu, and buttons in the effects window's Custom tab and the gradient preset window) lists personal assets and installed packages with previews, author, version, required app version and source.
- Effect recipes and gradients can be exported to and imported from `.gpixel-asset` package files. Packages are checked before anything is written (paths, sizes, file list and SHA-256, every effect against its settings schema, preview images) and installed atomically, so an interrupted install leaves the previous state intact.
- Turning a package off hides its recipes and gradients from the effects window, gradient menus and AI tools without removing its files; deleting moves it to a 30-day restorable archive, and installing a newer version keeps the previous one for rollback. Effects and gradients already inserted into projects never change.
- On first start, custom effects (`effect-presets.json`) and gradient presets (`gradient-presets.json`) move into personal assets once; the old files are kept as dated backups and are no longer read or written.

### Script effects: write your own effects in JavaScript
- The effects window has **New script** (starts from a light sweep example) and **Open JS file…** below the built-in effect types. A script declares `effect({ name, settings, render(img, ctx, s) })`; its settings panel (sliders, checkboxes, colors, choices) is built from the declaration, and the canvas, timeline, preview and export draw it exactly like built-in effects.
- The script editor shows syntax and declaration errors with line and column, runtime errors with their line, and the time per frame in the current preview. Applying an edited source switches the effect to the new source.
- The source is stored once in the project by its hash, and sources no longer used are dropped on save. Opening a project with scripts you have not seen asks whether to run each one (name, author, hash) and remembers the choice; scripts you write or import on this computer run without asking.
- Scripts run in an isolated engine with no file, network or .NET access. If a frame goes over the limits (2 seconds, 50 million statements, 256 MB), only that effect is skipped on that frame and marked in the effects list and timeline; after 3 failures in a row it stops until you turn it on again or press **Run again**.

### Browse, install and update assets from the online catalog
- The asset manager's **Browse** tab reads the online asset list from the Gpixel website over HTTPS, with search, effect/gradient and tag filters, and a detail view with preview, author, license, contents, required app version, size, dates and review status.
- **Install** and **Update to (version)** download with an inline progress bar and **Cancel**, retry brief connection failures, and install only when the file's SHA-256 matches the list; a package this version can't use shows the exact reason and can't be installed.
- The **Updates** tab lists installed packages that have a newer version and updates each one only when you click. Without a connection, the last downloaded list stays visible with a notice and installed assets keep working.
- The first official packages hold one asset each, so every gradient and effect shows up, is searched, previewed, installed and removed on its own in the asset manager: eight gradients (Rainbow, Repeating Rainbow, Sunset, Ocean, Transparent Fade, Fire, Forest, Grayscale) and three effect recipes made from built-in effects (Outlined Drop Shadow, Hit Reaction, Shiny Pickup). Catalog packages live in `catalog/`, and `tools/CatalogBuilder` checks them and builds the website's `docs/catalog/` (CI fails when it is out of date).

### Effect recipes can expose a few adjustable values
- Right-click a personal recipe in the effects window's Custom tab (or select it in the Asset Manager) and choose **Edit parameters** to pick which effect settings users can adjust: a color, a whole number, an on/off switch or a choice. One parameter can drive several settings of the same kind, and its range and choices stay within what every linked setting allows.
- A recipe with parameters is inserted as one item in the effect list that shows only its parameters; changing a value updates every linked effect setting at once, and the effects window is still a single undo step. Expand the item to edit the effects inside, or ungroup it into ordinary effects.
- The inserted effects keep their parameter definitions in the project file (an optional field of project format 4), so the project still edits and renders after the recipe or its package is deleted.
- Package checks reject parameters that point to a missing effect or unknown setting, use a different value kind, link the same setting twice, or allow values outside the effect's settings schema. Recipes with parameters are written as recipe format 2; recipes without them stay format 1.

### Publish your own assets to the online catalog
- **Publish Online…** in the asset manager (toolbar, and on a personal asset's details) bundles personal effect recipes and gradients into a public package: author ID, display name and website (remembered), package ID, version (the next version is suggested from your last one), name, short description, tags, license (CC0, CC BY, CC BY-SA, CC BY-NC, MIT, Apache 2.0 or your own `LicenseRef-` with its text), list preview and included assets.
- A pre-submission check runs as you type and lists errors (which block saving) and warnings, using exactly the rules the catalog CI applies: no package without a public author or license, author ID as the first part of the package ID, a list preview, and no changed or lowered published version.
- **Save and Open Submission Page…** saves the `.gpixel-asset` and a submission zip, then opens the new GitHub asset submission form prefilled with the package details. Gpixel uploads nothing; maintainers unpack the zip into `catalog/` and run `tools/CatalogBuilder`.
- Plain personal export still works as before; it now uses your remembered display name instead of the computer's user name.

### Script effects travel with recipes and packages
- Asset packages can hold script effects (`assets/scripts/*.js`). They appear as tiles in the effects window's Custom tab and in the asset manager; every script is checked before install (size, syntax and declaration, run inside the sandbox with tight limits).
- Effect recipes that contain script effects now carry the script sources, so a recipe saved as a personal asset, exported, published or installed on another computer draws the same once inserted; inserting copies the source into the project. Recipes without scripts are still written in the old format.
- Scripts that come from a package (file or online) never run until you click **Allow to run**; the tile and the asset manager say when a package contains scripts. **Save as asset** in a script effect's settings keeps an allowed script as a personal asset.
- The saved undo history now keeps the sources of older script versions, so undoing to an earlier script after reopening still draws it. Script effects this version cannot read keep their source and recipe group when saved.
- A script whose `padding` fails now counts as a failed run (shown in the effect list and suspended after repeated failures) instead of silently clipping, and a frame where a script failed is no longer cached as if it had succeeded.

### Pixelate current layer moved to the AI menu
- **Pixelate current layer…** moved from the Edit menu to the AI menu, right below **Pixelate AI image…**, which opens the same window. A shortcut you had assigned to it needs to be set again.

## v1.14.54 (2026-10-28)
### Automatically found AI CLI paths no longer require PATH setup
- Gpixel uses the full path of a discovered Claude Code or Codex executable and adds its folder to the child process PATH, so AI Status no longer warns when the executable is absent from Gpixel's own PATH.
- AI Status now starts Claude Code without sending a message or model request, supplies the same strict temporary MCP configuration as chat and waits for the Gpixel server to receive `tools/list`; success is shown as a verified live CLI connection.
- If a saved executable path exists but cannot run or complete the MCP handshake, Gpixel tries the remaining known locations, selects the first fully working CLI and clears or replaces the stale path automatically.

## v1.14.53 (2026-10-28)
### New key feature video: comparing cell by cell in the enlarged view
- The website's key features, the guide's Quick tasks > Pixelate and the in-app guide (F1) open with a new video showing the cell grid on the source and the enlarged view comparing result and source cell by cell.
- The README lists the same video among the key features.

### Effects window: four effect types per row, full scrolling and playback in the enlarged preview
- The effect type list shows four types per row, and scrolling now reaches the last row (the bottom tiles used to be cut off).
- The enlarged before/after preview (click the preview) has Play, previous/next frame buttons and the current frame, plus **Selected effect range only** and **Loop end to start**, which stay in sync with the effects window.
- In the enlarged preview, Space plays or pauses and ←/→ step frames.

## v1.14.52 (2026-10-27)
### New app icon: a chick holding a paint palette
- The app, window, Dock and taskbar icon is now a pixel chick holding a paint palette on a purple background, drawn at 32×32.
- The website's logo and favicon use the same picture.

### Website and coffee buttons in About Gpixel
- **Help > About Gpixel…** has two new buttons below the links: **Website** opens the site in your UI language, and **Buy the developer a coffee** opens the support page.
- Hovering either button shows the address it opens.

## v1.14.51 (2026-10-27)
### Support Gpixel development from the website and in-app guide
- The website download section now links to the Gpixel Buy Me a Coffee page.
- The in-app guide (F1) has a support button beside Open on website, with Korean and English labels.
- The guide's feedback section explains both ways to reach the support page.

## v1.14.50 (2026-10-27)
### Remember the AI Status dismissal when closing the window
- The "Don't show again" choice now saves when AI Status is closed with the window close button or Cancel, not only with Continue or Close.
- A new Gpixel version or a changed CLI file or path still shows AI Status again, as indicated below the checkbox.

## v1.14.49 (2026-10-27)
### Clearer AI CLI and MCP connection status
- AI Status distinguishes the executable Gpixel can run from the CLI found on Gpixel's current PATH; it warns when they are missing or different.
- MCP configuration is labeled as unverified until a chat message tests the live connection. If that connection fails, chat shows the exact CLI path it used.
- After opening the install/update terminal, keep AI Status open: it detects a new or updated CLI, switches from an unchanged manual override to automatic discovery, and checks sign-in again. Chat also refreshes its session when the selected path changes.

### Save and copy images from AI chat
- Right-click generated images, conversation snapshots, and attachments to save them as PNG or copy them. Click an image once to open its larger preview, which also offers the image menu.
- Image saving and system clipboard copying share the same actions used by the main window.

## v1.14.48 (2026-10-27)
### Clear starting paths and scannable long explanations in the guide
- The guide home now links directly to a first-time path and to image conversion, AI drawing, animation, skins, and export tasks.
- The table of contents separates **Quick tasks** from **Feature reference**, and long Pixelate settings, optimization, and preview explanations are split under topic headings.
- The development guide now defines consistent rules for headings, procedures, UI terms, figures, translation, and reviewing the rendered result.
- Preview figures now run from ① through ⑥, and control and context-menu descriptions use tables. The documentation check also rejects out-of-order first appearances of figure numbers.
- Videos and animations without numbered callouts now appear directly below their page heading.

## v1.14.47 (2026-10-27)
### Guide (F1): figures first, explanations below
- Every page now shows the screenshot first with the explanation of its numbers (①…) right below it. Pages with several screenshots put each one's explanation under it.
- Lists that start with figure numbers no longer show a second number such as "1. ②"; the figure number is the bullet.

## v1.14.46 (2026-10-27)
### Shape tool: line, curve, rectangle, ellipse, triangles, polygons, stars, spiral, cylinder, cube
- The toolbar's line group slot is now a single **Shape** (R) slot, and you pick the shape with the **Shape** buttons in Tool options. The old Line, Rectangle and Ellipse tools are now shapes of this tool; L picks the Shape tool's line (Shift snaps to 15° steps) and the Ellipse (O) shortcut is gone.
- **Equilateral triangle** uses the dragged line as one side; **Isosceles triangle**, **Regular polygon** (3–32 corners) and **Star** (3–32 points) fit the dragged box. Shift keeps the proportions undistorted.
- **Curve**: drag to set both ends, then move the mouse to bend it and click to confirm (Shift makes a circular arc, Esc cancels). **Spiral** (1–10 turns), **Cylinder** and **Cube** (2:1 isometric) are also available.
- Lines and curves let you choose **Pixel steps**: **Diagonal** (thinnest line) or **Orthogonal** (every pixel touches the next by an edge).
- Fill (U), line width, symmetry and right button = eraser work as they did for rectangles and ellipses, and the shape and counts are remembered.

### Shortcuts in the guide (F1) follow your settings and OS
- Command shortcuts in the guide show the keys you set in **Preferences > Shortcuts** ("no shortcut" if you removed them).
- On macOS, Ctrl, Alt and Shift in the guide appear as ⌘, ⌥ and ⇧, matching the menus.

### Shift line preview for brush tools
- After drawing with the pencil, eraser or another brush tool, holding Shift previews the straight line a click would draw from the last point (with brush size and symmetry).

## v1.14.45 (2026-10-27)
### Guide: saving and copying in the Pixelate window
- The homepage and in-app (F1) guide now explain right-clicking the source image and candidate tiles in the Pixelate window to copy (1× or ×4) or save as PNG, and where **Rerun only this preset** appears.

## v1.14.44 (2026-10-27)
### Save or copy images from the Pixelate window
- Right-click the source image on the left to **Copy image** or **Save as PNG…**. With **Show background removed** on, the background-removed source is used.
- Right-click any candidate tile to copy its result (1× or enlarged ×4) or save it as PNG; optimized candidates also keep **Rerun only this preset** in the same menu.
- Copies go to both Gpixel's clipboard and the system clipboard, so you can paste into the canvas or other apps. Multi-frame results use their first frame.

## v1.14.43 (2026-10-27)
### Ctrl does the same thing in canvas size mode with or without 9-slice
- A plain handle drag now always moves only the canvas edge (adds margin or crops), and Ctrl+drag always resizes the image too, whether or not **Stretch with 9-slice** is on. The checkbox only decides how the image is resized: with 9-slice (corners and edges kept) or nearest-neighbor.
- The canvas width/height numbers no longer resize the image when 9-slice is on; use Ctrl+drag or the image width/height fields.

### User guide brought up to date
- The homepage and in-app (F1) guide now cover recent changes: the six cell color modes, the new fidelity score, local grid correction and dither behavior, the 0–1000 OKLCh hue weight, the separate Auto Optimize Settings window with optimizer presets, stop-reason markers and **Rerun only this preset**, gradient color spaces and presets, the Presets window and the Presets tab in Preferences, and the "i" info icons in Tool settings.
- The affected screenshots (auto optimize, gradient, tool settings and tool pages) were retaken.

## v1.14.42 (2026-10-27)
### Guide: stretching with 9-slice in canvas size mode
- The Canvas size mode page of the homepage and in-app (F1) guide now explains **Stretch with 9-slice** and the Stretch / Tile middle fill, with a looping recording of stretching, shrinking and tiling a 9-slice sprite.
- The page's screenshot was retaken with the new checkbox marked, and the 9-slice page links to it.

### Terminating Gpixel on macOS and Linux closes it like the window
- A SIGTERM (for example `kill`, or the new `gpixel stop` / `restart` developer command) no longer ends Gpixel immediately: it closes the main window as if you clicked close, so unsaved documents are asked about and settings are saved. Cancelling keeps Gpixel running.
- Developers on macOS and Linux get `./dev.sh start | restart | stop | status` and `tools/install-dev-command.sh`, which installs a `gpixel` command for them (Windows already had `dev.bat`; `close` now works as an alias for `stop` on both).

## v1.14.41 (2026-10-27)
### Canvas size mode stretches with 9-slice
- Canvas size mode (Ctrl+Alt+C) has a new **Stretch with 9-slice** option, on by default. When the frame or a part has 9-slice borders, the handles and canvas size numbers resize the image too: corners stay as they are, edges stretch in one direction only, and only the middle grows, like a Unity UI image with Image Type = Sliced.
- Choose **Stretch** (nearest neighbor, Unity Sliced) or **Tile** (repeat the original middle pattern, Unity Tiled). The borders are shown as green dotted lines while dragging, and the image can't get smaller than both borders plus one pixel.
- After applying, the 9-slice borders keep the same pixel counts from the image edges (plus any margin), so the Unity Sprite Border still matches. Parts with their own borders are stretched with those, and pivots follow the same mapping.
- With 9-slice on, Ctrl+drag moves only the canvas edge (the old default); without borders, the mode works as before.

### Long explanations in tool settings moved behind an info icon
- The long help texts in Tool settings (canvas size, transform, gradient, 9-slice, eyedropper) and the usage hints for selection, move and gradient tools now appear when you hover the small "i" icon next to the title instead of taking up space under the controls.

## v1.14.40 (2026-10-27)
### Screenshots in the user guide
- The homepage guide and the in-app guide (F1) now show screenshots of the real app on nearly every page, including right-click menus, dropdowns and settings popups.
- What each page explains is marked on the image with a red box and a red number, and the text and the list under each image refer to the same numbers (①②…).
- One set of English screenshots is used for both the Korean and English guides. Click an image on the homepage to open it at full size.
- Eight pages also show a looping recording of the action (number fields, document tabs, selecting and editing in the timeline, tags, layer effects, transform mode and canvas size mode).

### Optimized candidates show why they stopped, and can be rerun alone
- The blue line under each optimized candidate in Pixelate now shows why that preset stopped, with its score: "Optimized (87.6)" when it stopped improving, "Time limit (60.4)" or "Trial limit (…)" when the budget ran out, and "Cancelled (…)".
- Right-click an optimized candidate and choose **Rerun only this preset** to run that preset again from the same starting settings. Only that tile is replaced in place; the other candidates stay.

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
