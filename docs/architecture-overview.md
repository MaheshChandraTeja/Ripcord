# Architecture Overview

## Layers

- **App (WinUI)** — ViewModels orchestrate jobs, emit receipts, optional broker use.
- **Engine** — Shred core: passes, ADS, rename noise, MFT slack, VSS helpers, SSD/TRIM.
- **Broker (elevated)** — Named pipe JSON API, policy gate, calls into Engine.
- **Receipts** — Models, Merkle, JSON/PDF export, verification.
- **Infrastructure** — Logging, configuration, DI.

## Data Flows

1. **Shred Job**
   - App VM → (optional) Broker `shred` → Engine → Progress events → ReceiptEmitter → JSON+attestation saved.

2. **Shadow Purge**
   - App VM → (optional) Broker `vss.*` → VSS actions → optional receipt.

3. **Free Space**
   - App VM → Engine SSD/Trim → summary stored in receipt.

## Security

- Elevation only in Broker; IPC limited to same-user SID.
- Policy validates local absolute paths, allowed verbs.
- Receipts provide auditability with hash/signature.

## Extensibility

- Profiles (overwrite strategies) are data-driven.
- Exporters can add QuestPDF build flag.
- Broker verbs are pluggable.

## Testing

- Unit tests for Engine/Receipts.
- E2E tests simulate cancellation & locked files.
- Benchmarks via BenchmarkDotNet.
