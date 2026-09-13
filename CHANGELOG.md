# Changelog

## [0.1.10](https://github.com/st0o0/WebSocket.Rx/compare/v0.1.9...v0.1.10) (2026-09-13)


### Features

* add global.json with SDK roll-forward and MTP runner ([278079c](https://github.com/st0o0/WebSocket.Rx/commit/278079c90338ded5e2c287f5c2f32a41f5a92e35))
* decouple release-please from build workflow ([be71ef0](https://github.com/st0o0/WebSocket.Rx/commit/be71ef08fdd5c7c46144d4f107f46e66c389e652))


### Bug Fixes

* make integration tests deterministic with close timeouts and event-driven waits ([87403c2](https://github.com/st0o0/WebSocket.Rx/commit/87403c265ec56759e401ac3fc2d5f5e704eb857f))


### Refactoring

* migrate to shared workflows, renovate, and standardized release-please ([9a35148](https://github.com/st0o0/WebSocket.Rx/commit/9a3514868b5ab4e48195f294ffa653238c69730c))
* rename CI jobs for cleaner GitHub check names ([085ede8](https://github.com/st0o0/WebSocket.Rx/commit/085ede8c5a0f8bc3943472d6936c435a4a2b575c))
* replace CodeQL with Trivy filesystem scan ([7b528d1](https://github.com/st0o0/WebSocket.Rx/commit/7b528d173bde0395fff86280ca42d409f9611e32))

## [0.1.9](https://github.com/st0o0/WebSocket.Rx/compare/v0.1.8...v0.1.9) (2026-06-22)


### Bug Fixes

* SendInstant extensions now correctly call SendInstantAsync ([1c6442b](https://github.com/st0o0/WebSocket.Rx/commit/1c6442bc75818a2816527f83e11f6ded18fd9634))
* **test:** wait for connection before sending in encoding test ([64a47c5](https://github.com/st0o0/WebSocket.Rx/commit/64a47c5209bc4d8f8d4980ec410b0d1f96ad6367))
