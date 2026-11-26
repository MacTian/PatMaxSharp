# Cognex.VisionPro.PMAlign.dll 元数据导出（决策相关子集）

证据类型：**实测**。全部内容通过 `System.Reflection.Metadata.MetadataReader` 只读元数据表得到，
不加载程序集、不反编译第三方源码。工具与复现命令见 `tools/cognex-meta/README.md`。

本文件只保留对实现有决策价值的类型；全量 443 个类型的原始数据在 `raw/pmalign-meta.json`。
本文件中的中文说明为本次分析所写，类型名、成员名、签名、默认值均为二进制原样。

## 0. 文件事实

| 项 | 值 |
|---|---|
| 路径 | `C:\Program Files\Cognex\VisionPro\ReferencedAssemblies\Cognex.VisionPro.PMAlign.dll` |
| 大小 | 22,809,032 bytes |
| SHA-256 | `14c6c3a8e1392a5fb0e0fdf7174b035402ba0d8fe7ba8ebca28347ba26fb1070` |
| Assembly | `Cognex.VisionPro.PMAlign`，AssemblyVersion `96.0.0.0` |
| Module MVID | `fe6b2dd5-8faf-40e0-a7f9-86316e87cf09` |
| TargetFramework | `".NETFramework,Version=v4.8", FrameworkDisplayName=".NET Framework 4.8"` |
| 类型总数 | 443（public 90；编译器生成 96 个已剔除） |
| 成员总数 | 1,825（字段+属性+方法+事件） |
| XML 文档 | `C:\Program Files\Cognex\VisionPro\ReferencedAssemblies\Cognex.VisionPro.PMAlign.xml` |
| 保留类型 | 347 |

## 1. 程序集级属性

| 属性 | 值 |
|---|---|
| `AssemblyCompany` | "Cognex" |
| `AssemblyConfiguration` | "" |
| `AssemblyCopyright` | "Copyright Â© Cognex Corporation 2026" |
| `AssemblyDescription` | "Cognex VisionPro PMAlign" |
| `AssemblyFileVersion` | "96.0.0.0" |
| `AssemblyInformationalVersion` | "96.0.0.0" |
| `AssemblyProduct` | "VisionPro" |
| `AssemblyTitle` | "Cognex.VisionPro.PMAlign" |
| `AssemblyTrademark` | "VISIONPROÂ®" |
| `CLSCompliant` | true |
| `ComVisible` | false |
| `SecurityRules` | blob:0100010000 |
| `TargetFramework` | ".NETFramework,Version=v4.8", FrameworkDisplayName=".NET Framework 4.8" |

## 2. 核心类型 public 成员

列说明：`Token` 是元数据 token（可据此在 `raw/pmalign-meta.json` 里定位）；
`[DefaultValue]` 是属性上的 `[DefaultValue(x)]` 实参（VisionPro 属性网格/序列化实际使用值）；
`IL 默认` 是方法体内 `ldc.*` → `call set_X` / `stfld` 观察到的写入（括号内为方法名）；
`原生接口` 是该属性的厂商文档属性里 `<include path='//interface[@name=...]/function[@vbname=...]'>` 指定的 C++ COM 成员名。

### `Cognex.VisionPro.PMAlign.CogPMAlignTool`

- 种类 `class`，可见性 `public`，Token `0200011D`，标志 `serializable`
- 基类 `Cognex.VisionPro.Implementation.CogToolBase`，接口 `Cognex.VisionPro.Implementation.Internal.ICogNetInternal`, `System.IDisposable`
- 官方说明：Tool which locates features using a PMAlign pattern. This tool encompasses all of the classes required to run a PMAlign inspection.

| 属性 | 类型 | Token | [DefaultValue] | IL 默认 | 原生接口 | 官方语义 |
|---|---|---|---|---|---|---|
| `SearchRegion` | `Cognex.VisionPro.ICogRegion` | `17000072` |  |  | `ICogPMAlignTool::SearchRegion` | Region of interest in the InputImage that is used to locate PMAlign pattern(s).  NULL means use entire InputImage.  The ICogPMAlignRunParams::SearchRegionMode property specifies exactly how the region will be applied to this image. |
| `Results` | `Cognex.VisionPro.PMAlign.CogPMAlignResults` | `17000073` |  |  | `ICogPMAlignTool::Results` | Results from the last invocation of the Run method.  A gcnew CogPMAlignResults is factoried for each inspection.  Previous results will be released prior to the new inspection. |
| `RunParams` | `Cognex.VisionPro.PMAlign.CogPMAlignRunParams` | `17000074` |  |  | `ICogPMAlignTool::RunParams` | The PMAlign run parameters. |
| `Pattern` | `Cognex.VisionPro.PMAlign.CogPMAlignPattern` | `17000075` |  |  | `ICogPMAlignTool::Pattern` | The PMAlign pattern. |
| `InputImage` | `Cognex.VisionPro.ICogImage` | `17000076` | `"System.Object, mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089"` |  | `ICogPMAlignTool::InputImage` | Search image used to run a PMAlign inspection.  The SearchRegion is applied to this InputImage in order to define the region that will be used locate PMAlign pattern(s). |
| `CurrentRecordEnable` | `Cognex.VisionPro.PMAlign.CogPMAlignCurrentRecordConstants` | `17000077` | `458831` |  | `ICogPMAlignTool::CurrentRecordEnable` | Enable creation of records by bitwise ORing the appropriate CurrentRecordConstants. |
| `LastRunRecordDiagEnable` | `Cognex.VisionPro.PMAlign.CogPMAlignLastRunRecordDiagConstants` | `17000078` | `1` |  | `ICogPMAlignTool::LastRunRecordDiagEnable` | Enable creation of records by bitwise ORing the appropriate LastRunRecordDiagConstants.  These are diagnostic records that may cause the Tool to have a longer execution time. |
| `LastRunRecordEnable` | `Cognex.VisionPro.PMAlign.CogPMAlignLastRunRecordConstants` | `17000079` | `66` |  | `ICogPMAlignTool::LastRunRecordEnable` | Enable creation of records by bitwise ORing the appropriate LastRunRecordConstants. |
| `RunStatus` | `Cognex.VisionPro.ICogRunStatus` | `1700007A` |  |  |  | Gets general information about the last call to Run. |
| `Name` | `string` | `1700007B` |  |  |  | Gets or sets the name of the tool. |

| 方法签名 | 官方语义 |
|---|---|
| `void .ctor(Cognex.VisionPro.PMAlign.CogPMAlignTool)` |  |
| `void .ctor()` |  |
| `void .ctor(nint)` |  |
| `virtual void Run()` | Runs the tool using the current parameter settings. |
| `virtual void OnDeserialization(object)` |  |

### `Cognex.VisionPro.PMAlign.CogPMAlignPattern`

- 种类 `class`，可见性 `public`，Token `02000115`，标志 `serializable`
- 基类 `Cognex.VisionPro.Implementation.CogObjectBase`，接口 `Cognex.VisionPro.Implementation.Internal.ICogNetInternal`, `System.IDisposable`, `System.ICloneable`, `System.Runtime.Serialization.ISerializable`, `Cognex.VisionPro.Implementation.ICogHasChanged`, `System.Runtime.Serialization.IDeserializationCallback`, `Cognex.VisionPro.ICogChangedEvent`
- 官方说明：Class which holds a single PMAlign pattern used for locating features within an image. Primary interface provides access to the training parameters, a method to train the pattern, results of training the pattern, and a method to execute an inspection.

