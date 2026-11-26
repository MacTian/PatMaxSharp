# `cogxpma.dll` 原生核心取证（字符串 / 导出表 / 数值常量）

本文只做静态取证：读文件、解 PE、抽字符串、扫立即数。**没有加载、没有执行、没有解密任何加壳内容**，
也没有修改原始二进制。所有结论都标注了「实测」或「推断」，并给出 RVA / 文件偏移 / hexdump。

路径与指纹：

| 项 | 值 |
|---|---|
| 原生核心 | `C:\Program Files\Cognex\VisionPro\bin\cogxpma.dll` |
| 大小 | 31,099,336 bytes |
| SHA-256 | `d2c077d7d808758ac6dfd5d6cb6d961a341e6f2826cedc718dc67bce8158fec0` |
| 托管程序集 | `Cognex.VisionPro.PMAlign.dll`，22,809,032 bytes，SHA-256 `14c6c3a8e1392a5fb0e0fdf7174b035402ba0d8fe7ba8ebca28347ba26fb1070` |

---

## 0. 结论摘要（先看这里）

1. **`cogxpma.dll` 的代码与数据全部被 WIBU 加密**：18 个节里有 17 个叫 `__wibu00…__wibu10`，熵 7.99-8.00；
   **不存在 `.text`、也不存在 `.rdata`**。因此"扫描 `.rdata` 成组 double 常量"这条路在该文件上**没有落脚点**（实测，见 §1）。
2. 磁盘上唯一明文的是 `.rsrc`（熵 5.43，936 条字符串）。它包含 **COM 注册脚本** 与**整套 IDL/类型库帮助文本**--
   后者就是 PMAlign 参数语义的原生出处（托管层的 `[CogDocSummary]` 属性就是从同一批文本生成的，见 §4、§5）。
3. 原生核心**没有以 `PatMax` 命名的 C 导出**，算法只能通过 COM 进入；导出表里 25 个 `C_ccADLinkInt_*` / `C_ccHardwareInt_*`
   是硬件/许可查询（实测，见 §2）。
4. 用户提示的 `2.0506096654409878` 与 `4.1012193308819755` **在 `cogxpma.dll` 里 0 命中**（实测，见 §6.2），
   但**在托管程序集的原生 `.text` 里逐字节找到了**：两者是一个 56 字节默认参数块的连续两个 `mov rax, imm64`
   （实测，见 §6.3）。`4.1012… = 2 × 2.0506…`（精确 2 倍），与仓库里 `DefaultSensitivityParameter = 2.0` 一致。
5. 早期文档声称的 `cogxpma.dll` "RVA 0xacc9b0"（评分函数）落在加密节 `__wibu00` 内，**无法作为明文代码地址复核**（实测，见 §7）。

---

## 1. PE 结构与加壳证据（实测）

PE32+ / x64（machine `0x8664`，magic `0x20B`），COFF 特征 `0x2022`（DLL + EXECUTABLE_IMAGE + LARGE_ADDRESS_AWARE），
`TimeDateStamp = 0x6AE4FDD2` → **2026-10-30 20:28:02 UTC**。

