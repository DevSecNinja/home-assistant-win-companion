# Changelog

## [0.10.1](https://github.com/DevSecNinja/home-assistant-win-companion/compare/v0.10.0...v0.10.1) (2026-09-04)


### Bug Fixes

* **deps:** update dependency sharp to v0.35.4 [automerge] ([#232](https://github.com/DevSecNinja/home-assistant-win-companion/issues/232)) ([02e5756](https://github.com/DevSecNinja/home-assistant-win-companion/commit/02e575622685ad7444d37c0886b70b50736faec2))
* **updates:** time out stalled downloads ([#224](https://github.com/DevSecNinja/home-assistant-win-companion/issues/224)) ([ed24c01](https://github.com/DevSecNinja/home-assistant-win-companion/commit/ed24c01bb5096f3020ed1ed973137a77e503f6f4))

## [0.10.0](https://github.com/DevSecNinja/home-assistant-win-companion/compare/v0.9.0...v0.10.0) (2026-08-19)


### Features

* **deps:** migrate from xunit v2 to xunit.v3 ([#203](https://github.com/DevSecNinja/home-assistant-win-companion/issues/203)) ([6d59db1](https://github.com/DevSecNinja/home-assistant-win-companion/commit/6d59db1eac1a4571f31e63ed141b70ae3024c33d))


### Bug Fixes

* improve update verification logging and sensor filter width ([#202](https://github.com/DevSecNinja/home-assistant-win-companion/issues/202)) ([87c3107](https://github.com/DevSecNinja/home-assistant-win-companion/commit/87c310706435a8010869c6555902b5bd106cf216))
* **ui:** preserve sensor source polling cadence ([#221](https://github.com/DevSecNinja/home-assistant-win-companion/issues/221)) ([bd5c93f](https://github.com/DevSecNinja/home-assistant-win-companion/commit/bd5c93f075c44815871d29569e665893cf0ce161))

## [0.9.0](https://github.com/DevSecNinja/home-assistant-win-companion/compare/v0.8.0...v0.9.0) (2026-08-18)


### Features

* **sensors:** add UTC offset to time zone sensor ([#197](https://github.com/DevSecNinja/home-assistant-win-companion/issues/197)) ([c786459](https://github.com/DevSecNinja/home-assistant-win-companion/commit/c78645938eb335a30b413b00957b99eb4f294ff3))
* **sensors:** add WireGuard status sensor ([#196](https://github.com/DevSecNinja/home-assistant-win-companion/issues/196)) ([8ec277e](https://github.com/DevSecNinja/home-assistant-win-companion/commit/8ec277e5054d2d5f9ec5e09fb95703529089de70))
* **ui:** add search bar to sensors overview ([#177](https://github.com/DevSecNinja/home-assistant-win-companion/issues/177)) ([9b57129](https://github.com/DevSecNinja/home-assistant-win-companion/commit/9b571296d31b995f46351c3735bf2afa62a09b19))
* **ui:** display HA Core and OS version in connection status card ([#194](https://github.com/DevSecNinja/home-assistant-win-companion/issues/194)) ([d7fa2be](https://github.com/DevSecNinja/home-assistant-win-companion/commit/d7fa2beb83c619eb6ce41b497ee6567418470484))
* **ui:** polish home screen URL display, add about section, and align media idle state ([#175](https://github.com/DevSecNinja/home-assistant-win-companion/issues/175)) ([3fbe101](https://github.com/DevSecNinja/home-assistant-win-companion/commit/3fbe101c7eaa035954606cd87f6782784708f10c))
* **ui:** refresh sensor previews automatically ([#198](https://github.com/DevSecNinja/home-assistant-win-companion/issues/198)) ([a5e50f7](https://github.com/DevSecNinja/home-assistant-win-companion/commit/a5e50f7ca5c1feffe00d6bb2016d7cfc48b946e3))

## [0.8.0](https://github.com/DevSecNinja/home-assistant-win-companion/compare/v0.7.0...v0.8.0) (2026-08-17)


### Features

* **github-release:** update release jdx/mise ( v2026.7.18 ➔ v2026.8.0 ) [automerge] ([#164](https://github.com/DevSecNinja/home-assistant-win-companion/issues/164)) ([9b6b34f](https://github.com/DevSecNinja/home-assistant-win-companion/commit/9b6b34f80d32550b62d6ef605643837e804e764a))
* **mise:** update tool zizmor ( 1.28.0 ➔ 1.29.0 ) [automerge] ([#165](https://github.com/DevSecNinja/home-assistant-win-companion/issues/165)) ([b19c170](https://github.com/DevSecNinja/home-assistant-win-companion/commit/b19c170adcdf2b5566c34a2019087063ef80826b))
* **sensors:** add AC power / battery charging state sensor ([#162](https://github.com/DevSecNinja/home-assistant-win-companion/issues/162)) ([1fb5cfa](https://github.com/DevSecNinja/home-assistant-win-companion/commit/1fb5cfad787d3d64c367b651effd47d0742fa41e))
* **sensors:** add currently playing media title/app sensor ([#171](https://github.com/DevSecNinja/home-assistant-win-companion/issues/171)) ([b415a1c](https://github.com/DevSecNinja/home-assistant-win-companion/commit/b415a1cb6b34cb2b4139b27d0aa43463c9a51393))
* **sensors:** add pending reboot binary_sensor ([#169](https://github.com/DevSecNinja/home-assistant-win-companion/issues/169)) ([c43c664](https://github.com/DevSecNinja/home-assistant-win-companion/commit/c43c664414541ef7a799747dce26d4fffacd3f30))
* **updating:** implement auto-update download, verification, and install ([#172](https://github.com/DevSecNinja/home-assistant-win-companion/issues/172)) ([d7fb279](https://github.com/DevSecNinja/home-assistant-win-companion/commit/d7fb2791b517279b9cd6ef617b2f42466eebfe40))


### Bug Fixes

* **app:** prevent crash on unhandled UI exceptions in update check ([#170](https://github.com/DevSecNinja/home-assistant-win-companion/issues/170)) ([02c5eac](https://github.com/DevSecNinja/home-assistant-win-companion/commit/02c5eac88a9e4759ea3bf711cded091b1526696a))
* **mise:** update tool pipx:checkov ( 3.3.8 ➔ 3.3.9 ) [automerge] ([#168](https://github.com/DevSecNinja/home-assistant-win-companion/issues/168)) ([8191501](https://github.com/DevSecNinja/home-assistant-win-companion/commit/819150111cf6d0f61d1644043d0acab77c47c81d))
* **sensor:** use update_location webhook for device tracker ([#173](https://github.com/DevSecNinja/home-assistant-win-companion/issues/173)) ([1276796](https://github.com/DevSecNinja/home-assistant-win-companion/commit/1276796a15283d7778d91e7f93982d5decc07bc9))

## [0.7.0](https://github.com/DevSecNinja/home-assistant-win-companion/compare/v0.6.1...v0.7.0) (2026-08-14)


### Features

* add opt-in Location sensor ([#155](https://github.com/DevSecNinja/home-assistant-win-companion/issues/155)) ([4be4a0c](https://github.com/DevSecNinja/home-assistant-win-companion/commit/4be4a0c15b2592d2245a5fe6d83ea6ff246c3990))


### Bug Fixes

* **deps:** update dependency microsoft.win32.systemevents ( 10.0.0 ➔ 10.0.10 ) [automerge] ([#151](https://github.com/DevSecNinja/home-assistant-win-companion/issues/151)) ([f564a89](https://github.com/DevSecNinja/home-assistant-win-companion/commit/f564a899d1ed7e8d8a750f5f7311a5736b9cd16e))
* **github-release:** update release jdx/mise ( v2026.7.14 ➔ v2026.7.18 ) [automerge] ([#152](https://github.com/DevSecNinja/home-assistant-win-companion/issues/152)) ([e147ee0](https://github.com/DevSecNinja/home-assistant-win-companion/commit/e147ee023a099de12e370b6b71f7cba8d94e3023))
* **mise:** update tool node ( 24.18.0 ➔ 24.18.1 ) [automerge] ([#154](https://github.com/DevSecNinja/home-assistant-win-companion/issues/154)) ([f72e6cb](https://github.com/DevSecNinja/home-assistant-win-companion/commit/f72e6cbeec5aace30c89c10620719585421c793c))

## [0.6.1](https://github.com/DevSecNinja/home-assistant-win-companion/compare/v0.6.0...v0.6.1) (2026-08-11)


### Bug Fixes

* restore overview icon and tray activation ([#146](https://github.com/DevSecNinja/home-assistant-win-companion/issues/146)) ([7ac9d37](https://github.com/DevSecNinja/home-assistant-win-companion/commit/7ac9d37ab454ea6f1b5f474b70c96334e7776584))

## [0.6.0](https://github.com/DevSecNinja/home-assistant-win-companion/compare/v0.5.0...v0.6.0) (2026-08-11)


### Features

* **ui:** refresh companion navigation and settings ([#143](https://github.com/DevSecNinja/home-assistant-win-companion/issues/143)) ([4589e07](https://github.com/DevSecNinja/home-assistant-win-companion/commit/4589e07873f6a8fcd1bcb55117bb4dab14f74244))


### Bug Fixes

* reliably activate background window from tray ([#142](https://github.com/DevSecNinja/home-assistant-win-companion/issues/142)) ([10b3884](https://github.com/DevSecNinja/home-assistant-win-companion/commit/10b388492cb382dc7135abac87edb1cf1304ef53))
* **ui:** polish connection status layout ([#140](https://github.com/DevSecNinja/home-assistant-win-companion/issues/140)) ([6fd295a](https://github.com/DevSecNinja/home-assistant-win-companion/commit/6fd295a15ca7f2335e1085d3561fb971ee11977b))

## [0.5.0](https://github.com/DevSecNinja/home-assistant-win-companion/compare/v0.4.1...v0.5.0) (2026-08-10)


### Features

* add dedicated settings page ([#132](https://github.com/DevSecNinja/home-assistant-win-companion/issues/132)) ([fff1f09](https://github.com/DevSecNinja/home-assistant-win-companion/commit/fff1f09e70dc90900b8833fc44ed1b14521cad85))


### Bug Fixes

* reduce microphone activity latency ([#131](https://github.com/DevSecNinja/home-assistant-win-companion/issues/131)) ([d30c660](https://github.com/DevSecNinja/home-assistant-win-companion/commit/d30c66068dd6242403ddffda05eb8d40c4840dd1))

## [0.4.1](https://github.com/DevSecNinja/home-assistant-win-companion/compare/v0.4.0...v0.4.1) (2026-08-10)


### Bug Fixes

* bound reconnect work during outages ([#128](https://github.com/DevSecNinja/home-assistant-win-companion/issues/128)) ([c8f3f20](https://github.com/DevSecNinja/home-assistant-win-companion/commit/c8f3f20e4b315246928ed4f7f932726c7bc9874c))
* improve tray update actions ([#123](https://github.com/DevSecNinja/home-assistant-win-companion/issues/123)) ([2d4a64d](https://github.com/DevSecNinja/home-assistant-win-companion/commit/2d4a64dc071e1b54e79463b462b89e3a51c40f2a))
* refresh WinGet module detection after installation ([#124](https://github.com/DevSecNinja/home-assistant-win-companion/issues/124)) ([6a86aea](https://github.com/DevSecNinja/home-assistant-win-companion/commit/6a86aeae05f2d744b4c8827b5620647a4523f6c9))

## [0.4.0](https://github.com/DevSecNinja/home-assistant-win-companion/compare/v0.3.0...v0.4.0) (2026-08-10)


### Features

* add network identity and Wi-Fi security sensors ([#106](https://github.com/DevSecNinja/home-assistant-win-companion/issues/106)) ([40e4c1a](https://github.com/DevSecNinja/home-assistant-win-companion/commit/40e4c1acc9f348b911267f1e03e441ec91bb3695))
* add serverless sensor catalog demo mode ([#108](https://github.com/DevSecNinja/home-assistant-win-companion/issues/108)) ([9e0cf01](https://github.com/DevSecNinja/home-assistant-win-companion/commit/9e0cf01e0c15d1a58f46fe5ac5b04b44e69627c6))
* add startup update checks ([#118](https://github.com/DevSecNinja/home-assistant-win-companion/issues/118)) ([2fb9964](https://github.com/DevSecNinja/home-assistant-win-companion/commit/2fb99646187379fbfc3d3748ab1c626ade576561))
* add trusted internal network CIDRs ([#117](https://github.com/DevSecNinja/home-assistant-win-companion/issues/117)) ([3a29ee4](https://github.com/DevSecNinja/home-assistant-win-companion/commit/3a29ee4a82069ebed05ab3544da2b6346db87aeb))


### Bug Fixes

* make tray and installer shutdown graceful ([#113](https://github.com/DevSecNinja/home-assistant-win-companion/issues/113)) ([b2f3ce7](https://github.com/DevSecNinja/home-assistant-win-companion/commit/b2f3ce73ae124461db9fd156e6d3e53abdc25034))
* refresh sensor preview after enabling ([#115](https://github.com/DevSecNinja/home-assistant-win-companion/issues/115)) ([5a0c098](https://github.com/DevSecNinja/home-assistant-win-companion/commit/5a0c098bc2c2424e25ca485295419eab7fe716eb))
* **release:** link setup installers in notes ([#111](https://github.com/DevSecNinja/home-assistant-win-companion/issues/111)) ([e310cdc](https://github.com/DevSecNinja/home-assistant-win-companion/commit/e310cdcbc201117789b3d0ea684f199de2212e76))

## [0.3.0](https://github.com/DevSecNinja/home-assistant-win-companion/compare/v0.2.0...v0.3.0) (2026-08-10)


### Features

* add sensor for joined Active Directory domain / workgroup ([#104](https://github.com/DevSecNinja/home-assistant-win-companion/issues/104)) ([f544d65](https://github.com/DevSecNinja/home-assistant-win-companion/commit/f544d6582166c501cc6fa653763b09c7d355f6bc))
* **deps:** Update dependency coverlet.collector ( 6.0.4 ➔ 10.0.1 ) ([b450fc1](https://github.com/DevSecNinja/home-assistant-win-companion/commit/b450fc154107511421229aad2a4731a570a29628))
* **deps:** update dependency microsoft.net.test.sdk ( 17.12.0 ➔ 17.14.1 ) [automerge] ([#96](https://github.com/DevSecNinja/home-assistant-win-companion/issues/96)) ([3c55297](https://github.com/DevSecNinja/home-assistant-win-companion/commit/3c55297515aa7ffe4cd624ff0f068c323552cfa8))
* **deps:** Update dependency Microsoft.NET.Test.Sdk ( 17.14.1 ➔ 18.8.1 ) ([#99](https://github.com/DevSecNinja/home-assistant-win-companion/issues/99)) ([8ce4204](https://github.com/DevSecNinja/home-assistant-win-companion/commit/8ce42043e11b9287ff4a78767bf81df795d0d0ef))
* **deps:** Update dependency xunit.runner.visualstudio ( 2.8.2 ➔ 3.1.5 ) ([#100](https://github.com/DevSecNinja/home-assistant-win-companion/issues/100)) ([dcaf731](https://github.com/DevSecNinja/home-assistant-win-companion/commit/dcaf731e6e919cbf505d2316c89fa092e388c480))
* migrate to .NET 10 ([#110](https://github.com/DevSecNinja/home-assistant-win-companion/issues/110)) ([3816786](https://github.com/DevSecNinja/home-assistant-win-companion/commit/381678680a8bb566dab6d36ad8adb55748b410a3))
* **mise:** update tool zizmor ( 1.26.1 ➔ 1.28.0 ) [automerge] ([#97](https://github.com/DevSecNinja/home-assistant-win-companion/issues/97)) ([99ee342](https://github.com/DevSecNinja/home-assistant-win-companion/commit/99ee342ba260590795df72ce5a1ce01822602435))


### Bug Fixes

* **deps:** update dependency coverlet.collector ( 6.0.2 ➔ 6.0.4 ) [automerge] ([#92](https://github.com/DevSecNinja/home-assistant-win-companion/issues/92)) ([c189c28](https://github.com/DevSecNinja/home-assistant-win-companion/commit/c189c28f9b98e4c6c2337a4fd2bea59dfbe274d5))
* **deps:** update dependency xunit ( 2.9.2 ➔ 2.9.3 ) [automerge] ([#93](https://github.com/DevSecNinja/home-assistant-win-companion/issues/93)) ([f30053e](https://github.com/DevSecNinja/home-assistant-win-companion/commit/f30053e8b7d525d22b2c99eca4c1a7471d5e4905))
* **github-release:** update release jdx/mise ( v2026.7.12 ➔ v2026.7.14 ) [automerge] ([#88](https://github.com/DevSecNinja/home-assistant-win-companion/issues/88)) ([6a22ad5](https://github.com/DevSecNinja/home-assistant-win-companion/commit/6a22ad5116ae718e1e554bd405988613d8782a64))
* **mise:** update tool pipx:checkov ( 3.3.6 ➔ 3.3.8 ) [automerge] ([#94](https://github.com/DevSecNinja/home-assistant-win-companion/issues/94)) ([58072bd](https://github.com/DevSecNinja/home-assistant-win-companion/commit/58072bd34e55f15fb253b3df88fcd40b4c2707a4))
* track Inno Setup compiler pin with Renovate ([d33ef4d](https://github.com/DevSecNinja/home-assistant-win-companion/commit/d33ef4db0b64e9ee40d94ce349fec33f71911b1c))

## [0.2.0](https://github.com/DevSecNinja/home-assistant-win-companion/compare/v0.1.0...v0.2.0) (2026-08-09)


### ⚠ BREAKING CHANGES

* the executable is now WindowsCompanion.exe and user data lives in %LOCALAPPDATA%\WindowsCompanion\. Release archives are named WindowsCompanion-<version>-win-<arch>.zip. Upgrading users should expect to sign in once.

### Features

* **brand:** add original project identity and reproducible assets ([53403e7](https://github.com/DevSecNinja/home-assistant-win-companion/commit/53403e701519eab419c19649e22bd0520567f7fc))
* **brand:** add original project identity and reproducible assets ([c4c6fc8](https://github.com/DevSecNinja/home-assistant-win-companion/commit/c4c6fc8bd8bdc10852b0aaa6d0e8b61a73ef2640))
* **deps:** update dependency sharp ( 0.34.5 ➔ 0.35.0 ) [security] ([#80](https://github.com/DevSecNinja/home-assistant-win-companion/issues/80)) ([83cb2a2](https://github.com/DevSecNinja/home-assistant-win-companion/commit/83cb2a26e914a7f6bea425ab40e3c1b1ddc2ed99))
* **installer:** add per-user Windows setup ([d867032](https://github.com/DevSecNinja/home-assistant-win-companion/commit/d867032df21e9cdab0a3f9a01b242218a5df83d5))
* **installer:** add per-user Windows setup ([96ddacb](https://github.com/DevSecNinja/home-assistant-win-companion/commit/96ddacb77f5eed745229d74a7d59b20b3f3c3d97))
* **mise:** Update tool node ( 22.23.2 ➔ 24.18.0 ) ([bb4aca8](https://github.com/DevSecNinja/home-assistant-win-companion/commit/bb4aca8f5178360aca63c1c4bb9c2596a11b3ae9))
* **mise:** Update tool node ( 22.23.2 ➔ 24.18.0 ) ([946b70b](https://github.com/DevSecNinja/home-assistant-win-companion/commit/946b70b3d0c7a5aad4fd512d2b2bb7eb3166d5e9))


### Bug Fixes

* **brand:** compare generated PNGs by pixels, not compressed bytes ([360880d](https://github.com/DevSecNinja/home-assistant-win-companion/commit/360880d13168bdaf908a56b5528b10890e297b41))
* **build:** align installer with renamed project ([cd49d76](https://github.com/DevSecNinja/home-assistant-win-companion/commit/cd49d76703f541dd9ab7c722c9afacebb783e36b))
* **deps:** update dependency sharp ( 0.35.0 ➔ 0.35.3 ) [automerge] ([#83](https://github.com/DevSecNinja/home-assistant-win-companion/issues/83)) ([c4b0b5f](https://github.com/DevSecNinja/home-assistant-win-companion/commit/c4b0b5f292fefd73e789a10290c1370b32c7f3f3))
* repair Release builds and submit the sign-in form on Enter ([2ba81da](https://github.com/DevSecNinja/home-assistant-win-companion/commit/2ba81daeba113d47961cfaaf7cc1e7a59910e787))
* repair Release builds and submit the sign-in form on Enter ([0348977](https://github.com/DevSecNinja/home-assistant-win-companion/commit/03489777b6199d0a24949d02cb057071018c98dc))


### Code Refactoring

* rename the product identity to WindowsCompanion ([30b62ab](https://github.com/DevSecNinja/home-assistant-win-companion/commit/30b62ab39b531e3dcab2fa5cf0b5a189d0754d7f))

## 0.1.0 (2026-08-09)


### Features

* add frontmost app sensor ([44afe2e](https://github.com/DevSecNinja/home-assistant-win-companion/commit/44afe2ec7d8a61bcbc2c4f9512c2c67d7545dfdc))
* add hardware, display, environment and storage sensors ([4bb970e](https://github.com/DevSecNinja/home-assistant-win-companion/commit/4bb970e88df34f865b3cefe2ce60a43ff3c048e9))
* add hardware, display, environment and storage sensors ([3890910](https://github.com/DevSecNinja/home-assistant-win-companion/commit/3890910b409c1fff27c326e67846b9a88787ae0f)), closes [#45](https://github.com/DevSecNinja/home-assistant-win-companion/issues/45)
* add meeting context sensors ([87cab58](https://github.com/DevSecNinja/home-assistant-win-companion/commit/87cab587b50f4a04c6f35dc2b9c1e2b72c4ed88e))
* add privacy-aware frontmost app sensor ([7981aa8](https://github.com/DevSecNinja/home-assistant-win-companion/commit/7981aa81f2726d282cd0839ce75c586336bf4e00))
* add Start with Windows toggle ([c0986c5](https://github.com/DevSecNinja/home-assistant-win-companion/commit/c0986c539c8d07adcfc604abb215fe86ccb68f4e))
* add Start with Windows toggle ([bcb6b49](https://github.com/DevSecNinja/home-assistant-win-companion/commit/bcb6b493f4867e1411ca32d9537d1174854f4f1a))
* add Wi-Fi identifier sensors ([01d7232](https://github.com/DevSecNinja/home-assistant-win-companion/commit/01d72327c71a7b9ea13b7c26e962a59968f35f8d))
* add WinGet update sensor ([70d12cf](https://github.com/DevSecNinja/home-assistant-win-companion/commit/70d12cf268b89e91a02ce71b7586fb63c6c09e1e))
* allow changing the server URL ([dc2203d](https://github.com/DevSecNinja/home-assistant-win-companion/commit/dc2203d2bdc90fe24bd4d0eb58952e7dbd57f473))
* **app:** add OAuth login and native shell ([0bbe88a](https://github.com/DevSecNinja/home-assistant-win-companion/commit/0bbe88a3b6dba0b4ead9098d9a69c42d74f5b24d))
* **app:** expose auto-start in status overview ([dff35c8](https://github.com/DevSecNinja/home-assistant-win-companion/commit/dff35c83b26ebd45a5f5cd891d0c48a66b508e63))
* **core:** implement Home Assistant foundation ([e332133](https://github.com/DevSecNinja/home-assistant-win-companion/commit/e33213380828f4a012c47b315b86a41bb0af4571))
* detect sleep, sign-out and shutdown transitions ([d98f494](https://github.com/DevSecNinja/home-assistant-win-companion/commit/d98f494e895ec8b13a2a60fba60d4fd0916af029))
* detect sleep, sign-out and shutdown transitions ([d50d550](https://github.com/DevSecNinja/home-assistant-win-companion/commit/d50d550e4e36d468d0430ba9e0850a2030d2c2ac))
* merge meeting context sensors ([#22](https://github.com/DevSecNinja/home-assistant-win-companion/issues/22)) ([681eb8e](https://github.com/DevSecNinja/home-assistant-win-companion/commit/681eb8e72c6edebd05e1e11730853c56af61c744))
* merge server URL changes ([#37](https://github.com/DevSecNinja/home-assistant-win-companion/issues/37)) ([b6f1433](https://github.com/DevSecNinja/home-assistant-win-companion/commit/b6f143375741c4775f2b5f9a5b68806d5a6dd6fb))
* merge Wi-Fi identifier sensors ([#38](https://github.com/DevSecNinja/home-assistant-win-companion/issues/38)) ([b16f944](https://github.com/DevSecNinja/home-assistant-win-companion/commit/b16f944aff67c49af2856d7dcfd2d2e57190e608))
* merge WinGet update sensor ([#25](https://github.com/DevSecNinja/home-assistant-win-companion/issues/25)) ([5bf52ed](https://github.com/DevSecNinja/home-assistant-win-companion/commit/5bf52ed534dc80c90481677eaf7bc11e5d219abb))
* **notifications:** add mobile app local push ([deb293d](https://github.com/DevSecNinja/home-assistant-win-companion/commit/deb293d0f942e73e0eb411de803e215153b76be5))
* **sensors:** add catalog and health reporting ([0243ac9](https://github.com/DevSecNinja/home-assistant-win-companion/commit/0243ac9598f79c63e4259325d6f5273672a90eee))
* **sensors:** add opt-in IPv6 and LAN MAC sensors ([19cc233](https://github.com/DevSecNinja/home-assistant-win-companion/commit/19cc233a8e8808118bb394864cbeff4d0b47648a))
* **sensors:** add opt-in IPv6 and LAN MAC sensors ([46225e7](https://github.com/DevSecNinja/home-assistant-win-companion/commit/46225e73ab86894f9241e3bd7415c926db5318f9))
* **sensors:** make system state opt-in behind a limits warning ([cb727fa](https://github.com/DevSecNinja/home-assistant-win-companion/commit/cb727fa72b97c15346cd076cf7524b49a15e3cac))
* separate internal and external Home Assistant URLs ([25a1ee1](https://github.com/DevSecNinja/home-assistant-win-companion/commit/25a1ee13f24f350a462f8e72c42c7b66d5d1d463))
* separate internal and external Home Assistant URLs ([92acac9](https://github.com/DevSecNinja/home-assistant-win-companion/commit/92acac9b3ed49dc352ac151d38e74edc0bfbafe8)), closes [#46](https://github.com/DevSecNinja/home-assistant-win-companion/issues/46)
* **ui:** explain sensor update and resource use ([04427ed](https://github.com/DevSecNinja/home-assistant-win-companion/commit/04427ed75bd920b844c60b740ddcbed94cea0f53))
* **ui:** explain sensor update and resource use ([f0bc7bc](https://github.com/DevSecNinja/home-assistant-win-companion/commit/f0bc7bc3806c7b83130fdb5c9643093833ddbe70))
* **ui:** suggest useful sensor automations ([b55ba5f](https://github.com/DevSecNinja/home-assistant-win-companion/commit/b55ba5fadaf4ddd9ec077be13d931e229ae7de34))
* **ui:** suggest useful sensor automations ([e4644b2](https://github.com/DevSecNinja/home-assistant-win-companion/commit/e4644b2af810b8ae981d337b3b81dbc68e2254e9))


### Bug Fixes

* **app:** reject non-HTTP server endpoints safely ([0844940](https://github.com/DevSecNinja/home-assistant-win-companion/commit/0844940451a713e2f0dc80331318698493b46ba9))
* **app:** reject non-HTTP server endpoints safely ([c48b3a2](https://github.com/DevSecNinja/home-assistant-win-companion/commit/c48b3a2f37d6cdfc3ab57be5ab45b9c7a357f408))
* **app:** serialize connection lifecycle against route switching ([9cabbae](https://github.com/DevSecNinja/home-assistant-win-companion/commit/9cabbae216de173800bf76d1e9537f8529152ce3))
* **app:** shut down cleanly before rebuilding ([0070bfe](https://github.com/DevSecNinja/home-assistant-win-companion/commit/0070bfe303c03a0f08fbea4ffdb136060caf6625))
* **app:** shut down cleanly before rebuilding ([8e00763](https://github.com/DevSecNinja/home-assistant-win-companion/commit/8e00763d47ac3bd36f648b59dba8c8c5bc2b31c7))
* **auth:** handle HTTP to HTTPS redirects ([3b48d4a](https://github.com/DevSecNinja/home-assistant-win-companion/commit/3b48d4a8586e4b0cf4c20990872ddd79ed49457f))
* **ci:** merge Windows CI repairs ([#52](https://github.com/DevSecNinja/home-assistant-win-companion/issues/52)) ([b152257](https://github.com/DevSecNinja/home-assistant-win-companion/commit/b1522579be6f765ff95ae774a74e70b7c2bddd67))
* **ci:** normalize YAML line endings ([d31e96f](https://github.com/DevSecNinja/home-assistant-win-companion/commit/d31e96f4b276648cc569555dd185b0cfb5d97620))
* **ci:** restore Windows runtime pack ([b152257](https://github.com/DevSecNinja/home-assistant-win-companion/commit/b1522579be6f765ff95ae774a74e70b7c2bddd67))
* **ci:** restore Windows runtime pack ([0ee5679](https://github.com/DevSecNinja/home-assistant-win-companion/commit/0ee5679e791de90e82d42f83486402b1bff5cd67))
* **connection:** default to one Home Assistant URL ([acefded](https://github.com/DevSecNinja/home-assistant-win-companion/commit/acefded7f474f1853793b2f2dc1d541679d874c5))
* **connection:** default to one Home Assistant URL ([41969bb](https://github.com/DevSecNinja/home-assistant-win-companion/commit/41969bb2562f4a30c7caf1f9ef3f5700dff40e31))
* keep non-HTTP endpoints out of the merged connection settings ([e0efba3](https://github.com/DevSecNinja/home-assistant-win-companion/commit/e0efba35c9634e0877933f42fd1ecb221d571f7c))
* resolve connection races and rejections ([1b15c67](https://github.com/DevSecNinja/home-assistant-win-companion/commit/1b15c67656a1e37e23744324e0c015b9580aa6b4))
* **sensors:** replace stale update trigger ([caed1db](https://github.com/DevSecNinja/home-assistant-win-companion/commit/caed1db9c94eecc2090676d762d65ebb934e2ecf))
* **sensors:** retire removed entities ([b1ad9af](https://github.com/DevSecNinja/home-assistant-win-companion/commit/b1ad9afe0dae2d05412b81c70774e3c8aa821bdf))
* **sensors:** send only accepted update fields ([468a1b2](https://github.com/DevSecNinja/home-assistant-win-companion/commit/468a1b2a617c2c3cd467cd48fcb5fd2eaec67686))
* **sensors:** synchronize frontmost debounce cancellation ([0cb7ecc](https://github.com/DevSecNinja/home-assistant-win-companion/commit/0cb7eccd8b124b7ca98123c1f722d969ee59e1d1))
* synchronise lifecycle push cancellation and pump shutdown ([4192616](https://github.com/DevSecNinja/home-assistant-win-companion/commit/41926161f85e115822409c51f142467d818efffa))
* **ui:** nest frontmost app detail setting ([f8fc629](https://github.com/DevSecNinja/home-assistant-win-companion/commit/f8fc629ede29a3d73f8ccbf197e10eaf109bdf90))
* **ui:** nest frontmost app detail setting ([09c67cc](https://github.com/DevSecNinja/home-assistant-win-companion/commit/09c67cc7bb9bd619a7b5716189a92b5adf235130))
* **ui:** open the companion at a comfortable size ([7af6fdc](https://github.com/DevSecNinja/home-assistant-win-companion/commit/7af6fdccdf16ba5969845d6c48a7abea44e046d0))
* **ui:** open the companion at a comfortable size ([12a4195](https://github.com/DevSecNinja/home-assistant-win-companion/commit/12a41959240601b8d5c49706e650b14537050915))
* **ui:** simplify sensor impact explanations ([58e8714](https://github.com/DevSecNinja/home-assistant-win-companion/commit/58e871426ba1c7d8ce0184fd76ed90d9ba21b474))
* **ui:** simplify sensor impact explanations ([5e6e41e](https://github.com/DevSecNinja/home-assistant-win-companion/commit/5e6e41e0480707198a0cf6a67a8bb0c14d387af3))


### Continuous Integration

* automate unsigned Windows releases ([0391632](https://github.com/DevSecNinja/home-assistant-win-companion/commit/0391632a2d400d81d4eebd48181c3d6341848e19))
