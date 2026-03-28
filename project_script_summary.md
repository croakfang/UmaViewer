# 项目脚本文件用途总结

## 模块分析

### 核心模块 (Root)
`Assets/Scripts` 根目录下的脚本构成了应用程序的入口和基础架构：
- **UmaViewerMain**: 程序的总入口，负责初始化、配置加载和资源下载流程。
- **UmaViewerUI**: 主界面管理器，处理所有主要的 UI 交互和面板切换。
- **UmaViewerBuilder**: 负责角色模型的构建、换装和场景搭建。
- **UmaContainer**: 角色和道具的容器基类，管理加载后的对象状态。

### 核心游戏逻辑 (Gallop)
`Assets/Scripts/umamusume/Gallop` 目录包含从游戏本体移植或重构的核心逻辑：
- **Live 演出系统**: `Director` (总导演), `StageController` (舞台控制), `LiveTimelineControl` (时间轴驱动) 共同实现了复杂的 Live 演出效果。
- **Cyalume**: 荧光棒控制系统。
- **DereScript**: 包含物理弹簧 (`CySpring`) 等底层视觉效果支持。

### 音频系统 (Audio)
`Assets/Scripts/Audio` 负责音频处理：
- **ClHcaSharp**: C# 实现的 HCA 音频解码库。
- **CriWareFormats**: 处理 CRIWare 中间件的音频格式 (ACB/AWB)。
- **UmaViewerAudio**: 负责管理游戏内的语音和背景音乐播放。

### 资源与数据库 (Manifest)
`Assets/Scripts/Manifest` 处理资源管理：
- **ManifestDB**: 使用 SQLite 管理资源元数据，支持资源版本检查和更新。
- **BSVReader**: 解析二进制 Manifest 文件。

### 导出工具 (Exporters)
`Assets/Scripts/Exporters` 提供了将游戏资产导出的功能：
- **ModelExporter**: 导出角色模型为 PMX (MMD) 格式。
- **AudioExporter**: 导出音频为 MP3 格式。
- **VMDRecorder**: 将 Live 动作录制为 VMD 动作文件。

### 辅助工具
- **LiveToolBox**: 自由摄像机、UI 隐藏等 Live 模式辅助工具。
- **Settings**: 画质、音量等系统设置的实现。
- **uGIF**: GIF 录制功能。

---

## 详细文件列表

## Assets/Scripts/AssetBundleDecryptor.cs
提供 `DecryptFileToBytes` 方法，根据密钥对文件进行异或解密。

## Assets/Scripts/Audio/ClHcaSharp/Ath.cs
HCA 音频解码中初始化听觉阈值（ATH）曲线的数据。

## Assets/Scripts/Audio/ClHcaSharp/BitReader.cs
提供按位读取二进制数据的 `BitReader` 类，用于解析 HCA 帧。

## Assets/Scripts/Audio/ClHcaSharp/Channel.cs
定义 HCA 解码的声道数据结构，包含频谱、缩放因子等信息。

## Assets/Scripts/Audio/ClHcaSharp/Cipher.cs
实现 HCA 文件的加密和解密算法（支持 Type 0, 1, 56）。

## Assets/Scripts/Audio/ClHcaSharp/Crc.cs
实现 CRC16 校验算法，用于验证 HCA 数据帧的完整性。

## Assets/Scripts/Audio/ClHcaSharp/Definitions.cs
定义 HCA 格式相关的常量（如帧大小、版本号）和枚举。

## Assets/Scripts/Audio/ClHcaSharp/HcaContext.cs
存储 HCA 文件的头部信息和解码状态的上下文类。

## Assets/Scripts/Audio/ClHcaSharp/HcaDecoder.cs
HCA 解码器的核心实现，负责将 HCA 数据流解码为 PCM 样本。


## Assets/Scripts/CameraOrbit.cs
实现摄像机轨道控制（旋转、缩放）和自由移动模式，支持 UI 配置同步。