| 属性 | 类型 | Token | [DefaultValue] | IL 默认 | 原生接口 | 官方语义 |
|---|---|---|---|---|---|---|
| `Composite` | `Cognex.VisionPro.PMAlign.CogPMAlignComposite` | `17000043` |  |  |  | Gets or sets the CogPMAlignComposite object. |
| `EdgeThreshold` | `double` | `17000044` | `10` |  | `ICogPMAlignPattern::EdgeThreshold` | Train-time edge threshold, under which, the edges will be ignored. Valid value is from 0.0 to 65535.0. Default value is 10.0. |
| `AutoEdgeThresholdEnabled` | `bool` | `17000045` | `true` |  | `ICogPMAlignPattern::AutoEdgeThresholdEnabled` | If true, an automatically computed edge threshold will be used for training. If false, the user provided edge threshold will be used. |
| `RepeatingPatterns` | `bool` | `17000046` | `true` |  | `ICogPMAlignPattern::RepeatingPatterns` | If true and training algorithm is Patmax, the pattern will be trained specially for repeating patterns. Otherwise, the pattern will be trained as before. |
| `Trained` | `bool` | `17000047` |  |  | `ICogPMAlignPattern::Trained` | True if the pattern is trained. |
| `TrainImageMaskOffsetY` | `int` | `17000048` | `0` |  | `ICogPMAlignPattern::TrainImageMaskOffsetY` | Y offset value in pixels.  The X & Y offset values measure the offset from the upper-left corner of the TrainImage to the upper-left corner of the TrainImageMask in image coordinates of the TrainImage. |
| `TrainImageMaskOffsetX` | `int` | `17000049` | `0` |  | `ICogPMAlignPattern::TrainImageMaskOffsetX` | X offset value in pixels.  The X & Y offset values measure the offset from the upper-left corner of the TrainImage to the upper-left corner of the TrainImageMask in image coordinates of the TrainImage. |
| `TrainImageMask` | `Cognex.VisionPro.CogImage8Grey` | `1700004A` | `"System.Object, mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089"` |  | `ICogPMAlignPattern::TrainImageMask` | Mask used when training the PMAlign pattern.  The mask allows the user to identify areas of the pattern as "Care" or "Don't Care".  The "Don't Care" areas can also be subclassified into "Score" and "Don't Score" with regards to clutter. |
| `Origin` | `Cognex.VisionPro.CogTransform2DLinear` | `1700004B` |  |  | `ICogPMAlignPattern::Origin` | A 6 DOF origin for the pattern.  This transform maps from "Pattern Space" to the selected space of TrainImage. |
| `TrainRegionMode` | `Cognex.VisionPro.CogRegionModeConstants` | `1700004C` | `0` |  | `ICogPMAlignPattern::TrainRegionMode` | Specifies how the TrainRegion is applied to the TrainImage. |
| `TrainRegion` | `Cognex.VisionPro.ICogRegion` | `1700004D` |  |  | `ICogPMAlignPattern::TrainRegion` | Region of interest indicating portion of TrainImage or TrainShapeModels that will be used to train the pattern.  A NULL TrainRegion indicates that the entire TrainImage or TrainShapeModels will be used to train the PMAlign pattern. |
| `TrainShapeModelsTransform` | `Cognex.VisionPro.ICogTransform2D` | `1700004E` |  |  | `ICogPMAlignPattern::TrainShapeModelsTransform` | Transform defines the space of all shape models when TrainMode is cogPMAlignTrainModeShapeModelsWithTransform, otherwise, this transform is ignored.  This is a pixel from shape model space transform. |
| `TrainShapeModels` | `Cognex.VisionPro.ICogShapeModelCollection` | `1700004F` |  |  | `ICogPMAlignPattern::TrainShapeModels` | A collection of shape models used to train a PMAlign pattern when the TrainMode property is either cogPMAlignTrainModeShapeModelsWithImage or cogPMAlignTrainModeShapeModelsWithTransform. |
| `TrainImage` | `Cognex.VisionPro.ICogImage` | `17000050` | `"System.Object, mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089"` |  | `ICogPMAlignPattern::TrainImage` | Pattern image used to train PMAlign.  The TrainRegion is applied to this TrainImage in order to define the region that will be used to train the pattern.  The TrainRegionMode property specifies how the region will be applied to this image. |
| `RuntimeScoringMethod` | `Cognex.VisionPro.PMAlign.CogPMAlignVisiblePixelMaskScoringMethodConstants` | `17000051` | `0` |  |  | Instructs the pattern how to treat non-visible pixels at run-time. |
| `TrainMode` | `Cognex.VisionPro.PMAlign.CogPMAlignTrainModeConstants` | `17000052` | `0` |  | `ICogPMAlignPattern::TrainMode` | Train mode indicates whether a PMAlign pattern should be trained based on the pixel content of the training image or trained based on a synthetic pattern described by a collection of shape models. |
| `TrainAlgorithm` | `Cognex.VisionPro.PMAlign.CogPMAlignTrainAlgorithmConstants` | `17000053` | `2` |  | `ICogPMAlignPattern::TrainAlgorithm` | Algorithm(s) used to train this PMAlign pattern.  The PMAlign pattern can only be executed using the algorithms that were enabled at train-time.  The algorithm actually used at run-time is specified in ICogPMAlignRunParams. |
| `PCPDiagMessage` | `string` | `17000054` |  |  | `ICogPMAlignPattern::PCPDiagMessage` | Diagnostic data string for use by Cognex. |
| `PCPString` | `string` | `17000055` | `""` |  | `ICogPMAlignPattern::PCPString` | Customization string (PCP) used to customize this PMAlign pattern.  The PCP string can be set by either setting the PCPString property directly or calling the PCPLoadFromFile method.  An empty string indicates that no PCP is set for this pattern. |
| `GrainLimitFine` | `double` | `17000056` | `1` |  | `ICogPMAlignPattern::GrainLimitFine` | Fine granularity limit.  User can manually set the grain limit if GrainLimitAutoSelect is false.  Valid range is 1.0 to 25.5 inclusive.  Coarse must be greater than or equal to fine.  Setting fine greater than coarse will also set coarse to the new value. |
| `GrainLimitCoarse` | `double` | `17000057` | `4` |  | `ICogPMAlignPattern::GrainLimitCoarse` | Coarse granularity limit.  User can manually set the grain limit if GrainLimitAutoSelect is false.  Valid range is 1.0 to 25.5 inclusive.  Coarse must be greater than or equal to fine.  Setting coarse less than fine will also set fine to the new value. |
| `GrainLimitAutoSelect` | `bool` | `17000058` | `true` |  | `ICogPMAlignPattern::GrainLimitAutoSelect` | If true then PMAlign will automatically generate grain limits during training.  If false then the values stored in the pattern prior to training will be used. |
| `HighSensitivityParameter` | `double` | `17000059` | `2` |  | `ICogPMAlignPattern::HighSensitivityParameter` | Control the sensitivity parameter used by PatMax when TrainAlgorithm is cogPMAlignTrainAlgorithmPatMaxHighSensitivity. |
| `ExpectedDeformationRate` | `double` | `1700005A` | `0.300000011920929` |  | `ICogPMAlignPattern::ExpectedDeformationRate` | The expected deformation rate of the PatFlex pattern.  This has no effect unless the TrainAlgorithm is PatFlex. |
| `Elasticity` | `double` | `1700005B` | `0` |  | `ICogPMAlignPattern::Elasticity` | The elasticity value is the amount of feature variance, in pixels, that PMAlign allows.  Setting a nonzero elasticity value permits PMAlign to find pattern instances with nonlinear geometric variation to be found. |
| `IgnorePolarity` | `bool` | `1700005C` | `false` |  | `ICogPMAlignPattern::IgnorePolarity` | If true, pattern polarity is ignored.  If false, only patterns with polarity matching the trained pattern will be found. |
| `ChangedEventSuspended` | `int` | `1700005E` |  |  |  | If nonzero, indicates that the raising of the Changed event has been suspended. This value is incremented when SuspendChangedEvent is called and decremented when ResumeAndRaiseChangedEvent is called. |
| `HasChanged` | `bool` | `1700005F` |  | `0 (GetObjectData)` |  | If true, the serializable state of this object has changed since the last time it was serialized. |

