# ADR-004: Raw and normalized event retention

- **Status:** Accepted
- **Decision:** Persist canonical normalized trade and quote events for this checkpoint; do not retain vendor-specific raw payloads yet.

## Context

Only a local fixture source exists and it already represents canonical trade/quote fields. Retaining provider-specific payloads would create an unnecessary vendor schema and privacy/licensing commitment before a provider is selected.

## Decision

Persist provider-independent events with provider name, symbol, explicit session identity, event time, receive time, sequence identity, and trade/quote values. A fixture or provider supplies session identity; the scanner does not infer a US market session from a UTC date. Keep the source fixture as a version-controlled replay input. Do not persist source DTOs or raw vendor messages until a provider's license, replay/debug needs, and payload-retention terms have been reviewed. Treat event identity as idempotent where a sequence identity exists.

## Consequences

- Analysis can be replayed from fixtures and canonical event rows.
- Event and analysis persistence is scoped by symbol and explicit session identity.
- Exact reconstruction of a future vendor payload is not possible until raw retention is approved.
- Retention duration and privacy/licensing rules must be decided before live-provider onboarding.
