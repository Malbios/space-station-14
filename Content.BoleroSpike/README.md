# Bolero WASM Spike

This spike demonstrates a minimal Bolero WebAssembly client that talks to a live Space Station 14 server. The client uses the server's `/status` endpoint, parses the JSON payload, and renders the key information alongside a generated SVG summary card.

## Running the spike locally

1. Start an SS14 server (for example by running `./runserver.sh` in this repository). The default status endpoint listens on `http://localhost:1212/status`.
2. Restore and run the client:
   ```bash
   dotnet watch --project Content.BoleroSpike/Client/Content.BoleroSpike.Client.fsproj run
   ```
   The client reads the target status base address from `wwwroot/appsettings.json` (key `SS14:StatusBaseAddress`) and falls back to `http://localhost:1212/`. Once the page loads it automatically queries `/status`, displays the raw data, and offers a "Refresh status" button for manual testing.

You can change the target server by editing `wwwroot/appsettings.json` or by setting the `SS14:StatusBaseAddress` configuration value when launching the client. The Bolero app expects an SS14-compatible status JSON response.