| 方法签名 | 官方语义 |
|---|---|
| `void .ctor(Cognex.VisionPro.PMAlign.CogPMAlignPattern)` |  |
| `void .ctor()` |  |
| `void .ctor(nint)` |  |
| `virtual void SuspendChangedEvent()` | Temporarily suspends the raising of the Changed event. May be called more than once, and a corresponding call to ResumeAndRaiseChangedEvent must be made for each call to SuspendChangedEvent. |
| `virtual void ResumeAndRaiseChangedEvent()` | Re-enables raising of the Changed event after SuspendChangedEvent has been called. Also raises the Changed event if the ChangedEventSuspended count is reduced to zero and any changes were made while events were suspended. Must be called once for each call to SuspendChangedEvent. |
| `void PCPLoadFromFile(string)` |  |
| `void ImportFromCvlArchive(string)` |  |
| `Cognex.VisionPro.CogGraphicCollection CreateGraphicsCoarse(Cognex.VisionPro.CogColorConstants)` |  |
| `Cognex.VisionPro.CogGraphicCollection CreateGraphicsFine(Cognex.VisionPro.CogColorConstants)` |  |
| `Cognex.VisionPro.CogStringCollection GetInfoStrings()` | Gets train-time diagnostic message text string(s) for this pattern. If there are no messages then the collection will be empty. The corresponding message ID(s) can be obtained from GetInfoIDs. |
| `Cognex.VisionPro.ICogImage GetTrainedPatternImage()` | Gets the image used to train the PMAlign pattern. This is the smallest possible sub-image of TrainImage that contains the TrainRegion. Actual features within the sub-image that are trained depends on the TrainRegionMode and TrainImageMask. |
| `Cognex.VisionPro.CogImage8Grey GetTrainedPatternImageMask()` | Gets the actual mask image used to train the current pattern. This mask is the combination of the TrainImageMask and any mask that may be required by TrainRegionMode. This mask is the same size as the image returned by GetTrainedPatternImage. |
| `Cognex.VisionPro.ICogShapeModelCollection GetTrainedPatternShapeModels()` | Gets the actual shape models used to train the current pattern. The user provided shape models are clipped using the pixel aligned bounding box of TrainRegion when TrainRegion is not nothing. |
| `void Train()` | Trains this PMAlign pattern. If the pattern is already trained then it will be untrained and retrained. |
| `void Untrain()` | Untrain this pattern. |
| `Cognex.VisionPro.PMAlign.CogPMAlignResults Execute(Cognex.VisionPro.ICogImage, Cognex.VisionPro.ICogRegion, Cognex.VisionPro.PMAlign.CogPMAlignRunParams)` |  |
| `int[] GetInfoIDs()` |  |
| `Cognex.VisionPro.CogGraphicCollection CreateGraphicsCompositeFeatures(Cognex.VisionPro.CogColorConstants, bool, bool)` |  |
| `Cognex.VisionPro.CogGraphicCollection CreateGraphicsComposite(Cognex.VisionPro.CogColorConstants, bool)` |  |
| `sealed void Dispose()` |  |

事件：`Changed`

### `Cognex.VisionPro.PMAlign.CogPMAlignRunParams`

- 种类 `class`，可见性 `public`，Token `0200010F`，标志 `serializable`
- 基类 `Cognex.VisionPro.Implementation.CogObjectBase`，接口 `Cognex.VisionPro.Implementation.Internal.ICogNetInternal`, `System.IDisposable`, `System.ICloneable`, `System.Runtime.Serialization.ISerializable`, `Cognex.VisionPro.Implementation.ICogHasChanged`, `Cognex.VisionPro.ICogChangedEvent`
- 官方说明：Class which holds PMAlign run parameters. The run parameters are used to control how PMAlign locates matching patterns in the run-time image.

