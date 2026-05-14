# StardewLivingNPCs

这是一个总仓库，用来统一管理当前 AI NPC 实验项目的两个部分：

- `LivingNPCs/`：我们新写的 SMAPI 行为层 Mod。
- `ValleyTalk/`：基于 dandm1/ValleyTalk 的源码副本，包含一个用于第三方上下文注入的 `ThirdPartyContext` prompt hook。

## 当前目标

LivingNPCs 负责让 NPC 做出安全、可控的小行为；ValleyTalk 负责生成动态对话。

当前集成链路：

```text
LivingNPCs 触发 NPC 行为
-> 记录短期行为记忆
-> 注入 ValleyTalk 的 ThirdPartyContext
-> ValleyTalk 生成对话时读取这些上下文
```

## 开发与部署

编译 LivingNPCs：

```powershell
cd LivingNPCs
dotnet build
```

编译 ValleyTalk：

```powershell
cd ValleyTalk
dotnet build src\ValleyTalk.csproj -p:GamePath="D:\SteamLibrary\steamapps\common\Stardew Valley"
```

构建成功后，`Pathoschild.Stardew.ModBuildConfig` 会自动部署到星露谷的 `Mods` 目录。

## 说明

ValleyTalk 原项目使用 LGPL v3 许可证，许可证文件保留在 `ValleyTalk/LICENSE.txt`。

当前本地 fork 对 ValleyTalk 做了兼容优化：

- 增加 `AllowLocalContentPackDialogueForAi` 配置，默认允许本地已加载内容包的对话样本进入 AI 上下文，所以 SVE/RSV 这类自定义 NPC 不再因为 manifest 没写 `PermitAiUse` 就被全局排除。
- 自定义 NPC 如果没有 ValleyTalk bio 文件，会从 Stardew Valley 1.6 的 `Data/Characters` 自动生成轻量 biography，至少包含年龄分类、地区、礼貌程度、社交倾向、乐观程度、是否可恋爱和家庭/朋友关系。

这个仓库先作为个人开发总仓库使用。之后如果要公开发布，建议补充更完整的版权、许可、安装和配置说明。
