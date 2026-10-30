# Archived Project Status and Validation Record (2026-10-27)

## Final same-page browser observation (2026-10-30)

**Browser criterion: PASS. Milestone A.1 - Live Scanner Validation: COMPLETE.** This closes only the outstanding browser-observability criterion; the prior ingestion/SQL/shutdown closeout below remains separate evidence.

Session `alpaca-iex-20261030T174047331Z`; existing integrated-browser page `d23e0d2a-5bd7-4986-9baf-19904dec8a5a`, URL `http://localhost:5291/`. Both timestamped, read-only DOM snapshots show Alpaca / iex / Limited, Live health, seven symbol/session results, one session, and symbols AAPL, AMZN, GOOGL, MSFT, NOK, SPY, TSLA. No refresh, navigation, extra tab, host/provider start, database write, or analytical/configuration change was performed.

Observation 1: **2026-10-30T17:49:59.124Z (13:49:59.124 EDT)**. Observation 2: **2026-10-30T17:51:04.190Z (13:51:04.190 EDT)**; elapsed **65.066 seconds**. Displayed times below are October 30 EDT, at the dashboard's displayed second precision. Arrows mean observation 1 -> observation 2.

| Dashboard field | Before | After |
| --- | --- | --- |
| Last market event / latest event time | 13:49:53 | 13:50:54 |
| Last receive time | 13:49:52 | 13:50:54 |
| Unique canonical events | 70,611 | 78,317 |
| Feed health | Live | Live |
| Visible reconnect/failure UI | None | None |

| Symbol | Reference price | Classification / publication | Bias | VWAP | Spread (bps) | Quality / 100 | Vetoes |
| --- | --- | --- | --- | --- | --- | --- | --- |
| AAPL | 345.48 -> 345.32 | Avoid / No Trade, NotQualified -> Probable Short, Active | Neutral -> Short | 345.82 -> 345.81 | 13.6 -> 7.5 | 15 -> 100 | NoDefensibleInvalidation -> none |
| AMZN | 239.45 -> 239.61 | Avoid / No Trade, NotQualified -> Probable Long, Active | Long (unchanged) | 239.33 (unchanged) | 55.3 -> 4.6 | 100 (unchanged) | ExcessiveSpread -> none |
| GOOGL | 279.13 -> 278.89 | Avoid / No Trade, NotQualified -> Probable Short, Active | Neutral -> Short | 279.03 (unchanged) | 10.4 -> 3.2 | 15 -> 100 | NoDefensibleInvalidation -> none |
| MSFT | 521.93 -> 521.35 | Avoid / No Trade, NotQualified (unchanged) | Neutral -> Short | 521.58 -> 521.59 | 50.1 -> 34.8 | 15 -> 100 | ExcessiveSpread + NoDefensibleInvalidation -> ExcessiveSpread + InsufficientDollarVolume |
| NOK | 8.50 -> 8.48 | Avoid / No Trade, NotQualified (unchanged) | Short (unchanged) | 8.53 (unchanged) | 11.8 (unchanged) | 100 (unchanged) | InsufficientDollarVolume (unchanged) |
| SPY | 754.68 -> 754.26 | Avoid / No Trade, NotQualified (unchanged) | Neutral -> Short | 754.67 -> 754.55 | 0.3 -> 0.4 | 15 -> 100 | InsufficientRequiredFeatureData + NoDefensibleInvalidation -> InsufficientRequiredFeatureData |
| TSLA | 397.63 -> 396.76 | Avoid / No Trade, NotQualified (unchanged) | Neutral -> Short | 398.24 -> 398.11 | 80.8 -> 123.9 | 15 -> 100 | ExcessiveSpread + NoDefensibleInvalidation -> ExcessiveSpread |

The publication explanation changed for AAPL, AMZN, and GOOGL from "The setup detector classified this candidate as Avoid / No Trade." to "Market data is healthy and the candidate remains within its validity period." Other publication explanations were unchanged. Quality scores are not probabilities of profit.

| Symbol | Analysis as-of | Expiry | Feature snapshot ID | Quote age (s) |
| --- | --- | --- | --- | --- |
| AAPL | 13:49:48 -> 13:50:54 | 13:51:48 -> 13:52:54 | 4082341dc4dc -> 1d100636b3b3 | 0.0 (unchanged) |
| AMZN | 13:49:48 -> 13:50:53 | 13:51:48 -> 13:52:53 | c9721d5b4395 -> 242343754b18 | 0.8 -> 0.0 |
| GOOGL | 13:49:47 -> 13:50:54 | 13:51:47 -> 13:52:54 | 288e8938e469 -> c590c072960e | 0.0 -> 3.0 |
| MSFT | 13:49:47 -> 13:50:53 | 13:51:47 -> 13:52:53 | f8d2cf4cd0f1 -> 336c5731c4fd | 0.0 (unchanged) |
| NOK | 13:49:48 -> 13:50:54 | 13:51:48 -> 13:52:54 | 3cffb8e6b258 -> fe2ce5528683 | 0.0 (unchanged) |
| SPY | 13:49:48 -> 13:50:54 | 13:51:48 -> 13:52:54 | 790db540eaa2 -> 9962379d9e1a | 0.0 (unchanged) |
| TSLA | 13:49:47 -> 13:50:54 | 13:51:47 -> 13:52:54 | 3981e2bf14a3 -> 9836440bebdb | 0.0 (unchanged) |

Additional captured feature changes (percent values are displayed percentages; dollar volume is USD):

| Symbol | Momentum / secondary horizon | Window volume / dollar volume / session cumulative | Trend / VWAP slope | EMA fast / medium | Price structure / ADX | Range position / realized volatility |
| --- | --- | --- | --- | --- | --- | --- |
| AAPL | 0.04 / 0.03 -> -0.06 / -0.03 | 2,084 / 719,823 / 22,246 -> 1,294 / 447,094 / 23,540 | Conflicting / Falling (unchanged) | 345.48 / 345.45 -> 345.33 / 345.39 | HigherHighHigherLow / 29.13 -> Mixed / 44.06 | 16.76 / 0.18 -> 3.21 / 0.19 |
| AMZN | 0.14 / 0.04 -> 0.05 / 0.06 | 4,365 / 1,044,807 / 35,604 -> 1,575 / 377,205 / 37,282 | Conflicting / Rising -> Bullish / Rising | 239.45 / 239.46 -> 239.58 / 239.53 | Mixed / 26.03 -> Flat / 21.54 | 63.33 / 0.26 -> 85.33 / 0.26 |
| GOOGL | 0.00 / 0.00 -> -0.07 / -0.04 | 1,590 / 443,848 / 25,392 -> 1,184 / 330,349 / 26,644 | Conflicting / Rising -> Bearish / Falling | 279.14 / 279.16 -> 278.89 / 278.94 | LowerHighLowerLow / 15.86 -> LowerHighLowerLow / 41.12 | 64.86 / 0.15 -> 21.62 / 0.16 |
| MSFT | -0.01 / 0.00 -> -0.11 / -0.02 | 2,041 / 1,065,129 / 22,913 -> 155 / 80,883 / 23,071 | Conflicting / Rising (unchanged) | 521.92 / 521.91 -> 521.66 / 521.85 | Mixed / 15.22 -> Mixed / 22.15 | 70.97 / 0.22 -> 46.02 / 0.24 |
| NOK | -0.06 / 0.00 -> -0.24 / -0.24 | 7,405 / 62,975 / 131,542 -> 4,489 / 38,155 / 136,031 | Conflicting / Falling (unchanged) | 8.50 / 8.50 -> 8.49 / 8.50 | Mixed / 29.14 -> Mixed / 44.20 | 10.00 / 0.36 -> 0.00 / 0.40 |
| SPY | 0.01 / 0.00 -> -0.06 / -0.03 | 1,107 / 835,403 / 9,354 -> 3,419 / 2,579,662 / 4,883 | Conflicting / Falling -> Bearish / Falling | 754.71 / 754.70 -> 754.26 / 754.27 | HigherHighHigherLow / 23.61 -> LowerHighLowerLow / 50.00 | 70.59 / 0.02 -> 11.32 / 0.03 |
| TSLA | -0.04 / -0.03 -> -0.22 / -0.13 | 810 / 322,202 / 11,377 -> 1,714 / 680,918 / 13,193 | Conflicting / Falling (unchanged) | 397.68 / 397.75 -> 396.80 / 396.99 | HigherHighHigherLow / 19.48 -> Mixed / 56.61 | 2.59 / 0.20 -> 0.00 / 0.22 |

Freshness/data quality remained Fresh/Good with complete-session True except SPY, which remained Degraded/Degraded with complete-session False. Dollar-volume and ADX status remained Available; feature version remained `features-v14`. SPY's displayed cumulative volume is bounded retained-state output, not proof of a counter reset or browser reload.

**Continuity:** same page load YES; browser refresh during observation NO; live values changed YES; circuit remained connected YES; reconnect/failure UI before intentional shutdown NO during the measured interval. Both snapshots have `performance.timeOrigin = 1793382094039.4`; the navigation entry remains `type=reload, startTime=0`, identifying the already-loaded page at approximately 17:41:34.039Z, before observation, not a refresh during the check. Across 63 intervening DOM samples, health was only Live, no reconnect/error element became visible, and there were zero captured console warnings/errors, page exceptions, or main-frame navigations. Hidden reconnect-template text was not counted as visible failure UI.

**Shutdown boundary:** the existing run was configured for **1,800 seconds**, not 600. It was allowed to finish normally; no Ctrl+C or external stop was issued. An additional 60 browser samples from 17:51:15.466Z-17:52:16.965Z showed only Live with no reconnect UI, console errors, exceptions, or navigation. Extended monitoring from **17:51:45.457Z to 18:12:03.617Z** retained **1,182 samples** with unchanged page time origin and zero navigation events. Before intentional shutdown it showed only Live, no reconnect/failure UI, and zero console warnings/errors or page exceptions.

The session diagnostics record **DurationElapsed at 2026-10-30T18:11:17.1925747Z (14:11:17.1925747 EDT)**. Only afterward did the browser record WebSocket disconnect code 1006 (console timestamp **18:11:17.253Z**, captured 18:11:17.258Z), visible "Rejoining the server..." (**18:11:17.938Z**), negotiation/fetch failures (**18:11:21.985Z**), and retry-countdown UI (**18:11:32.173Z**). Through 18:12:03.617Z, no terminal visible "Failed to rejoin" state appeared; rejoin retries continued against the stopped host. The last-rendered Live label after shutdown was frozen page content, not evidence of an active connection. All these disconnect/retry symptoms are **post-shutdown**, not an A.1 browser defect.

The matching launcher result records **exit code 0 at 18:11:27.3556604Z**; final diagnostics show drain **Completed**, remaining events **0**, and the sanitized app log has **0 Error/Critical entries**. Port 5291 has no remaining listener. Artifacts remain under `artifacts/alpaca-validation/alpaca-iex-20261030T174047331Z/`. No new SQL reconciliation or implementation validation was performed for this browser-only check.

## Consolidated A.1 validation record (2026-10-29)

