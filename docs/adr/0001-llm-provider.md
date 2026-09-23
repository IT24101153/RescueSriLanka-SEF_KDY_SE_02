# ADR 0001 — LLM provider for the Agentic AI subsystem

- **Status:** Accepted
- **Date:** 2026-09-18
- **Component:** A — Incident & Disaster Map (Incident Analysis Agent)
- **Decision owner:** Student A

## Context

The project proposal commits to *"Ollama, self-hosted / local model"* under the
module's no-cost service requirement, and repeats that commitment in its risk
table. This ADR records a deliberate deviation from it.

The Incident Analysis Agent needs a model that can read an incident's text
**and its photographs**, because a citizen's description and the image they
attached are different kinds of evidence and the agent is specified to use both.

Three things decided the choice:

1. **Disk on the development machine.** The agent was built on a 16 GB Apple M2
   with roughly 20 GB of free disk. `llama3.2-vision`, the smallest Ollama model
   that reads images well, is about 8 GB. It fits, but not comfortably, and a
   text-only model is not a substitute: it does not fail when handed images, it
   silently ignores them and grades on words alone, which would make the
   photo-upload feature decorative.
2. **Schema adherence.** The agent constrains the model to a JSON schema and
   re-validates every field. Smaller local models are measurably worse at
   staying inside a schema; each violation falls through to the rule engine, so
   an unreliable model produces a system that looks like it has no AI at all.
3. **Time.** The internal group deadline is 20 September. An 8 GB download plus
   tuning a local model's output was not the best use of the remaining days.

## Decision

**Google AI (Gemini) is the sole LLM provider.** Ollama is not implemented.

```jsonc
"GoogleAi": {
  "ApiKey": "…",                        // GoogleAi__ApiKey in deployment
  "Model":  "gemini-3-flash-preview"
}
```

The agents depend on `ILlmClient`, never on a concrete provider, so this is one
class (`Services/Llm/GoogleAiClient.cs`) behind an interface. A second provider
could be added later without touching any agent code — that property is retained
even though only one implementation ships today.

Gemini's free tier is used. It is free at the point of use, which satisfies "no
cost" in the budgetary sense, but it is **not** self-hosted and it is not what
the proposal described. That gap is the substance of this decision and is
recorded here rather than left implicit.

## The deterministic floor

The deviation is survivable because the agent never depends on a model being
available. `SeverityRules` is a transparent, rule-based scorer over disaster
type, exposed population, clustering of nearby reports, and 48-hour rainfall. It
runs whenever the model is unconfigured, unreachable, rate limited, times out,
or returns something that fails validation.

When that happens the run is recorded as `SucceededWithFallback`, the model is
recorded as `rule-engine`, and the reason is stored on the run. Observed
behaviour with no API key configured:

```
status       : SucceededWithFallback
model        : rule-engine
error        : No language model configured — deterministic rules applied.
approved     : False
proposed     : High 64/100
```

This satisfies the module's requirement that a workflow ends in either an
auditable success or a **safe, clearly recorded failure**. It is never a silent
one, and an incident is never left unscored.

## Human approval is unchanged by this decision

Whichever way the reasoning step resolves, the agent only ever *proposes*. The
proposed severity is written to `AiSeverity` / `AiSeverityScore` /
`AiConfidence` / `AiRationale`, which are separate columns from the `Severity`
in force. The severity in force changes only through the coordinator-only
endpoints, and a substituted value is recorded in `SeverityOverriddenBy`. Note
`approved: False` in the trace above: the proposal existed and was audited, and
nothing had been applied.

## Consequences

**Positive**

- A capable multimodal model with reliable schema adherence, so the photo
  evidence the Flutter app collects is actually used.
- Nothing to install: any group member can run the full system with a key in
  their own gitignored `appsettings.Development.json`.
- Fast enough that analysis completes in seconds, though it runs on a background
  queue regardless so no user waits on it.

**Negative — and these are real**

- **It contradicts the proposal.** The stack table says Ollama, the risk table
  says Ollama; the code says Gemini. Anyone reading both will notice, and the
  honest answer is this document rather than a claim that they agree.
- **An external dependency on the critical path of the AI demonstration.** If
  the free tier rate-limits during assessment, every run falls back to the rule
  engine and the agentic contribution cannot be shown live. `GoogleAiClient`
  retries 429/5xx with exponential backoff to reduce this, but cannot remove it.
- **Incident photographs leave the machine.** They may show identifiable people
  in distress. For a live deployment this would need a retention policy and a
  citizen-facing notice; for an assessed prototype it is accepted.
- **An API key is now a required secret.** It lives in
  `appsettings.Development.json`, which is gitignored, and is supplied as
  `GoogleAi__ApiKey` in deployment.

## Alternatives considered

| Option | Rejected because |
| --- | --- |
| Ollama, per the proposal | ~8 GB against ~20 GB of free disk; weaker schema adherence means frequent silent fallback to the rule engine; setup time against a near deadline. |
| Both, selected by configuration | Implemented and working, then removed on the owner's decision to keep one provider. It cost two classes and left the ADR describing code that no longer existed. |
| A smaller local vision model (`llava`, `moondream`) | Cheaper on disk, but the weakest link is schema adherence, and these are worse at it than `llama3.2-vision`, not better. |
| Rule engine only, no model | The module requires a genuine agentic contribution; a scoring formula is not one, and it cannot read photographs. |

## Risk to manage before assessment

Because there is no local fallback provider, a rate limit during the viva
degrades every run to the rule engine. Mitigation: run at least one analysis
shortly before the session so a real model run is already recorded and visible
in the agent history, and be ready to explain the fallback as designed
behaviour rather than a fault.

## Related

- `Services/Llm/ILlmClient.cs` — the provider-agnostic contract
- `Services/Llm/GoogleAiClient.cs` — this decision's implementation
- `Agents/IncidentAnalysisAgent/SeverityRules.cs` — the deterministic floor
- [ADR 0002](0002-incident-photo-storage.md) — image storage, decided separately
