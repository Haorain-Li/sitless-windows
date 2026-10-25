# 起身 · Sit Less

轻量的 Windows 久坐提醒工具，以可拖动的置顶浮窗显示倒计时。使用 Windows 原生窗体，无需安装、登录或联网，无第三方依赖。

![正常浮窗](previews/floating.png)

## 直接使用

下载 [Windows 便携版压缩包](portable/SitLess-Windows-v1.0.0.zip?raw=true)，解压到可写文件夹，双击 **SitLess.exe**。

也可直接下载 [SitLess.exe](portable/SitLess.exe?raw=true)。适用于 Windows 10/11，使用系统自带 .NET Framework 4.x，建议 4.8。

## 功能

- 默认每 **30 分钟提醒**，活动计时 **3 分钟**；提供 30/3、50/5 两种预设及自定义时长。
- 可拖动、置顶、暂停、切换精简模式，或收起到系统托盘。
- 到时保持提醒，手动开始活动；可延后 5 分钟，保留已累计的桌前计时。
- 中断活动不计完成次数；活动计时完成后等待你手动继续下一轮。
- 锁屏、休眠暂停计时，不根据键鼠空闲推断起身。
- 设置与当天统计仅保存到程序旁的 `sitless.settings.xml`，不上传数据。

![精简浮窗](previews/compact.png)

![到时提醒](previews/reminder.png)

## 规则依据

WHO 将久坐行为定义为清醒时坐着、斜倚或躺卧，能量消耗不超过 1.5 METs 的行为，并未给出适用于所有成年人的统一连续久坐时限。

- **30 分钟提醒**参考 Mayo Clinic 定期短暂中断坐姿的建议。
- **3 分钟活动**是便于执行的可调产品预设，不是医学界定。
- **50/5 办公预设**参考英国 HSE 对屏幕工作的建议：连续工作约 50–60 分钟后，安排 5–10 分钟短休息。

工具只记录计时，不识别身体姿态；完成活动计时不代表检测到实际站立或运动。请按自身能力走动、舒展或改变姿势。

来源：[WHO 建议](https://www.ncbi.nlm.nih.gov/books/NBK566046/) · [WHO 术语](https://www.ncbi.nlm.nih.gov/sites/books/NBK566047/) · [Mayo Clinic](https://www.mayoclinic.org/healthy-lifestyle/adult-health/expert-answers/sitting/faq-20058005) · [HSE](https://www.hse.gov.uk/contact/faqs/vdubreaks.htm)

## 占用与验证

可执行文件约 **36 KB**。本机启动稳定后约 10 秒的独立运行采样：内存工作集约 **36 MB**、私有内存约 **16 MB**，该采样窗口内 CPU 时间为 0.00 秒。读数会随系统、缩放和交互变化；共享的 .NET 运行库不包含在可执行文件体积内。

计时引擎的 **65 项断言通过**，涵盖到时提醒、延后、暂停、完整与中断活动、修改时长、跨天、睡眠间隔及统计恢复。详细说明见 [验证说明](验证说明.md)。

## 从源码构建

在 Windows PowerShell 中运行：

```powershell
.\build.ps1
```

构建脚本调用 Windows 自带的 .NET Framework C# 编译器，生成 `dist/SitLess.exe` 并运行计时测试，不需要下载依赖。

```powershell
# 快速体验：10 秒提醒、5 秒活动，不保存设置
.\dist\SitLess.exe --demo

# 生成界面预览
.\dist\SitLess.exe --render
```

源码：[App.cs](App.cs)（浮窗、设置与本机存储）、[TimerEngine.cs](TimerEngine.cs)（计时逻辑）、[TimerEngineTests.cs](TimerEngineTests.cs)（计时验证）。

![设置与规则](previews/settings.png)

完整操作说明见 [使用说明](使用说明.md)。
