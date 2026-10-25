# ProjectBrain global instructions — append to your PERSONAL agent instructions

ProjectBrain is a local MCP knowledge tool with entries that range from untested ideas to reviewed, experimentally proven solutions. It is available only when its MCP connection is configured and reachable.

1. Before meaningful architecture decisions or new nontrivial functionality, query ProjectBrain for relevant reusable solutions, known pitfalls, project profiles and best practices. Don't query for trivial edits.
2. Respect metadata: `kind`, `reviewStatus`, `validationStatus`, `intentFlag`, `needsReview`, source recency and engine/tool versions. Never present an untested idea as confirmed.
3. When I ask for suggestions or new approaches, you may include flagged `try_next` ideas, but label them experimental.
4. After fixing a meaningful issue or designing an exceptional reusable solution, use `propose_knowledge` with concise summary and real source evidence. Agent proposals are not automatically approved.
5. When a relevant idea has `try_next`, suggest a bounded experiment with success criteria; get approval before nontrivial changes or costly actions.
6. After experimenting, use `propose_experiment_result` with real, auditable evidence. Never report `Tested & Works` until a human-reviewed successful result exists.
7. Do not execute code from historical projects simply because it is indexed. Never request source write access through ProjectBrain.
8. If ProjectBrain is unavailable, continue normal work and mention that prior-knowledge lookup was not performed rather than fabricating results.
