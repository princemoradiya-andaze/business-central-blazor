# Schema and artifact discovery report

**Status:** Artifact inventory and a limited, read-only metadata review are complete. The user confirmed permission to inspect the local artifacts. The application schema remains independently implemented and is aligned only to selected concepts; see [the schema design](initial-schema.md).

## Verified findings

- The expected W1 and platform archives are present locally as files named `w1` and `platform` under the dedicated `BC\Artifacts` directory. Both are ZIP archives and remain unchanged.
- `w1` is 372,706,292 bytes and contains `manifest.json`, `database/Cronus.bclicense`, and `database/Demo Database BC (28-0).bak`. Its manifest reports W1 version `28.4.53241.0`, platform version `28.0.53152.0`, and country `w1`. The manifest's `licenseFile` points to the demo/runtime `Cronus.bclicense`; it is not evidence of source-code reuse rights.
- `platform` is 1,374,878,912 bytes and contains 6,955 archive entries, including 223 `.app` packages and 222 `*.Source.zip` archives. The W1 manifest identifies its paired platform build as `28.0.53152.0`.
- The Base Application source ZIP at `Applications\BaseApp\Source\Base Application.Source.zip` contains 9,247 members, including 8,095 AL files. Thirteen selected foundational table definitions were read from the archive in memory; no archive members were written to disk and the original archives were not changed.
- The limited review covered G/L Account (15), G/L Entry (17), General Journal Line (81), General Journal Batch (232), Currency (4), Currency Exchange Rate (330), Accounting Period (50), Dimension (348), Dimension Value (349), Customer (18), Vendor (23), Item (27), and No. Series (308). It informed the high-level relationships recorded in `initial-schema.md`; it was not an exhaustive object/field catalogue or a review of business logic.
- Compiled `.app` packages and platform binaries were not decompiled or used as a source of implementation code.
- A separate extracted `Dynamics.365.BC.18056.W1.DVD` directory also exists, but it is an older release and is not the requested 28.4 artifact. It is not being used as the schema source.
- No artifact media or sample database has been copied into the repository.

## Constraints and open verification

- The platform archive has no independently verified top-level version manifest. Its pairing with W1's `28.0.53152.0` is verified in the W1 manifest, not independently from the platform archive itself.
- The user explicitly confirmed that they have permission and authorized read-only inspection of the local source artifacts. This report records that user confirmation; it is not an independent legal interpretation of the applicable product terms.
- Inspection was limited to selected metadata. No AL source file, application package, license file, demo database, or implementation code was modified or copied into the repository.
- Do not import the supplied demo backup into the application database; it is sample data, and its `28-0` name alone does not establish compatibility with the W1 manifest version. Verify compatibility and licensing separately if it is ever needed.
- Keep the application independently implemented. Do not copy Microsoft implementation code, assets, schemas wholesale, or sample data into this project.
- Package presence is not proof that every Business Central table, field, relation, permission, or validation rule is available. Any eventual catalogue must retain object name, package/source archive, artifact version, and extraction method as provenance.

## Next discovery work

The selected metadata has informed a first schema alignment, but a full module-by-module catalogue, validation/posting logic review, and dimension-set model remain out of scope for this foundation. Review additional source metadata only when a later feature needs it, using the same permission boundary and recording artifact/version provenance. Do not create an EF entity for every Business Central table.
