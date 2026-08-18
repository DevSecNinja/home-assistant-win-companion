# Home Assistant Example Contract

Every published example MUST provide:

1. A concise purpose and user-visible outcome.
2. Prerequisites, including relevant Home Assistant capability or version.
3. Complete installation instructions appropriate to its artifact type.
4. A configuration block that contains no secrets or personal identifiers.
5. An explicit list of every placeholder and its expected replacement.
6. Expected behavior for success, unavailable inputs, and recovery.
7. Timing caveats when behavior depends on polling or scheduled evaluation.
8. Complete removal instructions.

Each example MUST occupy its own lowercase, hyphen-separated directory. Its
`README.md` and configuration artifacts MUST remain together in that directory.

## Category Contract

- `templates/` contains reusable template entities and explains whether each is
  installed through YAML or the Template helper.
- `automations/` contains automation artifacts and identifies whether each is
  directly importable, blueprint-based, or manually installed.
- An example MUST NOT be described as importable unless Home Assistant provides a
  supported import path for that exact artifact.

## Device Connectivity Contract

- Input: one exact Windows Companion device name.
- Default timeout: three minutes.
- Output: a binary sensor with connectivity semantics.
- Freshness source: the newest server-maintained `last_reported` among the
  device's sensor entities.
- Missing device or sensor behavior: disconnected.
- Recovery: the next report returns the output to connected.
- Network behavior: no additional companion message or timestamp sensor.
