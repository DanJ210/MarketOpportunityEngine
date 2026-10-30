# Massive WebSocket reference

This page is a project-oriented reference to Massive's [WebSocket Quickstart](https://massive.com/docs/websocket/quickstart). The official documentation is authoritative for current endpoints, message schemas, connection limits, entitlements, and provider terms; those details can change.

## Protocol at a glance

Massive streams market data over a persistent WebSocket. The quickstart's manual connection flow is:

1. Connect to the asset-class endpoint. For stocks, the real-time endpoint is `wss://socket.massive.com/stocks`; Massive also documents a separate delayed endpoint.
2. Send an authentication message after connecting:

   ```json
   { "action": "auth", "params": "YOUR_API_KEY" }
   ```

3. Wait for the successful authentication status before subscribing:

   ```json
   { "action": "subscribe", "params": "T.AAPL,Q.AAPL" }
   ```

   Subscription parameters are comma-separated channel names. The quickstart's example subscribes to minute aggregates (`AM.AAPL`); the project adapter instead subscribes to trades (`T`) and quotes (`Q`).
4. Read and process incoming messages. A WebSocket message may contain an array of multiple events, so consumers must handle both a single event and a batch.

The quickstart documents a default limit of one concurrent WebSocket connection per asset class. Check Massive's current plan, entitlement, connection, and data-use terms before relying on a feed. Its general performance guidance is to process messages promptly and reduce subscriptions if the consumer cannot keep up.

## How this project uses the stream

The current implementation is in [`MassiveMarketDataSource`](../../src/MarketOpportunity.Infrastructure/MassiveMarketDataSource.cs). It:

- Connects to the real-time stocks endpoint; it does not silently fall back to delayed data.
- Authenticates, then subscribes to `T.<symbol>` and `Q.<symbol>` for each symbol in the shared `Scanner:Universe:Symbols` setting.
- Handles single-event and array-shaped messages, and maps supported trade and quote messages to provider-independent market events.
- Uses provider event timestamps for event time, the local `TimeProvider` at receipt for receive time, and the configured `MarketData:Massive:SessionId` verbatim. It does not infer a session from a date.
- Starts without historical warm-up or gap backfill and does not automatically reconnect after disconnect. A new connection therefore does not restore events missed while disconnected.

This is a focused adapter, not a general implementation of every Massive channel or message type. It currently supports stock trades and quotes only; the quickstart's aggregate example and its other asset classes are not supported by this adapter.

## Credentials and scope

Keep the API key in local .NET User Secrets for development or a protected secret store such as Azure Key Vault when deployed. Never put it in source control, browser code, chat, or diagnostic logs. See the [README's optional live market data instructions](../../README.md#optional-live-market-data-massive) for configuration and the secure local setup.

Massive access is read-only market-data ingestion in this application. Provider selection does not authorize brokerage access, order placement, or a change to the recorded-fixture default. See [ADR-005](../adr/ADR-005-market-data-provider-criteria.md) for the provider-selection criteria and decision.

## Official references

- [Massive WebSocket Quickstart](https://massive.com/docs/websocket/quickstart)
- [Massive stocks WebSocket overview](https://massive.com/docs/websocket/stocks/overview)