| 节 | VA | VirtualSize | RawPtr | RawSize | 熵（2 MB 采样） |
|---|---|---|---|---|---|
| `__wibu00` | 0x00001000 | 0x00B26000 | 0x00001000 | 0x00B26000 | **8.000** |
| `__wibu01` | 0x00B27000 | 0x00007E00 | 0x00B27000 | 0x00007E00 | 7.994 |
| `__wibu02` | 0x00B2F000 | 0x003FAFCA | 0x00B2EE00 | 0x003FB000 | **8.000** |
| `__wibu03` | 0x00F2A000 | 0x0016D770 | 0x00F29E00 | 0x0005AA00 | 7.999 |
| `__wibu04` | 0x01098000 | 0x0008C25C | 0x00F84800 | 0x0008C400 | **8.000** |
| `__wibu05` | 0x01125000 | 0x00000700 | 0x01010C00 | 0x00000800 | 7.430 |
| `__wibu06` | 0x01126000 | 0x00024C08 | 0x01011400 | 0x00024E00 | 7.997 |
| **`.rsrc`** | 0x0114B000 | 0x00019E1C | 0x01036200 | 0x0001A000 | **5.426** ← 唯一明文 |
| `__wibu07` | 0x01165000 | 0x0001D4CC | 0x01050200 | 0x0001D600 | 7.997 |
| `__wibu08` | 0x01183000 | 0x00972000 | 0x0106D800 | 0x00971E00 | 6.549 |
| `__wibu09` | 0x01AF5000 | 0x00105000 | 0x019DF600 | 0x00104200 | 6.370 |
| `__wibu0a` | 0x01BFA000 | 0x00059000 | 0x01AE3800 | 0x00058200 | 6.503 |
| `__wibu0b` | 0x01C53000 | 0x00002000 | 0x01B3BA00 | 0x00001A00 | 4.743 |
| `__wibu0c` | 0x01C55000 | 0x00150000 | 0x01B3D400 | 0x0014FC00 | 5.939 |
| `__wibu0d` | 0x01DA5000 | 0x00001000 | 0x01C8D000 | 0x00000200 | 0.020 |
| `__wibu0e` | 0x01DA6000 | 0x00001000 | 0x01C8D200 | 0x00000200 | 1.050 |
| `__wibu0f` | 0x01DA7000 | 0x00001000 | 0x01C8D400 | 0x00000200 | 0.000 |
| `__wibu10` | 0x01DA8000 | 0x00119000 | 0x01C8D600 | 0x00118C00 | 6.950 |

**实测结论**：节名与熵共同证明 WIBU CodeMeter 全节加密；`.rdata`/`.text` 这两个名字在该文件里根本不存在。
所以任何"从 `.rdata` 取 double 常量"的期望都必须改为"从托管程序集的混合模式原生代码取"（§6.3）。

RVA↔文件偏移（用于本文所有换算）：

| 区 | 换算 |
|---|---|
| `cogxpma.dll` `.rsrc` | `file = rva − 0x114E00` |
| `cogxpma.dll` `__wibu*` | `file = rva +（各节 RawPtr − VA）`，例如 `__wibu00`: `file = rva` |
| `Cognex.VisionPro.PMAlign.dll` `.text` | `rva = file + 0xA00`（`.text` rva 0x1000 ↔ file 0x600） |

---

## 2. 导出表：只有 COM 入口 + 硬件/许可查询（实测）

`IMAGE_DIRECTORY_ENTRY_EXPORT`，共 **29** 个名字：

| 序号 | RVA | 名称 | 解读 |
|---|---|---|---|
| 1-20 | 0x00361CC0…0x00361E10 | `C_ccADLinkInt_ClearLastHardwareError`、`GetBoardModel`、`GetBoardSeries`、`GetCustomerEEPROMSize`、`GetDeviceName`、`GetEEPROMName`、`GetEEPROMReadable`、`GetEEPROMSize`、`GetEEPROMWritable`、`GetIDByteCount`、`GetIDBytes`、`GetOSObjectName`、`GetPorts`、`GetSerialIsProgrammable`、`GetSerialNumber`、`GetVendorBoardName`、`GetVendorPartNumber`、`GetVendorSerialNumber`、`delete`、`new` | 板卡/加密狗信息查询（**推断**：WIBU 许可对象的 C 桥接） |
| 21-25 | 0x0035F600…0x0035F660 | `C_ccHardwareInt_FindBoard`、`GetInterface`、`GetInterfaceCount`、`delete`、`new` | 同上 |
| 26 | 0x001755A0 | `DllCanUnloadNow` | **COM 进程内服务器** |
| 27 | 0x001755B0 | `DllGetClassObject` | 类工厂入口 |
| 28 | 0x001756A0 | `DllRegisterServer` | 注册表注册 |
| 29 | 0x00175790 | `DllUnregisterServer` | 反注册 |

**实测结论**：没有任何 `PatMax*`/`PMAlign*` C 符号。原生核心是 COM in-proc server，
所有参数名只能在 `.rsrc` 的类型库文本里看到（§4/§5），或通过托管代理的属性名反推。

