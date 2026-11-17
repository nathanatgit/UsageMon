# Privacy Policy / 隐私政策

_Last updated: 2026-11-09_

UsageMon is an open-source Windows app that shows your Codex and Claude Code usage quotas (and, optionally, DeepSeek and Kimi balances). It is not affiliated with or endorsed by OpenAI or Anthropic.

## What the app accesses

- **Claude Code sign-in token.** To read your own quota, UsageMon reads the OAuth access token that Claude Code stores on your PC (`~/.claude/.credentials.json`, or the folder in `CLAUDE_CONFIG_DIR`) and sends it only to `api.anthropic.com` to request your usage. If you enable the optional status-line bridge, it instead reads a local file written by Claude Code's status line.
- **Codex.** UsageMon starts your locally installed Codex CLI (`codex app-server`) and asks it for your rate limits. The CLI talks to OpenAI with your own sign-in; UsageMon never sees your OpenAI credentials.
- **DeepSeek / Kimi (experimental, off by default).** If you turn these on, UsageMon uses the API key you paste in Settings (kept encrypted with Windows DPAPI in `%LOCALAPPDATA%\UsageMon`, or the `DEEPSEEK_API_KEY` / `MOONSHOT_API_KEY` environment variable) and sends it only to `api.deepseek.com` / `api.moonshot.ai` (or `api.moonshot.cn`) to read your balance. If you sign in to Kimi Code with your browser, the resulting token is stored the same way and sent only to `kimi.com` / `kimi.ai` to read your plan quota.
- **Grok / GitHub Copilot (experimental, off by default).** If you turn these on, UsageMon reuses the sign-in those tools already saved (`~/.grok/auth.json`; `%LOCALAPPDATA%\github-copilot\apps.json`) and sends that token only to `grok.com` / `api.github.com` to read your quota. It never writes or refreshes those files. If you use UsageMon's own GitHub sign-in for Copilot instead, that token is stored encrypted like the keys above and sent only to `github.com` / `api.github.com`.

## What the app stores

Settings and a cache of the last usage reading are stored on your PC in `%LOCALAPPDATA%\UsageMon`. API keys and sign-in tokens you enter are kept separately, encrypted with Windows DPAPI (`*.bin` files); they are never written into these files in plain text.

## What the app does not do

- It does not collect, transmit or sell any data to the developer or to any third party other than the provider APIs listed above, and only to retrieve your own usage.
- It has no analytics, telemetry, advertising or crash reporting.
- It does not read other apps' credentials.

## Contact

Questions or concerns: https://github.com/nathanatgit/UsageMon/issues

---

## 隐私政策（中文）

UsageMon 是一款开源的 Windows 应用，用于显示 Codex 与 Claude Code 的用量额度（以及可选的 DeepSeek、Kimi 余额）。本应用与 OpenAI、Anthropic 无关联，亦未获其认可。

**应用会访问的内容**

- **Claude Code 登录令牌**：为读取你自己的额度，UsageMon 读取 Claude Code 保存在本机的 OAuth 访问令牌（`~/.claude/.credentials.json`，或 `CLAUDE_CONFIG_DIR` 指向的目录），并且仅发送至 `api.anthropic.com` 查询用量。若启用可选的状态栏桥接，则改为读取 Claude Code 状态栏写入的本地文件。
- **Codex**：UsageMon 启动你本机已安装的 Codex CLI（`codex app-server`）获取额度。CLI 使用你自己的登录与 OpenAI 通信，UsageMon 不会接触你的 OpenAI 凭据。
- **DeepSeek / Kimi（实验性，默认关闭）**：开启后，使用你在设置中粘贴的 API 密钥（以 Windows DPAPI 加密保存在 `%LOCALAPPDATA%\UsageMon`，或环境变量 `DEEPSEEK_API_KEY` / `MOONSHOT_API_KEY`），仅发送至 `api.deepseek.com` / `api.moonshot.ai`（或 `api.moonshot.cn`）查询余额。若用浏览器登录 Kimi Code，令牌同样加密保存，仅发送至 `kimi.com` / `kimi.ai` 查询套餐额度。
- **Grok / GitHub Copilot（实验性，默认关闭）**：开启后，复用这些工具已保存的登录信息（`~/.grok/auth.json`；`%LOCALAPPDATA%\github-copilot\apps.json`），令牌仅发送至 `grok.com` / `api.github.com` 查询额度；不会写入或刷新这些文件。若改用 UsageMon 自带的 GitHub 登录，令牌同样加密保存，仅发送至 `github.com` / `api.github.com`。

**应用保存的内容**

设置与最近一次用量缓存保存在本机 `%LOCALAPPDATA%\UsageMon`。你输入的 API 密钥和登录令牌单独以 Windows DPAPI 加密保存（`*.bin` 文件），不会以明文写入这些文件。

**应用不会做的事**

- 不会向开发者或上述服务商以外的任何第三方收集、传输或出售数据；访问上述服务商仅用于查询你自己的用量。
- 没有统计分析、遥测、广告或崩溃上报。
- 不会读取其他应用的凭据。

**联系方式**：https://github.com/nathanatgit/UsageMon/issues
