# Changelog

## [2.0.2](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization/compare/v2.0.1...v2.0.2) (2026-05-22)


### Bug Fixes

* fire analyzer-filter guards before _HandlePackageFileConflicts (2.0.2) ([#24](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization/issues/24)) ([d7e30a6](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization/commit/d7e30a6a48b732f48a1393cc91854e5c07e46316))

## [2.0.1](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization/compare/v2.0.0...v2.0.1) (2026-05-22)


### Features

* split ZeroAlloc.Authorization.Generator into a standalone package (2.0.1) ([#22](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization/issues/22)) ([9333bdc](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization/commit/9333bdc609ca68417aaf1b8366f414a6e09b8611))

## [2.0.0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization/compare/v1.2.2...v2.0.0) (2026-05-19)


### ⚠ BREAKING CHANGES

* drop sync IsAuthorized, async IsAuthorizedAsync, sync Evaluate from IAuthorizationPolicy. All policies must now implement async EvaluateAsync only. Sync-completing policies wrap their result in new ValueTask<...>(result) — allocation-free.

### Features

* v2 — source-generated policy registry + [Policy]/[RequirePolicy] rename ([#19](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization/issues/19)) ([5e3b2c7](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization/commit/5e3b2c71412b897145c2fc82283b32dd3cefd93b))


### Documentation

* v2 - rename to [Policy]/[RequirePolicy], async-only IAuthorizationPolicy ([#21](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization/issues/21)) ([9263ec5](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization/commit/9263ec55c36dbc380ab65099e2e9a1afd183c4fc))

## [1.2.2](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization/compare/v1.2.1...v1.2.2) (2026-05-12)


### Bug Fixes

* **readme:** absolute GitHub URLs so nuget.org links resolve ([#17](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization/issues/17)) ([9dfcab4](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization/commit/9dfcab43c0c1d8afe9cd052d7898912d81ab416e))

## [1.2.1](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization/compare/v1.2.0...v1.2.1) (2026-05-07)


### Documentation

* **backlog:** add host-coupling notes for items affecting Mediator.Authorization ([#13](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization/issues/13)) ([cddf38c](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization/commit/cddf38cef1f4d3be19187fb3f60309b75bf020ed))
* **backlog:** mark item [#6](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization/issues/6) (Certify the ZeroAlloc promise) as DONE ([#16](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization/issues/16)) ([3fd239e](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization/commit/3fd239e7f7c11897feca52173c3d4ec5f262137f))

## [1.2.0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization/compare/v1.1.1...v1.2.0) (2026-05-06)


### Features

* **certify:** AOT-enforced allocation gate for happy-path APIs ([#11](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization/issues/11)) ([3cfad38](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization/commit/3cfad383a4c1c230ecd5beebcd96679987078991))

## [1.1.1](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization/compare/v1.1.0...v1.1.1) (2026-05-03)


### Bug Fixes

* **release-please:** drop pre-major flags + rename changelog-types→changelog-sections ([#7](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization/issues/7)) ([dce002f](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization/commit/dce002f5a6c6d4b7756e619c5b45428513327f68))

## [1.1.0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization/compare/v1.0.0...v1.1.0) (2026-05-01)


### Features

* add [Authorize] and [AuthorizationPolicy] attributes ([adb2ae6](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization/commit/adb2ae6c778b71409178df4078b8f605c59c1faf))
* add AuthorizationFailure struct for structured deny info ([f92e6d0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization/commit/f92e6d0536f2aff7ecdc29b862fef5dcf56e21d3))
* add Evaluate/EvaluateAsync via UnitResult&lt;AuthorizationFailure&gt; ([ffb1901](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization/commit/ffb19018f5178b1a323a48527372e5285200ea3a))
* add IAuthorizationPolicy with async overload via default-interface-method ([ea51654](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization/commit/ea51654da3320675f4e9f6b1cde4474f99d7edd0))
* add ISecurityContext + AnonymousSecurityContext ([f6a5dd7](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization/commit/f6a5dd7e57487fb0923f50953150847bdb435210))
* add PolicyEvaluationBenchmarks (BenchmarkDotNet harness) ([715ffc3](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization/commit/715ffc3bbb56e5ea219949031749b51361542794))
* aot smoke test exercises every public path ([3fe4143](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization/commit/3fe414348d65e8e837804997ae04cb10ba6572e1))
* ZeroAlloc.Authorization 1.0 readiness ([6583f9d](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization/commit/6583f9d8153807e966d77e5b0ebfff9cd290f7b7))


### Bug Fixes

* guard AuthorizationFailure.Code against default-struct null ([ab4bae2](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization/commit/ab4bae244d8452487a8aec6802dd4250a5ae1b61))
* harden public API contracts (null-checks, attribute targets, cancellation) ([968c82c](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization/commit/968c82c3a43a3835ee02303b77dfcb34ed9ce1b1))

## Changelog

All notable changes to ZeroAlloc.Authorization will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).