导入表里值得注意的（实测，来自 `IMAGE_DIRECTORY_ENTRY_IMPORT`）：

- `KERNEL32.dll`：`LoadLibraryExW` / `GetProcAddress` / `OpenFileMappingW` / `EncodePointer`（延迟绑定与指针编码）
- `ADVAPI32.dll`：`CryptAcquireContextW`、`CryptCreateHash`、`CryptDecrypt`、`CryptSignHashW`、`CryptGenRandom`、`CryptExportKey`
  （**推断**：许可校验/加密狗交互）
- `USER32.dll`：`LoadStringW` ← **这正是把 `.rsrc` 当字符串表读取的 API**，坐实 §5 的文本来源
- `VCRUNTIME140.dll` / `MSVCP140.dll` / 12 个 `api-ms-win-crt-*`：MSVC 2015+ 工具链
- `WINHTTP.dll`、`WINTRUST.dll`、`WS2_32.dll`、`IPHLPAPI.DLL`、`imagehlp.dll`、`PSAPI.DLL`：网络许可 / 签名校验 / 诊断
- `ntdll.dll`、`ole32.dll`、`OLEAUT32.dll`：COM 与 RTL

编译器指纹（实测）：`__wibu02` 节里残留明文路径 **`b:\vx\exe_x64\Release\cogxpma.pdb`**
（RVA `0x00DFD9E4`，同一节还有 `cogxpma.DLL` @ `0x00F23E24`）。说明：x64、Release、构建机 `b:\vx\exe_x64`。

---

## 3. 字符串抽取方法与总量（实测）

方法（`tools/cognex-meta` 的 `strings` 模式）：

1. 按节遍历原始字节；
2. ASCII 可打印（`0x20-0x7E` + `0x09`）连续长度 ≥ 6；
3. UTF-16LE（低字节可打印、高字节 0）连续长度 ≥ 6；
4. 每个结果记录 **RVA + 文件偏移 + 所属节 + 编码 + 长度 + 原文**。

结果：**70,115 条**（ASCII 69,887 / UTF-16LE 228）。按节分布：

| 节 | 条数 | 说明 |
|---|---|---|
| `__wibu00` | 35,811 | 密文里偶然出现的可打印串，无意义 |
| `__wibu02` | 7,886 | 同上（但含 PDB 路径等残留明文） |
| `__wibu08` | … | 等等 |
| **`.rsrc`** | **936** | **全部有价值的内容都在这里** |

关键词过滤（`patmax|pmalign|pattern|score|contrast|grain|angle|peak|clutter|coverage|feature|dipole|projection|gabor|sigma|threshold|…`）
命中 **799 条 / 771 条去重**，完整清单（含每条 ±16…+48 字节 hexdump）在
[`raw/native-strings-keywords.md`](raw/native-strings-keywords.md)；全量清单（已截断到前 12,000 条）在
[`raw/native-strings-all.txt`](raw/native-strings-all.txt)。

> 说明：`dipole`、`gabor`、`projection`、`sigma` 这几个词在 `.rsrc` 里**没有**命中--
> 说明它们属于内部实现名，不出现在面向用户的类型库文本里。所有相关代码都在加密节内，静态不可见（实测 + 由此推断）。

---

## 4. `.rsrc` 里的 COM 注册（实测，RVA + hexdump）

`InprocServer32 = s '%MODULE%'`，`ThreadingModel = s 'Both'`，TypeLib `{BCFDA6A3-969D-11D2-8A1E-006097F7CFE2}`，
AppID `{F47E8420-832B-11D2-AD1C-006097FE57F1}`。

