# ADR-013: Versioned hybrid MarketSituation evidence

- **Status:** Accepted
- **Decision:** Separate the complete `market-situation-v1` analytical candidate universe from its compact immutable selected-result assessment, then persist the two contracts through normalized immutable SQL evidence. Correction Batches 1-2 establish the domain/application and persistence boundaries; API/UI compact projections and host operational hardening remain later batches.

## Context

Bounded live validation of complete historical sessions exposed a scalability failure in the current MarketSituation persistence shape. The evaluator considers every eligible level, but `MarketSituationAssessment.EligibleLevels` embeds that complete universe, contributes it to assessment identity, and is serialized into immutable SQL `AssessmentJson`. A representative MSFT assessment was approximately 299.7 MB and SQL persistence timed out.

The analytical result was valid before persistence failed. A persistence failure must not relabel complete `PriorSessionEnhanced` analysis as `CurrentSessionOnly`, replace the selected level, truncate candidates, or weaken completeness.

## Decision

- `MarketSituationCandidateSet` owns the complete canonical candidate universe and the feature/context/configuration identities that establish it. It is analytical evidence, not a setup, plan, prediction, or execution instruction.
- The candidate-set ordering preserves the existing `market-situation-v1` role, price, source-precedence, and LevelId ordering. A final canonical serialized-level tie-break makes the remaining equal-key order independent of collection, dictionary, persistence, or provider enumeration order.
- `CandidateSetId` is a full 64-hex-character SHA-256 commitment to all candidate records and material scope identities. It excludes wall-clock, retrieval, evaluation, persistence, UI, and database timing. Reordering the same canonical universe does not change it; any material addition, removal, price, role, or provenance change does.
- `CompactMarketSituationAssessment` references `CandidateSetId` and retains the selected support/resistance levels, interaction geometry, primary evidence, bounded selection summaries, exact feature/context/configuration identities, quality/completeness evidence, candidate counts, and deterministic assessment identity.
- Compact interactions retain `LevelId` plus geometry and reuse direct selected-level provenance; they do not duplicate the complete `ImportantPriceLevel` in each interaction or embed losing candidates.
- The existing `MarketSituationAssessment` with `EligibleLevels` remains the legacy compatibility projection. Existing v1 rows are read as the legacy persistence/read-model shape, and production API/Blazor and MarketReaction still receive the compatible projection.
- `market-situation-persistence-v2` identifies the compact normalized envelope. `20261127170000_AddNormalizedMarketSituationPersistence` adds one immutable candidate-set header, normalized source provenance, ordered scalar member rows, and nullable v2 discriminator/reference/digest fields to `market_situation_assessment`. Legacy rows remain null-discriminator rows and are not backfilled or rewritten.
- Candidate evidence is stored once and referenced by compact assessments. `CandidateSetId` uses a binary-collated 64-character SHA-256 key; source identity is likewise a full digest. Immutable collision checks compare header digests, counts, source material, and canonical member content before accepting replay.
- Candidate members are inserted as bounded `SqlBulkCopy` batches (default 1,000, infrastructure-configured), and header/source/member/completion/assessment operations commit in one SQL transaction. A 16 KiB compact assessment is the largest single v2 analytical payload parameter. Full candidate evidence never becomes one JSON or individual insert/round trip.
- A candidate set is marked complete only after exact member-count verification. A compact assessment references the completed set in the same transaction; cancellation or failure rolls back the whole write, preventing a referable incomplete set and making retry safe.
- Candidate evidence is not silently truncated. Batch 1 compact serializations were 3,992 bytes for a simultaneous-interaction case and 2,600 bytes for 100,005 analytical candidates. Batch 2 persisted 100,005 normalized members in 101 batches; the compact SQL JSON was 4,065 bytes. The persisted compact safeguard is enforced at 16 KiB. Normal compact API/read models and forensic pages remain Batch 3 targets and are not implemented.
- Persistence health is separate from analytical completeness. Successful analysis may produce a valid compact result before a persistence adapter fails, and that later failure cannot rewrite selected levels or `ContextMode`.
- The analytical definition remains `market-situation-v1`; `market-reaction-v1` remains unchanged and can project domain-equivalent pinned level/zone evidence. No scanner configuration version bump is required.
- Raw historical trades remain unpersisted. A.2.6 remains paused while corrective Batches 3-5 proceed; `multi-session-structure-v1` and its pending Batch 2 direction are not changed.

## Consequences

- Batch 1 proves deterministic identity, analytical equivalence, reaction compatibility, legacy readability, bounded compact output, and collision rejection. Batch 2 proves clean/existing migration, idempotent replay, normalized large-set persistence, rollback/ retry safety, and immutable conflict detection.
- Compact assessment identity changes from the legacy v1 identity because the full candidate array is replaced by its full SHA-256 commitment. Legacy rows retain their existing shape and identity; future v2 writes must be distinguishable by the versioned envelope and must never reuse a legacy row as if it were v2.
- Production persistence now writes compact v2 evidence, while current API/Blazor still receive the explicit legacy projection. This resolves the giant MarketSituation payload path; it does not change API read models or launcher/host operational behavior.
- Correction Batch 3 is next/authorized for current-state/API/Blazor compact projections and bounded forensic reads. Host/launcher consequences belong to Batch 4; sustained/bounded validation closeout belongs to Batch 5.

See [ADR-009](./ADR-009-market-situation-level-interaction.md), [ADR-010](./ADR-010-market-reaction-confirmation.md), [ADR-012](./ADR-012-structural-significance-and-multi-session-trend.md), [PROJECT_STATUS.md](../../PROJECT_STATUS.md), and [PRODUCT_ROADMAP.md](../../PRODUCT_ROADMAP.md#a24a-marketsituation).