| 属性 | 类型 | Token | [DefaultValue] | IL 默认 | 原生接口 | 官方语义 |
|---|---|---|---|---|---|---|
| `CoarseAcceptThresholdEnabled` | `bool` | `17000014` | `false` |  | `ICogPMAlignPattern::CoarseAcceptThresholdEnabled` | Whether to use CoarseAcceptThreshold. Default is false. |
| `CoarseAcceptThreshold` | `double` | `17000015` | `0.33` |  | `ICogPMAlignRunParams::CoarseAcceptThreshold` | If CoarseAcceptThresholdEnabled is true, then results whose score is greater than or equal to CoarseAcceptThreshold will be refined and then compared against AcceptThreshold. Otherwise result scores are only compared against AcceptThreshold. The legal range for CoarseAcceptThreshold is [0.0, 1.0] and the default is 0.33. |
| `AutoEdgeThresholdEnabled` | `bool` | `17000016` | `true` |  | `ICogPMAlignPattern::AutoEdgeThresholdEnabled` | If true, user provided edge threshold will be used for training. If false, computed edge threshold will be used. Default value is false. |
| `EdgeThreshold` | `double` | `17000017` | `5` |  | `ICogPMAlignRunParams::EdgeThreshold` | Run-time edge threshold, under which, edges will be ignored. Valid value range is from 0.0 to 65535.0. Default value is 5.0. |
| `OwnedFlexParams` | `Cognex.VisionPro.PMAlign.ICogPMAlignFlexRunParams` | `17000018` |  |  | `ICogPMAlignRunParams::FlexParams` | Gets the runtime parameters specific to the PatFlex algorithm.  These parameters are owned by this object, but are conveniently grouped together inside the returned interface. |
| `StartPose` | `Cognex.VisionPro.CogTransform2DLinear` | `17000019` |  |  | `ICogPMAlignRunParams::StartPose` | Transform that defines the starting model pose within the input image. |
| `RunMode` | `Cognex.VisionPro.PMAlign.CogPMAlignRunModeConstants` | `1700001A` | `0` |  | `ICogPMAlignRunParams::RunMode` | The mode in which this PMAlign pattern will be run. |
| `OutsideRegionThreshold` | `double` | `1700001B` | `0` |  | `ICogPMAlignRunParams::OutsideRegionThreshold` | Specifies the proportion of features that can be outside the search region without affecting score. The value must be set to 0 when using PatFlex or PatMaxPerspective train algorithm. |
| `SearchImageMaskOffsetY` | `int` | `1700001C` | `0` |  | `ICogPMAlignRunParams::SearchImageMaskOffsetY` | Y offset value in pixels.  The X & Y offset values measure the offset from the upper-left corner of the run-time input image to the upper-left corner of the SearchImageMask in image coordinates of the input image. |
| `SearchImageMaskOffsetX` | `int` | `1700001D` | `0` |  | `ICogPMAlignRunParams::SearchImageMaskOffsetX` | X offset value in pixels.  The X & Y offset values measure the offset from the upper-left corner of the run-time input image to the upper-left corner of the SearchImageMask in image coordinates of the input image. |
| `SearchImageMask` | `Cognex.VisionPro.CogImage8Grey` | `1700001E` | `"System.Object, mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089"` |  | `ICogPMAlignRunParams::SearchImageMask` | Mask that is used when running the PMAlign pattern.  The mask allows the user to identify areas of the pattern as "Care" or "Don't Care".  The mask is aligned to the run-time input image using the SearchImageMaskOffsetX & Y values. |
| `SearchRegionMode` | `Cognex.VisionPro.CogRegionModeConstants` | `1700001F` | `0` |  | `ICogPMAlignRunParams::SearchRegionMode` | Mode which specifies how the search region is to be applied to the input image of a PMAlign inspection. |
| `SaveMatchInfo` | `bool` | `17000020` | `false` |  | `ICogPMAlignRunParams::SaveMatchInfo` | Flag indicating whether to save information necessary to create result match graphics.  Must be set to true if user wants to get match info graphics using the ICogPMAlignResult::CreateResultGraphics method. |
| `GrainLimitFine` | `double` | `17000021` | `1` |  | `ICogPMAlignRunParams::GrainLimitFine` | Fine grain limit that will be used at run-time if GrainLimitsUsePattern is false.  Coarse must be greater than or equal to fine.  Setting fine greater than coarse will also set coarse to the new fine value.  Valid range is from 1.0 to 25.5 inclusive. |
| `GrainLimitCoarse` | `double` | `17000022` | `4` |  | `ICogPMAlignRunParams::GrainLimitCoarse` | Coarse grain limit that will be used at run-time if GrainLimitsUsePattern is false.  Coarse must be greater than or equal to fine.  Setting coarse less than fine will also set fine to the new coarse value.  Valid range is from 1.0 to 25.5 inclusive. |
| `GrainLimitsUsePattern` | `bool` | `17000023` | `true` |  | `ICogPMAlignRunParams::GrainLimitsUsePattern` | If true then the grain limits specified in CogPMAlignPattern will be used.  If false then the grain limits specified in this CogPMAlignRunParams will be used. |
| `XYOverlap` | `double` | `17000024` | `0.8` |  | `ICogPMAlignRunParams::XYOverlap` | Result candidates overlap in area if the percentage of area overlap is greater than XYOverlap.  PMAlign discards the weaker pattern instance when two pattern instances overlap for all DOFs as well as area. |
| `ZoneScaleY` | `Cognex.VisionPro.PMAlign.CogPMAlignZoneScale` | `17000025` |  |  | `ICogPMAlignRunParams::ZoneScaleY` | Zone configuration for the Y scale degree of freedom only. |
| `ZoneScaleX` | `Cognex.VisionPro.PMAlign.CogPMAlignZoneScale` | `17000026` |  |  | `ICogPMAlignRunParams::ZoneScaleX` | Zone configuration for the X scale degree of freedom only. |
| `ZoneScale` | `Cognex.VisionPro.PMAlign.CogPMAlignZoneScale` | `17000027` |  |  | `ICogPMAlignRunParams::ZoneScale` | Zone configuration for the uniform scale degree of freedom. |
| `ZoneAngle` | `Cognex.VisionPro.PMAlign.CogPMAlignZoneAngle` | `17000028` |  |  | `ICogPMAlignRunParams::ZoneAngle` | Zone configuration for the angle degree of freedom.  Units are in Radians. |
| `RunAlgorithm` | `Cognex.VisionPro.PMAlign.CogPMAlignRunAlgorithmConstants` | `17000029` | `2` |  | `ICogPMAlignRunParams::RunAlgorithm` | The algorithm used to run this PMAlign pattern.  The algorithm specified must be among those for which the PMAlign object was trained. |
| `Timeout` | `double` | `1700002A` | `5000` |  | `ICogPMAlignRunParams::Timeout` | The maximum execution time (msec) allowed to run a PMAlign inspection.  If PMAlign has not finished by the end of the timeout period, it will stop soon thereafter and return E_Cog_TimeoutExpired.  This value only applies if TimeoutEnabled is true. |
| `TimeoutEnabled` | `bool` | `1700002B` | `false` |  | `ICogPMAlignRunParams::TimeoutEnabled` | If true then the Timeout property limits the execution time of the PMAlign inspection.  If false then timeout is disabled and the Timeout property is ignored. |
| `ScoreUsingClutter` | `bool` | `1700002C` | `true` |  | `ICogPMAlignRunParams::ScoreUsingClutter` | Determines if PatMax considers extraneous features when computing the score of a pattern instance.  True, extraneous features are considered and will result in lower scores.  False, extraneous features are ignored.  Available for PatMax only. |
| `ContrastThreshold` | `double` | `1700002D` | `10` |  | `ICogPMAlignRunParams::ContrastThreshold` | Minimum acceptable contrast for a pattern instance.  Only pattern instances where the average difference in pixel values across all feature boundaries exceeds the contrast threshold are considered by PMAlign.  Threshold must be between 0 and 65535. |
| `AcceptThreshold` | `double` | `1700002E` | `0.5` |  | `ICogPMAlignRunParams::AcceptThreshold` | Acceptance threshold on the score of a result.  Only instances of the pattern that receive scores greater than or equal to this threshold are accepted.  Valid range is from 0 to 1. |
| `ApproximateNumberToFind` | `int` | `1700002F` | `1` |  | `ICogPMAlignRunParams::ApproximateNumberToFind` | Number of results to look for.  In some cases, PMAlign may return more or fewer results than requested. |
| `ChangedEventSuspended` | `int` | `17000031` |  |  |  | If nonzero, indicates that the raising of the Changed event has been suspended. This value is incremented when SuspendChangedEvent is called and decremented when ResumeAndRaiseChangedEvent is called. |
| `HasChanged` | `bool` | `17000032` |  | `0 (GetObjectData)` |  | If true, the serializable state of this object has changed since the last time it was serialized. |

| 方法签名 | 官方语义 |
|---|---|
| `void .ctor(Cognex.VisionPro.PMAlign.CogPMAlignRunParams)` |  |
| `void .ctor()` |  |
| `void .ctor(nint)` |  |
| `virtual void SuspendChangedEvent()` | Temporarily suspends the raising of the Changed event. May be called more than once, and a corresponding call to ResumeAndRaiseChangedEvent must be made for each call to SuspendChangedEvent. |
| `virtual void ResumeAndRaiseChangedEvent()` | Re-enables raising of the Changed event after SuspendChangedEvent has been called. Also raises the Changed event if the ChangedEventSuspended count is reduced to zero and any changes were made while events were suspended. Must be called once for each call to SuspendChangedEvent. |
| `sealed void Dispose()` |  |

事件：`Changed`

### `Cognex.VisionPro.PMAlign.CogPMAlignResult`

- 种类 `class`，可见性 `public`，Token `02000117`，标志 `serializable`
- 基类 `Cognex.VisionPro.Implementation.CogObjectBase`，接口 `Cognex.VisionPro.Implementation.Internal.ICogNetInternal`, `System.IDisposable`, `System.ICloneable`, `System.Runtime.Serialization.ISerializable`
- 官方说明：Class which holds a single PMAlign result. Each result corresponds to a found instance of the pattern.

