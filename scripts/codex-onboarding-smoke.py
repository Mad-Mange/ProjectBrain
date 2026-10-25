"""Check user-global ProjectBrain discovery from a new empty Codex project.

Uses the user's configured Codex model/login for one read-only session. No
per-project instructions/config are created and no primary API token is used.
"""
import datetime
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import tomllib
import uuid


repo = Path(__file__).resolve().parent.parent
codex_home = Path(os.environ.get("CODEX_HOME") or Path.home() / ".codex")
config = tomllib.loads((codex_home / "config.toml").read_text(encoding="utf-8-sig"))
server = config.get("mcp_servers", {}).get("projectbrain", {})
if server.get("url") != "http://127.0.0.1:4317/mcp" or server.get("bearer_token_env_var") != "PROJECTBRAIN_AGENT_TOKEN":
    raise SystemExit("Run scripts/connect-codex.ps1 before this user-global onboarding check.")
global_instructions = next((p for p in (codex_home / "AGENTS.override.md", codex_home / "AGENTS.md") if p.exists() and p.read_text(encoding="utf-8-sig").strip()), None)
if not global_instructions or "<!-- projectbrain:begin -->" not in global_instructions.read_text(encoding="utf-8-sig"):
    raise SystemExit("Global ProjectBrain retrieval instructions are missing.")

environment = os.environ.copy()
if not environment.get("PROJECTBRAIN_AGENT_TOKEN") and os.name == "nt":
    import winreg
    with winreg.OpenKey(winreg.HKEY_CURRENT_USER, "Environment") as key:
        environment["PROJECTBRAIN_AGENT_TOKEN"] = winreg.QueryValueEx(key, "PROJECTBRAIN_AGENT_TOKEN")[0]
if not environment.get("PROJECTBRAIN_AGENT_TOKEN"):
    raise SystemExit("The Codex user environment needs PROJECTBRAIN_AGENT_TOKEN.")

executable = shutil.which("codex.cmd" if os.name == "nt" else "codex")
if not executable:
    raise SystemExit("Codex CLI is not installed.")
project = Path(tempfile.mkdtemp(prefix="projectbrain-onboarding-"))
evidence = repo / ".local" / ("codex-onboarding-" + uuid.uuid4().hex[:10])
evidence.mkdir(parents=True)
prompt = """We are starting a new .NET command-line tool with SQLite persistence and a repository abstraction.
Before proposing its storage design, consult relevant prior project knowledge as required by your inherited instructions.
This is a read-only onboarding check: do not create or modify files, run shell commands, install software, or submit proposals.
Use your configured knowledge tools. If a narrow search is empty, try broader keywords.
Return at most two useful prior records with their IDs, project/source paths, and review and validation status.
Inspect one relevant project profile if available. Clearly distinguish static observations from evidence of tested success.
Answer briefly and never display credentials."""
command = [executable, "exec", "--ephemeral", "--json", "--color", "never", "--sandbox", "read-only",
           "--skip-git-repo-check", "--cd", str(project), "--output-last-message", str(evidence / "answer.txt"),
           "-c", "mcp_servers.projectbrain.disabled_tools=['propose_knowledge','propose_experiment_result']"]
for name in config.get("mcp_servers", {}):
    if name != "projectbrain":
        command.extend(["-c", f"mcp_servers.{name}.enabled=false"])
command.append("-")
started = datetime.datetime.now(datetime.timezone.utc).isoformat()
with (evidence / "events.jsonl").open("w", encoding="utf-8") as out, (evidence / "stderr.log").open("w", encoding="utf-8") as err:
    try:
        execution = subprocess.run(command, input=prompt, encoding="utf-8", env=environment, cwd=project,
                                   stdout=out, stderr=err, timeout=150)
        return_code = execution.returncode
    except subprocess.TimeoutExpired:
        raise SystemExit(f"Codex onboarding timed out; inspect {evidence}.") from None

events = []
for line in (evidence / "events.jsonl").read_text(encoding="utf-8").splitlines():
    try:
        events.append(json.loads(line))
    except json.JSONDecodeError:
        pass
calls = [e.get("item", {}) for e in events if e.get("type") == "item.completed" and e.get("item", {}).get("type") == "mcp_tool_call"]
brain_calls = [c for c in calls if c.get("server") == "projectbrain" and c.get("status") == "completed" and not c.get("error")]
records = []
for call in brain_calls:
    if call.get("tool") != "search_knowledge":
        continue
    for content in (call.get("result") or {}).get("content", []):
        if content.get("type") != "text":
            continue
        try:
            payload = json.loads(content["text"])
            for record in (payload.get("data") or {}).get("results", []):
                if record.get("id") and record.get("reviewStatus") and record.get("validationStatus"):
                    records.append({key: record.get(key) for key in ("id", "title", "reviewStatus", "validationStatus", "isVerified")})
        except (json.JSONDecodeError, AttributeError, TypeError):
            continue
shell_calls = [e for e in events if e.get("item", {}).get("type") == "command_execution"]
passed = return_code == 0 and bool(records) and not shell_calls and not list(project.iterdir())
report = {"passed": passed, "startedUtc": started, "newProject": str(project), "projectFiles": [p.name for p in project.iterdir()],
          "configScope": "user", "globalInstructions": str(global_instructions), "server": server["url"],
          "model": config.get("model"), "returnCode": return_code, "toolsCalled": [c.get("tool") for c in brain_calls],
          "records": records, "shellCommands": len(shell_calls), "evidence": str(evidence)}
(evidence / "report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
(repo / ".local" / "codex-onboarding-latest.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
if not passed:
    raise SystemExit(f"Codex onboarding not verified; inspect {evidence}.")
print("PASS: a new empty project used the user-global ProjectBrain MCP and retrieved real records with trust statuses.")
print("Tools:", ", ".join(report["toolsCalled"]))
print("No project files or shell commands. Evidence:", evidence)
