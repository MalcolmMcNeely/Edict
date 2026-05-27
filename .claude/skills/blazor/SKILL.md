---
name: blazor
description: Use this skill when editing, creating, or reviewing any Blazor component (.razor) in this repo. Covers render mode, folder layout, live read-model refresh, and SignalR/state rules for Sample.Web.Components (the substrate-agnostic Razor class library that Sample.Azure.Web and Sample.KafkaPostgres.Web both mount).
---

# Blazor Component Conventions

`Sample.Web.Components` is the canonical Edict consumer reference — a substrate-agnostic Razor class library mounted by `Sample.Azure.Web` and `Sample.KafkaPostgres.Web`. It is **plain Blazor Server** — no UI component library, no design system, no JS interop beyond what the framework ships. The point of the sample is to show off Edict primitives, not Blazor flourishes.

## No UI component library

Use standard HTML elements (`<button>`, `<input>`, `<table>`, `<select>`, etc.) and the framework-provided components (`<NavLink>`, `<RouteView>`, `<Router>`, `<HeadOutlet>`). Do **not** add MudBlazor, Radzen, Fluent UI, Bootstrap, Tailwind, or any other library. If a page needs layout, write CSS in `wwwroot/app.css`. If a page needs interactivity, use Blazor's built-in event handlers and `StateHasChanged`.

If a future slice genuinely needs a richer widget, raise it for discussion first — adding a component library is a project-level decision, not a per-page convenience.

## Render mode — global `InteractiveServer`

Interactive Server rendering is configured **globally** by setting the render mode on `<Routes>` in `App.razor`:

```razor
<Routes @rendermode="InteractiveServer" />
```

This establishes a single persistent SignalR circuit for the entire app. All pages and child components inherit this mode automatically.

**Never** add `@rendermode InteractiveServer` to individual page components (`.razor` files with `@page`). Doing so causes each navigation to tear down and recreate the circuit, which breaks Blazor's enhanced navigation — the URL updates but the page silently fails to activate.

## Folder layout

Pages and layout under `Sample.Web.Components/`:

```
Components/
  _Imports.razor           — global usings (one per project + namespace)
  App.razor                — <html> shell, <Routes @rendermode="InteractiveServer" />
  Routes.razor             — <Router>, <RouteView DefaultLayout="...">
  Layout/
    MainLayout.razor       — page chrome (sidebar + main)
    NavMenu.razor          — left-nav <NavLink> list
  Pages/{Feature}/{Feature}.razor
```

`{Feature}` matches the Edict feature folders elsewhere in the sample (`Home`, `Orders`, `Fulfillment`, `DeadLetter`, `ClaimCheck`, `Idempotency`). One page per `.razor` file. One top-level `@page` route per file.

## Live read-model refresh

Edict pages observe projection state, not push streams. The convention for live views:

- Subscribe to a server-side `PeriodicTimer` in `OnInitializedAsync`, tick every ~1–2 seconds.
- Inside the tick callback: fetch the projection row(s), then `await InvokeAsync(StateHasChanged)`. Blazor's existing SignalR circuit pushes the diff to the browser.
- `@implements IDisposable` and cancel the timer + dispose the `CancellationTokenSource` in `Dispose()`.
- Swallow `OperationCanceledException` on shutdown; don't log it.

Do **not** add a separate SignalR hub or an Orleans-stream subscription on the client tier. The point of the read-side model is that the projection is the truth — the timer just asks "what's the current row?".

## State and async

- Async callbacks that fire outside the render loop (timer ticks, awaited tasks completing late) must wrap UI mutations in `await InvokeAsync(StateHasChanged)`.
- `@inject` is the dependency-resolution mechanism. Edict surfaces consumers bind to: `IEdictSender`, `IEdictTableRepository<TRow>`, `IEdictDeadLetterRepository`.
- Wire DI registrations in `Program.cs`, not in Razor code. A page never news up a repository.

## Imports

`_Imports.razor` carries the global usings — every Edict.Contracts namespace a page might need, plus the standard `Microsoft.AspNetCore.Components.*` set the template provides. Do not add per-file `@using` directives for things already in `_Imports.razor`.

## What this app does not do

- Authentication / authorisation.
- Multi-tenancy.
- Real-time event push from the server to specific clients.
- Custom JS interop.
- Component tests (bUnit). The sample's mission is to demonstrate Edict, not Blazor.