| 属性 | 类型 | Token | [DefaultValue] | IL 默认 | 原生接口 | 官方语义 |
|---|---|---|---|---|---|---|
| `FineStage` | `bool` | `17000060` |  |  | `ICogPMAlignResult::FineStage` | True if this result is from the fine stage of processing, and false if this result is from the coarse stage of processing. |
| `MaxCoarseAcceptThreshold` | `double` | `17000061` |  |  | `ICogPMAlignResult::MaxCoarseAcceptThreshold` | Gets the maximum coarse accept threshold that would allow this result to be found. |
| `ModelName` | `string` | `17000062` |  |  |  | The pattern name. The pattern name is only available for multi-model. |
| `ModelID` | `int modopt(System.Runtime.CompilerServices.IsLong)` | `17000063` |  |  |  | The pattern unique ID. |
| `OutsideRegionAreaProportion` | `double` | `17000064` |  |  | `ICogPMAlignResult::OutsideRegionAreaProportion` | The proportion of pattern area that is outside the search region. |
| `OutsideRegionFeatureProportion` | `double` | `17000065` |  |  | `ICogPMAlignResult::OutsideRegionFeatureProportion` | The proportion of pattern features that is outside the search region. |
| `UnwarpedInputImage` | `Cognex.VisionPro.ICogImage` | `17000066` |  |  | `ICogPMAlignResult::UnwarpedInputImage` | Gets the run-time input image unwarped by the flex deformation found for this result. Returns Nothing (NULL) if PatFlex was not run, or if SaveDeformationInfo was not set to cogPMAlignFlexDeformationInfoTransformAndUnwarpData on the last run, or if the area of the unwarped image would be over 100 times larger than the area of the original image. |
| `Contrast` | `double` | `17000067` |  |  | `ICogPMAlignResult::Contrast` | Contrast measurement of this result which is the average of the pixel gradient measurements across all feature boundaries. |
| `Accepted` | `bool` | `17000068` |  |  | `ICogPMAlignResult::Accepted` | True if the result's Score was >= the ICogPMAlignRunParams::AcceptThreshold. |
| `Clutter` | `double` | `17000069` |  |  | `ICogPMAlignResult::Clutter` | Clutter score is the number of extraneous features present in this result divided by the number of features in the trained pattern. The value ranges from 0.0 to infinity.  Only available for PatMax, -1 is always returned for PatQuick. |
| `Coverage` | `double` | `1700006A` |  |  | `ICogPMAlignResult::Coverage` | Coverage score is the fraction (0.0 to 1.0) of features in the trained pattern that are found in this result.  Only available for PatMax, -1 is always returned for PatQuick. |
| `FitError` | `double` | `1700006B` |  |  | `ICogPMAlignResult::FitError` | Quality measure that indicates the degree to which the found pattern features conform to the trained pattern, without regard to missing features.  Zero is a perfect fit.  Infinity is a poor fit.  Only available for PatMax, -1 returned for PatQuick. |
| `Score` | `double` | `1700006C` |  |  | `ICogPMAlignResult::Score` | Score for this result.  The score is between 0.0 and 1.0 with higher values indicating a closer match between the trained pattern and the pattern in the search image. |
| `ID` | `int` | `1700006D` |  |  | `ICogPMAlignResult::ID` | A unique ID used to identify this PMAlign result.  This ID also corresponds to the index of this result in the CogPMAlignResults to which this result belongs. |

| 方法签名 | 官方语义 |
|---|---|
| `void .ctor(Cognex.VisionPro.PMAlign.CogPMAlignResult)` |  |
| `void .ctor()` |  |
| `void .ctor(nint)` |  |
| `Cognex.VisionPro.CogTransform2DLinear GetPose()` | Transform from "Pattern Space" to the selected space of the run-time input image. This transform can be used to find any point relative to the pattern in the selected space of the run-time input image. |
| `double[] GetRunImageFromTrainImagePerspectiveTransformElements()` | Cognex Internal Use Only. |
| `Cognex.VisionPro.CogStringCollection GetInfoStrings()` | Gets run-time diagnostic message text string(s) for this individual PMAlign result. If there are no messages then the collection will be empty. The corresponding message ID(s) can be obtained from the GetInfoIDs method. |
| `Cognex.VisionPro.CogCompositeShape CreateResultGraphics(Cognex.VisionPro.PMAlign.CogPMAlignResultGraphicConstants)` |  |
| `void MapPointRunTimeImageFromTrainTimeImage(double, double, ref double, ref double)` |  |
| `void MapPointTrainTimeImageFromRunTimeImage(double, double, ref double, ref double)` |  |
| `int[] GetInfoIDs()` |  |
| `sealed void Dispose()` |  |

### `Cognex.VisionPro.PMAlign.CogPMAlignResults`

- 种类 `class`，可见性 `public`，Token `02000119`，标志 `serializable`
- 基类 `Cognex.VisionPro.Implementation.CogObjectBase`，接口 `Cognex.VisionPro.Implementation.Internal.ICogNetInternal`, `System.IDisposable`, `System.ICloneable`, `System.Runtime.Serialization.ISerializable`, `System.Collections.ICollection`
- 官方说明：Class which holds a collection of PMAlign results. An instance of this class will be created and filled with individual results in order of decreasing score.

| 属性 | 类型 | Token | [DefaultValue] | IL 默认 | 原生接口 | 官方语义 |
|---|---|---|---|---|---|---|
| `Item` | `Cognex.VisionPro.PMAlign.CogPMAlignResult` | `1700006E` |  |  |  | Gets the item at the specified index.  In C#, this property is the indexer for the collection. |
| `SyncRoot` | `object` | `1700006F` |  |  |  | Gets an object that can be used to synchronize access to this collection. |
| `IsSynchronized` | `bool` | `17000070` |  |  |  | Gets a value indicating whether access to this collection is synchronized (thread-safe). |
| `Count` | `int` | `17000071` |  |  |  | Gets the number of items contained in this collection. |

| 方法签名 | 官方语义 |
|---|---|
| `void .ctor(Cognex.VisionPro.PMAlign.CogPMAlignResults)` |  |
| `void .ctor()` |  |
| `void .ctor(nint)` |  |
| `virtual void CopyTo(System.Array, int)` |  |
| `int IndexOf(Cognex.VisionPro.PMAlign.CogPMAlignResult)` |  |
| `Cognex.VisionPro.CogStringCollection GetInfoStrings()` | Run-time diagnostic message containing information about the PMAlign inspection which produced this collection of results. If there are no messages then the collection will be empty. The corresponding message ID(s) can be obtained from GetInfoIDs. |
| `Cognex.VisionPro.CogRectangle GetTrainArea()` | Returns a copy of the train-pixel aligned rectangle enclosing the original train pattern. |
| `int[] GetInfoIDs()` |  |
| `sealed void Dispose()` |  |

### 关于不存在的类型

- `CogPMAlignTrainParams`：**本程序集中不存在**（已对 443 个类型名逐一比对，无同名或近似名）。
  训练参数实际分布在 `CogPMAlignPattern`（`TrainAlgorithm` / `TrainMode` / `EdgeThreshold` /
  `GrainLimit*` / `Elasticity` / `IgnorePolarity` / `HighSensitivityParameter` / `ExpectedDeformationRate`）
  与 `CogPMAlignComposite`（`TuningElasticity` / `ImageFraction` / `Timeout`）上，没有独立的 `*TrainParams` 类。

## 3. 支撑类型（缺了上面那些就用不起来）

### `Cognex.VisionPro.PMAlign.CogPMAlignZoneAngle`

- 种类 `class`，可见性 `public`，Token `0200011F`，标志 `serializable`
- 基类 `Cognex.VisionPro.Implementation.CogObjectBase`，接口 `Cognex.VisionPro.Implementation.Internal.ICogNetInternal`, `System.IDisposable`, `System.ICloneable`, `System.Runtime.Serialization.ISerializable`, `Cognex.VisionPro.Implementation.ICogHasChanged`, `Cognex.VisionPro.ICogChangedEvent`
- 官方说明：Class which holds angle Degree Of Freedom (DOF) information. PMAlign can locate patterns that have changed in angle from the trained pattern. This class specifies the angle(s) at which patterns may be located.

