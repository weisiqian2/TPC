# 安全响应政策（SECURITY.md）

本文件描述如何在本仓库（TPC）中负责任地报告安全问题，以及维护者的响应和处理流程。请在发现安全漏洞或可能导致用户数据泄露/远程代码执行等严重问题时，优先使用下述受控渠道，不要将详细漏洞信息或可利用的 PoC 发布到公开 issue/讨论区，防止被恶意利用。

重要提示
- 首选方式：使用 GitHub 的 Security Advisories（推荐）向仓库报告私密安全问题。
- 备选方式：发送加密邮件到仓库 README 中公开的联系邮箱：wcgwsqll@gmail.com（若使用明文邮件，请勿包含敏感凭据；建议先通过该邮箱索取 GPG 公钥以便加密）。
- 请勿在公开的 Issue、PR 或 Discussions 中贴出完整可利用代码或 PoC。

受影响范围（Scope）
- 本仓库中的源代码与文档（包括 native、agent、WinUI、tpcwei_meshd 等组件）。
- 仓库发布的官方安装包（publish/ 目录及通过 Releases 发布的二进制）。
- 与本项目直接维护、发布的自建网关与服务组件。
不在范围（Out of scope）
- 第三方依赖库的上游漏洞（请同时报告给相关库的维护者）。
- 用户未授权的第三方服务器或非官方镜像环境。

汇报内容模板（建议）
请在提交时尽量包含如下信息，便于快速复现与定位：
- 漏洞标题（简短描述）。
- 影响组件与版本（例如：p2p_core vX.Y，Windows 发布包 2026-05-01）。
- 严重程度估计（低/中/高/严重/危急）并说明原因。
- 可复现步骤（步骤越详细越好）或测试环境配置。
- PoC（优先通过加密邮件或 Security Advisory 私密提交；若必须在报告中包含，请先与维护者沟通）。
- 任意日志、堆栈跟踪、捕获的网络数据、配置文件样例（脱敏后）。
- 报告者联系方式（用于后续沟通与确认）。

加密建议
- 若包含敏感 PoC 或凭证，请先通过邮件与维护者沟通并请求 GPG 公钥进行加密；或使用 GitHub Security Advisory 的私密报告功能。
- 请勿直接在公开渠道贴出密钥、密码或任意未脱敏的用户数据。

响应时间与处理原则
- 确认回执：我们将在收到有效安全报告后 3 个工作日内确认收到（通过邮件或 Security Advisory 回执）。
- 初步响应与分级：我们会在 14 天内完成初步分析并评估严重性；对紧急漏洞（可远程利用、影响广泛）会加急处理。
- 修复与发布：对于明确的安全缺陷，我们会在可行时间内发布补丁；通常目标是在 30–90 天内发布修复版本或给出缓解办法。若需要更长时间，会与报告者协商并给出公开时间表。
- 协调披露：我们优先采用协调披露流程。请在与维护者协商好的公开披露日期前不要公开漏洞细节或 PoC。

优先级分类参考（仅供参考）
- 危急（Critical）：远程无交互可执行任意代码、重大密钥泄露、严重权限提升，影响大量用户。
- 高（High）：可远程利用的严重认证绕过、数据泄露或持久后门。
- 中（Medium）：需要特定条件或本地访问的漏洞，或有限的数据泄露。
- 低（Low）：信息泄露（非敏感）、难以利用的边界情况、UI/文案问题等。

奖赏与致谢
- 当前仓库暂无正式漏洞赏金计划（Bug Bounty）。针对负责披露并提供可复现补丁的报告者，我们会在 RELEASE / README / CONTRIBUTORS 中列出致谢（可匿名）。

法律与良性研究保护
- 我们支持良性安全研究。只要您的研究遵循负责披露原则并且不故意破坏用户数据或服务，我们不会对报告者采取法律行动。
- 请避免进行破坏性测试（例如对生产服务进行 DoS、删除数据等）。

紧急联系方式
- 首选：GitHub Security Advisories（私密）。
- 邮件：wcgwsqll@gmail.com（若需要，请先请求 GPG 公钥以加密敏感内容）。
- 若您无法使用以上渠道，请在 GitHub 上创建 Security Advisory 或在仓库主页查找最新联系方式。

后续步骤（维护者建议）
- 我们建议仓库维护者：
  1. 在 GitHub 仓库启用 Security Advisories（Settings → Security）。
  2. 在 README 顶部明确 LICENSE 与联系方式，避免混淆。
  3. 考虑发布 GPG 公钥用于加密报告。
  4. 明确是否建立漏洞赏金计划或外包 SAST/SOC 服务。

翻译（English summary）

This repository follows a responsible disclosure policy. Preferred reporting channel is GitHub Security Advisories. Alternative is encrypted email to the contact address in the README (wcgwsqll@gmail.com). Do not post PoC or sensitive details in public issues. Provide a detailed report (affected component/version, steps to reproduce, PoC via encrypted channel, logs). Maintainers will acknowledge within 3 business days, perform initial triage within 14 days, and aim to ship fixes within 30–90 days depending on severity. Coordinated disclosure is preferred. No bounty program is currently offered. Good-faith researchers will not face legal action.
