# 0018 Export dump rules

Status: accepted

- `scripts/export.sh` writes `docs/llm/dump.txt` from files git tracks or would track.
- Excluded: everything under `docs/llm/`, build and tool folders (`bin`, `obj`, `node_modules`, `artifacts`, `.velixir`, ...), binary files, `.env` files, `package-lock.json` (generated, third-party metadata) and `LICENSE` (third-party text).
- Included: all source, config, scripts, workflows and the rest of `docs/`, including these ADRs.
- Nothing is redacted. The repository holds no secrets; the deploy key is a GitHub secret.