| ProgID | CLSID | RVA（ProgID 串） |
|---|---|---|
| `Cognex.CogPMAlignRunParams(.1)` | `{BCFDA6B4-969D-11D2-8A1E-006097F7CFE2}` | 0x0114B2F5 / 0x0114B338 |
| `Cognex.CogPMAlignResult(.1)` | `{BCFDA6B6-969D-11D2-8A1E-006097F7CFE2}` | 0x0114B5F5 |
| `Cognex.CogPMAlignResults(.1)` | `{BCFDA6B8-969D-11D2-8A1E-006097F7CFE2}` | 0x0114B8DD |
| `Cognex.CogPMAlignPattern(.1)` | `{7FCF30BD-3940-11D3-ABFD-00A0247AA525}` | 0x0114BBCD |
| `Cognex.CogPMAlignTool(.1)` | `{4ABDB856-396D-11D3-AD50-006097FE57F1}` | 0x0114BEBD |
| `Cognex.CogPMAlignZoneAngle(.1)` | `{A4A0F787-46CC-11D3-ABFF-00A0247AA525}` | 0x0114C295 |
| `Cognex.CogPMAlignZoneScale(.1)` | `{3236C9FE-8595-48D0-8B79-B7988F890642}` | 0x0114C595 |
| `Cognex.CogPMAlignResultsShared(.1)` | `{5379DFE8-4389-4DC6-ADC9-C1474E7E26AB}` | 0x0114C895 |

hexdump（`Cognex.CogPMAlignRunParams.1` @ RVA `0x0114B2F5`）：

```text
rva 0x0114B2E0  ... （前一条 "TYPELIB" 引用的尾部）
rva 0x0114B2F5  09 43 6F 67 6E 65 78 2E  43 6F 67 50 4D 41 6C 69 |.Cognex.CogPMAlig|
rva 0x0114B305  67 6E 52 75 6E 50 61 72  61 6D 73 2E 31 20 3D 20 |gnRunParams.1 = |
rva 0x0114B315  73 20 27 43 6F 67 50 4D 41  6C 69 67 6E 52 75 6E 50 |s 'CogPMAlignRunP|
rva 0x0114B325  61 72 61 6D 73 20 43 6C 61  73 73 27 0D 0A        |arams Class'..|
```

**实测结论**：托管层 `CogPMAlign*` 类与原生 COM 类**一一对应**（同一 ProgID 家族），
所以托管属性的 `CogDoc*` 里写的 `ICogPMAlignRunParams::AcceptThreshold` 就是原生成员的准名。

---

## 5. `.rsrc` 里的参数字符串（原生语义的真正出处，实测）

这些是 `LoadStringW` 读的类型库/帮助文本。**它们与托管层 `[CogDocSummary]` 的原文一致**，
即：托管文档不是另写的，就是从这批字串生成/同步的。

### 5.1 `ContrastThreshold`（RVA `0x0115430E`）

```text
rva 0x0115430E  4D 69 6E 69 6D 75 6D 20  61 63 63 65 70 74 61 62 |Minimum acceptab|
rva 0x0115431E  6C 65 20 63 6F 6E 74 72  61 73 74 20 66 6F 72 20 |le contrast for |
rva 0x0115432E  61 20 70 61 74 74 65 72  6E 20 69 6E 73 74 61 6E |a pattern instan|
rva 0x0115433E  63 65 2E 20 20 4F 6E 6C  79 20 70 61 74 74 65 72 |ce.  Only patter|
rva 0x0115434E  6E 20 69 6E 73 74 61 6E  63 65 73 20 77 68 65 72 |n instances wher|
rva 0x0115435E  65 20 74 68 65 20 61 76  65 72 61 67 65 20 64 69 |e the average di|
```

> "Minimum acceptable contrast for a pattern instance. Only pattern instances where the average difference in pixel values across all feature boundaries …"

### 5.2 粒度双值 `GrainLimitFine/Coarse`（RVA `0x011576DE` / `0x011575DE`）

```text
rva 0x011576DE  46 69 6E 65 20 67 72 61  6E 75 6C 61 72 69 74 79 |Fine granularity|
rva 0x011576EE  20 6C 69 6D 69 74 2E 20  20 55 73 65 72 20 63 61 | limit.  User ca|
rva 0x011576FE  6E 20 6D 61 6E 75 61 6C  6C 79 20 73 65 74 20 74 |n manually set t|
rva 0x0115770E  68 65 20 67 72 61 69 6E  20 6C 69 6D 69 74 20 69 |he grain limit i|
rva 0x0115771E  66 20 47 72 61 69 6E 4C  69 6D 69 74 41 75 74 6F |f GrainLimitAuto|
rva 0x0115772E  53 65 6C 65 63 74 20 69  73 20 66 61 6C 73 65 2E |Select is false.|
```

