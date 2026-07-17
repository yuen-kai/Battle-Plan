# Improvement Run Schemas

Use these shapes to keep parallel reports comparable. Return valid JSON with no
markdown fence. Use empty arrays only when the field is genuinely inapplicable.

## Specialist audit

```json
{
  "discipline": "gameplay",
  "scopeReviewed": ["path or subsystem"],
  "facts": [
    {
      "claim": "Implemented fact",
      "evidence": ["path:symbol or runtime observation"],
      "confidence": "high"
    }
  ],
  "unknowns": [
    {
      "question": "What could not be established",
      "neededEvidence": "Exact runtime, asset, or user evidence"
    }
  ],
  "candidates": [
    {
      "id": "GAMEPLAY-01",
      "title": "Short improvement name",
      "problem": "Current player or production problem",
      "proposedOutcome": "Observable improved state, not implementation detail",
      "evidence": ["path:symbol", "test or runtime observation"],
      "factOrInference": "fact",
      "severity": "critical",
      "confidence": 5,
      "playerImpact": 5,
      "strategicFit": 5,
      "effort": 2,
      "regressionRisk": 2,
      "affectedFiles": ["likely/path"],
      "dependencies": [],
      "externalSources": [
        {
          "url": "https://source.example/asset",
          "author": "Creator name",
          "license": "CC0-1.0",
          "commercialUseVerified": true,
          "attributionRequired": false,
          "verificationStatus": "verified"
        }
      ],
      "acceptanceChecks": ["setup -> action -> observable result"],
      "rollbackBoundary": "Smallest reversible unit",
      "recommendedDisposition": "implement"
    }
  ]
}
```

Allowed values:

- `factOrInference`: `fact`, `inference`, `unknown`
- `severity`: `critical`, `high`, `medium`, `low`
- numeric scores: integer 1–5
- `recommendedDisposition`: `implement`, `experiment`, `defer`, `reject`
- `verificationStatus`: `verified`, `unclear`, `rejected`

Omit `externalSources` when no third-party source is proposed. Never mark a
source verified from a marketplace label or search snippet alone; verify the
asset page and license text.

## Candidate ranking

Compute a directional score:

`3 × playerImpact + 2 × confidence + 2 × strategicFit − effort − regressionRisk`

The score does not overrule:

- creative direction;
- user-approved scope;
- dependencies;
- evidence quality;
- Unity/editor constraints;
- conflicting uncommitted work.

Selection guardrails:

- Prefer systemic fixes over cosmetic symptom patches.
- Prefer one-variable balance experiments over bundled tuning.
- Do not select medium-confidence rule changes without an approved experiment.
- Do not select a candidate solely to represent every discipline.
- A large candidate is a final handoff unless the charter explicitly includes it.

## Technical verifier

```json
{
  "candidateId": "GAMEPLAY-01",
  "verdict": "confirmed",
  "currentBehavior": "What the code/assets actually do",
  "authorityBoundary": "Server/client/editor ownership if applicable",
  "evidence": ["path:symbol", "serialized value", "test observation"],
  "contradictions": ["Documentation or specialist claims that disagree"],
  "feasibility": "small",
  "requiredOwners": ["gameplay-engineer"],
  "requiredFiles": ["path"],
  "verificationNeeds": ["exact assertion"],
  "notes": "Remaining uncertainty"
}
```

Allowed `verdict`: `confirmed`, `partially-confirmed`, `rejected`, `runtime-needed`.

## Cross-review

```json
{
  "candidateId": "GAMEPLAY-01",
  "reviewingDiscipline": "systems-balance",
  "agreement": "agree-with-conditions",
  "benefits": ["Expected player benefit"],
  "tradeoffs": ["Cross-discipline cost or regression"],
  "requiredChanges": ["Condition before implementation"],
  "measurement": ["Metric and threshold"],
  "recommendation": "implement"
}
```

Allowed `agreement`: `agree`, `agree-with-conditions`, `disagree`.

## Producer implementation item

```json
{
  "candidateId": "GAMEPLAY-01",
  "owner": "gameplay-engineer",
  "ownedFiles": ["path", "path.meta"],
  "dependsOn": [],
  "expectedCompileOrImportImpact": "C# compile and domain reload",
  "acceptanceChecks": ["exact check"],
  "runtimeRequest": {
    "required": true,
    "setup": ["step"],
    "actions": ["step"],
    "passEvidence": ["observable"],
    "completionSignal": "RESULT line or state"
  },
  "rollbackBoundary": "Files or serialized values to reverse"
}
```

Every file and its `.meta` must have exactly one owner in a wave.

## Writer handoff

```json
{
  "candidateIds": ["GAMEPLAY-01"],
  "changedFiles": ["path"],
  "summary": ["Implemented outcome"],
  "assumptions": ["Assumption retained"],
  "compileOrImportImpact": "Expected effect",
  "selfChecks": ["Read-only or local check performed"],
  "requestedEditModeChecks": ["test or assertion"],
  "requestedSerializedChecks": ["asset/path/value assertion"],
  "requestedRuntimeCheck": {
    "required": true,
    "setup": ["step"],
    "actions": ["step"],
    "passEvidence": ["observable"],
    "completionSignal": "signal"
  },
  "knownRisks": ["Unresolved risk"]
}
```

Writers do not report Unity compilation or runtime success unless they held the
explicit sole Unity lease and actually observed it.

## QA acceptance result

```json
{
  "candidateId": "GAMEPLAY-01",
  "status": "pass",
  "checks": [
    {
      "name": "Exact acceptance check",
      "status": "pass",
      "evidence": "Observed result",
      "environment": "Edit Mode, Play Mode, serialized inspection, or device"
    }
  ],
  "newConsoleErrors": 0,
  "regressions": [],
  "unverified": [],
  "recommendedAction": "accept"
}
```

Allowed `status`: `pass`, `fail`, `partial`, `blocked`.
Allowed `recommendedAction`: `accept`, `repair`, `revert-candidate`, `defer`.

## Final review packet

```json
{
  "verdict": "ready-for-user-review",
  "implementedOutcomes": [
    {
      "candidateId": "GAMEPLAY-01",
      "playerOutcome": "What improved",
      "files": ["path"],
      "evidence": ["test count or runtime observation"]
    }
  ],
  "verification": {
    "editMode": "count/result",
    "playMode": "scenario/result",
    "console": "error count",
    "screenshots": ["path or omitted with reason"]
  },
  "decisions": ["Rule or assumption selected"],
  "deferred": ["Candidate and reason"],
  "residualRisks": ["Unverified risk"],
  "worktreeState": "Uncommitted; no push or PR",
  "manualFinalCheck": ["Small user-visible check"]
}
```

The user-facing final response should summarize this packet in plain language;
do not dump raw JSON unless requested.
