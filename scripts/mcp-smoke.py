"""Read-only Streamable HTTP MCP probe. Does not alter client configuration."""
import json
import os
import urllib.request

endpoint = os.environ.get("PROJECTBRAIN_E2E_API_URL", "http://127.0.0.1:4317").rstrip("/") + "/mcp"
token = os.environ.get("PROJECTBRAIN_AGENT_TOKEN") or os.environ.get("PROJECTBRAIN_AUTH_TOKEN")
if not token:
    raise SystemExit("Set PROJECTBRAIN_AGENT_TOKEN or PROJECTBRAIN_AUTH_TOKEN in this process.")
headers = {"Authorization": "Bearer " + token, "Content-Type": "application/json", "Accept": "application/json, text/event-stream"}

def rpc(method, params=None, request_id=1):
    payload = {"jsonrpc": "2.0", "method": method}
    if request_id is not None:
        payload["id"] = request_id
    if params is not None:
        payload["params"] = params
    request = urllib.request.Request(endpoint, data=json.dumps(payload).encode(), headers=headers)
    with urllib.request.urlopen(request, timeout=30) as response:
        data = response.read(65537).decode()
        if len(data) > 65536:
            raise RuntimeError("Response exceeded smoke-test budget.")
        session = response.headers.get("Mcp-Session-Id")
        if session:
            headers["Mcp-Session-Id"] = session
        if not data:
            return None
        if response.headers.get("Content-Type", "").startswith("text/event-stream"):
            data = next(line[5:].strip() for line in data.splitlines() if line.startswith("data:") and '"jsonrpc"' in line)
        result = json.loads(data)
        if "error" in result:
            raise RuntimeError("MCP returned an error: " + str(result["error"].get("code")))
        return result.get("result")

initialized = rpc("initialize", {"protocolVersion": "2025-11-25", "capabilities": {}, "clientInfo": {"name": "projectbrain-smoke", "version": "2.0"}}, 1)
headers["MCP-Protocol-Version"] = initialized["protocolVersion"]
rpc("notifications/initialized", request_id=None)
listed = rpc("tools/list", request_id=2)
names = {tool["name"] for tool in listed["tools"]}
assert {"search_knowledge", "propose_knowledge", "propose_experiment_result"} <= names
assert not any("approve" in name or "verify" in name for name in names)
for tool in listed["tools"]:
    hints = tool["annotations"]
    assert hints["readOnlyHint"] == (not tool["name"].startswith("propose_"))
    assert not hints["destructiveHint"] and not hints["openWorldHint"]
    assert hints["idempotentHint"]
result = rpc("tools/call", {"name": "search_knowledge", "arguments": {"query": "repository", "statusFilter": "all", "includeIdeas": True, "limit": 5}}, 3)
assert not result.get("isError", False)
print("PASS: initialized, listed", len(names), "tools, and called search_knowledge.")
project_id = os.environ.get("PROJECTBRAIN_MCP_PROFILE_ID")
if project_id:
    profile = rpc("tools/call", {"name": "get_project_profile", "arguments": {"projectId": project_id}}, 4)
    assert not profile.get("isError", False)
    rendered = json.dumps(profile)
    expected_name = os.environ.get("PROJECTBRAIN_MCP_PROFILE_NAME", "")
    assert expected_name and expected_name in rendered, "Project profile response did not contain the expected indexed project."
    print("PASS: get_project_profile returned indexed project", expected_name + ".")
print("Confirm the matching MCP tool log in docker compose logs api.")

