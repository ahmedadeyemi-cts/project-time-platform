# Pulse Enterprise Experience Audit

**Baseline:** `main@83aa6179a25321cca663d15e5071e3e485f0c070`

## Decision

Pulse will support two persistent presentation modes:

- **Enterprise — primary/default:** one route-aware page identity, shared design tokens, consistent controls and data surfaces, responsive behavior, and protected overlay clearance.
- **Classic — alternate:** the current interface remains available without removing route markup or workflows.

Presentation and theme remain independent: Enterprise + Light, Enterprise + Dark, Classic + Light, and Classic + Dark.

## System-wide findings

The existing top header is the strongest shared product element and remains structurally unchanged. Registered workspaces currently mix large heroes, plain headings, route banners, and newer enterprise headers. The primary view therefore adds one compact registry-aware page header and a last-loaded, scoped style layer for common surfaces, controls, tables, statuses, focus states, and responsive behavior.

Every current module family is covered through the shared module registry. Specialized workflows keep their internal behavior; the enterprise layer standardizes presentation rather than rewriting business logic.

## Module Management

The primary route title is **Module Management**. All authorized modules remain on one page. Existing search, category filtering, role visibility, availability status, and enable/disable controls remain authoritative. The wide-screen grid supports six cards and scales through 5/4/3/2/1 columns.

Module 006 is presented as **Customer Pipelines** with neutral pipeline iconography for Toyota, Hyundai, and other customers. Its existing technical route remains intact for compatibility.

## Global layout contract

Desktop reserves left clearance for Session Intelligence and Administrator View-As. Mobile removes that offset. Bottom clearance protects page actions and notices from Ask Celar AI. The assistant, View-As behavior, and session controls remain functionally unchanged.

## Acceptance gates before merge

- [ ] Enterprise defaults only when no saved view exists.
- [ ] Classic restores the current interface without route or workflow loss.
- [ ] Switching views does not reload or change the active workspace.
- [ ] All four view/theme combinations work.
- [ ] Every registered module receives consistent page identity.
- [ ] Module Management shows all authorized modules on one page.
- [ ] Module 006 displays as Customer Pipelines with a neutral icon.
- [ ] Left controls and Ask Celar AI do not cover page content.
- [ ] Desktop, tablet, mobile, keyboard, reduced-motion, and production-build checks pass.
