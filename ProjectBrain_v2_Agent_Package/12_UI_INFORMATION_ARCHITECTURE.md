# 12 — Frontend Implementation and UX Details (v2)

## Navigation
Left sidebar: Dashboard, Projects, Knowledge, Idea Inbox, Try Next, Experiments, Review Queue, Activity, Settings. Header: global search, `+ Quick Capture`, last scan state, pending review count.

## Dashboard
- Sources tracked / available
- Scan status, last successful run, due time, root unavailable warning
- Changed projects since previous scan
- New candidates for review
- Recent captures, `Try Next` count, tests awaiting approval

## Quick Capture mockup
```text
┌──────────────────────────────────────────────────────────┐
│  + QUICK CAPTURE                                      X │
├──────────────────────────────────────────────────────────┤
│ [ Idea ▼ ] [ Best Practice ] [ Lesson Learned ] [...]    │
│                                                          │
│ Paste text, code, thoughts, article URL, etc...          │
│ ┌──────────────────────────────────────────────────────┐ │
│ │ Maybe separate game rules into pure C++ for DQN ... │ │
│ └──────────────────────────────────────────────────────┘ │
│                                                          │
│ Source URL (optional): [______________________________]  │
│ Project (optional):    [ Any / unassigned            ▼]  │
│ Tags (optional):       [______________________________]  │
│ [x] I want to try this      Review: Draft / Untested      │
│                                                          │
│ [ Save to Inbox ] [ Save & add another ] [ Cancel ]       │
└──────────────────────────────────────────────────────────┘
```

## Knowledge item detail
Title, original text (expand), normalized summary, badges for Kind/Review/Validation/Intent; card with related projects, source evidence and stale alerts; action buttons `Flag: Try Next`, `Create Experiment`, `Propose as Best Practice`, `Mark Deprecation`, subject to permissions. Timeline of all edits, scan revisions, experiments, human approvals. User can edit note; preserve revision history.

## Experiments
- Planned: hypothesis, target project, success criteria, owner, target engine/runtime version.
- Running: explicit start and status, source/evidence references.
- Completed: outcome, actual test artifacts, reviewer, limitations.
- Proposed results from agents appear in review queue; no automatic green `works` status.

## Search
Filters: kind, language/engine, review status, validation status, intent flag, project, stale flag. Search result cards must show trust markers and source references. Distinguish approved tips from experimental ideas, ranking approved tested higher by default.

## Accessibility / UI rules
Keyboard accessible, meaningful labels, focus in popup, controlled `Esc`, progress status for scan, avoid giant modal; autosize textarea, responsive >320px, sanitize markdown, no unsafe innerHTML, empty states and loading states; locale English default (option sv-SE); dark mode optional. Quick Capture should require no mandatory metadata except content. Persist operation immediately.
