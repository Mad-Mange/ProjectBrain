# 10 — Global Agent Onboarding, Docker and Security (v2 — normative)

## Functional target
Install ProjectBrain **once** on Windows 11 using Docker Desktop. Global MCP connections and global agent instructions allow new projects to reuse knowledge without repeatedly mentioning ProjectBrain. On startup each agent should perform at least one knowledge search for significant architecture work, but cannot guarantee obedience: **verify actual calls** in tool logs. New projects under a mounted scan root appear on next scheduled scan (6h default); `Scan Now` immediate.

## Runtime
- `api`: ASP.NET Core .NET 10, REST `/api/v1`, MCP Streamable HTTP `/mcp`, scheduled scanner background hosted service, SQLite on Docker volume.
- `web`: React/TypeScript/Vite static build behind nginx unprivileged, reverse proxy `/api/` + `/health/`; browser accesses one origin.
- `projectbrain_data`: durable named volume; back up with SQLite online backup, including knowledge and raw captures.
- Mount `PROJECTS_ROOT` bind at `/sources/projects:ro`; API process, parser and backups NEVER write to it. Never execute downloaded/user code, git hooks, Blender .blend, Unity/Unreal editor or scripts during scanning. No need to rebuild sources.
- Host API `127.0.0.1:4317`; browser `127.0.0.1:4318`. Do not bind to 0.0.0.0 host. No internet required for deterministic work.
- `PROJECTBRAIN_AUTH_TOKEN` required on startup in `.env` for v2 to prevent casual unauthorized local HTTP writes; supply value generated locally using CSPRNG. Never commit token. Bearer token for REST/MCP except health. UI asks for token and keeps it in process memory (not localStorage). Provide clear re-entry after refresh. Review same-origin protections; validate Host + Origin, prevent DNS rebinding and CSRF. Local-only is NOT sufficient authentication.
- Configuration examples are **starter templates**, not a built or validated application. User/agent must replace `D:/Projects` and set a secret and build code.

### First-run commands (after code has been implemented)
```powershell
Copy-Item .env.example .env
# In .env set PROJECTS_ROOT=D:/Projects and PROJECTBRAIN_AUTH_TOKEN=<random 32+ byte secret>
docker compose config --quiet
docker compose up --build -d
docker compose ps
# Visit http://127.0.0.1:4318, provide the local token, authorize inventory and inspect projects.
```

### Scanner schedule
- Every 6h incremental scan, on-start overdue scan, weekly full hash audit, manual Scan Now.
- Missing/offline mount -> `unavailable`, do NOT tombstone files, do NOT mark all evidence stale.
- Content changed -> reindex affected profile/knowledge references, make evidence stale if applicable, never delete learned items.
- Quick Capture data unrelated to projects is never changed by scanner.
- Optional source host file watcher can trigger a debounced scan but is NOT authoritative on Docker Desktop Windows.

## Global agent setup
Exact client config formats change independently. Prefer built-in CLI commands when provided by vendor and **verify** by listing MCP tools. Examples are in `templates/mcp/` and instructions in `templates/agent-rules/`.

- **Codex**: user-wide `~/.codex/config.toml` has `[mcp_servers.projectbrain]` + `url`. Use `bearer_token_env_var = "PROJECTBRAIN_AUTH_TOKEN"` if supported by installed Codex; set env var in Codex process **without printing the token**. Global `~/.codex/AGENTS.md` instructs searches before significant work. Verify `codex mcp list` and actual call.
- **GitHub Copilot CLI**: add an HTTP server at the user scope using `copilot mcp add --transport http projectbrain http://127.0.0.1:4317/mcp`. Set Authorization using supported secure local config; inspect client docs for header/environment handling. Portable `~/.copilot/mcp-config.json` uses **`mcpServers`**. Personal instructions in `~/.copilot/copilot-instructions.md` or `~/.copilot/instructions/*.instructions.md`. Verify `copilot mcp list`.
- **GitHub Copilot VS Code**: latest Copilot harness reads portable user-level config. `MCP: Add Server -> HTTP -> Global -> Copilot Global` preferred; legacy `.vscode/mcp.json` uses **`servers`** rather than `mcpServers`. Check which harness is active. Do NOT assume CLI and VS Code legacy config are the same.
- **Claude Code**: add a user-scoped remote HTTP MCP server (e.g. `claude mcp add --transport http --scope user projectbrain http://127.0.0.1:4317/mcp`, adjust exact flag order for installed version), provide bearer auth via supported secure header mechanism. User-wide `~/.claude/CLAUDE.md` instructs retrieval; verify tool connection.
- **Claude inside Copilot**: inherits the Copilot client/harness tools; separate Claude Code configuration does not automatically apply.
- **Cloud-hosted agents**: `127.0.0.1` refers to their runtime, NOT the user's home workstation. Do not expose MCP port to internet as a shortcut; defer remote architecture, TLS/auth and tunnel/access controls.

## Agent instruction behavior
Before nontrivial design/implementation: search; evaluate trust, version, engine; apply only if relevant. For simple edits do not waste tokens on queries. After a significant solved issue: propose knowledge with evidence; do not self-verify. When an experiment is requested, explicitly seek user approval before running tests that modify current project or cost money. `ProjectBrain` never autonomously changes old projects.

## Project-specific registration
Optional `projectbrain init` CLI or website button (agent to implement v2): register project root or link existing project; offer to create project-level `AGENTS.md`/`CLAUDE.md` **only with explicit permission**. Global MCP should work with no project file. Never edit the historical project folders in bulk.

## References checked 2026-10-23
- https://learn.microsoft.com/en-us/dotnet/core/releases-and-support
- https://github.com/modelcontextprotocol/csharp-sdk/blob/main/docs/concepts/getting-started.md
- https://github.com/modelcontextprotocol/csharp-sdk/blob/main/docs/concepts/transports/transports.md
- https://developers.openai.com/learn/docs-mcp
- https://docs.github.com/en/copilot/how-tos/copilot-cli/customize-copilot/add-mcp-servers
- https://code.visualstudio.com/docs/agent-customization/mcp-servers
- https://code.visualstudio.com/docs/agents/reference/mcp-configuration
Always inspect current actual client version and vendor documentation before assuming auth flag syntax.