> 全文（长度 259）含：**"Valid range is 1.0 to 25.5 inclusive. Coarse must be greater than or equal to fine…"**
> -- 即 `[1.0, 25.5]` 这条范围在**原生**文本里就写着，不只是托管属性的转述。

### 5.3 其他高价值 `.rsrc` 字符串清单（RVA / 长度 / 原文摘要）

| RVA | 长度 | 摘要 |
|---|---|---|
| `0x011571D2` | 255 | "Interface which provides access to all of the train time parameters and to the results of training a PMAlign pattern…" |
| `0x011575DE` | 254 | Coarse granularity limit… Valid range 1.0 to 25.5；coarse ≥ fine |
| `0x01154B1A` | 250 | Coarse grain limit at run-time（`GrainLimitsUsePattern=false` 时生效） |
| `0x01154C16` | 250 | Fine grain limit at run-time；设 fine > coarse 会联动抬高 coarse |
| `0x01153C56` | 250 | 角度 DOF 重叠判据 `abs(Angle#1 − Angle#2) <= Overlap` |
| `0x01153EDE` | 250 | 角度 zone 下限（仅 `cogPMAlignZoneLowHigh`） |
| `0x01153FDA` | 250 | 角度 zone 上限 |
| `0x011539EE` | 254 | 角度 zone 名义值（仅 `cogPMAlignZoneNominal`） |
| `0x011540D6` | 235 | 尺度 DOF 重叠判据（`larger/smaller <= Overlap`） |
| `0x0115620E` | 250 | **Coverage**：训练特征被找到的比例；PatQuick 恒 −1 |
| `0x011563BA` | 235 | **Clutter**：多余特征 ÷ 训练特征数，0…∞；PatQuick 恒 −1 |
| `0x01156572` | 222 | 单条结果的诊断文本集合 |
| `0x0115911A` | 250 | 整次运行的诊断文本 |
| `0x01159216` | 243 | 整次运行的诊断 **ID** 集合（对应枚举 20300…） |
| `0x0115A61A` | 235 | `SearchRegion` 语义（NULL = 整图；受 `SearchRegionMode` 影响） |
| `0x0115B8B6` | 254 | `CogPMAlignPattern` 类说明：主接口提供训练参数访问 |

---

## 6. 数值常量：逐项取证

### 6.1 `.rsrc` 的 double 扫描--全部是文本噪声（实测）

对 `.rsrc` 做「8 字节对齐、有限、短小数、且 8 个字节不全是可打印 ASCII」的 double 扫描，得到 89 个候选；
逐条看上下文后确认**全部是把 ASCII 文本误读成 double**。典型反例：

```text
0x0114BD18  956834.096078  bits=302D313144332D41   ← 这是文本 "30BD-3940-11D3-A…"
01036F10  33 30 42 44 2D 33 39 34  30 2D 31 31 44 33 2D 41  |30BD-3940-11D3-A|
01036F20  42 46 44 2D 30 30 41 30  32 34 37 41 41 35 32 35  |BFD-00A0247AA525|
01036F30  7D 20 3D 20 73 20 27 43  6F 67 50 4D 41 6C 69 67  |} = s 'CogPMAlig|
```

（我已给扫描器加了「8 字节全可打印即丢弃」的规则，剩下的 89 条仍是同类文本重叠，
比如 `0x0114BD18` 就是上面这个 CLSID 字符串的一段。）**结论：`.rsrc` 里没有算法数值常量。**

完整输出：[`raw/native-doubles.txt`](raw/native-doubles.txt)（89 条，含分组与 hexdump）。

### 6.2 参考值精确位型搜索（实测）

对整文件做 IEEE-754 8 字节位型精确匹配（`find` 模式）：

