# Changelog

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
