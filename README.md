# 兔兔秘密花园存档修改器

这是适用于 Steam Windows 版《兔兔秘密花园》（BUNNY GARDEN）的离线存档修改器。

用户只需运行 EXE，不需要安装 Python，也不需要运行 PowerShell。首次运行启动器会用 Windows 自带的 .NET Framework 编译 EXE；之后可直接运行 `BunnyGardenSaveEditor.exe`。

## 功能

- 修改指定非空槽位的金钱
- 修改花奈、凛、美羽香的总好感度
- 可选跳转游戏内日期
- 自动识别标准 Steam 安装目录，也可手动选择 `UserData`
- 自动备份、原子写入、写后读回验证
- 当 Steam 自动云存档存在内容完全相同的镜像时，可同步更新镜像

本工具不解锁路线、不伪造成就、不修改每日好感度，也不会上传或收集存档。

## 使用方法

1. 完全退出游戏和 Steam 云同步等待状态。
2. 双击 `Launch-BunnyGardenSaveEditor.cmd`。
3. 选择非空槽位，填写数值；如需跳日期，再勾选“修改游戏日期”。
4. 点击“备份并保存修改”。
5. 启动游戏，确认数值和事件正常后再继续游玩。

每次实际写入均会在原存档旁创建带时间戳的 `.bak` 备份。请保留它，直到确认游戏内读取正常。

## 日期跳转

日期修改默认关闭。启用后会同步修改游戏日期与“前一天”字段，允许范围是 `2023-05-06` 至 `2023-09-24`，即酒吧可正常营业的主线日历。

跳日期不会补发已经错过的邀请、重置事件标志或改变路线进度。全成就应在每个关键邀请日前留存档差分；详细路线见 [ACHIEVEMENTS.md](ACHIEVEMENTS.md)。

## 构建与公开检查

在 Windows 上运行 `build.cmd` 可从 `BunnyGardenSaveEditor.cs` 编译 EXE。运行 `package.cmd` 可生成发布 ZIP；运行 `test_public.cmd` 会编译源码并扫描常见个人路径和令牌模式。两项检查均不需要已安装游戏或个人存档。

## 兼容性与边界

当前 Steam/Windows 存档格式为 `Deflate(BinaryFormatter(GB.Save.SaveData))`。工具调用游戏目录内已有的程序集读取该格式，因此需要本机安装游戏。游戏更新、Steam 云冲突和游戏内事件条件仍应由玩家在游戏内确认。