| 属性 | 类型 | Token | [DefaultValue] | IL 默认 | 原生接口 | 官方语义 |
|---|---|---|---|---|---|---|
| `Overlap` | `double` | `1700007C` | `6.28318530717959` |  | `ICogPMAlignZoneAngle::Overlap` | Result candidates overlap for an angle DOF if the abs(Angle #1 - Angle #2) <= Overlap.  PMAlign discards the weaker pattern instance when two pattern instances overlap for all degrees of freedom (DOF) as well as area.  Overlap units are in radians. |
| `High` | `double` | `1700007D` | `0.785398163397448` |  | `ICogPMAlignZoneAngle::High` | High limit (in radians) for angle DOF zone.  Only used if Configuration is cogPMAlignZoneLowHigh.  Low must be <= to high.  Setting high < low will also set low to the new value. |
| `Low` | `double` | `1700007E` | `-0.785398163397448` |  | `ICogPMAlignZoneAngle::Low` | Low limit (in radians) for angle DOF zone.  Only used if Configuration is cogPMAlignZoneLowHigh.  Low must be <= to high.  Setting low > high will also set high to the new value. |
| `Nominal` | `double` | `1700007F` | `0` |  | `ICogPMAlignZoneAngle::Nominal` | The nominal value (in radians) is only used if Configuration is cogPMAlignZoneNominal.  In this case PMAlign only finds pattern instances that are close to the Nominal value specified and the results will always be reported at exactly the found value. |
| `Configuration` | `Cognex.VisionPro.PMAlign.CogPMAlignZoneConstants` | `17000080` | `0` |  | `ICogPMAlignZoneAngle::Configuration` | Configuration of this DOF zone which specifies whether PMAlign will use either the low & high limits for the expected range of values or just the nominal value. |
| `ChangedEventSuspended` | `int` | `17000082` |  |  |  | If nonzero, indicates that the raising of the Changed event has been suspended. This value is incremented when SuspendChangedEvent is called and decremented when ResumeAndRaiseChangedEvent is called. |
| `HasChanged` | `bool` | `17000083` |  | `0 (GetObjectData)` |  | If true, the serializable state of this object has changed since the last time it was serialized. |

| 方法签名 | 官方语义 |
|---|---|
| `void .ctor(Cognex.VisionPro.PMAlign.CogPMAlignZoneAngle)` |  |
| `void .ctor()` |  |
| `void .ctor(nint)` |  |
| `virtual void SuspendChangedEvent()` | Temporarily suspends the raising of the Changed event. May be called more than once, and a corresponding call to ResumeAndRaiseChangedEvent must be made for each call to SuspendChangedEvent. |
| `virtual void ResumeAndRaiseChangedEvent()` | Re-enables raising of the Changed event after SuspendChangedEvent has been called. Also raises the Changed event if the ChangedEventSuspended count is reduced to zero and any changes were made while events were suspended. Must be called once for each call to SuspendChangedEvent. |
| `sealed void Dispose()` |  |

事件：`Changed`

### `Cognex.VisionPro.PMAlign.CogPMAlignZoneScale`

- 种类 `class`，可见性 `public`，Token `02000121`，标志 `serializable`
- 基类 `Cognex.VisionPro.Implementation.CogObjectBase`，接口 `Cognex.VisionPro.Implementation.Internal.ICogNetInternal`, `System.IDisposable`, `System.ICloneable`, `System.Runtime.Serialization.ISerializable`, `Cognex.VisionPro.Implementation.ICogHasChanged`, `Cognex.VisionPro.ICogChangedEvent`
- 官方说明：Class which holds scale Degree Of Freedom (DOF) information. PMAlign can locate patterns that have changed in scale from the trained pattern. This class specifies the scale(s) at which patterns may be located.

| 属性 | 类型 | Token | [DefaultValue] | IL 默认 | 原生接口 | 官方语义 |
|---|---|---|---|---|---|---|
| `Overlap` | `double` | `17000084` | `1.4` |  | `ICogPMAlignZoneScale::Overlap` | Result candidates overlap for a scale DOF if the scale ratio (larger scale/smaller scale) <= Overlap.  PMAlign discards the weaker pattern instance when two pattern instances overlap for all degrees of freedom (DOF) as well as area. |
| `High` | `double` | `17000085` | `1.2` |  | `ICogPMAlignZoneScale::High` | High limit for DOF zone.  Only used if Configuration is cogPMAlignZoneLowHigh.  In this case, PMAlign will use the low & high limits for the expected range of values.  Low must be <= to high.  Setting high < low will also set high to the new value. |
| `Low` | `double` | `17000086` | `0.8` |  | `ICogPMAlignZoneScale::Low` | Low limit for DOF zone.  Only used if Configuration is cogPMAlignZoneLowHigh.  In this case, PMAlign will use the low & high limits for the expected range of values.  Low must be <= to high.  Setting low > high will also set high to the new value. |
| `Nominal` | `double` | `17000087` | `1` |  | `ICogPMAlignZoneScale::Nominal` | The nominal value is only used if Configuration is cogPMAlignZoneNominal.  In this case PMAlign only finds pattern instances that are close to the Nominal value specified and the results will always be reported at exactly the found value. |
| `Configuration` | `Cognex.VisionPro.PMAlign.CogPMAlignZoneConstants` | `17000088` | `0` |  | `ICogPMAlignZoneScale::Configuration` | Configuration of this DOF zone which specifies whether PMAlign will use either the low & high limits for the expected range of values or just the nominal value. |
| `ChangedEventSuspended` | `int` | `1700008A` |  |  |  | If nonzero, indicates that the raising of the Changed event has been suspended. This value is incremented when SuspendChangedEvent is called and decremented when ResumeAndRaiseChangedEvent is called. |
| `HasChanged` | `bool` | `1700008B` |  | `0 (GetObjectData)` |  | If true, the serializable state of this object has changed since the last time it was serialized. |

| 方法签名 | 官方语义 |
|---|---|
| `void .ctor(Cognex.VisionPro.PMAlign.CogPMAlignZoneScale)` |  |
| `void .ctor()` |  |
| `void .ctor(nint)` |  |
| `virtual void SuspendChangedEvent()` | Temporarily suspends the raising of the Changed event. May be called more than once, and a corresponding call to ResumeAndRaiseChangedEvent must be made for each call to SuspendChangedEvent. |
| `virtual void ResumeAndRaiseChangedEvent()` | Re-enables raising of the Changed event after SuspendChangedEvent has been called. Also raises the Changed event if the ChangedEventSuspended count is reduced to zero and any changes were made while events were suspended. Must be called once for each call to SuspendChangedEvent. |
| `sealed void Dispose()` |  |

事件：`Changed`

### `Cognex.VisionPro.PMAlign.CogPMAlignComposite`

- 种类 `class`，可见性 `public`，Token `02000113`，标志 `serializable, beforefieldinit`
- 基类 `Cognex.VisionPro.Implementation.CogSerializableChangedEventBase`，接口 `Cognex.VisionPro.Implementation.Internal.ICogPMAlignComposite`, `System.Runtime.Serialization.IDeserializationCallback`, `System.IDisposable`
- 官方说明：The PMAlign composite class.

| 属性 | 类型 | Token | [DefaultValue] | IL 默认 | 原生接口 | 官方语义 |
|---|---|---|---|---|---|---|
| `PercentFineFeaturesUsed` | `double` | `1700003A` |  | `-1 (TrainComposite)` |  | Gets the percentage of the fine feature that were used in the pattern from the initial train image.The value is set when the user calls train and at least one composite item is enabled. |
| `PercentCoarseFeaturesUsed` | `double` | `1700003B` |  | `-1 (TrainComposite)` |  | Gets the percentage of the coarse feature that were used in the pattern from the initial train image.The value is set when the user calls train and the item is enabled. |
| `IgnorePolarity` | `bool` | `1700003C` |  |  |  | Gets or sets whether composite training should ignore polarity or not. |
| `Timeout` | `double` | `1700003D` |  |  |  | Gets or sets the maximum amount of time in milliseconds that training can take. |
| `TimeoutEnabled` | `bool` | `1700003E` |  |  |  | Gets or sets whether composite training is allowed to time out. If true, the composite training is limited by the Timeout value. |
| `SelectedIndex` | `int` | `1700003F` |  |  |  | Gets or sets the SelectedIndex property. This property is used to create the record content graphics for the composite data item specified in the selected index. The SelectedIndex is used by the edit control to specify the item selected. |
| `TuningElasticity` | `double` | `17000040` |  |  |  | Gets or sets the TuningElasticity property. The value indicates how far the feature location can vary. The value is specified in pixels. |
| `ImageFraction` | `double` | `17000041` |  |  |  | Gets or sets the ImageFraction property. The feature must be present in at least this fraction of the images to contribute to the produced pattern. |
| `CompositeData` | `Cognex.VisionPro.PMAlign.CogPMAlignCompositeDataItemCollection` | `17000042` |  |  |  | Gets or sets the CompositeData property. |

| 方法签名 | 官方语义 |
|---|---|
| `void .ctor(Cognex.VisionPro.PMAlign.CogPMAlignComposite)` |  |
| `void .ctor()` |  |
| `virtual void TrainComposite(nint, ref int, ref nint, ref nint, ref int)` |  |
| `virtual void SetCompositeParent(nint)` |  |
| `virtual void Clone(ref nint)` |  |
| `virtual void ClearTrainData()` |  |
| `virtual void GetIgnorePolarity(ref bool)` |  |
| `virtual void GetEnabledCount(ref int)` |  |
| `virtual void GetCount(ref int)` |  |
| `virtual void GetSelectedIndex(ref int)` |  |
| `virtual void GetImage(int, ref nint)` |  |
| `virtual void GetEnabled(int, ref bool)` |  |
| `virtual void GetAlignmentFeatures(int, ref nint)` |  |
| `virtual void GetTrainGraphicsIndex(int, ref int)` |  |
| `virtual void OnDeserialization(object)` |  |
| `void ClearCollection()` | Clear the collection and leave the pattern trained state as is. In case the application memory is low, this can free up some memory. Remember to save the tool before clearing the collection. |
| `sealed void Dispose()` |  |

事件：`InvalidatePMAlignCurrentRecord`

### `Cognex.VisionPro.PMAlign.CogPMAlignMultiResults`

- 种类 `class`，可见性 `public`，Token `02000129`，标志 `serializable, beforefieldinit`
- 基类 `Cognex.VisionPro.Implementation.CogSerializableObjectBase`，接口 `System.IDisposable`
- 官方说明：The CogPMAlignMultiResults class. The class holds the results for the multi-model tool. The object is constructed at runtime.

| 属性 | 类型 | Token | [DefaultValue] | IL 默认 | 原生接口 | 官方语义 |
|---|---|---|---|---|---|---|
| `ResultItemCollection` | `Cognex.VisionPro.PMAlign.CogPMAlignMultiResultItemCollection` | `1700009D` |  |  |  | Returns the CogPMAlignMultiResultItemCollection object. This object contains the number of found pattern instances for each reported pattern. |
| `PMAlignResults` | `Cognex.VisionPro.PMAlign.CogPMAlignResults` | `1700009E` |  |  |  | Returns the CogPMAlignResults object. This object contains the scores and positions for all found patterns, sorted by score. This object is a collection of CogPMAlignResult objects. The CogPMAlignResult class grew 2 read-only properties: ModelID and ModelName. The ModelID is the internal unique id of the pattern. The ModelName is the name associated with the pattern item and allows the user to figure out which result belongs to which pattern. |

| 方法签名 | 官方语义 |
|---|---|
| `void .ctor(Cognex.VisionPro.PMAlign.CogPMAlignMultiResults)` |  |
| `void .ctor()` |  |
| `sealed void Dispose()` |  |

### `Cognex.VisionPro.PMAlign.CogPMAlignMultiResultItem`

- 种类 `class`，可见性 `public`，Token `02000127`，标志 `serializable, beforefieldinit`
- 基类 `Cognex.VisionPro.Implementation.CogSerializableChangedEventBase`，接口 `System.IDisposable`
- 官方说明：The PMAlignMultiResultItem class. This class holds the number of results found for a pattern. The execute method in CogPMAlignMulti will construct the object and will fill the content using internal setters.

| 属性 | 类型 | Token | [DefaultValue] | IL 默认 | 原生接口 | 官方语义 |
|---|---|---|---|---|---|---|
| `NumFound` | `int` | `1700009A` |  |  |  | Returns the number of results returned for a pattern. |
| `UniqueID` | `int` | `1700009B` |  |  |  | Returns the unique id of the pattern. |
| `Name` | `string` | `1700009C` |  |  |  | Returns the name of the pattern. |

| 方法签名 | 官方语义 |
|---|---|
| `void .ctor(Cognex.VisionPro.PMAlign.CogPMAlignMultiResultItem)` |  |
| `void .ctor()` |  |
| `sealed void Dispose()` |  |

### `Cognex.VisionPro.PMAlign.CogPMAlignMultiRunParams`

- 种类 `class`，可见性 `public`，Token `02000126`，标志 `serializable, beforefieldinit`
- 基类 `Cognex.VisionPro.Implementation.CogSerializableChangedEventBase`，接口 `System.IDisposable`
- 官方说明：The CogPMAlignMultiRunParams class. This class holds a CogPMAlignRunParams in addition to the mutli-model run parameters.

| 属性 | 类型 | Token | [DefaultValue] | IL 默认 | 原生接口 | 官方语义 |
|---|---|---|---|---|---|---|
| `PMAlignRunParams` | `Cognex.VisionPro.PMAlign.CogPMAlignRunParams` | `17000095` |  |  |  | Gets or sets the PMAlignRunParams. |
| `RuntimeMode` | `Cognex.VisionPro.PMAlign.CogPMAlignMultiRuntimeModeConstants` | `17000096` |  |  |  | Gets or sets the runtime mode. |
| `StopSequentialThreshold` | `double` | `17000097` |  |  |  | Gets or sets the StopSequentialThreshold. This property is only used in Sequential search as one of the stopping condition criteria. Sequential searches will stop when the total number of found instances (with a score at or above this threshold) from a single pattern meets or exceeds the ApproximateNumberToFind. It does not alter the PatMax search algorithm. The value must be greater or equal to the AcceptThreshold otherwise a runtime error will be reported. |
| `ReportResultsFromOnePatternOnly` | `bool` | `17000098` |  |  |  | Gets or sets the ReportResultsFromOnePatternOnly Boolean.<para>For Sequential search <see cref="T:Cognex.VisionPro.PMAlign.CogPMAlignMultiRuntimeModeConstants">Sequential</see></para><para>For SequentialMostRecentlySuccessful search <see cref="T:Cognex.VisionPro.PMAlign.CogPMAlignMultiRuntimeModeConstants">SequentialMostRecentlySuccessful</see></para><para>For SequentialMostSuccessful search <see cref="T:Cognex.VisionPro.PMAlign.CogPMAlignMultiRuntimeModeConstants">SequentialMostSuccessful</see></para><para>For Exhaustive search <see cref="T:Cognex.VisionPro.PMAlign.CogPMAlignMultiRuntimeModeConstants">Exhaustive</see></para> |
| `UseXYOverlapBetweenPatterns` | `bool` | `17000099` |  |  |  | Gets or sets the UseXYOverlapBetweenPatterns Boolean. If true, the overlap value from the PMAlignRunParams is used to discard overlapping instances of different patterns. Otherwise, the overlap value is only used to discard overlapping instances of a single pattern. |

| 方法签名 | 官方语义 |
|---|---|
| `void .ctor(Cognex.VisionPro.PMAlign.CogPMAlignMultiRunParams)` |  |
| `void .ctor()` |  |
| `sealed void Dispose()` |  |

### `Cognex.VisionPro.PMAlign.CogPMAlignResultsShared`

- 种类 `class`，可见性 `public`，Token `0200011B`，标志 `serializable`
- 基类 `Cognex.VisionPro.Implementation.CogObjectBase`，接口 `Cognex.VisionPro.Implementation.Internal.ICogNetInternal`, `System.IDisposable`, `System.ICloneable`, `System.Runtime.Serialization.ISerializable`

| 方法签名 | 官方语义 |
|---|---|
| `void .ctor(Cognex.VisionPro.PMAlign.CogPMAlignResultsShared)` |  |
| `void .ctor()` |  |
| `void .ctor(nint)` |  |
| `sealed void Dispose()` |  |

## 4. 全部 public enum 与常量值（28 个，值来自 Constant 表）

| enum | 成员 = 值 |
|---|---|
| `CogPMAlignCurrentRecordConstants` | None=0， TrainImage=1， TrainRegion=2， TrainImageMask=4， PatternOrigin=8， PatternFeaturesCoarse=16， PatternFeaturesFine=32， TrainShapeModels=64， TrainedPatternImage=256， TrainedPatternRegion=512， TrainedPatternImageMask=1024， TrainedPatternOrigin=2048， TrainedPatternFeaturesCoarse=4096， TrainedPatternFeaturesFine=8192， TrainedPatternShapeModels=16384， InputImage=65536， SearchRegion=131072， SearchImageMask=262144， CompositeDataItemCoarseUsed=524288， CompositeDataItemCoarseUnused=1048576， CompositeDataItemFineUsed=2097152， CompositeDataItemFineUnused=4194304， CompositeDataItemAlignment=8388608， All=-1 |
| `CogPMAlignFlexDeformationFitConstants` | Perspective=0， SurfaceFlex=1 |
| `CogPMAlignFlexDeformationInfoConstants` | None=0， Transform=1， TransformAndUnwarpData=2 |
| `CogPMAlignFlexRefinementConstants` | None=0， Coarse=1， Medium=2， Fine=3 |
| `CogPMAlignLastRunRecordConstants` | None=0， ResultsOrigin=2， ResultsBoundingBox=8， ResultsMatchRegion=64， ResultsCoordinateAxes=128， ResultsMatchShapeModels=256， All=-1 |
| `CogPMAlignLastRunRecordDiagConstants` | None=0， InputImageByReference=1， InputImageDeepCopy=4， TrainImageDeepCopy=16， TrainRegion=32， PatternOrigin=64， PatternFeaturesCoarse=128， PatternFeaturesFine=256， TrainImageMask=512， TrainShapeModels=1024， TrainedPatternImage=4096， TrainedPatternRegion=8192， TrainedPatternOrigin=16384， TrainedPatternFeaturesCoarse=32768， TrainedPatternFeaturesFine=65536， TrainedPatternImageMask=131072， TrainedPatternShapeModels=262144， ResultsMatchFeatures=1048576， SearchRegion=2097152， SearchImageMask=4194304， FlexDeformationGrid=8388608， FlexUnwarpedImages=16777216， All=-1 |
| `CogPMAlignMultiCurrentRecordConstants` | None=0， InputImage=1， SearchRegion=2， SearchImageMask=4， PatternTrainImage=8， PatternTrainRegion=16， PatternTrainImageMask=32， PatternOrigin=64， PatternFeaturesCoarse=128， PatternFeaturesFine=256， PatternTrainShapeModels=512， All=-1 |
| `CogPMAlignMultiLastRunRecordConstants` | None=0， TipText=1， ResultsOrigin=2， ResultsMatchRegion=4， ResultsCoordinateAxes=8， ResultsMatchShapeModels=16， All=-1 |
| `CogPMAlignMultiLastRunRecordDiagConstants` | None=0， InputImageByReference=1， InputImageDeepCopy=2， ResultsMatchFeatures=4， SearchRegion=8， SearchImageMask=16， All=-1 |
| `CogPMAlignMultiRuntimeModeConstants` | Sequential=0， SequentialMostRecentlySuccessful=1， SequentialMostSuccessful=2， Exhaustive=3 |
| `CogPMAlignResultGraphicConstants` | None=0， TipText=1， Origin=2， BoundingBox=4， MatchFeatures=16， MatchRegion=32， CoordinateAxes=64， MatchShapeModels=128， FlexDeformationGrid=256， All=-1 |
| `CogPMAlignRunAlgorithmConstants` | PatMax=0， PatQuick=1， BestTrained=2， PatFlex=3， PatMaxPerspective=4 |
| `CogPMAlignRunDiagnosticConstants` | ContrastThreshold=20300， Proximity=20301， ExcessClutter=20302， Degenerate=20303 |
| `CogPMAlignRunModeConstants` | SearchImage=0， RefineStartPose=1 |
| `CogPMAlignTrainAlgorithmConstants` | PatMax=0， PatQuick=1， PatMaxAndPatQuick=2， PatFlex=3， PatMaxHighSensitivity=4， PatMaxPerspective=5 |
| `CogPMAlignTrainDiagnosticConstants` | TooFewFeatures=10000， Granularity=10001， Blurry=10100， ConfusingDOF=10101， Degenerate=10102， FineFeatures=10200 |
| `CogPMAlignTrainModeConstants` | Image=0， ShapeModelsWithImage=1， ShapeModelsWithTransform=2 |
| `CogPMAlignVisiblePixelMaskScoringMethodConstants` | DoNotScoreFeatures=0， ScoreFeaturesAsClutter=1 |
| `CogPMAlignZoneConstants` | Nominal=0， LowHigh=1 |
| `Ordering` |  |
| `GrainLimitPolicy` |  |
| `RuntimeMode` |  |
| `Algorithm` |  |
| `DOF` |  |
| `DeformationFit` |  |
| `Refinement` |  |
| `SaveMatchInfoMode` |  |
| `SensitivityMode` |  |

## 5. 其余 public 类型索引（只列名，成员见 raw/pmalign-meta.json）

`CogPMAlignBadWeightsException`、`CogPMAlignCanNotTrainException`、`CogPMAlignImageTypeNotSupported`、`CogPMAlignImportFromCVLErrorException`、`CogPMAlignInvalidAlgorithmForPatFlexXFormException`、`CogPMAlignInvalidCompositeTrainAlgorithm`、`CogPMAlignInvalidCompositeTrainMode`、`CogPMAlignInvalidOutsideRegionThresholdException`、`CogPMAlignInvalidPCPException`、`CogPMAlignModelTooLargeException`、`CogPMAlignNoShapeModelsException`、`CogPMAlignPatFlexNotTrainedException`、`CogPMAlignPatMaxNotTrainedException`、`CogPMAlignPatMaxPerspectiveNotTrainedException`、`CogPMAlignPatQuickNotTrainedException`、`CogPMAlignPatternIgnorePolarityMustBeTrueException`、`CogPMAlignSaveDeformationInfoErrorException`、`CogPMAlignSaveMatchInfoErrorException`、`CogPMAlignUndefinedPolarityException`、`CogPMAlignWrongTrainModeException`、`InvalidatePMAlignCurrentRecord_Delegate`、`CogPMAlignCompositeDataItem`、`CogPMAlignCompositeDataItemCollection`、`CogPMAlignMulti`、`CogPMAlignMultiChangedEventArgs`、`CogPMAlignMultiChangedEventHandler`、`CogPMAlignMultiResultItemCollection`、`CogPMAlignMultiTool`、`CogPMAlignPatternChangingEventArgs`、`CogPMAlignPatternChangingEventHandler`、`CogPMAlignPatternItem`、`ICogPMAlignFlexRunParams`、`CogCancellationToken`、`CogPMAlignFlexRunParams`、`BadType`、`Errors`、`UnknownVersion`、`ccEllipseGeom`、`ccEllipseUnit`、`BadImage`、`BadParams`、`CanNotTrain`、`Errors`、`InternalError`、`NotImplemented`、`NotTrained`、`UnsupportedHW`、`_Bxty`、`_Bxty`

### 已剔除的编译器生成类型

共 96 个，全部为编译器占位类型（serialization TypeDescriptor 的 extraBytes 桩、
`<PrivateImplementationDetails>`、`__StaticArrayInitTypeSize=*`、含 `<>` 的闭包/迭代器桩），
没有任何 public 成员或 API 价值，已从本文件剔除；若需查全，见 `raw/pmalign-meta.json`。

其中 65 个是序列化 `$_TypeDescriptor$_extraBytes_N` 桩，其余为静态数组初始化桩与闭包桩。