This section preserves durable evidence from the live IEX, forensic, instrumentation, and shutdown work. At this checkpoint, the latest 30-minute closeout supplied the missing logs, shutdown trigger, and exit code; the remaining browser evidence gap was subsequently closed by the [2026-10-30 same-page observation](#final-same-page-browser-observation-2026-10-30). The current checkpoint is in [PROJECT_STATUS.md](./PROJECT_STATUS.md), and the operator procedure is in [README.md](./README.md#one-final-instrumented-a1-observation).

### Self-describing 30-minute IEX closeout (2026-10-29)

Session `alpaca-iex-20261029T190952800Z` ran Alpaca IEX / Limited in Development on port 5291 against `MarketOpportunityEngine_LiveValidationTests` (the launcher's guarded database; exact-session SQL rows found there) for `AAPL, MSFT, SPY, NOK, GOOGL, AMZN, TSLA`. Its diagnostic, clock, launcher-result, and sanitized app-log artifacts all carry this SessionId. The process ran from `19:09:58.618Z` to `19:40:12.937Z` (1,814.3 s); configured duration was 1,800 s. The app log and diagnostic both record `trigger=DurationElapsed` at `19:40:02.371Z`; the launcher captured exit code **0**. The separate 81-second session `alpaca-iex-20261029T190804148Z` was not merged.

One connection (`1d6138ad-...`) authenticated, subscribed all seven symbols for trades and quotes, stayed Live with no reconnect, and ended `Disconnected / CancellationRequested` at shutdown. It received **87,003** messages (**87,000** market-data), producing **209,616** canonical events (**14,334 trades / 195,282 quotes**) with 0 parser failures. Accepted = dequeued = analyzed = persistence-acknowledged = **209,616**; duplicates, processing/persistence failures, cancelled writes, channel depth, and remaining work were 0; drain **Completed** about 10.3 s after the request (not separately timed; 20 s budget). Sampled high-water was 1,642 with 0 asynchronous write waits. Unresolved-gap state and malformed/unsupported-message counts are not instrumented.

Exact-session read-only SQL matched exactly: 209,616 rows and distinct identities, 14,334 trades, 195,282 quotes, **2,433** snapshots and evaluations, 12,320 evidence rows, and 0 zero-ask rows. All evaluations had Alpaca / iex / Limited / realtime provenance with `features-v14`, `momentum-continuation-v1`, and `scanner-config-v15`. Event time spanned `19:10:03.003Z`-`19:40:02.589Z`; receive time spanned `19:10:02.825Z`-`19:40:02.419Z`.

| Symbol | Events | Snapshots / evaluations | Avoid / Probable long / Probable short / Watch |
|---|---:|---:|---:|
| AAPL | 23,465 | 350 | 347 / 0 / 3 / 0 |
| AMZN | 36,811 | 350 | 324 / 7 / 16 / 3 |
| GOOGL | 15,732 | 350 | 253 / 29 / 40 / 28 |
| MSFT | 2,651 | 336 | 284 / 13 / 31 / 8 |
| NOK | 7,941 | 348 | 231 / 33 / 47 / 37 |
| SPY | 111,469 | 350 | 350 / 0 / 0 / 0 |
| TSLA | 11,547 | 349 | 325 / 6 / 8 / 10 |
| **Total** | **209,616** | **2,433** | **2,114 / 88 / 145 / 86** |

All 2,114 Avoid evaluations, and only those, carried at least one veto (86.9%). Veto evidence counts: NoDefensibleInvalidation 1,636, InsufficientRequiredFeatureData 935, ExcessiveSpread 565, InsufficientDollarVolume 156, DollarVolumeUnavailable 26, VolatilityUnavailable 19, InvalidQuoteOrPrice 15, StaleQuote 10. Quality scores ranged 0-100 (mean 39.97, median 15). Classifications and scores are not calibrated probabilities or profit expectations.

| Stage | Samples | Min / max ms | Median / p95 bucket ms | >1 s | >5 s | >10 s | >15 s |
|---|---:|---:|---:|---:|---:|---:|---:|
| Provider -> message | 209,616 | -195.7 / 646.8 | [-195.7,-100] / [-195.7,-100] | 0 | 0 | 0 | 0 |
| Message -> canonical | 209,616 | 0.004 / 333.0 | [0.1,0.25] / [2,5] | 0 | 0 | 0 | 0 |
| Canonical -> analysis | 209,616 | 0.1 / 20,167.1 | [2k,5k] / [5k,10k] | 189,460 (90.4%) | 42,288 (20.2%) | 1,992 (0.95%) | 128 (0.06%) |
| Analysis -> persistence | 2,433 | 48.7 / 5,910.7 | [200,500] / [1k,2k] | 195 (8.0%) | 1 (0.04%) | 0 | 0 |
| Persistence call | 2,433 | 48.3 / 5,897.5 | [200,500] / [1k,2k] | 187 (7.7%) | 1 (0.04%) | 0 | 0 |
| Channel-write wait | 0 | n/a | n/a | 0 | 0 | 0 | 0 |

Latency is primarily after message receipt, in canonical-to-analysis waiting and secondarily persistence; no write blocked. Aggregate histograms prevent per-symbol burst correlation. The app log recorded 22 persistence anomalies >1 s (largest logged 2.24 s), all on the single connection without reconnects. The clock snapshot again showed `Leap Indicator 3 (not synchronized)`, last sync error 1, phase offset 1.586 s, root dispersion 9.356 s, and an unreconciled last-sync timestamp; it gives no authoritative UTC bound, so the negative provider-to-message lag (median bucket below -100 ms) remains unexplained.

There were 0 zero-ask quotes. All 38 metadata groups were complete with no unattributed events or dropped transitions. Quotes had bid/ask exchange `V`; tape `C` except NOK `A` and SPY `B`; conditions `R`, plus `?` on AAPL, AMZN, GOOGL, MSFT, and NOK. The app log has no Error/Critical entries; Warning entries were four EF decimal-precision model warnings and one HTTPS-port warning.

The agent browser retained the final dashboard DOM for this SessionId: status Live, `Provider: Alpaca · Feed: iex`, Limited coverage, all seven symbols, and last render `15:39:53 EDT` (`19:39:53Z`). Retained console errors (`Failed to complete negotiation`, then `Failed to rejoin`) begin at `19:40:04Z`, after the duration-triggered shutdown. This proves the dashboard loaded and displayed correct provenance, but one final snapshot cannot prove live updates without refresh or continuous circuit health. No resource measurements were retained.

### Completed instrumented IEX observation (2026-10-29)

Session `alpaca-iex-20261029T171641938Z` ran Alpaca IEX / Limited for the requested universe `AAPL, MSFT, SPY, NOK, GOOGL, AMZN, TSLA`; all seven symbols were confirmed for both trades and quotes. The diagnostic runtime was **601.892 seconds**, from `13:17:10.086 EDT` to its final capture at `13:27:11.978 EDT`. The current launcher defaults to 600 seconds, port 5291, Development, and `MarketOpportunityEngine_LiveValidationTests`; the exact invocation was not retained, so those launch settings are inferred from the source/defaults and observed runtime. The diagnostic JSON embeds this SessionId; the colocated clock filename matches it, although the clock JSON itself has no SessionId field. The clock file was captured at `17:17:03.940Z`; the diagnostic was first created at `17:17:15.273Z`, captured its final snapshot at `17:27:11.978Z`, and was last written at `17:27:11.995Z`.

The export records one connection (`003537b4-ad00-4f13-88fc-8a2195b7a3f2`), one successful socket, authentication and subscription acknowledgements, then Live; it ended as `Disconnected / CancellationRequested` at `13:27:10.312 EDT`, with no reconnect. There were **23,722** complete WebSocket messages, **23,719** market-data messages, **57,758** canonical events (**2,511 trades / 55,247 quotes**), and no parser failures. No separate malformed/unsupported-message count is exposed. All diagnostic metadata groups were complete (30 groups; no unattributed metadata events or dropped transitions). The export does not record a final data-gap state.

Exact-session, read-only SQL verification against `MarketOpportunityEngine_LiveValidationTests` found **57,758** canonical rows and distinct identities, **766** feature snapshots, **766** Momentum Continuation evaluations, and **3,458** evidence rows. Provenance was Alpaca / iex / Limited / realtime, with trade and quote support. Event time ranged from `17:17:10.8306486Z` to `17:27:10.2851874Z`; receive time ranged from `17:17:10.7518025Z` to `17:27:10.1749248Z`.

| Symbol | Events | Snapshots / evaluations | Avoid / Long / Short / Watch |
|---|---:|---:|---:|
| AAPL | 9,998 | 114 | 107 / 6 / 1 / 0 |
| AMZN | 6,589 | 114 | 83 / 26 / 1 / 4 |
| GOOGL | 3,468 | 113 | 66 / 12 / 30 / 5 |
| MSFT | 338 | 86 | 80 / 6 / 0 / 0 |
| NOK | 2,317 | 112 | 77 / 26 / 8 / 1 |
| SPY | 32,116 | 115 | 115 / 0 / 0 / 0 |
| TSLA | 2,932 | 112 | 106 / 5 / 0 / 1 |
| **Total** | **57,758** | **766** | **634 / 81 / 40 / 11** |

Pipeline accounting reconciled: accepted = dequeued = analyzed = persistence-acknowledged = **57,758**; duplicates, processing failures, persistence failures, cancelled writes, remaining work, and final channel depth were all **0**. Drain status was **Completed**; sampled channel high-water was **1,320**, with **0** asynchronous write waits and longest wait **0 ms**. Persistence acknowledgement is not an INSERT count; SQL independently verified the persisted rows and identities.

| Timing stage (ms) | Samples | Min / max | Median bucket | p95 bucket |
|---|---:|---:|---:|---:|
| Provider event -> complete message receipt | 57,758 | -115.549 / 102.519 | [-115.549, -100] | [-100, -50] |
| Message receipt -> canonical creation | 57,758 | 0.004 / 33.724 | [0.1, 0.25] | [2, 5] |
| Canonical creation -> analysis start | 57,758 | 0.372 / 19,069.470 | [2,000, 5,000] | [5,000, 10,000] |
| Analysis start -> persistence completion | 766 | 96.433 / 10,644.304 | [200, 500] | [1,000, 2,000] |
| Persistence-call duration | 766 | 95.901 / 10,641.400 | [200, 500] | [1,000, 2,000] |
| Asynchronous channel-write wait | 0 | not sampled | n/a | n/a |

Percentiles are histogram intervals, not exact values. Counts above the 1/5/10/15-second thresholds were not exported for this run and cannot be reconstructed from its aggregate summary; later runs export them per stage. The largest observed delays were after message receipt, principally between creation and analysis and during persistence. Histograms are aggregate-only, so the bursts cannot be correlated to individual symbols or event types. There were no measured blocked writes despite the sampled queue high-water.

The host-clock snapshot reported source `time.windows.com,0x9`, last successful sync `10/29/2026 9:16:07 AM` (timezone not stated), phase offset `1.5863064 s`, root delay `0.0339603 s`, root dispersion `9.3564537 s`, `Leap Indicator 3 (not synchronized)`, stratum 0, last sync error 1, and 7,952.066 seconds since last good sync. The returned last-sync time and elapsed-since-sync fields do not reconcile with the capture time under either UTC or EDT interpretation. No authoritative contemporaneous UTC error bound exists; the clock evidence therefore does not establish the cause of the negative event-to-message lags.

There were **0 zero-ask quotes**, so there are no affected symbols, percentages, or zero-ask-to-positive-ask sequences. Quote metadata captured bid/ask exchange `V`; tape was `C` for AAPL, AMZN, GOOGL, MSFT, TSLA, `A` for NOK, and `B` for SPY. Quote condition `R` was recorded throughout, with `?` additionally on AMZN, GOOGL, and TSLA. This run does not establish zero-ask semantics.

`ShutdownDrainStatus=Completed` and the reconciled counters establish that accepted non-duplicate work reached analysis and successful persistence before the final diagnostic export. The configured drain budget is 20 seconds by the current default; actual drain duration is not separately timed. The observed cancellation at approximately 600 seconds is consistent with the launcher's 600-second duration timer, but the exact trigger cannot be distinguished from operator cancellation without the actual launcher arguments/logs. No session-specific sanitized launcher/application logs or browser/circuit evidence were retained in the reviewed evidence. Consequently the process exit code, fatal-log absence, and Blazor behavior are **UNKNOWN**; A.1 remains blocked on that evidence gap, not on an observed ingestion or pipeline failure. No resource-pressure measurements were captured.

### Live IEX validation

Three separate Alpaca IEX sessions on 2026-10-28 persisted **1,571,489 canonical events** in total to the dedicated `MarketOpportunityEngine_LiveValidationTests` SQL Server database:

| Session | Canonical events | Trades | Quotes | Feature snapshots / evaluations |
|---|---:|---:|---:|---:|
| `alpaca-iex-20261028T043805864Z` | 41,418 | 2,308 | 39,110 | 341 |
| `alpaca-iex-20261028T141628627Z` | 243,079 | 7,010 | 236,069 | 1,664 |
| `alpaca-iex-20261028T184942408Z` | 1,286,992 | 35,162 | 1,251,830 | 2,616 |

Persisted provenance was Alpaca / IEX / Limited / realtime, with trade and quote support, across seven symbols. The sessions also persisted feature calculations and Momentum Continuation evaluations. These are separate sessions, not one continuous runtime; database timestamp ranges do not establish process uptime. Blazor live behavior has been observed previously, but no browser/circuit evidence was retained for the completed instrumented run documented above.

### Timestamp and quote forensics

- Event time came from provider timestamps; receive time came from the application host clock. Both were represented as UTC, but historical host/provider clock alignment and an authoritative offset/error bound were unavailable.
- Negative event-to-receive lag occurred across all sessions, symbols, and event types; minimum observed lag was about **-827 ms**. Its cause is not established by UTC formatting alone.
- Large positive-lag bursts occurred across multiple symbols: **27,486** events in the second session exceeded 30 seconds; **1,895** in the third exceeded 30 seconds, including **5** over five minutes. Persisted records cannot distinguish provider/network delay from host scheduling, socket buffering, or application backpressure.
- The sessions contained **1,636** zero-ask quotes; every one also had zero ask size. There were **389** persisted evaluations with both `InvalidQuoteOrPrice` and a zero snapshot ask; **354** had zero ask as the only failed input to that veto. The conservative veto operated as implemented.
- Forensics found no conclusive provider definition for zero-sided IEX quotes and could not recover original quote conditions from the persisted canonical events. Neither anomaly establishes a correctness defect; do not compensate timestamps or relax quote rules based on this evidence.

### Instrumentation and unresolved measurement limits

Opt-in instrumentation measures complete-message receipt, per-event creation, analysis, persistence, asynchronous channel waits, connection lifecycle, and bounded provider metadata. It exports sanitized timing and accounting evidence, including clock diagnostics. Complete-message receipt is an application receive-call boundary, not a wire-arrival timestamp. The 2026-10-29 run documented above supplies these measurements and a live drain reconciliation; it does not supply browser/circuit evidence or an authoritative contemporaneous clock error bound. Historical uninstrumented sessions cannot supply those missing boundaries.

Other evidence limits include absent raw market-data payloads, unpersisted subscription/rejection details in the earlier runs, and no persisted historical publication/health-transition timeline. Provider semantics for zero asks and the source of historical lag bursts remain unresolved.

### Graceful-shutdown defect and correction

A deterministic healthy-store test reproduced accepted-work loss on normal cancellation: **2 accepted, 2 dequeued, 1 analyzed, 1 persistence-acknowledged, 0 persistence failures, channel depth 0**. Cancellation stopped the consumer before its five-second analysis interval elapsed, leaving already-dequeued pending work without a final analyze/persist flush. An empty channel therefore did not prove accepted work had drained.

The correction stops producer acceptance, completes the channel, drains queued inputs, force-analyzes pending batches, and uses a persistence token independent of host cancellation. A successful drain requires reconciled accepted/dequeued/analyzed/duplicate/persistence accounting and zero remaining work. `Operations:GracefulShutdownTimeoutSeconds` is validated to 1-300 seconds and defaults to 20 seconds; timeout or processing/persistence failure is surfaced with explicit accounting, never reported as a successful drain. This is an in-process bounded drain; no durable queue was added.

### Verification and limits

After the shutdown correction, focused shutdown/pipeline/publication/fixture-determinism tests passed (**41/41**), the full offline unit suite passed (**231/231**), hosted fixture integration passed on two runs, guarded SQL persistence/migration integration passed (**3/3**), hosted diagnostic checks passed (**2/2**), and the solution build succeeded with **0 warnings and 0 errors**. SQL tests targeted guarded dedicated test catalogs. These results validate implementation and offline/integration behavior; the completed live-session measurements are recorded above, with browser/circuit and host-log evidence still missing.

No analytical formulas, quote rules, event identity/order, scoring, classifications, analytical settings, provider timestamps/parsing/selection, channel capacity, or persistence schema changed during instrumentation or shutdown correction. The 2026-10-29 completed session provides live graceful-shutdown accounting; earlier live sessions do not.

## Milestone A.1 command and database preparation (2026-10-28 UTC)

This follow-up prepared repeatable operator commands; it did not run an Alpaca or live exchange session. No analytical/runtime source code was modified.

- Added the opt-in `sqlserver-test-init` Compose service. It creates `MarketOpportunityEngine_LiveValidationTests` only if absent, using the existing SQL Server service and data volume. The existing container was healthy before execution and was not restarted/recreated. The service ran successfully and returned that exact `DB_NAME()`; repeating it also succeeded without resetting the catalog or fixture rows. No development data was accessed or changed.
- Added `scripts/Start-AlpacaValidation.ps1` for PowerShell 7.6+ / .NET 10. Explicit `-Feed iex` reads the existing `MarketData:Massive:Symbols` User Secrets setting in memory and requests the non-empty distinct universe through Alpaca; `-Feed test` explicitly selects synthetic FAKEPACA. Credentials/unrelated secrets are not printed, the symbol list is not copied into configuration, and the fixture remains the shared default.
- Both actual no-provider preflights passed, verifying the dedicated SQL target with `SELECT DB_NAME()`. The IEX setting contains **8 entries / 7 distinct symbols**; the synthetic alternative requests one symbol. An explicitly supplied development-database connection was rejected rather than replaced. Environment restoration passed, including absent versus empty values.
- A PowerShell launch interception harness exercised both normal feed branches without starting an app/provider. It verified provider/feed/session arguments, the configured IEX universe versus FAKEPACA, `--no-launch-profile`, `--no-build`, and the dedicated database environment inherited at the run boundary. All assertions and environment-restoration checks passed. This proves command wiring only, not a received subscription acknowledgement.
- The hosted fixture regression test ran against the new dedicated catalog: **1 passed, 0 failed, 0 skipped**. It initialized the application schema and preserved deterministic fixture rows, using fixture data only. No provider connection was opened.
- The exact README SQL pipeline using `scripts/Read-AlpacaValidation.sql` returned `MarketOpportunityEngine_LiveValidationTests` and **0 canonical events / 0 FeatureSnapshots / 0 opportunity evaluations** for the deliberately unused session `a1-command-verification`. This validates the read-only reporting command, not live event persistence.
- Final sequential offline validation: full unit suite **211 passed, 0 failed, 0 skipped**; solution build **0 warnings, 0 errors**; Compose configuration and `git diff --check` passed.

Initial tooling attempts exposed a PowerShell SQL connection-string property setter issue and a missing native SNI load path. The launcher now uses SQL keyword indexers and explicitly loads the architecture-specific SNI library from the existing Web build. Initial harness restoration comparisons used the wrong baseline; the corrected harness captured values immediately before invocation and passed. These were local preparation failures before any provider connection; no database failure or exchange-data result is inferred from them.

The README now supplies setup, IEX versus synthetic launch, browser/status observation, exact-session persistence reporting, and Ctrl+C shutdown commands. Subscription acknowledgements, symbol-specific provider rejection details, raw message/parser counters, and reconnect totals remain unexposed by the application and must not be inferred. No complete-shutdown-drain guarantee was added. **Milestone A.1 remains partially verified pending an active-market IEX run.**

## Milestone A.1 market-hours preflight (2026-10-27, 23:18 Eastern)

This was a preflight only, not a live validation run. At the recorded time, the regular U.S. equity market session was closed, so no Alpaca connection was opened and no subscription or event-processing result is claimed.

Only the existing Web project User Secrets key `MarketData:Massive:Symbols` was read. It contained **8 entries and 7 distinct symbols** (one duplicate); no other User Secrets were listed or exposed. The Alpaca source deduplicates the `MarketData:Alpaca:Symbols` input, but its normal local setting selects `test` and `FAKEPACA`; the current source configuration does not automatically map the existing configured universe across keys. A live run must supply the existing key's values as a one-run Alpaca symbols override, without adding a source-controlled list or changing analytical/provider coupling.

No application was started and no SQL connection was opened. This avoided the development database configured by default in `appsettings.Development.json`; a future run must first explicitly select and verify the dedicated test database. The shared browser tab at `http://localhost:5291/` showed recorded Blazor negotiate/fetch failures at approximately **21:39-21:40 UTC**, so no current UI health or live-update result is inferred from that stale tab. No raw WebSocket counts, current feed health, or current application error totals are available.

Existing earlier evidence remains as recorded below: a different six-symbol after-hours IEX probe authenticated and subscribed but received no exchange events, and the later test-feed recovery run matched 39 processed and persisted canonical events. Neither establishes IEX exchange-event processing for the current configured universe. **Milestone A.1 remains partially verified.**

## Milestone A.1 after-hours IEX application follow-up (2026-10-27)

After the real-socket recovery check below, the actual application was launched with **Alpaca / iex**, the six requested symbols **SPY, QQQ, AAPL, MSFT, NVDA, AMD**, explicit session `alpaca-a1-iex-app-20261027`, and the guarded existing dedicated test catalog. Health was verified before sampling. The separate 90-second observation starting **21:30:35 UTC** collected **18 samples each** of `/health`, `/api/v1/system/status`, and `/api/v1/opportunities`. All endpoints returned HTTP 200. Status remained **Connecting**, with **0 processed canonical events**, no last event/receive timestamp, no gap, and `Alpaca / iex / Limited` real-time trade/quote capabilities. Opportunities remained empty. This is correct no-data behavior, not proof of Live exchange ingestion.

The initial read-only SQL helper accidentally queried the configured development catalog rather than the explicitly launched child catalog. Those results are not used as dedicated-database evidence. A subsequent explicit read-only query selected and returned `DB_NAME() = MarketOpportunityEngine_IntegrationTests`, confirming **0 events, 0 feature snapshots, 0 opportunities** for the exact IEX session. No records or schema were deleted or reset.

Browser verification showed **Provider: Alpaca, Feed: iex, Coverage: Limited, Connecting, Last market event: --**. It exposed misleading inherited empty-state text ("recorded events are being processed") despite no events. The live empty state now explicitly says no live analysis is available and it is waiting for valid market events. The fixture wording is preserved; the event summary now says "unique canonical events" for either source. No analytical logic changed.

After this presentation-only fix, the solution build again succeeded with **0 warnings/errors** and full offline units passed **211/211**. The rebuilt app was launched again on the same guarded IEX configuration; browser checks confirmed the new live empty text and absence of the recorded-processing claim. All owned hosts were stopped, the verification port was released, the exact temporary launch helper was removed, and whitespace checks passed. The separate IEX probe below remains the explicit authentication/subscription evidence; this app observation adds no received exchange events. Runtime counters for raw messages/parser errors remain unavailable. **Exchange-event-driven IEX dashboard verification is still unavailable during this after-hours observation; no synthetic substitute or forced analytical qualification is claimed.**

## Milestone A.1 real-socket recovery follow-up (2026-10-27)

This follow-up supersedes the earlier "real socket recovery unverified" boundary, without changing analytics or runtime source. An isolated verification-only CONNECT tunnel listened on `127.0.0.1:5294` and allowed only `stream.data.alpaca.markets:443`. The app's process-local proxy setting routed its real Alpaca test WebSocket through the tunnel. TLS remained end-to-end to Alpaca, with certificate validation unchanged; no credentials or provider payloads were decoded or retained. The app used the explicitly guarded existing `MarketOpportunityEngine_IntegrationTests` database and session `alpaca-a1-recovery-20261027`.

The report interval was **21:15:41.636380-21:19:13.663790 UTC**, approximately **212 seconds**, with **85 samples**. The first health samples were Connecting, then Live at **21:16:02.660145 UTC**. After roughly one minute of data, one deliberate closure of the owned TCP tunnel produced:

| UTC timestamp | Observed state | Processed unique events | Unresolved gap |
|---|---|---|---|
| 21:17:03.387754 | Offline | 14 | true |
| 21:17:06.214058 | Connecting | 14 | true |
| 21:17:12.577282 | Live | 15 | true |

The configured retry delay was **3000 ms**. The replacement tunnel was held until **21:17:09.529632 UTC**; no Live status appeared while that upstream connection was withheld. After reconnect, new valid market data restored Live while the gap remained explicit. Every nonempty publication projection sampled after the fault was **Suspended / MarketDataGapUnresolved**. The synthetic analytical decisions were all AvoidNoTrade, so this is not evidence that an Active exchange-data candidate was suspended.

The tunnel counted **2 upstream connections and 1 forced disconnect**. The app logged one sanitized **WebSocketException** disconnect/retry warning; stderr was empty. There was also the existing local HTTP/HTTPS-redirection configuration warning. Final processed unique events were **39**. Read-only SQL verified **38 Quote + 1 Trade events, 26 feature snapshots, and 26 immutable AvoidNoTrade / Alpaca / test / Synthetic opportunities**, with analysis timestamps **21:16:02.6859039-21:19:12.6859132 UTC**. This run's final processed/persisted totals matched; it does not retroactively reconcile the earlier run or guarantee shutdown tail flushing. Inspection confirms events wait in pending batches between analysis writes; forced termination can leave that tail unpersisted.

Initial empty opportunity samples had no publication to inspect; null projection entries in the verification report are not opportunities or errors. Initial readiness timed out before the actual validation and was retried with bounded readiness handling. An execution-helper summary later emitted null fields and spurious transitions; those were not used as evidence. Direct inspection of the saved JSON confirmed `completed=true`, the SQL totals above, and the monotonic recorded transition sequence. All owned host/tunnel processes were stopped, and neither verification port remained listening. Temporary verification code stayed outside the repository.

A focused cancellation-during-retry test now confirms prompt cancellation during the maximum **300000 ms** reconnect delay without starting a second connection attempt. An existing retry test initially failed because it polled a transient **100 ms** Offline state after synchronous work; it now subscribes before pipeline startup and captures that transition synchronously rather than depending on scheduler timing. No runtime logic or analytical assertion was changed.

**Final sequential validation:** focused scanner/publication **21 passed, 0 failed, 0 skipped**; full offline units **211 passed, 0 failed, 0 skipped**; solution build **0 warnings, 0 errors**. SQL integration suite results remain those recorded below; this follow-up additionally verified exact-session real-source persistence with read-only queries. IEX market-event/dashboard validation remains blocked by the observed after-hours absence of exchange messages, not credentials or test-stream connectivity. No claim of full A.1 completion or profitability is made.

## Milestone A.1 authenticated follow-up (2026-10-27)

The local Alpaca key ID and secret were configured after the earlier missing-credential attempt below. No credential values are recorded here. The standalone opt-in connectivity test passed **1/1 with no skips** against the official Alpaca `test` WebSocket with `FAKEPACA`. Its pass establishes receipt of a canonical event, not independent channel/message totals.

### Application, persistence, and browser

The real application ran from **2026-10-27T20:44:55Z to 20:49:59Z**, approximately five minutes, through Alpaca, canonical events, symbol state, existing features/detector, publication gate, scanner state, SQL, and Blazor. The sanitized monitor captured 61 status samples, observed **Connecting -> Live**, a maximum of **61 unique processed canonical events**, no unresolved-gap flags, and healthy HTTP responses. No reconnect, parser-error, or unhandled-failure indicators were observed; stderr was empty. These observations are not instrumented raw-frame/parser totals or proof of a full-session soak.

Browser verification without reload observed event count **8 -> 10** and analysis time **20:45:32 -> 20:45:42 UTC**. The dashboard displayed `Alpaca / test / Synthetic`, Live health, event/receive timestamps, reference price/VWAP **134.56**, spread approximately **142.4 bps**, quality **15**, `Avoid / No Trade`, and existing vetoes. No analytical formulas or thresholds were adjusted for synthetic output. Synthetic repeated trade identities are deduplicated and are not realistic participation evidence.

**Database targeting discrepancy:** the first host's intended dedicated-test connection override did not reach the child process. Read-only exact-session queries found session `alpaca-a1-test-20261027-1640` in the existing development `MarketOpportunityEngine` database: **59 Quote + 1 Trade events, 41 feature snapshots, 41 opportunities**, all `AvoidNoTrade / Alpaca / test / Synthetic`. Snapshot timestamps spanned **20:45:02.6859597-20:49:52.6857336 UTC**. The dedicated integration database contained zero rows for that session. All records were preserved. The maximum processed count of 61 versus 60 persisted events remains unreconciled; no complete-tail-flush guarantee is claimed.

A subsequent host was launched with a connection passed explicitly through `ProcessStartInfo.Environment` after confirming the dedicated catalog. Health returned HTTP 200. Read-only queries against **`MarketOpportunityEngine_IntegrationTests`**, exact session `alpaca-a1-guarded-20261027-1700`, found **16 Quote + 1 Trade events, 12 feature snapshots, and 12 opportunities**, all `AvoidNoTrade / Alpaca / test / Synthetic`. Analysis timestamps were **21:07:37.6860053-21:08:52.6872932 UTC**. No live status sampling or second browser result is claimed: the verification helper initially probed incorrect unversioned endpoints (404) rather than the existing `/api/v1` routes. The host was stopped and its stopped state confirmed. SQL password environment state was cleared.

Initial launch/verification helper failures (missing session configuration, connection/environment propagation, PowerShell assembly/script issues, and incorrect API paths) are tooling/setup failures, not demonstrated Alpaca rejections or analytical failures. They are not counted as passing continuous-run evidence.

### IEX

The corrected read-only WebSocket probe ran **20:59:45-21:00:46 UTC** (configured receive window 60 seconds), after regular market close. Transport connection, authentication, and subscription to trades and quotes all succeeded for **SPY, QQQ, AAPL, MSFT, NVDA, AMD** using the official IEX endpoint. It observed **0 trades, 0 quotes, 0 provider errors**, and no local/transport/auth/subscription error. This is connection evidence only: no exchange event, canonical IEX event, IEX analytics/dashboard update, or Live transition was verified. An earlier local probe failure happened before transport and is not a provider rejection.

### Validation boundary and remaining exit criteria

Current-code offline evidence remains **20/20 focused scanner/publication tests**, **210/210 full unit tests**, and a solution build with **0 warnings/errors**. The prior guarded hosted fixture runs passed **1/1 twice** and persistence/migration tests **3/3**. No runtime source changes were made during this authenticated follow-up.

The synthetic runs produced only vetoed `AvoidNoTrade` decisions; no active candidate suspension was demonstrated against the live provider. No real socket fault was induced. Reconnect, bounded delay, new-data-only Live, gap suspension, and cancellation have deterministic test coverage only. Raw message totals, parser-error counters, publication transition totals, shutdown tail persistence, and longer/full-session continuous operation remain unverified or unavailable. No profitability evaluation was performed.

**Milestone A.1 is partially verified, not complete.** Remaining work is market-available IEX application/dashboard validation, controlled real-transport recovery validation, and longer operational observation with explicit accounting boundaries. All owned verification hosts were stopped. Sanitized reports/logs were moved out of the repository to session artifacts; credentials and temporary reports were not added to source configuration.

## Milestone A.1 validation attempt (2026-10-27)

The current working tree's connection-resilience/dashboard changes passed the focused scanner/publication suite (**20/20**), full offline unit suite (**210/210**), and solution build (**0 warnings, 0 errors**). The standalone Alpaca connectivity project skipped its single test because the project User Secrets contained no Alpaca API key or secret; the test skipped before opening a socket. No authentication, WebSocket, subscription, canonical live-event, IEX, dashboard-with-live-events, or soak evidence was produced. Live operating metrics remain unavailable.

The already-running healthy Compose SQL Server and pre-existing `MarketOpportunityEngine_IntegrationTests` database were verified with a read-only catalog query. Guarded hosted fixture tests passed **1/1 on each of two runs against the same database**; persistence/migration tests passed **3/3**. The target database and fixture rows were preserved; migration tests used only their documented uniquely named temporary databases. The first catalog-query attempt did not trust the local self-signed certificate and stopped before SQL tests; the retry used `sqlcmd -C` and succeeded. An intermediate PowerShell guard script also had a parser error before database work; it was corrected. No credentials were printed, and process-only connection settings were cleared.

This checkpoint added bounded Alpaca reconnect retries, explicit unresolved-gap publication suspension, and dashboard visibility for existing fields. The deterministic retry test confirms unhealthy/connecting states and that the feed becomes live only after a valid event, while publication remains suspended after reconnection. These are local test results, not proof against Alpaca's service. Actual FAKEPACA and IEX validation, continuous operation, and its requested runtime metrics remain blocked by missing Alpaca credentials.

**Audit date:** 2026-10-27 (validated configured minimum short-horizon dollar-volume veto; prior realized-volatility, descriptive feature, SQL/API/browser, and implementation-audit evidence retained separately below)
**Evidence base:** repository source, tests, configuration, migrations, ADRs, README, master specification, Alpaca official WebSocket/stock-data documentation, and the validation results recorded below.

## Latest Analysis Snapshot - configured minimum dollar-volume veto (2026-10-27)

**Versioned detector risk-veto slice: COMPLETE. Full Milestone A: NOT COMPLETE.**

| Area | New verified result | Evidence boundary |
|---|---|---|
| Focused offline tests | **156 passed, 0 failed, 0 skipped** | `ScannerReplayTests` and `MomentumContinuationTests`; event-time window, ordering/duplicates, isolation, partial retention, invalid statuses, threshold boundaries, detector veto accumulation, identity and JSON. |
| Full offline unit suite | **208 passed, 0 failed, 0 skipped** | Unit project only; no database, web host, or provider. |
| Solution build | **Succeeded, 0 warnings, 0 errors** | Full solution, including integration-test compilation. |
| SQL persistence/migrations | **Not run / unavailable** | Guarded preflight found `MARKETOPPORTUNITY_SQL_TEST_CONNECTION` unset. No SQL connection, service operation, or database test was attempted. |
| Hosted fixture/browser/provider/Azure | **Not run** | No hosted/UI/provider/Azure validation claimed. |
| Versions | **features-v14 / scanner-config-v15** | Adds dollar-volume feature/status and positive decimal minimum; setup remains `momentum-continuation-v1`. No schema migration. |

`ShortHorizonDollarVolumeUsd` is the decimal sum, in USD, of `TradeEvent.Price * TradeEvent.Size` for eligible trades in the inclusive event-time window `[AsOfUtc - MomentumWindowSeconds, AsOfUtc]`. The configured primary momentum window is the only duration source. Input is restricted to the same-symbol, explicit-session retained snapshot at `AsOfUtc`, in the existing canonical event order and first-occurrence duplicate handling. Quotes, receive time, wall-clock time, other symbols/sessions and future events do not contribute. Decimal multiplication and summation overflow is surfaced; it is not silently discarded, clamped, or caught.

The feature has nullable decimal value plus dedicated `ShortHorizonDollarVolumeStatus` (`Available`, `Insufficient`, `InvalidTradeData`). No eligible window trades gives null/`Insufficient`; any eligible trade with `Price <= 0` or `Size <= 0` gives null/`InvalidTradeData`, with no partial sum; positive inputs yield a numeric `Available` value, including a single trade. `HasCompleteSessionData=false` means the value describes only retained observations and does not establish upstream/session-start completeness or qualify it for comparison with the full-window minimum.

`Scanner:MinimumShortHorizonDollarVolumeUsd` defaults to positive decimal **100,000 USD**. Every Momentum Continuation evaluation with complete state and consistent `Available` non-negative dollar volume adds `InsufficientDollarVolume` only when `value < minimum`; equality passes. `Insufficient`/null adds `DollarVolumeUnavailable`. `InvalidTradeData`, unknown statuses, negative available values, and all inconsistent value/status combinations add `InvalidDollarVolumeData`, even for partial state. A consistent available partial value skips only the numeric comparison; the existing `InsufficientRequiredFeatureData` hard veto remains. Existing invalid trade/session vetoes and unrelated detector behavior remain. The $100,000 cutoff is explicitly provisional risk tolerance, **not market-calibrated**; synthetic fixtures are regression data and not cutoff calibration or profitability evidence.

The fixture regression expectations were independently derived from event-time-eligible trade price/size inputs, not arrival order:

| Symbol/session | Ordered eligible `price × size` inputs | Expected USD / status |
|---|---|---|
| MOE / fixture-2026-10-22 | 149.90×1200, 150.08×900, 150.23×1400, 150.40×300, 150.48×1800 | 841,258 / Available |
| MOE / fixture-2026-10-23 | 150.50×1250, 150.30×900, 150.08×1450, 149.90×1850 | 818,326 / Available |
| QNT / fixture-2026-10-22 | 80.40×1100, 80.20×950, 80.08×1300, 79.90×1700 | 404,564 / Available |
| QNT / fixture-2026-10-23 | 79.80×1150, 80.00×1000, 80.20×1400, 80.40×350, 80.48×1750 | 453,030 / Available |

The new value/status (including null/status-only changes) participate explicitly in deterministic feature identity and serialized snapshot JSON. HTTP uses the existing string-enum convention; persisted snapshot JSON uses numeric enum encoding. The new threshold is part of serialized configuration; changing it under the same configuration version is rejected by the existing immutable same-version persistence guard. The new JSON fields require no SQL schema migration.

**Passing commands/results, in requested order:**

| Order | Exact command | Result |
|---|---|---|
| Focused offline | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj --filter "FullyQualifiedName~ScannerReplayTests|FullyQualifiedName~MomentumContinuationTests"` | **156 passed, 0 failed, 0 skipped** |
| Full offline units | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj` | **208 passed, 0 failed, 0 skipped** |
| Solution build | `dotnet build MarketOpportunityEngine.sln` | **Succeeded, 0 warnings, 0 errors** |

**SQL guard/result:** The process-environment preflight found `MARKETOPPORTUNITY_SQL_TEST_CONNECTION` unset. No connection string was printed or parsed, no SQL connection or service operation was attempted, and no SQL tests were run. SQL coverage is **not run / unavailable**, not a pass or failure. Hosted fixture, browser, live-provider, User Secrets, and Azure validation were not run.

**Failed attempt, not passing evidence:** The initial focused invocation stopped during compilation (0 tests executed) because a positional `FeatureSnapshot` fixture lacked the new arguments and a replay test's mixed derived-event switch needed an explicit base type. Those test-only compilation errors were corrected. The focused rerun and subsequent full offline suite passed.

**Recommended next smallest Milestone A slice:** Define the source and completeness contract for any same-time-of-day relative-volume feature before implementation; do not infer its baseline or calibrate thresholds from synthetic fixtures.

## Historical Analysis Snapshot - validated realized-volatility risk veto (2026-10-26)

**Versioned detector risk-veto slice: COMPLETE. Full Milestone A: NOT COMPLETE.**

| Area | New verified result | Evidence boundary |
|---|---|---|
| Focused offline tests | **145 passed, 0 failed, 0 skipped** | `ScannerReplayTests` and `MomentumContinuationTests`; threshold boundaries, status/value branches, partial-retention behavior, detector outcomes, and existing replay/identity invariants. |
| Full offline unit suite | **197 passed, 0 failed, 0 skipped** | Unit project only; no database, web host, or provider. |
| Solution build | **Succeeded, 0 warnings, 0 errors** | Full solution, including integration-test compilation. |
| SQL persistence/migrations | **Not run / unavailable** | Guarded preflight found `MARKETOPPORTUNITY_SQL_TEST_CONNECTION` unset. No SQL connection, service operation, or database test was attempted. |
| Hosted fixture/browser/provider/Azure | **Not run** | No hosted/UI/provider/Azure validation claimed. |
| Versions | **features-v13 / scanner-config-v14** | Adds positive decimal `MaximumSessionRealizedVolatilityPercent = 1.00`; setup remains `momentum-continuation-v1`. Volatility formula and all feature calculations remain unchanged. |

`SessionRealizedVolatilityPercent` remains the descriptive, unannualized trade-point realized-volatility value in percent points; this is not bar-based ATR, probability, or profitability evidence. The configurable cutoff is **1.00 percentage point** (`1.00` means 1%), validated as a positive decimal (`> 0`) with no upper cap. It is an explicitly provisional risk-tolerance choice, not statistically market-calibrated; synthetic fixture data is not cutoff calibration evidence.

The detector evaluates this gate on every snapshot evaluation. For `HasCompleteSessionData == true` and a consistent `Available` status/non-negative value, `value >= threshold` adds `ExcessiveRealizedVolatility`; lower values do not. `Insufficient` with null adds `VolatilityUnavailable`. `InvalidPriceData`, unknown statuses, negative available values, and all inconsistent/missing value/status combinations add `InvalidVolatilityData`. For partial retained-session data, a consistent available value is not compared with the threshold; the existing `InsufficientRequiredFeatureData` hard veto remains. An insufficient or invalid volatility status still produces its explicit data veto even when the session is partial. Existing risk and data-quality vetoes are preserved.

The threshold is serialized in `ScannerConfiguration`, so the existing SQL store's same-version JSON equality guard treats a changed threshold as a conflicting immutable configuration. The persistence test was updated to exercise that exact same-version threshold change; the SQL test itself was not run because its guarded connection variable was absent. Only `scanner-config-v13` advances to `scanner-config-v14`; `features-v13`, `momentum-continuation-v1`, prior immutable decisions, prior feature calculations, and the volatility formula are unchanged. No schema migration was added.

**Passing commands/results:**

| Order | Exact command | Result |
|---|---|---|
| Focused offline | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj --filter "FullyQualifiedName~ScannerReplayTests|FullyQualifiedName~MomentumContinuationTests"` | **145 passed, 0 failed, 0 skipped** |
| Full offline units | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj` | **197 passed, 0 failed, 0 skipped** |
| Solution build | `dotnet build MarketOpportunityEngine.sln` | **Succeeded, 0 warnings, 0 errors** |

**SQL guard/result:** A read-only process-environment preflight reported `MARKETOPPORTUNITY_SQL_TEST_CONNECTION` as unset. No connection string was printed or parsed, no SQL connection was attempted, no service was started/stopped, and no User Secrets were loaded. SQL coverage is **not run / unavailable**, not a pass or failure.

**Failed attempts, not passing evidence:** The initial focused run had **142 passed / 3 failed** because existing synthetic scenarios crossed the new authorized threshold while their assertions still expected detector neutrality. The golden replay now asserts `ExcessiveRealizedVolatility`; tests of unrelated detector behavior use below-threshold data. The focused rerun passed **145/145**.

**Historical recommendation at this preceding checkpoint:** Define the minimum-liquidity/dollar-volume gate only after establishing its eligible data source, event-time observation window, units, and unavailable/partial-data behavior. That slice is now implemented and validated above; do not derive cutoff calibration from synthetic fixtures.

## Historical Analysis Snapshot - descriptive retained-session trade-point realized volatility (2026-10-26)

**Versioned descriptive retained-session trade-point realized-volatility slice: COMPLETE. Full Milestone A: NOT COMPLETE.**

| Area | New verified result | Evidence boundary |
|---|---|---|
| Focused replay | **129 passed, 0 failed, 0 skipped** | Exact rising/falling/unchanged values; insufficient/invalid, ordering, duplicate, cutoff, partial eviction, symbol/session, JSON, identity and detector neutrality. |
| Offline units | **193 passed, 0 failed, 0 skipped** | Passed before SQL and again after the documented SQL sequence. |
| Hosted fixture HTTP + SQL | **1 passed on each of two consecutive runs** | Same dedicated database without reset; exact values/statuses asserted in all four HTTP and raw persisted snapshots with provenance, completeness, identity, canonical events and negative lookups. |
| SQL persistence/migrations | **3 passed, 0 failed, 0 skipped** | Changed prices create a new immutable snapshot/decision; original JSON and decision remain unchanged. |
| Solution build | **Succeeded, 0 warnings, 0 errors** | Full solution, including integration tests. |
| Versions | **features-v13 / scanner-config-v13** | Nullable `FeatureSnapshot.SessionRealizedVolatilityPercent` plus `RealizedVolatilityStatus` participate explicitly in deterministic identity; fixed rule/no new parameter; setup remains `momentum-continuation-v1`. Prior persisted records are not rewritten. |
| Browser/live-provider/Azure | **Not run** | Analytics-only change; no UI/provider changes and no new browser, provider, or Azure evidence. |

For canonical ordered eligible trade prices `P[0..n-1]`, define each consecutive log return as `r[i] = ln(P[i] / P[i-1])`. `SessionRealizedVolatilityPercent` is `100 * sqrt(sum(r[i]^2))`, unannualized, in percent points, and not divided by movement count or elapsed time. `Math.Log`/`Math.Sqrt` are calculated using double precision; the value is converted to decimal and rounded to **9 fractional decimal places, midpoint-to-even**. It is a trade-point realized-volatility estimate, not bar-based ATR; no OHLC bars are synthesized.

Input is every retained same-symbol, explicit-session `TradeEvent.Price` with `EventTimeUtc <= AsOfUtc`, in the unchanged canonical event-time/provider/ordinal-sequence/canonical-identity order. Equal timestamps remain separate consecutive observations; existing first-occurrence duplicate handling remains. Quotes, sizes, receive time and wall-clock time do not contribute. The contract has no missing trade-price representation. No eligible trade or a single positive trade yields null / `Insufficient`; any eligible price `<= 0` (including a single invalid price or a mixed valid/invalid set) yields null / `InvalidPriceData`; two or more positive prices yield a decimal / `Available`. Equal prices yield numeric zero / `Available`.

Read results with `HasCompleteSessionData`: false means any value describes retained partial-session observations and is not complete-session volatility; it does not change status. Eviction may reduce the retained observations to an insufficient count. Future events are excluded unless their arrival evicts an eligible retained event, which may change later as-of results and completeness. HTTP serializes `sessionRealizedVolatilityStatus` as a string; persisted snapshot JSON uses its default numeric enum representation. Value (including null) and status are both included explicitly in deterministic feature identity. The rule is fixed, so no new configuration parameter is needed. Versions advance from `features-v12` / `scanner-config-v12` to `features-v13` / `scanner-config-v13`; `momentum-continuation-v1` is unchanged. No detector qualification/scoring/classification, evidence/contradictions, opportunity/publication, veto, UI, provider, or prior feature calculation changed.

Fixture HTTP/persisted JSON values were independently derived from event-time-ordered prices, including the late events: MOE/2026-10-22 `0.199999477`, MOE/2026-10-23 `0.231391437`, QNT/2026-10-22 `0.367550138`, QNT/2026-10-23 `0.443762951`; all have `Available` status (persisted enum value `0`). The fixture test also verifies no change to the serialized opportunity results.

**Passing commands and results (documented execution order; all test runs had 0 failures/0 skips):**

| Order | Exact command | Result |
|---|---|---|
| Offline focused | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj --filter FullyQualifiedName~ScannerReplayTests` | **129 passed, 0 failed, 0 skipped** |
| Offline full, before SQL | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj` | **193 passed, 0 failed, 0 skipped** |
| Hosted fixture 1 | `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter FullyQualifiedName~FixtureWebApplicationTests` | **1 passed, 0 failed, 0 skipped** |
| Hosted fixture 2 | Same command, same database without reset | **1 passed, 0 failed, 0 skipped** |
| Persistence/migrations | `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter "FullyQualifiedName~SqlServerPersistenceTests|FullyQualifiedName~ExplicitSessionMigrationTests"` | **3 passed, 0 failed, 0 skipped** |
| Offline full, after SQL | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj` | **193 passed, 0 failed, 0 skipped** |
| Solution build | `dotnet build MarketOpportunityEngine.sln` | **Succeeded, 0 warnings, 0 errors** |

**SQL gate/result:** `MARKETOPPORTUNITY_SQL_TEST_CONNECTION` was absent. The existing Compose SQL Server was already running and healthy; a read-only query confirmed its existing dedicated `MarketOpportunityEngine_IntegrationTests` database. A PowerShell-side builder preflight attempt failed with `PlatformNotSupportedException` before connecting; the actual .NET 10 integration-test builder guard then validated the test catalog before SQL operations. The fallback connection was constructed from the existing container credential only in process memory and cleared in `finally`; the PowerShell process launching `dotnet` did not load SqlClient. No service was started/stopped/recreated; no fixture records or database were reset/deleted; the configured database was not dropped. Migration tests used only uniquely named temporary databases. Browser, live-provider, User Secrets, and Azure checks were not run.

**Failed attempts, not passing evidence:** A focused compile initially failed because the directly affected positional `FeatureSnapshot` test fixture lacked the new arguments; the next focused run had 124 passed / 5 failed, including precision expectations and prior identity tests requiring volatility normalization. Corrections led to the historical descriptive slice's final 129/129 run. An early hosted fixture expectation used arrival order for the two late trades; corrected event-time-order expectations passed in both final hosted runs. A PowerShell builder preflight raised `PlatformNotSupportedException`, and an initial brace-escaped connection string led to login failure before SQL work. A separate unfiltered integration invocation also reproduced the same initial hosted expected-value mismatch (193 unit tests passed, with 1 hosted test failure); it is not passing evidence for the final sequence. A corrected quoted connection and the in-test .NET 10 builder guard enabled that historical guarded sequence.

**Historical next-slice recommendation at that checkpoint:** Define a validated volatility-risk threshold and explicit hard-veto behavior, including the treatment of `Insufficient`, `InvalidPriceData`, and partial retained-session results. This was subsequently implemented as the separate `scanner-config-v14` gate in the latest snapshot. Full Milestone A remains incomplete.

## Historical Analysis Snapshot - retained-session trend direction (2026-10-26)

The preceding descriptive trend-direction slice advanced `features-v11` / `scanner-config-v11` to `features-v12` / `scanner-config-v12`; the non-null `TrendDirectionContext` output and detailed rules remain in the README and source. That slice passed focused replay **123/123**, offline units **187/187**, hosted fixture **1/1 twice**, persistence/migrations **3/3**, and solution build. Its browser/live-provider/Azure checks were not run. This historical evidence is retained separately from the current `features-v13` results above.

## Historical Analysis Snapshot - retained-session trade-point ADX (2026-10-26)
This is an explicit **trade-point ADX variant**, not bar-based ADX. The canonical events expose trades and quotes but no OHLC bars, so the calculator does not synthesize bars. Each eligible trade price is one observation. For consecutive prices `P[t-1]`, `P[t]`: `TR = abs(P[t] - P[t-1])`; `up = P[t] - P[t-1]`; `down = P[t-1] - P[t]`; `+DM = up` only when `up > down && up > 0`; `-DM = down` only when `down > up && down > 0`; otherwise the respective DM is zero. Equal consecutive prices have zero TR/DM.

`Scanner:TrendStrengthPeriod` defaults to **14 price movements** and validates as a positive integer. For period `N`, the first `N` movements seed arithmetic means for TR/+DM/-DM. Subsequent values use Wilder smoothing `smoothed = prior + (current - prior) / N`. `+DI = 100 * smoothed(+DM) / smoothed(TR)` and `-DI = 100 * smoothed(-DM) / smoothed(TR)`; `DX = 100 * abs(+DI - -DI) / (+DI + -DI)`. Zero true range or zero DI sum defines DX as zero. Initial ADX is the arithmetic mean of the first `N` DX values; subsequent ADX uses the same Wilder recurrence. Calculations use decimal values deterministically; ADX is nondirectional strength on 0–100, not probability.

Eligible input is all retained `TradeEvent`s for the same symbol and explicit session with `EventTimeUtc <= AsOfUtc`, in canonical event-time/provider/ordinal-sequence/canonical-identity order. There is no duration interval; older eligible trades contribute. Quotes, sizes (including zero/negative), receive time, and wall-clock time are excluded. Equal timestamps are distinct, canonically ordered observations. Warm-up needs **2 × N eligible trades**: `N` movements for initial DI/DX, then `N` DX values for initial ADX. No eligible trade or fewer than `2 × N` returns `SessionAdx = null`, `AdxStatus.Insufficient`. Any eligible price `<= 0` returns null and `InvalidPriceData`, without dropping the invalid trade. A non-null value with `HasCompleteSessionData = false` is retained-partial data; completeness does not change the status/null value. A future event is excluded unless arrival evicts eligible retained state, which can change a historical-as-of result; completeness must be interpreted alongside the value.

The output is `decimal? SessionAdx` / JSON `sessionAdx` and `AdxStatus SessionAdxStatus` / JSON `sessionAdxStatus`. HTTP uses the existing string-enum convention; SQL snapshot JSON retains numeric enum encoding (`Available=0`, `Insufficient=1`, `InvalidPriceData=2`). Both fields, including null and state, participate in deterministic feature identity. `TrendStrengthPeriod` is included in serialized configuration and versioned validation. Defaults advance from `features-v10` / `scanner-config-v10` to `features-v11` / `scanner-config-v11`; setup remains `momentum-continuation-v1`. Earlier persisted configurations/snapshots/decisions are not rewritten. ADX is descriptive only and does not affect qualification, scoring, classifications, publication, vetoes, UI, providers, or prior feature calculations.

The hosted fixture has 4–7 eligible trades per symbol/session, below the default 28-observation warm-up; exact expected `sessionAdx` is null and status is `Insufficient` in each HTTP snapshot and raw persisted SQL snapshot JSON. Persistence replay asserts ADX 100 for the original monotonic four-trade input and 57.14285714285714285714285714 for changed input under test period 2; the changed input creates distinct immutable records while original snapshot JSON, opportunity fields, and evidence remain unchanged.

**Passing commands and results (all passing runs had 0 failures/0 skips):** Focused replay `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj --filter FullyQualifiedName~ScannerReplayTests` (**119/119**); full offline units `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj` (**183/183** before SQL and again after); hosted fixture `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter FullyQualifiedName~FixtureWebApplicationTests` (**1/1 twice against the same database**); SQL persistence/migrations `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter "FullyQualifiedName~SqlServerPersistenceTests|FullyQualifiedName~ExplicitSessionMigrationTests"` (**3/3**); and `dotnet build MarketOpportunityEngine.sln` (**0 warnings/0 errors**). Integration-test project compile-only build also passed with 0 warnings/0 errors.

The connection variable was absent. The existing Compose SQL Server was already healthy and its existing dedicated `MarketOpportunityEngine_IntegrationTests` catalog existed; a read-only query verified it. The process-only connection was constructed from the running container credential in memory and guarded by `SqlConnectionStringBuilder` before SQL work. The parent process launching `dotnet` did not load SqlClient. Overrides and password references were cleared; fixture rows were retained; no database was reset/dropped and no service was started/stopped. Migration tests used only uniquely named temporary databases. Browser, live-provider, User Secrets, and Azure checks were not run.

**Failed attempts, not passing evidence:** The first focused build found the positional test snapshot fixture missing the new fields. Subsequent replay assertions corrected an expected decimal rounding and an identity test's unintended VWAP change before the final 119/119 run. An initial persistence/migration run was **2 passed / 1 failed** because its exact ADX decimal expectation differed by one final decimal digit; the corrected run passed **3/3**. Separate PowerShell child builder attempts returned `Microsoft.Data.SqlClient is not supported on this platform`, and one orchestration attempt ended after preflight without launching tests. These were child-runner/expectation failures; they are not evidence SQL Server was unavailable. The final SQL test runs passed their in-process connection-string guard and the guarded catalog was available. No browser, live-provider, User Secrets, or Azure verification was run.

**Historical next-slice recommendation at that checkpoint:** Add descriptive trend-direction context derived from existing EMA relationship/slopes, VWAP slope, price structure, and ADX, with explicit neutral/conflicting semantics and replay tests; keep it separate from `momentum-continuation-v1` qualification and scoring. This recommendation was completed in the subsequent trend-direction slice.

## Historical snapshot: retained-session price structure (2026-10-26)

The preceding structure slice passed focused replay **107/107**, offline units **171/171**, hosted fixture **1/1 twice**, persistence/migrations **3/3**, and solution build **0 warnings/0 errors**. Its exact fixture `sessionPriceStructure` was null for all four sessions because there were fewer than two confirmed pivots of each type. Structure output was descriptive, used the fixed two-left/two-right pivot rule, and participated explicitly in `features-v10` identity. Historical results are retained here; current versions are `features-v11` / `scanner-config-v11`.

## Historical high/low analysis snapshot - 2026-10-25

## Historical volume-slice snapshot - 2026-10-25

The following volume-only snapshot and its totals are historical, superseded by the extrema evidence above.

**Versioned descriptive session cumulative-volume slice: COMPLETE. Full Milestone A: NOT COMPLETE.**

| Area | Latest verified result | Evidence boundary |
|---|---|---|
| Hosted fixture → SQL + HTTP | **1 passed, 0 failed, 0 skipped on each of two consecutive runs** | Same dedicated SQL database; isolated Testing host; exact four analyses/snapshots, four absent-resource `404`s, and 26 canonical events compared. |
| Hosted API negative lookups | **4 exact `404 Not Found` assertions passed on both hosted runs** | Valid absent opportunity GUID; absent symbol; explicit absent session for both MOE and QNT. Exact valid session-specific lookups also passed, demonstrating no cross-session fallback. |
| SQL persistence / migrations | **3 passed, 0 failed, 0 skipped** | Dedicated local test database; includes clean-schema and legacy-schema migration tests. |
| Focused replay tests | **46 passed, 0 failed, 0 skipped** | 16 added volume cases; existing ROC/slope/replay cases preserved. |
| Offline unit tests | **110 passed, 0 failed, 0 skipped** | Passed immediately after focused tests and again after SQL; no live provider or database required. |
| Solution build | **Succeeded; no warnings or errors reported** | Includes integration-project compilation. |
| Running dashboard | **Historical integrated-browser walkthrough passed** | Not rerun for this analytics-only slice; no UI change or new browser evidence. |
| Hosted API versus persisted SQL | **4 opportunity IDs, 4 snapshot IDs/payloads, and all 26 canonical events matched automatically** | Exact ID/version-scoped SQL comparisons include analytical fields/evidence/provenance and trade/quote fields. API still reads process memory, not SQL history. |
| Effective configuration / SQL immutability | **Existing HTTP and SQL assertions preserved and passed** | HTTP exposes both windows; SQL rejects a same-version window change and retains original configuration/snapshot payloads. |
| Session cumulative volume | **Exact HTTP and persisted JSON assertions passed** | MOE-22: 5600; MOE-23: 5450; QNT-22: 5050; QNT-23: 5650. All report complete retained state. |
| Live providers / Azure | **Not run in this slice** | No provider User Secrets or live connection. Earlier Massive attempt belongs to historical evidence in section 20. |

### Observed fixture results

| Symbol | Explicit session | Analytical classification | Publication state / reason | Persisted events |
|---|---|---|---|---:|
| MOE | fixture-2026-10-22 | ProbableLong | Expired / OpportunityExpired | 7 |
| QNT | fixture-2026-10-22 | ProbableShort | Expired / OpportunityExpired | 6 |
| MOE | fixture-2026-10-23 | ProbableShort | Active / PublicationAllowed | 6 |
| QNT | fixture-2026-10-23 | ProbableLong | Active / PublicationAllowed | 7 |

All four results carried `RecordedFixture` / `Synthetic` / `Synthetic` provider/feed/coverage, realtime **false**, and trade/quote support **true**. Publication was evaluated at fixture replay time, not today's market time. Scores/classifications are not calibrated probabilities or profitability claims.

### Changes, operational state, and remaining work

- **Changes:** Added nullable `long` `SessionCumulativeVolume`: checked sum of sizes over all retained same-symbol/explicit-session trades at/before `AsOfUtc`, not either ROC window. No eligible trades or any eligible size `<= 0` yields null; one positive-size trade is sufficient. Quotes/receive/system time do not contribute. Positive sizes remain measurable independently of invalid prices; existing price validity, quality, and veto semantics are untouched. `ShortHorizonVolume`, both validated ROC horizons, and exact VWAP-slope tests/algorithm remain unchanged.
- **Completeness:** Read volume with `HasCompleteSessionData`. True means no event eviction; false marks a non-null retained sum as partial, never complete session volume. This sticky flag includes quote eviction and does not establish upstream feed/session-start coverage. After eviction, no eligible trades or invalid retained sizes still yields null. Future events are excluded but may evict earlier state; this is not historical replay/backfill.
- **Feature contract versions:** bumped from v5 to `features-v6` and `scanner-config-v6`; serialized configuration and new volume participate in deterministic identity. Setup remains `momentum-continuation-v1`. Prior persisted configurations/snapshots/decisions remain immutable. No detector, scoring, classification, publication, veto, UI, provider, dependency, or migration change was made.
- **Preserved test isolation:** The fixture host remains non-Development, uses early host configuration, explicitly registers Fixture/configuration/SQL, and does not load provider User Secrets. The non-empty test database-name guard remains intact.
- **Preserved fix:** [SqlServerPersistenceTests](tests/MarketOpportunity.IntegrationTests/SqlServerPersistenceTests.cs) still migrates when `EnsureCreatedAsync` returns false. No test-database guard was weakened.
- **Final-run database safety:** The variable was absent; reused the existing healthy Compose SQL Server as authorized by this request, targeting `MarketOpportunityEngine_IntegrationTests` plus uniquely named migration-test databases. The non-empty catalog containing `test` was validated with `Microsoft.Data.SqlClient.SqlConnectionStringBuilder`. Credentials were held in process memory, not printed/saved. Fixture records were retained; no configured database was dropped.
- **Last verified cleanup:** Test hosts disposed; no separate web process was started for this slice. The pre-existing SQL container remained healthy/running. Connection settings were process-only. The prior port-5228 cleanup/browser walkthrough is historical, not rechecked here.
- **Remaining limits:** API/history reads remain memory-backed; publication history is not persisted; live reconnect/gap recovery, three additional detectors, and broader Milestone A feature/risk coverage are absent. Local fixture verification does not establish Azure SQL or live-provider readiness.
- **Historical next-slice recommendation:** Expose descriptive retained-session high/low prices with event-time cutoff, invalid-input handling, completeness, and replay tests. That recommendation was implemented in the next checkpoint recorded below.
- **Evidence timing:** New ordered automated runs are recorded first in section 20. Earlier failed attempts and historical browser/runtime runs remain separate.

Section 20 records exact commands and separates the successful final run from the earlier blocked preflight and prior checkpoint. Sections 24–27 summarize remaining blockers and the handoff.

## 1. Executive Status

- **Current milestone:** Milestone A — Usable Scanner.
- **Current implementation checkpoint:** Publication/lifecycle safety and Alpaca Market Data development stream are implemented in addition to the existing fixture/Massive sources.
- **Overall status:** **PARTIALLY IMPLEMENTED.** Publication safety and provider provenance are implemented. Local SQL persistence/migration, hosted fixture HTTP, and browser dashboard verification passed on 2026-10-25. Authenticated Alpaca connectivity remains unverified.
- **Last completed vertical slice:** Descriptive retained-session range position over the established extrema, with offline replay and repeated hosted fixture → SQL → current-state API regression. Existing features and detector output are preserved. This does not establish database-backed historical API reads; browser evidence is historical.
- **Currently active work:** Range-position analytics and hosted regressions complete; the pre-existing SQL container was preserved. No live provider or UI change.
- **Primary blocker:** None for this verification slice. Full Milestone A still lacks three detectors, several required features/gates, reconnect/gap recovery, and database-backed reads.
- **Recommended immediate next step:** Add fast/medium EMA relationship and event-time slope as versioned descriptive trend features, following the master specification; do not change detector outcomes until separately authorized. Keep provider recovery separate; do not connect live providers implicitly.

Milestone A has a tested offline analysis slice, an application-level publication safety boundary, and a verified local fixture SQL/API/interactive UI workflow. Provider adapters include fixture, Alpaca, and Massive. Full usable-scanner exit criteria still require reliable live recovery, four detectors, required risk/liquidity coverage, and structural invalidation monitoring.

## 2. Technology Verification

| Technology | Status | Evidence | Notes |
|---|---|---|---|
| .NET 10 / `net10.0` | **IMPLEMENTED** | All eight source/test projects target `net10.0`; solution restore/build succeeded. | Optional connectivity test project is outside the solution/default unit suite. No runtime patch pinning is visible. |
| ASP.NET Core 10 | **IMPLEMENTED** | Web SDK project targets `net10.0`; hosting/configuration is in `src/MarketOpportunity.Web/Program.cs`. | Local app startup depends on configured SQL Server. |
| C# | **IMPLEMENTED** | Source projects use C#; domain, analytics, pipeline, adapters, and endpoint code are C#. | Nullable reference types are enabled on the projects inspected. |
| Blazor | **IMPLEMENTED** | Blazor Web App components under `src/MarketOpportunity.Web/Components`; `App.razor` uses `InteractiveServer`. | No separate frontend technology. |
| Entity Framework Core 10 | **IMPLEMENTED** | Infrastructure references `Microsoft.EntityFrameworkCore.SqlServer` 10.0.0 and owns `MarketOpportunityDbContext`. | Local persistence and both migration tests passed on 2026-10-25. |
| Microsoft SQL Server | **IMPLEMENTED** | `docker-compose.yml` defines SQL Server 2022 Developer; development configuration targets `localhost,1433`. | SQL integration tests require an explicitly configured, dedicated test database. |
| Azure SQL Database compatibility | **PARTIALLY IMPLEMENTED** | Uses EF Core SQL Server provider and ordinary SQL Server mappings. | No Azure SQL connection, deployment, or compatibility test configuration exists. Compatibility is plausible, not verified. |
| SignalR | **PARTIALLY IMPLEMENTED** | Blazor Interactive Server uses its circuit transport; ADR-001 explicitly declines a separate application hub. | No app-level `AddSignalR`, hub, or domain event contract is present. |
| OpenTelemetry | **PARTIALLY IMPLEMENTED** | `Program.cs` adds ASP.NET Core tracing and ASP.NET Core/runtime metrics; OTLP exporter is conditional on `OTEL_EXPORTER_OTLP_ENDPOINT`. | No scanner-specific spans, counters, histograms, or end-to-end trace propagation. |
| Azure App Service | **NOT IMPLEMENTED** | No App Service deployment configuration or IaC found. | The Web project is an ASP.NET Core application but has no Azure hosting setup. |
| Azure Key Vault | **NOT IMPLEMENTED** | No Key Vault package/provider or resource configuration found. | Local README advises User Secrets; this is not deployed secret integration. |
| Azure Monitor / Application Insights | **NOT IMPLEMENTED** | No Application Insights package/exporter or Azure Monitor configuration found. | OTLP exporter is generic and does not alone constitute Azure Monitor integration. |
| Bicep | **NOT IMPLEMENTED** | No `.bicep` files found. | Azure infrastructure is not provisioned from code. |
| Azure DevOps Pipeline | **NOT IMPLEMENTED** | No Azure pipeline YAML or pipeline configuration found. | `.github/copilot-instructions.md` is not CI/CD. |

## 3. Solution / Repository Structure

Meaningful repository tree (generated `bin`, `obj`, and vendored Bootstrap assets omitted):

```text
MarketOpportunityEngine.sln
README.md
Market_Opportunity_Engine_Final_Master_Spec.md
docker-compose.yml
.github/
  copilot-instructions.md
docs/
  adr/
    ADR-001-blazor-render-mode.md
    ADR-002-ef-sqlserver-conventions.md
    ADR-003-in-process-event-transport.md
    ADR-004-market-event-retention.md
    ADR-005-market-data-provider-criteria.md
  providers/
    alpaca-websocket.md
    massive-websocket.md
src/
  MarketOpportunity.Domain/
    MarketEvents.cs
  MarketOpportunity.Analytics/
    MomentumContinuationDetector.cs
    SymbolEventState.cs
  MarketOpportunity.Application/
    ScannerPipeline.cs
  MarketOpportunity.Infrastructure/
    AlpacaMarketDataSource.cs
    EfScannerStore.cs
    FixtureMarketDataSource.cs
    MarketDataSourceFactory.cs
    MassiveMarketDataSource.cs
    MarketOpportunityDbContext.cs
    Migrations/
      20261023190000_AddExplicitSessionIdentity.cs
      20261024232400_AddOpportunityMarketDataProvenance.cs
  MarketOpportunity.Web/
    Program.cs
    ScannerRunnerService.cs
    appsettings.json
    appsettings.Development.json
    Properties/launchSettings.json
    Fixtures/momentum-continuation.json
    Components/
      App.razor
      Routes.razor
      Layout/
      Pages/
        Home.razor
        Counter.razor
        Weather.razor
        Error.razor
        NotFound.razor
tests/
  MarketOpportunity.UnitTests/
    AlpacaMarketDataTests.cs
    MassiveMarketDataOptionsTests.cs
    MassiveMarketDataParserTests.cs
    MomentumContinuationTests.cs
    ScannerPipelineTests.cs
    ScannerReplayTests.cs
  MarketOpportunity.IntegrationTests/
    FixtureWebApplicationTests.cs
    ExplicitSessionMigrationTests.cs
    SqlServerPersistenceTests.cs
  MarketOpportunity.AlpacaConnectivityTests/
    AlpacaTestStreamConnectivityTests.cs
```

### Projects and dependencies

| Project | Purpose | Project dependencies |
|---|---|---|
| `MarketOpportunity.Domain` | Canonical market-event, feature, opportunity, classification, and scanner-configuration contracts. | None. |
| `MarketOpportunity.Analytics` | Event-time symbol/session state, feature calculations, deterministic IDs, and Momentum Continuation detector. | Domain. |
| `MarketOpportunity.Application` | Market-data/store interfaces, health state, provider-independent publication gate/read model, and fixture/live orchestration pipeline. | Domain, Analytics. |
| `MarketOpportunity.Infrastructure` | Fixture, Alpaca, and Massive adapters; source factory; SQL entities/context/migrations and EF store. | Domain, Application. |
| `MarketOpportunity.Web` | ASP.NET Core host, API routes, hosted scanner, and Blazor interface. | Domain, Application, Analytics, Infrastructure. |
| `MarketOpportunity.UnitTests` | Fast tests for parser, options, analytics, replay, and pipeline behavior. | Domain, Analytics, Application, Infrastructure. |
| `MarketOpportunity.IntegrationTests` | SQL Server persistence/migration tests and exact hosted fixture Web API + persistence regression. | Analytics, Infrastructure, Web. |
| `MarketOpportunity.AlpacaConnectivityTests` | Explicitly invoked network test for Alpaca's synthetic `test` stream; not part of the solution/default unit suite. | Infrastructure; shares Web project's User Secrets ID. |

**Architectural assessment:** No direct dependency violation was found in the inspected project references: Domain has no project dependencies; Analytics has no EF/HTTP/provider dependency; Infrastructure owns EF and provider adapters. The application is a modular monolith. There are no separate API, Contracts, MarketData, Replay, Models, or Observability projects from the broader illustrative master-spec tree; API and hosting are currently in Web.

## 4. Architecture Status

| Pipeline stage | Status | Implementation / key files | Tests | Known limitations |
|---|---|---|---|---|
| Market data | **IMPLEMENTED** for fixture; **PARTIALLY IMPLEMENTED** for optional live sources | `FixtureMarketDataSource`; `AlpacaMarketDataSource`; `MassiveMarketDataSource`; centralized factory | Pipeline tests and Alpaca/Massive parser/options/capability/factory tests | Authenticated connectivity, entitlements, reconnect, and recovery are not integration-tested. |
| Normalization | **PARTIALLY IMPLEMENTED** | Fixture JSON, Massive messages, and Alpaca `t`/`q` messages map to canonical events in Infrastructure. | Alpaca/Massive parser tests cover timestamps, identities, malformed and unsupported messages. | No distinct reusable normalizer/data-quality stage; no correction or gap contract; no aggregates. |
| Event-time state | **IMPLEMENTED** for bounded symbol/session state | `SymbolEventState` stores and sorts events; duplicate identities are ignored while retained. | Replay tests cover ordering, duplicates, sessions, and future-event exclusion. | No watermarks, gap tracking, corrections, or exchange-session calendar. |
| Features | **PARTIALLY IMPLEMENTED** | `FeatureCalculator` in `SymbolEventState.cs`. | Momentum and replay tests cover VWAP, current price, return, volume, and as-of cutoff. | Narrow feature set; bounded eviction means cumulative session values become incomplete and vetoed. |
| Setup detector | **PARTIALLY IMPLEMENTED** | `MomentumContinuationDetector`. | `MomentumContinuationTests`, `ScannerReplayTests`. | Only one detector, using one short horizon and VWAP alignment; no complete trend/context engine. |
| Risk/data-quality gates | **PARTIALLY IMPLEMENTED** | Quote/data vetoes remain in the detector; `OpportunityPublicationGate` separately evaluates source health and candidate expiry in Application. Descriptive trade-point volatility is now available but is not a veto. | Tests cover stale/offline transition, immutable evidence, expiry, stale quote, invalid quote/price, spread, insufficient data, and evicted state. | Gap recovery, liquidity, reward/risk, volatility thresholds/veto, and structural invalidation monitoring remain absent. |
| Opportunity/publication lifecycle | **PARTIALLY IMPLEMENTED** | Immutable detector `Opportunity` plus separate `PublicationDecision` (`Active`, `Suspended`, `Expired`, `NotQualified`) and reason; `ScannerState` recalculates on feed-status changes. | Deterministic gate, transition, API read-model serialization, and fixture tests. | Current publication-state transitions are process-memory read-model state, not append-only SQL lifecycle rows; no later-price structural invalidation monitor. |
| Persistence | **SQL VERIFIED 2026-10-26** | `EfScannerStore`, `MarketOpportunityDbContext`, provider provenance columns, SQL Server provider, explicit-session and provenance migrations. | Hosted fixture HTTP/SQL regression passed twice on the same dedicated database; persistence/migration selector passed 3/3. | API does not query stored history; all tests require a guarded dedicated SQL test database. |
| ASP.NET Core | **IMPLEMENTED AND FIXTURE-VERIFIED** | `Program.cs` maps health and five API routes; opportunity list/detail responses expose publication status/reason; `ScannerRunnerService` starts scanner. | Hosted HTTP + SQL fixture test passed; local browser/API results matched SQL IDs. | API still serves the in-memory singleton state, not persisted history. |
| Blazor | **IMPLEMENTED IN CODE** | Interactive Server `App.razor`; dashboard in `Home.razor` shows source/feed/coverage and per-candidate lifecycle/reason. | No component/UI tests found; build succeeded. | Runtime browser workflow was not exercised; no SignalR application hub. |

**Exact evidence boundary:** Unit tests cover analysis/publication safety and provider parsers/options without network access. On 2026-10-26 the guarded hosted fixture HTTP/SQL regression passed twice on the same database and the persistence/migration selector passed; the prior local browser walkthrough from 2026-10-25 remains historical evidence only. Authenticated provider streams and historical reads remain unverified/absent. The API reads `ScannerState`, not persisted records.

## 5. Market Data Status

`IMarketDataSource` has three implementations. Provider choice is centralized by `MarketDataSourceFactory`; each source provides capability metadata.

| Source | Type | Data supplied | Trades | Quotes | Aggregates | Provider-specific | Authentication | Working / tested | Limitations |
|---|---|---|---|---|---|---|---|---|---|
| `FixtureMarketDataSource` | Recorded JSON fixture | Synthetic `MOE` and `QNT` events, interleaved over two explicit sessions; event and receive timestamps, provider, sequence identity, trade and quote fields. | Yes | Yes | No | No; normalized fixture values | No | Pipeline unit tests and hosted API+SQL test passed; local browser walkthrough passed on 2026-10-25. No committed browser test suite. | Small, synthetic, finite replay; not actual exchange data. |
| `AlpacaMarketDataSource` | Live WebSocket (also Alpaca synthetic test stream) | Stock trade/quote messages mapped to canonical events. Feed configurable as `test`, `iex`, `sip`, or `delayed_sip`. | Yes | Yes | No | Yes, parser and socket adapter confined to Infrastructure | Yes, API key ID + secret in User Secrets/configuration | Parser, configuration validation, feed capability mapping, source factory, and provenance unit-tested; test-stream connectivity test is opt-in and was not run. | No reconnect, historical warm-up, correction handling, gap/backfill, or aggregates; test feed is synthetic; IEX is limited and delayed SIP is delayed. |
| `MassiveMarketDataSource` | Optional live WebSocket | Massive real-time stock trades (`T.<symbol>`) and quotes (`Q.<symbol>`); provider event time and local receive time. | Yes | Yes | No | Yes, DTO parsing confined to Infrastructure | Yes, Massive API key; feed entitlement required | Parser/options are unit-tested. Actual authenticated WebSocket operation was not tested. | No historical warm-up, reconnect, heartbeat recovery, gap detection/backfill, or aggregates; depends on configured plan and credentials. |

The active default in `appsettings.json` is `MarketData:Provider = Fixture`. No evidence was collected that a live provider is currently connected. There is no historical source. Alpaca test stream configuration defaults to feed `test`/symbol `FAKEPACA`; credentials and explicit session ID are required before selection. Massive remains supported. No order/broker integration exists.

## 6. Canonical Market Event Model

The domain contracts are in `MarketEvents.cs`:

| Concept | Status | Evidence / notes |
|---|---|---|
| `MarketEvent` | **IMPLEMENTED** | Abstract record with `Symbol`, `SessionId`, `EventTimeUtc`, `ReceivedTimeUtc`, `Provider`, nullable `SequenceId`. |
| `TradeEvent` | **IMPLEMENTED** | Adds `decimal Price`, `long Size`. |
| `QuoteEvent` | **IMPLEMENTED** | Adds decimal bid/ask prices and nullable long bid/ask sizes. |
| Provider/feed capability metadata | **IMPLEMENTED** | `MarketDataCapabilities` records provider, feed, coverage (`Synthetic`, `Limited`, `Consolidated`, `ProviderDefined`), real-time flag, and supported trade/quote channels. |
| Aggregate/bar event | **NOT IMPLEMENTED** | Unsupported by the canonical event switch and provider adapter. |
| Symbol | **PARTIALLY IMPLEMENTED** | String field, not a validated instrument/symbol value object. |
| Event time / receive time | **IMPLEMENTED** | Separate `DateTimeOffset` fields; both live adapters map provider time to event time and injected `TimeProvider` to receive time. |
| Provider | **IMPLEMENTED** | String provider identity. |
| Event/sequence identity | **PARTIALLY IMPLEMENTED** | Nullable provider sequence plus deterministic `MarketEventIdentity.Create`; identity is derived rather than a field on the record. |
| Explicit session identity | **IMPLEMENTED** | Required by adapters and pipeline validation; session is never inferred from date. |

- **Ordering:** `SymbolEventState` binary-inserts by event time, then provider, sequence ID, and derived identity. Equal event times are deterministic.
- **Duplicates:** A sequence-backed content identity is hashed and deduplicated in state; content-derived identity is used when no sequence is available. The identity set is bounded with event eviction, so an evicted event's identity is no longer remembered in memory. SQL uses a unique event-identity index for persisted idempotency.
- **Late events:** Accepted and inserted into event-time order. No explicit late-event classification or late-event metrics are recorded.
- **Invalid events:** Adapters reject missing structural fields and unsupported/malformed event types. Invalid numeric quote/trade values can enter the canonical model and are marked invalid by feature calculation and detector vetoes; they are not uniformly rejected at ingestion.
- **Gaps/corrections:** **NOT IMPLEMENTED.** No gap record, correction contract, watermark, or backfill workflow.
- **Analysis provenance:** `FeatureSnapshot` and `Opportunity` carry provider/feed/coverage/realtime/channel metadata. Opportunity columns persist these fields through the provenance migration; hosted/persistence tests and local SQL queries verified fixture provenance on 2026-10-25.

## 7. Event-Time State Engine

- **Bounded?** Each `SymbolEventState` is capped by `Scanner:MaximumStateEvents` (default 10,000). Oldest event-time entries are removed and `HasEvictedEvents` marks completeness false. The state dictionaries are keyed by `(Symbol, SessionId)`. The fixture pipeline also retains a separate list of all input events for persistence; that collection is not bounded by the state cap.
- **Indexing:** In-memory sorted `List<MarketEvent>` with binary insertion and a `HashSet<string>` of identities; symbol/session isolation is enforced.
- **Windows:** `MomentumWindowSeconds` defaults to 60 seconds; `SecondaryMomentumWindowSeconds` defaults to 30 seconds and must be positive and shorter. The second ROC uses its own inclusive window; original return/volume and VWAP slope retain the first window. Session VWAP uses retained events in the explicit session.
- **`TimeProvider`:** Registered in Web and passed to live pipeline timing and both live adapters' receive timestamps. Feature/as-of analysis uses event time. Publication lifecycle accepts explicit evaluation time; fixture results are evaluated at event-time `AsOfUtc` to remain deterministic.
- **Event time vs receive time:** Distinct in domain and persistence. Quote age is based on quote event time relative to `AsOfUtc`; live-feed health uses receive time.
- **Future-event leakage:** `GetEventsAsOf` excludes events with event time after the requested as-of; replay and feature tests explicitly cover future-event exclusion.
- **Stale data:** Quote age compared with `MaximumQuoteAgeSeconds` (default 15); live receive age compared with `MaximumLiveFeedStalenessSeconds` (default 30) for feed status.
- **Feed lifecycle:** Live status progresses from Connecting to Live, then Stale by configured receive-age threshold; normal stream completion becomes Offline and exceptions become Error. Lifecycle gate suspends candidates for unhealthy statuses; there is still no reconnect/backfill or gap recovery.

## 8. Feature Engine Status

All implemented calculations are in `FeatureCalculator` (`SymbolEventState.cs`). There is no separate feature registry; all actual calculated values are strongly typed fields of `FeatureSnapshot`.

| Feature | Status | Inputs / output / horizon | Tests | Known limitations |
|---|---|---|---|---|
| Current/reference price | **IMPLEMENTED** | Last trade at/before as-of; decimal reference price. | Momentum and replay tests. | Does not fall back to quote midpoint. |
| Bid/ask spread | **IMPLEMENTED** | Latest quote at/before as-of; spread in basis points. | Invalid/excessive spread detector tests. | No size-weighted/microprice feature. |
| VWAP | **IMPLEMENTED** | Cumulative session trade price × size / total size within retained state. | Momentum/replay tests include VWAP values. | Not a bar-based VWAP; evicted earlier trades make session result incomplete. |
| Price relative to VWAP | **IMPLEMENTED** | Last trade vs VWAP, percent. | Momentum alignment tests. | No reclaim, rejection, or volatility normalization. |
| Session VWAP slope | **IMPLEMENTED, DESCRIPTIVE** | First-to-last cumulative VWAP sample change per elapsed event-time minute in primary momentum window; Rising/Falling/Flat/Insufficient. | Existing exact +1.5/-1.5/0/null slope tests preserved. | Requires two distinct timestamps; retained-session completeness applies. |
| EMA | **IMPLEMENTED, DESCRIPTIVE** | Fast/medium per-trade EMA (5/20 default), spread, and event-time slopes. | Exact SMA seeding, smoothing, periods, slopes, invalid prices, isolation and identity replay. | Non-positive eligible prices invalidate all EMA outputs; retained partial state is indicated separately. |
| Trend strength / trade-point ADX | **IMPLEMENTED, DESCRIPTIVE** | Wilder-smoothed trade-point DMI/ADX with configurable 14-movement default period; scalar trade prices only, no synthesized OHLC bars. | Exact rising/falling/flat/alternating paths, initialization/smoothing, warm-up, ordering, invalid inputs, duplicates, bounded eviction, completeness, identity, JSON and detector neutrality. | Nondirectional; values may reflect partial retained history; not used in setup qualification/scoring. |
| Trend direction context | **IMPLEMENTED, DESCRIPTIVE** | Fixed enum context from EMA spread/slopes, VWAP slope/direction and session price structure; ADX remains separate nondirectional strength. | Agreement, disagreement, zero/flat, missing/invalid, partial retention, ADX independence, identity, JSON, fixture HTTP/SQL and detector neutrality. | Reads only existing retained snapshot inputs; partial results must be read with `HasCompleteSessionData`; no qualification/scoring effect. |
| Trade-point realized volatility | **IMPLEMENTED, DESCRIPTIVE** | `SessionRealizedVolatilityPercent`: unannualized `100 * sqrt(sum(ln(P[i] / P[i-1])^2))` over all retained eligible trade prices; decimal percent rounded to 9 places. | Exact independent rising/falling/unchanged values; insufficient/invalid, equal-time ordering, duplicates, as-of cutoff, eviction/completeness, symbol/session, JSON, identity, fixture HTTP/SQL and detector neutrality. | Not bar-based ATR; partial retained data is not complete-session volatility; no volatility threshold or veto. |
| Momentum / rate of change | **IMPLEMENTED, LIMITED** | Original 60-second and descriptive secondary 30-second default ROC windows; first-to-last percentage change. Secondary ROC is null for fewer than two observations or any non-positive in-window price. | Exact +25%/-25%/0%, insufficient/invalid prices, canonical equal-time ordering, isolation, replay, configuration identity, future-event exclusion. | Two short horizons; no acceleration or new detector inputs. |
| RSI | **NOT IMPLEMENTED** | — | None. | — |
| MACD | **NOT IMPLEMENTED** | — | None. | — |
| Volume | **IMPLEMENTED, LIMITED** | Unchanged primary-window size sum; descriptive nullable `SessionCumulativeVolume` over all retained eligible session trades. | Exact sums, invalid/empty input, overflow, isolation, canonical replay, cutoff, eviction, identity, HTTP/SQL JSON. | Retained partial sum after eviction per `HasCompleteSessionData`; no dollar volume or price/volume divergence. |
| Relative volume | **NOT IMPLEMENTED** | — | None. | No historical time-of-day baseline. |
| Volume acceleration | **NOT IMPLEMENTED** | — | None. | — |
| ATR | **NOT IMPLEMENTED** | No OHLC bars; no bars are synthesized. | None. | — |
| Relative strength vs market | **NOT IMPLEMENTED** | — | None. | No benchmark feed/context. |
| Sector-relative strength | **NOT IMPLEMENTED** | — | None. | No sector classification or benchmark. |
| Session high/low and range position | **IMPLEMENTED (descriptive)** | `FeatureCalculator`, replay and hosted HTTP/SQL tests. | Nullable extrema and `(ReferencePrice - low) / (high - low) * 100m`; null on empty, invalid price/reference, or zero range. | Partial after any eviction; no breakout qualification or detector input. |
| Opening range | **NOT IMPLEMENTED** | — | None. | No exchange calendar/opening-range configuration. |
| Support/resistance structure | **NOT IMPLEMENTED** | — | None. | — |
| Breakout levels | **NOT IMPLEMENTED** | — | None. | — |
| Quote freshness | **IMPLEMENTED** | `AsOfUtc - latest quote EventTimeUtc`; quote validity and age; thresholds drive data quality and veto. | Stale quote test. | No source heartbeat or feed-gap semantics in the snapshot. |

Other snapshot fields include quote validity, trade-price validity, required-data availability, session completeness, and `DataQualityStatus`.

## 9. Milestone A Setup Detectors

| Detector | Status | Direction support | Conditions/evidence/scoring | Veto integration | Tests / limitations |
|---|---|---|---|---|---|
| Momentum Continuation | **IMPLEMENTED, LIMITED** | Long and short; neutral when directional return threshold is not met. | Requires a configured directional return for direction, then scores directional momentum, VWAP alignment, and any positive window volume. Evidence and contradictions are recorded. | Uses stale quote, invalid quote/price, excessive spread, insufficient/incomplete state, and no defensible invalidation checks. | Long/short, watch, veto, and replay tests exist. Price structure and ADX are descriptive only; no composite trend-strength, relative-strength, risk/reward, or liquidity scoring. |
| Confirmed Breakout / Breakdown | **NOT IMPLEMENTED** | — | — | — | No breakout levels, confirmation, or detector tests. |
| VWAP Reclaim / Rejection | **NOT IMPLEMENTED** | — | — | — | VWAP-side alignment is not a reclaim/rejection detector. |
| Failed Breakout / Reversal | **NOT IMPLEMENTED** | — | — | — | No prior breakout state, reversal tracking, or detector tests. |

## 10. Classification and Scoring

- The classification enum contains **Probable Long**, **Probable Short**, **Watch**, and **Avoid / No Trade**.
- In the one detector, any active veto yields `AvoidNoTrade`. Otherwise directional long/short with score at or above `ProbableScoreThreshold` (default 75) yields Probable Long/Short; score at or above `WatchScoreThreshold` (default 35) yields Watch; otherwise Avoid/No Trade.
- Scores add configured points for directional momentum (50), VWAP alignment (35), and positive window activity (15), then clamp to 0–100. The three configured weights must sum to 100. Contradictory evidence is described but **does not subtract points**.
- Thresholds, weights and both ROC windows are in `ScannerConfiguration` and `appsettings.json`; the effective configuration endpoint returns them. Version defaults are `scanner-config-v5`, `features-v5`, and unchanged `momentum-continuation-v1`.
- The UI explicitly labels it “Rules-based setup quality” and disclaims probability/recommendation. **The system does not present score as probability of profit.**
- Other constants include the 5-second live analysis interval and 5,000-item channel capacity in `ScannerPipeline`; the 1 MiB WebSocket message cap is in `MassiveMarketDataSource`; score clamp endpoints are 0 and 100. These operational/value-range constants are not currently configuration values. The scoring logic is intentionally simple but treats any positive window volume as participation support rather than relative/thresholded confirmation.

## 11. Risk and Data-Quality Gates

| Veto / requirement | Implemented? | Configuration / behavior | Test coverage |
|---|---|---|---|
| Stale/connecting/offline/error feed | **IMPLEMENTED as publication gate** | Application publication decision is `Suspended` unless live source status is `Live`; health changes reevaluate current results. This is separate from detector scoring/vetoes. | Deterministic tests cover stale transition, clean disconnect to Offline, and preservation of analytical evidence. |
| Stale quote | **IMPLEMENTED** | Quote age over configured 15 seconds or negative age adds `StaleQuote`. | Covered. |
| Invalid quote | **IMPLEMENTED** | Non-positive bid/ask or ask below bid adds `InvalidQuoteOrPrice`. | Covered. |
| Invalid price/trade data | **IMPLEMENTED** | Any retained trade with non-positive price/size sets invalid price data and detector vetoes. | Covered. |
| Excessive spread | **IMPLEMENTED** | Missing spread or above configured 25 bps adds `ExcessiveSpread`. | Covered. |
| Insufficient liquidity | **IMPLEMENTED, LIMITED** | `MinimumShortHorizonDollarVolumeUsd` defaults to 100,000 USD; complete, consistent available dollar volume strictly below it adds `InsufficientDollarVolume`. Partial available values skip only the comparison. The cutoff is provisional and not market-calibrated. | Focused replay/detector tests cover boundaries, invalid/unavailable/partial states, ordering, duplicates, isolation, and retention. |
| Insufficient feature data | **IMPLEMENTED, LIMITED** | Required trade/window/VWAP/quote data or complete state missing adds `InsufficientRequiredFeatureData`. | Covered. |
| Missing benchmark/context | **NOT IMPLEMENTED** | No benchmark/context input exists. | None. |
| Excessive volatility | **IMPLEMENTED** | Complete, consistent available volatility at or above the configured 1.00 percentage-point maximum adds `ExcessiveRealizedVolatility`; partial values skip comparison. The cutoff is provisional and not market-calibrated. | Focused detector tests cover thresholds, invalid/unavailable/partial states, and veto accumulation. |
| Poor reward/risk | **NOT IMPLEMENTED** | No target or reward/risk calculation. | None. |
| Opportunity expiration | **PARTIALLY IMPLEMENTED** | Detector sets `ValidUntilUtc`; publication gate returns `Expired` when evaluated after it. No persistent lifecycle history or background expiration worker. | Gate expiry and deterministic fixture-time behavior tested. |
| Structural invalidation | **PARTIALLY IMPLEMENTED** | Textual invalidation uses session VWAP for directional setup; neutral/no VWAP yields veto. No later price monitor changes opportunity status. | Text and no-invalidation behavior covered. |
| Data gap / recovering | **NOT IMPLEMENTED** | No gap state, backfill or recovering veto. | None. |

## 12. Opportunity Model

`Opportunity` contains immutable detector evidence. Separately, the application read model contains a current `PublicationDecision`:

| Field | Present? |
|---|---|
| Symbol / explicit session | Yes |
| `AsOfUtc`, `ValidUntilUtc` | Yes |
| Setup type | Yes, currently “Momentum Continuation” |
| Direction/bias and classification | Yes |
| Quality score (0–100) | Yes |
| Supporting evidence / contradictions / active vetoes | Yes |
| Reference price | Nullable decimal |
| Invalidation | Text condition |
| Targets | **No** |
| Feature-set version | Yes |
| Setup-definition version | Yes |
| Configuration version | Yes |
| Provider/feed/coverage/realtime/channel provenance | Yes |

Publication state is `Active`, `Suspended`, `Expired`, or `NotQualified`, with a structured status reason; it does not rewrite the analytical candidate, evidence, contradictions, or detector vetoes. IDs are deterministic hashes of input/configuration-derived identity. Identical replay preserves identity; changed inputs/configuration can create distinct records. `EfScannerStore` inserts only when the ID is absent and persists evidence separately. Configuration version values are immutable in the store: reusing a version with different JSON throws. Current publication decisions are process-memory projections, not append-only persisted lifecycle revisions; no later-price structural invalidation monitor exists.

## 13. Database Status

- **Engine/configuration:** SQL Server 2022 Developer container via `docker-compose.yml`; development connection string is `localhost,1433`. EF configured in `Program.cs` with SQL Server provider and retry-on-failure.
- **DbContext:** One `MarketOpportunityDbContext` in Infrastructure.
- **Entities/tables:** `market_event`, `feature_snapshot`, `opportunity`, `opportunity_evidence`, and `configuration_snapshot`.
- **Persistence:** `EfScannerStore` writes canonical events, JSON feature snapshots, opportunity attributes, evidence/contradictions/veto/invalidation, and immutable configuration JSON. It deduplicates by event identity and opportunity/snapshot IDs.
- **Migrations:** `20261023190000_AddExplicitSessionIdentity` adds non-null `SessionId` with `legacy-unknown` for existing rows and symbol/session/time indexes. `20261024232400_AddOpportunityMarketDataProvenance` adds provider/feed/coverage/realtime/channel columns using idempotent SQL for the development startup path. Both migration tests passed; persistence-test setup now also migrates existing test schemas.
- **Indexes:** Unique event identity; market event `(Symbol, SessionId, EventTimeUtc)`; feature snapshot `(Symbol, SessionId, AsOfUtc)`; opportunity `(Symbol, SessionId, AsOfUtc)`; evidence by opportunity.
- **Local config:** Docker SQL Server and Development configuration are present. README/provider docs describe User Secrets for Alpaca and Massive; credentials are absent from checked-in settings. A local-only SQL development password default is present in Docker Compose; it should not be reused outside disposable local development.
- **Azure SQL:** Provider/mappings are SQL Server compatible, but Azure SQL configuration, schema deployment, and verification are absent.
- **Intentionally absent logical tables:** No separate instrument, market session, trade/quote tables, aggregate bar, data gap, regime snapshot, agent assessment, opportunity outcome, paper observation, model registry/evaluation, system incident, or configuration audit tables. The single `market_event` table stores both trades and quotes. Outcomes/ML are future milestones.
- **Can fixture/event/opportunity data be persisted and retrieved?** SQL persistence/migration tests passed, and direct SQL queries matched the running fixture API's four opportunity IDs, four snapshot IDs, and 26 events. The API and dashboard **do not retrieve persisted history**; they serve current in-memory `ScannerState`. Persistence does not imply historical read capability.

## 14. Blazor Status

- **Hosting/render mode:** Blazor Web App, Interactive Server, `Routes` in `App.razor`; documented by ADR-001.
- **Pages/components:** Dashboard `Home.razor`, template `Counter`, `Weather`, `Error`, `NotFound`, routes, main layout/nav, reconnect modal.
- **Dashboard state:** Shows provider/feed and coverage capabilities, feed state, event time, symbol/session results, price, VWAP, spread, quote freshness/data quality, score, evidence, contradictions, analytical vetoes, invalidation, expiry, analysis versions, and publication status/reason.
- **Application integration:** Injects singleton `ScannerState` and `ScannerConfiguration`; subscribes to `ScannerState.Changed` and rerenders. Does not query EF directly.
- **SignalR/realtime:** Interactive Server circuits provide the Blazor connection. `ScannerState.Changed` updates connected UI circuits in-process. No separate application hub/events are implemented.
- **Errors/loading/data health:** Empty/loading state and feed/freshness labels are present. `ScannerRunnerService` records Error status and structured exception log if processing fails. Unhealthy live state suspends existing candidates and displays the reason; no gap-recovery state, benchmark health, or deep health dashboard.
- **Observed browser behavior (2026-10-25):** The local Fixture dashboard showed Recorded status, Synthetic feed/coverage, and all four symbol/session cards. The 2026-10-22 MOE long/QNT short results were Expired; the 2026-10-23 MOE short/QNT long results were Active with reasons. Provenance, evidence, vetoes, and versions were visible. Browser reload established a `/_blazor` WebSocket. This is a local fixture walkthrough, not live-provider/error-recovery coverage.

## 15. API Status

All routes are GET endpoints in `src/MarketOpportunity.Web/Program.cs`. The exact hosted fixture HTTP/SQL regression passed twice against the same database on 2026-10-25. A separate, earlier running-app/browser check verified health, status, list, all four details, and all four session-specific snapshots; that walkthrough was not repeated for the test-only slice.

| Method / path | Purpose | Implementation | Working / tested |
|---|---|---|---|
| `GET /health` | ASP.NET Core health response. | `AddHealthChecks` + `MapHealthChecks`; no database/provider-specific checks registered. | Local runtime returned Healthy. |
| `GET /api/v1/system/status` | Current feed/source/event status and capabilities. | Reads `ScannerState`. | Hosted test passed; local runtime reported Recorded / 26 events. |
| `GET /api/v1/opportunities` | Current opportunities sorted by as-of, with publication decision. | Reads in-memory results; not SQL. Response shape wraps analytical `Opportunity` with current publication status/reason. | Hosted test passed; local API IDs/provenance matched SQL. |
| `GET /api/v1/opportunities/{id}` | Current in-memory opportunity and publication decision. | Reads `ScannerState`, returns 404 if absent. | Hosted test passed; all four local detail responses returned 200 and matching IDs. |
| `GET /api/v1/symbols/{symbol}/snapshot` | Latest in-memory feature snapshot; optional `sessionId` query narrows session. | Reads `ScannerState`. | Hosted test passed; all four local session-specific responses returned 200 and SQL-matching IDs. |
| `GET /api/v1/configuration/effective` | Effective scanner configuration. | Returns singleton `ScannerConfiguration`. | Prior hosted HTTP test asserts the payload; current unit test checks defaults/serialization, and current hosted verification was not run. |

`GET /api/v1/market/regime`, `/api/v1/symbols/{symbol}/features`, and any standalone feature/history API are not present. No app-level SignalR hubs or named hub events are implemented.

## 16. Configuration System

`ScannerConfiguration` is bound from the `Scanner` section in `appsettings.json`, validated at startup, and returned by the effective-configuration endpoint.

| Parameter | Default | Location / notes |
|---|---:|---|
| Configuration version | `scanner-config-v15` | Adds `MinimumShortHorizonDollarVolumeUsd` = 100,000 USD; positive decimal; serialized in immutable `configuration_snapshot`. |
| Feature set version | `features-v14` | Adds nullable decimal `ShortHorizonDollarVolumeUsd` plus dedicated non-null status; retains all prior feature outputs. |
| Maximum session realized volatility | 1.00 percentage point | `MaximumSessionRealizedVolatilityPercent`; `> 0`, no upper cap; inclusive veto at/above on complete, consistent available values. Provisional, not market-calibrated. |
| Minimum short-horizon dollar volume | 100,000 USD | `MinimumShortHorizonDollarVolumeUsd`; positive decimal; strict-below veto on complete, consistent available values; equality passes. Provisional, not market-calibrated. |
| Structure pivot strength (fixed) | 2 observations on each side | Versioned feature rule, not a configurable property; two later eligible trades confirm a candidate. No elapsed-time threshold or tolerance. |
| Trend-strength period | 14 trade movements | `TrendStrengthPeriod`; positive integer; first ADX after 2 × N eligible price observations. |
| Setup definition version | `momentum-continuation-v1` | Scanner config and opportunity. |
| Maximum events per symbol/session | 10,000 | Bounds event state, not all pipeline collections. |
| Momentum window | 60 seconds | Short-horizon return/share-volume/dollar-volume and cumulative session VWAP/EMA-slope window. |
| Secondary momentum window | 30 seconds | `SecondaryMomentumWindowSeconds`; positive and strictly less than primary window. Descriptive `SecondaryHorizonReturnPercent` only. |
| Fast EMA period | 5 trades | `EmaFastPeriod`; positive observation count, strictly below medium period; SMA seed then alpha `2/(N+1)` per trade. |
| Medium EMA period | 20 trades | `EmaMediumPeriod`; positive observation count greater than fast period; SMA seed then alpha `2/(N+1)` per trade. |
| Maximum quote age | 15 seconds | Quote staleness veto. |
| Maximum live feed staleness | 30 seconds | Changes feed health; unhealthy live health suspends publication in the Application gate. |
| Maximum spread | 25 basis points | Spread veto. |
| Minimum directional return | 0.05% | Long/short direction threshold. |
| Minimum required trades | 3 | Data sufficiency. |
| Watch / Probable thresholds | 35 / 75 | Classification. |
| Opportunity duration | 120 seconds | Sets `ValidUntilUtc`; publication gate reports Expired at evaluation time, without a persisted lifecycle history/worker. |
| Momentum / VWAP / activity weights | 50 / 35 / 15 | Must sum to 100. |

Configuration snapshots are immutable per version in SQL. Feature/opportunity deterministic IDs include the versioned input. There is no external configuration administration UI or effective version history API beyond current settings. Other hard-coded analytical/operational values include the 5-second live analysis interval, channel capacity 5,000, and 1 MiB provider message cap; no dynamic session/calendar configuration exists.

## 17. Observability

| Capability | Status | Evidence / implementation |
|---|---|---|
| Structured logging | **PARTIALLY IMPLEMENTED** | Standard `ILogger` structured log in `ScannerRunnerService` for unexpected processing exception and completion counts. Default ASP.NET logging configuration exists. |
| OpenTelemetry tracing | **PARTIALLY IMPLEMENTED** | ASP.NET Core instrumentation only. |
| OpenTelemetry metrics | **PARTIALLY IMPLEMENTED** | ASP.NET Core and runtime instrumentation only. |
| OTLP export | **PARTIALLY IMPLEMENTED** | Conditional exporter if `OTEL_EXPORTER_OTLP_ENDPOINT` is set. |
| Scanner-specific metrics/spans | **NOT IMPLEMENTED** | No event, feature, scoring, veto, DB, channel, or UI spans/counters/histograms found. |
| Health checks | **PARTIALLY IMPLEMENTED** | `/health` uses default health checks; no SQL or feed health check registration. |
| Application Insights / Azure Monitor | **NOT IMPLEMENTED** | No Azure-specific integration/configuration found. |

No project-specific metrics or spans exist for the minimum observability list in the master specification.

## 18. Azure Readiness

| Capability | Status | Evidence / limitation |
|---|---|---|
| Bicep | **NOT IMPLEMENTED** | No Bicep artifacts. |
| Azure App Service | **NOT IMPLEMENTED** | No app, deployment profile, or hosting settings. |
| Azure SQL Database | **NOT IMPLEMENTED for deployment** | SQL Server provider is used, but only local SQL Server connection is configured. |
| Key Vault | **NOT IMPLEMENTED** | No provider/resource integration. |
| Application Insights | **NOT IMPLEMENTED** | No Azure exporter/SDK or resource configuration. |
| Azure Monitor | **NOT IMPLEMENTED** | No Azure Monitor setup. |
| Entra ID | **NOT IMPLEMENTED** | No authentication/authorization integration. |
| Azure DevOps Pipeline | **NOT IMPLEMENTED** | No Azure pipeline file. |

**Local-development readiness:** Partially prepared: .NET 10 solution, SQL Server Docker Compose service, development launch profiles, fixture default, and User Secrets instructions. A running SQL Server is required for web startup and SQL integration tests.
**Azure-deployment readiness:** Not ready; no infrastructure, hosted secret store, identity, deployment pipeline, Azure database configuration, or Azure observability implementation.

## 19. Architecture Decision Records

| ADR | Decision | Status / consequences |
|---|---|---|
| [ADR-001](docs/adr/ADR-001-blazor-render-mode.md) | Blazor Web App with Interactive Server; no separate app SignalR hub for current checkpoint. | Accepted. Server keeps analysis/credentials off browser; circuit transport is used. |
| [ADR-002](docs/adr/ADR-002-ef-sqlserver-conventions.md) | EF Core 10 and SQL Server; Infrastructure owns persistence; singular snake_case tables; development `EnsureCreated`, reviewed migrations for existing schema. | Accepted. Production migration orchestration remains follow-up. |
| [ADR-003](docs/adr/ADR-003-in-process-event-transport.md) | Fixture runs direct/in-process; use bounded `Channel<MarketEvent>` only for continuous producer/consumer. | Accepted. Live pipeline uses bounded channel with `Wait`; no distributed broker. |
| [ADR-004](docs/adr/ADR-004-market-event-retention.md) | Persist normalized trade/quote events; no raw vendor payload retention until reviewed. | Accepted. No raw-source reconstruction; retention/licensing duration still needs decision before live use beyond evaluation. |
| [ADR-005](docs/adr/ADR-005-market-data-provider-criteria.md) | Massive first live trade/quote provider; Alpaca approved alternative, not broker authorization. | Accepted. Fixture remains default; provider entitlements and terms must be confirmed. |

The requested render mode, EF/SQL Server, `Channel<T>`, retention, and provider-selection decisions are all documented.

## 20. Test Status

### Historical 2026-10-26 realized-volatility risk-veto evidence (superseded)

At that checkpoint, the focused replay/detector filter passed **145/145**, full offline units passed **197/197**, and the solution build succeeded with **0 warnings and 0 errors**. SQL persistence/migrations were **not run / unavailable** because the guarded environment variable was absent; hosted fixture, browser, live-provider, User Secrets, and Azure validation were not run. Current dollar-volume validation and its separate SQL gate status are recorded in the Latest Analysis Snapshot above.

The historical `scanner-config-v14` policy applied on every detector evaluation. Complete, consistent `Available` volatility values at or above 1.00 percentage point added `ExcessiveRealizedVolatility`; lower values did not. `Insufficient`/null added `VolatilityUnavailable`. `InvalidPriceData`, unknown statuses, negative values, and inconsistent value/status combinations added `InvalidVolatilityData`. For partial retained-session data, a valid available value was not threshold-compared and the incomplete-session veto remained. At that time, `features-v13` and `momentum-continuation-v1` were unchanged.

### Historical 2026-10-26 descriptive retained-session realized-volatility evidence (superseded)

The current result, exact command totals, SQL gate, failed attempts, and next-slice recommendation are recorded in the Latest Analysis Snapshot above. The focused replay suite passed **129/129**; the full offline suite passed **193/193** before and after the guarded SQL sequence; hosted fixture HTTP/SQL passed **1/1 twice** against the same database without reset; persistence/migrations passed **3/3**; and solution build succeeded with **0 warnings and 0 errors**.

The four fixture groups independently assert event-time-ordered trade-point volatility `0.199999477`, `0.231391437`, `0.367550138`, and `0.443762951` in HTTP decimal output with string `Available` status and raw persisted snapshot JSON with numeric status `0`. Partial-retention/invalid/null/status behavior, deterministic identity and JSON serialization, and byte-identical serialized detector output when the descriptive value/state varies are covered offline.

### Historical 2026-10-26 retained-session trend-direction evidence (superseded)

Current results and the SQL gate outcome are summarized in the latest analysis snapshot above. See that snapshot for exact passing totals and the separately recorded initial focused-test failures.

The output is non-null `TrendDirectionContext SessionTrendDirection`, serialized as `sessionTrendDirection`. Enum values are `Bullish`, `Bearish`, `Neutral`, `Conflicting`, `Insufficient`, and `Invalid`. Inputs are the signed fast/medium EMA spread and both signed EMA slopes; session VWAP slope and direction; session price structure; and existing ADX value/status. Positive EMA values vote bullish, negative bearish, and zero is neutral. VWAP Rising/Falling votes bullish/bearish, Flat is neutral, Insufficient contributes no vote. HigherHighHigherLow/LowerHighLowerLow vote bullish/bearish, Flat is neutral, Mixed forces conflict. ADX is validated as nondirectional strength; its numeric magnitude never votes.

Precedence is invalid/inconsistent supplied input → Invalid; any opposing bullish and bearish votes or Mixed structure → Conflicting; otherwise any one-sided vote → Bullish/Bearish; available directional inputs with only zero/flat signals → Neutral; no available directional inputs → Insufficient. Null EMA/slope, VWAP Insufficient, null structure and ADX Insufficient do not vote. Non-positive available EMA values, incomplete/inconsistent EMA inputs, VWAP enum/slope mismatch, unknown directional enums, invalid ADX status/value, and ADX InvalidPriceData return Invalid. If there is only partial signal availability, use available votes; neutral signals do not cancel directional votes.

This uses fixed sign and existing enum states, so there is no new configuration property or configurable threshold. Defaults advance `features-v11` / `scanner-config-v11` to `features-v12` / `scanner-config-v12`; `momentum-continuation-v1` remains unchanged. The enum is non-null in every snapshot and participates explicitly in deterministic identity, including all six states. HTTP uses the existing string-enum convention; persisted snapshot JSON uses its existing numeric enum encoding. `HasCompleteSessionData` does not change direction resolution; when false, the context describes retained partial features, not complete-session direction. Same-symbol/session and event-time cutoff are inherited from the existing `FeatureSnapshot` and `AsOfUtc`; no receive/wall-clock, ordering, duplicate, retention, or completeness semantics change.

Offline focused tests cover bullish/bearish agreement; EMA, VWAP and price-structure disagreements; flat and conflicting signals; ADX magnitude independence; no/mixed availability; invalid/mismatched states; complete versus retained-partial context; enum round trips; identity; and byte-identical serialized detector output as descriptive signals vary. The hosted fixture's exact independently-derived expected context is MOE/2026-10-22 Bullish, MOE/2026-10-23 Bearish, QNT/2026-10-22 Bearish and QNT/2026-10-23 Bullish. Hosted assertions read that enum from HTTP string JSON and raw persisted numeric JSON alongside existing provenance/completeness/identity checks. Persistence asserts original and changed input produce new immutable records without rewriting original snapshot/decision/evidence.

### Historical 2026-10-26 retained-session trade-point ADX evidence (superseded)

Added `SessionAdx` and `SessionAdxStatus` as descriptive outputs derived from bounded retained trades. The observation contract is one scalar trade price per canonical observation; no OHLC bars are synthesized. For adjacent prices, `TR=abs(P[t]-P[t-1])`, `up=P[t]-P[t-1]`, `down=P[t-1]-P[t]`; `+DM=up` only if `up>down && up>0`, `-DM=down` only if `down>up && down>0`, otherwise zero. Equal adjacent prices therefore have zero TR/DM.

`TrendStrengthPeriod` defaults to 14 trade movements and must be positive. Seed arithmetic means of TR/+DM/-DM from the first N movements; update with Wilder's `prior+(current-prior)/N`. Calculate `+DI=100*smoothed(+DM)/smoothed(TR)`, `-DI=100*smoothed(-DM)/smoothed(TR)`, and `DX=100*abs(plusDI-minusDI)/(plusDI+minusDI)`. Zero TR or a zero DI sum yields DX=0. Initial ADX is the mean of the first N DX values, then uses the same Wilder recurrence. All math uses decimal. The first value requires 2×N eligible price observations. `SessionAdx` is nullable decimal; no eligible history or fewer than 2×N trades yields null/`Insufficient`; any eligible non-positive price yields null/`InvalidPriceData`. Available values are nondirectional 0–100 strength.

Only retained same-symbol, explicit-session `TradeEvent`s with `EventTimeUtc <= AsOfUtc` contribute, in canonical ordering; no elapsed-time interval is applied. Quotes, sizes, receive time and wall-clock time are excluded. Equal-time trades count separately in canonical order. `HasCompleteSessionData=false` does not null a warmed-up value but marks it as retained-partial. Future arrivals can evict eligible retained history and change a historical-as-of output. Null/value and status explicitly participate in deterministic feature identity; the positive period is in serialized versioned configuration. Versions are `features-v11` / `scanner-config-v11`; setup remains unchanged at `momentum-continuation-v1`. No detector, scoring, veto, publication, UI, provider, or prior feature behavior changed.

Replay coverage adds exact rising/falling/flat/non-directional and smoothing paths; period-2 and default-period initialization; size/quote and invalid-price cases; inclusive AsOf and old retained trades; equal timestamps/canonical ordering; receive-time changes; duplicate/conflicting first occurrence; symbol/session isolation; eviction/completeness; JSON null/value and identity; positive-period validation; and byte-identical serialized opportunity output as ADX value/status varies. Hosted fixture observations are independently 4–7 trades per group, below 28-observation default warm-up; HTTP has `sessionAdx:null`/`Insufficient`, and raw SQL JSON has null/value status 1 for all four groups. Persistence tests assert original ADX 100 and changed-input ADX 57.14285714285714285714285714, while asserting changed inputs create distinct immutable snapshots/opportunities and do not rewrite the original snapshot, decision, or evidence.

**Passing validation (all runs 0 failures/0 skips):**

| Order | Exact command | Result |
|---|---|---|
| Offline focused | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj --filter FullyQualifiedName~ScannerReplayTests` | **119 passed, 0 failed, 0 skipped** |
| Offline full, before SQL | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj` | **183 passed, 0 failed, 0 skipped** |
| Hosted fixture 1 | `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter FullyQualifiedName~FixtureWebApplicationTests` | **1 passed, 0 failed, 0 skipped** |
| Hosted fixture 2 | Same command on same database without reset | **1 passed, 0 failed, 0 skipped** |
| Persistence/migrations | `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter "FullyQualifiedName~SqlServerPersistenceTests|FullyQualifiedName~ExplicitSessionMigrationTests"` | **3 passed, 0 failed, 0 skipped** |
| Offline full, after SQL | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj` | **183 passed, 0 failed, 0 skipped** |
| Solution build | `dotnet build MarketOpportunityEngine.sln` | **0 warnings, 0 errors** |

SQL guard: `MARKETOPPORTUNITY_SQL_TEST_CONNECTION` was absent. The existing Compose SQL Server was already healthy and the existing dedicated `MarketOpportunityEngine_IntegrationTests` database was verified with a read-only query. A process-only connection was built from the container credential in memory and guarded by `SqlConnectionStringBuilder`; no SqlClient assembly was loaded in the parent process launching `dotnet`. Both hosted runs used the same database without reset; fixture rows were retained, no configured database was dropped, and migration tests used only unique temporary databases. Temporary overrides and credential references were cleared. Browser/live-provider/User Secrets/Azure checks were not run.

**Failed attempts, not passing evidence:** Initial focused compilation required adding the new positional fields to a test fixture. Two later focused assertions had an incorrect decimal expectation and an identity test with unintended VWAP change; both were fixed before 119/119 passed. The first persistence/migration execution had **2 passed / 1 failed** due to the final decimal rounding digit; corrected expected output then passed 3/3. A separate PowerShell child SqlClient guard failed with `Microsoft.Data.SqlClient is not supported on this platform` before tests, and one runner orchestration attempt stopped after preflight without launching tests. These failures are runner/expectation issues, not evidence of SQL unavailability; the final .NET tests passed their in-process connection-string guard.

### Historical 2026-10-26 retained-session price-structure evidence

Nine replay cases were added for the fixed two-left/two-right pivot rule, rising/falling/mixed/flat structure, confirmation and warm-up, equal-price selection and exact decimal comparisons, equal-time canonical ordering, size/quote/invalid-price eligibility, `AsOfUtc`, duplicates/receive-time independence, symbol/session isolation, eviction/completeness, JSON null/value round trips, deterministic identity, and detector neutrality. The existing replay assertions remain intact.

**Passing validation (0 failures/0 skips):**

| Order | Exact command | Result |
|---|---|---|
| Offline focused | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj --filter FullyQualifiedName~ScannerReplayTests` | **107 passed, 0 failed, 0 skipped** |
| Offline full | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj` | **171 passed, 0 failed, 0 skipped** |
| Hosted fixture 1 | `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter FullyQualifiedName~FixtureWebApplicationTests` | **1 passed, 0 failed, 0 skipped** |
| Hosted fixture 2 | Same command on the same database without reset | **1 passed, 0 failed, 0 skipped** |
| SQL persistence/migrations | `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter "FullyQualifiedName~SqlServerPersistenceTests|FullyQualifiedName~ExplicitSessionMigrationTests"` | **3 passed, 0 failed, 0 skipped** |
| Post-SQL full units | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj` | **171 passed, 0 failed, 0 skipped** |
| Solution build | `dotnet build MarketOpportunityEngine.sln` | **Succeeded, 0 warnings, 0 errors** |

The SQL connection override was absent. The existing healthy Compose SQL Server and already-existing dedicated `MarketOpportunityEngine_IntegrationTests` database were used. A read-only container-local query verified the database, while an isolated encoded PowerShell 7 child loaded the built Windows runtime SqlClient assembly via `Assembly.LoadFrom` and validated/built the test connection with `SqlConnectionStringBuilder` indexers. The SqlClient runtime was not loaded into the PowerShell child launching `dotnet`. Temporary credentials/overrides stayed in process memory and were cleared; fixture records were retained; no database was reset/dropped and no service was started/stopped. Migration tests used unique temporary databases only.

**Failed attempts, not passing evidence:** Early focused attempts exposed invalid decimal `InlineData`, a positional `FeatureSnapshot` constructor missing the new field, incorrect swing paths, incomplete identity assertions, and a missing JSON converter using; they were corrected before the final 107/107 run. Pre-test SQL guards failed at different setup stages (PowerShell type/assembly resolution, script parsing, and `DllNotFoundException` opening SqlConnection from PowerShell). None launched the primary SQL test sequence. The successful primary gate used builder-only validation plus the read-only container-local database query. A later redundant persistence/migration rerun reached the test runner but failed **0/3** because its temporary override caused `sa` login failures; the read-only database query still passed. This is a runner/credential setup failure, not SQL-unavailability evidence, and is separate from the passing primary sequence. No browser, live-provider, User Secrets, or Azure verification was run.

### Historical 2026-10-25 descriptive retained-trade EMA evidence

Added nullable `FastEma`, `MediumEma`, `FastMediumEmaSpreadPercent`, `FastEmaSlopePerMinute`, and `MediumEmaSlopePerMinute` to `FeatureSnapshot`; calculations use the existing bounded `SymbolEventState` and its canonical ordering. Only retained same-symbol/explicit-session trades at or before `AsOfUtc` contribute. EMA updates once per observation, independent of size and elapsed time. Periods default to fast 5 and medium 20 trades, are positive integers, and require fast < medium. The first value is the SMA seed of N positive trade prices; subsequent values use `price * alpha + priorEMA * (1-alpha)`, where `alpha=2/(N+1)`. Fewer than N observations yields null. Any eligible non-positive price makes every EMA output null; quotes do not contribute.

Spread is `(fast-medium)/medium*100m`, null until both EMA values exist. Each slope is the signed EMA change per elapsed event-time minute across the first and last initialized timestamp samples in the inclusive `[AsOfUtc-MomentumWindowSeconds, AsOfUtc]` interval (60 seconds by default); all trades at one timestamp update in canonical order, and the last canonical EMA at that timestamp is the sample. Earlier trades can warm up an EMA outside the slope interval. Fewer than two distinct timestamp samples yields null; equal endpoint values yield zero. Read outputs with `HasCompleteSessionData`; false means non-null values may describe only retained partial state. Future observations can affect an as-of value if they evict eligible retained state.

All five outputs use nullable decimal JSON fields `fastEma`, `mediumEma`, `fastMediumEmaSpreadPercent`, `fastEmaSlopePerMinute`, and `mediumEmaSlopePerMinute`, including explicit JSON nulls; each value is explicitly included in deterministic feature identity. Configuration/version advance from `features-v8` / `scanner-config-v8` to **features-v9 / scanner-config-v9**; setup remains **momentum-continuation-v1**. No detector, opportunity, scoring, qualification, classification, publication, veto, UI, provider, schema, or `App.razor` behavior changed.

**Passing validation (0 failed/0 skipped):**

| Order | Exact command | Result |
|---|---|---|
| Offline 1 | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj --filter FullyQualifiedName~ScannerReplayTests` | **98 passed, 0 failed, 0 skipped** |
| SQL 1 | `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter FullyQualifiedName~FixtureWebApplicationTests` | **1 passed, 0 failed, 0 skipped** |
| SQL 2 | Same hosted fixture command, same database without reset | **1 passed, 0 failed, 0 skipped** |
| SQL 3 | `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter "FullyQualifiedName~SqlServerPersistenceTests|FullyQualifiedName~ExplicitSessionMigrationTests"` | **3 passed, 0 failed, 0 skipped** |
| Final units | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj` | **162 passed, 0 failed, 0 skipped** |
| Build | `dotnet build MarketOpportunityEngine.sln` | **Succeeded, 0 warnings, 0 errors** |

Hosted HTTP and raw persisted SQL JSON assert exact independently-derived fixture values: MOE / fixture-2026-10-22 fast EMA **150.218** from prices `149.90, 150.08, 150.23, 150.40, 150.48`; QNT / fixture-2026-10-23 fast EMA **80.176** from `79.80, 80.00, 80.20, 80.40, 80.48`. Other fixture fast values are null; all medium, spread, and slope values are null because fewer than 20 trades and fewer than two initialized fast samples exist in the slope interval. The SQL persistence test confirms new input creates a new snapshot/decision with fast EMA **102.1** and slope **12 price/minute**, without rewriting the original records.

The connection variable was absent. The already-running healthy Compose SQL Server and only its dedicated `MarketOpportunityEngine_IntegrationTests` database were reused; a read-only `sqlcmd` query verified the database, and an isolated encoded PowerShell 7 child validated the catalog with the built Windows runtime `SqlConnectionStringBuilder` via `Assembly.LoadFrom` and indexer setters. The parent launching `dotnet` did not load SqlClient. Process-only overrides were cleared; fixture records and the configured test database were not reset/dropped; migration tests used unique temporary databases. Browser/live-provider/User Secrets/Azure checks were not run.

**Failed attempts, not passing evidence:** Initial focused runs exposed a nullable conditional compile error, a positional test fixture missing fields, and incorrect EMA test expectations/session helper inputs; all were corrected before the passing focused run. SQL guard-runner attempts involving encoded command construction and a failed login stopped before SQL tests; the successful subsequent guard used the existing healthy database, and the required complete SQL sequence passed. These unsuccessful guard attempts are not classified as SQL test failures or evidence the server was unavailable.

### 2026-10-25 descriptive retained-session range-position evidence (historical)

Added nullable decimal `SessionRangePositionPercent` to the existing `FeatureSnapshot` and `FeatureCalculator`. Formula: `(ReferencePrice - SessionLowPrice) / (SessionHighPrice - SessionLowPrice) * 100m`, using decimal arithmetic and only retained same-symbol/explicit-session trades with `EventTimeUtc <= AsOfUtc`; the reference remains the latest eligible retained trade in existing deterministic order. The existing extrema are reused; no additional state or eligibility path was introduced. Values are not clamped or rounded and do not use either ROC window.

Empty state, unavailable/non-positive reference, unavailable extrema, and zero-width range return null. One trade and equal-price trades therefore return null; the feature does not require `MinimumRequiredTrades`. Any eligible non-positive price nulls extrema and range position even when outside both ROC windows. Sizes neither weight nor filter the range: zero/negative-size trades with positive prices still contribute to extrema/reference/position. Existing `IsPriceDataValid`, data quality, volume, detector, veto, publication, rolling volume, ROC, and exact VWAP-slope behavior remain unchanged. Read position with sticky `HasCompleteSessionData`; false means retained partial-session range, while true does not prove upstream session-start coverage. Quote eviction remains sticky; future arrivals may evict earlier state.

`sessionRangePositionPercent` is serialized for both values and null and explicitly participates in deterministic feature identity. Defaults advance from `features-v7` / `scanner-config-v7` to **features-v8 / scanner-config-v8**; `momentum-continuation-v1` remains unchanged. Prior configurations, snapshots, and decisions remain immutable; SQL persistence verifies a later input creates a distinct snapshot/decision without changing the original snapshot JSON, opportunity fields, or evidence. No schema migration was required.

**New successful validation (all test runs passed, 0 failures/skips):**

| Order | Exact command | Result |
|---|---|---|
| Offline 1 | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj --filter FullyQualifiedName~ScannerReplayTests` | **83 passed, 0 failed, 0 skipped** |
| Offline 2 | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj` | **147 passed, 0 failed, 0 skipped** |
| SQL 1 | `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter FullyQualifiedName~FixtureWebApplicationTests` | **1 passed, 0 failed, 0 skipped** |
| SQL 2 | Same hosted fixture command | **1 passed, 0 failed, 0 skipped**, same database and no reset |
| SQL 3 | `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter "FullyQualifiedName~SqlServerPersistenceTests|FullyQualifiedName~ExplicitSessionMigrationTests"` | **3 passed, 0 failed, 0 skipped** |
| Final units | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj` | **147 passed, 0 failed, 0 skipped** after the final replay test was added |
| Build | `dotnet build MarketOpportunityEngine.sln` | **Succeeded, 0 warnings, 0 errors** |

The hosted tests assert exact position in both HTTP snapshot payloads and raw persisted SQL snapshot JSON. Independently derived fixture expectations: MOE-22 **100**, MOE-23 **0**, QNT-22 **0**, QNT-23 **100**, with retained reference/extrema values and `HasCompleteSessionData=true`. Replay coverage includes low/high/interior, rising/falling, extrema outside both ROC windows, empty/single/equal/below-minimum-trade ranges, invalid prices inside/outside windows, positive prices with invalid sizes including the latest reference, quote exclusion, isolation, canonical first duplicates/conflicting payloads, reordered/equal-time/receive-time replay, inclusive cutoff/future extreme and invalid exclusion, eviction/partial/null/sticky quote completeness/future-arrival eviction, identity mutation and JSON round trips, and detector output invariance at null/0/25/100. All prior exact volume, extrema, ROC, VWAP, payload, negative-lookup, canonical-event, configuration-version, and immutability assertions remain.

The SQL connection variable was absent. The pre-existing Compose SQL Server was confirmed healthy, and only its dedicated `MarketOpportunityEngine_IntegrationTests` database was used. A PowerShell 7 encoded child loaded the built Windows runtime `Microsoft.Data.SqlClient` assembly via `Assembly.LoadFrom` to validate `InitialCatalog` with `SqlConnectionStringBuilder`; the isolated child returned no connection text, and the parent launching `dotnet` loaded no SQL-client runtime assembly. Temporary process overrides/password references were cleared; fixture rows and the configured test DB were retained; no service was started/stopped/reset and the configured DB was not dropped. Migration tests used only uniquely named temporary databases.

**Failed attempts, not passing evidence:** Initial focused replay run was **80 passed / 2 failed** due to the test expecting 25 instead of the exact 50 midpoint for range 100..150, and a version-change test reusing the new `scanner-config-v8` default. The expected value and changed test version were corrected; the successful 83-case focused run followed. Guard setup attempts that loaded the root SQL-client assembly, passed the DLL path as a script, or assigned an unsupported builder property failed before SQL tests ran. The successful isolated PowerShell 7 guard correctly invoked an encoded child, loaded the built Windows runtime assembly using `Assembly.LoadFrom`, and populated `SqlConnectionStringBuilder` through its indexer. These were runner/guard setup failures, not evidence of SQL unavailability. No browser, live-provider, User Secrets, or Azure verification was run.

### 2026-10-25 descriptive retained-session high/low evidence (historical)

Versions advance from `features-v6` / `scanner-config-v6` to **features-v7 / scanner-config-v7**; **momentum-continuation-v1** is unchanged. The exact extrema contract is at the top of this report and in README. Both nullable decimal fields are serialized with existing web naming and explicitly included in the deterministic feature-value identity. No persistence schema/ADR decision changes were needed.

**New successful validation, in execution order (all exit 0):**

| Order | Exact command | New result |
|---|---|---|
| Offline 1 | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj --filter FullyQualifiedName~ScannerReplayTests` | **72 passed, 0 failed, 0 skipped** |
| Offline 2 | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj` | **136 passed, 0 failed, 0 skipped** |
| SQL 1 | `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter FullyQualifiedName~FixtureWebApplicationTests` | **1 passed, 0 failed, 0 skipped** |
| SQL 2 | `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter FullyQualifiedName~FixtureWebApplicationTests` | **1 passed, 0 failed, 0 skipped**, same database, no reset |
| SQL 3 | `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter "FullyQualifiedName~SqlServerPersistenceTests|FullyQualifiedName~ExplicitSessionMigrationTests"` | **3 passed, 0 failed, 0 skipped** |
| Final units | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj` | **136 passed, 0 failed, 0 skipped** |
| Final build | `dotnet build MarketOpportunityEngine.sln` | **Succeeded, 0 warnings, 0 errors** |

The 26 added replay cases cover extrema across all retained trades outside/inside both windows; empty/single/equal prices below the detector minimum; zero/negative first/intermediate/last prices inside and outside both windows; positive prices with invalid sizes retaining extrema but preserving vetoes; quote exclusion; symbol/explicit-session isolation; canonical first duplicate with conflicting payloads; reordered/equal-time input and receive-time independence for stable IDs; inclusive cutoff with future extreme/invalid prices; prior-extrema eviction, partial/empty/invalid retained results, conservative sticky quote eviction, and future arrivals evicting historical state. Serialization/exact identity assertions now independently mutate each new field (including null representation), preserving the explicit volume identity assertion. Detector comparisons vary each extrema field independently, including null, and compare the full serialized opportunity output.

The exact fixture constants at the top were inspected from trade prices, not generated solely by the calculator under test. Hosted HTTP and raw persisted SQL snapshot JSON both assert them alongside all exact prior volume/completeness checks. Existing four positive snapshot/detail/list payloads, four absent-resource `404`s, 26 canonical-event identities/fields, effective configuration, analytical evidence/provenance, version and immutable configuration/snapshot assertions remain in place. Original SQL extrema **103/100** remain unchanged after later input generates **103.50/100**. The volume assertions **100 then 800**, both ROC horizons/validation and exact VWAP-slope assertions were preserved, not replaced or weakened.

**Safety/cleanup:** The connection variable was absent. Used the request-authorized existing healthy Compose SQL Server and dedicated `MarketOpportunityEngine_IntegrationTests`, plus only migration tests' unique temporary databases. Credentials were captured and the connection built in process memory. An isolated encoded PowerShell 7 child used `Assembly.LoadFrom` and `Microsoft.Data.SqlClient.SqlConnectionStringBuilder` to require non-empty `InitialCatalog` containing `test` case-insensitively. No SQL-client runtime was loaded into the parent launching `dotnet`; no SQL connection was opened from PowerShell. Temporary overrides/credential references were cleared. No helper/task files were needed. No server or configured test database was started/stopped/reset/dropped; fixture rows were retained. Hosted factories disposed; pre-existing container remained running/healthy.

**Failed attempt, not passing evidence:** Initial focused run: **66 passed, 6 failed, 0 skipped** (72 total). Six theory rows used incorrect test-only expected volume **130** instead of **160**; older trade sizes were 10+20+30, plus existing volume 100. Corrected that expected constant only, then ran the complete successful sequence above. No SQL/assembly/runner failures occurred in this slice. Prior runner/native-SNI failures below remain historical.

**Unavailable/not-run:** No remaining SQL verification blocker. No new browser walkthrough, live-provider connection, provider User Secrets or Azure evidence. App UI and `App.razor` unchanged. Full Milestone A remains incomplete.

### Earlier 2026-10-25 descriptive session cumulative-volume evidence (historical)

**Contract:** `SessionCumulativeVolume` is nullable `long`, summing only retained trades for the state's symbol/explicit session with `EventTimeUtc <= AsOfUtc`, including trades outside both ROC horizons. No trades or any non-positive size yields null, not zero; one valid size is sufficient regardless of `MinimumRequiredTrades`. No trade is silently filtered for invalid size. Positive sizes remain summable even with invalid prices, while existing `IsPriceDataValid`/quality/veto behavior remains unchanged. Quotes and receive/system time never supply volume. Existing checked long summation throws `OverflowException`, never wrapping/clamping. `ShortHorizonVolume` is unchanged.

`HasCompleteSessionData` is reused, not redefined: no eviction means the non-null sum covers the observed retained session; any eviction makes a non-null sum partial. The flag is sticky/conservative even for quote eviction, not proof of upstream session-start coverage. No eligible retained trades or invalid sizes still yields null. Future events cannot affect prior values/identity if retained eligible events/completeness are unchanged; future arrivals can evict prior events, so bounded state cannot reproduce arbitrarily old snapshots. There is no backfill.

Versions advance from `features-v5` / `scanner-config-v5` to **`features-v6` / `scanner-config-v6`**. The setup stays **`momentum-continuation-v1`**. Volume participates explicitly in deterministic feature-value identity and JSON serialization. Existing configuration immutability/version checks, retained canonical first-occurrence deduplication/order, both ROC horizons/validation, exact VWAP-slope tests, detector/publication/veto behavior, SQL/migration assertions, and all hosted positive/negative lookups remain intact. No schema, dependency, UI (including `App.razor`), provider, or infrastructure change.

**New successful validation, in order:**

| Order | Exact command | New result |
|---|---|---|
| Offline 1 | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj --filter FullyQualifiedName~ScannerReplayTests` | **46 passed, 0 failed, 0 skipped**, exit 0. |
| Offline 2 | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj` | **110 passed, 0 failed, 0 skipped**, exit 0. |
| SQL 1 | `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter FullyQualifiedName~FixtureWebApplicationTests` | **1 passed, 0 failed, 0 skipped**. |
| SQL 2 | `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter FullyQualifiedName~FixtureWebApplicationTests` | **1 passed, 0 failed, 0 skipped**, same database without reset. |
| SQL 3 | `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter "FullyQualifiedName~SqlServerPersistenceTests|FullyQualifiedName~ExplicitSessionMigrationTests"` | **3 passed, 0 failed, 0 skipped**. |
| Final units | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj` | **110 passed, 0 failed, 0 skipped**. |
| Final build | `dotnet build MarketOpportunityEngine.sln` | **Succeeded; no warnings/errors reported**. |

The final sequence completed successfully with fail-fast exit-code checks. Sixteen added replay cases assert session accumulation outside both windows; unchanged rolling volume; 0/1/2-trade input; zero/negative sizes; positive sizes with invalid prices; exact `long.MaxValue` and overflow; quote exclusion; symbol/session isolation; duplicates, equal timestamps, reordered input and changed receive times; inclusive cutoff and excluded invalid/overflowing future sizes; partial/unavailable retained sums after eviction; bounded historical-replay limits; raw snapshot serialization/exact identity contribution; and unchanged detector output. Prior slope and ROC exact assertions remain.

Hosted HTTP snapshots and raw persisted SQL JSON assert **5600 / 5450 / 5050 / 5650** shares for MOE-22/MOE-23/QNT-22/QNT-23, all with complete retained state. Four snapshot/opportunity identities and payloads, all 26 canonical events, classifications/publication/evidence/provenance, effective configuration, and four exact absent-resource `404`s still pass on both runs. SQL persistence additionally asserts original volume **100** remains unchanged after later input produces **800**; same-version configuration changes remain rejected.

**Safety/cleanup:** The environment variable was absent. Reused the pre-existing healthy Compose SQL Server, targeting only `MarketOpportunityEngine_IntegrationTests` (plus migration tests' uniquely named temporary databases). A PowerShell 7 child loaded the SQL-client runtime using `Assembly.LoadFrom` and validated the non-empty case-insensitive `test` catalog via `Microsoft.Data.SqlClient.SqlConnectionStringBuilder` before any SQL-backed test. No SQL connection was opened from PowerShell and no SQL runtime assembly was loaded into the parent launching `dotnet`. Credentials stayed in process memory, were not printed/saved, and temporary overrides were cleared/restored. No server was started/stopped or configured database dropped/reset; fixture rows were retained. Final status: `sqlserver running healthy`. Hosted test factories disposed. Temporary VS Code task and session scripts were removed.

**Failed attempts in this slice, not passing validation evidence:** First Windows PowerShell child `Add-Type` guard failed dependency resolution (`System.Memory`, `Microsoft.Identity.Client`, `Azure.Core`); it opened no SQL connection and no tests ran. Subsequent terminal-runner calls returned `program not found`, including a trivial shell probe. Recovery ran the same documented sequence through a temporary VS Code task and isolated PowerShell 7 child. These were runner/assembly failures, not evidence SQL Server was unavailable.

**Unavailable/not-run boundaries:** No remaining SQL verification blocker. No new browser walkthrough, live-provider connection/User Secrets, or Azure evidence. Full Milestone A remains incomplete. Historical results/failures below are preserved and are not new runs.

### Earlier 2026-10-25 second short-horizon ROC evidence (historical)

Added `SecondaryHorizonReturnPercent` to `features-v5` with `scanner-config-v5`. `SecondaryMomentumWindowSeconds` defaults to 30 and must satisfy `0 < secondary < MomentumWindowSeconds` (default 60). Eligible trades belong to the same symbol and explicit session and fall in the inclusive event-time window ending at `AsOfUtc`. The first and last trades in existing canonical order yield `(last price - first price) / first price * 100` in decimal arithmetic. Fewer than two observations or any non-positive in-window price yields null; flat prices yield zero. Equal timestamps count as separate observations ordered by provider, ordinal sequence and canonical identity. Receive/system time does not determine the value. The original ROC, VWAP-slope algorithm/exact assertions, and all detector/publication/veto logic are unchanged.

The added window is serialized in configuration identity and exposed through the existing effective-configuration API. The new value is included in deterministic feature identity. SQL persistence continues to reject changed configuration values under the same version; the extended SQL test checks rejection for this specific window and compares the retained original configuration/snapshot. No old decision is rewritten and no schema migration is needed.

**Successful primary validation, in execution order (all exit 0):**

| Order | Exact command | New result |
|---|---|---|
| Offline 1 | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj --filter FullyQualifiedName~ScannerReplayTests` | **30 passed, 0 failed, 0 skipped**. |
| Offline 2 | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj` | **94 passed, 0 failed, 0 skipped**. |
| SQL 1 | `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter FullyQualifiedName~FixtureWebApplicationTests` | **1 passed, 0 failed, 0 skipped**. |
| SQL 2 | `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter FullyQualifiedName~FixtureWebApplicationTests` | **1 passed, 0 failed, 0 skipped**, same database without reset. |
| SQL 3 | `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter "FullyQualifiedName~SqlServerPersistenceTests|FullyQualifiedName~ExplicitSessionMigrationTests"` | **3 passed, 0 failed, 0 skipped**. |
| Final units | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj` | **94 passed, 0 failed, 0 skipped**. |
| Final build | `dotnet build MarketOpportunityEngine.sln` | **Succeeded; 0 warnings, 0 errors**. |

The 22 added replay cases cover exact rising **+25%**, falling **-25%**, flat **0%**, inclusive lower/upper boundaries, quote exclusion, non-positive first/intermediate/last prices, fewer than two in-window observations, same-time canonical ordering, changed receive times, reordered input, symbol/session isolation, configuration validation and deterministic identity, and descriptive-only detector behavior. Existing golden replay, duplicate, future-event exclusion and exact VWAP-slope tests remain intact. Changing only the new window also verifies unchanged primary ROC/slope and detector outcomes.

Hosted tests retain all four valid detail/snapshot/list comparisons, all four exact missing-resource `404`s, expected classifications/publication reasons, all four opportunity/snapshot SQL payloads, and all 26 canonical event identities and fields. The hosted effective-configuration assertion is additional, not a replacement.

**Safety and cleanup:** `MARKETOPPORTUNITY_SQL_TEST_CONNECTION` was absent. This request authorized fallback to the existing healthy Compose SQL Server; it was not started/stopped/replaced. The process-only connection targeted `MarketOpportunityEngine_IntegrationTests`, with a non-empty case-insensitive `test` catalog guard using `Microsoft.Data.SqlClient.SqlConnectionStringBuilder` before SQL tests. Credentials were captured in memory from that container, never printed/saved. Final guard ran in a child PowerShell; tests ran under their normal .NET runtime. Fixture rows were retained, no configured database was dropped, and migration tests used only uniquely named temporary databases. Environment overrides and temporary credential references were cleared; the temporary validation helper was removed. No provider User Secrets, Alpaca/Massive connectivity, or separate web process was used.

**Earlier attempts and diagnostics in this slice (not successful SQL/build evidence):**

- A PowerShell SQL-open preflight failed `TdsParser` initialization because native `Microsoft.Data.SqlClient.SNI.dll` could not load. It reached no tests and made no database changes.
- A subsequent in-process validation wrapper returned **-2147450751** without test output for the first hosted command and attempted unit/build commands; hosted repetition and SQL persistence/migrations did not run in that attempt. A separate `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj --no-restore --list-tests` then exited 0, but discovery is not a passing test run.
- A diagnostic `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj --no-restore` passed **94/94**, exit 0. That diagnostic attempt did not reach SQL/build before its execution limit. It is separate from the complete ordered run above.
- The final sequence isolated the builder guard in a child PowerShell and invoked the documented commands directly; every command passed. The wrapper failure's precise cause was not established; it was not evidence that SQL Server was unavailable.

**Unavailable/not-run boundaries:** No SQL verification blocker remains. No new browser/live-provider/Azure evidence is claimed; earlier browser and VWAP-slope results below are historical. Full Milestone A remains incomplete.

### Earlier 2026-10-25 session VWAP-slope feature evidence (historical)

Added event-time-derived session VWAP slope to the existing feature snapshot. For the existing `MomentumWindowSeconds`, the calculator groups eligible trades by distinct event timestamp, tracks cumulative session VWAP through each timestamp, and computes `(last cumulative VWAP - first cumulative VWAP) / elapsed minutes`. Actual event-time spacing handles irregular intervals. Fewer than two distinct in-window timestamps with positive cumulative volume yields null slope and `Insufficient`; exactly zero is `Flat`, positive is `Rising`, negative is `Falling`. No new threshold or config parameter was introduced. The feature is descriptive only and does not change setup qualification, score, publication, or vetoes.

The snapshot and scanner configuration versions are now `features-v4` and `scanner-config-v4`. Existing persisted decision rows are immutable; this calculation creates new deterministic feature/opportunity identities. No schema migration, dependency, or provider change was needed.

| Order | Exact command | New final result |
|---|---|---|
| Focused | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj --filter FullyQualifiedName~ScannerReplayTests` | Exit **0**; **8 passed, 0 failed, 0 skipped**. |
| 1 | `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter FullyQualifiedName~FixtureWebApplicationTests` | Exit **0**; **1 passed, 0 failed, 0 skipped**. |
| 2 | `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter FullyQualifiedName~FixtureWebApplicationTests` | Exit **0**; **1 passed, 0 failed, 0 skipped**, same dedicated database, no reset/deletion. |
| 3 | `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter "FullyQualifiedName~SqlServerPersistenceTests|FullyQualifiedName~ExplicitSessionMigrationTests"` | Exit **0**; **3 passed, 0 failed, 0 skipped**. |
| 4 | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj` | Exit **0**; **72 passed, 0 failed, 0 skipped**. |
| 5 | `dotnet build MarketOpportunityEngine.sln` | Exit **0**; **0 warnings, 0 errors**. |

Focused replay coverage includes rising and falling slopes with exact expected values of **+1.5** and **-1.5 price units per minute** across irregular event intervals, flat zero, insufficient input, symbol/session isolation, deterministic identity, and future-event exclusion. The full fixture regression retained all prior exact positive API/SQL checks and unknown opportunity/symbol/session `404` assertions. SQL tests used the existing Compose SQL Server and guarded `MarketOpportunityEngine_IntegrationTests` database. Fixture records were retained, migration tests used only their uniquely named temporary databases, the container was not changed, and process-only credentials/configuration were cleared. No provider User Secrets, live provider connections, or browser walk-through were used.

**Earlier attempts in this slice (not passing evidence):**

- The first focused build found one test-only `FeatureSnapshot` construction missing the new slope fields; the publication-gate test fixture was updated with the explicit insufficient/null state.
- The next focused run passed 7 of 8 replay tests; the existing configuration identity test selected `scanner-config-v4`, which had become the new default and therefore was not a changed configuration. Its test-only changed value was updated to `scanner-config-v5`; no production default was changed to accommodate the test.
- The first SQL setup attempt loaded the neutral SQL client instead of the Windows runtime assembly and failed before any database test. Loading the packaged Windows runtime `Microsoft.Data.SqlClient.dll` resolved setup. No database was reset/dropped, and no credential was printed or saved.

No outstanding verification blocker remains for this feature slice. The hosted fixture test still uses the isolated Testing/Fixture setup and no Development User Secrets.

### Earlier 2026-10-25 hosted API negative-lookup regression evidence

Added hosted API `404` assertions in [FixtureWebApplicationTests](tests/MarketOpportunity.IntegrationTests/FixtureWebApplicationTests.cs) and directly related documentation; the existing fixture-to-SQL-to-API assertions and persistence-test schema migration fix remain unchanged. The final sequence used the already-running Compose SQL Server with a process-only, guarded connection to `MarketOpportunityEngine_IntegrationTests`. The test connection variable was initially absent; local SQL credentials were obtained in memory without printing or saving them. No test records were deleted to make replay pass.

| Order | Exact command | New final result |
|---|---|---|
| 1 | `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter FullyQualifiedName~FixtureWebApplicationTests` | Exit **0**; **1 passed, 0 failed, 0 skipped**. |
| 2 | `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter FullyQualifiedName~FixtureWebApplicationTests` | Exit **0**; **1 passed, 0 failed, 0 skipped**, same database and retained fixture records. |
| 3 | `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter "FullyQualifiedName~SqlServerPersistenceTests|FullyQualifiedName~ExplicitSessionMigrationTests"` | Exit **0**; **3 passed, 0 failed, 0 skipped**. Migration tests drop only their uniquely named temporary databases, never the configured database. |
| 4 | `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj` | Exit **0**; **69 passed, 0 failed, 0 skipped**. |
| 5 | `dotnet build MarketOpportunityEngine.sln` | Exit **0**; **0 warnings, 0 errors**. |

The hosted test now checks the exact four symbol/session pairs and expected classifications; complete list/detail payload equality; all four session-specific snapshots and analytical versions; exact `404 Not Found` for a valid absent opportunity GUID, an absent symbol, and an unknown explicit session for both MOE and QNT; SQL opportunity IDs, analytical fields, supporting/contradictory evidence, veto absence, invalidation, and provenance; SQL snapshot IDs/columns and deserialized payload equality; and all canonical event identities/IDs, provider/session/sequence, event/receive times, and trade/quote fields. Because valid session snapshots are compared exactly before the negative assertions, the regression also confirms an unknown session does not fall back to another session's latest result. The fixture has **26** events and **26** unique canonical identities, grouped **7/6/6/7** for MOE-22/MOE-23/QNT-22/QNT-23. Expected events retain the first occurrence of each canonical identity, matching existing duplicate semantics. Queries select exact expected identities/versions, so unrelated history is neither counted as evidence nor rejected.

Publication is checked at deterministic replay time **2026-10-23T14:30:42Z**. Both October 22 opportunities are **Expired / OpportunityExpired**; both October 23 opportunities are **Active / PublicationAllowed**. This is the current in-memory publication projection, not persisted lifecycle history. The HTTP API still reads `ScannerState`.

**Earlier attempts in this slice (not passing evidence):**

- SQL setup wrappers initially failed while loading the SQL client or constructing the PowerShell wrapper; no tests ran in those attempts. Loading the Windows runtime SQL client directly resolved setup. One diagnostic retry lacked the process environment variable and failed the existing guard.
- The first preflight after adding the negative assertions attempted a direct PowerShell SQL connection and failed because native `Microsoft.Data.SqlClient.SNI.dll` could not be loaded. No test command ran and no database was mutated. The successful ordered run skipped that PowerShell connection smoke check and let the .NET integration-test runtime load its packaged SQL dependency.
- The old factory's late `ConfigureAppConfiguration` override did not reliably override settings already captured by `Program`. Initial hosted attempts unexpectedly inherited local Massive settings, attempted a connection, received a plan/access denial, and failed with a disposed-host exception. These were not successful live-provider validation. Logs showed no migrations applied and no completed fixture analysis; the old host's SQL target was not independently established. No provider credential values were displayed or saved.
- Fixed this within the test factory: use the non-Development `Testing` environment to avoid loading provider User Secrets, supply early host configuration, and explicitly register the fixture source, guarded SQL connection, and checked-in scanner configuration. Schema preparation now runs explicitly against the guarded test connection. No application/provider adapter code changed.
- A subsequent fixture-only run failed the exact opportunity-ID check because constructing default configuration in test code serialized decimal thresholds differently from the checked-in configuration (`25` versus `25.0`). The expected replay now reads the checked-in scanner section, preserving its existing representation and immutable configuration version rather than rewriting records or weakening the ID assertions.

**Cleanup and evidence boundary:** All final ordered commands passed, test hosts were disposed, the pre-existing SQL container remained healthy, and the process connection was cleared. The configured database and fixture rows were retained. No separate app/browser was started for this test-only slice; the walkthrough below remains historical. Azure and intentional Alpaca/Massive connectivity checks were not run. No outstanding verification blocker remains for the regression slice.

### Earlier 2026-10-25 completed Compose-backed fixture/browser verification (historical)

After operator approval, reused the already-running healthy Compose `sqlserver` container with the dedicated `MarketOpportunityEngine_IntegrationTests` database. Constructed a process-only connection from local container credentials without displaying or persisting it; the non-empty catalog/`test` guards remain unchanged. The development database was not targeted.

The first SQL persistence run failed with missing provenance columns (`DataCoverage`, `DataFeed`, `DataProvider`, and channel flags): its setup used only `EnsureCreated`, which does not upgrade existing schemas. Changed `SqlServerPersistenceTests` to call `MigrateAsync` when `EnsureCreatedAsync` returns false, matching application startup. No application behavior or migration guard was weakened.

| Command / check | Final result after fix |
|---|---|
| `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter FullyQualifiedName~FixtureWebApplicationTests` | Exit **0**; **1 passed, 0 failed, 0 skipped**. Ran first, then again after the setup fix. |
| Filtered persistence/migration command in [README](README.md#fixture-runtime-verification-order) | Exit **0**; **3 passed, 0 failed, 0 skipped** (one persistence test and two migration tests). Initial pre-fix run: 2 passed, 1 failed. |
| `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj` | Exit **0**; **69 passed, 0 failed, 0 skipped** after the code change. |
| `dotnet build MarketOpportunityEngine.sln` | Exit **0**; **0 warnings, 0 errors** after the code change. |
| `dotnet run --project src\MarketOpportunity.Web --launch-profile http --no-build` | Started with process-only Fixture/provider and guarded SQL overrides. `/health` returned Healthy; status reported Recorded / RecordedFixture / 26 events. |
| Integrated browser at `http://localhost:5228/` | All four cards, publication state/reason, and RecordedFixture / Synthetic / Synthetic provenance observed. Reload established a Blazor `/_blazor` WebSocket. |
| Browser HTTP checks | Four opportunity details and four session-specific snapshots returned **200**, with matching IDs/symbol/session. |
| Guarded, read-only SQLCMD queries in the test database | Exact opportunity ID set (**4**) and snapshot ID set (**4**) matched the running API. Fixture events: MOE 2026-10-22 **7**, MOE 2026-10-23 **6**, QNT 2026-10-22 **6**, QNT 2026-10-23 **7**; total **26**. All four opportunity rows: RecordedFixture / Synthetic / Synthetic, realtime false, trades/quotes true. |
| Cleanup | Temporary attached web app stopped; port 5228 no longer listening. Existing SQL container remained running/healthy and its volume was not removed or recreated; fixture records retained in the test database. Connection overrides were process-only. |
| Live provider / Azure checks | **Not run**; no Alpaca/Massive connection, provider credential use, or Azure deployment. |

Observed publication states were Expired / OpportunityExpired for both 2026-10-22 cards, and Active / PublicationAllowed for both 2026-10-23 cards. Fixture publication evaluation uses replay time (`2026-10-23T14:30:42Z`), not a claim of current market validity. No strategy profitability claim is implied.

The initial missing-variable blocker below is historical and resolved for this run. A future terminal/session still needs a safely constructed dedicated test connection; no permanent credential configuration was created. The API remains memory-backed: matching persisted rows does **not** prove historical reads or live recovery.

### Earlier 2026-10-25 preflight (superseded by the completed run above)

Inspected `FixtureWebApplicationTests`, `SqlServerPersistenceTests`, `ExplicitSessionMigrationTests`, and the integration project dependencies. At that checkpoint the hosted test attempted to force Fixture mode using late configuration, waited for four opportunity results, checked status/list/detail/snapshot HTTP routes, and checked persisted opportunity IDs/provenance plus broad snapshot/event counts. The new regression slice above replaces those counts and hardens provider isolation. The persistence test covers session isolation, immutable records, replay idempotency, and provenance. Both migration tests use isolated temporary databases. All three test classes retain the non-empty database-name/`test` guard.

| Check / command | Exact result |
|---|---|
| Process-environment preflight for `MARKETOPPORTUNITY_SQL_TEST_CONNECTION` | **Absent**. No connection value, credentials, or User Secrets were displayed. No SQL connection was attempted. |
| `dotnet --version` | **10.0.402**. |
| `dotnet test tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj --filter FullyQualifiedName~FixtureWebApplicationTests` | **Not run**: missing dedicated SQL test connection. |
| Filtered SQL persistence/migration command in [README](README.md#fixture-runtime-verification-order) | **Not run**: same safety gate. |
| Manual Fixture-mode dashboard / HTTP walkthrough | **Not run**: no safely configured local test database was established. No app or Docker process was started and no database was altered. |
| `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj` | **Succeeded**, exit code **0**; **69 passed, 0 failed, 0 skipped**; reported test duration **2 seconds**. |
| `dotnet build MarketOpportunityEngine.sln` | **Succeeded**, exit code **0**; **0 warnings, 0 errors**; elapsed **00:00:02.60**. Includes the hosted integration-test project, but does not execute it. |
| Alpaca/Massive connectivity | **Not run**. No live-market connection or provider credentials were used. |

At that earlier preflight, only documentation changed and runtime checks remained unverified. The later approved Compose-backed run above resolved that blocker. Migration coverage requires permission to create/drop its uniquely named temporary databases.

### Prior 2026-10-24 checkpoint evidence

These results are historical, not additional commands run during the 2026-10-25 attempt:

| Command | Result |
|---|---|
| `dotnet restore MarketOpportunityEngine.sln` | **Succeeded**, exit code 0. |
| `dotnet build MarketOpportunityEngine.sln --no-restore` | **Succeeded**, exit code 0; **0 warnings, 0 errors**. |
| `dotnet test tests\MarketOpportunity.UnitTests\MarketOpportunity.UnitTests.csproj --no-build` | **Succeeded**, **69 passed, 0 failed, 0 skipped**. |
| `dotnet build tests\MarketOpportunity.IntegrationTests\MarketOpportunity.IntegrationTests.csproj` | **Succeeded**, exit code 0; **0 warnings, 0 errors**. Hosted fixture test compiled but was not executed. |
| `dotnet test tests\MarketOpportunity.AlpacaConnectivityTests\MarketOpportunity.AlpacaConnectivityTests.csproj` | **Succeeded**, **0 passed, 1 skipped, 0 failed**; skipped because credentials are absent. No network request was attempted. |
| SQL Server integration test execution, including `FixtureWebApplicationTests` | **Not run**. `MARKETOPPORTUNITY_SQL_TEST_CONNECTION` was absent; no database was mutated. |

At that prior checkpoint SQL integration coverage was **unverified**, not passed; the completed 2026-10-25 run above supersedes it. The connectivity project initially lacked the environment-variable configuration package; that dependency was added and restore/test then succeeded with the expected credential-gated skip.

### Existing test categories

- **Unit:** Yes — Alpaca/Massive parser/options/capability/factory, analytics, detector, publication lifecycle, and application pipeline tests.
- **Integration:** Yes, real SQL Server — hosted fixture/API/persistence test, persistence/idempotence test, and two migration tests all passed on 2026-10-25.
- **Architecture:** No dedicated architecture test project.
- **Component:** No component test project.
- **Replay/golden:** Replay-style deterministic tests in `ScannerReplayTests`; fixtures are constructed in test code rather than an external golden snapshot suite.
- **UI:** No committed browser/UI test project. Local integrated-browser walkthrough completed on 2026-10-25.

### Specific coverage evidence

| Behavior | Coverage |
|---|---|
| VWAP | Yes: replay and detector tests assert calculated session VWAP. |
| Spread | Yes: invalid quote and excessive spread veto tests. |
| Event ordering | Yes: late/out-of-order input sorted deterministically. |
| Duplicates | Yes: repeated identity does not change replay result. |
| Future-event leakage | Yes: future event cannot affect earlier snapshot/opportunity. |
| Setup qualification | Yes: probable long/short and watch cases. |
| Setup rejection | Yes: veto and insufficient-data cases. |
| Stale-data handling | Stale-quote veto and stale/offline live-feed publication suspension are tested separately. |
| Excessive-spread veto | Yes. |
| Insufficient-data behavior | Yes. |
| Opportunity expiration | Valid-until timestamp and publication-gate expiry tested; persistent lifecycle history is not implemented. |
| Configuration/version propagation | Snapshot/opportunity identity and version fields covered offline; SQL persistence/version behavior passed integration coverage. |
| Alpaca mapping | Parser, timestamp/identity handling, feed capabilities, options, and source selection are offline unit-tested; socket connectivity is not verified. |
| Publication/API read model | JSON serialization unit-tested; hosted HTTP/SQL test passed and local browser/API results matched persisted IDs/provenance. |

## 21. End-to-End Proof

Offline unit coverage plus the 2026-10-25 local fixture SQL/API/browser verification establishes the following evidence:

```text
Synthetic canonical events
  ✓  test event sources and replay inputs
Normalization/parser
  ✓  Alpaca/Massive parser unit tests; fixture adapter maps JSON to canonical events
Event-time state
  ✓  sorted ordering, duplicates, symbol/session isolation, bounded eviction
Features
  ✓  VWAP, current price, short return, window volume, quote age, as-of cutoff
Momentum detector
  ✓  long/short/watch qualification, evidence, scoring, versioning
Risk/data-quality gates
  ✓  quote-age, invalid data, spread, insufficient data, incomplete state
Opportunity result
  ✓  deterministic identity and expiration timestamp
Publication decision
  ✓  active/suspended/expired/not-qualified state and reason; feed-health reevaluation
Provider capabilities/provenance
  ✓  feed mapping, source selection and offline provenance tests
Test store boundary
  ✓  ScannerPipeline tests use a recording store
SQL persistence
  ✓  hosted fixture, persistence, and both migration tests passed; 26 fixture events in SQL
HTTP API
  ✓  hosted test passed; local four opportunity/snapshot IDs matched SQL
Blazor UI
  ✓  local browser walkthrough: four cards, publication/provenance, Blazor WebSocket
Alpaca/Massive provider sockets
  ?  adapters compile; parser/options tested; no authenticated stream exercised
```

`✓` denotes the scoped fixture evidence recorded in section 20, not live-provider verification, database-backed API reads, a permanent browser regression suite, or full Milestone A completion.

## 22. Master Spec Compliance

### Implemented as Specified

- .NET 10/C#, ASP.NET Core Blazor, EF Core SQL Server, and modular-monolith project boundaries.
- Provider-independent market-event contracts with decimal/integer trading values and event/receive timestamps.
- Explicit symbol/session event state, deterministic event-time ordering, dedupe, bounded retained state, and future-event cutoff.
- Deterministic feature/setup versions, opportunity explanations, evidence, contradictions, veto list, score, and expiry timestamp.
- Independent publication gate suspends live candidates for Connecting, Stale, Offline, or Error source health without changing analytical evidence.
- Provider/feed coverage and channel provenance flows into snapshots/opportunities; Alpaca is optional and Fixture remains the configured default.
- Fixture default, SQL Server local environment, canonical event retention, and accepted ADRs.
- No machine learning, broker access, order placement, Redis, distributed broker, microservices, Kubernetes, Level 2, or LLM-generated signals found.

### Implemented Differently

- The repo includes optional Alpaca and Massive live adapters, while fixture remains the configured default. Both live adapters remain non-resilient.
- SignalR is present only as Blazor Interactive Server circuit transport; there is no separate SignalR application hub.
- Trade and quote persistence share one `market_event` table instead of separate logical tables.
- The application/API read model is in-memory. Persistence is write-oriented at this point; current API results are not database-backed.
- Session ID is part of the current event contract and is required rather than inferred.
- Configuration and feature-set versions exist, but no complete operational configuration-history/audit interface exists.

### Missing but Required for Current Checkpoint

- Remaining three Milestone A detectors.
- Feed reconnect/recovery and gap handling; unhealthy feed publication suspension is implemented but does not provide data-integrity recovery.
- Liquidity, benchmark/context, volatility, reward/risk, targets, and later-observation structural invalidation.
- Additional requested feature engines (trend strength, multi-horizon momentum, relative volume/strength, opening range); the current price-structure output is descriptive only and is not integrated into trend scoring or detectors.
- Historical/persisted API reads. The fixture's parallel SQL persistence/current-state API/Blazor path is verified, not database-backed reads.
- Live dashboard completeness and runtime proof, including error/recovery health.
- Azure SQL deployment configuration and reviewed production schema deployment path are absent despite being required by the technology baseline.
- SQL integration verification passed against a dedicated local test database; production/Azure database verification remains absent.

### Intentionally Deferred

- Machine learning, backtesting/outcomes/research, broker or automated trading, LLM explanations, and future model tables.
- Raw provider payload retention pending license/replay review.
- Separate application SignalR hub pending a need beyond the current Blazor Server circuit.
- Redis, distributed messaging, microservices, Kubernetes, and Level 2 processing are explicitly outside Milestone A and absent.

### Accidental Scope Expansion

No evidence of accidental scope expansion into ML, trading, distributed infrastructure, or other prohibited future platform work. Alpaca and Massive are optional data adapters; Alpaca is explicitly approved in ADR-005 and does not authorize broker access.

## 23. Technical Debt / Concerns

### HIGH

1. **No gap/recovery/reconnect path.** Live adapter termination ends the stream; missed events cannot be backfilled and there is no unresolved-gap/recovering state. Publication suspension limits stale-result display but does not restore data integrity.
2. **Reads remain memory-only.** Fixture SQL/API/browser runtime is verified, but the API/dashboard do not load historical data after restart.
3. **Core scanner capability is incomplete.** One detector and a subset of data-quality checks do not meet Milestone A detector/risk requirements.

### MEDIUM

1. **Publication lifecycle is process-local.** Current `PublicationDecision` reflects health and expiration in memory; append-only lifecycle history is not persisted, and later price action does not monitor structural invalidation.
2. **State-boundedness does not bound every collection.** Fixture pipeline retains each state's event list separately for persistence, and symbol/session maps have no global cardinality cap. Current fixture/provider configuration keeps expected inputs limited but the overall memory bound is not universal.
3. **Current data API is a process-local read model.** Endpoints return only current in-memory results, not stored opportunities or snapshots.
4. **Development SQL credential default is checked into Compose configuration.** Documentation scopes it to local development and recommends overriding it; it must not be reused outside disposable local use.
5. **Feature completeness and participation scoring are narrow.** A positive window volume still awards the full activity weight; no relative-volume baseline exists. The separately configured short-horizon dollar-volume hard veto is implemented but does not expand scoring.
6. **Browser regression coverage is manual.** Hosted HTTP/SQL tests and a local browser walkthrough passed; there is no committed component/browser test suite or live error/recovery browser coverage.

### LOW

1. **Operational tuning constants are embedded in code.** Live batch cadence, bounded channel capacity, and provider message-size limit do not appear in validated scanner configuration.
2. **No scanner-specific telemetry.** Current OTel instrumentation cannot explain event-to-opportunity latency, veto counts, ingestion gaps, persistence latency, or SignalR client health.

## 24. Current Blockers

- **Verification blocker:** SQL persistence/migration verification for the current dollar-volume slice was not run because `MARKETOPPORTUNITY_SQL_TEST_CONNECTION` was absent at guarded preflight. No SQL connection or service operation was attempted. Historical SQL results for prior slices are recorded separately and are not current gate evidence.
- **Local run dependency:** Web startup requires SQL Server configuration/service, even when using fixture mode.
- **Milestone A implementation blockers:** Only Momentum Continuation exists; required detector and hard-gate coverage is incomplete; live reconnect/gap recovery is absent; persisted-result APIs are not implemented. The preceding fixture SQL/API regression is historical; current hosted/SQL validation was not run. Browser walkthrough evidence is historical.
- **External live-data dependency:** Alpaca or Massive credentials and feed entitlement are needed for intentional live socket validation. Initial pre-isolation hosted attempts inherited local Massive configuration and were denied; final tests use the isolated Fixture host without loading provider User Secrets. No provider credential values were printed or saved.

Current dollar-volume verification is recorded in section 20 and the latest analysis snapshot: focused replay/detector tests, full offline units, and solution build passed; SQL was not run/unavailable. Historical hosted fixture and SQL evidence applies only to earlier slices. No browser walkthrough, live connectivity, User Secrets, or Azure verification is claimed for this slice.

## 25. Recommended Next Vertical Slice

**Decision (2026-10-27; investigation only):** Do not implement relative volume or add a proxy. The master spec calls for volume normalized to the same point in a trading session when historical reference data is available, but does not choose a comparison interval, number of sessions, baseline statistic, or history source.

**Current-volume candidate, not an approved formula:** Compare eligible cumulative trade shares from the declared session open through `AsOfUtc` with prior comparable sessions at the same elapsed time from the official market-session open. Event time, not receive or wall-clock time, defines eligibility and alignment. The result could be a dimensionless ratio of current cumulative shares to a baseline cumulative-share statistic, but neither that ratio nor the denominator statistic is specified or approved. A rolling interval would be a different feature and is not authorized by the current spec's same-point-in-session wording.

**Evidence from the runtime path:** `IMarketDataSource.ReadEventsAsync` supplies a single event stream to `ScannerPipeline`; live Alpaca and Massive implementations are WebSocket streams and the default source is a synthetic fixture. `ScannerPipeline` constructs independent `SymbolEventState` instances keyed by symbol and explicit session ID. State keeps at most `MaximumStateEvents` (default 10,000), deduplicates by stable event identity, orders by event time/provider/sequence/identity, and exposes only events at or before the requested `AsOfUtc`. `FeatureCalculator.SessionCumulativeVolume` sums eligible retained trade sizes, while `ShortHorizonDollarVolumeUsd` sums price × size in the inclusive `[AsOfUtc - MomentumWindowSeconds, AsOfUtc]` interval; neither reads another session or computes a historical comparison.

`HasCompleteSessionData` is currently `!SymbolEventState.HasEvictedEvents`. It reports only whether this in-memory state evicted an event; it does not establish that ingestion began at session open, that all upstream events arrived, or that there were no feed gaps. Canonical events include explicit session ID, provider, event/receive time, and trade values, but no session-open/calendar offset or per-event feed coverage. Provider capabilities describe the active source, not historical comparisons. The fixture's synthetic sessions are regression inputs, not an approved history source or calibration sample.

`IScannerStore.PersistRunAsync` writes canonical events to SQL, but there is no runtime historical-event read interface, startup replay/bootstrap from SQL, or database-backed event API. ADR-004 records normalized-event persistence but sets no retention duration; persistence therefore does not imply scanner availability, session completeness, or permission to compare historical coverage. No history count, mean/median choice, missing/gap policy, or comparable-feed rule can be justified from current inputs.

**Smallest prerequisite:** Approve a provider-independent historical-volume source and its retention/licensing, explicit session calendar/open, symbol/session identity, units, market/feed coverage, and gap/completeness guarantees. Separately decide which prior sessions qualify, the minimum count, baseline statistic, interval and missing/invalid behavior. Only then specify the deterministic input contract and implement a descriptive feature. Do not add a detector veto, scoring change, provider connection, or SQL read path as part of this investigation.

**Version and validation boundary:** No feature/configuration parameter, feature version, or configuration version changes; `features-v14`, `scanner-config-v15`, and `momentum-continuation-v1` remain unchanged. No relative-volume tests were added or run. The preceding dollar-volume slice's focused tests (156/156), offline unit suite (208/208), and solution build (0 warnings/errors) remain the latest validation evidence; guarded SQL remains not run/unavailable because `MARKETOPPORTUNITY_SQL_TEST_CONNECTION` was unset. No hosted fixture, browser, live-provider, User Secrets, or Azure validation is claimed.

**Explicitly exclude:** Other setup detectors, live-provider connections, broker integration, provider selection changes, Azure deployment, ML, and distributed transport.

**Explicitly exclude:** Other setup detectors, live-provider connections, broker integration, provider selection changes, Azure deployment, ML, and distributed transport.

## 26. Questions Requiring Architect Decision

Before relative-volume implementation, decide the approved historical-volume source and its retention/licensing terms; the session calendar/open and exchange-local alignment; the comparable-feed/coverage rule; the qualifying prior-session count and minimum; and the baseline statistic. Define how missing, partial, invalid, or gapped sessions affect availability. These decisions are prerequisites, not authorization to connect a provider or add a runtime history reader.

## 27. Handoff Summary for Copilot

# Handoff Summary for Copilot

**Current Milestone:** Milestone A — Usable Scanner; no formal checkpoint ID recorded.
**Working Vertical Slice:** Descriptive retained-session trade-point volatility (`features-v13`) and risk veto (`scanner-config-v14`), followed by the configured minimum short-horizon dollar-volume feature/veto (`features-v14` / `scanner-config-v15`). Dollar volume uses decimal trade notional in the inclusive primary momentum window; complete consistent values strictly below provisional $100,000 USD are vetoed; equality passes. Insufficient/invalid/inconsistent and partial-session behavior is explicit. Current offline tests/build passed; current SQL/hosted/browser verification was not run.
**Implemented:** .NET 10 modular monolith; fixture default; optional Alpaca and Massive trade/quote WebSocket adapters; capability/provenance metadata; event-time state and duplicate identity; VWAP, short-horizon return/share-volume/dollar-volume, spread, quote freshness; one directional detector with explicit volatility and dollar-volume risk vetoes; independent health-to-publication gate; SQL Server/EF persistence code and provenance migration; GET status/opportunity/snapshot/config endpoints; Interactive Server dashboard; five accepted ADRs.
**Not Implemented:** Three other Milestone A detectors; composite trend-direction/quality scoring; relative-volume/strength, ATR, opening-range, benchmark/sector context, reward/risk; data-gap recovery/reconnect; persisted publication revisions; later-event invalidation monitor; database-backed read API; App Service/Azure SQL deployment, Key Vault, Azure Monitor/App Insights, Entra ID, Bicep, Azure DevOps.
**Current Data Source:** Fixture is the configured default; Alpaca test feed defaults to `FAKEPACA`, while live feeds require operator credentials, symbols, explicit session ID, and entitlement. Massive remains supported. No historical adapter.
**Current Features:** Current trade reference price, bid/ask/spread, retained-session VWAP, session VWAP slope per minute with direction/insufficient state, price vs VWAP, fast/medium EMA (5/20 trade periods by default), signed fast/medium spread and event-time slopes, trade-point Wilder ADX (`TrendStrengthPeriod` 14 by default; null/state for warm-up or invalid prices), descriptive `SessionTrendDirection` (bullish/bearish/neutral/conflicting/insufficient/invalid), descriptive `SessionRealizedVolatilityPercent` (unannualized percent points from consecutive retained-trade log returns; nullable with status; consumed only by the separate hard veto), `ShortHorizonDollarVolumeUsd` (decimal USD sum of price × size for retained eligible trades in the inclusive primary momentum window; nullable with dedicated status and completeness semantics), two separately configured short-horizon returns (secondary descriptive/nullable), unchanged primary-window share volume, descriptive nullable session cumulative volume, session high/low and range position, fixed two-left/two-right session price structure (nullable; higher-high/higher-low, lower-high/lower-low, mixed, or flat; partial after eviction per `HasCompleteSessionData`), quote age/validity, required-data/completeness quality. Versions: `features-v14`, `scanner-config-v15`, unchanged `momentum-continuation-v1`.
**Current Detector(s):** Momentum Continuation only; Long, Short, Watch, Avoid/No Trade.
**Current Vetoes:** Stale quote, invalid quote/price, excessive/unavailable spread, insufficient required data/incomplete state, excessive realized volatility (`>= 1.00` percentage point on complete/available data), unavailable/invalid volatility data, insufficient short-horizon dollar volume (strictly `< MinimumShortHorizonDollarVolumeUsd` on complete/available data), unavailable/invalid/inconsistent dollar-volume data, and no defensible invalidation. Partial available values skip only their numeric cutoff; incomplete-state veto remains. Both numeric thresholds are provisional, not market-calibrated. Separately, unhealthy live source suspends publication with a lifecycle reason.

**Persistence:** EF Core 10 SQL Server store and five tables; session-identity/provenance migrations. Previous hosted fixture/persistence/migration results are historical; current SQL tests were not run because the guarded test connection was absent. API reads process memory.
**Blazor:** Local integrated-browser walkthrough verified all four fixture cards, publication/provenance, and a Blazor WebSocket. No committed browser regression suite or live recovery walkthrough.
**Realtime UI:** In-process `ScannerState.Changed` updates Blazor Server circuits; no application SignalR hub.
**Tests:** Current dollar-volume slice: focused replay/detector **156/156**; offline unit suite **208/208**; solution build **0 warnings/0 errors**. SQL integration was **not run / unavailable** because the guarded connection environment variable was absent; persistence assertions for new JSON fields and same-version threshold immutability compiled but were not executed. Hosted fixture, browser, live-provider, User Secrets, and Azure validation were not run. Historical SQL/hosted results remain separately labeled in section 20. The initial focused compile attempt is separately described as a failed attempt (0 tests executed).
**Azure Readiness:** No deployment infrastructure/integration. Only local SQL Server Docker setup and configuration abstractions exist.
**Primary Technical Debt:** No reconnect/gap recovery; publication state is not persisted as revisions; no later-event invalidation monitor; historical reads absent. Exact fixture SQL/API positive and negative lookup regression assertions are implemented.
**Current Blocker:** No implementation blocker for the completed slice; guarded SQL integration remains unverified until a dedicated test connection is configured. Full Milestone A still requires missing functionality.
**Recommended Next Step:** Approve a historical-volume source, session-calendar alignment, retention and data-completeness contract, then decide the comparable-session count and baseline statistic. Do not treat persisted-but-unread events, retained-session cumulative volume, short-horizon dollar volume, or synthetic fixtures as that baseline.
**Decisions Needed:** The 1.00 volatility and $100,000 dollar-volume cutoffs are provisional and not market-calibrated. Do not connect live providers implicitly.
