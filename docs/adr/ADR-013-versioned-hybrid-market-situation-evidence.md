# ADR-013: Versioned hybrid MarketSituation evidence

- **Status:** Accepted
- **Decision:** Separate the complete `market-situation-v1` analytical candidate universe from its compact immutable selected-result assessment. Correction Batch 1 establishes the domain/application contract boundary only; normalized SQL persistence is Correction Batch 2 and has not started.

## Context

Bounded live validation of complete historical sessions exposed a scalability failure in the current MarketSituation persistence shape. The evaluator considers every eligible level, but `MarketSituationAssessment.EligibleLevels` embeds that complete universe, contributes it to assessment identity, and is serialized into immutable SQL `AssessmentJson`. A representative MSFT assessment was approximately 299.7 MB and SQL persistence timed out.

The analytical result was valid before persistence failed. A persistence failure must not relabel complete `PriorSessionEnhanced` analysis as `CurrentSessionOnly`, replace the selected level, truncate candidates, or weaken completeness.

## Decision

- `MarketSituationCandidateSet` owns the complete canonical candidate universe and the feature/context/configuration identities that establish it. It is analytical evidence, not a setup, plan, prediction, or execution instruction.
- The candidate-set ordering preserves the existing `market-situation-v1` role, price, source-precedence, and LevelId ordering. A final canonical serialized-level tie-break makes the remaining equal-key order independent of collection, dictionary, persistence, or provider enumeration order.
- `CandidateSetId` is a full 64-hex-character SHA-256 commitment to all candidate records and material scope identities. It excludes wall-clock, retrieval, evaluation, persistence, UI, and database timing. Reordering the same canonical universe does not change it; any material addition, removal, price, role, or provenance change does.
- `CompactMarketSituationAssessment` references `CandidateSetId` and retains the selected support/resistance levels, interaction geometry, primary evidence, bounded selection summaries, exact feature/context/configuration identities, quality/completeness evidence, candidate counts, and deterministic assessment identity.
- Compact interactions retain `LevelId` plus geometry and reuse direct selected-level provenance; they do not duplicate the complete `ImportantPriceLevel` in each interaction or embed losing candidates.
- The existing `MarketSituationAssessment` with `EligibleLevels` remains the legacy compatibility projection and is still read as the existing persistence/read-model v1 shape. Production SQL, API, and Blazor wiring remain on that shape in this batch.
- `market-situation-persistence-v2` identifies the compact envelope and its future normalized candidate-evidence persistence/read model. No backfill, schema, migration, table, column, or store change is made by this decision.
- Candidate evidence should eventually be stored once and referenced by compact assessments. Immutable collision checks must compare the full canonical candidate content and reject conflicting reuse of a candidate-set identity.
- Candidate evidence is not silently truncated. Measured Batch 1 compact serializations were 3,992 bytes for a simultaneous-interaction case and 2,600 bytes for 100,005 candidates. Initial pending-owner operational budgets are 16 KiB for compact persisted assessments, 32 KiB for normal compact API/read models, and 64 KiB per forensic candidate-evidence page. These are engineering safeguards, not analytical thresholds or scanner configuration values; overflow must reject or page explicitly.
- Persistence health is separate from analytical completeness. Successful analysis may produce a valid compact result before a persistence adapter fails, and that later failure cannot rewrite selected levels or `ContextMode`.
- The analytical definition remains `market-situation-v1`; `market-reaction-v1` remains unchanged and can project domain-equivalent pinned level/zone evidence. No scanner configuration version bump is required.
- Raw historical trades remain unpersisted. A.2.6 remains paused while Correction Batches 2-4 complete; `multi-session-structure-v1` and its pending Batch 2 direction are not changed.

## Consequences

- Batch 1 can prove deterministic identity, analytical equivalence, reaction compatibility, legacy readability, bounded compact output, and collision rejection without SQL.
- Compact assessment identity changes from the legacy v1 identity because the full candidate array is replaced by its full SHA-256 commitment. Legacy rows retain their existing shape and identity; future v2 writes must be distinguishable by the versioned envelope and must never reuse a legacy row as if it were v2.
- Current production persistence still uses the legacy giant shape until Correction Batch 2. This ADR does not claim the live persistence incident is fixed.
- Correction Batch 2 is next and authorized for normalized SQL persistence and migration. API/Blazor changes belong to Batch 3; operational host/launcher consequences belong to Batch 4.

See [ADR-009](./ADR-009-market-situation-level-interaction.md), [ADR-010](./ADR-010-market-reaction-confirmation.md), [ADR-012](./ADR-012-structural-significance-and-multi-session-trend.md), [PROJECT_STATUS.md](../../PROJECT_STATUS.md), and [PRODUCT_ROADMAP.md](../../PRODUCT_ROADMAP.md#a24a-marketsituation).
