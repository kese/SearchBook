# SearchBook design system

SearchBook uses a compact EMR-inspired Windows work surface: clinical blue-gray
chrome, dense record tables, restrained semantic color, and clear task state.
`DesignReferences/awesome-design-md` (commit
`8147538b4226ae41e2487a9179e3bcc1f68e8554`) remains a project-local reference
for hierarchy and design documentation, but the product UI is intentionally not
an IBM/Carbon reproduction.

## Intent

The app should feel like a dependable record workstation rather than a marketing
dashboard. Frequently used controls stay close to the data, decorative labels are
removed, and the results grid owns most of the window.

## Core tokens

- App canvas: `#E8EFF3`
- Record surface: `#FFFFFF`
- Section header: `#EDF4F7`
- Hairline: `#C9D6DE`
- Ink: `#233746`
- Muted ink: `#667A89`
- Primary: `#176B87`
- Title chrome: `#234A63`
- Success: `#24785D`
- Warning surface: `#FFF8E5`
- Error: `#B44545`
- Spacing follows a 4px grid; corners use a restrained 3–6px radius.

## Typography and icons

- Use Segoe UI with Malgun Gothic fallback.
- Body and table text are 10–13px; section titles are 14px.
- Use semibold only for section names, actions, headers, and key values.
- Use Windows `Segoe MDL2 Assets` for command icons.
- Runtime file-transfer motion must load the current system folder and document
  icons through Windows shell APIs; do not substitute emoji or decorative art.

## Window shell

- Keep the custom 42px title bar and functional minimize, maximize/restore, close,
  dragging, and resize borders. Do not restore the default Windows title bar.
- The secondary 42px bar contains file/template/export actions only.
- Do not add redundant page names, workflow slogans, context strips, or LOCAL pills.
- Keep the 25px footer for the active path and platform only.

## Work surface

- The left rail contains only the target file and request conditions.
- The top work band contains counts, remaining time, and the measured speed graph.
- While a query runs, the primary start button changes into the compact Windows
  file-transfer animation; no separate animation panel is shown.
- The results table fills the remaining area with 31px rows, zebra striping, and
  fixed blue-gray headers.
- Progress uses a thin teal rule.

## Interaction rules

- Teal indicates primary action and live progress; green, amber, and red are
  reserved for semantic state.
- The active result workbook must never be opened while autosave may replace it.
  Create a uniquely named snapshot under `results/previews` and open that copy.
- The Excel result action keeps visible top and bottom breathing room.
- Query completion must remain non-modal.

## Avoid

- Large branded headers, redundant breadcrumbs, numbered card ornaments, and
  unused animation columns.
- Emoji, custom-drawn faux Windows icons, or blank shell AVI placeholders.
- Multiple competing accent colors, floating shadows, or excessive pills.
- Opening a workbook that the autosave loop still needs to replace.
