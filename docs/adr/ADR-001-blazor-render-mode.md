# ADR-001: Blazor render mode and component structure

- **Status:** Accepted
- **Decision:** Use a Blazor Web App with Interactive Server rendering.

## Context

The first release is a single ASP.NET Core modular monolith. It needs a small dashboard, no browser-side market-data credentials, and a simple path for server-originated updates.

## Decision

Render the application through Interactive Server components. Keep route/page components in `MarketOpportunity.Web/Components/Pages`, shared layout in `Components/Layout`, and domain-independent formatting in presentation components. Pages consume application read models/services; they do not access EF Core or provider adapters directly. The fixture runner updates a singleton presentation state that notifies connected component circuits. Blazor's server circuit transport is sufficient for this checkpoint; no separate SignalR hub is introduced.

## Consequences

- Scanner logic and credentials remain server-side.
- The hosted ASP.NET Core app carries the interactive UI and its circuits.
- Browser disconnection interrupts the interactive circuit but does not affect analysis.
- A dedicated SignalR application hub can be added when there is a real external event-stream requirement.
