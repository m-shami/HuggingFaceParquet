# Design brief — Hugging Face Parquet Browser

## Direction exploration

The product is a compact data utility, so the interface should prioritize scan speed, readable tables, and dependable status feedback rather than marketing imagery.

| Direction | Intro | Probability |
|---|---|---:|
| Catalog Desk | A light, orderly data workbench with strong typographic hierarchy and precise table surfaces. | 0.08 |
| Terminal Ledger | A dark, technical console with monospaced emphasis and dense operational status. | 0.03 |
| Archive Paper | A warm editorial archive with muted paper tones and tactile record cards. | 0.02 |

**Committed direction: Catalog Desk.**

## Design dimensions

- **Design movement:** Contemporary editorial data tooling: restrained, functional, and clear.
- **Core principles:** Make repository identity obvious; keep file selection and estimated size visible; distinguish queued, active, completed, and failed states; never hide a user-visible limitation.
- **Color philosophy:** A pale blue-gray canvas, white content surfaces, deep ink text, muted slate secondary text, and a restrained teal action accent. Use green, amber, and red only for success, in-progress/warning, and error states.
- **Layout paradigm:** A centered, responsive work area with a narrow header, repository search panel, compact summary cards, and table-first content. Put preview controls close to the grid and keep long values in expandable detail.
- **Signature elements:** A simple parquet-column mark, small metadata chips for config/split, visible selected-file count/size, and an unobtrusive progress row for each download.
- **Interaction philosophy:** Prefer clear, reversible controls; disable duplicate submissions; show immediate selection feedback; use jQuery AJAX for progress and paging; preserve readable loading and failure states.
- **Animation:** Minimal, short transitions only for progress and disclosure; respect reduced-motion preferences.
- **Typography system:** System sans-serif for interface text, tabular numerals for sizes/progress, and a compact monospace treatment only for repository IDs and file paths.
- **Brand essence:** A dependable small desk for inspecting public dataset files.
- **Brand voice:** Direct, calm, and explicit about what's available and what isn't.
- **Wordmark/logo:** Use the product name “Parquet Browser” as HTML text; avoid generated wordmark lettering.
- **Signature brand color:** Muted teal used for the primary action and the simple parquet mark.

## Asset scope

This is a functional data/control dashboard. Do not add hero or stock imagery. Use one small, product-specific app mark in the header and favicon; interface icons may be simple inline SVGs with accessible labels.
