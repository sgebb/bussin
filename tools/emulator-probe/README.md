# Emulator probe

Checks what Bussin needs from the [Service Bus emulator](https://github.com/Azure/azure-service-bus-emulator-installer), so feedback on [installer PR #154](https://github.com/Azure/azure-service-bus-emulator-installer/pull/154) (AMQP over WebSockets) is based on what was actually observed.

Needs Docker (or Podman) and Node 22+. Run from the repo root:

```bash
node tools/emulator-probe/run.mjs --accept-eula
```

`--accept-eula` means you accept the [emulator EULA](https://github.com/Azure/azure-service-bus-emulator-installer/blob/main/EMULATOR_EULA.txt) and the [Azure SQL Edge EULA](https://go.microsoft.com/fwlink/?linkid=2139274); the script won't start the containers without it. Results go to `tools/emulator-probe/report.md`, and Bussin's client logs go to `probe-debug.log`.

What it checks:

1. **Management HTTP API (5300):** can entities and counts be listed through the ATOM API that the Administration Client uses, and would a browser be allowed to (CORS headers, preflight with `Authorization`)?
2. **Bussin's own client code over WebSocket:** `bridge.mjs` is a local stand-in for the PR's listener (WebSocket on 15673, piped to the AMQP port 5672). The run then calls the client-js functions Bussin uses: CBS with SAS, peek, peek-lock, settle, dead-letter, receive-by-sequence, schedule/cancel, topic rules, sessions and Watch.
3. **The emulator's own WebSocket listener (5673):** skipped until an image with PR #154 ships. When it's there, it checks which `Origin` headers are accepted, then repeats group 2 directly against it.

Other commands:

```bash
node tools/emulator-probe/run.mjs --no-start      # probe an emulator you already run
node tools/emulator-probe/run.mjs --tag <tag>     # a specific emulator image tag
node tools/emulator-probe/run.mjs --down          # remove the probe containers
```
