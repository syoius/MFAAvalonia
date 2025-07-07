<!-- markdownlint-disable MD033 MD041 -->
<p align="center">
  <img alt="LOGO" src="https://github.com/SweetSmellFox/MFAAvalonia/blob/master/MFAAvalonia/MFAAvalonia.ico" width="256" height="256" />
</p>

<div align="center">

# MFAAvalonia

<!-- prettier-ignore-start -->
<!-- markdownlint-disable-next-line MD036 -->
_✨ 基于 **[Avalonia](https://github.com/AvaloniaUI/Avalonia)** 的 **[MAAFramework](https://github.com/MaaXYZ/MaaFramework)** 通用 GUI 项目 ✨_
<!-- prettier-ignore-end -->

  <img alt="license" src="https://img.shields.io/github/license/SweetSmellFox/MFAAvalonia">
  <img alt=".NET" src="https://img.shields.io/badge/.NET-≥%208-512BD4?logo=csharp">
  <img alt="platform" src="https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20%7C%20macOS-blueviolet">
  <img alt="commit" src="https://img.shields.io/github/commit-activity/m/SweetSmellFox/MFAAvalonia">
  <img alt="stars" src="https://img.shields.io/github/stars/SweetSmellFox/MFAAvalonia?style=social">
  <a href="https://mirrorchyan.com/zh/projects?rid=MFAAvalonia&source=mfaagh-badge" target="_blank"><img alt="mirrorc" src="https://img.shields.io/badge/Mirror%E9%85%B1-%239af3f6?logo=countingworkspro&logoColor=4f46e5"></a>
</div>
<div align="center">

[English](./README_en.md) | [简体中文](./README.md)

</div>

## 预览图

<p align="center">
  <img alt="preview" src="https://github.com/SweetSmellFox/MFAAvalonia/blob/master/MFAAvalonia/Img/preview.png" height="595" width="900" />
</p>

## 使用需求

- .NET 8.0
- 一个基于`MaaFramework`的资源项目

## 说明

### 如何使用

#### 自动安装

- 下载项目中workflows/install.yml并修改```项目名称```,```作者名```,```项目名```,```MAAxxx```
- 将修改后的install.yml替换MAA项目模板.github/workflows/install.yml
- 推送新版本

#### 手动安装

- 下载最新发行版并解压
- 将maafw项目中assets/resource中所有内容复制到MFAAvalonia/resource中
- 将maafw项目中assets/interface.json文件复制到MFAAvalonia/中
- ***修改***刚刚复制的interface.json文件
- 下面是一个例子

 ```
{
  "resource": [
    {
      "name": "官服",
      "path": "{PROJECT_DIR}/resource/base"
    },
    {
      "name": "Bilibili服",
      "path": [
        "{PROJECT_DIR}/resource/base",
        "{PROJECT_DIR}/resource/bilibili"
      ]
    }
  ],
  "task": [
    {
      "name": "任务",
      "entry": "任务"
    }
  ]
}
```

修改为

```
{
  "name": "项目名称", //默认为null
  "version":  "项目版本", //默认为null
  "mirrorchyan_rid":  "项目ID(从Mirror酱下载的必要字段)", //默认为null , 比如 M9A
  "mirrorchyan_multiplatform":  false, //项目多平台字段(从Mirror酱下载的字段)默认为false
  "url":  "项目链接(目前应该只支持Github)", //默认为null , 比如 https://github.com/{Github账户}/{Github项目}
  "custom_title": "自定义标题", //默认为null, 使用该字段后，标题栏将只显示custom_title和version
  "resource": [
    {
      "name": "官服",
      "path": "{PROJECT_DIR}/resource/base"
    },
    {
      "name": "Bilibili服",
      "path": [
        "{PROJECT_DIR}/resource/base",
        "{PROJECT_DIR}/resource/bilibili"
      ]
    }
  ],
  "task": [
    {
      "name": "任务",
      "entry": "任务接口",
      "check": true,  //默认为false，任务默认是否被选中
      "doc": "文档介绍",  //默认为null，显示在任务设置选项底下，可支持富文本，格式在下方
      "repeatable": true,  //默认为false，任务可不可以重复运行
      "repeat_count": 1,  //任务默认重复运行次数，需要repeatable为true
    }
  ]
}
```

可以通过controller的数量来锁定控制，可以通过controller[0]来控制默认控制器

### `doc`字符串格式：

#### 使用类似`[color:red]`文本内容`[/color]`的标记来定义文本样式。

#### 支持的标记包括：

- `[color:color_name]`：颜色，例如`[color:red]`。

-  ~~`[size:font_size]`：字号，例如`[size:20]`。~~

- `[b]`：粗体。

- `[i]`：斜体。

- `[u]`：下划线。

- `[s]`：删除线。

- `[align:left/center/right]`：居左，居中或者居右，只能在一整行中使用。

### 实验性功能

- `MFAAvalonia` 为interface和interface的task新增了`advanced`字段。
- `advanced`字段的用法类似于option，其功能类似于支持通过UI输入框让用户自行编辑功能的`pipeline_override`。
- 下面是使用`advanced`的例子

``` json
{
  "task": [
        {    
            "name": "测试A",
            "entry": "任务A",
            "advanced": ["高级设置A","高级设置B"]
        }
  ],
  "advanced": {
    "高级设置A": {
      "field": "ce",
      "type": "string",
      "default": "cs.png",
      "pipeline_override": {
          "任务A": {
            "template": "{ce}"
          }
      }
    },
    "高级设置B": {
      "field": ["x","y"],
      "type": ["int", "int"],
      "default": ["1", "1"],
      "pipeline_override": {
          "任务A": {
            "roi": ["{x}","{y}",3,4]
          }
      }
    }
  }
}
```
**注：field和type和default支持string | list<string>类型，default默认为空。**

**注：上面json中注释内容为文档介绍用，实际运行时不能写入。**

- 运行

## 开发相关

- 欢迎各位大佬贡献代码
- `MFAAvalonia` 有interface多语言支持,在`interface.json`同目录下新建`lang`文件夹,里面内含`zh-cn.json`,`zh-tw.json`和`en-us.json`后，doc和任务的name和选项的name可以使用key来指代。MFAAvalonia会自动根据语言来读取文件的key对应的value。如果没有则默认为key
- `MFAAvalonia` 会读取`resource`文件夹(不区分大小写)的`Announcement.md`作为公告，更新资源时会自动下载一份Changelog作为公告
- `MFAAvalonia` 可以通过启动参数`-c 配置名称`来指定以特定配置文件启动，无须后缀名`.json`

**注：在MFA的v1.1.6版本中，移除了focus系列字段，改为any focus，原先的不再可用！**

- `focus` : *string* | *object*  
格式为
  ```
  "focus": {
    "start": "任务开始",  注：*string* | *string[]*    
    "succeeded": "任务成功",  注：*string* | *string[]* 
    "failed": "任务失败", 注：*string* | *string[]* 
    "toast": "弹窗提醒" 注：*string* 
  }
  ```
  ```
   "focus": "测试"
  ```
  等同于
  ```
  "focus": {
    "start": "测试"
  }
    ```
除了`toast`，其他的均支持使用类似`[color:red]`文本内容`[/color]`的标记来定义文本颜色。

## 许可证

**MFAAvalonia** 使用 **[GPL-3.0 许可证](./LICENSE)** 开源。

## 致谢

### 开源项目

- **[SukiUI](https://github.com/kikipoulet/SukiUI)**\
  A Desktop UI Library for Avalonia.
- **[MaaFramework](https://github.com/MaaAssistantArknights/MaaFramework)**\
  基于图像识别的自动化黑盒测试框架。
- **[Serilog](https://github.com/serilog/serilog)**\
  C# 日志记录库
- **[Newtonsoft.Json](https://github.com/CommunityToolkit/dotnet)**\
  C# JSON 库
- **[MirrorChyan](https://github.com/MirrorChyan/docs)**\
  Mirror酱更新服务
- **[AvaloniaExtensions.Axaml](https://github.com/dotnet9/AvaloniaExtensions)**\
  为Avalonia UI开发带来便利的语法糖库
- **[CalcBindingAva](https://github.com/netwww1/CalcBindingAva)**\
  CalcBinding is an advanced Binding markup extension that allows you to write calculated binding expressions in xaml, without custom converter

### 开发者

感谢所有为 **MFAAvalonia** 做出贡献的开发者。

<a href="https://github.com/SweetSmellFox/MFAAvalonia/graphs/contributors">
  <img src="https://contrib.rocks/image?repo=SweetSmellFox/MFAAvalonia&max=1000" alt="Contributors to MFAAvalonia"/>
</a>