| 值 | 位型（小端字节序） | `cogxpma.dll` 命中 | 落点 |
|---|---|---|---|
| `2.0506096654409878` | `C1714F0AA6670040` | **0** | - |
| `4.1012193308819755` | `C1714F0AA6671040` | **0** | - |
| `0.3` | `333333333333D33F` | **0** | - |
| `π/4 = 0.7853981633974483` | `182D4454FB21E93F` | **0** | - |
| `2π = 6.283185307179586` | `182D4454FB211940` | **0** | - |
| `1.4` | `666666666666F63F` | **0** | - |
| `25.5` | `0000000000803940` | **0** | - |
| `65535` | `00000000E0FFEF40` | **0** | - |
| `4.0` | `0000000000001040` | 1 | RVA `0x01C5ACFA`（`__wibu0c`，加密节） |

**解读**：加密节里出现 1 次 `4.0` 的位型，按随机基线（每型期望 ≈ 31 MB / 2⁶⁴ ≈ 1.7×10⁻¹²）本不该出现，
说明 WIBU 容器并非均匀随机（例如含明文长度/对齐字段），但**该命中无法归属任何语义**，不作为证据。
其余值 0 命中**不能**推出"算法不用这些常量"--因为内容被加密，静态不可见。

### 6.3 转机：托管程序集里的混合模式原生代码（实测，**本次最有价值的发现**）

`Cognex.VisionPro.PMAlign.dll` 是 **C++/CLI 混合模式**程序集：它的 `.text`（RVA 0x1000，大小 0xF3C800）里
有大量明文 x64 机器码，`ccPMAlign*` 那批类就是原生代码的托管外壳。于是 tuned 数值常量在这里**是明文的**。

还原方法（`nconst` 模式）：扫 `.text` 里 `48 B8+r imm64`（`mov r64, imm64`）的 8 字节立即数，
按 IEEE-754 解成 double，保留"有限、非零、10 位小数内仍精确、8 字节不全可打印"的值；
再用 `.pdata`（`IMAGE_DIRECTORY_ENTRY_EXCEPTION`，54,708 条 `RUNTIME_FUNCTION`）把每条常量归属到函数。
结果：**800 条常量，落在 432 个有 unwind 记录的函数里**（[`raw/managed-native-constants.txt`](raw/managed-native-constants.txt)）。

#### 6.3.1 用户提示的两个值的归属

`2.0506096654409878` 在 `find` 下命中 RVA `0x00EAF2E5`（文件 `0x00EAE8E5`），
`4.1012193308819755` 命中 RVA `0x00EAF2F3`（文件 `0x00EAE8F3`）--相隔 14 字节，说明不是两条连续的 `ldc.r8`，
而是**一个结构体初始化函数里的两条 `mov rax, imm64`**。反汇编 + hexdump：

```text
file 0x00EAE8A0  rva 0x00EAF2A0  00 00 49 8B 5B 38 49 8B  73 40 49 8B 7B 48 49 8B |..I.[8I.s@I.{HI.|
file 0x00EAE8B0  rva 0x00EAF2B0  E3 41 5F 41 5E 41 5D 41  5C 5D C3 CC CC CC CC CC |.A_A^A]A\]......|
file 0x00EAE8C0  rva 0x00EAF2C0  48 B8 00 00 00 00 00 00  34 40 48 C7 41 28 00 00 |H.......4@H.A(..|
file 0x00EAE8D0  rva 0x00EAF2D0  00 00 48 89 01 48 B8 00  00 00 00 00 00 F0 3F 48 |..H..H........?H|
file 0x00EAE8E0  rva 0x00EAF2E0  89 41 08 48 B8 C1 71 4F  0A A6 67 00 40 48 89 41 |.A.H..qO..g.@H.A|
file 0x00EAE8F0  rva 0x00EAF2F0  10 48 B8 C1 71 4F 0A A6  67 10 40 48 89 41 18 33 |.H..qO..g.@H.A.3|
file 0x00EAE900  rva 0x00EAF300  C0 C7 41 20 07 00 00 00  C7 41 30 01 00 00 00 C3 |..A .....A0.....|
```

逐指令解码（前一条 `C3 CC CC CC CC CC` 是上一个函数的 `ret` + 对齐填充，所以本函数从 `0x00EAF2C0` 开始）：