## Assets/Scripts/Config.cs
管理应用程序配置（如路径、密钥、语言、画质设置），支持读写 JSON 配置文件。

## Assets/Scripts/Easing.cs
提供常用的缓动函数（EaseIn, EaseOut, EaseInOut），用于动画计算。

## Assets/Scripts/LaserSystemKeyProbe.cs
调试工具，用于在 asset bundle 加载时检测并记录包含特定关键词（laser/driver 等）的资源 Key。

## Assets/Scripts/RotationConvert.cs
提供将 Maya 欧拉角转换为 Unity 四元数的工具方法。

## Assets/Scripts/SceneLaserScan.cs
场景扫描工具，查找并记录场景中名称包含 laser/beam/ray 的 GameObject、Mesh 或材质。

## Assets/Scripts/Screenshot.cs
实现截图和 GIF/序列帧录制功能，支持自定义分辨率、透明背景和抗锯齿。

## Assets/Scripts/SliderDisplay.cs
简单的 UI 组件，用于将 Slider 的数值实时显示在 Text 组件上。

## Assets/Scripts/UmaAssetBundleStream.cs
自定义文件流，用于读取并解密经过异或加密的 AssetBundle 文件。

## Assets/Scripts/UmaAssetManager.cs
核心资源管理类，负责 AssetBundle 的加载、依赖解析、缓存管理及卸载。


## Assets/Scripts/UmaContainer.cs
角色与道具容器的基类，管理 Live 模式状态、Shader 效果数据及材质渲染器的切换逻辑。

角色容器类，继承自 `UmaContainer`，负责角色的加载（身体、头部、头发、服装）、骨骼合并、IK 设置、物理模拟（DynamicBone）、表情驱动及纹理管理。支持普通角色、Mob 角色和 Mini 角色的加载逻辑。

## Assets/Scripts/UmaContainerProp.cs
道具容器类，负责加载和实例化道具对象。

## Assets/Scripts/UmaSceneController.cs
场景控制器，负责场景的异步加载和卸载，以及加载进度的 UI 显示。

## Assets/Scripts/UmaViewerAudio.cs
音频管理类，负责加载和播放角色的语音、音效，支持 Live 模式下的多声部控制和音量/声像调节。

## Assets/Scripts/UmaViewerBuilder.cs
核心构建器，负责协调角色的加载流程，包括查找资源、实例化容器、应用服装和配件、处理 Live 角色布局等。


## Assets/Scripts/UmaViewerMain.cs
应用程序主入口，负责初始化系统、加载配置、下载/更新资源（Master, Mob, Costume, Live 等）、管理全局资源列表和场景渲染管线。

## Assets/Scripts/UmaViewerDownload.cs
资源下载管理器，支持并发下载游戏资源（Manifest, Generic, AssetBundle），并将其缓存到本地。

主 UI 控制器，管理各种列表（角色、服装、动作、Live）的显示与交互，处理表情/动作面板的生成和事件绑定。

## Assets/Scripts/UmaViewerGlobalShader.cs
管理全局 Shader 参数（如光照方向、环境光、阴影设置等），确保所有材质在 Live 模式下渲染一致。

## Assets/Scripts/shader export.cs
Shader导出工具，该脚本可能用于将 Shader 的属性或关键字导出为 JSON 格式以便分析或外部使用。

## Assets/Scripts/shader test.cs
Shader测试脚本，用于在运行时查找特定 Shader 的材质并输出调试信息，验证 Shader 是否正确加载和应用。

## Assets/Scripts/Audio/
包含 `ClHcaSharp` (HCA 解码库) 和 `CriWareFormats` (CRIWare 格式解析库) 以及 `UmaWaveStream` 等音频流处理脚本，用于处理游戏音频的解密、解码和播放。

## Assets/Scripts/BaseNcoding/
BaseN 编码库，提供 Base64, Base32 等多种编码格式的转换工具。

