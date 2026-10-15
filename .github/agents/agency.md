---
name: agency
description: Custom agent for the Agency PR review workflow
tools: ["*"]
mcp-servers:
  ado:
    type: local
    command: agency
    args: ["mcp", "ado"]
    tools:
      - "*"
      - "repo_create_pull_request_thread"
      - "repo_reply_to_comment"
      - "repo_update_pull_request_thread"
  kusto:
    type: local
    command: agency
    args: ["mcp", "kusto"]
    tools: ["*"]
  icm:
    type: local
    command: agency
    args: ["mcp", "icm"]
    tools: ["*"]
---

/agency Review the PR, publish only genuinely new findings or material updates, and verify that every actionable finding is covered by an existing or newly created thread.
