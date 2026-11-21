# Changelog

## [0.1.11](https://github.com/st0o0/WebSocket.Rx/compare/v0.1.10...v0.1.11) (2026-11-21)


### Features

* add slopwatch claude code hook for real-time analysis ([247aaeb](https://github.com/st0o0/WebSocket.Rx/commit/247aaeb2c9920113545604c90b45a89469ae086b))
* integrate slopwatch analysis with proper error handling ([1a5b05f](https://github.com/st0o0/WebSocket.Rx/commit/1a5b05fa455c6c67b69571f39c0b8367eb837607))
* **security:** add push trigger for source scan on main ([549ba47](https://github.com/st0o0/WebSocket.Rx/commit/549ba47cb8192572e6226a58b4632d29ba3ed3ef))


### Bug Fixes

* add missing final newlines to pass lint ([2722e4d](https://github.com/st0o0/WebSocket.Rx/commit/2722e4dbee685c802f3941249cc61468e848f466))
* grant pull-requests/issues write permission to labeler and label-sync callers ([b4060f7](https://github.com/st0o0/WebSocket.Rx/commit/b4060f7e826326bcc9c0ac0fc41203bc9cbd8828))
* **security:** add packages:read permission for reusable workflow compatibility ([7c6df75](https://github.com/st0o0/WebSocket.Rx/commit/7c6df75597f4a7059c450333cf9a2646937042ae))
* **security:** wire trivy-config in security workflow ([c638cee](https://github.com/st0o0/WebSocket.Rx/commit/c638cee053886fabeae241e35509a86f84155d33))
* strip BOM, normalize line endings to LF, add .gitattributes ([926d118](https://github.com/st0o0/WebSocket.Rx/commit/926d118894ff19350db050c2f4d4ca30056fde27))
* use CLAUDE_PROJECT_DIR in slopwatch hook for correct working directory ([1b257f6](https://github.com/st0o0/WebSocket.Rx/commit/1b257f6bd26ab44cb60fca2170ff9a1820ac919a))


### Documentation

* extend assertion patterns with xUnit best practices ([f38ee55](https://github.com/st0o0/WebSocket.Rx/commit/f38ee55cb92e3d92895dcbd08fa58107cbb740a4))
* remove em dashes and simplify wording ([ce1408a](https://github.com/st0o0/WebSocket.Rx/commit/ce1408a563bdf704e94f144fce67a5bbd55af332))


### Refactoring

* **security:** remove unused trivy.yaml placeholder ([2d0ac6d](https://github.com/st0o0/WebSocket.Rx/commit/2d0ac6d9dc5a300a1f17e4474d9b0ab76b3b0be7))
* **security:** simplify permissions, add push trigger ([2941913](https://github.com/st0o0/WebSocket.Rx/commit/2941913ebf6c923498145d290c9a897dea0b675b))

## [0.1.10](https://github.com/st0o0/WebSocket.Rx/compare/v0.1.9...v0.1.10) (2026-09-28)


### Features

* add global.json with SDK roll-forward and MTP runner ([278079c](https://github.com/st0o0/WebSocket.Rx/commit/278079c90338ded5e2c287f5c2f32a41f5a92e35))
* decouple release-please from build workflow ([be71ef0](https://github.com/st0o0/WebSocket.Rx/commit/be71ef08fdd5c7c46144d4f107f46e66c389e652))


### Bug Fixes

* make integration tests deterministic with close timeouts and event-driven waits ([87403c2](https://github.com/st0o0/WebSocket.Rx/commit/87403c265ec56759e401ac3fc2d5f5e704eb857f))
* remove top-level permissions restriction breaking release-please ([829fe69](https://github.com/st0o0/WebSocket.Rx/commit/829fe696700a8750caa6d3091ff5aa0bb2ae60b3))
* resolve dispose bug, concurrency issues, and stabilize flaky tests ([5415ae1](https://github.com/st0o0/WebSocket.Rx/commit/5415ae1498e9930fc87e3341837c27e81b59ddd1))
* use softprops/action-gh-release for nupkg upload ([19fd335](https://github.com/st0o0/WebSocket.Rx/commit/19fd3350b435cf2259904d7dfcad4e2327e78e58))
* **ws:** Improve reconnection logic ([ea66ec5](https://github.com/st0o0/WebSocket.Rx/commit/ea66ec5902bc8ba31d7066a93c7fa6c9898ecd57))


### Documentation

* align README badges ([83c351c](https://github.com/st0o0/WebSocket.Rx/commit/83c351c40448286e0fa0f66f1be49ea3dc7396d3))


### Refactoring

* migrate to shared workflows, renovate, and standardized release-please ([9a35148](https://github.com/st0o0/WebSocket.Rx/commit/9a3514868b5ab4e48195f294ffa653238c69730c))
* rename CI jobs for cleaner GitHub check names ([085ede8](https://github.com/st0o0/WebSocket.Rx/commit/085ede8c5a0f8bc3943472d6936c435a4a2b575c))
* replace CodeQL with Trivy filesystem scan ([7b528d1](https://github.com/st0o0/WebSocket.Rx/commit/7b528d173bde0395fff86280ca42d409f9611e32))

## [0.1.9](https://github.com/st0o0/WebSocket.Rx/compare/v0.1.8...v0.1.9) (2026-06-22)


### Bug Fixes

* SendInstant extensions now correctly call SendInstantAsync ([1c6442b](https://github.com/st0o0/WebSocket.Rx/commit/1c6442bc75818a2816527f83e11f6ded18fd9634))
* **test:** wait for connection before sending in encoding test ([64a47c5](https://github.com/st0o0/WebSocket.Rx/commit/64a47c5209bc4d8f8d4980ec410b0d1f96ad6367))
