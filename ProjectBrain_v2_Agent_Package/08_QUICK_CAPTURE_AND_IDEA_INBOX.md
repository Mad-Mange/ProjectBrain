# 08 — Quick Capture, Idea Inbox, and Review UX (v2 — normative)

## Mission
A user can paste raw notes, ideas, best-practice candidates, code snippets, or URL references in <10 seconds. Their thought must be saved **even without AI, tags, category, or any project selected**. Never conflate a human's idea with a proven solution. This is a FIRST-CLASS MVP feature, not an optional extension.

## Navigation / screen flows
- Persistent `+ Quick Capture` button in web header, keyboard shortcut **Ctrl+K within the ProjectBrain web app** (do not capture OS-global keys from a browser).
- Screen tabs: `Inbox`, `Knowledge Library`, `Try Next`, `Experiments`, `Review Queue`.
- Quick Capture modal or page: multiline paste (required), optional title, optional dropdown `Idea | Best Practice | Lesson Learned | Reusable Solution` (default `Idea`), optional source URL, optional project association, optional tags, check `Want to try` (default off), save.
- On Save: local transaction persists original note immediately; show ID and success state. Never block save waiting for an LLM.
- After Save: background **deterministic** suggestions for title/tags/duplicates via keyword/FTS. Optional LLM enrichment only if explicitly enabled, with human approval for modifications. The untouched original text remains.
- Inbox item detail: original paste, normalized summary, type, badges, experiment flag, projects, related entries, timeline, verification evidence and edit history.
- `Try Next` filter contains entries flagged `try_next`, prioritized and searchable by engine/language/project; user can assign target project and experiment checklist.
- Agent-proposed entries ALWAYS appear in Review Queue; human's own Quick Capture item can be saved immediately as a personal draft (not auto-approved, not verified).
- Pasted markdown/code displayed safely: sanitize HTML/links, escape script tags; do not run pasted scripts or fetch links by default.
- For each entry show four **distinct** dimensions: Type; Review status; Experiment status; Intent flag. Do not imply verification from type alone.

## Data entry example
User pastes: `Maybe put pure C++ game rules outside Unreal Engine so reinforcement learning can run millions of steps without rendering.`
Saved with:
```json
{
  "kind": "idea",
  "originalText": "Maybe put pure C++ game rules outside Unreal Engine so reinforcement learning can run millions of steps without rendering.",
  "title": null,
  "intentFlag": "try_next",
  "reviewStatus": "draft",
  "validationStatus": "untested",
  "tags": ["unreal", "cpp", "reinforcement-learning"]
}
```
Tags in this example are optional suggestions; do not assume they were provided or confirmed. Later an experiment records project, design, tests and outcome.

## REST contract (`/api/v1`)
JSON camelCase; UTC timestamps; problem-details style errors; authentication required for all writes. See 10.

| Verb + path | Requirement |
|---|---|
| `POST /captures` | Accept raw text first, return `201 {captureId, knowledgeId, reviewStatus:"draft"}`; optional idempotency key header |
| `GET /captures?status=&query=&page=` | Inbox with pagination |
| `GET /captures/{id}` | Original note + metadata, edits and processing info |
| `PATCH /captures/{id}` | Human edit title/type/tags/project links/flag; optimistic version |
| `POST /captures/{id}/suggest` | Enqueue classification/duplicate suggestions; AI not required |
| `GET /knowledge/{id}` | Include new v2 status fields and source note relationship |
| `PATCH /knowledge/{id}` | Human-owned edits; validated version, audited |
| `GET /knowledge/search?q=&kind=&reviewStatus=&validationStatus=&intentFlag=` | Search filters and explicit badges |
| `GET /knowledge/try-next` | `intentFlag=try_next` sorted priority |
| `GET /knowledge/review-queue` | Agent proposals + flagged conflicts and stale evidence |
| `GET /captures/{id}/similar` | Ranked possible duplicates, **never destructive auto merge** |

Use normal response DTO shapes from `09_LIFECYCLE_EXPERIMENTS_AND_CONTRACTS.md`. Server uses the SAME application services for REST and MCP. Root `/api/v1` prefix is implied in table.

## Save behavior and idempotency
- Trim for empty check; preserve original raw content, meaningful whitespace and line breaks.
- Raw text <= 64 KiB (configurable); <= 30 tags; title <= 200 Unicode chars; URL <= 2048 chars.
- Do not auto-download external websites or images: a URL is a reference until explicit consent and safety checks are implemented.
- Idempotency key prevents accidental duplicate saves on retries; no automatic dedupe of two intentionally similar ideas.
- Search FTS5 indexes title, summary, tags and safe normalized text; retain original separately in storage.
- Add rate limiting (e.g. 30 writes/minute per local client) with clear error. Drafts remain searchable with badge `Untested / Draft` and separate filter; agents default to trusted results but may retrieve ideas explicitly.

## Experiment story end to end
1. Quick Capture idea, flag **Want to try**.
2. Future agent `search_knowledge` returns result explicitly marked `idea / draft / untested / try_next`.
3. User or agent opens experiment proposal, defines success criteria before testing.
4. Agent performs an experiment in the **active project** with normal agent privileges (ProjectBrain scanner itself never executes source code).
5. Agent submits test outcomes as a **proposal** and attaches evidence; user reviews, can set validation status `passed`, `failed`, or `inconclusive`.
6. Only after human approval and evidence can UI show `Tested & Works`; link to project/commit/tests and environment limitations.
7. Later relevant project change marks evidence `stale` or `needsReview`, not silently falsifies or deletes previous outcome.

## Stretch goal explicitly excluded from v2 MVP
System-wide `Ctrl+Alt+K` shortcut requires a separate signed/trusted Windows tray companion (outside Docker). Do not implement global keyboard hooks in the Docker/web app. UI shortcut is sufficient initially.

## Acceptance checks
- Create capture in offline mode/no LLM key; immediate persistence and visible Inbox row.
- Paste code with `<script>` and markdown: no code execution/XSS.
- Save same raw note intentionally twice = two records without idempotency key; retry with same key = one record.
- Switch type to Best Practice: stays unverified.
- Set Want to try: visible in Try Next, but not in Tested & Works.
- Propose experiment result from MCP: visible to human, not self-verified.
- Restart containers: all notes/status/history persist in named volume.
