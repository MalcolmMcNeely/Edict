---
name: blazor
description: Use this skill when editing, creating, or reviewing any Blazor component (.razor) in this repo. Covers MudBlazor component rules, layout conventions, render mode, SignalR state, and bUnit test setup for Covenant's Blazor Server app.
---

# Blazor Component Conventions

## Component library — MudBlazor

Covenant uses **MudBlazor** as its sole UI component library. Raw HTML interactive elements are **banned** — use MudBlazor equivalents instead:

| Banned | Use instead |
|---|---|
| `<button>` | `<MudButton>` / `<MudIconButton>` |
| `<input>` | `<MudTextField>` |
| `<select>` | `<MudSelect>` |
| `<table>`, `<thead>`, `<tbody>`, `<tr>`, `<td>` | `<MudDataGrid>` or `<MudSimpleTable>` |
| `<input type="file">` / `<InputFile>` | `<MudFileUpload>` |
| `<input type="checkbox">` | `<MudCheckBox>` |

Non-interactive structural elements (`<div>`, `<section>`, `<h1>`–`<h6>`, `<p>`, `<span>`) may still be used freely, or replaced with `<MudText>`, `<MudPaper>`, `<MudGrid>` where layout benefit is clear.

### Layout — no Bootstrap

Bootstrap is **not loaded**. Do not use Bootstrap utility classes (`d-flex`, `justify-content-between`, `mb-3`, `py-5`, etc.) — they will silently have no effect. Use MudBlazor layout components instead:

| Layout need | Use |
|---|---|
| Horizontal row with spacing/alignment | `<MudStack Row="true" AlignItems="..." Justify="...">` |
| Vertical stack with spacing | `<MudStack Spacing="2">` |
| Multi-column grid | `<MudGrid>` / `<MudItem xs="...">` |
| Spacing/padding on a component | `Class="ma-2"` / `Class="pa-4"` (MudBlazor spacing classes) |

> **Warning:** MudBlazor spacing classes use `!important`. Do not use `pa-*` on `MudMainContent` — it overrides the built-in `padding-top: var(--mud-appbar-height)` and hides content behind the AppBar. Use `px-* pb-*` instead to preserve the top clearance.

| Page-level heading | `<MudText Typo="Typo.h4">` instead of `<h1>` |

### Required providers in MainLayout

`MainLayout.razor` must include **all four** providers at the bottom of the markup, outside the layout structure. Missing any one of them causes a runtime `InvalidOperationException` when the corresponding MudBlazor component is first rendered.

```razor
<MudThemeProvider Theme="CovenantTheme.Default" IsDarkMode="true" DefaultScrollbar="true" />
<MudPopoverProvider />
<MudDialogProvider />
<MudSnackbarProvider />
```

### MudFileUpload (v9 API)

Use `CustomContent` (not the removed `ActivatorContent`) to render a custom trigger button. `CustomContent` is a `RenderFragment<MudFileUpload<T>>` — the context gives access to `OpenFilePickerAsync`. When nested inside an `EditForm`, disambiguate the context name to avoid a compile error:

```razor
<MudFileUpload T="IBrowserFile" FilesChanged="HandleFileChange" Accept=".docx">
    <CustomContent Context="fileUpload">
        <MudButton Variant="Variant.Outlined" Color="Color.Primary" OnClick="@fileUpload.OpenFilePickerAsync">
            Choose File
        </MudButton>
    </CustomContent>
</MudFileUpload>
```

### Imports

`_Imports.razor` must contain `@using MudBlazor`. Do not add per-file `@using MudBlazor` directives.

### bUnit tests

MudBlazor component tests must call `ctx.Services.AddMudServices()` in test setup before rendering any component that uses Mud components.

## Render mode

Interactive Server rendering is configured **globally** by setting the render mode on `<Routes>` in `App.razor`:

```razor
<Routes @rendermode="InteractiveServer"/>
```

This establishes a single persistent SignalR circuit for the entire app. All pages and child components inherit this mode automatically.

**Never** add `@rendermode InteractiveServer` to individual page components (`.razor` files with `@page`). Doing so causes each navigation to tear down and recreate the circuit, which breaks Blazor's enhanced navigation — the URL updates but the page silently fails to activate.

## SignalR and state

- Subscribe to hub events in `OnInitializedAsync`; always call `await InvokeAsync(StateHasChanged)` inside hub callbacks.
- Never broadcast SignalR events across tenant boundaries — group clients by `tenantId`.
