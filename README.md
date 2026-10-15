# Hunyuan3D batch image to GLB

This script targets the running Hunyuan3D-2 Gradio UI at `http://localhost:8080`. It reads the live `/config`, uses the UI's queued `shape_generation` or `generation_all` endpoint, then calls `on_export_click` to apply the UI's mesh processing and GLB export. It keeps the UI's current model settings. Jobs run one at a time.

## Windows setup

Install the small client dependency into your regular Python environment:

```powershell
python -m pip install -r requirements.txt
```

Alternatively, your portable installation at `C:\AI\Hunyuan3D2_WinPortable\python_standalone\python.exe` already has `gradio_client` 1.10.3. Start the Hunyuan UI first.

## Usage

```powershell
python hunyuan_batch.py --input "C:\Assets\Images" --output "C:\Assets\Models" --url http://localhost:8080
```

For this Unity project:

```powershell
python hunyuan_batch.py --input "D:\applications\Unity\Projects\Kenshi_VR\Assets\MyAssets\mesh\weapons" --output "D:\applications\Unity\Projects\Kenshi_VR\Assets\MyAssets\mesh\weapons\Generated" --url http://localhost:8080
```

The script accepts PNG, JPG, JPEG, JFIF, and WEBP images. Add `--shape-only` to skip texture, `--recursive` to mirror input subfolders, `--overwrite` to replace valid existing GLBs, or `--dry-run` to list planned work. `--texture` requires the running UI to support it. `--max-retries 2` and `--timeout 3600` are defaults. Timeout is per queued API call in seconds; raise it for long jobs.

The output folder contains `hunyuan_manifest.json` with each image's status, duration, output path, and errors. Existing structurally valid GLBs are skipped. A GLB is checked for its glTF 2 header and complete JSON and binary chunks before success is recorded. Ctrl+C keeps completed outputs and the manifest.

If a queued call reaches `--timeout`, the script requests cancellation and stops the batch. This avoids submitting a new GPU job while the timed-out one may still be active.

## Discovered local installation

The listener on port 8080 is `C:\AI\Hunyuan3D2_WinPortable\python_standalone\python.exe -s gradio_app.py --mini --profile 2 --turbo`, matching `C:\AI\Hunyuan3D2_WinPortable\Hunyuan3D-2\gradio_app.py`. The live service is Gradio 5.33.2, queue enabled, with `/gradio_api` as its API prefix. `/config` confirms `shape_generation`, `generation_all`, and `on_export_click`. The UI uses shape generation, optional face reduction and texture generation, then an export handler. For textured export, that handler re-exports the textured mesh. For shape export, it removes floaters and degenerate faces and optionally simplifies. The script follows this path through the live queued endpoints. No installation files are changed.

Verified locally: the supplied sword image (`Gemini_Generated_Image_rm5g6jrm5g6jrm5g.jfif`) produced a textured GLB. Two additional weapon images completed sequentially. All three GLBs passed structural checks and were opened by `trimesh`; a rerun skipped existing valid outputs. A terrain texture image failed inside Hunyuan generation and was recorded as failed after two retries.
