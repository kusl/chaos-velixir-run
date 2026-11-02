# 0024 Random article reader

Status: accepted

- `/read` is a static SSR page with no circuit. It redirects (302) to `/read/{id}` for a random article, so every article has a stable address and enhanced navigation shows the final URL. With no articles it renders a notice instead.
- Source: `wwwroot/nytimes.txt` (`Chaos:ArticlesFile`), plain UTF-8, also served as a static file. Records start with a header between two `=` rules (20 or more, up to 6 header lines). Inside: optional `SUMMARY` paragraph, `ARTICLE`, then a head paragraph (title, optional dek, `By …`, `Published yyyy-MM-dd HH:mm UTC`, source URL, matched from the end) and body paragraphs split by blank lines. Paragraphs whose every line starts with `- `, `• `, `* ` render as lists; other line breaks are kept. A file with no rules is one article.
- Id: first 12 hex digits of SHA-256 of the source URL, or of title and body without one. Stable across reordering and edits elsewhere in the file. The first record wins on duplicate ids.
- `ArticleLibrary` indexes the file once (offset and length per id, about 50 KB for 500 articles), then reads and parses one record per request. It re-indexes when the file's length or modified time changes, treats a missing, unreadable or over-64 MB file as empty, and returns nothing rather than throwing. A record that no longer parses to its id is dropped and the index rebuilt.
- Unknown or removed ids render "Not available" with a link to a random article (200, so the history list still renders).
- Reading history lives only in the browser: IndexedDB `chaos-reading` v1, store `articles`, key `id`, values `{ id, title, byline, published, first, last, reads }`, capped at 1,000 by least recent `last`. `App.razor.js` imports `Pages/ReadPage.razor.js` on load and `enhancedload` when `#reading-history` exists. The list is `data-permanent` and only written by that module.
- Typography: serif stack, 17–19 px, line height 1.7, 38 rem measure, hyphenation in the body, solid background behind the text.
