# LimeZu asset sharing

`Assets/Art/Tiles/LimeZu/` is paid content and is intentionally excluded from the
public Git repository. Scenes still depend on the exact GUIDs and sprite file IDs
stored in its `.meta` files, so every team member must use one canonical copy.

## Team workflow

1. Keep one versioned ZIP in private storage that only licensed team members can
   access. Preserve the full `Assets/...` path and every `.meta` file.
2. Name releases immutably, for example `limezu-project-v3-<sha256>.zip`. Never
   replace an existing release with different bytes.
3. Extract the ZIP at the Unity project root before opening Unity. Do not copy the
   folder loose because Korean and space-containing filenames have previously been
   corrupted in transit.
4. Record the ZIP SHA-256 in the pull request or team chat. The receiver verifies
   it before opening Unity.
5. When scenes start using additional tiles, send a dependency-only delta ZIP
   containing the new assets and their `.meta` files. A full bundle is only needed
   for initial setup or a deliberate asset-source update.

## Project palettes

The project keeps only generated Tile assets referenced by current scenes. The
large stock palettes were replaced locally by:

- `AshburnVillage/Palettes/Project_Used_Exteriors.prefab`
- `AshburnVillage/Palettes/Project_Used_Interiors.prefab`

Restore the archived stock palettes only when level design needs tiles that are not
in these two palettes, then create and distribute a new canonical bundle version.

## Avoid

- Do not freshly import or re-slice LimeZu on another computer. That generates new
  GUIDs and sprite file IDs.
- Do not send assets without their `.meta` files.
- Do not commit the paid bundle through Git LFS. LFS changes storage mechanics, not
  licensing or public-repository exposure.
- Do not use a live cloud-synced folder as the Unity asset directory. Sync races can
  separate an asset from its `.meta` file.

Each teammate remains responsible for complying with the asset license, including
any per-seat purchase requirement.
