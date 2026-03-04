# Ripcord Receipts Specification

## Overview

A **Receipt** is canonical JSON documenting an erasure job (files processed, bytes, verification).  
An **Attestation** binds a receipt hash (SHA-256) and may include an X.509 signature.

- Canonicalization: property order is lexicographic, UTF-8, no indentation.
- Hash: `SHA-256(receiptCanonicalJson)`, hex-lower.
- Optional signature: RSA-PKCS#1 v1.5 with SHA-256 over the same canonical bytes.

## Schema

See `reports/schemas/receipt.schema.json`. Required fields:
- `schemaVersion` = `"1.0"`
- `jobId`, timestamps (`createdUtc`, `startedUtc`, `completedUtc`)
- `machine`, `user`
- `profileId`, `profileName`
- `dryRun`
- `stats` (`filesProcessed`, `filesFailed`, `filesDeleted`, `bytesOverwritten`)
- `items[]` (per-file results)

## Merkle Root (Items)

To support partial disclosure, a Merkle tree (SHA-256) is computed over **item canonical JSON bytes**:
- Leaf hash = `SHA256(0x00 || leaf)`
- Node hash = `SHA256(0x01 || left || right)`
- Odd duplication (last node duplicated).

The UI/CLI can show `ItemsMerkleRootHex`.

## Attestation

```json
{
  "schemaVersion": "1.0",
  "receiptHashAlg": "SHA-256",
  "receiptHashHex": "<hex>",
  "createdUtc": "2025-08-31T09:00:00Z",
  "signerSubject": "CN=Ripcord Signing",
  "signerThumbprint": "ABCDEF...",
  "signatureAlg": "RSA-SHA256-PKCS1",
  "signatureB64": "<base64>",
  "certificateB64": "<base64 DER>"
}

Verification steps:

    Rebuild canonical JSON; compute hash; compare to receiptHashHex.

    If signatureB64 present: verify with embedded cert (certificateB64) or known trust store.

Storage & Retention

    Default path: C:\ProgramData\Ripcord\Receipts

    Suggested retention: 7 years (org policy dependent).

    Access control: restrict to Administrators / authorized operators.

Example Receipt (trimmed)

{
  "schemaVersion":"1.0",
  "jobId":"6a0a3a36-8f14-4c2a-9a18-2d7a9678a3f5",
  "createdUtc":"2025-08-31T08:55:12.3456789Z",
  "startedUtc":"2025-08-31T08:54:10Z",
  "completedUtc":"2025-08-31T08:55:10Z",
  "machine":"HOST01",
  "user":"DOMAIN\\alice",
  "profileId":"default",
  "profileName":"Default (3-pass)",
  "dryRun":false,
  "stats":{"filesProcessed":3,"filesFailed":0,"filesDeleted":3,"bytesOverwritten":5242880},
  "items":[{"path":"C:\\tmp\\a.bin","sizeBytes":1048576,"deleted":true,"passes":3,"adsBefore":0,"adsAfter":0}]
}


---

### `docs/ui-wireframes.md`
```markdown
# UI Wireframes (high-level)

## Shell

┌──────────────── Ripcord ────────────────┐
│ Jobs | Free Space | Shadow Purge | Receipts | Settings
│────────────────────────────────────────────────────────
│ [ Path: C:\target\folder ][Add File][Add Folder]
│ │
│ ▸ job item path… [ 45% ] OK │
│ ▸ job item path… [ 12% ] … │
│ │
└──────────────────────────────────────────────────────────┘


## Free Space
- Volume selector, reserve slider, “Dry Run” toggle.
- Progress bar with bytes written and TRIM status.

## Shadow Purge
- Volume input `C:\`
- Options: include client-accessible, older-than-days, dry-run.
- List of snapshots with ID, created date, “Delete” buttons.

## Receipts
- Folder picker, Refresh, Verify, Export.
- List: filename, profile, hash-short, verified badge.

## Settings
- Toggle “Prefer Broker”, “Auto-start Broker”.
- Receipts folder path + browse.
- “Ping Broker” status indicator.