| RVA | 字节 | 指令 | 写入 |
|---|---|---|---|
| `0x00EAF2C0` | `48 B8 00 00 00 00 00 00 34 40` | `mov rax, 0x4034000000000000` | = **20.0** |
| `0x00EAF2CA` | `48 C7 41 28 00 00 00 00` | `mov qword [rcx+28h], 0` | +0x28 = 0（空指针） |
| `0x00EAF2D2` | `48 89 01` | `mov [rcx], rax` | **+0x00 = 20.0** |
| `0x00EAF2D5` | `48 B8 .. .. .. F0 3F` | `mov rax, 0x3FF0000000000000` | = **1.0** |
| `0x00EAF2DF` | `48 89 41 08` | `mov [rcx+08h], rax` | **+0x08 = 1.0** |
| `0x00EAF2E3` | `48 B8 C1 71 4F 0A A6 67 00 40` | `mov rax, <2.0506096654409878>` | |
| `0x00EAF2ED` | `48 89 41 10` | `mov [rcx+10h], rax` | **+0x10 = 2.0506096654409878** |
| `0x00EAF2F1` | `48 B8 C1 71 4F 0A A6 67 10 40` | `mov rax, <4.1012193308819755>` | |
| `0x00EAF2FB` | `48 89 41 18` | `mov [rcx+18h], rax` | **+0x18 = 4.1012193308819755** |
| `0x00EAF2FF` | `33 C0` | `xor eax, eax` | 返回 0 |
| `0x00EAF301` | `C7 41 20 07 00 00 00` | `mov dword [rcx+20h], 7` | **+0x20 = 7（int32）** |
| `0x00EAF308` | `C7 41 30 01 00 00 00` | `mov dword [rcx+30h], 1` | **+0x30 = 1（int32）** |
| `0x00EAF30F` | `C3` | `ret` | |

于是这个默认参数块是（**实测**结构与数值）：

| 偏移 | 值 | 类型 |
|---|---|---|
| +0x00 | 20.0 | double |
| +0x08 | 1.0 | double |
| +0x10 | **2.0506096654409878** | double |
| +0x18 | **4.1012193308819755** | double |
| +0x20 | 7 | int32 |
| +0x28 | 0 | 指针 |
| +0x30 | 1 | int32 |

**推断（高置信）**：`+0x10` 是 σx、`+0x18` 是 σy，因为
`4.1012193308819755 = 2 × 2.0506096654409878` 是**精确**的 2 倍（IEEE 双精度乘 2 只改指数位，不引入误差），
而仓库里 `GaborEdgeDetector.DefaultSensitivityParameter = 2.0` 且 `SigmaY = SigmaX * SensitivityParameter`
--两者完全吻合。更强的旁证：`+0x20 = 7` 恰好等于仓库的半窗公式
`HalfSize = ceil(√2 × max(σx,σy) + 0.95) = ceil(1.41421356 × 4.10121933 + 0.95) = ceil(6.75) = 7`。

**仍属推断、不能当事实的部分**：`+0x00 = 20.0`（可能是核半径上界/大核裁剪阈值）、`+0x08 = 1.0`（可能是 σ 下限或权重）、
`+0x30 = 1`（可能是一个枚举/布尔）。这个函数是 **leaf**（无栈帧、无 `.pdata` unwind 记录，所以按 RVA 反查不到名字），
无法静态归属到具体托管成员--需要符号或动态跟踪才能定名。

#### 6.3.2 旁证：`ZoneScale` 的 1.2 / 1.4 成对出现

同一个函数（`.pdata` 记录 `0x00A95190..0x00A9742F`，该函数内共 42 条常量，是常量最密集的函数）：

| RVA | 值 | 写入 |
|---|---|---|
| `0x00A95884` | **1.2** | `[I+0x08]` |
| `0x00A95892` | **1.4** | `[I+0x10]` |

与托管元数据里 `CogPMAlignZoneScale.High = 1.2`、`Overlap = 1.4` 的默认值**完全一致**（`params-reference.md` §4）。
这交叉验证了两条独立取证路径（元数据 `[DefaultValue]` ↔ 原生机码立即数）读的是同一套默认值。

---

## 7. 与既有文档中 RVA 声称的核对（实测 + 结论）

