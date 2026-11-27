# ACC Telemetry Dashboard

<p align="right"><b>中文</b> | <a href="README_EN.md">English</a></p>

> Assetto Corsa Competizione 遥测监控面板

> 通过读取 ACC 共享内存，能够显示数据、录圈、看回放、学赛道图等

---

<p align="center"><img src="img/p1.png" width="880"></p>

## 快速开始

1. 双击 `AccDash.exe`
2. 启动 ACC 并进入赛道，dashboard会自动连接，等待顶部状态灯变绿

---

## 目录结构

```
acc-dash\
  AccDash.exe          主程序
  AccDash.ini          窗口设置
  build.cmd            编译脚本
  tracks\              赛道轮廓
  assets\
    app.ico              程序图标
    icon-256.png         图标原图
  src\         
    Program.cs           入口
    Interop.cs           Win32 P/Invoke、配色表 Pal
    Model.cs             Fmt 格式化、Ring 环形缓冲、Snap 数据快照、Clock
    Shm.cs               ACC 共享内存结构体与读取器 AccShm
    Config.cs            AccDash.ini 读写
    Dash.cs              主窗口：字段、构造函数、三列控件创建
    Dash.Layout.cs       三列布局与窗口尺寸自适应
    Dash.Runtime.cs      定时器、Tick 主循环、共享内存开关
    Dash.Render.cs       快照渲染
    Dash.Laps.cs         圈速录制、列表、回放进入/退出
    Dash.Map.cs          赛道地图自学习、闭合、落盘、续画
    Ui\Drawing.cs        字体、绘图助手 G、控件基类 Pane
    Ui\Widgets.cs        数据卡 Tile、状态灯 StatusLamp、扁平按钮 FlatBtn
    Ui\Charts.cs         曲线 Trace、方向盘表盘、踏板柱、G-G 盘、大数字、SESSION 面板
    Ui\Slider.cs         TIME 滑杆
    Laps\LapData.cs      圈速样本、LapData、.accdash 文件格式
    Laps\LapBrowser.cs   圈速列表控件与 LAPS 对话框
    Track\TrackOutline.cs 赛道轮廓折线与 .map 文件格式
    Track\TrackMap.cs    左列赛道地图控件
    Ui\VideoPane.cs      视频卡片
    Video\Ff.cs          ffmpeg 定位、抓屏参数、游戏窗口矩形
    Video\LiveCapture.cs 实时预览解码到 320x180
    Video\Recorder.cs    1 秒分片录制与单圈视频裁切
    Dash.Video.cs        视频卡片接线：预览 / 录制 / 内嵌回放
```

---

## 快捷键


| 键      | 作用                      |
| --------- | --------------------------- |
| `F2`    | 冻结 / 恢复数据（FREEZE） |
| `F3`    | 清空曲线                  |
| `F4`    | 保存 AccDash.ini          |
| `F5`    | 打开圈速列表              |
| `F6`    | 退出回放，回到实时        |
| `Space` | 回放播放 / 暂停           |
| `F11`   | 全屏切换                  |

---

## 圈速：录制、导出、导入、回放

<p align="center"><img src="img/p2.png" width="380"></p>

<p align="center"><img src="img/p3.png" width="880"></p>

- 每次过起点线自动开始录一圈，过线结束时自动存进内存列表
- `LAPS` 或 `F5` 打开列表：`VIEW` 回放、`EXPORT...` 存成 `.accdash`、`IMPORT...` 读入文件、`DELETE` 删除
- 回放时：两张曲线显示整圈数据，底部进度条拖动查看；`PLAY` 播放 / 暂停，长按 `PLAY` 8 倍速快进；`TIME` 滑杆决定曲线显示多宽的时间窗
- `LIVE` 回到实时

---

## 赛道地图

### 绘制

1. 对于tracks中不存在的赛道，出站圈结束后过线开始记录，第二圈过线时闭合、平滑，存成 `tracks\<赛道>.map`，因此在hotlap中至少要跑两圈
2. 之后跑同样赛道直接读取已有的赛道文件，不再需要重绘
3. 未闭合的轮廓每 15 秒存 `tracks\<赛道>.partial.map`，下次启动自动续画
4. 重绘赛道：退出面板，删掉对应的 `.map`

**目前已有部分赛道：BRANDS HATCH、PAUL RICARD、ZANDVOORT、MISANO、SPA、BARCELONA、24H NURB、HUNGARORING、MONZA、NURBURGRING、SILVERSTONE、ZOLDER**

---

## 配置文件 AccDash.ini

```ini
[display]
TraceSeconds=10      ; 曲线时间窗 3..60 秒
SpeedUnit=kmh        ; kmh | mph
TempUnit=c           ; c | f
GScale=2.0           ; G-G 圆盘满量程 0.5..4.0 g
LongGSign=-1         ; -1 = 制动为正（默认），+1 = 制动为负

[video]
Video=1              ; 1 = 视频功能开（实时预览 + 单圈录像）
VideoPreview=1       ; 0 = 只录像，不显示实时预览
VideoCrf=26          ; 画质，越小越清楚、文件越大
FfmpegPath=          ; 留空则按 ..\tools\ffmpeg\bin\ffmpeg.exe 找
```

改完重启生效

---

## 数据来源

数据来自 ACC 的三块命名共享内存，只读映射、约 30 Hz 刷新：

- `Local\acpmf_physics` —— 速度、转速、档位、踏板、G 值、胎温、油量、FFB 输出
- `Local\acpmf_graphics` —— 圈数、名次、圈速、DELTA、赛段、抓地力、天气
- `Local\acpmf_static` —— 赛道名、车型、最大转速

---

## 编译

需要 Windows 自带的 .NET Framework 4.x `csc.exe`。双击 `build.cmd` 即可。

手工编译等价于：

```bat
for /r src %f in (*.cs) do @echo "%f">>sources.rsp
%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:winexe /out:AccDash.exe /optimize+ ^
  /win32icon:assets\app.ico ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll @sources.rsp
```

---