## Assets/Scripts/DynamicBone/
Dynamic Bone 物理模拟库，用于实现角色头发、衣服等部位的动态物理效果。

## Assets/Scripts/Exporters/AudioExporter.cs
音频导出工具，使用 NAudio.Lame 库将 `UmaWaveStream` 转换为 MP3 格式并保存到本地。

## Assets/Scripts/Exporters/ModelExporter.cs
模型导出工具，将 `UmaContainerCharacter` 转换为 PMX (MMD) 格式。处理网格、材质、骨骼、BlendShape (表情/口型) 的转换和写入。

## Assets/Scripts/Exporters/UnityPMXRuntimeLoader/
PMX/MMD 模型加载库，用于在 Unity 中运行时加载和显示 MMD 模型文件。

## Assets/Scripts/Exporters/VMDRecorder/
VMD 动作录制工具，用于将 Unity 中的角色动画录制为 MMD 的 VMD 动作文件。

## Assets/Scripts/LiveToolBox/
包含 Live 模式下的辅助工具，如自由摄像机 (`FreeCam.cs`)、Live UI 控制 (`LiveViewerUI.cs`) 和滑块控制 (`SliderControl.cs`)。

## Assets/Scripts/Manifest/ManifestDB.cs
Manifest 数据库管理类，使用 SQLite 存储和查询资源元数据 (MetaDB)。负责 Manifest 文件的下载、解析、更新以及 Master 数据库的处理。

## Assets/Scripts/Manifest/BSVReader.cs
BSV (Binary Separated Values) 格式读取器，用于解析 Manifest 文件中的二进制数据结构。

## Assets/Scripts/Pose/
姿势编辑与管理模块，包含 `PoseManager` (姿势管理)、`RuntimeGizmo` (运行时 Gizmo 库) 以及骨骼/Transform 的序列化类 (`SerializableBone` 等)。

## Assets/Scripts/Settings/
包含一系列 `UISettings*.cs` 脚本，负责将 UI 配置选项（如画质、音频、摄像机、环境等）与系统设置进行绑定和应用。

## Assets/Scripts/UI/
UI 逻辑模块，包含页面管理 (`PageManager.cs`)、角色选择 (`LiveCharacterSelect.cs`)、弹窗 (`Popup.cs`) 以及 UI 元素拖拽等功能。

## Assets/Scripts/utility/
包含 `UmaUtility.cs` 等通用工具类。

## Assets/Scripts/uGIF/
GIF 编码库，用于将屏幕截图序列编码为 GIF 动图。

## Assets/Scripts/umamusume/Gallop/
核心游戏逻辑模块，包含 Live 演出系统的核心实现。

## Assets/Scripts/umamusume/Gallop/Live/Director.cs
Live 演出的总导演类，负责协调音乐播放、时间轴同步、角色/舞台加载、摄像机管理以及 VMD 录制流程。

## Assets/Scripts/umamusume/Gallop/Live/StageController.cs
舞台控制器，管理舞台上的各种对象（灯光、激光、屏幕等），根据时间轴数据更新它们的状态（位置、旋转、显隐）。

## Assets/Scripts/umamusume/Gallop/Live/Cutt/LiveTimelineControl.cs
Live 时间轴驱动器，负责解析和播放 Live 演示数据（Timeline），计算这一帧的摄像机位置、角色表情、口型、队形偏移等，并分发事件给各个子系统。

## Assets/Scripts/umamusume/Gallop/AssetHolder.cs
简单的资源持有者组件，用于在 Inspector 中绑定和引用 AssetTable 数据。

## Assets/Scripts/umamusume/Gallop/Cyalume/
包含荧光棒 (Cyalume) 的控制逻辑，处理荧光棒的点亮、颜色切换和动作绑定。

## Assets/Scripts/umamusume/Gallop/DereScript/
包含部分从 Deresute (偶像大师灰姑娘女孩) 移植或共用的脚本，主要涉及 `CySpring` (物理弹簧骨骼) 和舞台辅助逻辑。





