# Schema and artifact discovery report

**Status:** Artifact inventory completed; source-content review and schema catalogue remain pending. An initial application-owned schema has been designed and migrated independently; see [the schema design](initial-schema.md).

## Verified findings

- The expected W1 and platform archives are present locally as files named `w1` and `platform` under the dedicated `BC\Artifacts` directory. Both are ZIP archives; neither was modified, and no files were extracted to disk.
- `w1` is 372,706,292 bytes and contains `manifest.json`, `database/Cronus.bclicense`, and `database/Demo Database BC (28-0).bak`. Its manifest reports W1 version `28.4.53241.0`, platform version `28.0.53152.0`, and country `w1`. The manifest's `licenseFile` points to the demo/runtime `Cronus.bclicense`; it is not evidence of source-code reuse rights.
- `platform` is 1,374,878,912 bytes and contains 6,955 archive entries, including 223 `.app` packages and 222 `*.Source.zip` archives. Package names include the Base Application. The W1 manifest identifies its paired platform build as `28.0.53152.0`. Inner source-archive members and AL source contents have not been inspected.
- The presence of source-archive packages is verified; whether the 28.4 archives contain inspectable AL source has not yet been verified. Compiled `.app` packages and platform binaries are not treated as losslessly decompilable source.
- A separate extracted `Dynamics.365.BC.18056.W1.DVD` directory also exists, but it is an older release and is not the requested 28.4 artifact. It is not being used as the schema source.
- The platform archive includes third-party license files, but no applicable Business Central on-premises 28.4 product EULA or artifact-specific source-use grant was verified in the inspected archive inventory. The older DVD's license files are for a different release and do not establish terms for these archives.
- The official [Microsoft Dynamics 365 Business Central Online Product Terms](https://www.microsoft.com/licensing/terms/productoffering/Dynamics365BusinessCentral/OL) are publicly reachable but concern the Online offering; they do not establish rights for these on-premises artifacts or the user's entitlement to them.
- No artifact media or sample database has been copied into the repository.

## Constraints and open verification

- The platform archive has no independently verified top-level version manifest. Its pairing with W1's `28.0.53152.0` is verified in the W1 manifest, not independently from the platform archive itself.
- Before reading, extracting, copying, modifying, or reusing proprietary AL source, verify the applicable Microsoft license terms and the user's entitlement. The exact terms for this artifact were not established in the archive inventory.
- Do not import the supplied demo backup into the application database; it is sample data, and its `28-0` name alone does not establish compatibility with the W1 manifest version. Verify compatibility and licensing separately if it is ever needed.
- Use source only as permitted metadata/workflow reference. Do not copy Microsoft implementation code, assets, schemas wholesale, or sample data into this independently implemented product.
- Package presence is not proof that every Business Central table, field, relation, permission, or validation rule is available. Any eventual catalogue must retain object name, package/source archive, artifact version, and extraction method as provenance.

## Next discovery work

After the artifact-specific terms and entitlement are verified, inspect only relevant AL source archives and package metadata for table/field definitions, keys, relations, enums, dimensions, company scoping, and posting relationships. Catalogue candidate objects by module and compare them to the initial independent schema; do not create an EF entity for every source table. Until then, the initial schema is based only on independently modeled ERP concepts and must not be described as Business Central metadata-validated.
