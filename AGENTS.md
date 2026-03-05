# AGENTS.md instructions for C:\Dev\AuraMark

<INSTRUCTIONS>
## Skills

Skill 是存放在 `SKILL.md` 的本地工作流指引，用于让 Codex 在本项目内稳定执行固定流程。

### Available skills

- auramark-e2e-autopilot: AuraMark 端到端自愈开发闭环（实现-编译-测试-启动-截图-日志与UI检查-自动修复-复测），在最大迭代次数内追求全绿或输出阻塞原因。 (file: C:/Dev/AuraMark/.codex/skills/auramark-e2e-autopilot/SKILL.md)

### How to use skills

- 触发规则：当用户点名 skill（例如 `$auramark-e2e-autopilot` 或直接说 “用 auramark-e2e-autopilot”），或需求明显匹配该 skill 描述时，本轮必须使用它。
- 使用方式：先读取对应 `SKILL.md`，只加载完成任务所需的最小 references/scripts，避免一次性加载全部。
- 自动化闭环：禁止无限循环；严禁自动更新 UI baseline（必须人工确认）。
</INSTRUCTIONS>

