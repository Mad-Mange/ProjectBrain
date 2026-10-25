# MCP Client Configuration: authentication is required

These are **illustrative** formats. The ProjectBrain v2 server requires an Authorization bearer token for MCP at `/mcp`. Set `PROJECTBRAIN_AUTH_TOKEN` in the host process environment for Codex and Copilot examples; never commit values to project files or paste secrets into a prompt.

- `codex-config.toml` uses `bearer_token_env_var` (check installed Codex version).
- `copilot-portable.mcp.json` uses portable `${PROJECTBRAIN_AUTH_TOKEN}` substitution; check the Copilot version and whether environment lookup is enabled.
- `vscode-mcp.json` uses legacy VS Code `${env:PROJECTBRAIN_AUTH_TOKEN}` substitution and top-level `servers`. For newer Copilot harnesses prefer portable global setup.
- `claude-code.mcp.json` is only a transport template. **It lacks the required bearer header**, and WILL NOT authenticate as-is. Prefer `claude mcp add` user scope with supported secure header authentication and verify a real tool call. Inspect your installed Claude Code documentation for current commands before configuring.

`127.0.0.1` points to the machine running the agent. Cloud-run agents cannot reach your Windows machine through this address. Do not expose a local HTTP MCP server to the public internet.