仓库内 `Matching/Scorer.cs`、`Core/PatternModel.cs` 的注释引用了早期逆向结论，例如
"RVA `0xacc9b0`"（评分/卷积函数）、`pattern + 0x90`（总权重 W）、`pattern + 0x8c`（指数 N）、`pattern + 0x18`（AcceptThreshold）。

| 声称 | 本次可验证性 |
|---|---|
| `cogxpma.dll` RVA `0xacc9b0` | **落在 `__wibu00` 内**（该节 VA 0x1000，VirtualSize 0xB26000 → 覆盖 0x1000…0xB27000）。该节熵 **8.000**，是密文。所以这个 RVA 不可能是磁盘上的明文代码地址；若要复核必须先在内存里解密。**本机静态无法验证**。 |
| `pattern + 0x8c/0x90/0x18` 等结构偏移 | 同上，结构定义在加密节内。**静态无法验证**。 |
| 早期文档目录 `pmalign-analysis/*.md` | 按用户说明是 OneDrive 云占位文件，本机读不出，故未引用。 |

**结论**：本文不推翻这些声称，但它们目前**只有"曾被某人观察到"这一级证据**，与本文的
"RVA + 字节 + 逐指令解码"不在同一证据级别。建议在 README 里引用时明确区分。

---

## 8. 复现

```powershell
cd tools/cognex-meta

# 1) 原生字符串 + 导出/导入表（写出 raw/native-strings-all.txt / -keywords.md / -export-import.txt）
dotnet run -- .\strings "C:\Program Files\Cognex\VisionPro\bin\cogxpma.dll" ..\..\docs\evidence\raw --min 6

# 2) .rsrc 的 double 扫描（含分组 hexdump）
dotnet run -- .\doubles "C:\Program Files\Cognex\VisionPro\bin\cogxpma.dll" ..\..\docs\evidence\raw --section .rsrc

# 3) 参考值精确位型搜索（任意文件）
dotnet run -- .\find "C:\Program Files\Cognex\VisionPro\bin\cogxpma.dll" 2.0506096654409878 4.1012193308819755 0.3 4
dotnet run -- .\find "C:\Program Files\Cognex\VisionPro\ReferencedAssemblies\Cognex.VisionPro.PMAlign.dll" 2.0506096654409878 4.1012193308819755

# 4) 混合模式原生代码的 mov-imm64 double 常量还原（按 .pdata 归属函数）
dotnet run -- .\nconst "C:\Program Files\Cognex\VisionPro\ReferencedAssemblies\Cognex.VisionPro.PMAlign.dll" ..\..\docs\evidence\raw\managed-native-constants.txt

# 5) 某个 RVA 落在哪个托管方法体内（IL 定位）
dotnet run -- .\where "C:\Program Files\Cognex\VisionPro\ReferencedAssemblies\Cognex.VisionPro.PMAlign.dll" 0x00EAF2C0
```

（`nconst` 输出的 800 条常量、`.pdata` 函数归属、store 目标寄存器偏移，都在
[`raw/managed-native-constants.txt`](raw/managed-native-constants.txt)。）

---

## 9. 局限（明确写清楚，避免误引）

1. **`cogxpma.dll` 的算法实现、常量、结构布局，本机静态全部不可见**（WIBU 全节加密）。本文对它只给出
   字符串/导出/导入/节表层面的证据。任何以"我从 `cogxpma.dll` 的 `.rdata` 读到 0.30/4.0"为形式的说法都不成立。
2. `GaborEdgeDetector` 那个常量块虽然**值**是实测的，但**语义**（哪个偏移是什么）是推断；
   `20.0`、`1.0`、`7`、`1` 的用途需要动态验证才能定论。
3. 800 条原生常量的语义未逐条解读；`raw/managed-native-constants.txt` 是候选池，不是结论。
4. 未扫描 `cogxpmaENU.dll` / `cogxpmaCHS.dll`（15-18 KB 的 MUI 附属资源）：英文/中文文本本身已在
   `cogxpma.dll` 的 `.rsrc` 里，附属 DLL 只是本地化覆盖层，对常量取证无新增价值。
