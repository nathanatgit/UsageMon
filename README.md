# UsageMon · 用量悬浮窗

<p align="center"><img src="docs/hero.png" alt="UsageMon panel and usage tooltip" width="560"></p>

[![Downloads](https://img.shields.io/github/downloads/nathanatgit/UsageMon/total?label=downloads&color=0067c0)](https://github.com/nathanatgit/UsageMon/releases)
[![Latest release](https://img.shields.io/github/v/release/nathanatgit/UsageMon?color=0067c0)](https://github.com/nathanatgit/UsageMon/releases/latest)

Just another simple floating/taskbar monitor, that's all of it.

For your AI usage in WinUI3 style, with Mica and Arcylic material.

一个简单的 WinUI3 风格的 AI 用量显示悬浮窗，可选云母、亚克力材质，支持Claude、ChatGPT、Deepseek（实验性）和KIMI（实验性）。

Simple and native, no expensive settings and fancy UIs. Minimal and clean, click and leave it there.

## Download

Get the latest build from [Releases](https://github.com/nathanatgit/UsageMon/releases/latest), picking x64, x86 or arm64 to match your PC.

| Build | Files | Needs |
|---|---|---|
| Self-contained | `…-portable.zip` (unzip and run `UsageMon.exe`, no install), `….msi`, `….msixbundle` | Nothing else |
| Framework-dependent (smallest) | `…-framework-dependent.zip` | [.NET 8 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) and [Windows App Runtime 2.0](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads) |

The `.msixbundle` is self-signed: import `UsageMon-signing-certificate.cer` into *Trusted People* (or *Trusted Root Certification Authorities*) before installing it.

## UI Screenshots(支持简体中文和英文）

**Panel layouts** — joined or separate, horizontal or vertical, and docked to a screen edge. The percentage text on the rings can be switched on or off in Settings.

![Panel layouts](docs/panel-layouts.png)

**Taskbar meter** — rings with the 5-hour and 7-day percentages beside them.

![Taskbar meter](docs/taskbar.png)

**Settings** — light and dark.

![Settings, light](docs/settings.png)

## License

[MIT](LICENSE). Third-party notices are in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
