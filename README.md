# Protobuf-MSBuild-For-Unity (UPM)

使用 UPM 包嵌套 MSBuild .NET 工程，在构建时通过 Grpc.Tools 自动从 `.proto` 生成 C#，并编译为 DLL，连同依赖（Google.Protobuf.dll）一起复制到包的 `Runtime/Plugins`，供 Unity 项目直接引用。

## 目录结构

```
Protobuf-MSBuild-For-Unity/
├─ package.json
├─ README.md
├─ Runtime/
│  ├─ Plugins/            ← 构建后会自动复制 DLL 到这里
│  └─ ProtobufMSBuildForUnity.Protobuf.Runtime.asmdef
├─ Samples~/
│  └─ SimpleUsage/        ← Unity 示例脚本
└─ Dotnet/
   └─ ProtobufMSBuild/
      ├─ ProtobufMSBuildForUnity.Protobuf.Messages.csproj
      └─ Protos/
         └─ player.proto
```

## 使用步骤

1) 将本文件夹作为 UPM 本地包导入 Unity：
- 打开 Unity → Window → Package Manager → 加号 → Add package from disk → 选择此目录下的 `package.json`。

2) 构建 .NET 工程：
- 系统需安装 .NET SDK（建议 6.0+/8.0）。
- 在包根目录下执行：
  ```powershell
  dotnet build .\Dotnet\ProtobufMSBuild\ProtobufMSBuildForUnity.Protobuf.Messages.csproj -c Release
  ```
- 构建成功后，`Runtime/Plugins` 会出现：
  - `ProtobufMSBuildForUnity.Protobuf.Messages.dll`（你生成的消息库）
  - `Google.Protobuf.dll`（运行时依赖）

3) 在 Unity 中使用：
- 可在 Samples 中导入 `SimpleUsage` 示例，或在自己的脚本中直接：
  ```csharp
  using ProtobufMSBuildForUnity.Protobuf.Messages;
  using Google.Protobuf;
  ```
  即可访问 `player.proto` 生成的 `Player` 类型。

## 开发流程（更新 .proto）

1. 在 `Dotnet/ProtobufMSBuild/Protos` 中添加或修改你的 `.proto` 文件。
2. 确保每个 proto 设置 `option csharp_namespace = "ProtobufMSBuildForUnity.Protobuf.Messages";`（或你自定义的命名空间）。
3. 运行构建命令（见上文），生成并复制新的 DLL 到 `Runtime/Plugins`。
4. 切回 Unity，等待脚本编译完成即可使用新消息类型。

## 说明

- 目标框架为 `netstandard2.0`，兼容 Unity 2019+（.NET 4.x Equivalent）。
- 使用 `Grpc.Tools` 作为 `protoc` 生成器，仅生成消息类型（`GrpcServices="None"`）。
- 通过 `CopyLocalLockFileAssemblies=true` 复制运行时依赖（如 Google.Protobuf）。

## 常见问题

- 若 Unity 报找不到类型：
  - 确认你已构建 .NET 项目且 DLL 已复制到 `Runtime/Plugins`。
  - 确认 Unity 版本支持 .NET Standard 2.0（Unity 2019+）。
- 若想更改命名空间或程序集名称：
  - 修改 `ProtobufMSBuildForUnity.Protobuf.Messages.csproj` 中 `AssemblyName`/`RootNamespace`。
  - 在 proto 文件里同步修改 `option csharp_namespace`。

---
作者：WJQ
欢迎按需调整结构与命名！