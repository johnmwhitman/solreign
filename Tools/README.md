# Tools

Index of local Python utilities in `Tools/` and `Tools/_Solreign/`:

- `check_crlf.py`: Checks committed files for CRLF line endings. Invocation: `Tools/check_crlf.py` or `python3 Tools/check_crlf.py`.
- `iconsmooth.py`: Slices and converts an input texture sheet into individual smoothed icon states. Invocation: `python3 Tools/iconsmooth.py in.png METRICS <tg/citadel/tau/vxa/vxap/rt_states> OUTPREFIX`.
- `iconsmooth_inv.py`: Reconstructs a texture sheet from individual smoothed icon states. Invocation: `python3 Tools/iconsmooth_inv.py INPREFIX METRICS <tg/citadel/tau/vxa/vxap/rt_states> out.png`.
- `iconsmooth_lib.py`: Shared library defining conversion modes, state tables, and metric parsing for iconsmooth utilities. Invocation: imported by `iconsmooth.py` and `iconsmooth_inv.py` (`import iconsmooth_lib`).
- `update_changelog.py`: Merges changelog part YAML files into the changelog file. Invocation: `python3 Tools/update_changelog.py changelog_file parts_dir [--category CATEGORY]`.
- `actions_changelog_rss.py`: Updates an RSS file on a remote server with updates to the changelog. Invocation: `python3 Tools/actions_changelog_rss.py`.
- `actions_changelogs_since_last_run.py`: Sends updates to a Discord webhook for new changelog entries since the last GitHub Actions publish run. Invocation: `python3 Tools/actions_changelogs_since_last_run.py`.
- `actions_extract_failures.py`: Ingests XML files in an NUnit format and extracts failed tests for CI/CD pipelines. Invocation: `python3 Tools/actions_extract_failures.py`.
- `generate_heat_distortion_textures.py`: Generates textures (noise and soft circle) for heat distortion. Invocation: `python3 Tools/generate_heat_distortion_textures.py`.
- `make_roompack.py`: Analyzes rectangular bounding boxes in a greyscale bitmap to create dungeon room pack configs. Invocation: `python3 Tools/make_roompack.py file [--center] [--offset OFFSET OFFSET]`.
- `_Solreign/check_prototype_collisions.py`: Detects cross-file same-kind prototype ID collisions in SS14 YAML. Invocation: `python3 Tools/_Solreign/check_prototype_collisions.py [PrototypesDir]`.
