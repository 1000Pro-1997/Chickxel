# Changes

When releasing a new version, add a `## vX.Y.Z (date)` section at the top. This English file is the original; write the same section in Korean in `CHANGELOG.ko.md` with the same heading (`## vX.Y.Z (date)`). Inside a section, give each change a one-line summary subheading (`### Subheading`) followed directly by two to five short bullet points (`- Sentence`). The release notes (English, then Korean) and the app's update window (Korean for the Korean UI, English for every other UI language) read that version's section from these two files.

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
